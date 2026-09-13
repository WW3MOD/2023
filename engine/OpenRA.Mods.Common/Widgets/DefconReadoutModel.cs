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
 * WHAT THE DEFCON READOUT SAYS -- the wording, the visibility rules and the tick arithmetic, with no
 * dependency on Widget, Renderer, Actor or World.
 *
 * A plain class for exactly the reason DefconEscalationState and NuclearReleaseLadder are plain
 * classes, and stated in both their headers: nothing in OpenRA.Test can construct a World, and
 * nothing in it can construct a Renderer either, so a rule living inside a Draw() is a rule verified
 * by reading only. Everything here is a string or an int, so DefconReadoutTest can pin the copy
 * itself -- which matters more than usual, because of what the copy is.
 *
 * ==== THE COPY IS APPROVED AND VERBATIM ====
 * The three level lines come from the wording table at the bottom of
 * WORKSPACE/mockups/defcon-hud-directions.html, whose own note is the specification:
 *
 *     "These three lines do more work than the graphics do. Each states a rule as what the player
 *      can and cannot do right now, never as the mechanic behind it -- no stances, no autonomous
 *      targeting, no counters, no terrain."
 *
 * So DO NOT "improve" them toward the mechanism. `DefconFireDiscipline` is the trait that implements
 * DEFCON 2, and the player is never told the word `AttackSource`, the word "stance", or that a hold
 * exists at exactly one rung. RuleLineIsApprovedCopy in the test fixture holds all three to the
 * character, and it exists to fail rather than to pass.
 *
 * ==== THE TICK RATE, WRITTEN OUT BECAUSE THIS REPO HAS GOT IT WRONG ELEVEN TIMES ====
 * The timestep is 60 ms (mods/ww3mod/mod.yaml:358 selects `default`, whose block is at :382), so one
 * tick is 0.06 s and the rate is 1000/60 = 16.67 ticks/s -- NOT 25.
 *
 *     BannerHoldMs 4000 / 60 = 66 ticks, and 66 x 0.06 s = 3.96 s -- the "about 4 seconds" the
 *     mockup asks for. Read as 25 tps it would have been 100 ticks, which is SIX seconds of real
 *     time: the same 1.5x error, reached the same way.
 *
 * The identity to check any of this against is the DEFAULT NO-RUSH PERIOD: 5 minutes is 5000 ticks,
 * and 5000 x 0.06 s = 300 s = 5:00. TheTickRateIdentityHolds asserts exactly that, so a machine that
 * ever disagrees says so in the test run rather than in a countdown nobody times with a stopwatch.
 * (It used to name DefconEscalationInfo.StandardTicks, which was retired on 2026-09-13 when the
 * three pace fields became one minutes dropdown. The arithmetic is unchanged -- the default was
 * chosen to leave it unchanged -- but the field that carried it no longer exists.)
 *
 * Note that the on-screen countdowns themselves are NOT formatted here -- they go through
 * WidgetUtils.FormatTime, which takes the timestep as an argument. What this class owns is the
 * decision of WHETHER there is a clock to draw at all, which is the part with a rule behind it.
 */

