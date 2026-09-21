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

		// ---- WHAT THE TRANSITION SOUNDS LIKE ----------------------------------------------------
		// Until 2026-09-13 it sounded like NOTHING. The band appeared, held four seconds and left,
		// and a player looking anywhere else on the screen missed the rule change entirely.
		//
		// THESE ARE UI BLEEPS AND NOT SPEECH, AND THAT IS A COMPROMISE WORTH KNOWING ABOUT. The mod's
		// Speech pool is Red Alert's recorded lines (rules/sound/notifications.yaml:1-116) and none of
		// them says anything about a border opening or autonomous fire being released -- there is no
		// recorded DEFCON line to use, and naming a file that does not exist is worse than a bleep.
		// So each transition takes the closest existing UI cue, chosen for WEIGHT rather than
		// meaning: AlertBleep (bleep6) for 3 -> 2, which opens a border, and AlertBuzzer (buzzy1) for
		// 2 -> 1, which releases the shooting. Both are defined in that file's Sounds pool (:135,
		// :134). Replacing either with a recorded line is a one-line chrome edit and nothing else.
		//
		// NOT LINTED. CheckNotifications does not walk widget fields -- see DefconAlert's header --
		// so a typo here is caught by DefconAlert's pool check at runtime and by nothing else.
		public readonly string NotificationPool = DefconAlert.SoundsPool;

		/// <summary>Notification played on the 3 -> 2 transition.</summary>
		public readonly string LevelTwoNotification = "AlertBleep";

		/// <summary>Notification played on the 2 -> 1 transition.</summary>
		public readonly string LevelOneNotification = "AlertBuzzer";

		/// <summary>Fluent key of the system line shown on the 3 -> 2 transition.</summary>
		public readonly string LevelTwoTextNotification = "notification-defcon-two";

		/// <summary>Fluent key of the system line shown on the 2 -> 1 transition.</summary>
		public readonly string LevelOneTextNotification = "notification-defcon-one";

		/// <summary>Fluent key of the event-log line naming who took the first life (§B6).</summary>
		// SEPARATE FROM LevelOneTextNotification AND IN ADDITION TO IT. That line states the RULE that
		// just changed; this one states WHO CHOSE, which is the half the match record was missing. The
		// 2026-09-19 review asks for both: "a single combined banner ... plus an event-log line naming
		// the first casualty's type and owner, so the match record shows who chose."
		public readonly string FirstCasualtyTextNotification = "notification-defcon-first-casualty";

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

		/// <summary>The level the CURRENT banner also covers, when two edges were combined into one.</summary>
		// NoLevel means "this banner is about one edge", which is every banner in a match where the
		// phases are far apart. See DefconReadoutModel.CombinesWithPrevious for the whole argument.
		int shownFromLevel = DefconEscalationState.NoLevel;

		/// <summary>Banners RAISED this match, combined ones counting once. Read by Test bindings.</summary>
		// THE OBSERVABLE THE SCENARIO ASSERTS ON, and it has to be a count of raises rather than
		// anything read off the screen: an autotest runs --hidden, which suspends rendering, so Draw
		// may never be called at all. Everything that decides WHAT the player gets therefore lives in
		// Tick (below) and Draw only paints what Tick decided -- which is where that logic belonged
		// anyway. Before this, the hold window was expired inside Draw, so a hidden run's banner
		// never expired.
		public int BannersRaised { get; private set; }

		/// <summary>Is the banner now on screen a COMBINED one? False when nothing is showing.</summary>
		public bool ShowingCombinedBanner => shownLevel != DefconEscalationState.NoLevel
			&& shownFromLevel != DefconEscalationState.NoLevel;

		/// <summary>The level the band now on screen announces, or NoLevel when nothing is.</summary>
		public int ShownLevel => shownLevel;

		/// <summary>The earlier edge a COMBINED band also covers, or NoLevel. Diagnostic only.</summary>
		public int ShownFromLevel => shownFromLevel;

		[ObjectCreator.UseCtor]
		public DefconTransitionBannerWidget(World world)
		{
			this.world = world;

			titleFont = Game.Renderer.Fonts[TitleFont];
			causeFont = Game.Renderer.Fonts[CauseFont];
		}

		// ---- IT MUST NOT EAT THE MOUSE, DRAWN OR NOT ---------------------------------------------
		// THE SAME GUARD, AND THE SAME REASON, AS DefconReadoutWidget.cs:245. Suppressing the band
		// inside Draw() does NOT make this widget invisible: `Visible` is still true (Widget.cs:222),
		// so GetCursorOuter's `IsVisible() && EventBoundsContains(pos)` test passes (Widget.cs:399-415)
		// and the inherited EventBounds => RenderBounds (Widget.cs:327) claims the whole full-width
		// band. It then answers with the inherited default cursor (Widget.cs:398). PLAYER_ROOT is
		// added AFTER the interaction controller and the walk is in REVERSE, so that "default" beats
		// the world's move/attack cursor -- across a full-window-width strip, for the whole match, on
		// every frame the banner is not showing.
		//
		// Rectangle.Empty rather than driving `Visible`: these bands are non-interactive announcements
		// with no children, no tooltip and no input of any kind, so the bounds that match what they do
		// are none EVEN WHILE DRAWN. A player ordering units through the moment a DEFCON transition
		// lands should not have the order swallowed by the thing telling them about it.
		public override Rectangle EventBounds => Rectangle.Empty;

		public override void Tick()
		{
			if (!initialised)
			{
				initialised = true;
				escalation = world.WorldActor.TraitOrDefault<DefconEscalation>();
			}

			if (escalation == null)
				return;

			// ---- THE HOLD EXPIRES HERE, NOT IN Draw() ------------------------------------------
			// It used to expire in Draw, which made "four seconds" mean "four seconds of RENDERING".
			// A --hidden autotest suspends rendering entirely, so under it the banner was raised and
			// then never came down -- and a scenario asking "how many banners did the player get" got
			// an answer that depended on whether anyone was looking. Tick decides, Draw paints.
			var hold = DefconReadoutModel.BannerHoldTicks(world.GameSpeed.Timestep);
			if (shownLevel != DefconEscalationState.NoLevel
				&& !DefconReadoutModel.CombinesWithPrevious(shownAtTick, world.WorldTick, hold))
			{
				shownLevel = DefconEscalationState.NoLevel;
				shownFromLevel = DefconEscalationState.NoLevel;
			}

			var level = escalation.Level;
			if (level == lastLevel)
				return;

			// A REAL TRANSITION IS BETWEEN TWO REAL LEVELS. The NoLevel -> level edge is the first
			// frame of any Escalation match and is not something that happened to the player.
			if (lastLevel != DefconEscalationState.NoLevel && DefconReadoutModel.TransitionCause(level) != null)
			{
				// ---- ONE BANNER, NOT A BANNER AND A HALF (§B6) ---------------------------------
				// THE RECORD, NOT WHAT THIS WIDGET HAPPENED TO WITNESS -- and that distinction is the
				// whole of the 2026-09-21 rewrite. `Ui.Tick` runs from the LOGIC tick (Game.cs:786-790)
				// but on `Ui.Timestep` = 40 ms of WALL CLOCK (Widget.cs:30), which is a completely
				// different clock from the world's (`orderManager.LastTickTime`, OrderManager.cs:187).
				// The two only look alike at 1x speed on a machine that is keeping up.
				//
				// So "did I see a banner for the rung above" is not a question this widget can answer
				// reliably. Under any hitch, any fast-forward, and always in a --hidden autotest (where
				// run-test.sh sets Graphics.CapFramerate=false and the sim free-runs), the world can
				// advance dozens of ticks between two UI ticks -- and a DEFCON 2 that lasts 98 ticks,
				// or one tick, can pass entirely between them. The widget would then see 3 -> 1 with no
				// 2 in it, raise one banner for OPEN WAR, and reproduce EXACTLY the defect §B6 exists to
				// remove, from a second cause nobody had noticed.
				//
				// DefconEscalation.LevelReachedTick is the authority instead: it is written in synced
				// code on the tick each level is first reached and cannot be missed by sampling. From
				// it, "were these two edges close together" is a fact about the match rather than about
				// this client's frame rate.
				//
				// The hold RESTARTS. That is the point of the whole change: the first banner has
				// typically been up for one or two ticks when the kill lands (run 260915_012829: 3 -> 2
				// at 5000, 2 -> 1 at 5001), so inheriting its elapsed time would put the combined
				// banner on screen for the remaining 65 ticks and leave the player reading a sentence
				// about two events in the time budgeted for one.
				var upper = level + 1;
				var upperReachedTick = escalation.LevelReachedTick(upper);
				var thisReachedTick = escalation.LevelReachedTick(level);

				var combining = DefconReadoutModel.CombinedTransitionCause(upper, level, 0) != null
					&& upperReachedTick >= 0 && thisReachedTick >= 0
					&& DefconReadoutModel.CombinesWithPrevious(upperReachedTick, thisReachedTick, hold);

				// COMBINING DOES NOT RAISE A SECOND BAND WHEN THE FIRST ONE IS ALREADY UP -- it rewrites
				// that band and restarts its clock, which is what "the player gets ONE banner" means.
				// But when this widget never showed the rung above at all (it ticked straight past it),
				// the player has had NO band yet and this one is their first: it must count. Either way
				// BannersRaised ends up as the number of distinct bands the player was actually shown,
				// which is the only thing it claims to be.
				var rewritingTheBandOnScreen = combining && shownLevel == upper;

				shownFromLevel = combining ? upper : DefconEscalationState.NoLevel;
				shownLevel = level;
				shownAtTick = world.WorldTick;

				if (!rewritingTheBandOnScreen)
					BannersRaised++;

				// ONCE PER EDGE, PER CLIENT, and it is this `if` that guarantees both. The method
				// returns above whenever the level has not moved, so this body runs on the one frame
				// the change is first seen; and the NoLevel test above it is what stops a match
				// OPENING with an alarm for a transition that never happened.
				DefconAlert.Play(world, NotificationPool,
					level == DefconEscalationState.Floor ? LevelOneNotification : LevelTwoNotification);

				DefconAlert.Line(world,
					level == DefconEscalationState.Floor ? LevelOneTextNotification : LevelTwoTextNotification);

				// WHO CHOSE. Raised on the 2 -> 1 edge whether or not the banners combined, because the
				// match record wants it either way -- the combined banner is what makes the moment
				// VISIBLE, and this is what makes it ATTRIBUTABLE. Both names come from
				// DefconEscalation, which captured them as strings on the tick the kill landed; a null
				// pair means the level moved by something other than a reported casualty and the line
				// is skipped rather than printed with blanks in it.
				if (level == DefconEscalationState.Floor
					&& escalation.FirstCasualtyAttackerOwner != null && escalation.FirstCasualtyType != null)
					DefconAlert.Line(world, FirstCasualtyTextNotification,
						"attacker", escalation.FirstCasualtyAttackerOwner,
						"weapon", escalation.FirstCasualtyAttackerType ?? "?",
						"victim", escalation.FirstCasualtyType,
						"owner", escalation.FirstCasualtyOwner ?? "?");
			}

			lastLevel = level;
		}

		public override void Draw()
		{
			if (shownLevel == DefconEscalationState.NoLevel || !IsVisible())
				return;

			// NO EXPIRY TEST HERE ANY MORE -- Tick owns it, so the hold is four seconds of GAME rather
			// than four seconds of drawing. PITFALL, still live in Tick: GameSpeed.Timestep, not
			// world.Timestep. The latter is mutated at runtime by the debug speed button and by
			// test-mode speed multipliers. See conventions.md.

			// THE COMBINED BAND. The title stays the level the player is NOW on -- that is the state
			// they have to act under, and the arrow form "DEFCON 2 -> 1" was rejected because no glyph
			// like it appears anywhere else in the mod's copy and an unrenderable one draws as a box.
			// Both edges are named in the CAUSE line instead, which is where this banner has always put
			// causes, and the duration goes with them.
			var cause = DefconReadoutModel.TransitionCause(shownLevel);
			if (shownFromLevel != DefconEscalationState.NoLevel)
			{
				var from = escalation?.LevelReachedTick(shownFromLevel) ?? -1;
				var to = escalation?.LevelReachedTick(shownLevel) ?? -1;
				var seconds = from >= 0 && to >= 0
					? DefconReadoutModel.SecondsBetween(from, to, world.GameSpeed.Timestep)
					: 0;

				cause = DefconReadoutModel.CombinedTransitionCause(shownFromLevel, shownLevel, seconds) ?? cause;
			}

			// THE GEOMETRY IS DefconBannerBand's, shared with the other two banners since 2026-09-13.
			// The two lines are centred as a BLOCK rather than independently, and the band has top and
			// bottom rules only -- see that file for why both of those carry meaning.
			DefconBannerBand.Draw(RenderBounds, DefconPalette.ForLevel(shownLevel),
				titleFont, $"DEFCON {shownLevel}",
				causeFont, cause);
		}
	}
}
