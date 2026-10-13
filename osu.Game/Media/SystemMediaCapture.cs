// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;

namespace osu.Game.Media
{
    /// <summary>
    /// Component which tracks the media currently playing on the operating system (when the platform supports it)
    /// and exposes its metadata and cover art for consumption by the game.
    /// </summary>
    public partial class SystemMediaCapture : CompositeDrawable
    {
        private const string cover_resource_name = @"cover";

        [Resolved(canBeNull: true)]
        private ISystemMediaSource? source { get; set; }

        [Resolved]
        private GameHost host { get; set; } = null!;

        private readonly BindableBool captureEnabled = new BindableBool();

        /// <summary>
        /// Whether the user has enabled capture of system media.
        /// </summary>
        public IBindable<bool> CaptureEnabled => captureEnabled;

        /// <summary>
        /// The track which is currently playing on the operating system, or <c>null</c> if nothing is playing
        /// (or the platform does not support tracking).
        /// </summary>
        public readonly Bindable<SystemMediaTrack?> CurrentTrack = new Bindable<SystemMediaTrack?>();

        /// <summary>Whether the system media is currently playing.</summary>
        public readonly BindableBool IsPlaying = new BindableBool();

        /// <summary>
        /// The cover art of the current system track, decoded and ready for display.
        /// May be <c>null</c> if no track is playing or the cover art could not be retrieved.
        /// </summary>
        public readonly Bindable<Texture?> CoverTexture = new Bindable<Texture?>();

        /// <summary>
        /// Whether system media capture is enabled and functional in this session.
        /// </summary>
        public bool Active => captureEnabled.Value && source?.Available == true;

        /// <summary>
        /// Real-time amplitude data of the system audio output, if the platform backend supports capturing it.
        /// </summary>
        public IHasAmplitudes? SystemAudioAmplitudes => source?.AudioAmplitudes;

        /// <summary>
        /// Whether the captured system audio should provide amplitudes to the game's beat sync.
        /// This intentionally does not require metadata availability, as the audio capture works independently
        /// from (and may be ready before) the system media metadata session.
        /// </summary>
        public bool AmplitudesActive => captureEnabled.Value && SystemAudioAmplitudes != null;

        private SystemMediaCoverStore coverStore = null!;
        private TextureStore? coverTextureStore;
        private SystemMediaTrack? lastTrack;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.CaptureSystemMedia, captureEnabled);
            coverStore = new SystemMediaCoverStore();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (source == null)
            {
                Logger.Log("System media capture is unavailable on this platform.", LoggingTarget.Runtime, LogLevel.Important);
                return;
            }

            source.TrackChanged += onTrackChanged;
            source.PlayingChanged += onPlayingChanged;
            source.Start();

            Logger.Log($"System media capture initialised: enabled={captureEnabled.Value} available={source.Available} audioAmplitudes={source.AudioAmplitudes != null}", LoggingTarget.Runtime, LogLevel.Important);

            Scheduler.AddDelayed(logDiagnostics, 5000, true);
        }

        private void logDiagnostics()
        {
            if (!captureEnabled.Value)
                return;

            ChannelAmplitudes amplitudes = SystemAudioAmplitudes?.CurrentAmplitudes ?? ChannelAmplitudes.Empty;

            float maxBin = 0;

            foreach (float bin in amplitudes.FrequencyAmplitudes.Span)
            {
                if (bin > maxBin)
                    maxBin = bin;
            }

            Logger.Log($"System media capture state: available={source?.Available == true} active={Active} peak={amplitudes.Maximum:0.0000} maxBin={maxBin:0.0000} track=\"{CurrentTrack.Value?.Title ?? string.Empty}\" cover={CoverTexture.Value != null}", LoggingTarget.Runtime, LogLevel.Verbose);
        }

        private void onTrackChanged(SystemMediaTrack? track) => Schedule(() => updateTrack(track));

        private void onPlayingChanged(bool playing) => Schedule(() => IsPlaying.Value = playing);

        private void updateTrack(SystemMediaTrack? track)
        {
            if (track != null && lastTrack != null && track.Equals(lastTrack) && CoverTexture.Value != null)
                return;

            CurrentTrack.Value = track;
            lastTrack = track;

            if (track?.CoverData == null)
            {
                // no cover art available (request failed or nothing playing); consumers will fall back to the built-in background.
                CoverTexture.Value = null;
                return;
            }

            try
            {
                coverStore.SetCoverData(track.CoverData);

                var previousStore = coverTextureStore;
                if (previousStore != null)
                    Scheduler.AddDelayed(previousStore.Dispose, 3000);

                coverTextureStore = new TextureStore(host.Renderer, host.CreateTextureLoaderStore(coverStore), false, scaleAdjust: 1);

                // decoding happens on this thread; covers are generally small enough for this to be imperceptible.
                CoverTexture.Value = coverTextureStore.Get(cover_resource_name);
            }
            catch
            {
                // cover art could not be decoded; consumers will fall back to the built-in background.
                CoverTexture.Value = null;
            }
        }

        /// <summary>
        /// Skip to the next track in the system player.
        /// </summary>
        public void Next()
        {
            if (Active)
                source!.Next();
        }

        /// <summary>
        /// Skip to the previous track in the system player.
        /// </summary>
        public void Previous()
        {
            if (Active)
                source!.Previous();
        }

        /// <summary>
        /// Toggle play/pause in the system player.
        /// </summary>
        public void TogglePlay()
        {
            if (Active)
                source!.TogglePlay();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (source != null)
            {
                source.TrackChanged -= onTrackChanged;
                source.PlayingChanged -= onPlayingChanged;
            }

            coverTextureStore?.Dispose();
        }

        /// <summary>
        /// A volatile single-entry store which serves the latest cover art bytes to the texture loader.
        /// </summary>
        private class SystemMediaCoverStore : IResourceStore<byte[]>
        {
            private byte[]? coverData;

            public void SetCoverData(byte[] data) => coverData = data;

            public byte[] Get(string name) => coverData ?? null!;

            public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = new CancellationToken()) =>
                Task.FromResult(coverData ?? null!);

            public Stream? GetStream(string name) => coverData == null ? null : new MemoryStream(coverData, false);

            public IEnumerable<string> GetAvailableResources()
            {
                yield return cover_resource_name;
            }

            public void Dispose()
            {
            }
        }
    }
}
