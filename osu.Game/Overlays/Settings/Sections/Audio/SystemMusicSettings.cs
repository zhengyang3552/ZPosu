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
                    TooltipText = @"开启后，主菜单背景将跟随系统播放器的封面（获取失败或没有歌曲时显示内置背景），osu! 的播放控件将控制系统的播放；开启期间内置的歌曲选择不可用",
                    Current = config.GetBindable<bool>(OsuSetting.CaptureSystemMedia),
                },
            };
        }
    }
}
