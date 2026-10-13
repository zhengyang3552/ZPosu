#nullable disable

using osu.Framework.Audio.Track;
using osu.Game.Media;

namespace osu.Game.Rulesets.Hikariii.Features.Player.Misc
{
    /// <summary>
    /// Compatibility helper which allows the music-reactive features of this ruleset to follow the
    /// system audio while system media capture is active (in which case the in-game track is silent).
    /// </summary>
    public static class MusicAmplitudeCompat
    {
        /// <summary>
        /// Retrieves the frequency amplitudes which should currently drive visuals.
        /// </summary>
        /// <param name="capture">The system media capture, if available.</param>
        /// <param name="fallback">The amplitudes to use when system media capture is inactive.</param>
        public static float[] GetFrequencyAmplitudes(SystemMediaCapture capture, ChannelAmplitudes fallback)
        {
            if (capture?.AmplitudesActive == true && capture.SystemAudioAmplitudes != null)
                return capture.SystemAudioAmplitudes.CurrentAmplitudes.FrequencyAmplitudes.ToArray();

            return fallback.FrequencyAmplitudes.ToArray();
        }
    }
}
