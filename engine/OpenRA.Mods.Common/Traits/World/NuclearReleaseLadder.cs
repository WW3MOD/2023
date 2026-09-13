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
 * THE YIELD BANDS, AND THE RELEASE GATE THAT OPENS THEM. Two things, both shared by every game mode,
 * and NOTHING ELSE -- the shared pressure ladder this file used to be is gone.
 *
 * ==== WHAT WAS DELETED ON 2026-09-13, AND WHY IT IS DELETED RATHER THAN TUNED ====
 * This class carried decision 06's ladder: ONE pressure value for the match, doubling per detonation
 * (0 -> 1 -> 3 -> 7 -> 15), with both sides always reading the same rung. Its known and accepted cost
 * was that GOING FIRST WAS FREE -- the firer was released exactly as far as the victim. It also kept
 * a per-firer detonation breakdown that nothing read, banked against a possible "asymmetric variant".
 *
 * The user's ruling of 2026-09-13 (manager-2b944571 decision 01) replaces all of it: firing is what
 * ARMS THE OTHER SIDE, each side carries its own permanent level, and a hit buys a time-boxed
 * retaliation grant one band up. A single shared counter cannot express any of that, so the pressure,
 * the doubling, the ceiling and the per-firer tally are removed rather than left inert. The asymmetric
 * variant is no longer a variant -- it is NuclearExchangeState, which is what the exchange now is.
 *
 * THE HOST CEILING WENT WITH IT. `nuclear-ceiling` is dropped by the same ruling ("No host ceiling"):
 * the top of the ladder is the game-ender band, and it is reachable only through a chain of deliberate
 * replies rather than by a number a host can set.
 *
 * ==== WHAT SURVIVES, AND WHY IT LIVES HERE RATHER THAN IN THE EXCHANGE ====
 * The BAND TABLE is the mod's shared vocabulary for "how big is this warhead", read by three
 * unrelated consumers: Escalation's exchange, Skirmish's NuclearUnlockClock, the readout's labels,
 * and PowerPurchaseWiringTest. It belongs to none of them.
 *
 * The RELEASE GATE is DefconEscalation's -- it is a clock hung off the DEFCON level, not off anything
 * the exchange knows -- so it stays owned and ticked there, and NuclearExchange polls its terminal
 * state. See the file header on NuclearExchange for why polling rather than notification.
 *
 * A plain class with no dependency on Actor, World or the lobby, for the reason DefconEscalationState,
 * NuclearExchangeState and NuclearUnlockSchedule all give in their own headers: nothing in
 * OpenRA.Test can construct a World, so arithmetic living inside a trait method is arithmetic
 * verified by reading only.
 *
 * ==== WHEN THE GATE OPENS: DEFCON 1, PLUS A DELAY (user ruling, 2026-09-10, unchanged) ====
 *     An Escalation match is at HOLD -- no nuclear weapon of any yield -- until DEFCON 1 has been
 *     reached AND a configurable delay has run from that moment.
 *
 * A DELAY OF 0 IS LEGAL and means "opens on the tick DEFCON 1 is reached". It is not a disabled
 * value: there is deliberately no setting that hands nuclear weapons to a match which has not reached
 * DEFCON 1, because such a setting would restore the pre-ruling behaviour by configuration.
 *
 * THE GATE IS ARMED BY THE LEVEL, NOT BY A TRANSITION. Tick is handed the current DEFCON level every
 * tick and starts counting the first time it sees the floor, however the match got there -- the
 * 3 -> 2 clock and then a casualty, or a lobby Start At of 1. An edge-triggered gate would never open
 * at all in the last of those cases, because there is no transition to observe.
 */

