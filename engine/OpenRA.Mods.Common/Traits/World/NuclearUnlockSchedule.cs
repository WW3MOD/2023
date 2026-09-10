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
 * THE SKIRMISH UNLOCK CLOCK'S ARITHMETIC -- which yield band is on sale, given how long the match
 * has run. No Actor, no World, no lobby, for exactly the reason DefconEscalationState and
 * NuclearReleaseLadder are plain classes and say so in their own headers: nothing in OpenRA.Test can
 * construct a World, so a rule living inside a trait method is a rule verified by READING.
 *
 * ==== THE RULING IT IMPLEMENTS (decision 16, confirmed by decision 22) ====
 * "Skirmish may have nuclear weapons ... They are built/purchased, gated behind time intervals --
 * low yield purchasable after ten minutes, the next tier after twenty, and so on. No free nukes in
 * Skirmish, and no escalation per used nuke at all in Skirmish."
 *
 * So rung N comes up for sale at N intervals in: the 1 kt band at 10:00, 20 kt at 20:00, 50 kt at
 * 30:00, 100 kt at 40:00 on the shipped default. Nothing here reads a detonation -- that is the
 * whole difference from NuclearReleaseLadder, which is climbed by firing and is Escalation's.
 *
 * ==== IT IS AN INDEX INTO THE LADDER'S EXISTING BAND TABLE, NOT A SECOND TABLE ====
 * The rungs are NuclearRung values and the conditions they release are
 * GrantConditionOnNuclearReleaseInfo.Conditions -- the same five names Escalation grants, keyed the
 * same way. That is deliberate and is what keeps the two modes from drifting: a weapon names its own
 * band once, in YAML, and both mode's clocks decide only HOW FAR UP that one table they have got.
 * Adding a rung to NuclearRung therefore needs no edit here.
 *
 * ==== GAME-ENDERS ARE NOT REACHABLE AND THAT IS NOT A CAP, IT IS THE CEILING OF THE TYPE ====
 * HighestPurchasableRung is HundredKiloton, one below GameEnder, and the cap the host sets is
 * clamped INTO that -- so no lobby value, no interval and no elapsed time reaches the 200 kt+ band.
 * Decision 17.3, which the user chose over the agent's recommendation and made stricter than it was
 * offered: "Game-enders are NEVER purchasable in Skirmish", with no host override. They keep their
 * meaning as the thing that ends matches, reachable in Escalation or from a Time Limit the host set
 * deliberately, and never bought at minute forty.
 */

namespace OpenRA.Mods.Common.Traits
{
	public static class NuclearUnlockSchedule
	{
		/// <summary>The lowest band the clock ever sells: rung 1, the 1 kt band.</summary>
		public const int LowestRung = (int)NuclearRung.Kiloton;

		/// <summary>
		/// The highest band the clock ever sells. ONE BELOW <see cref="NuclearRung.GameEnder"/>, by the
		/// user's ruling (decision 17.3) rather than by arithmetic -- see the file header.
		/// </summary>
		public const int HighestPurchasableRung = (int)NuclearRung.HundredKiloton;

		/// <summary>Nothing is on sale: the rung a match sits at before its first interval elapses.</summary>
		public const int NothingReleased = (int)NuclearRung.Hold;

		/// <summary>
		/// Minutes to ticks at a given millisecond timestep. 10 minutes at the mod's 60 ms timestep is
		/// 10000 ticks, which is the identity to check any change here against.
		/// </summary>
		// DELIBERATELY NOT TimeLimitManager's `1000 / world.Timestep` IDIOM, and this is the one place
		// in this file worth reading twice. That expression is INTEGER DIVISION: at a 60 ms timestep it
		// yields 16 ticks per second, not 16.67, so `minutes * 60 * 16` makes ten minutes 9600 ticks --
		// 576 seconds of real time, 4% short, and short by more the longer the interval. Multiplying
		// before dividing keeps it exact: 10 * 60 * 1000 / 60 = 10000 ticks = 600.0 s.
		//
		// The 4% is invisible in a time limit, which is why it has survived there; it is NOT invisible
		// here, because this number is what the lobby timeline DRAWS as the band boundary. A bar
		// claiming 10:00 over a clock that fires at 9:36 is the specific class of lie this whole
		// branch exists to remove.
		public static int TicksForMinutes(int minutes, int timestepMilliseconds)
		{
			if (minutes <= 0 || timestepMilliseconds <= 0)
				return 0;

			return minutes * 60 * 1000 / timestepMilliseconds;
		}

		/// <summary>
		/// The highest band on sale after this many ticks. <paramref name="intervalTicks"/> of 0 or less
		/// means NO CLOCK -- everything up to the cap is on sale from the first tick, which is the
		/// behaviour every Skirmish match had before this file existed.
		/// </summary>
		// THE BOUNDARY IS INCLUSIVE AT THE BOTTOM: at exactly one interval the 1 kt band IS on sale,
		// which is what makes the timeline's marker the tick the shop opens rather than the tick before
		// it. Integer division does that for free and cannot drift.
		public static int RungAt(int elapsedTicks, int intervalTicks, int capRung)
		{
			var cap = ClampCap(capRung);

			// NO CLOCK. Not "an infinitely long interval" -- the cap still applies, so a host who
			// turns the wait off still does not get game-enders.
			if (intervalTicks <= 0)
				return cap;

			if (elapsedTicks < intervalTicks)
				return NothingReleased;

			var released = elapsedTicks / intervalTicks;
			return released > cap ? cap : released;
		}

		/// <summary>Ticks from match start until this band comes up for sale. 0 if it already is.</summary>
		// Used by the lobby timeline to position the band boundary, so it MUST agree with RungAt above
		// or the bar draws a boundary the match does not honour. The two are inverses by construction:
		// RungAt divides where this multiplies.
		public static int TicksUntilRung(int rung, int intervalTicks)
		{
			if (intervalTicks <= 0 || rung <= NothingReleased)
				return 0;

			return ClampCap(rung) * intervalTicks;
		}

		/// <summary>The host's cap, forced into the band range this clock is allowed to sell.</summary>
		public static int ClampCap(int capRung)
		{
			if (capRung < LowestRung)
				return LowestRung;

			return capRung > HighestPurchasableRung ? HighestPurchasableRung : capRung;
		}
	}
}
