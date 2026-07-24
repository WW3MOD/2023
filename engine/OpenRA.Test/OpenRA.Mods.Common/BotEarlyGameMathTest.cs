#region Copyright & License Information
/*
 * WW3MOD early-game economy math tests — experimental AI, PIPELINE item 12.
 *
 * Pure-logic pins for the three early-game decisions:
 *   1. ResupplyDemand — no supply trucks while every fielded unit has full ammo
 *      (need mirrors SupplyProvider.CalculateNeed);
 *   2. AntiAirDemand — vehicle-AA call-ins capped to the OBSERVED air threat;
 *   3. EarlyGamePhase — the young-match window that swaps in smaller attack axes.
 *
 * Each decision lives in a pure static class so it is testable without a World and
 * ports into a future v3 brain. Zero RNG — deterministic by construction.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BotEarlyGameMathTest
	{
		// ---------- ResupplyDemand ----------

		[Test]
		public void UnitNeed_FullAmmoIsZero()
		{
			// Every pool topped up ⇒ no need, no truck.
			var need = ResupplyDemand.UnitNeed(new[] { (Ammo: 3, Current: 3, SupplyValue: 65) });
			Assert.That(need, Is.EqualTo(0f));
		}

		[Test]
		public void UnitNeed_EmptyPoolIsFull()
		{
			var need = ResupplyDemand.UnitNeed(new[] { (Ammo: 3, Current: 0, SupplyValue: 65) });
			Assert.That(need, Is.EqualTo(1f));
		}

		[Test]
		public void UnitNeed_WeightsBySupplyValueAcrossPools()
		{
			// A cheap bulk pool missing 1 round and an expensive missile pool missing 1 round:
			// need is dominated by the SupplyValue weight, matching SupplyProvider.CalculateNeed.
			// missing = 100*1 (rifle) + 1*200 (missile) = 300; capacity = 900*1 + 3*200 = 1500.
			var need = ResupplyDemand.UnitNeed(new[]
			{
				(Ammo: 900, Current: 800, SupplyValue: 1),
				(Ammo: 3, Current: 2, SupplyValue: 200),
			});
			Assert.That(need, Is.EqualTo(300f / 1500f).Within(1e-6f));
		}

		[Test]
		public void UnitNeed_NoCapacityIsZero()
		{
			Assert.That(ResupplyDemand.UnitNeed(new[] { (Ammo: 0, Current: 0, SupplyValue: 0) }), Is.EqualTo(0f));
			Assert.That(ResupplyDemand.UnitNeed(null), Is.EqualTo(0f));
		}

		[Test]
		public void MeetsThreshold_SkipsNearlyFull()
		{
			// 499/500 ammo ⇒ need 0.002, below the 0.05 default ⇒ not meaningful (no truck).
			Assert.That(ResupplyDemand.MeetsThreshold(0.002f, 0.05f), Is.False);
			// At/above threshold ⇒ meaningful.
			Assert.That(ResupplyDemand.MeetsThreshold(0.05f, 0.05f), Is.True);
			Assert.That(ResupplyDemand.MeetsThreshold(0.5f, 0.05f), Is.True);
		}

		// ---------- AntiAirDemand ----------

		[Test]
		public void MaxAllowed_ZeroAirZeroBaselineIsNoVehicleAA()
		{
			// The overbuild cure: no observed air, baseline 0 ⇒ zero vehicle AA permitted.
			Assert.That(AntiAirDemand.MaxAllowed(0, 0, 1), Is.EqualTo(0));
		}

		[Test]
		public void MaxAllowed_ScalesWithObservedAir()
		{
			Assert.That(AntiAirDemand.MaxAllowed(2, 0, 1), Is.EqualTo(2));
			Assert.That(AntiAirDemand.MaxAllowed(3, 1, 1), Is.EqualTo(4));
			Assert.That(AntiAirDemand.MaxAllowed(2, 0, 2), Is.EqualTo(4));
		}

		[Test]
		public void MaxAllowed_FloorsNegativeInputs()
		{
			Assert.That(AntiAirDemand.MaxAllowed(-5, 1, 2), Is.EqualTo(1));
			Assert.That(AntiAirDemand.MaxAllowed(3, -1, -1), Is.EqualTo(0));
		}

		[Test]
		public void ShouldBuildMore_GatesAtCap()
		{
			// No air seen, baseline 0 ⇒ never build vehicle AA regardless of owned count.
			Assert.That(AntiAirDemand.ShouldBuildMore(0, 0, 0, 1), Is.False);
			// One aircraft seen ⇒ may build the first, then stop.
			Assert.That(AntiAirDemand.ShouldBuildMore(0, 1, 0, 1), Is.True);
			Assert.That(AntiAirDemand.ShouldBuildMore(1, 1, 0, 1), Is.False);
		}

		// ---------- EarlyGamePhase ----------

		[Test]
		public void IsEarly_TrueOnlyInsideWindowAndWhenEnabled()
		{
			Assert.That(EarlyGamePhase.IsEarly(0, true, 4500), Is.True);
			Assert.That(EarlyGamePhase.IsEarly(4499, true, 4500), Is.True);
			Assert.That(EarlyGamePhase.IsEarly(4500, true, 4500), Is.False);
			Assert.That(EarlyGamePhase.IsEarly(9999, true, 4500), Is.False);
			// Disabled ⇒ always false (frozen: normal axis sizing).
			Assert.That(EarlyGamePhase.IsEarly(0, false, 4500), Is.False);
		}

		[Test]
		public void EarlySizing_OpensMoreAxesForFewUnits()
		{
			// Demonstrates the intended effect via the already-tested allocator: with a small early
			// pool, smaller UnitsPerAxis/MinAxisSize disperse into more axes than the normal constants.
			var normal = PoiOffenseMath.DesiredAxisCount(6, 5, 8, 3, 4); // 6 units, 8/axis, min 3 → 1 axis
			var early = PoiOffenseMath.DesiredAxisCount(6, 5, 3, 2, 4);   // 3/axis, min 2 → more packets
			Assert.That(normal, Is.EqualTo(1));
			Assert.That(early, Is.GreaterThan(normal));
		}
	}
}
