using System.Collections.Generic;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class BackgroundWidget : Widget
	{
		public readonly bool ClickThrough = false;
		public readonly bool Draggable = false;
		public string Background = "dialog";
		public Dictionary<string, string> Panels = new Dictionary<string, string>(); // WW3MOD: Added to support Panels field
		public int2 ButtonStride = int2.Zero; // Added for WW3MOD settings panel button spacing

		public override void Draw()
		{
			WidgetUtils.DrawPanel(Background, RenderBounds);
		}

		public BackgroundWidget() { }

		bool moving;
		int2? prevMouseLocation;

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (ClickThrough || !RenderBounds.Contains(mi.Location))
				return false;

			if (!Draggable || (moving && (!TakeMouseFocus(mi) || mi.Button != MouseButton.Left)))
				return true;

			if (prevMouseLocation == null)
				prevMouseLocation = mi.Location;
			var vec = mi.Location - (int2)prevMouseLocation;
			prevMouseLocation = mi.Location;
			switch (mi.Event)
			{
				case MouseInputEvent.Up:
					moving = false;
					YieldMouseFocus(mi);
					break;
				case MouseInputEvent.Down:
					moving = true;
					Bounds = new Rectangle(Bounds.X + vec.X, Bounds.Y + vec.Y, Bounds.Width, Bounds.Height);
					break;
				case MouseInputEvent.Move:
					if (moving)
						Bounds = new Rectangle(Bounds.X + vec.X, Bounds.Y + vec.Y, Bounds.Width, Bounds.Height);
					break;
			}

			return true;
		}

		protected BackgroundWidget(BackgroundWidget other)
			: base(other)
		{
			Background = other.Background;
			Panels = new Dictionary<string, string>(other.Panels);
			ClickThrough = other.ClickThrough;
			Draggable = other.Draggable;
			ButtonStride = other.ButtonStride;
		}

		public override Widget Clone() { return new BackgroundWidget(this); }
	}
}
