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
 * The DEFCON escalation state machine, with no dependency on Actor, World or the lobby.
 *
 * It is a plain class on purpose. Every interesting rule of the mode -- that Skirmish never moves,
 * that only 3 -> 2 is on a clock, that 2 -> 1 waits for a life to be taken -- is decided here, and a
 * plain class is the only shape of that logic an NUnit test can reach: nothing in OpenRA.Test can
 * construct a World, so a rule that lives inside a trait method is a rule verified by reading only.
 *
 * DefconEscalation owns exactly one of these and does nothing to the level that this class does not do.
 */

namespace OpenRA.Mods.Common.Traits
{
	public enum DefconGameMode
	{
		// The default, and a strict no-op: the level sits at NoLevel forever and no condition is granted.
		Skirmish,

		// The mode this whole file exists for. Starts at the configured level and escalates.
		Escalation,

		// Pinned at the configured level: conditions are granted so the DEFCON-keyed content is
		// reachable, but nothing escalates it. This is the one mode the brief did not specify;
		// "a fixed posture you can build and test against" is the reading taken, and changing it
		// means changing ClockFor and ReportCasualty below and nothing else.
		Sandbox
	}

	public enum DefconPace
	{
		Slow,
		Standard,
		Fast
	}

	public class DefconEscalationState
	{
		// DEFCON is not in play. Distinct from any real level so that "Skirmish grants nothing" is a
		// property of the level itself rather than a special case every consumer has to remember.
		public const int NoLevel = 0;

		// 3 is the CEILING, deliberately: it is the real-world standing posture, so there is no 4 or 5
		// to climb down from. 1 is the floor and is terminal.
		public const int Ceiling = 3;
		public const int Floor = 1;

		readonly DefconGameMode mode;
		readonly int ticksFromThreeToTwo;

		public int Level { get; private set; }

		// Ticks left on the 3 -> 2 clock; 0 whenever no clock is running, which includes every level
		// other than 3 and every mode other than Escalation.
		public int TicksUntilNextLevel { get; private set; }

		public DefconEscalationState(DefconGameMode mode, int startLevel, int ticksFromThreeToTwo)
		{
			this.mode = mode;
			this.ticksFromThreeToTwo = ticksFromThreeToTwo;

			if (mode == DefconGameMode.Skirmish)
				Level = NoLevel;
			else if (startLevel < Floor)
				Level = Floor;
			else if (startLevel > Ceiling)
				Level = Ceiling;
			else
				Level = startLevel;

			TicksUntilNextLevel = ClockFor(Level);
		}

		int ClockFor(int level)
		{
			// ONLY 3 -> 2 is on a clock. 2 -> 1 waits for a casualty (ReportCasualty) and 1 is terminal,
			// so both of those return 0 and Tick then does nothing for the rest of the match.
			if (mode != DefconGameMode.Escalation || level != Ceiling)
				return 0;

			return ticksFromThreeToTwo;
		}

		// Returns true on the tick the level actually moves, so the caller can log or notify exactly once.
		public bool Tick()
		{
			if (TicksUntilNextLevel <= 0)
				return false;

			if (--TicksUntilNextLevel > 0)
				return false;

			return SetLevel(Level - 1);
		}

		// A qualifying casualty has happened somewhere in the match. Only DEFCON 2 listens for it:
		// a casualty at 3 does NOT bank an early drop to 1, because in practice the first life is taken
		// long before the 3 -> 2 clock expires and latching it would collapse the whole opening phase
		// into a single tick.
		public bool ReportCasualty()
		{
			if (mode != DefconGameMode.Escalation || Level != Ceiling - 1)
				return false;

			return SetLevel(Floor);
		}

		bool SetLevel(int level)
		{
			if (level == Level)
				return false;

			Level = level;
			TicksUntilNextLevel = ClockFor(level);
			return true;
		}
	}
}
