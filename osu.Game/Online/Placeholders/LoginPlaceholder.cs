// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;

namespace osu.Game.Online.Placeholders
{
    /// <summary>
    /// A placeholder shown where online content would appear, in the place of a sign-in prompt.
    /// This build has no account system, so the placeholder is purely informational and not interactive.
    /// </summary>
    public sealed partial class LoginPlaceholder : ClickablePlaceholder
    {
        public LoginPlaceholder(LocalisableString actionMessage)
            : base(actionMessage, FontAwesome.Solid.UserLock)
        {
        }
    }
}
