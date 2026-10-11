// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Overlays;

namespace osu.Game.Tests.Visual.Menus
{
    public partial class TestSceneMainMenu : OsuGameTestScene
    {
        [Test]
        public void TestVolumeControlViaScroll()
        {
            AddUntilStep("wait for volume overlay loaded", () => Game.ChildrenOfType<VolumeOverlay>().Single().IsLoaded);
            AddStep("reset volume", () => Audio.Volume.Value = 1);

            AddStep("mouse in centre", () => InputManager.MoveMouseTo(Game.ScreenSpaceDrawQuad.Centre));

            AddRepeatStep("scroll down", () => InputManager.ScrollVerticalBy(-10), 5);
            AddAssert("volume lowered down", () => Audio.Volume.Value < 1);
            AddRepeatStep("scroll up", () => InputManager.ScrollVerticalBy(10), 5);
            AddAssert("volume raised up", () => Audio.Volume.Value == 1);
        }
    }
}
