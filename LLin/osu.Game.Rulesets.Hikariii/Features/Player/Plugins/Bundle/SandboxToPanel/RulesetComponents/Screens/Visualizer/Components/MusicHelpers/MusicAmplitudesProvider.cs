#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Media;
using osu.Game.Rulesets.Hikariii.Features.Player.Misc;

namespace osu.Game.Rulesets.Hikariii.Features.Player.Plugins.Bundle.SandboxToPanel.RulesetComponents.Screens.Visualizer.Components.MusicHelpers
{
    public abstract partial class MusicAmplitudesProvider : CurrentBeatmapProvider
    {
        public readonly BindableBool IsKiai = new BindableBool();

        [Resolved(canBeNull: true)]
        private SystemMediaCapture systemMediaCapture { get; set; }

        private ITrack track;

        protected override void OnBeatmapChanged(ValueChangedEvent<WorkingBeatmap> beatmap)
        {
            base.OnBeatmapChanged(beatmap);
            track = beatmap.NewValue?.Track;
        }

        protected override void Update()
        {
            base.Update();

            // while system media capture is active the in-game track is silent, so the captured system audio drives the visuals instead.
            OnAmplitudesUpdate(MusicAmplitudeCompat.GetFrequencyAmplitudes(systemMediaCapture, track?.CurrentAmplitudes ?? ChannelAmplitudes.Empty));
            IsKiai.Value = Beatmap.Value?.Beatmap.ControlPointInfo.EffectPointAt(track?.CurrentTime ?? 0).KiaiMode ?? false;
        }

        protected abstract void OnAmplitudesUpdate(float[] amplitudes);
    }
}
