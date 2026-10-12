// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Graphics.Textures;

namespace osu.Game.Graphics.Backgrounds
{
    /// <summary>
    /// A background which displays the cover art of the media currently playing on the operating system.
    /// </summary>
    public partial class SystemMediaBackground : Background
    {
        public readonly Texture Cover;

        public SystemMediaBackground(Texture cover)
        {
            Cover = cover;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Sprite.Texture = Cover;
        }

        public override bool Equals(Background other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.GetType() == GetType()
                   && ((SystemMediaBackground)other).Cover == Cover;
        }
    }
}
