using OpenRA.Widgets;

namespace OpenRA
{
	public class NullInputHandler : IInputHandler
	{
		// Ignore all input
		public void ModifierKeys(Modifiers mods) { }
		public void OnKeyInput(KeyInput input) { }
		public void OnTextInput(string text) { }
		public void OnMouseInput(MouseInput input) { }
	}

	public class DefaultInputHandler : IInputHandler
	{
		readonly World world;
		public DefaultInputHandler(World world)
		{
			this.world = world;
		}

		public void ModifierKeys(Modifiers mods)
		{
			Game.HandleModifierKeys(mods);
		}

		public void OnKeyInput(KeyInput input)
		{
			Sync.RunUnsynced(world, () => Ui.HandleKeyPress(input));
		}

		public void OnTextInput(string text)
		{
			Sync.RunUnsynced(world, () => Ui.HandleTextInput(text));
		}

		public void OnMouseInput(MouseInput input)
		{
			Sync.RunUnsynced(world, () => Ui.HandleInput(input));
		}
	}

	public class MouseButtonPreference
	{
		public MouseButton Action => Game.Settings.Game.UseClassicMouseStyle ? MouseButton.Left : MouseButton.Right;

		public MouseButton Cancel => Game.Settings.Game.UseClassicMouseStyle ? MouseButton.Right : MouseButton.Left;

		// Added for WW3MOD to support configurable attack move button
		// public MouseButton AttackMove => Game.Settings.Game.AttackMoveButton ?? MouseButton.Right;
		public MouseButton AttackMove => MouseButton.Right; // Simplified, got errors with above code
	}
}
