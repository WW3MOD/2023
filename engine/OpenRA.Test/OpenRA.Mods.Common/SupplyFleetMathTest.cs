#region Copyright & License Information
/*
 * WW3MOD @experimental — demand-sized supply fleet tests.
 *
 * Pins the decision UnitBuilderBotModule.ChooseSupplyFleetShortfall turns into a truck call-in when
 * SupplyDemandSizing is on, so "the bot runs one truck all match while the infantry starves" cannot come
 * back:
 *   (1) THE REGRESSION — the measured shape (a starving front, one truck owned) must want MORE than one.
 *       This is the whole point of the class; if it ever passes with a desired count of 1, the fix is gone.
 *   (2) SCALING — the fleet follows customer count, rounding UP so a remainder of demand is never left
 *       permanently unserved.
 *   (3) THE FLOOR — held even at zero starving customers, because a fleet bought only once men are dry
 *       arrives after the fight it was needed for.
 *   (4) THE CEILING — binds no matter how bad the front gets, so supply cannot eat the whole budget. It
 *       outranks the floor when a config sets them crossed.
 *   (5) OVER-PROVISION — the named tunable multiplies the honest number and is the knob that walks back
 *       down; 100% must reproduce the honest number exactly.
 *   (6) DEGENERATE CONFIG — absorbed, never trusted: no divide-by-zero, no empty clamp range, no negative.
 * Pure integer math; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyFleetMathTest
	{
		// ===== (1) The regression =====

		[Test]
		public void DesiredTrucks_StarvingFrontWantsMoreThanTheOneTruckMeasured()
		{
			// The user's match: a front with a dozen dry men and a single standing truck. Under the old
			// composition-share sizing this was one truck; anything that returns 1 here is that bug.
			var desired = SupplyFleetMath.DesiredTrucks(12, 6, 200, 2, 6);

			Assert.That(desired, Is.GreaterThan(1));
			Assert.That(desired, Is.EqualTo(4)); // ceil(12/6) = 2 honest, doubled.
		}

		// ===== (2) Scaling with demand =====

		[Test]
		public void DesiredTrucks_ScalesWithCustomerCount()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(6, 6, 100, 0, 99), Is.EqualTo(1));
			Assert.That(SupplyFleetMath.DesiredTrucks(12, 6, 100, 0, 99), Is.EqualTo(2));
			Assert.That(SupplyFleetMath.DesiredTrucks(30, 6, 100, 0, 99), Is.EqualTo(5));
		}

		[Test]
		public void DesiredTrucks_RoundsUpSoRemainderDemandIsServed()
		{
			// Seven men against a six-man load is two trucks. Rounding down strands the seventh forever.
			Assert.That(SupplyFleetMath.DesiredTrucks(7, 6, 100, 0, 99), Is.EqualTo(2));
			Assert.That(SupplyFleetMath.DesiredTrucks(1, 6, 100, 0, 99), Is.EqualTo(1));
		}

		[Test]
		public void DesiredTrucks_OverProvisionAlsoRoundsUp()
		{
			// ceil(1 * 150 / 100) = 2, not 1.
			Assert.That(SupplyFleetMath.DesiredTrucks(6, 6, 150, 0, 99), Is.EqualTo(2));
		}

		// ===== (3) The floor =====

		[Test]
		public void DesiredTrucks_FloorHeldWithNoStarvingCustomers()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(0, 6, 200, 2, 6), Is.EqualTo(2));
		}

		[Test]
		public void DesiredTrucks_ZeroFloorAllowsAnEmptyFleetWhenNobodyIsStarving()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(0, 6, 200, 0, 6), Is.EqualTo(0));
		}

		// ===== (4) The ceiling =====

		[Test]
		public void DesiredTrucks_CeilingBindsHoweverBadTheFrontGets()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(500, 6, 200, 2, 6), Is.EqualTo(6));
			Assert.That(SupplyFleetMath.DesiredTrucks(int.MaxValue, 6, 200, 2, 6), Is.EqualTo(6));
		}

		[Test]
		public void DesiredTrucks_CeilingBelowFloorIsRaisedToTheFloorNotLeftEmpty()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(50, 6, 100, 4, 2), Is.EqualTo(4));
		}

		// ===== (5) Over-provision is the tunable =====

		[Test]
		public void DesiredTrucks_HundredPercentIsTheHonestNumber()
		{
			for (var starving = 0; starving <= 40; starving++)
			{
				var honest = (starving + 5) / 6;
				Assert.That(SupplyFleetMath.DesiredTrucks(starving, 6, 100, 0, 99), Is.EqualTo(honest),
					$"starving={starving}");
			}
		}

		[Test]
		public void DesiredTrucks_TuningOvercompensationDownShrinksTheFleet()
		{
			var bold = SupplyFleetMath.DesiredTrucks(12, 6, 200, 2, 6);
			var timid = SupplyFleetMath.DesiredTrucks(12, 6, 100, 2, 6);

			Assert.That(bold, Is.GreaterThan(timid));
		}

		[Test]
		public void DesiredTrucks_IsMonotonicInDemand()
		{
			var previous = 0;
			for (var starving = 0; starving <= 200; starving++)
			{
				var desired = SupplyFleetMath.DesiredTrucks(starving, 6, 200, 2, 6);
				Assert.That(desired, Is.GreaterThanOrEqualTo(previous), $"starving={starving}");
				previous = desired;
			}
		}

		// ===== (6) Degenerate config =====

		[Test]
		public void DesiredTrucks_NonPositiveCustomersPerTruckReadsAsOne()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(3, 0, 100, 0, 99), Is.EqualTo(3));
			Assert.That(SupplyFleetMath.DesiredTrucks(3, -5, 100, 0, 99), Is.EqualTo(3));
		}

		[Test]
		public void DesiredTrucks_NonPositiveOvercompensationReadsAsHonest()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(12, 6, 0, 0, 99), Is.EqualTo(2));
			Assert.That(SupplyFleetMath.DesiredTrucks(12, 6, -100, 0, 99), Is.EqualTo(2));
		}

		[Test]
		public void DesiredTrucks_NegativeInputsClampToZeroRatherThanUnderflow()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(-10, 6, 200, 0, 6), Is.EqualTo(0));
			Assert.That(SupplyFleetMath.DesiredTrucks(12, 6, 200, -3, 6), Is.EqualTo(4));
		}

		[Test]
		public void DesiredTrucks_ExtremeOvercompensationDoesNotOverflow()
		{
			Assert.That(SupplyFleetMath.DesiredTrucks(1000000, 1, int.MaxValue, 0, 6), Is.EqualTo(6));
		}

		// ===== The standing floor with a denominator (SupplyTruckFloorPer) =====
		//
		// SCOPE — READ THIS BEFORE TRUSTING THESE AS REGRESSION COVER. These cases pin the ARITHMETIC of a
		// scaled floor clamped by DesiredTrucks, and nothing else. The helper below composes the two pure
		// functions HERE, in the test file; it does not call UnitBuilderBotModule. Both functions are
		// unchanged by the commit that added these tests, so **every case here passes on the unfixed code**
		// — delete the three wiring lines in SupplyFleetUnderDesired and the suite stays green.
		//
		// What is therefore NOT covered: that the module actually passes EffectiveFloor's result into
		// DesiredTrucks rather than the raw Info field, which is precisely where the defect lived. That join
		// needs a world (Rearmable traits on owned actors, a per-tick cache), so it is out of reach of this
		// fixture and is not claimed. The cheap partial substitute is a config lint — any profile setting
		// SupplyDemandSizing with a positive SupplyTruckFloor should also set SupplyTruckFloorPer — which
		// catches a YAML regression but still not a C# one.
		//
		// Shipped @experimental config: cap 3, per 10, customersPerTruck 6, overcompensation 200, ceiling 6.
		static int DesiredWithScaledFloor(int starving, int customers, int cap, int per)
		{
			return SupplyFleetMath.DesiredTrucks(starving, 6, 200,
				SupportFloorMath.EffectiveFloor(cap, per, customers), 6);
		}

		[Test]
		public void ScaledFloor_ZeroCustomersHoldsNoStandingFleet()
		{
			// The t=0 case the user reported first (PIPELINE 57(a)): no infantry, so no floor, so no truck is
			// called in before anyone has fired a shot. A constant floor cannot have this property.
			Assert.That(DesiredWithScaledFloor(0, 0, 3, 10), Is.EqualTo(0));

			// And it stays zero until the ratio is actually met, rather than rounding one up early.
			Assert.That(DesiredWithScaledFloor(0, 9, 3, 10), Is.EqualTo(0));
		}

		[Test]
		public void ScaledFloor_PhasesInWithTheInfantryItResupplies()
		{
			Assert.That(DesiredWithScaledFloor(0, 10, 3, 10), Is.EqualTo(1));
			Assert.That(DesiredWithScaledFloor(0, 20, 3, 10), Is.EqualTo(2));
			Assert.That(DesiredWithScaledFloor(0, 30, 3, 10), Is.EqualTo(3));
		}

		[Test]
		public void ScaledFloor_IsCappedByTheFlatFloorNotByTheCeiling()
		{
			// A large army must not turn the standing reserve into the whole fleet: the cap binds well below
			// the ceiling, leaving the remaining headroom for actual measured starvation.
			Assert.That(DesiredWithScaledFloor(0, 800, 3, 10), Is.EqualTo(3));
		}

		[Test]
		public void ScaledFloor_DemandStillOutranksTheReserve()
		{
			// The floor is a MINIMUM, never a maximum: measured starvation must still size the fleet above the
			// reserve, up to the ceiling. 18 starving at 6 per truck x 200% = 6.
			Assert.That(DesiredWithScaledFloor(18, 10, 3, 10), Is.EqualTo(6));
		}

		[Test]
		public void ScaledFloor_UnconfiguredRatioKeepsTheFlatFloorVerbatim()
		{
			// The byte-identity contract: per <= 0 is the default, so every profile that does not opt in keeps
			// its existing answer. (@stable never reaches this path at all — SupplyDemandSizing gates it.)
			Assert.That(DesiredWithScaledFloor(0, 0, 2, 0), Is.EqualTo(2));
			Assert.That(DesiredWithScaledFloor(0, 100, 2, 0), Is.EqualTo(2));
		}
	}
}
