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
 * ==== WHEN THE LADDER OPENS: DEFCON 1, PLUS A DELAY (user ruling, 2026-09-10) ====
 * This is the question the first version of this file deliberately left open, and leaving it open
 * was a deadlock in waiting: the ladder had to START at 1 kt, because the only thing that moves it
 * is a detonation and a ladder opening at HOLD could never be climbed at all. The ruling closes it.
 *
 *     An Escalation match is at HOLD -- no nuclear weapon of any yield -- until DEFCON 1 has been
 *     reached AND a configurable delay has run from that moment. The ladder then opens at StartRung
 *     and climbs on detonations exactly as it did before.
 *
 * So HOLD stops being an absorbing state: what leaves it is a CLOCK rather than a detonation, which
 * is what makes the ladder as drawn climbable. HOLD is still a CEILING value as well ("no nuclear
 * weapons this match"), and the two uses do not interact -- a HOLD ceiling pins the ladder shut
 * whatever the gate does, and the gate refuses everything whatever the ceiling is.
 *
 * A DELAY OF 0 IS LEGAL and means "opens on the tick DEFCON 1 is reached". It is not a disabled
 * value: there is deliberately no setting that hands nuclear weapons to a match which has not
 * reached DEFCON 1, because such a setting would restore the pre-ruling behaviour by configuration.
 *
 * THE GATE IS ARMED BY THE LEVEL, NOT BY A TRANSITION. Tick is handed the current DEFCON level every
 * tick and starts counting the first time it sees the floor, however the match got there -- the
 * 3 -> 2 clock and then a casualty, or a lobby Start At of 1. An edge-triggered gate would never
 * open at all in the last of those cases, because there is no transition to observe.
 */

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	// The rungs of the release ladder, as the user drew them on 2026-09-09 and with the 50/100 kt
	// rung SPLIT IN TWO on the ruling of 2026-09-10:
	//     HOLD -> 1 kt -> 20 kt -> 50 kt -> 100 kt -> 200 kt+
	// FIVE yield rungs above HOLD where there were four. The integer values are load-bearing -- Rung
	// is a [Sync] int projection on DefconEscalation and the ladder climbs by incrementing -- so do
	// not reorder them, and note that the split RENUMBERED GameEnder from 4 to 5. Nothing persists a
	// rung index across a build, so the renumber costs nothing; the one thing it does reach is the
	// Conditions dictionary in GrantConditionOnNuclearReleaseInfo, which is keyed on these values
	// and is a YAML-overridable field -- a map that hard-codes rung numbers there needs re-reading.
	public enum NuclearRung
	{
		// Nothing nuclear is permitted. TWO DIFFERENT WAYS TO BE HERE, and they are separate
		// mechanisms: as the match's CEILING setting ("no nuclear weapons this match"), and as the
		// state of every Escalation match before the release gate opens. See the file header.
		Hold = 0,

		// Sub-kiloton and kiloton tactical warheads: the 0.3 kt B61 dial and the 1 kt 9M729.
		Kiloton = 1,

		// The 10 kt and 20 kt rung -- the middle B61 dial, the Iskander, and `Atomic` itself.
		TwentyKiloton = 2,

		// 50 kt: the top B61 dial setting and the Kinzhal-N. Its own rung since 2026-09-10; these
		// two shared a rung with the 100 kt pair below until the split.
		FiftyKiloton = 3,

		// 100 kt: the W76 and the Kalibr.
		HundredKiloton = 4,

		// "Game-enders are anything above ~200 kt" -- the Sarmat RV, the B83 and `AtomicHighYield`.
		GameEnder = 5
	}

	public class NuclearReleaseLadder
	{
		public const int Lowest = (int)NuclearRung.Hold;
		public const int Highest = (int)NuclearRung.GameEnder;

		// THE BAND TABLE. A weapon's stated yield maps to the LOWEST rung that releases it, and
		// these four numbers are the ladder the user drew rather than anything derived.
		// Read the yields out of the weapon files, never from a power's name: `NukeRuKinzhalN` is
		// 50 kt and `NukeRuKalibr` is 100 kt, and SINCE THE 2026-09-10 SPLIT those are two different
		// rungs. They shared one before it, so that pair is exactly what a stale reading gets wrong.
		//
		// THE UNIT IS TONS OF TNT, NOT KILOTONS, and that is forced rather than chosen. The smallest
		// warhead in the mod is the B61-12's lowest dial setting at 0.3 kt, which is not an integer
		// number of kilotons -- in kilotons it truncates to ZERO, and a zero yield reads as "this
		// power is not nuclear", so the one weapon the bottom rung exists for would have reported no
		// detonation at all. Tons are exact for every yield in the arsenal and 50 Mt is 5e7, well
		// inside int. Every comment in the weapon files still speaks kilotons; multiply by 1000.
		public const int KilotonBandCeilingTons = 1000;
		public const int TwentyKilotonBandCeilingTons = 20000;
		public const int FiftyKilotonBandCeilingTons = 50000;
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
		readonly int startRung;
		readonly int releaseDelayTicks;

		// THE RELEASE GATE'S COUNTDOWN. It runs only while the match is at DEFCON 1, so it is a
		// countdown to release rather than a match clock, and it reads as the full delay until then.
		int ticksUntilRelease;

		// Per-firer detonation counts. THE SHARED LADDER NEVER READS THIS -- RungFor returns one
		// number for everybody. It is recorded because the asymmetric variant decision 06 keeps alive
		// needs exactly this breakdown, and a call site that never passed the firer could not be
		// upgraded to it later without touching every caller. See the file header.
		readonly Dictionary<string, int> detonationsByFirer = new Dictionary<string, int>();

		// The one shared pressure value. 2^n - 1 after n detonations.
		public int Pressure { get; private set; }

		// Detonations counted, which is also the rung index before the ceiling is applied.
		public int Detonations { get; private set; }

		/// <summary>Whether the ladder has opened at all. Until it has, every yield is refused.</summary>
		public bool ReleaseOpen { get; private set; }

		/// <summary>Ticks left before release once the match is at DEFCON 1; the full delay before then.</summary>
		public int TicksUntilRelease => ticksUntilRelease;

		// THE TWO-ARGUMENT FORM STILL HAS A GATE -- it takes a delay of ZERO, which opens on the tick
		// DEFCON 1 is reached, not a match that starts open. There is no constructor that skips the
		// gate, deliberately: see the file header on why 0 is a legal delay and not a disable.
		public NuclearReleaseLadder(DefconGameMode mode, int ceiling)
			: this(mode, ceiling, (int)NuclearRung.Kiloton, 0) { }

		public NuclearReleaseLadder(DefconGameMode mode, int ceiling, int startRung, int releaseDelayTicks)
		{
			this.mode = mode;
			this.ceiling = Clamp(ceiling);
			this.startRung = Clamp(startRung);

			// Clamped rather than trusted. DefconEscalationInfo refuses a negative in RulesetLoaded,
			// but this class is constructible from a test and from any future caller, and a negative
			// delay reaching the decrement below would open the gate on the first tick at DEFCON 1 --
			// i.e. would silently become 0, which is the one behaviour a mistyped value must not get.
			this.releaseDelayTicks = releaseDelayTicks < 0 ? 0 : releaseDelayTicks;
			ticksUntilRelease = this.releaseDelayTicks;
		}

		static int Clamp(int rung)
		{
			return rung < Lowest ? Lowest : (rung > Highest ? Highest : rung);
		}

		/// <summary>The rung an Escalation match opens at, before any detonation.</summary>
		public int StartRung => startRung;

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

			// THE RELEASE GATE, checked before anything else this class knows. Until DEFCON 1 has
			// been reached and the delay has run, an Escalation match is at HOLD and no warhead of
			// any yield is permitted. Ruling of 2026-09-10; see the file header.
			if (!ReleaseOpen)
				return Lowest;

			// ONCE OPEN the ladder sits at startRung rather than at HOLD, which is forced rather than
			// chosen. Decision 06's accepted cost is that "going first is free" -- so firing has to
			// be possible before anyone has fired. A ladder that OPENED at HOLD could never be
			// climbed: the only thing that moves it from here is a detonation, and at HOLD there is
			// no warhead any player is permitted to detonate. The gate above is a clock, which is
			// exactly why it can leave HOLD when a detonation could not.
			var rung = startRung + Detonations;
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

			if (tons <= FiftyKilotonBandCeilingTons)
				return (int)NuclearRung.FiftyKiloton;

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

			// A DETONATION BEFORE THE GATE OPENS DOES NOT COUNT, and is dropped whole -- no pressure,
			// no per-firer tally. Nothing a player can click reaches here while the ladder is shut,
			// because every nuclear power is gated on a band condition that is not granted; but a Lua
			// scenario or a bot calling ReportNuclearRelease directly can, and banking those would
			// let a match arrive at DEFCON 1 with the ladder already part-climbed.
			if (!ReleaseOpen)
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

		/// <summary>One tick, given the match's current DEFCON level. True on the tick the ladder opens.</summary>
		// THE ONLY THING THAT OPENS THE LADDER. It takes the LEVEL rather than a "DEFCON 1 has
		// happened" edge so that a match configured to START at DEFCON 1 arms on its first tick:
		// there is no transition to observe in that case, and an edge-triggered gate would leave
		// such a match at HOLD forever -- which is the exact deadlock this ruling removed.
		//
		// Deterministic: integer arithmetic, no RNG, no wall-clock. Its one caller is
		// DefconEscalation.Tick, which runs on the World actor on every client on the same tick, so
		// every client opens the gate on the same tick. See ReportNuclearRelease's determinism note.
		public bool Tick(int defconLevel)
		{
			if (mode != DefconGameMode.Escalation || ReleaseOpen)
				return false;

			// NOT AT DEFCON 1 YET, so the countdown does not run. The delay is therefore time spent
			// AT the bottom level, not time since the match began -- a match that takes fifteen
			// minutes to reach DEFCON 1 still owes the full delay when it gets there.
			if (defconLevel != DefconEscalationState.Floor)
				return false;

			// A delay of 0 falls straight through to the open below, which is the ruling's "0 opens
			// immediately". Otherwise this is the decrement idiom DefconEscalationState.Tick uses, so
			// the ladder opens on the delay'th tick at DEFCON 1 rather than one tick either side.
			if (ticksUntilRelease > 0 && --ticksUntilRelease > 0)
				return false;

			ReleaseOpen = true;
			return true;
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
