// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;

namespace osu.Game.Overlays.Settings.Sections.Audio
{
    public partial class SystemMusicSettings : SettingsSubsection
    {
        protected override LocalisableString Header => @"系统音乐";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsCheckbox
                {
                    LabelText = @"捕获系统音乐",
                    TooltipText = @"开启后，osu! 的播放控件将控制系统播放器，主菜单的频谱与律动会跟随系统音乐；开启期间内置的歌曲选择不可用",
                    Current = config.GetBindable<bool>(OsuSetting.CaptureSystemMedia),
                },
            };
        }
    }
}
