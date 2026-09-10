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
 * The identity to check any of this against is DefconEscalationInfo.StandardTicks: 5000 ticks = 300 s
 * = 5:00. TheTickRateIdentityHolds asserts exactly that, so a machine that ever disagrees says so in
 * the test run rather than in a countdown nobody times with a stopwatch.
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

		/// <summary>The rung labels, lowest first -- one per <see cref="NuclearRung"/>, in enum order.</summary>
		// DERIVED FROM THE ENUM RATHER THAN LISTED, because the ladder has already changed shape once:
		// the 2026-09-10 ruling split 50/100 kt into two rungs and RENUMBERED GameEnder from 4 to 5. A
		// hard-coded list of steps would have kept drawing four boxes with the labels one rung out of
		// step, which is a readout that lies rather than one that fails. If a rung is ever added
		// without a label here, StepsCoverEveryRung fails the build's tests.
		public static IReadOnlyList<string> Steps()
		{
			var values = (NuclearRung[])Enum.GetValues(typeof(NuclearRung));
			var steps = new string[values.Length];
			for (var i = 0; i < values.Length; i++)
				steps[i] = ShortRungLabel(values[i]);

			return steps;
		}

		/// <summary>The compact label drawn inside a step box.</summary>
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
		// SHUT AND OPEN ARE DIFFERENT SENTENCES because they are different rules, and the shut one is
		// the whole reason this block is drawn before anything nuclear can be fired: a ten-minute wait
		// with nothing on screen explaining it is a player concluding the feature is broken. Both
		// state what may and may not be done, never the gate that does it.
		public static string NuclearFootLine(bool releaseOpen)
		{
			return releaseOpen
				? "Both sides are released to the same yield. Each use raises it."
				: "No warhead may be fired yet. Both sides are released at the same moment.";
		}

		/// <summary>Is the nuclear release readout drawn?</summary>
		// THREE CONDITIONS, and each removes a case where the block would be noise or a lie:
		//   - Escalation only. The ladder is pinned wide open in Skirmish and Sandbox
		//     (NuclearReleaseLadder.RungFor returns Highest for both), so there is no ladder to draw.
		//   - A HOLD ceiling is the host saying "no nuclear weapons this match". Nothing will ever
		//     move, so the block would be a permanently empty promise.
		//   - Before DEFCON 1 the gate's countdown is not running at all -- NuclearReleaseLadder.Tick
		//     returns early at any other level and TicksUntilRelease reads as the full delay. Drawing
		//     a frozen clock is worse than drawing none, and the DEFCON strip is carrying the phase.
		public static bool ShowsNuclear(DefconGameMode mode, int level, int ceilingRung, bool releaseOpen)
		{
			if (mode != DefconGameMode.Escalation || ceilingRung <= (int)NuclearRung.Hold)
				return false;

			return releaseOpen || level == DefconEscalationState.Floor;
		}
	}
}
