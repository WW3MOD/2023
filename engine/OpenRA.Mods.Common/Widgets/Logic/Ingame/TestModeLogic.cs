#region Copyright & License Information
/*
 * WW3MOD developer test harness UI logic.
 * Mounted unconditionally; gates itself on TestMode.IsActive so normal gameplay is unaffected.
 */
#endregion

using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	public class TestModeLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public TestModeLogic(Widget widget)
		{
			if (!TestMode.IsActive)
			{
				widget.Visible = false;
				widget.IsVisible = () => false;
				return;
			}

			var nameLabel = widget.GetOrNull<LabelWidget>("TEST_NAME");
			if (nameLabel != null)
				nameLabel.GetText = () => TestMode.Name;

			var pass = widget.Get<ButtonWidget>("PASS_BUTTON");
			pass.OnClick = () =>
			{
				TestMode.WriteResult("pass", "");
				Game.Exit();
			};

			var fail = widget.Get<ButtonWidget>("FAIL_BUTTON");
			fail.OnClick = () =>
			{
				TestMode.WriteResult("fail", "");
				Game.Exit();
			};

			var skip = widget.Get<ButtonWidget>("SKIP_BUTTON");
			skip.OnClick = () =>
			{
				TestMode.WriteResult("skip", "");
				Game.Exit();
			};
		}
	}
}
