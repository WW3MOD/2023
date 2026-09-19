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

using System;
using Eluant;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptGlobal("DateTime")]
	public class DateGlobal : ScriptGlobal
	{
		readonly TimeLimitManager tlm;

		// The DEFAULT GameSpeed's milliseconds per tick, not a ticks-per-second rate. See the
		// constructor.
		readonly int timestepMilliseconds;

		public DateGlobal(ScriptContext context)
			: base(context)
		{
			tlm = context.World.WorldActor.TraitOrDefault<TimeLimitManager>();
			var gameSpeeds = Game.ModData.Manifest.Get<GameSpeeds>();
			var defaultGameSpeed = gameSpeeds.Speeds[gameSpeeds.DefaultSpeed];

			// ==== WAS `ticksPerSecond = 1000 / defaultGameSpeed.Timestep`, AND THAT TRUNCATED ====
			// Integer division: 1000 / 60 = 16, not 16.667. So DateTime.Seconds(60) returned 960
			// ticks -- 57.6 real seconds -- and every Lua delay in the mod ran 4 % early. The error
			// is invisible upstream because cnc/ra/d2k/ts all default to a 40 ms timestep where
			// 1000 / 40 = 25 exactly; it only bites at 60 ms. Multiplying before dividing is exact
			// at BOTH, so this is byte-identical for those mods and correct for this one.
			timestepMilliseconds = defaultGameSpeed.Timestep;
		}

		[Desc("True on the 31st of October.")]
		[Obsolete("Use CurrentMonth and CurrentDay instead.")]
		public bool IsHalloween => DateTime.Today.Month == 10 && DateTime.Today.Day == 31;

		[Desc("Get the current game time (in ticks).")]
		public int GameTime => Context.World.WorldTick;

		[Desc("Converts the number of seconds into game time (ticks).")]
		public int Seconds(int seconds)
		{
			return TickTime.TicksForSeconds(seconds, timestepMilliseconds);
		}

		[Desc("Get the current year (1-9999).")]
		public int CurrentYear => DateTime.Now.Year;
		[Desc("Get the current month (1-12).")]
		public int CurrentMonth => DateTime.Now.Month;
		[Desc("Get the current day (1-31).")]
		public int CurrentDay => DateTime.Now.Day;
		[Desc("Get the current hour (0-23).")]
		public int CurrentHour => DateTime.Now.Hour;
		[Desc("Get the current minute (0-59).")]
		public int CurrentMinute => DateTime.Now.Minute;
		[Desc("Get the current second (0-59).")]
		public int CurrentSecond => DateTime.Now.Second;

		[Desc("Converts the number of minutes into game time (ticks).")]
		public int Minutes(int minutes)
		{
			// Not Seconds(minutes * 60): converting once from minutes cannot lose a tick to an
			// intermediate, and at 60 ms the two happen to agree only because 60 * 1000 divides
			// evenly. Going through TickTime directly keeps that an identity rather than a
			// coincidence of this timestep.
			return TickTime.TicksForMinutes(minutes, timestepMilliseconds);
		}

		[Desc("Return or set the time limit (in ticks). When setting, the time limit will count from now. Setting the time limit to 0 will disable it.")]
		public int TimeLimit
		{
			get => tlm?.TimeLimit ?? 0;

			set
			{
				if (tlm != null)
					tlm.TimeLimit = value == 0 ? 0 : value + GameTime;
				else
					throw new LuaException("Cannot set TimeLimit, TimeLimitManager trait is missing.");
			}
		}

		[Desc("The notification string used for custom time limit warnings. See the TimeLimitManager trait documentation for details.")]
		public string TimeLimitNotification
		{
			get => tlm?.Notification;

			set
			{
				if (tlm != null)
					tlm.Notification = value;
				else
					throw new LuaException("Cannot set TimeLimitNotification, TimeLimitManager trait is missing.");
			}
		}
	}
}
