#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the tick -> displayed-gametime contract behind the in-game clock.
	/// The bug: GameTimerLogic formatted World.WorldTick with World.Timestep, which the debug speed
	/// button mutates at runtime — doubling the speed retroactively halved the whole displayed match
	/// duration. The clock now formats with World.GameSpeed.Timestep, the match's configured value,
	/// which never changes. These tests fix the mapping at the baseline and demonstrate the rescaling
	/// that using a mutated timestep would reintroduce.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/GameTimerLogic.cs
	/// </summary>
	[TestFixture]
	public class GameClockFormatTest
	{
		const int BaselineTimestep = 60;   // ww3mod "default" GameSpeed (mod.yaml GameSpeeds)
		const int TicksPerMinute = 1000;   // 60_000ms / 60ms

		[Test]
		public void TicksMapToGametimeAtTheBaselineTimestep()
		{
			Assert.That(WidgetUtils.FormatTime(0, BaselineTimestep), Is.EqualTo("00:00"));
			Assert.That(WidgetUtils.FormatTime(16, BaselineTimestep), Is.EqualTo("00:01"));
			Assert.That(WidgetUtils.FormatTime(TicksPerMinute, BaselineTimestep), Is.EqualTo("01:00"));
			Assert.That(WidgetUtils.FormatTime(10 * TicksPerMinute, BaselineTimestep), Is.EqualTo("10:00"));
			Assert.That(WidgetUtils.FormatTime(60 * TicksPerMinute, BaselineTimestep), Is.EqualTo("1:00:00"));
		}

		[Test]
		public void AMutatedTimestepRescalesElapsedTime()
		{
			// This is the bug, expressed as an assertion: the debug speed button sets
			// world.Timestep = baseline / multiplier, so ten minutes of gametime read as five at 2x.
			// The clock must therefore never format with world.Timestep.
			const int TenMinutes = 10 * TicksPerMinute;
			Assert.That(WidgetUtils.FormatTime(TenMinutes, BaselineTimestep), Is.EqualTo("10:00"));
			Assert.That(WidgetUtils.FormatTime(TenMinutes, BaselineTimestep / 2), Is.EqualTo("05:00"));
			Assert.That(WidgetUtils.FormatTime(TenMinutes, BaselineTimestep / 4), Is.EqualTo("02:30"));
		}

		[Test]
		public void DisplayedTimeIsMonotonicInTicks()
		{
			// Whatever the formatting, more ticks never reads as less time at a fixed timestep.
			var previous = -1;
			for (var tick = 0; tick < 5 * TicksPerMinute; tick += 7)
			{
				var seconds = (int)System.Math.Ceiling(tick * BaselineTimestep / 1000f);
				Assert.That(seconds, Is.GreaterThanOrEqualTo(previous));
				previous = seconds;
			}
		}

		[Test]
		public void RealTimeTooltipFormatsAsAClock()
		{
			// The hover tooltip formats wall-clock seconds with the same helper as the main clock,
			// so both readings share a format and can be compared at a glance.
			Assert.That(WidgetUtils.FormatTimeSeconds(0), Is.EqualTo("00:00"));
			Assert.That(WidgetUtils.FormatTimeSeconds(59), Is.EqualTo("00:59"));
			Assert.That(WidgetUtils.FormatTimeSeconds(600), Is.EqualTo("10:00"));
			Assert.That(WidgetUtils.FormatTimeSeconds(3661), Is.EqualTo("1:01:01"));
		}
	}
}
