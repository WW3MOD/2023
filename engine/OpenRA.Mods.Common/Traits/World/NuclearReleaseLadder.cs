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
 * THE NUCLEAR RELEASE LADDER -- one shared pressure counter, doubling per detonation, deciding
 * which yields both sides are permitted.
 *
 * A plain class with no dependency on Actor, World or the lobby, for exactly the reason
 * DefconEscalationState is one: nothing in OpenRA.Test can construct a World, so arithmetic living
 * inside a trait method is arithmetic verified by reading only. The doubling, the rung boundaries,
 * the ceiling and the band table are all here, and DefconEscalation does nothing to them that this
 * class does not do.
 *
 * ==== THE RULE, from the user's ruling of 2026-09-09 (decision 06) ====
 * "Every detonation adds to one pressure value, doubling per use, and both sides always read the
 * same rung. Whoever fires, both climb together and are permitted exactly the same yields."
 *
 * So Pressure is ONE number for the match, not one per player, and RungFor ignores which player is
 * asking. The known and accepted cost is that GOING FIRST IS FREE: the firer is released exactly as
 * far as the victim. That was argued and settled; do not "fix" it here.
 *
 * ==== THE DOUBLING, WHICH IS LITERAL ====
 *      Pressure = Pressure * 2 + 1        0 -> 1 -> 3 -> 7 -> 15 -> 31
 * After n detonations Pressure is 2^n - 1, so the n'th detonation adds 2^(n-1) -- each one worth as
 * much as everything before it put together, which is what "doubling per use" means. The rung is
 * then the number of detonations, recovered as the count of low set bits, and the +1 is what makes
 * that recovery exact rather than a logarithm with rounding in it. All integer, no floating point
 * anywhere: see the determinism note on DefconEscalation.ReportNuclearRelease.
 *
 * PRESSURE SATURATES rather than overflowing. Ceiling is at most GameEnder = rung 4, so a match is
 * pinned four detonations in and the counter stops moving; the guard below is what stops a long
 * Sandbox session shifting an int past 2^31.
 *
 * ==== WHY THE ASYMMETRIC VARIANT IS STILL LAYERABLE ====
 * Decision 06 keeps the rejected alternative alive on purpose -- "firing releases the OPPONENT one
 * rung further than yourself" -- and says it must remain reachable without undoing this work. Two
 * deliberate shapes here buy that, and neither costs anything today:
 *
 *   1. ReportDetonation TAKES THE FIRER and stores the count against it, even though the shared
 *      ladder never reads the breakdown. The information the asymmetric variant needs is therefore
 *      already being recorded rather than thrown away at the call site.
 *   2. Every consumer asks RungFor(player), not a bare Rung property. Today every player gets the
 *      same answer by construction. The asymmetric variant is then a change to the BODY of RungFor
 *      and to nothing else -- no call site, no trait, no YAML.
 *
 * ==== WHAT THIS CLASS DELIBERATELY DOES NOT DECIDE ====
 * When the ladder OPENS. HOLD is modelled as a CEILING value ("no nuclear weapons this match"),
 * not as a starting rung that something has to unlock -- because nothing in decisions 04 or 06 says
 * what would unlock it, and a start-at-HOLD ladder whose only mover is a detonation can never be
 * climbed at all. Binding HOLD to DEFCON 1 is the obvious candidate and is one line in
 * DefconEscalation; it is called out in the report rather than guessed at here.
 */

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	// The rungs of the release ladder, in the order the user drew them on 2026-09-09:
	//     HOLD -> 1 kt -> 20 kt -> 50-100 kt -> 200 kt+
	// The integer values are load-bearing -- Rung is a [Sync] int projection on DefconEscalation and
	// the ladder climbs by incrementing -- so do not reorder them.
	public enum NuclearRung
	{
		// Nothing nuclear is permitted. Reachable only as a CEILING setting; see the file header.
		Hold = 0,

		// Sub-kiloton and kiloton tactical warheads: the 0.3 kt B61 dial and the 1 kt 9M729.
		Kiloton = 1,

		// The 10 kt and 20 kt rung -- the middle B61 dial, the Iskander, and `Atomic` itself.
		TwentyKiloton = 2,

		// "50-100 kt" as drawn: the top B61 dial, the Kinzhal-N, the W76 and the Kalibr.
		HundredKiloton = 3,

		// "Game-enders are anything above ~200 kt" -- the Sarmat RV, the B83 and `AtomicHighYield`.
		GameEnder = 4
	}

	public class NuclearReleaseLadder
	{
		public const int Lowest = (int)NuclearRung.Hold;
		public const int Highest = (int)NuclearRung.GameEnder;

		// THE BAND TABLE. A weapon's stated yield maps to the LOWEST rung that releases it, and
		// these three numbers are the ladder the user drew rather than anything derived.
		// Read the yields out of the weapon files, never from a power's name: `NukeRuKinzhalN` is
		// 50 kt and `NukeRuKalibr` is 100 kt, and both sit on the same rung.
		//
		// THE UNIT IS TONS OF TNT, NOT KILOTONS, and that is forced rather than chosen. The smallest
		// warhead in the mod is the B61-12's lowest dial setting at 0.3 kt, which is not an integer
		// number of kilotons -- in kilotons it truncates to ZERO, and a zero yield reads as "this
		// power is not nuclear", so the one weapon the bottom rung exists for would have reported no
		// detonation at all. Tons are exact for every yield in the arsenal and 50 Mt is 5e7, well
		// inside int. Every comment in the weapon files still speaks kilotons; multiply by 1000.
		public const int KilotonBandCeilingTons = 1000;
		public const int TwentyKilotonBandCeilingTons = 20000;
		public const int HundredKilotonBandCeilingTons = 100000;

		// ABOVE THIS, NOTHING IN NORMAL PLAY -- at any ceiling setting, on any rung.
		//
		// The user's ruling on the Tsar Bomba (decision 04): "mostly a gimmick... We keep it in code
		// but it should be disabled and cannot be used in game for now (keep it for sandbox)." It is
		// 50 Mt; the largest weapon that stays in play is AtomicHighYield at 6 Mt. 10000 sits between
		// the two with an order of magnitude of margin either side, so this is a statement about the
		// gap and not a tuned edge -- a new 8 Mt weapon would be in play, a new 20 Mt one would not.
		public const int SandboxOnlyAboveTons = 10000000;

		readonly DefconGameMode mode;
		readonly int ceiling;

		// Per-firer detonation counts. THE SHARED LADDER NEVER READS THIS -- RungFor returns one
		// number for everybody. It is recorded because the asymmetric variant decision 06 keeps alive
		// needs exactly this breakdown, and a call site that never passed the firer could not be
		// upgraded to it later without touching every caller. See the file header.
		readonly Dictionary<string, int> detonationsByFirer = new Dictionary<string, int>();

		// The one shared pressure value. 2^n - 1 after n detonations.
		public int Pressure { get; private set; }

		// Detonations counted, which is also the rung index before the ceiling is applied.
		public int Detonations { get; private set; }

		public NuclearReleaseLadder(DefconGameMode mode, int ceiling)
		{
			this.mode = mode;
			this.ceiling = ceiling < Lowest ? Lowest : (ceiling > Highest ? Highest : ceiling);
		}

		// The ceiling actually in force, after the mode has had its say.
		public int Ceiling => mode == DefconGameMode.Escalation ? ceiling : Highest;

		/// <summary>The rung this player is permitted to fire from.</summary>
		// ONE NUMBER FOR EVERYBODY, and the parameter is ignored on purpose -- see the file header
		// for why it is taken anyway. `player` is an InternalName rather than a Player so that this
		// class stays constructible in a test.
		//
		// SKIRMISH AND SANDBOX ARE BOTH FULLY RELEASED, for two different reasons. Skirmish is
		// required to be a STRICT NO-OP: it is the shipped default game mode, the user tests from
		// main, and every nuclear demo scenario in tools/autotest/scenarios runs in it -- so a
		// Skirmish match must permit exactly what it permitted before this file existed. Sandbox is
		// released because decision 04 puts the Tsar Bomba there and nowhere else.
		public int RungFor(string player)
		{
			if (mode != DefconGameMode.Escalation)
				return Highest;

			var rung = Detonations;
			return rung > Ceiling ? Ceiling : rung;
		}

		/// <summary>Whether a weapon of this yield may be fired by this player right now.</summary>
		public bool Permits(string player, int tons)
		{
			// THE TSAR BOMBA GATE, checked BEFORE the rung so that no ceiling setting and no number
			// of detonations can ever reach it.
			//
			// THE TEST IS "NOT ESCALATION" RATHER THAN "IS SANDBOX", and that distinction is a bug
			// this class shipped for one test run. Written `mode == Sandbox` it refused the weapon
			// in SKIRMISH as well -- Skirmish being the shipped default game mode and the one
			// demo-nuke-arsenal runs in, so that demo's 50 Mt shot would have stopped firing with
			// no error anywhere, which is precisely the failure the strict-no-op rule exists to
			// prevent.
			//
			// It must also agree with GrantConditionOnNuclearReleaseInfo.UnrestrictedCondition,
			// which is the gate that actually ships in YAML and is granted in exactly these modes.
			// Two statements of one rule is how they drift apart; keep them phrased alike.
			if (tons > SandboxOnlyAboveTons)
				return mode != DefconGameMode.Escalation;

			return RungForYield(tons) <= RungFor(player);
		}

		/// <summary>The lowest rung that releases a weapon of this yield in tons of TNT.</summary>
		public static int RungForYield(int tons)
		{
			if (tons <= 0)
				return Lowest;

			if (tons <= KilotonBandCeilingTons)
				return (int)NuclearRung.Kiloton;

			if (tons <= TwentyKilotonBandCeilingTons)
				return (int)NuclearRung.TwentyKiloton;

			if (tons <= HundredKilotonBandCeilingTons)
				return (int)NuclearRung.HundredKiloton;

			return (int)NuclearRung.GameEnder;
		}

		/// <summary>A nuclear weapon has been released. Returns true if the rung actually moved.</summary>
		// The firer is recorded and then ignored, which is the shared ladder's whole content.
		public bool ReportDetonation(string firer, int tons)
		{
			// Skirmish and Sandbox never climb. Skirmish because it must not change a tick, Sandbox
			// because it is pinned wide open and there is nothing above it to climb to.
			if (mode != DefconGameMode.Escalation)
				return false;

			if (firer != null)
				detonationsByFirer[firer] = DetonationsBy(firer) + 1;

			var before = RungFor(firer);

			// Saturate rather than overflow. Once the pressure is past what the top rung needs, the
			// counter has said everything it can say and further doubling is arithmetic nobody reads.
			if (Detonations <= Highest)
			{
				Detonations++;
				Pressure = (Pressure * 2) + 1;
			}

			return RungFor(firer) != before;
		}

		/// <summary>Detonations attributed to one player. For the asymmetric variant; unused today.</summary>
		public int DetonationsBy(string firer)
		{
			if (firer == null || !detonationsByFirer.TryGetValue(firer, out var count))
				return 0;

			return count;
		}
	}
}
