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
 * THE NUCLEAR EXCHANGE -- Escalation's replacement for the shared pressure ladder, as the user
 * ruled it on 2026-09-13 (manager-2b944571 decision 01). Two things per SIDE and nothing else:
 *
 *     PermanentLevel  -- the highest band this side may fire freely, on cooldown. Never falls.
 *     WindowLevel + WindowTicksRemaining -- a RETALIATION GRANT one band above what this side was
 *                        last hit with, valid for a window and then gone.
 *
 * A plain class with no dependency on Actor, World or the lobby, for exactly the reason
 * DefconEscalationState, NuclearReleaseLadder and NuclearUnlockSchedule are plain classes and each
 * says so in its own header: nothing in OpenRA.Test can construct a World, so arithmetic living
 * inside a trait method is arithmetic verified by reading only.
 *
 * ==== WHAT THIS SUPERSEDES, AND WHY THE OLD SHAPE IS GONE RATHER THAN TUNED ====
 * Decision 06's ladder was ONE shared counter that both sides read, so "going first is free" was
 * its known and accepted cost: the firer was released exactly as far as the victim. The 2026-09-13
 * ruling inverts that -- firing is what ARMS THE OTHER SIDE and nothing else does -- so a shared
 * counter cannot express it. The pressure value, its doubling and the per-firer breakdown kept for
 * the "asymmetric variant" are all deleted rather than left inert; the asymmetric variant is what
 * this file IS.
 *
 * ==== THE RULES, IN THE ORDER THEY FIRE ====
 *  1. RELEASE. Unchanged, and still owned by NuclearReleaseLadder's gate on DefconEscalation: a
 *     countdown runs from DEFCON 1 and on expiry BOTH sides' PermanentLevel becomes Kiloton. Before
 *     that every side is at Hold and no warhead of any yield is permitted, so rule 2 cannot fire.
 *  2. ANY LAUNCH ARMS THE OTHER SIDE. Side A fires band Y, and for every other side B:
 *         B.PermanentLevel = max(B.PermanentLevel, Y)        -- parity in kind, permanently
 *         B.WindowLevel    = max(B.WindowLevel, Y + 1)       -- one band up, for a window
 *         B.WindowTicksRemaining = the full window           -- RESTARTED on every hit
 *     Wherever it lands. There is no damage attribution and no demonstration shot; the ruling
 *     rejected decision 14's 10 % rule outright.
 *  3. THE WINDOW LAPSES. At zero the grant is GONE and the side is back at its PermanentLevel.
 *     There are no indefinite grants -- which is the whole reason a held apocalypse cannot stall
 *     the match.
 *  4. FIRING INSIDE THE WINDOW is just rule 2 again with a bigger Y, so replying at Y+1 arms the
 *     other side permanently at Y+1 and opens their window at Y+2. Firing AT OR BELOW one's own
 *     permanent level arms the other side only by the band actually fired: FIRING SMALL DOES NOT
 *     ESCALATE MUCH, which is what makes the winner's correct play restraint.
 *  5. GAME-ENDERS ARE REACHABLE ONLY AS A WINDOW GRANT. Nothing ever writes GameEnder into a
 *     PermanentLevel -- rule 2's permanent raise is capped one band below the top, EXPLICITLY and
 *     not as a consequence of the other rules (see the comment at the cap; writing it as a
 *     consequence was a bug the fixture caught). Firing one begins the final exchange, which is the
 *     trait's business rather than this class's.
 *
 * ==== WHY A WINDOW IS TIME-BOXED PERMISSION AND NOT A STOCKED SHOT ====
 * The ruling is explicit that "bookkeeping is cooldown-unlock, not stock", superseding decisions
 * 13/14/20. So a window does not hold a COUNT: while it is open the side may fire that band as
 * often as its cooldown allows, and when it closes the permission evaporates whether or not it was
 * used. That is also why a repeat hit at the same Y restarts the window rather than banking a
 * second one -- there is nothing to bank.
 *
 * ==== DETERMINISM ====
 * Integer arithmetic throughout, no RNG, no wall-clock, no floating point. Side keys are supplied
 * by the caller and iterated in REGISTRATION order (sideKeys, a List) rather than in Dictionary
 * order, so every enumeration this class exposes is identical on every client.
 */

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// How hard this match's nuclear cooldowns bite. Named for the real doctrines, and the ORDER is
	/// slowest-to-fastest rather than alphabetical.
	/// </summary>
	public enum NuclearPosture
	{
		/// <summary>Limited War: the longest waits, so an exchange is a handful of deliberate shots.</summary>
		Limited = 0,

		/// <summary>Flexible Response: the shipped cooldowns, unscaled. The default.</summary>
		Flexible = 1,

		/// <summary>Massive Retaliation: the shortest waits, so a spiral runs to its end quickly.</summary>
		Massive = 2
	}

	/// <summary>The posture multiplier, as a percentage of a power's shipped ChargeInterval.</summary>
	// UNTUNED PLACEHOLDERS. Nobody has played this mode; 150/100/60 are round numbers either side of
	// "as shipped" and are the user's brief rather than a measurement.
	//
	// AND THEY CURRENTLY SCALE NOTHING ON THE SHIPPED ARSENAL, which is a fact about the mod rather
	// than about this code: every nuclear power in mods/ww3mod/rules/ingame/nuclear-arsenal.yaml sets
	// RequiresPurchase: True (:108, :158, :199, :257, :315, :367, :458, :504, :552, :611), and
	// SupportPowerInstance forces TotalTicks to 0 for a purchased power (SupportPowerManager.cs:229),
	// so there is no interval for a multiplier to multiply. This is built against ChargeInterval
	// anyway so that it is correct on the day a nuclear power carries one; see
	// SupportPowerInstance's constructor for where it is applied.
	public static class NuclearPostureScale
	{
		public const int LimitedPercent = 150;
		public const int FlexiblePercent = 100;
		public const int MassivePercent = 60;

		public static int Percent(NuclearPosture posture)
		{
			switch (posture)
			{
				case NuclearPosture.Limited: return LimitedPercent;
				case NuclearPosture.Massive: return MassivePercent;
				default: return FlexiblePercent;
			}
		}

		/// <summary>Scale a tick count by the posture. Multiplies BEFORE dividing, so 60 % is exact.</summary>
		// The idiom is NuclearUnlockSchedule.TicksForMinutes's, for its reason: `ticks * (pct/100)`
		// would evaluate the parenthesis in integer arithmetic and yield 0 or 1, which is not a
		// rounding error but a total loss of the value.
		public static int Apply(int ticks, NuclearPosture posture)
		{
			if (ticks <= 0)
				return 0;

			return ticks * Percent(posture) / 100;
		}
	}

	/// <summary>What one call to <see cref="NuclearExchangeState.ReportLaunch"/> did.</summary>
	public readonly struct NuclearLaunchOutcome
	{
		/// <summary>False when the launch was dropped whole -- wrong mode, before release, or not nuclear.</summary>
		public readonly bool Counted;

		/// <summary>The band that was fired, as a <see cref="NuclearRung"/> value.</summary>
		public readonly int Band;

		/// <summary>A game-ender was released: the match ends, and the trait is what begins it.</summary>
		public readonly bool FinalExchange;

		public NuclearLaunchOutcome(bool counted, int band, bool finalExchange)
		{
			Counted = counted;
			Band = band;
			FinalExchange = finalExchange;
		}

		public static readonly NuclearLaunchOutcome Dropped = new NuclearLaunchOutcome(false, (int)NuclearRung.Hold, false);
	}

	public class NuclearExchangeState
	{
		/// <summary>One side's whole position. A side is a TEAM, or a player with no team.</summary>
		public sealed class SideState
		{
			/// <summary>The highest band this side may fire freely. Never falls.</summary>
			public int PermanentLevel = (int)NuclearRung.Hold;

			/// <summary>The band this side's retaliation window grants. Meaningless while the window is shut.</summary>
			public int WindowLevel = (int)NuclearRung.Hold;

			/// <summary>Ticks of window left. 0 is shut.</summary>
			public int WindowTicksRemaining;

			/// <summary>
			/// Bumped every time the window is OPENED OR RESTARTED. A consumer that has to act once
			/// per grant -- making the granted tier fire-ready, say -- watches this rather than
			/// watching WindowTicksRemaining, which cannot distinguish "restarted on the same band"
			/// from "not yet ticked".
			/// </summary>
			public int WindowSerial;
		}

		readonly DefconGameMode mode;
		readonly int retaliationWindowTicks;

		// REGISTRATION ORDER, not Dictionary order. Every enumeration this class exposes walks this
		// list, so the order is identical on every client and in every test. See the header.
		readonly List<int> sideKeys = new List<int>();
		readonly Dictionary<int, SideState> sides = new Dictionary<int, SideState>();

		/// <summary>Whether the release gate has opened. Until it has, every side is at Hold.</summary>
		public bool Released { get; private set; }

		public NuclearExchangeState(DefconGameMode mode, int retaliationWindowTicks)
		{
			this.mode = mode;

			// Clamped rather than trusted, for NuclearReleaseLadder's reason: the Info refuses a
			// non-positive value, but this class is constructible from a test and from any future
			// caller, and a negative window would make every grant lapse on the tick it opened.
			this.retaliationWindowTicks = retaliationWindowTicks < 0 ? 0 : retaliationWindowTicks;
		}

		/// <summary>The window length this state was built with, in ticks.</summary>
		public int RetaliationWindowTicks => retaliationWindowTicks;

		/// <summary>Sides, in registration order.</summary>
		public IReadOnlyList<int> Sides => sideKeys;

		/// <summary>
		/// <para>Is a player one of the match's SIDES? Two booleans, and the one that is deliberately
		/// NOT here is the thing worth reading this for.</para>
		///
		/// <para>`Playable` IS NOT PART OF THE TEST, and a first version of this rule had it and was
		/// wrong. <c>PlayerReference.Playable</c> defaults to FALSE (`PlayerReference.cs:24`) and says
		/// only "is this a slot the lobby offers", so requiring it silently drops every map-authored
		/// combatant — a scripted enemy in a mission, or either side of an autotest scenario that did
		/// not happen to write the line. Such a player could then be nuked and arm nobody, and could
		/// never retaliate, because the exchange would not know they existed.</para>
		///
		/// <para>IT COST A SCENARIO RUN. test-nuclear-exchange authored `Playable: True` on USA and not
		/// on Russia; the run logged "NUCLEAR RELEASE: all 1 sides" and every Russian nuclear power
		/// stayed dark for the whole match, while <see cref="DefconWall"/> — which partitions the same
		/// players with the predicate below — logged "derived from 2 home(s) in 2 group(s)" on the very
		/// same tick. Two traits disagreeing about who is in the match is the bug; this is the shipped
		/// side of that disagreement (`DefconWall.cs:296`).</para>
		/// </summary>
		public static bool CountsAsASide(bool nonCombatant, bool spectating)
		{
			return !nonCombatant && !spectating;
		}

		/// <summary>Add a side. Idempotent, so a caller may register from a loop without checking.</summary>
		public void RegisterSide(int side)
		{
			if (sides.ContainsKey(side))
				return;

			sides.Add(side, new SideState());
			sideKeys.Add(side);
		}

		/// <summary>This side's state, or null if it was never registered.</summary>
		public SideState For(int side)
		{
			return sides.TryGetValue(side, out var state) ? state : null;
		}

		public int PermanentLevelFor(int side)
		{
			return For(side)?.PermanentLevel ?? (int)NuclearRung.Hold;
		}

		/// <summary>The window's band, or Hold when no window is open.</summary>
		public int WindowLevelFor(int side)
		{
			var s = For(side);
			return s != null && s.WindowTicksRemaining > 0 ? s.WindowLevel : (int)NuclearRung.Hold;
		}

		public int WindowTicksRemainingFor(int side)
		{
			return For(side)?.WindowTicksRemaining ?? 0;
		}

		/// <summary>
		/// Everything this side may fire right now: its permanent level, or its window's band while
		/// one is open. THE ONE NUMBER the condition layer reads.
		/// </summary>
		public int ReleasedLevelFor(int side)
		{
			var s = For(side);
			if (s == null)
				return (int)NuclearRung.Hold;

			var window = s.WindowTicksRemaining > 0 ? s.WindowLevel : (int)NuclearRung.Hold;
			return window > s.PermanentLevel ? window : s.PermanentLevel;
		}

		/// <summary>
		/// The release gate has opened: every side holds the lowest band permanently, from now on.
		/// Returns true the first time only, so a caller may poll it.
		/// </summary>
		public bool Release()
		{
			if (Released || mode != DefconGameMode.Escalation)
				return false;

			Released = true;

			foreach (var key in sideKeys)
			{
				var s = sides[key];
				if (s.PermanentLevel < (int)NuclearRung.Kiloton)
					s.PermanentLevel = (int)NuclearRung.Kiloton;
			}

			return true;
		}

		/// <summary>
		/// One tick of every open window. Returns the sides whose window LAPSED on this tick, in
		/// registration order, so a caller can undo whatever it did when the window opened.
		/// </summary>
		// Allocates only when something actually lapses, which is at most a handful of ticks in a
		// whole match: the null stays null on every other tick and the caller treats it as empty.
		public List<int> TickWindows()
		{
			List<int> lapsed = null;

			foreach (var key in sideKeys)
			{
				var s = sides[key];
				if (s.WindowTicksRemaining <= 0)
					continue;

				// The decrement idiom DefconEscalationState.Tick and NuclearReleaseLadder.Tick both
				// use, so a window of N ticks is open for exactly N ticks rather than N +/- 1.
				if (--s.WindowTicksRemaining > 0)
					continue;

				s.WindowLevel = (int)NuclearRung.Hold;
				(lapsed ??= new List<int>()).Add(key);
			}

			return lapsed;
		}

		/// <summary>
		/// Side <paramref name="firerSide"/> has released a warhead of <paramref name="tons"/> tons of
		/// TNT. Arms every other side by rule 2.
		/// </summary>
		public NuclearLaunchOutcome ReportLaunch(int firerSide, int tons)
		{
			// Skirmish and Sandbox have no exchange at all -- their release is NuclearUnlockClock's
			// schedule, which no detonation moves. Same strict-no-op rule the ladder carried.
			if (mode != DefconGameMode.Escalation)
				return NuclearLaunchOutcome.Dropped;

			// A LAUNCH BEFORE RELEASE IS DROPPED WHOLE. Nothing a player can click reaches here while
			// the gate is shut, because every nuclear power is gated on a band condition that is not
			// granted -- but a Lua scenario or a bot calling the trait directly can, and arming a
			// side off the back of one would let a match arrive at release already escalated.
			if (!Released)
				return NuclearLaunchOutcome.Dropped;

			// THE TSAR BOMBA GATE, checked before the band. Nothing a player can click reaches here
			// with a 50 Mt warhead -- MissileStrikePower@TsarBomba is gated on the unrestricted
			// condition, which is never granted inside Escalation -- but a Lua scenario calling the
			// trait directly can, and arming the other side with a game-ender off the back of a weapon
			// that is not in play would end the match by a route decision 04 closed.
			if (tons > NuclearReleaseLadder.SandboxOnlyAboveTons)
				return NuclearLaunchOutcome.Dropped;

			var band = NuclearReleaseLadder.RungForYield(tons);
			if (band <= (int)NuclearRung.Hold)
				return NuclearLaunchOutcome.Dropped;

			var windowBand = band + 1;
			if (windowBand > NuclearReleaseLadder.Highest)
				windowBand = NuclearReleaseLadder.Highest;

			// A PERMANENT LEVEL IS CAPPED ONE BELOW THE TOP, and this is an EXPLICIT rule rather
			// than a consequence of the others -- it was written as a consequence and was WRONG,
			// which NuclearExchangeStateTest.GameEndersAreReachableOnlyThroughAWindow caught.
			//
			// The reasoning that failed: "nothing can fire a game-ender without a window, so no
			// permanent level can ever be raised to one". True of every band EXCEPT the top, because
			// firing a game-ender is itself a launch and rule 2 would hand the other side parity in
			// kind -- a PERMANENT game-ender, which is exactly the indefinite draw card decision 01
			// rules out ("If the window lapses, no side holds a game-ender").
			//
			// It does not matter in play, because that launch also begins the final exchange and the
			// match ends. It matters here, because otherwise the invariant holds by luck -- resting
			// on DoomsdayStrike being wired and reaching every side before anyone reads a level.
			var permanentBand = band;
			if (permanentBand >= (int)NuclearRung.GameEnder)
				permanentBand = (int)NuclearRung.GameEnder - 1;

			foreach (var key in sideKeys)
			{
				if (key == firerSide)
					continue;

				var s = sides[key];

				if (permanentBand > s.PermanentLevel)
					s.PermanentLevel = permanentBand;

				// max(), not assignment: a side already holding a window for a BIGGER band must not
				// have it cut down by a subsequent small shot. "A hit at a higher band raises the
				// window's band to the max" (decision 01).
				if (s.WindowTicksRemaining <= 0 || windowBand > s.WindowLevel)
					s.WindowLevel = windowBand;

				// RESTARTED ON EVERY HIT, including a hit that changed neither level. Being shot at
				// again is what re-opens the reply window; that is the rule, and it is also what
				// makes repeated small strikes a real cost to the side firing them.
				s.WindowTicksRemaining = retaliationWindowTicks;
				s.WindowSerial++;
			}

			return new NuclearLaunchOutcome(true, band, band >= (int)NuclearRung.GameEnder);
		}
	}
}