namespace OpenRA.Mods.Common.Traits
{
	// The rungs, as the user drew them on 2026-09-09 and with the 50/100 kt rung SPLIT IN TWO on the
	// ruling of 2026-09-10:
	//     HOLD -> 1 kt -> 20 kt -> 50 kt -> 100 kt -> 200 kt+
	// FIVE yield rungs above HOLD where there were four. The integer values are load-bearing -- every
	// level in NuclearExchangeState is one of these and the retaliation window is literally `band + 1`
	// -- so do not reorder them, and note that the split RENUMBERED GameEnder from 4 to 5. Nothing
	// persists a rung index across a build, so the renumber costs nothing; the one thing it does reach
	// is the Conditions dictionary in GrantConditionOnNuclearReleaseInfo, which is keyed on these
	// values and is a YAML-overridable field -- a map that hard-codes rung numbers there needs
	// re-reading.
	public enum NuclearRung
	{
		// Nothing nuclear is permitted: the state of every Escalation side before the release gate
		// opens, and of every side that has somehow been armed with nothing.
		//
		// IT IS NO LONGER ALSO A CEILING SETTING. `nuclear-ceiling` is dropped by the 2026-09-13
		// ruling, so "no nuclear weapons this match" is not expressible from the lobby any more.
		Hold = 0,

		// Sub-kiloton and kiloton tactical warheads: the 0.3 kt B61 dial and the 1 kt 9M729.
		Kiloton = 1,

		// The 10 kt and 20 kt rung -- the middle B61 dial, the Iskander, and `Atomic` itself.
		TwentyKiloton = 2,

		// 50 kt: the top B61 dial setting and the Kinzhal-N. Its own rung since 2026-09-10; these
		// two shared a rung with the 100 kt pair below until the split.
		FiftyKiloton = 3,

		// 100 kt: the W76 and the Kalibr. THE TOP OF WHAT A PERMANENT LEVEL CAN EVER REACH -- see
		// NuclearExchangeState's rule 5.
		HundredKiloton = 4,

		// "Game-enders are anything above ~200 kt" -- the Sarmat RV, the B83 and `AtomicHighYield`.
		// In Escalation this rung is reachable ONLY as a retaliation window grant, and firing one
		// begins the final exchange.
		GameEnder = 5
	}

	public static class NuclearReleaseLadder
	{
		public const int Lowest = (int)NuclearRung.Hold;
		public const int Highest = (int)NuclearRung.GameEnder;

		// THE BAND TABLE. A weapon's stated yield maps to the LOWEST rung that releases it, and these
		// four numbers are the ladder the user drew rather than anything derived.
		// Read the yields out of the weapon files, never from a power's name: `NukeRuKinzhalN` is
		// 50 kt and `NukeRuKalibr` is 100 kt, and SINCE THE 2026-09-10 SPLIT those are two different
		// rungs. They shared one before it, so that pair is exactly what a stale reading gets wrong.
		//
		// THE UNIT IS TONS OF TNT, NOT KILOTONS, and that is forced rather than chosen. The smallest
		// warhead in the mod is the B61-12's lowest dial setting at 0.3 kt, which is not an integer
		// number of kilotons -- in kilotons it truncates to ZERO, and a zero yield reads as "this
		// power is not nuclear", so the one weapon the bottom rung exists for would have reported no
		// launch at all. Tons are exact for every yield in the arsenal and 50 Mt is 5e7, well inside
		// int. Every comment in the weapon files still speaks kilotons; multiply by 1000.
		public const int KilotonBandCeilingTons = 1000;
		public const int TwentyKilotonBandCeilingTons = 20000;
		public const int FiftyKilotonBandCeilingTons = 50000;
		public const int HundredKilotonBandCeilingTons = 100000;

