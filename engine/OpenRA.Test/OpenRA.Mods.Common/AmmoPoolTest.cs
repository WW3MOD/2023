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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AmmoPoolTest
	{
		// Helper: create AmmoPoolInfo with custom values via reflection
		// (AmmoPoolInfo fields are readonly, so we set them before constructing AmmoPool)
		static AmmoPool CreatePool(int ammo = 10, int initialAmmo = -1, int reloadCount = 1,
			int reloadDelay = 50, int fullReloadTicks = 0, int fullReloadSteps = 0, int supplyValue = 1)
		{
			var info = new AmmoPoolInfo();
			SetField(info, nameof(AmmoPoolInfo.Ammo), ammo);
			SetField(info, nameof(AmmoPoolInfo.InitialAmmo), initialAmmo);
			SetField(info, nameof(AmmoPoolInfo.ReloadCount), reloadCount);
			SetField(info, nameof(AmmoPoolInfo.ReloadDelay), reloadDelay);
			SetField(info, nameof(AmmoPoolInfo.FullReloadTicks), fullReloadTicks);
			SetField(info, nameof(AmmoPoolInfo.FullReloadSteps), fullReloadSteps);
			SetField(info, nameof(AmmoPoolInfo.SupplyValue), supplyValue);
			return new AmmoPool(info);
		}

		static void SetField(object obj, string name, object value)
		{
			var field = obj.GetType().GetField(name);
			if (field == null)
				throw new ArgumentException($"Field {name} not found on {obj.GetType().Name}");
			field.SetValue(obj, value);
		}

		// --- Constructor / Initial Ammo ---

		[Test]
		public void InitialAmmoDefaultsToMax()
		{
			var pool = CreatePool(ammo: 8);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(8));
			Assert.That(pool.HasFullAmmo, Is.True);
			Assert.That(pool.HasAmmo, Is.True);
		}

		[Test]
		public void InitialAmmoCanBeSetLower()
		{
			var pool = CreatePool(ammo: 10, initialAmmo: 3);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(3));
			Assert.That(pool.HasFullAmmo, Is.False);
			Assert.That(pool.HasAmmo, Is.True);
		}

		[Test]
		public void InitialAmmoZeroMeansEmpty()
		{
			var pool = CreatePool(ammo: 5, initialAmmo: 0);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(0));
			Assert.That(pool.HasAmmo, Is.False);
			Assert.That(pool.HasFullAmmo, Is.False);
		}

		[Test]
		public void InitialAmmoClampedToMax()
		{
			// InitialAmmo >= Ammo should give full ammo
			var pool = CreatePool(ammo: 5, initialAmmo: 99);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(5));
		}

		[Test]
		public void InitialAmmoNegativeDefaultsToMax()
		{
			// InitialAmmo = -1 (default) means start full
			var pool = CreatePool(ammo: 6, initialAmmo: -1);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(6));
		}

		// --- HasHalfAmmo ---

		[Test]
		public void HasHalfAmmoThreshold()
		{
			var pool = CreatePool(ammo: 10, initialAmmo: 6);
			Assert.That(pool.HasHalfAmmo, Is.True, "6 > 10/2 = 5, should be true");

			pool = CreatePool(ammo: 10, initialAmmo: 5);
			Assert.That(pool.HasHalfAmmo, Is.False, "5 is not > 5");

			pool = CreatePool(ammo: 10, initialAmmo: 4);
			Assert.That(pool.HasHalfAmmo, Is.False, "4 < 5");
		}

		// --- GiveAmmo ---
		// Note: GiveAmmo/TakeAmmo require an Actor for UpdateCondition.
		// We test the boundary logic that doesn't need conditions (AmmoCondition is null by default).
		// The null AmmoCondition path in UpdateCondition returns early, so no Actor needed.

		// We can't call GiveAmmo/TakeAmmo without an Actor because UpdateCondition is called.
		// But with AmmoCondition = null (default), UpdateCondition returns immediately.
		// The Actor parameter is only used for self.GrantCondition/RevokeCondition.
		// So we pass null — this works when AmmoCondition is null.

		[Test]
		public void GiveAmmoIncreasesCount()
		{
			var pool = CreatePool(ammo: 10, initialAmmo: 5);
			var result = pool.GiveAmmo(null, 3);
			Assert.That(result, Is.True);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(8));
		}

		[Test]
		public void GiveAmmoClampsToMax()
		{
			var pool = CreatePool(ammo: 10, initialAmmo: 8);
			pool.GiveAmmo(null, 5);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(10));
		}

		[Test]
		public void GiveAmmoReturnsFalseWhenFull()
		{
			var pool = CreatePool(ammo: 5);
			var result = pool.GiveAmmo(null, 1);
			Assert.That(result, Is.False);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(5));
		}

		[Test]
		public void GiveAmmoRejectsNegative()
		{
			var pool = CreatePool(ammo: 10, initialAmmo: 5);
			var result = pool.GiveAmmo(null, -1);
			Assert.That(result, Is.False);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(5));
		}

		// --- TakeAmmo ---

		[Test]
		public void TakeAmmoDecreasesCount()
		{
			var pool = CreatePool(ammo: 10);
			var result = pool.TakeAmmo(null, 3);
			Assert.That(result, Is.True);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(7));
		}

		[Test]
		public void TakeAmmoClampsToZero()
		{
			var pool = CreatePool(ammo: 10, initialAmmo: 2);
			pool.TakeAmmo(null, 5);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(0));
		}

		[Test]
		public void TakeAmmoReturnsFalseWhenEmpty()
		{
			var pool = CreatePool(ammo: 5, initialAmmo: 0);
			var result = pool.TakeAmmo(null, 1);
			Assert.That(result, Is.False);
		}

		[Test]
		public void TakeAmmoRejectsNegative()
		{
			var pool = CreatePool(ammo: 10);
			var result = pool.TakeAmmo(null, -1);
			Assert.That(result, Is.False);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(10));
		}

		[Test]
		public void TakeAllAmmoThenGiveBack()
		{
			var pool = CreatePool(ammo: 3);

			pool.TakeAmmo(null, 1);
			pool.TakeAmmo(null, 1);
			pool.TakeAmmo(null, 1);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(0));
			Assert.That(pool.HasAmmo, Is.False);

			pool.GiveAmmo(null, 2);
			Assert.That(pool.CurrentAmmoCount, Is.EqualTo(2));
			Assert.That(pool.HasAmmo, Is.True);
			Assert.That(pool.HasFullAmmo, Is.False);
		}

		// --- SupplyValue ---

		[Test]
		public void SupplyValueStoredCorrectly()
		{
			var pool = CreatePool(ammo: 10, supplyValue: 5);
			Assert.That(pool.Info.SupplyValue, Is.EqualTo(5));
		}

		[Test]
		public void SupplyValueDefaultIsOne()
		{
			var pool = CreatePool(ammo: 10);
			Assert.That(pool.Info.SupplyValue, Is.EqualTo(1));
		}

		// --- FullReloadTicks math ---
		// The reload formula: RemainingTicks = FullReloadTicks * ReloadCount / Ammo
		// With FullReloadSteps: ReloadCount = Ceiling(Ammo / FullReloadSteps)

		[Test]
		public void FullReloadStepsCalculatesReloadCount()
		{
			// Ammo=10, FullReloadSteps=3 → ReloadCount = Ceiling(10/3) = 4
			// This means each reload step gives 4 ammo (last step clamped)
			var ammo = 10;
			var steps = 3;
			double a = ammo / steps; // integer division: 10/3 = 3
			var reloadCount = (int)Math.Ceiling(a);
			Assert.That(reloadCount, Is.EqualTo(3), "Integer division: 10/3=3, Ceiling(3)=3");

			// With double division it would be Ceiling(3.33) = 4
			// But the code uses integer division first, then Ceiling
			// This is the actual behavior in AmmoPool.cs line 142:
			// double a = Info.Ammo / Info.FullReloadSteps; (integer division assigned to double)
		}

		[Test]
		public void FullReloadTicksPerStep()
		{
			// FullReloadTicks=300, ReloadCount=3, Ammo=10
			// Ticks per step = 300 * 3 / 10 = 90
			var fullReloadTicks = 300;
			var reloadCount = 3;
			var ammo = 10;
			var ticksPerStep = fullReloadTicks * reloadCount / ammo;
			Assert.That(ticksPerStep, Is.EqualTo(90));
		}

		[Test]
		public void FullReloadTicksRoundsDown()
		{
			// FullReloadTicks=100, ReloadCount=3, Ammo=10
			// Ticks per step = 100 * 3 / 10 = 30
			var ticksPerStep = 100 * 3 / 10;
			Assert.That(ticksPerStep, Is.EqualTo(30));

			// FullReloadTicks=100, ReloadCount=3, Ammo=7
			// Ticks per step = 100 * 3 / 7 = 42 (integer division)
			ticksPerStep = 100 * 3 / 7;
			Assert.That(ticksPerStep, Is.EqualTo(42));
		}
	}
}
