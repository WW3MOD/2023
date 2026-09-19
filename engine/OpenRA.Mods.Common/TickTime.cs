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
 * WALL-CLOCK DURATIONS -> TICKS. One shape, used everywhere, so the truncation cannot come back.
 *
 * ==== THE DEFECT THIS CLASS EXISTS TO DELETE ====
 * The idiom it replaces is `ticksPerSecond = 1000 / world.Timestep`, then `seconds * ticksPerSecond`.
 * That first expression is INTEGER division. At this mod's 60 ms timestep it yields 16, not 16.667,
 * so every duration built on it is 4 % short -- and short by more the longer the duration, because
 * the error is proportional. A 90-minute Time Limit expired at 86:24 (TimeLimitManager.cs), and a
 * 720 s tournament clock built the other way round on a hardcoded 25 ran 1080 s (TournamentConfig.cs).
 *
 * The fix is only ever "multiply before you divide". There is no ticks-per-second number in here at
 * all, and that is the point: a ticks-per-second INT cannot represent 16.667, so any code that
 * materialises one has already lost the 4 % before it multiplies.
 *
 * ==== WHY A TIMESTEP ARGUMENT RATHER THAN A WORLD ====
 * Nothing in OpenRA.Test can construct a World, so a conversion living inside a trait method is a
 * conversion verified by READING. Every method here is pure integer arithmetic over two ints and is
 * pinned by TickTimeTest at both 60 ms (this mod) and 40 ms (the upstream RA/CNC speeds, where the
 * old `1000 / 40 = 25` happened to be exact -- which is precisely why the bug survived upstream).
 *
 * ==== WHICH TIMESTEP TO PASS ====
 * The match's CONFIGURED timestep, not the live one. `world.Timestep` is mutated at runtime by the
 * debug speed button, by TestModeSpeedMultiplier and by BotVsBotMatchWatcher; `world.GameSpeed.Timestep`
 * never is. Reading `world.Timestep` ONCE in a trait constructor is equivalent and is the established
 * idiom here (DefconEscalation.cs, NuclearUnlockClock.cs), because trait construction precedes every
 * one of those mutations -- they all happen at IWorldLoaded or later. Reading it per tick is not.
 */

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// Converts wall-clock durations to game ticks exactly, by multiplying before dividing.
	/// </summary>
	public static class TickTime
	{
		/// <summary>
		/// Ticks in <paramref name="milliseconds"/> of real time. 0 if the timestep is not positive,
		/// rather than dividing by zero.
		/// </summary>
		public static int TicksForMilliseconds(int milliseconds, int timestepMilliseconds)
		{
			if (timestepMilliseconds <= 0)
				return 0;

			return Clamp((long)milliseconds / timestepMilliseconds);
		}

		/// <summary>
		/// Ticks in <paramref name="seconds"/> of real time. 720 s at a 60 ms timestep is 12000 ticks,
		/// which is the identity to check any change here against.
		/// </summary>
		public static int TicksForSeconds(int seconds, int timestepMilliseconds)
		{
			if (timestepMilliseconds <= 0)
				return 0;

			return Clamp((long)seconds * 1000 / timestepMilliseconds);
		}

		/// <summary>
		/// Ticks in <paramref name="minutes"/> of real time. 90 minutes at a 60 ms timestep is 90000
		/// ticks -- exactly 90 real minutes, which the truncated form made 86:24.
		/// </summary>
		public static int TicksForMinutes(int minutes, int timestepMilliseconds)
		{
			if (timestepMilliseconds <= 0)
				return 0;

			return Clamp((long)minutes * 60 * 1000 / timestepMilliseconds);
		}

		// Durations are held as int ticks everywhere downstream. int.MaxValue ticks is ~1500 years at
		// 60 ms, so this saturates rather than wrapping a nonsense config value into a negative
		// deadline that would read as "already expired".
		static int Clamp(long ticks)
		{
			if (ticks > int.MaxValue)
				return int.MaxValue;

			if (ticks < int.MinValue)
				return int.MinValue;

			return (int)ticks;
		}
	}
}
