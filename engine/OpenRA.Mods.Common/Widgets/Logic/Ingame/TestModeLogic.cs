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
		public TestModeLogic(Widget widget, World world)
		{
			if (!TestMode.IsActive)
			{
				widget.Visible = false;
				widget.IsVisible = () => false;
				return;
			}

			// Give the local human watching the test window full-map vision (world view),
			// the same camera an "All Players" observer gets. RenderPlayer is render-side
			// only: FogObscures/ShroudObscures short-circuit to false when it's null, so
			// no unit is hidden. Each player's own shroud/MapLayers — which the AI and the
			// verdict read — are untouched. Only for a real player slot (autotests);
			// spectator/tournament clients (LocalPlayer null) keep their observer default.
			if (world.LocalPlayer != null && !world.LocalPlayer.Spectating)
				world.RenderPlayer = null;

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
