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
		// ---- ShouldBankCycle: the bound is on PROGRESS, not on time ----

		[Test]
		public void BanksWhileShortUnaffordableAndProgressing()
		{
			// The measured case: fleet short, ~100 cash against a 1000 truck, balance still climbing.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, 0, 4), Is.True);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, 3, 4), Is.True);
		}

		[Test]
		public void BankingIsGatedOnDemand()
		{
			// THE GUARD THAT KEEPS THIS FROM BECOMING SupplyTruckFloor. With nobody dry the fleet is not
			// short, so banking must be impossible no matter how poor we are — otherwise this reproduces the
			// t=0 opening-buy bug the user reported first as two trucks and again as two medics.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(false, false, 0, 4), Is.False);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(false, true, 0, 4), Is.False);
		}

		[Test]
		public void NeverBanksWhatItCouldSimplyBuy()
		{
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, true, 0, 4), Is.False);
		}

		[TestCase(4)]
		[TestCase(5)]
		[TestCase(99)]
		public void AbandonsOnceTheBalanceStopsAdvancing(int stalled)
		{
			// The anti-deadlock property. A drained treasury sets no new high, so the spell ends and ordinary
			// production resumes instead of holding silent against money this module does not control.
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, stalled, 4), Is.False);
		}

		[TestCase(0)]
		[TestCase(-1)]
		public void StallCyclesOffReproducesTheFallThrough(int maxStalled)
		{
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, 0, maxStalled), Is.False);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, true, 5, maxStalled), Is.False);
			Assert.That(SupplyPrecedenceMath.ShouldBankCycle(false, false, 0, maxStalled), Is.False);
		}

		// ---- UpdateStall ----

		[Test]
		public void ANewHighResetsTheStall()
		{
			Assert.That(SupplyPrecedenceMath.UpdateStall(500, 400, 3), Is.EqualTo(0));
		}

		[Test]
		public void FlatOrFallingBalanceAccumulatesStall()
		{
			// Equal is NOT progress — a balance pinned by another spender must eventually end the spell.
			Assert.That(SupplyPrecedenceMath.UpdateStall(400, 400, 0), Is.EqualTo(1));
			Assert.That(SupplyPrecedenceMath.UpdateStall(380, 400, 1), Is.EqualTo(2));
		}

		[Test]
		public void AMeasuredHealthySpellNeverAbandons()
		{
			// The real USA trace: two flat cycles and a dip (554 -> 521) while climbing overall. Tolerance 4
			// must ride that out, or the fix stops working on the very economy it was measured against.
			var trace = new long[] { 92, 92, 158, 224, 224, 290, 290, 356, 422, 422, 488, 488, 554, 521, 521, 587, 587, 753, 819 };
			long best = 0;
			var stalled = 0;
			var worst = 0;
			foreach (var cash in trace)
			{
				stalled = SupplyPrecedenceMath.UpdateStall(cash, best, stalled);
				if (cash > best)
					best = cash;

				if (stalled > worst)
					worst = stalled;

				Assert.That(SupplyPrecedenceMath.ShouldBankCycle(true, false, stalled, 4), Is.True,
					$"abandoned a healthy climbing spell at cash={cash}");
			}

			Assert.That(worst, Is.EqualTo(2), "longest non-new-high run in the measured spell");
		}

		[Test]
		public void APinnedBalanceAbandonsQuickly()
		{
			// The real Russia trace: cash pinned 3-9 across a long silent spell because another spender was
			// draining the shared treasury. The old cycle-count bound held production silent for 30 cycles
			// against this; the progress bound must give up within its tolerance.
			var trace = new long[] { 3, 3, 5, 9, 9, 3, 3, 8, 3, 3, 8 };
			long best = 0;
			var stalled = 0;
			var banked = 0;
			foreach (var cash in trace)
			{
				stalled = SupplyPrecedenceMath.UpdateStall(cash, best, stalled);
				if (cash > best)
					best = cash;

				if (SupplyPrecedenceMath.ShouldBankCycle(true, false, stalled, 4))
					banked++;
			}

			Assert.That(banked, Is.LessThan(11), "must not bank the whole pinned spell");
			Assert.That(banked, Is.LessThanOrEqualTo(8));
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
