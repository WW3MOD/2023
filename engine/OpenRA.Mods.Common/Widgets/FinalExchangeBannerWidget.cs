#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * THE FINAL EXCHANGE COUNTDOWN -- the one window the player must not be able to miss. Its length
 * is DoomsdayStrikeInfo.FinalExchangeWindowTicks (250 = 15 s; it was briefly 500 between
 * 2026-09-16 and 2026-09-20, for an asymmetry between the two factions' game-enders that no
 * longer exists).
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

		// WHAT HAPPENS NOW, NOT WHAT THE PLAYER MAY DO. "PLACE YOUR WARHEADS" told a player what
		// button to press and left the consequence of not pressing it entirely unstated -- which,
		// while Dead Hand existed, a player could reasonably read as "or nothing of mine flies".
		// Since 2026-09-20 an unplaced package fires at the enemy anyway, and the banner says so:
		// the choice on offer is WHERE, never WHETHER.
		public readonly string Line = "PLACE YOUR STRIKE PACKAGE — UNPLACED FIRES AT THE ENEMY";

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

		// ---- IT MUST NOT EAT THE MOUSE, DRAWN OR NOT ---------------------------------------------
		// THE SAME GUARD, AND THE SAME REASON, AS DefconReadoutWidget.cs:245. Suppressing the band
		// inside Draw() does NOT make this widget invisible: `Visible` is still true (Widget.cs:222),
		// so GetCursorOuter's `IsVisible() && EventBoundsContains(pos)` test passes (Widget.cs:399-415)
		// and the inherited EventBounds => RenderBounds (Widget.cs:327) claims the whole 110px
		// full-width band. It then answers with the inherited default cursor (Widget.cs:398).
		// PLAYER_ROOT is added AFTER the interaction controller and the walk is in REVERSE, so that
		// "default" beats the world's move/attack cursor for the entire match outside the window.
		//
		// AND THE DRAWN CASE MATTERS MORE HERE THAN FOR THE OTHER TWO. This band is up for the whole
		// fifteen-second window while the line under it tells the player to place a strike package --
		// a player doing exactly that, through the strip, must not have the cursor go dead on them.
		public override Rectangle EventBounds => Rectangle.Empty;

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

			var clock = $"{seconds / 60}:{seconds % 60:00}";

			// THE GEOMETRY IS DefconBannerBand's, shared with the other two banners since 2026-09-13.
			// The instruction and the clock go in as two pieces of ONE centred line, which is what
			// lets the clock carry its own colour without the line jumping sideways as the digits
			// change width. See that file for why the band has no side borders.
			DefconBannerBand.Draw(RenderBounds, DefconPalette.DefconOne, titleFont, Title,
				lineFont, Line + "  —  ", clock,
				seconds <= UrgentSeconds ? DefconPalette.DefconOne : DefconPalette.Rule);
		}
	}
}
