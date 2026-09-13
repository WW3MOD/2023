#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * THE FINAL EXCHANGE COUNTDOWN -- fifteen seconds the player must not be able to miss.
 *
 * Unlike DefconTransitionBannerWidget, which this is otherwise modelled on, it is NOT a four-second
 * announcement. It is on screen for the whole window and it carries a clock, because the thing it is
 * telling the player is not "something changed" but "you have this long to act". A banner that
 * announced the exchange and then left is a banner that would send the player looking for a timer
 * that does not exist.
 *
 * ==== IT WATCHES, IT IS NOT TOLD, AND THAT IS THE SAME RULE AS THE DEFCON BANNER ====
 * DoomsdayStrike opens the window inside synced simulation code on every client. Everything here --
 * whether to draw, what colour, what number -- is client-local render state read back out of the
 * trait each frame. Keeping it on this side of the line means there is no route from a rendering
 * decision into the simulation, and nothing here to get wrong in a sync report. The cost is that the
 * window is noticed on the next FRAME rather than the same tick, which is under 17 ms.
 *
 * ==== THE CLOCK IS TICKS, CONVERTED WITH GameSpeed.Timestep ====
 * PITFALL: not world.Timestep. That one is mutated at runtime by the debug speed button and by
 * test-mode speed multipliers, so a countdown converted with it would read a different number of
 * seconds for the same fifteen ticks depending on what speed the game happened to be running at.
 * Same rule, and the same reason, as DefconTransitionBannerWidget's hold and TimeLimitManager's
 * own countdown label (TimeLimitManager.cs:143-146). See conventions.md.
 *
 * The value itself comes from the trait, which counts in world ticks -- so the clock stops when the
 * game is paused, exactly as the window it is counting does.
 */

using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class FinalExchangeBannerWidget : Widget
	{
		public readonly string TitleFont = "BigBold";
		public readonly string LineFont = "Regular";

		/// <summary>Seconds left at which the clock turns to the DEFCON 1 red.</summary>
		public readonly int UrgentSeconds = 5;

		public readonly string Title = "FINAL EXCHANGE";
		public readonly string Line = "PLACE YOUR WARHEADS";

		readonly World world;
		readonly SpriteFont titleFont, lineFont;

		DoomsdayStrike doomsday;
		bool initialised;

		[ObjectCreator.UseCtor]
		public FinalExchangeBannerWidget(World world)
		{
			this.world = world;

			titleFont = Game.Renderer.Fonts[TitleFont];
			lineFont = Game.Renderer.Fonts[LineFont];
		}

		public override void Tick()
		{
			if (initialised)
				return;

			// Resolved once, lazily, and null-checked at every use: a map is free not to carry the
			// trait, and chrome is loaded long after the world actor exists so there is no
			// half-constructed window to fall into here (see DefconReadoutWidget's header).
			initialised = true;
			doomsday = world.WorldActor.TraitOrDefault<DoomsdayStrike>();
		}

		public override void Draw()
		{
			if (doomsday == null || !doomsday.FinalExchangeOpen || !IsVisible())
				return;

			var ticks = doomsday.FinalExchangeTicksRemaining;

			// ROUNDED UP. At 1 tick left the honest thing to show is "1", not "0" -- a clock that sits
			// on zero while the player can still place is a clock that has stopped telling the truth.
			var timestep = world.GameSpeed.Timestep;
			var seconds = (ticks * timestep + 999) / 1000;

			var accent = DefconPalette.DefconOne;
			var rb = RenderBounds;

			WidgetUtils.FillRectWithColor(rb, Color.FromArgb(209, 9, 10, 8));

			// Top and bottom rules only, no side borders: the band runs off both edges of the screen,
			// which is what makes it read as the screen changing state rather than as a dialog.
			var border = Color.FromArgb(128, accent);
			WidgetUtils.FillRectWithColor(new Rectangle(rb.X, rb.Y, rb.Width, 1), border);
			WidgetUtils.FillRectWithColor(new Rectangle(rb.X, rb.Bottom - 1, rb.Width, 1), border);

			var titleSize = titleFont.Measure(Title);

			var clock = $"{seconds / 60}:{seconds % 60:00}";
			var separator = "  —  ";
			var lineText = Line + separator;
			var lineSize = lineFont.Measure(lineText);
			var clockSize = lineFont.Measure(clock);

			var blockHeight = titleSize.Y + 7 + lineSize.Y;
			var y = rb.Y + ((rb.Height - blockHeight) / 2);

			titleFont.DrawTextWithContrast(Title, new float2(rb.X + ((rb.Width - titleSize.X) / 2), y),
				accent, Color.FromArgb(160, 0, 0, 0), 2);

			y += titleSize.Y + 7;

			// The instruction and the clock are ONE centred line drawn in two pieces, so that the clock
			// can carry its own colour without the line jumping sideways as the digits change width.
			var x = rb.X + ((rb.Width - (lineSize.X + clockSize.X)) / 2);

			lineFont.DrawTextWithContrast(lineText, new float2(x, y),
				Color.FromArgb(203, 201, 190), Color.FromArgb(160, 0, 0, 0), 1);

			lineFont.DrawTextWithContrast(clock, new float2(x + lineSize.X, y),
				seconds <= UrgentSeconds ? accent : DefconPalette.Rule, Color.FromArgb(160, 0, 0, 0), 1);
		}
	}
}
