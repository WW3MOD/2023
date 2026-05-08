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

			var descLabel = widget.GetOrNull<LabelWidget>("TEST_DESCRIPTION");
			if (descLabel != null)
				descLabel.GetText = () => TestMode.Description ?? "";

			var restart = widget.GetOrNull<ButtonWidget>("RESTART_BUTTON");
			if (restart != null)
				restart.OnClick = Game.RestartGame;
		}
	}
}
