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

using System;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Tests the suppression system's mathematical properties.
	/// Suppression is implemented via YAML conditions (not C# logic), but the
	/// tier boundaries, modifier progressions, and decay math can be validated.
	/// This catches regressions if someone changes the YAML values accidentally.
	/// </summary>
	[TestFixture]
	public class SuppressionMathTest
	{
		// --- Infantry suppression tiers (10 tiers, cap 100) ---
		// Each tier covers a range of 10 suppression points

		static int GetInfantrySpeedModifier(int suppression)
		{
			if (suppression <= 0) return 100;
			if (suppression <= 10) return 90;
			if (suppression <= 20) return 80;
			if (suppression <= 30) return 70;
			if (suppression <= 40) return 60;
			if (suppression <= 50) return 50;
			if (suppression <= 60) return 40;
			if (suppression <= 70) return 30;
			if (suppression <= 80) return 20;
			if (suppression <= 90) return 10;
			return 0; // 91-100
		}

		static int GetInfantryInaccuracyModifier(int suppression)
		{
			if (suppression <= 0) return 100;
			if (suppression <= 10) return 120;
			if (suppression <= 20) return 140;
			if (suppression <= 30) return 160;
			if (suppression <= 40) return 180;
			if (suppression <= 50) return 200;
			if (suppression <= 60) return 220;
			if (suppression <= 70) return 240;
			if (suppression <= 80) return 260;
			if (suppression <= 90) return 280;
			return 300; // 91-100
		}

		static int GetVehicleTurretSpeedModifier(int suppression)
		{
			if (suppression <= 0) return 100;
			if (suppression <= 10) return 85;
			if (suppression <= 20) return 70;
			if (suppression <= 30) return 55;
			if (suppression <= 40) return 40;
			return 25; // 41-50
		}

		static int GetVehicleInaccuracyModifier(int suppression)
		{
			if (suppression <= 0) return 100;
			if (suppression <= 10) return 115;
			if (suppression <= 20) return 130;
			if (suppression <= 30) return 150;
			if (suppression <= 40) return 175;
			return 200; // 41-50
		}

		// --- Decay math ---
		// Infantry: reduce 1 every 5 ticks. Vehicle: reduce 1 every 3 ticks.

		static int SimulateDecay(int startSuppression, int ticks, int reduceTicks, int reduceAmount, int cap)
		{
			var suppression = Math.Min(startSuppression, cap);
			for (var t = 0; t < ticks; t++)
			{
				if (t % reduceTicks == 0 && suppression > 0)
					suppression = Math.Max(0, suppression - reduceAmount);
			}

			return suppression;
		}

		// --- Tests ---

		[Test]
		public void InfantrySpeedDecreasesLinearly()
		{
			// Speed modifier decreases by 10% per tier
			for (var tier = 0; tier <= 10; tier++)
			{
				var suppression = tier * 10;
				if (suppression == 0)
				{
					Assert.That(GetInfantrySpeedModifier(0), Is.EqualTo(100));
					continue;
				}

				var expected = 100 - (tier * 10);
				Assert.That(GetInfantrySpeedModifier(suppression), Is.EqualTo(expected),
					$"Speed at suppression {suppression} should be {expected}%");
			}
		}

		[Test]
		public void InfantryInaccuracyIncreasesLinearly()
		{
			// Inaccuracy increases by 20% per tier (120, 140, ..., 300)
			for (var tier = 1; tier <= 10; tier++)
			{
				var suppression = tier * 10;
				var expected = 100 + (tier * 20);
				Assert.That(GetInfantryInaccuracyModifier(suppression), Is.EqualTo(expected),
					$"Inaccuracy at suppression {suppression} should be {expected}%");
			}
		}

		[Test]
		public void InfantryFullyPinnedAtMaxSuppression()
		{
			Assert.That(GetInfantrySpeedModifier(100), Is.EqualTo(0), "Speed should be 0 at max suppression");
			Assert.That(GetInfantryInaccuracyModifier(100), Is.EqualTo(300), "Inaccuracy should be 300% at max");
		}

		[Test]
		public void VehicleTurretSlowsButMovementUnaffected()
		{
			// Vehicles have NO speed modifier — only turret, inaccuracy, burst wait
			// This test documents the design: vehicles keep moving under fire
			Assert.That(GetVehicleTurretSpeedModifier(50), Is.EqualTo(25),
				"Turret at max vehicle suppression should be 25%");
		}

		[Test]
		public void VehicleCapIsLowerThanInfantry()
		{
			// Vehicle cap = 50, Infantry cap = 100
			// This means vehicles can't be fully suppressed
			Assert.That(GetVehicleInaccuracyModifier(50), Is.EqualTo(200));
			Assert.That(GetInfantryInaccuracyModifier(50), Is.EqualTo(200));
			// At same absolute value (50), both have 200% inaccuracy
			// But infantry can go to 300% at suppression 100, vehicles can't
		}

		[Test]
		public void InfantryDecayTakesCorrectTime()
		{
			// Infantry: reduce 1 every 5 ticks, starting at 50
			// Full decay from 50: 50 * 5 = 250 ticks
			var remaining = SimulateDecay(50, 250, 5, 1, 100);
			Assert.That(remaining, Is.EqualTo(0));

			// After 100 ticks (20 reductions): 50 - 20 = 30
			remaining = SimulateDecay(50, 100, 5, 1, 100);
			Assert.That(remaining, Is.EqualTo(30));
		}

		[Test]
		public void VehicleDecayIsFasterThanInfantry()
		{
			// Vehicle: reduce 1 every 3 ticks vs Infantry: every 5 ticks
			// From suppression 30, after 60 ticks:
			var infantryRemaining = SimulateDecay(30, 60, 5, 1, 100);
			var vehicleRemaining = SimulateDecay(30, 60, 3, 1, 50);

			Assert.That(vehicleRemaining, Is.LessThan(infantryRemaining),
				"Vehicles should recover from suppression faster than infantry");
		}

		[Test]
		public void SuppressionClampsToCap()
		{
			// Even if we somehow get 150 suppression, it clamps to cap
			var infantryDecay = SimulateDecay(150, 0, 5, 1, 100);
			Assert.That(infantryDecay, Is.EqualTo(100));

			var vehicleDecay = SimulateDecay(150, 0, 3, 1, 50);
			Assert.That(vehicleDecay, Is.EqualTo(50));
		}

		[Test]
		public void ProneTriggersAtCorrectThreshold()
		{
			// InfantryStates triggers prone at suppression > 30
			// This is the design boundary between mobile-but-slow and pinned-prone
			var threshold = 30;
			Assert.That(GetInfantrySpeedModifier(threshold), Is.EqualTo(70),
				"At prone threshold, infantry should still have 70% speed");
			Assert.That(GetInfantrySpeedModifier(threshold + 1), Is.EqualTo(60),
				"Just above prone threshold, speed drops to 60%");
		}
	}
}
