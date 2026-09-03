#region Copyright & License Information
/*
 * WW3MOD @experimental — Logistics Center restock-dispatch tests.
 *
 * Pins USER RULING 2026-09-03: "Bots needs to learn how to resupply the LC... by sending a truck to it, to
 * transfer supplies to the LC from the truck. But make sure that works both for bots and humans."
 *
 * WHAT THESE DO NOT TEST, STATED SO THE SUITE IS NOT MISREAD: the TRANSFER itself. That already worked for
 * humans before this change and is not touched by it — DropsSupplyCache.ResolveOrder:312 accepts the order
 * because LOGISTICSCENTER carries AbsorbsSupplyCache, and Activities/DeliverSupply.cs:148-154 performs
 * DeductSupply/AddSupply through SupplyTransferMath.AmountToDeliver. The bot now issues that same order, so
 * "works for both" is achieved by SHARING the path, not by mirroring it. What is new, and what is tested
 * here, is only the DISPATCH DECISION: which Centre needs a truck, which truck to send, and when to hand it
 * back.
 *
 * The prices are the shipped ones so the arithmetic is checkable: LOGISTICSCENTER SupplyProvider TotalSupply
 * 2250 (structures.yaml:469), TRUK 750.
 *
 * Pure integer/boolean decisions; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class LogisticsCenterRestockMathTest
	{
		const int CentreCapacity = 2250;
		const int TruckFull = 750;
		const int Threshold = 500;   // per mille — half empty
		const int MinDelivery = 250;
		const int MaxDistance = 40;

		// ================= WHEN A CENTRE WANTS A TRUCK =================

		[Test]
		public void AFreshlyDeployedCentreIsFullAndWantsNothing()
		{
			// THE DISCLOSED BUG THIS MUST NOT REPEAT: an LCCV deploys into a Centre that starts FULL at
			// 2250/2250, and the first human delivery ever attempted "drove the whole way and transferred
			// nothing". A bot that dispatches to a full Centre reproduces exactly that, every scan.
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(2250, CentreCapacity, Threshold), Is.False);
		}

		[Test]
		public void ACentreBelowHalfWantsATruck()
		{
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(1124, CentreCapacity, Threshold), Is.True);
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(0, CentreCapacity, Threshold), Is.True);

			// Exactly at the bar is NOT below it — the threshold is a strict "under".
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(1125, CentreCapacity, Threshold), Is.False);
		}

		[Test]
		public void TheThresholdIsAFractionSoItMeansTheSameAtEveryCapacity()
		{
			// The same trait runs at 2250 on a Centre and 750 on a truck. Half empty is half empty at both;
			// an absolute bar would mean two different things.
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(374, TruckFull, Threshold), Is.True);
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(375, TruckFull, Threshold), Is.False);
		}

		[Test]
		public void ZeroCapacityOrZeroThresholdNeverAsksForATruck()
		{
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(0, 0, Threshold), Is.False);
			Assert.That(LogisticsCenterRestockMath.CentreNeedsRestock(0, CentreCapacity, 0), Is.False);
		}

		// ================= WHETHER A GIVEN TRUCK IS WORTH SENDING =================

		[Test]
		public void AFullTruckToAnEmptyCentreIsWorthTheDrive()
		{
			Assert.That(
				LogisticsCenterRestockMath.WorthDispatching(TruckFull, CentreCapacity, 10, MinDelivery, MaxDistance),
				Is.True);
		}

		[Test]
		public void ATrickleIsNotWorthTheDrive()
		{
			// The anti-oscillation term: a truck that delivers 200 drops under the follower's
			// RestockThreshold, is released as spent, and is immediately re-dispatched.
			Assert.That(
				LogisticsCenterRestockMath.WorthDispatching(200, CentreCapacity, 10, MinDelivery, MaxDistance),
				Is.False);
			Assert.That(
				LogisticsCenterRestockMath.WorthDispatching(250, CentreCapacity, 10, MinDelivery, MaxDistance),
				Is.True);
		}

		[Test]
		public void HeadroomCapsTheTransferAndCanItselfRefuse()
		{
			// A nearly-full Centre has room for 100, so a full truck still only moves 100 — under the bar.
			Assert.That(LogisticsCenterRestockMath.TransferableAmount(TruckFull, 100), Is.EqualTo(100));
			Assert.That(
				LogisticsCenterRestockMath.WorthDispatching(TruckFull, 100, 10, MinDelivery, MaxDistance),
				Is.False);

			Assert.That(LogisticsCenterRestockMath.TransferableAmount(TruckFull, 0), Is.EqualTo(0));
			Assert.That(LogisticsCenterRestockMath.TransferableAmount(0, CentreCapacity), Is.EqualTo(0));
		}

		[Test]
		public void ATruckAcrossTheMapIsNotWorthTheDrive()
		{
			Assert.That(
				LogisticsCenterRestockMath.WorthDispatching(TruckFull, CentreCapacity, 40, MinDelivery, MaxDistance),
				Is.True);
			Assert.That(
				LogisticsCenterRestockMath.WorthDispatching(TruckFull, CentreCapacity, 41, MinDelivery, MaxDistance),
				Is.False);

			// 0 disables the distance guard entirely.
			Assert.That(
				LogisticsCenterRestockMath.WorthDispatching(TruckFull, CentreCapacity, 400, MinDelivery, 0),
				Is.True);
		}

		// ================= WHICH TRUCK =================

		[Test]
		public void TheNearerTruckWinsEvenWhenItIsCarryingLess()
		{
			// Distance dominates: the Centre needs stock SOON, and the fullest truck is routinely the one
			// on the far side of the army. Lower rank is better.
			var near = LogisticsCenterRestockMath.DispatchRank(5, 300);
			var farFull = LogisticsCenterRestockMath.DispatchRank(20, 750);
			Assert.That(near, Is.LessThan(farFull));
		}

		[Test]
		public void LoadOnlySeparatesTrucksThatAreEquallyClose()
		{
			var closeFull = LogisticsCenterRestockMath.DispatchRank(5, 750);
			var closeLight = LogisticsCenterRestockMath.DispatchRank(5, 300);
			Assert.That(closeFull, Is.LessThan(closeLight));
		}

		[Test]
		public void NoRealisticLoadCanBridgeOneCellOfDistance()
		{
			// The scale factor is what makes distance strictly dominant. If this ever fails, a very full
			// truck can out-rank a much nearer one and the dispatch starts choosing badly.
			var oneCellNearerEmpty = LogisticsCenterRestockMath.DispatchRank(5, 0);
			var oneCellFurtherHuge = LogisticsCenterRestockMath.DispatchRank(6, 99999);
			Assert.That(oneCellNearerEmpty, Is.LessThan(oneCellFurtherHuge));
		}

		// ================= HANDING THE TRUCK BACK =================

		[Test]
		public void EveryEndingConditionReleasesTheClaim()
		{
			// THE HALF THAT GOES WRONG. A module that claims and forgets leaves the truck alive-and-claimed
			// forever, invisible to SupplyFollowerBotModule — the bot silently loses its supply fleet. Each
			// condition is pinned separately so none can be dropped without a test noticing.
			Assert.That(LogisticsCenterRestockMath.ErrandEnded(true, false, false, false, false), Is.True, "truck gone");
			Assert.That(LogisticsCenterRestockMath.ErrandEnded(false, true, false, false, false), Is.True, "centre gone/captured");
			Assert.That(LogisticsCenterRestockMath.ErrandEnded(false, false, true, false, false), Is.True, "truck empty");
			Assert.That(LogisticsCenterRestockMath.ErrandEnded(false, false, false, true, false), Is.True, "centre full");
			Assert.That(LogisticsCenterRestockMath.ErrandEnded(false, false, false, false, true), Is.True, "truck idle");
		}

		[Test]
		public void ARunningErrandIsNotReleased()
		{
			// The mirror, and it is the one that matters for order spam: a truck still driving must keep its
			// claim, or the next scan re-dispatches it and resets the drive forever.
			Assert.That(LogisticsCenterRestockMath.ErrandEnded(false, false, false, false, false), Is.False);
		}
	}
}
