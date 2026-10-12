// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Audio.Track;

namespace osu.Game.Media
{
    /// <summary>
    /// A platform backend which tracks the media that is currently playing on the operating system.
    /// </summary>
    /// <remarks>
    /// Implementations are expected to raise events from background threads; consumers must marshal
    /// callbacks to the update thread themselves.
    /// </remarks>
    public interface ISystemMediaSource
    {
        /// <summary>
        /// Whether the platform backend is available and ready to be queried.
        /// </summary>
        bool Available { get; }

        /// <summary>
        /// Start tracking the system media, if not already started.
        /// </summary>
        void Start();

        /// <summary>
        /// Raised when the currently playing track changes. A <c>null</c> track indicates that nothing is playing.
        /// </summary>
        event Action<SystemMediaTrack?>? TrackChanged;

        /// <summary>
        /// Raised when the playback state of the system media changes.
        /// </summary>
        event Action<bool>? PlayingChanged;

        /// <summary>
        /// Real-time amplitude data of the system audio output, if the platform backend supports capturing it.
        /// </summary>
        IHasAmplitudes? AudioAmplitudes { get; }

        /// <summary>
        /// Skip to the next track in the system player.
        /// </summary>
        void Next();

        /// <summary>
        /// Skip to the previous track in the system player.
        /// </summary>
        void Previous();

        /// <summary>
        /// Toggle play/pause in the system player.
        /// </summary>
        void TogglePlay();
    }
}
