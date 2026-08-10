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
	}
}
