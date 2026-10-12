// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Media
{
    /// <summary>
    /// Information about a track which is currently playing on the operating system.
    /// </summary>
    public class SystemMediaTrack : IEquatable<SystemMediaTrack>
    {
        public readonly string Title;

        public readonly string Artist;

        /// <summary>
        /// The raw (encoded) bytes of the track's cover art, if available.
        /// </summary>
        public readonly byte[]? CoverData;

        public SystemMediaTrack(string title, string artist, byte[]? coverData)
        {
            Title = title;
            Artist = artist;
            CoverData = coverData;
        }

        public bool Equals(SystemMediaTrack? other) =>
            other != null && Title == other.Title && Artist == other.Artist;
    }
}
