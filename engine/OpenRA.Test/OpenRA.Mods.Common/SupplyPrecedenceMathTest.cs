#region Copyright & License Information
/*
 * WW3MOD @experimental — resupply precedence tests.
 *
 * Pins the two decisions that turn "soldiers out of ammo are useless, that should be the first priority to
 * solve at all times" (user ruling 2026-08-15) into procurement behaviour:
 *   (1) SizingCustomers — the fleet is sized from the bar at which SupplyProvider actually SERVES a customer,
 *       not from the stricter starving bar that read 0 at every snapshot of a match in which ammo-need was
 *       continuously True.
 *   (2) ShouldBankCycle — a cycle may buy NOTHING and bank toward a truck it cannot yet afford, BOUNDED so
 *       precedence can never become a production deadlock.
 *
 * The two cases that matter most are BankingIsGatedOnDemand (this must not become a floor — a floor with no
 * denominator is the bug the user reported first, twice) and BankingIsBounded (unbounded precedence starves
 * the whole army). Pure integer/boolean decisions; no world mounted.
 */
#endregion

using OpenRA.Mods.Common.Traits;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyPrecedenceMathTest
	{
		// ---- ShouldBankCycle ----

		[Test]
		public void BanksWhileShortUnaffordableAndWithinBudget()
		{
			// The measured case: fleet short, ~100 cash against a 1000 truck. Falling through here is what
			// spends the truck's money on a rifleman and guarantees the truck is never bought.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, 0, 12), Is.True);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, 11, 12), Is.True);
		}

		[Test]
		public void BankingIsGatedOnDemand()
		{
			// THE GUARD THAT KEEPS THIS FROM BECOMING SupplyTruckFloor. With nobody dry the fleet is not
			// short, so banking must be impossible no matter how poor we are — otherwise this reproduces the
			// t=0 opening-buy bug the user reported first as two trucks and again as two medics.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(false, false, 0, 12), Is.False);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(false, true, 0, 12), Is.False);
		}

		[Test]
		public void NeverBanksWhatItCouldSimplyBuy()
		{
			// If the truck is affordable the caller buys it outright; banking would delay the very thing the
			// ruling prioritises.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, true, 0, 12), Is.False);
		}

		[TestCase(12)]
		[TestCase(13)]
		[TestCase(1000)]
		public void BankingIsBounded(int alreadyBanked)
		{
			// Unbounded precedence is a deadlock: an army whose income never reaches the truck price would
			// buy nothing at all, forever. Past the bound the cycle falls through and ordinary buying resumes.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, alreadyBanked, 12), Is.False);
		}

		[TestCase(0)]
		[TestCase(-1)]
		public void BankCyclesOffReproducesTheFallThrough(int maxBank)
		{
			// The off-switch contract: the default (0) must return false for every input combination, so the
			// pre-feature answer comes back verbatim.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, 0, maxBank), Is.False);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, true, 5, maxBank), Is.False);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(false, false, 0, maxBank), Is.False);
		}

		// ---- SizingCustomers ----

		[Test]
		public void SizingOffReturnsTheStarvingCountVerbatim()
		{
			Assert.That(SupplyPrecedenceMath.SizingCustomers(false, 3, 9), Is.EqualTo(3));
			Assert.That(SupplyPrecedenceMath.SizingCustomers(false, 0, 9), Is.EqualTo(0));
		}

		[Test]
		public void SizingOnUsesTheServiceBar()
		{
			// The measured match, in one line: starving=0 at every snapshot while men were demonstrably dry.
			// Sizing from the serving bar is what makes DesiredTrucks non-zero and lets the pre-empt fire.
			Assert.That(SupplyPrecedenceMath.SizingCustomers(true, 0, 6), Is.EqualTo(6));
		}

		[Test]
		public void SwitchingSizingOnCanOnlyRaiseTheFleet()
		{
			// The need bar is looser than the starving bar, so needy should dominate — but if a threshold
			// edit ever crossed them, the max keeps this change one-directional instead of silently
			// shrinking the fleet.
			Assert.That(SupplyPrecedenceMath.SizingCustomers(true, 7, 2), Is.EqualTo(7));
			Assert.That(SupplyPrecedenceMath.SizingCustomers(true, 7, 7), Is.EqualTo(7));
		}

		[Test]
		public void NegativeCountsAreClampedNotTrusted()
		{
			Assert.That(SupplyPrecedenceMath.SizingCustomers(true, -4, -9), Is.EqualTo(0));
			Assert.That(SupplyPrecedenceMath.SizingCustomers(false, -4, 9), Is.EqualTo(0));
		}

		// ---- The composed behaviour the fix depends on ----

		[Test]
		public void ServiceBarSizingProducesAFleetWhereStarvingBarProducedNone()
		{
			// End-to-end on the pure math, reproducing the measured numbers: six needy customers, none below
			// the 25% starving bar, SupplyCustomersPerTruck 6, floor 0. The old input asks for zero trucks;
			// the new input asks for one. This is the arithmetic the whole branch turns on.
			var oldInput = SupplyPrecedenceMath.SizingCustomers(false, 0, 6);
			var newInput = SupplyPrecedenceMath.SizingCustomers(true, 0, 6);

			Assert.That(SupplyFleetMath.DesiredTrucks(oldInput, 6, 100, 0, 6), Is.EqualTo(0));
			Assert.That(SupplyFleetMath.DesiredTrucks(newInput, 6, 100, 0, 6), Is.EqualTo(1));
		}
	}
}
