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
		// ==== WHY DEFCON 3's LINE STATES TWO RULES AND THE OTHER TWO STATE ONE ====
		// It read "The border is closed. Neither side may cross it." until 2026-09-19, and that was the
		// whole of the phase when the wording table was written. It is not any more: 5fef37dc
		// ("Positioning phase: no weapon fires, by any path", 2026-09-16) gated Armament.CanFire on the
		// level, so at DEFCON 3 NOTHING fires -- not autotarget, not an ordered attack, not force-fire
		// at bare ground (DefconFireDiscipline.PermitsWeapon). The copy predated the rule, so a player
		// who force-fired at anything got silence and no explanation anywhere on the HUD.
		//
		// The register is unchanged and is the constraint that shaped the rewrite: both clauses say
		// what the player MAY NOT DO, neither names a trait, and TheRuleLinesNameNoMechanism still
		// passes. It is deliberately not split into a second line -- the strip has one rule slot, and
		// the DEFCON 2 line already carries two clauses in one string for the same reason.
		public static string RuleLine(int level)
		{
			switch (level)
			{
				case 3: return "The border is closed. Nothing may cross it, and nothing may fire.";
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
		//
		// The qualifier now lives INSIDE HoldsFire (2026-09-19) rather than beside it, so this reads as
		// one call instead of restating the mode rule next to it. Stating it twice was harmless while
		// the two agreed and is exactly the shape that drifts: the readout would have been the only
		// site still correct if the predicate had been fixed and this had not.
		public static bool ShowsTrigger(DefconGameMode mode, int level)
		{
			return DefconFireDiscipline.HoldsFire(mode, level);
		}

		/// <summary>The compact label for a rung. Reached through <see cref="LedgerRungLabel"/>.</summary>
		// ==== "1kt" HERE AND "1 kt" IN RungLabel IS THE APPROVED DESIGN, NOT A DRIFT ====
		// Reviewed 2026-09-14 because the two forms are on screen TOGETHER -- the nuclear block's value
		// slot prints "1 kt" about twenty pixels above a ledger box printing "1kt" (frame 010 of
		// 260914_122039_p8182_demo-defcon-readout) -- and that reads as an inconsistency until you check
		// the mockup, which does exactly the same thing on purpose:
		//
		//     defcon-hud-directions.html:181   <span class="nnow">20 kt</span>          <- the value slot
		//     defcon-hud-directions.html:183-4 <span class="st">1kt</span> ... "200kt+"  <- the boxes
		//
		// The split is BOX vs PROSE, and it is consistent on both sides: every rung named inside a
		// bordered cell is compact, every rung named in a running sentence is spaced. RungLabel's
		// callers are all prose -- the value slot, both banner lines, the lobby dropdown, the
		// NuclearUnlockClock option descriptions -- and this one's only caller is the ledger box.
		//
		// IT IS NOT WIDTH THAT FORCES IT, which is the plausible-sounding reason to check first: a band
		// box is (Bounds.Width - 72) / 5 = 53px at the shipped 341, and "100kt" measures about 30px in
		// Tiny, so the space would fit five times over. Do not "fix" this toward one form on the
		// strength of the boxes being tight, because they are not.
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
		// THREE STATES SINCE v2, AND THE FOURTH WAS DELETED RATHER THAN LEFT UNREACHABLE. `Window`
		// drew a retaliation grant, which no longer exists; `Charging` used to be a per-BAND clock
		// and is now the side's one cooldown, so every box at or below the level shows it together.
		//
		// HELD AND CHARGING ARE THE SAME PERMISSION AND DIFFERENT FACTS, which is why both survive:
		// a side inside its cooldown holds every one of these bands and may fire none of them, and a
		// ledger that drew them lit would be telling a player they are covered when they are not.
		// That distinction is the entire value of putting the enemy's row on screen.
		public enum LedgerCell
		{
			/// <summary>Above this side's level. Not held at all.</summary>
			Dark,

			/// <summary>At or below the level, and the side is off cooldown: fireable now.</summary>
			Held,

			/// <summary>At or below the level, but the side is inside its cooldown.</summary>
			Charging
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

		/// <summary>What one band's box shows for one side. Rule 2, drawn.</summary>
		// TWO QUESTIONS AND THEY ARE ASKED IN THIS ORDER: does the side HOLD this band, and may it
		// fire ANYTHING. The first is per-box and the second is per-row, which is exactly why v2's
		// ledger carries one clock for the row instead of one per box -- every lit box on a row
		// shares a single answer to the second question.
		public static LedgerCell CellFor(NuclearRung band, int level, bool releaseOpen, bool onCooldown)
		{
			// BEFORE RELEASE EVERY BOX IS DARK on both rows, whatever the state underneath says.
			// Nothing has been handed out yet and the block's value slot is carrying RELEASE IN m:ss;
			// a lit box under a countdown to the moment boxes light would contradict it.
			if (!releaseOpen)
				return LedgerCell.Dark;

			if ((int)band > level)
				return LedgerCell.Dark;

			return onCooldown ? LedgerCell.Charging : LedgerCell.Held;
		}

		/// <summary>The clock in a ledger row's value slot: READY, or the side's cooldown as m:ss.</summary>
		// ONE CLOCK PER ROW, IN WORDS WHEN THERE IS NOTHING TO COUNT. v1 drew a small countdown inside
		// each band box and the player had up to ten of them on screen; under v2 all of a row's boxes
		// share one number, so drawing it ten times would be ten copies of the same fact competing
		// with the labels for the same 53 pixels.
		//
		// "READY" AND NOT AN EM DASH, which is the other convention on this panel (NoClock). The dash
		// means "this rung ends on an event rather than a clock" -- an ABSENCE of a countdown. Here
		// the countdown has RUN OUT, which is the opposite fact and the one the player is waiting for,
		// so it is worth a word. A row whose boxes are all dark gets neither: see the caller.
		public const string LedgerReady = "READY";

		/// <summary>The foot line under the ledger. THREE RULES, ONE SENTENCE EACH.</summary>
		// ALL THREE STATE WHAT MAY AND MAY NOT BE DONE, never the mechanism that does it -- the
		// mockup's own note is the specification and it is quoted in this file's header.
		//
		// THE SHUT LINE IS THE WHOLE REASON THIS BLOCK IS DRAWN BEFORE ANYTHING NUCLEAR CAN FIRE: a
		// ten-minute wait with nothing on screen explaining it is a player concluding the feature is
		// broken.
		//
		// THE OPEN LINES CHANGED TWICE AND BOTH OLD VERSIONS WERE LIES BY THE END. The first read
		// "Both sides are released to the same yield. Each use raises it." -- the shared pressure
		// ladder, where firing raised BOTH sides, so a player who fired to climb handed the climb to
		// their opponent instead. The second promised "You may answer one band up until the window
		// closes", and there is no window in v2. This pair says the two things a player actually has
		// to weigh: firing costs your TEAM its whole arsenal for a while, and it hands the enemy a
		// bigger one permanently.
		public static string NuclearFootLine(bool releaseOpen, bool onCooldown)
		{
			if (!releaseOpen)
				return "No warhead may be fired yet. Both sides are released at the same moment.";

			return onCooldown
				? "Your whole team is reloading. Firing again raises the enemy's level further."
				: "Firing puts your whole team on cooldown and raises the enemy's level.";
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
		// because all three happen in a 341-pixel panel in a corner: the gate opens, THEY are armed,
		// and a grant they never used runs out. Each gets a line here and a sound at the call site.

		/// <summary>The banner shown when the viewer's own side's LEVEL RISES.</summary>
		// "ESCALATED", NOT "ARMED", AND THE WORD IS THE RULING. v1's banner announced a retaliation
		// grant -- a thing you were given and had one minute to spend -- and "ARMED" was right for
		// that. v2 has no grant: the enemy fired, and your ceiling moved up permanently. "ESCALATED"
		// says the thing that actually happened, and says it about the MATCH rather than about a
		// weapon, which is what stops the banner reading as an instruction to use it.
		public const string ArmedBannerTitle = "ESCALATED";

		/// <summary>The banner shown when the release gate opens for both sides.</summary>
		public const string NuclearReleaseBannerTitle = "NUCLEAR RELEASE";

		/// <summary>Its second line. Verbatim from the 2026-09-13 brief.</summary>
		public const string NuclearReleaseBannerLine = "1 kt available to both sides";

		/// <summary>The escalation banner's second line: the band the rise just opened.</summary>
		// NO CLOCK AND NO "reply or hold", WHICH IS THE v2 CHANGE. This read "20 kt available for
		// 1:00 — reply or hold" and both halves were about a grant that expired. A level does not
		// expire, so there is no number to put here and nothing to hurry the player: the ruling's
		// strategic claim is that holding is often the winning move, and a banner that counted down
		// was the HUD arguing against it once every four seconds.
		//
		// IT NAMES THE NEW TOP BAND RATHER THAN LISTING EVERYTHING BELOW IT. A rise from 1 to 3
		// opens 20 kt and 50 kt; the ledger is on screen and shows both, and the banner has four
		// seconds and one line, so it says the biggest.
		public static string ArmedBannerLine(int band)
		{
			return $"{RungLabel(band)} now available";
		}
	}
}
