// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Logging;
using osu.Game.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace osu.Desktop.Media
{
    /// <summary>
    /// Tracks the media which is currently playing on Windows, using the System Media Transport Controls (SMTC) API.
    /// </summary>
    public class WindowsSystemMediaSource : ISystemMediaSource
    {
        private const long max_cover_bytes = 32 * 1024 * 1024;

        public bool Available => manager != null;

        public IHasAmplitudes? AudioAmplitudes => systemAudioAmplitudes;

        public event Action<SystemMediaTrack?>? TrackChanged;

        public event Action<bool>? PlayingChanged;

        private readonly WasapiLoopbackAmplitudes systemAudioAmplitudes = new WasapiLoopbackAmplitudes();

        private GlobalSystemMediaTransportControlsSessionManager? manager;
        private GlobalSystemMediaTransportControlsSession? session;

        private readonly object sessionLock = new object();

        public void Start()
        {
            if (manager != null)
                return;

            Task.Run(initialise);
        }

        private async Task initialise()
        {
            try
            {
                systemAudioAmplitudes.Start();
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to start system audio capture: {e.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }

            try
            {
                GlobalSystemMediaTransportControlsSessionManager newManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ConfigureAwait(false);

                newManager.CurrentSessionChanged += onCurrentSessionChanged;

                manager = newManager;

                GlobalSystemMediaTransportControlsSession? preferredSession = getPreferredSession(newManager);
                bindSession(preferredSession);

                Logger.Log($"System media metadata (SMTC) initialised; current session: {preferredSession?.SourceAppUserModelId ?? "none"}", LoggingTarget.Runtime, LogLevel.Important);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to initialise system media capture: {e.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        private void onCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            // session enumeration is deferred off the event callback thread, as querying sessions from within
            // the event can be re-entrant into the platform API.
            _ = Task.Run(() => bindSession(getPreferredSession(sender)));
        }

        /// <summary>
        /// Picks the session which should be treated as the currently playing system media.
        /// osu! itself publishes a media session through the bundled LLin plugin's media integration,
        /// so sessions belonging to this process are ignored in favour of other applications' sessions.
        /// </summary>
        private static GlobalSystemMediaTransportControlsSession? getPreferredSession(GlobalSystemMediaTransportControlsSessionManager sessionManager)
        {
            try
            {
                GlobalSystemMediaTransportControlsSession? fallback = null;

                foreach (GlobalSystemMediaTransportControlsSession candidate in sessionManager.GetSessions())
                {
                    if (isOwnSession(candidate))
                        continue;

                    fallback ??= candidate;

                    try
                    {
                        if (candidate.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            return candidate;
                    }
                    catch
                    {
                        // the session can no longer be queried; move on to the next one.
                    }
                }

                return fallback;
            }
            catch
            {
                return sessionManager.GetCurrentSession();
            }
        }

        private static bool isOwnSession(GlobalSystemMediaTransportControlsSession candidate)
        {
            string ownExecutable = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);

            if (string.IsNullOrEmpty(ownExecutable))
                return false;

            return candidate.SourceAppUserModelId?.Contains(ownExecutable, StringComparison.OrdinalIgnoreCase) == true;
        }

        private void bindSession(GlobalSystemMediaTransportControlsSession? newSession)
        {
            lock (sessionLock)
            {
                if (session != null)
                {
                    session.MediaPropertiesChanged -= onMediaPropertiesChanged;
                    session.PlaybackInfoChanged -= onPlaybackInfoChanged;
                }

                session = newSession;

                if (session != null)
                {
                    session.MediaPropertiesChanged += onMediaPropertiesChanged;
                    session.PlaybackInfoChanged += onPlaybackInfoChanged;
                }
            }

            _ = refreshTrackAsync();
            refreshPlayback();
        }

        private void onMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
            => _ = refreshTrackAsync();

        private void onPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
            => refreshPlayback();

        private async Task refreshTrackAsync()
        {
            GlobalSystemMediaTransportControlsSession? currentSession;
            lock (sessionLock)
                currentSession = session;

            if (currentSession == null)
            {
                TrackChanged?.Invoke(null);
                return;
            }

            try
            {
                GlobalSystemMediaTransportControlsSessionMediaProperties properties = await currentSession.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
                byte[]? coverData = await readCoverAsync(properties).ConfigureAwait(false);
                TrackChanged?.Invoke(new SystemMediaTrack(properties.Title ?? string.Empty, properties.Artist ?? string.Empty, coverData));
            }
            catch
            {
                TrackChanged?.Invoke(null);
            }
        }

        private async Task<byte[]?> readCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
        {
            try
            {
                if (properties.Thumbnail == null)
                    return null;

                using var stream = await properties.Thumbnail.OpenReadAsync().AsTask().ConfigureAwait(false);

                if (stream.Size == 0 || stream.Size > max_cover_bytes)
                    return null;

                using var reader = new DataReader(stream);

                if (await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false) == 0)
                    return null;

                byte[] bytes = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(bytes);
                return bytes;
            }
            catch
            {
                // the cover art request failed; the game will fall back to its built-in background.
                return null;
            }
        }

        private void refreshPlayback()
        {
            GlobalSystemMediaTransportControlsSession? currentSession;
            lock (sessionLock)
                currentSession = session;

            if (currentSession == null)
                return;

            try
            {
                GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo = currentSession.GetPlaybackInfo();
                PlayingChanged?.Invoke(playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            }
            catch
            {
                // ignore transient failures.
            }
        }

        public void Next() => tryControl(async s => await s.TrySkipNextAsync().AsTask().ConfigureAwait(false));

        public void Previous() => tryControl(async s => await s.TrySkipPreviousAsync().AsTask().ConfigureAwait(false));

        public void TogglePlay() => tryControl(async s => await s.TryTogglePlayPauseAsync().AsTask().ConfigureAwait(false));

        private void tryControl(Func<GlobalSystemMediaTransportControlsSession, Task> action)
        {
            GlobalSystemMediaTransportControlsSession? currentSession;
            lock (sessionLock)
                currentSession = session;

            if (currentSession == null)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await action(currentSession).ConfigureAwait(false);
                }
                catch
                {
                    // the system player can reject commands; nothing to do about it.
                }
            });
        }
    }
}