using System;
using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Widgets
{
	public static class DefconReadoutModel
	{
		/// <summary>The em dash the clock field falls back to. LOAD-BEARING, NOT COSMETIC.</summary>
		// The player has just spent minutes watching a countdown in this exact spot, so the EMPTIED
		// SLOT is what teaches them that this rung ends on an event rather than on a clock. Hiding the
		// field instead would leave them waiting for a bar that never fills -- which is the one thing
		// the mockup says every direction has to avoid.
		public const string NoClock = "—";

		/// <summary>The pulsing line that replaces the clock's job at DEFCON 2.</summary>
		public const string TriggerLine = "FIRST KILL ENDS THIS PHASE";

		/// <summary>How long the transition banner is held, in milliseconds. See the file header.</summary>
		public const int BannerHoldMs = 4000;

		/// <summary>Ticks the transition banner is held for. 4000 / 60 = 66 ticks = 3.96 s.</summary>
		// Takes the timestep rather than assuming one, so a mod or a match on a different game speed
		// holds the banner for four SECONDS rather than for a fixed number of ticks that only means
		// four seconds at 60 ms. Guarded against a zero timestep because Widget code cannot afford to
		// divide by whatever it was handed.
		public static int BannerHoldTicks(int timestep)
		{
			return timestep <= 0 ? 0 : BannerHoldMs / timestep;
		}

		/// <summary>The level's name, as the wording table gives it.</summary>
		public static string LevelName(int level)
		{
			switch (level)
			{
				case 3: return "Positioning";
				case 2: return "Weapons free";
				case 1: return "Open war";
				default: return null;
			}
		}

		/// <summary>The ONE line stating the rule in force. Approved copy; see the file header.</summary>
		public static string RuleLine(int level)
		{
			switch (level)
			{
				case 3: return "The border is closed. Neither side may cross it.";
				case 2: return "Your units will not fire on their own. Every shot is one you order.";
				case 1: return "Everything is released. Units engage on sight.";
				default: return null;
			}
		}

		/// <summary>The banner's second line: what CAUSED the level to move, and what it released.</summary>
		// NAMING THE CAUSE IS THE WHOLE POINT OF THE BANNER -- it is what turns a state change into a
		// rule the player can infer without ever being told it. The DEFCON 1 line is verbatim from the
		// mockup. The DEFCON 2 line is written to its pattern, because the mockup drew only the one
		// banner and names the pattern in its note ("a life taken, the clock run out").
		//
		// Each names the cause, then the consequence, in the same register as the rule lines: what the
		// player may now do, never the trait that does it. The 3 -> 2 line says the border rather than
		// the fire discipline on purpose -- the fire rule is what the strip is about to say underneath
		// it, and a banner that said both would be teaching two things in four seconds.
		public static string TransitionCause(int level)
		{
			switch (level)
			{
				case 2: return "The holding period has run out. The border is open.";
				case 1: return "A life has been taken. Autonomous fire is released.";
				default: return null;
			}
		}

		/// <summary>Is a DEFCON readout drawn at all?</summary>
		// SKIRMISH IS A STRICT NO-OP and this is where that is honoured on screen: the mode leaves the
		// level at NoLevel, so asking the level rather than asking the mode is enough. That is
		// deliberate -- it is the same test DefconEscalationTest.SkirmishIsAStrictNoOp makes, and a
		// readout keyed on the mode instead could drift away from the rest of the feature.
		public static bool IsVisible(int level)
		{
			return LevelName(level) != null;
		}

		/// <summary>Is there a running countdown to show, or does the clock field go to an em dash?</summary>
		// ONLY 3 -> 2 IS ON A CLOCK (DefconEscalationState.ClockFor). At DEFCON 2 the phase ends on a
		// casualty and at DEFCON 1 nothing follows, so both empty the field. Sandbox is pinned at its
		// configured level and never moves at all, so it empties the field at every level including 3.
		public static bool ShowsClock(DefconGameMode mode, int level)
		{
			return mode == DefconGameMode.Escalation && level == DefconEscalationState.Ceiling;
		}

		/// <summary>Is the pulsing "first kill ends this phase" line shown?</summary>
		// ESCALATION ONLY, and that qualifier is the difference between a hint and a lie: in Sandbox a
		// casualty moves nothing (DefconEscalationState.ReportCasualty returns false for every mode but
		// Escalation), so a Sandbox match pinned at DEFCON 2 would be promising the player a transition
		// that cannot happen.
		public static bool ShowsTrigger(DefconGameMode mode, int level)
		{
			return mode == DefconGameMode.Escalation && DefconFireDiscipline.HoldsFire(level);
		}

		/// <summary>The compact label for a rung. Reached through <see cref="LedgerRungLabel"/>.</summary>
		// Steps() USED TO LIVE BESIDE THIS and built a label per rung for the single step row the
		// nuclear block drew before the ledger. The ledger replaced that row with one per SIDE, so
		// the list is built by LedgerBands() instead -- which skips Hold, because a row lists what a
		// side holds. The enum-derived guarantee moved with it and is pinned by
		// LedgerBandsFollowTheEnumOrder for the same reason Steps() was: the ladder renumbered once
		// already, and a hand-written list would have gone on drawing boxes one rung out of step.
		public static string ShortRungLabel(NuclearRung rung)
		{
			switch (rung)
			{
				case NuclearRung.Hold: return "HOLD";
				case NuclearRung.Kiloton: return "1kt";
				case NuclearRung.TwentyKiloton: return "20kt";
				case NuclearRung.FiftyKiloton: return "50kt";
				case NuclearRung.HundredKiloton: return "100kt";
				case NuclearRung.GameEnder: return "200kt+";
				default: return null;
			}
		}

		/// <summary>The rung named in full, for the readout's current-yield field.</summary>
		public static string RungLabel(int rung)
		{
			switch (rung)
			{
				case (int)NuclearRung.Hold: return "HOLD";
				case (int)NuclearRung.Kiloton: return "1 kt";
				case (int)NuclearRung.TwentyKiloton: return "20 kt";
				case (int)NuclearRung.FiftyKiloton: return "50 kt";
				case (int)NuclearRung.HundredKiloton: return "100 kt";
				case (int)NuclearRung.GameEnder: return "200 kt+";
				default: return null;
			}
		}

		/// <summary>The line under the step boxes.</summary>
		// THREE DIFFERENT SENTENCES because they are three different rules, and the shut one is the
		// whole reason this block is drawn before anything nuclear can be fired: a ten-minute wait
		// with nothing on screen explaining it is a player concluding the feature is broken. All three
		// state what may and may not be done, never the mechanism that does it.
		//
		// THE OPEN LINE CHANGED ON 2026-09-13 AND WAS A LIE BEFORE IT. It read "Both sides are
		// released to the same yield. Each use raises it." -- which was the shared pressure ladder,
		// where firing raised BOTH sides together. Under the exchange, firing raises the OTHER side;
		// a player who read the old line and fired to climb would have handed their opponent the
		// climb instead.
		public static string NuclearFootLine(bool releaseOpen, bool windowOpen)
		{
			if (!releaseOpen)
				return "No warhead may be fired yet. Both sides are released at the same moment.";

			return windowOpen
				? "You may answer one band up until the window closes. Firing arms them in turn."
				: "Firing arms the other side at that yield, and one band above it for a while.";
		}

		/// <summary>Is the nuclear release readout drawn?</summary>
		// TWO CONDITIONS, and each removes a case where the block would be noise or a lie:
		//   - Escalation only. Skirmish and Sandbox get their bands from NuclearUnlockClock's schedule
		//     instead, which this block does not describe.
		//   - Before DEFCON 1 the gate's countdown is not running at all -- NuclearReleaseGate.Tick
		//     returns early at any other level and TicksUntilRelease reads as the full delay. Drawing
		//     a frozen clock is worse than drawing none, and the DEFCON strip is carrying the phase.
		//
		// THE CEILING CONDITION IS GONE with `nuclear-ceiling` itself (2026-09-13 ruling): a host can
		// no longer say "no nuclear weapons this match", so there is no permanently-empty case left to
		// suppress.
		public static bool ShowsNuclear(DefconGameMode mode, int level, bool releaseOpen)
		{
			if (mode != DefconGameMode.Escalation)
				return false;

			return releaseOpen || level == DefconEscalationState.Floor;
		}

		// ==== THE LEDGER =====================================================================
		// Added 2026-09-13. The nuclear block used to draw ONE row -- the viewer's own bands -- and
		// the player could read what they held and nothing about what was pointed back at them. The
		// ruling's whole strategic claim is that the winner's correct play is RESTRAINT, and a player
		// cannot choose restraint over a position they cannot see. Both rows are on screen for both
		// players because COUNTABILITY IS THE MECHANIC: you hold 20 kt, they hold 20 kt and a 50 kt
		// window with 40 seconds left on it, and the decision follows from the two rows.

		/// <summary>What one band's box says about one side.</summary>
		// FOUR STATES AND NOT THREE. Held and Charging are the same PERMISSION and different facts:
		// a side whose 20 kt is regenerating may not fire it this minute, and a ledger that drew both
		// as lit would be telling a player they are covered when they are not. That distinction is
		// the entire value of putting the enemy's row on screen.
		public enum LedgerCell
		{
			/// <summary>Not held at all.</summary>
			Dark,

			/// <summary>Held permanently and ready to fire now.</summary>
			Held,

			/// <summary>Held permanently but regenerating. Carries a countdown.</summary>
			Charging,

			/// <summary>A retaliation window grant. Carries the window countdown.</summary>
			Window
		}

		/// <summary>The bands a ledger row draws, lowest first: every rung above Hold.</summary>
		// DERIVED FROM THE ENUM, for the reason Steps() states and restated here because this is a
		// SECOND derivation and the two must not drift apart. The ladder has already renumbered once
		// (the 2026-09-10 ruling split 50/100 kt and moved GameEnder from 4 to 5); a hand-written
		// list of five bands would have gone on drawing five boxes with every label one rung out of
		// step, which is a ledger that lies rather than one that fails. LedgerBandsFollowTheEnum
		// pins it.
		//
		// Hold is skipped because a row lists what a side HOLDS and Hold is the absence of that.
		public static IReadOnlyList<NuclearRung> LedgerBands()
		{
			var values = (NuclearRung[])Enum.GetValues(typeof(NuclearRung));
			var bands = new List<NuclearRung>(values.Length);
			foreach (var v in values)
				if (v > NuclearRung.Hold)
					bands.Add(v);

			return bands;
		}

		/// <summary>The label inside a ledger box. As <see cref="ShortRungLabel"/> except at the top.</summary>
		// "END" RATHER THAN "200kt+", and the difference is the point of that box. Every other rung is
		// a yield the player weighs against another yield; the top one is not a bigger bomb, it is the
		// end of the match. A number there invites it to be read as one more step up the same scale,
		// which is exactly the misreading decision 01 closed by making game-enders reachable only as a
		// grant. The step row keeps the yield label, because there the rung IS being read as a
		// position on a ladder.
		public static string LedgerRungLabel(NuclearRung rung)
		{
			return rung == NuclearRung.GameEnder ? "END" : ShortRungLabel(rung);
		}

		/// <summary>What one band's box shows for one side.</summary>
		public static LedgerCell CellFor(NuclearRung band, int permanentLevel, int windowBand,
			bool windowOpen, bool releaseOpen, bool charging)
		{
			// BEFORE RELEASE EVERY BOX IS DARK on both rows, whatever the state underneath says.
			// Nothing has been handed out yet and the block's value slot is carrying RELEASE IN m:ss;
			// a lit box under a countdown to the moment boxes light would contradict it.
			if (!releaseOpen)
				return LedgerCell.Dark;

			// THE WINDOW WINS WHERE THE TWO MEET, and they can meet. Off a single hit they cannot --
			// rule 2 raises the permanent level to Y and opens the window at Y+1 -- but a side hit at
			// 20 kt and then at 50 kt holds 50 kt permanently AND a window at 50 kt until the older
			// grant's band is overtaken. Drawing the grant is the honest reading: a grant is the thing
			// that EXPIRES, and the expiry is what the player has to act on before it does.
			if (windowOpen && (int)band == windowBand)
				return LedgerCell.Window;

			if ((int)band > permanentLevel)
				return LedgerCell.Dark;

			return charging ? LedgerCell.Charging : LedgerCell.Held;
		}

		public const string LedgerOwnLabel = "YOU";
		public const string LedgerEnemyLabel = "ENEMY";

		/// <summary>The label down the left of a ledger row.</summary>
		// A PLAYER READS YOU/ENEMY AND AN OBSERVER READS THE SIDE NAMES, and the split is not
		// cosmetic. "YOU" is the shortest thing that makes a row countable at a glance, and it is
		// exactly the word that is a LIE on an observer's screen -- there is no local player there to
		// be "you", and an observer following Russia would otherwise be told Russia is them.
		public static string LedgerRowLabel(bool hasLocalPlayer, bool isViewerSide, string sideName)
		{
			if (hasLocalPlayer || string.IsNullOrEmpty(sideName))
				return isViewerSide ? LedgerOwnLabel : LedgerEnemyLabel;

			return sideName.ToUpperInvariant();
		}

		// ==== THE MOMENTS ====================================================================
		// Three things happen that a player watching the battlefield will otherwise miss entirely,
		// because all three happen in a 352-pixel panel in a corner: the gate opens, THEY are armed,
		// and a grant they never used runs out. Each gets a line here and a sound at the call site.

		/// <summary>The banner shown when the viewer's own side gains a retaliation grant.</summary>
		public const string ArmedBannerTitle = "ARMED";

		/// <summary>The banner shown when the release gate opens for both sides.</summary>
		public const string NuclearReleaseBannerTitle = "NUCLEAR RELEASE";

		/// <summary>Its second line. Verbatim from the 2026-09-13 brief.</summary>
		public const string NuclearReleaseBannerLine = "1 kt available to both sides";

		/// <summary>The armed banner's second line. The caller formats the clock.</summary>
		// THE SAME PATTERN AS TransitionCause AND FOR THE SAME REASON: the first line says what
		// happened, the second says what the player may now DO and for how long. "reply or hold"
		// names BOTH options on purpose -- the ruling's strategic claim is that holding is often the
		// winning move -- where a line reading "reply now" would be the HUD telling the player to
		// take the escalation the whole design is trying to make a deliberate choice.
		//
		// THE SEPARATOR IS AN EM DASH AND NOT A MIDDLE DOT. The em dash is proven in this font at
		// this size -- DefconReadoutModel.NoClock and FinalExchangeBannerWidget both ship it, and the
		// demo's frame 03 exists to confirm it draws as a dash rather than a missing-glyph box. No
		// frame has ever confirmed U+00B7, so using one here would put an unverified glyph in the one
		// message the player has four seconds to read.
		public static string ArmedBannerLine(int band, string clock)
		{
			return $"{RungLabel(band)} available for {clock} — reply or hold";
		}
	}
}
