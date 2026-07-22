#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage B danger-kernel test.
 *
 * Pins the two things the design §2B fixes in stone: (1) which channel a weapon
 * feeds, derived from its ValidTargets (Air/Helicopter vs ground types), and
 * (2) the kernel shape — range sets radius, lethality×durability×confidence sets
 * intensity — so a sniper reads big-and-faint while a humvee reads small-and-dense
 * and an unarmed truck reads ~nothing. Pure math; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DangerFieldKernelTest
	{
		// Mirrors DangerFieldLayerInfo defaults.
		static readonly DangerKernelParams Params = new(
			rangeBufferCells: 2, maxRadiusCells: 32, durabilityBase: 100, healthDivisor: 10, costDivisor: 50);

		static int Cells(int n) => n * 1024;

		// ---- Channel membership from ValidTargets (the Air ≠ Helicopter discriminator) ----

		static readonly (string Name, string[] Targets, bool Ground, bool Air)[] MembershipTable =
		{
			// Ground MG: lists Helicopter, so it threatens BOTH ground troops and our helis.
			("mg-7.62mm",     new[] { "Infantry", "Unarmored", "Helicopter" }, true,  true),
			// Tank main gun: heavy/light ground armour only — no air threat.
			("tank-gun",      new[] { "Heavy", "Light" },                      true,  false),
			// Rifle: infantry only.
			("rifle",         new[] { "Infantry" },                            true,  false),
			// MANPAD / Stinger-class SAM: Air only — pure anti-air, no ground threat.
			("manpad",        new[] { "Air" },                                 false, true),
			// Tunguska's 30mm autocannon: Helicopter only in the data — anti-air, not anti-ground.
			("tunguska-30mm", new[] { "Helicopter" },                          false, true),
		};

		[Test]
		public void ChannelMembershipMatchesValidTargets()
		{
			Assert.Multiple(() =>
			{
				foreach (var row in MembershipTable)
				{
					var targets = new BitSet<TargetableType>(row.Targets);
					Assert.That(DangerKernelMath.WeaponThreatensGround(targets), Is.EqualTo(row.Ground),
						$"{row.Name} ground membership");
					Assert.That(DangerKernelMath.WeaponThreatensAir(targets), Is.EqualTo(row.Air),
						$"{row.Name} air membership");
				}
			});
		}

		// ---- Kernel shapes ----

		// Representative ground-domain facts (range in cells, throughput = damage/window).
		static readonly DangerKernelFacts Sniper = new(groundRange: Cells(12), airRange: 0, groundThroughput: 30, airThroughput: 0, health: 100, cost: 300);
		static readonly DangerKernelFacts Humvee = new(groundRange: Cells(6), airRange: 0, groundThroughput: 300, airThroughput: 0, health: 300, cost: 600);
		static readonly DangerKernelFacts Tank = new(groundRange: Cells(24), airRange: 0, groundThroughput: 400, airThroughput: 0, health: 1000, cost: 1500);
		static readonly DangerKernelFacts Truck = new(groundRange: 0, airRange: 0, groundThroughput: 0, airThroughput: 0, health: 250, cost: 400);

		[Test]
		public void SniperIsWideAndFaint_HumveeIsNarrowAndDense()
		{
			var sniper = DangerKernelMath.Compute(Sniper, DangerChannel.Ground, 100, Params);
			var humvee = DangerKernelMath.Compute(Humvee, DangerChannel.Ground, 100, Params);

			Assert.Multiple(() =>
			{
				Assert.That(sniper.Contributes, Is.True);
				Assert.That(humvee.Contributes, Is.True);
				// Range sets width: the sniper reaches farther.
				Assert.That(sniper.RadiusCells, Is.GreaterThan(humvee.RadiusCells), "sniper aura is wider");
				// Lethality sets density: the humvee's core is far more intense.
				Assert.That(humvee.Intensity, Is.GreaterThan(sniper.Intensity), "humvee aura is denser");
			});
		}

		[Test]
		public void TankOutweighsHumvee()
		{
			// Bigger gun + far higher durability/cost ⇒ the densest ground aura of the set.
			var tank = DangerKernelMath.Compute(Tank, DangerChannel.Ground, 100, Params);
			var humvee = DangerKernelMath.Compute(Humvee, DangerChannel.Ground, 100, Params);
			Assert.That(tank.Intensity, Is.GreaterThanOrEqualTo(humvee.Intensity));
			Assert.That(tank.RadiusCells, Is.GreaterThan(humvee.RadiusCells));
		}

		[Test]
		public void UnarmedTruckContributesNothing()
		{
			var ground = DangerKernelMath.Compute(Truck, DangerChannel.Ground, 100, Params);
			var air = DangerKernelMath.Compute(Truck, DangerChannel.Air, 100, Params);
			Assert.Multiple(() =>
			{
				Assert.That(ground.Contributes, Is.False);
				Assert.That(ground.RadiusCells, Is.EqualTo(0));
				Assert.That(ground.Intensity, Is.EqualTo(0));
				Assert.That(air.Contributes, Is.False);
			});
		}

		[Test]
		public void GroundOnlyUnitDoesNotFeedAirChannel()
		{
			// The tank has no air-capable weapon: it contributes to ground but not air.
			Assert.That(DangerKernelMath.Compute(Tank, DangerChannel.Ground, 100, Params).Contributes, Is.True);
			Assert.That(DangerKernelMath.Compute(Tank, DangerChannel.Air, 100, Params).Contributes, Is.False);
		}

		[Test]
		public void AirOnlyUnitFeedsOnlyAirChannel()
		{
			// A SAM: air range + throughput, zero ground.
			var manpad = new DangerKernelFacts(groundRange: 0, airRange: Cells(20), groundThroughput: 0, airThroughput: 120, health: 80, cost: 250);
			var air = DangerKernelMath.Compute(manpad, DangerChannel.Air, 100, Params);
			var ground = DangerKernelMath.Compute(manpad, DangerChannel.Ground, 100, Params);
			Assert.Multiple(() =>
			{
				Assert.That(air.Contributes, Is.True);
				Assert.That(air.RadiusCells, Is.EqualTo(20 + 2));
				Assert.That(ground.Contributes, Is.False);
			});
		}

		[Test]
		public void ConfidenceScalesIntensity()
		{
			var full = DangerKernelMath.Compute(Tank, DangerChannel.Ground, 100, Params);
			var half = DangerKernelMath.Compute(Tank, DangerChannel.Ground, 50, Params);
			Assert.Multiple(() =>
			{
				Assert.That(half.Intensity, Is.LessThan(full.Intensity), "lower confidence ⇒ fainter aura");
				Assert.That(half.RadiusCells, Is.EqualTo(full.RadiusCells), "confidence does not change reach");
				// Roughly linear: half confidence ≈ half intensity.
				Assert.That(half.Intensity, Is.EqualTo(full.Intensity / 2).Within(1));
			});
		}

		[Test]
		public void RadiusIsBufferedAndCapped()
		{
			// Buffer: reach = range + RangeBufferCells.
			var humvee = DangerKernelMath.Compute(Humvee, DangerChannel.Ground, 100, Params);
			Assert.That(humvee.RadiusCells, Is.EqualTo(6 + 2));

			// Cap: a long-reach artillery kernel is clamped to MaxRadiusCells.
			var arty = new DangerKernelFacts(groundRange: Cells(40), airRange: 0, groundThroughput: 200, airThroughput: 0, health: 300, cost: 1200);
			var kernel = DangerKernelMath.Compute(arty, DangerChannel.Ground, 100, Params);
			Assert.That(kernel.RadiusCells, Is.EqualTo(Params.MaxRadiusCells));
		}
	}
}
