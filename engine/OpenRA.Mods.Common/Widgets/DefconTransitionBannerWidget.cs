#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

/*
 * THE TRANSITION BANNER -- the moment that actually teaches the system.
 *
 * Four seconds of full-width band on each level change, then it is gone and DefconReadoutWidget's
 * strip carries the state from there. The mockup's note is the specification:
 *
 *     "Naming the CAUSE in the second line -- a life taken, the clock run out -- is what turns a
 *      state change into a rule the player can infer without ever being told it."
 *
 * So the second line is not decoration and is not a subtitle for the first. A banner that said only
 * "DEFCON 1" would tell a player what rung they are on and nothing about how they got there, and the
 * next match they would learn nothing again. The lines themselves live in DefconReadoutModel.
 *
 * ==== THE CHANGE IS DETECTED BY WATCHING, NOT BY BEING TOLD, AND THAT IS DELIBERATE ====
 * The obvious alternative is a notification raised from DefconEscalation.Tick, where the level
 * actually moves. This watches instead, comparing the level it saw last frame, for one reason: the
 * trait's transition happens inside synced code on every client, and everything this widget does --
 * a hold timer, a colour, whether to draw at all -- is client-local render state. Keeping it on this
 * side of the line means there is no route from a rendering decision back into the simulation, and
 * nothing here to get wrong in a sync report.
 *
 * The cost of watching is that a change is noticed on the next FRAME rather than the same tick,
 * which is under 17 ms at any frame rate a player would tolerate.
 *
 * ==== THE HOLD IS COUNTED IN WORLD TICKS, NOT WALL CLOCK ====
 * So it stops when the game is paused. A banner that ran out behind a pause menu would be a banner
 * the player never read, and this is a mode whose whole problem was that nothing reached the screen.
 * The conversion is in DefconReadoutModel.BannerHoldTicks: 4000 ms / 60 ms = 66 ticks = 3.96 s at
 * 16.67 ticks/s. NOT 25 tps -- that reading gives 100 ticks, which is six seconds.
 */

using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class DefconTransitionBannerWidget : Widget
	{
		public readonly string TitleFont = "BigBold";
		public readonly string CauseFont = "Regular";

		readonly World world;
		readonly SpriteFont titleFont, causeFont;

		DefconEscalation escalation;
		bool initialised;

		// The level as of the last frame. NoLevel until the first Tick, which is what stops a match
		// OPENING with a banner: a match that starts at DEFCON 2 by lobby setting has not transitioned
		// into anything, and announcing a cause that never happened is worse than announcing nothing.
		int lastLevel = DefconEscalationState.NoLevel;

		int shownLevel = DefconEscalationState.NoLevel;
		int shownAtTick;

		[ObjectCreator.UseCtor]
		public DefconTransitionBannerWidget(World world)
		{
			this.world = world;

			titleFont = Game.Renderer.Fonts[TitleFont];
			causeFont = Game.Renderer.Fonts[CauseFont];
		}

		public override void Tick()
		{
			if (!initialised)
			{
				initialised = true;
				escalation = world.WorldActor.TraitOrDefault<DefconEscalation>();
			}

			if (escalation == null)
				return;

			var level = escalation.Level;
			if (level == lastLevel)
				return;

			// A REAL TRANSITION IS BETWEEN TWO REAL LEVELS. The NoLevel -> level edge is the first
			// frame of any Escalation match and is not something that happened to the player.
			if (lastLevel != DefconEscalationState.NoLevel && DefconReadoutModel.TransitionCause(level) != null)
			{
				shownLevel = level;
				shownAtTick = world.WorldTick;
			}

			lastLevel = level;
		}

		public override void Draw()
		{
			if (shownLevel == DefconEscalationState.NoLevel || !IsVisible())
				return;

			// PITFALL: GameSpeed.Timestep, not world.Timestep. The latter is mutated at runtime by the
			// debug speed button and by test-mode speed multipliers -- with it, holding the banner for
			// "four seconds" would mean four seconds at whatever speed the game happened to be running
			// when the level moved. See conventions.md.
			var hold = DefconReadoutModel.BannerHoldTicks(world.GameSpeed.Timestep);
			if (world.WorldTick - shownAtTick >= hold)
			{
				shownLevel = DefconEscalationState.NoLevel;
				return;
			}

			var accent = DefconPalette.ForLevel(shownLevel);
			var rb = RenderBounds;

			WidgetUtils.FillRectWithColor(rb, Color.FromArgb(209, 9, 10, 8));

			// Top and bottom rules only, no side borders: the band runs off both edges of the screen,
			// which is what makes it read as the screen changing state rather than as a dialog.
			var border = Color.FromArgb(128, accent);
			WidgetUtils.FillRectWithColor(new Rectangle(rb.X, rb.Y, rb.Width, 1), border);
			WidgetUtils.FillRectWithColor(new Rectangle(rb.X, rb.Bottom - 1, rb.Width, 1), border);

			var title = $"DEFCON {shownLevel}";
			var titleSize = titleFont.Measure(title);

			var cause = DefconReadoutModel.TransitionCause(shownLevel);
			var causeSize = causeFont.Measure(cause);

			var blockHeight = titleSize.Y + 7 + causeSize.Y;
			var y = rb.Y + ((rb.Height - blockHeight) / 2);

			titleFont.DrawTextWithContrast(title, new float2(rb.X + ((rb.Width - titleSize.X) / 2), y),
				accent, Color.FromArgb(160, 0, 0, 0), 2);

			y += titleSize.Y + 7;

			causeFont.DrawTextWithContrast(cause, new float2(rb.X + ((rb.Width - causeSize.X) / 2), y),
				Color.FromArgb(203, 201, 190), Color.FromArgb(160, 0, 0, 0), 1);
		}
	}
}