		// ABOVE THIS, NOTHING IN NORMAL PLAY -- on any rung, through any window.
		//
		// The user's ruling on the Tsar Bomba (decision 04): "mostly a gimmick... We keep it in code
		// but it should be disabled and cannot be used in game for now (keep it for sandbox)." It is
		// 50 Mt; the largest weapon that stays in play is AtomicHighYield at 6 Mt. 10000000 sits
		// between the two with an order of magnitude of margin either side, so this is a statement
		// about the gap and not a tuned edge -- a new 8 Mt weapon would be in play, a new 20 Mt one
		// would not.
		//
		// THE GATE THAT ACTUALLY SHIPS IS THE CONDITION, and always was:
		// GrantConditionOnNuclearReleaseInfo.UnrestrictedCondition is granted only outside Escalation,
		// and `MissileStrikePower@TsarBomba` is gated on it rather than on any band. This constant is
		// the second statement of that one rule -- the one a YAML edit cannot reach -- and it is read
		// by NuclearExchangeState.ReportLaunch, which refuses to let a weapon this size arm anybody.
		public const int SandboxOnlyAboveTons = 10000000;

		/// <summary>The lowest rung that releases a weapon of this yield in tons of TNT.</summary>
		public static int RungForYield(int tons)
		{
			if (tons <= 0)
				return Lowest;

			if (tons <= KilotonBandCeilingTons)
				return (int)NuclearRung.Kiloton;

			if (tons <= TwentyKilotonBandCeilingTons)
				return (int)NuclearRung.TwentyKiloton;

			if (tons <= FiftyKilotonBandCeilingTons)
				return (int)NuclearRung.FiftyKiloton;

			if (tons <= HundredKilotonBandCeilingTons)
				return (int)NuclearRung.HundredKiloton;

			return (int)NuclearRung.GameEnder;
		}
	}

	/// <summary>
	/// The release gate: an Escalation match holds no nuclear weapon of any yield until DEFCON 1 has
	/// been reached and a delay has run from that moment. Owned and ticked by
	/// <see cref="DefconEscalation"/>; see this file's header for the ruling behind it.
	/// </summary>
	public class NuclearReleaseGate
	{
		readonly DefconGameMode mode;
		readonly int releaseDelayTicks;

		// It runs only while the match is at DEFCON 1, so it is a countdown to release rather than a
		// match clock, and it reads as the full delay until then.
		int ticksUntilRelease;

		/// <summary>Whether the gate has opened at all. Until it has, every side is at Hold.</summary>
		public bool ReleaseOpen { get; private set; }

		/// <summary>Ticks left before release once the match is at DEFCON 1; the full delay before then.</summary>
		public int TicksUntilRelease => ticksUntilRelease;

		public NuclearReleaseGate(DefconGameMode mode, int releaseDelayTicks)
		{
			this.mode = mode;

			// Clamped rather than trusted. DefconEscalationInfo refuses a negative in RulesetLoaded,
			// but this class is constructible from a test and from any future caller, and a negative
			// delay reaching the decrement below would open the gate on the first tick at DEFCON 1 --
			// i.e. would silently become 0, which is the one behaviour a mistyped value must not get.
			this.releaseDelayTicks = releaseDelayTicks < 0 ? 0 : releaseDelayTicks;
			ticksUntilRelease = this.releaseDelayTicks;
		}

		/// <summary>One tick, given the match's current DEFCON level. True on the tick the gate opens.</summary>
		// Deterministic: integer arithmetic, no RNG, no wall-clock. Its one caller is
		// DefconEscalation.Tick, which runs on the World actor on every client on the same tick, so
		// every client opens the gate on the same tick.
		public bool Tick(int defconLevel)
		{
			if (mode != DefconGameMode.Escalation || ReleaseOpen)
				return false;

			// NOT AT DEFCON 1 YET, so the countdown does not run. The delay is therefore time spent AT
			// the bottom level, not time since the match began -- a match that takes fifteen minutes
			// to reach DEFCON 1 still owes the full delay when it gets there.
			if (defconLevel != DefconEscalationState.Floor)
				return false;

			// A delay of 0 falls straight through to the open below, which is the ruling's "0 opens
			// immediately". Otherwise this is the decrement idiom DefconEscalationState.Tick uses, so
			// the gate opens on the delay'th tick at DEFCON 1 rather than one tick either side.
			if (ticksUntilRelease > 0 && --ticksUntilRelease > 0)
				return false;

			ReleaseOpen = true;
			return true;
		}
	}
}
