#region Copyright & License Information
/*
 * WW3MOD — supply-transfer arbitration tests.
 *
 * Pins the gesture polarity from the user ruling of 2026-08-30:
 *
 *     "the default action for trucks when ordered to an LC should be to resupply the LC, unless they
 *      are empty then they are themselves resupplied. If we use 'force-move' it could be inverted, so
 *      force move to a LC means it resupplies the truck."
 *
 * The polarity SHIPPED INVERTED before this change — a plain click restocked the truck and only Ctrl
 * delivered — so these are not tests of a fresh feature but of a deliberate reversal, and the cases
 * the ruling names are asserted by name so the reversal cannot be quietly undone.
 *
 * "EMPTY" WAS SUBSEQUENTLY RULED TO MEAN "at or below RestockThreshold", not zero (user, 2026-08-30):
 * a transport at or under 50 receives, above 50 gives. The threshold is the transport's own tuned
 * value, passed in rather than restated here, so these tests use a named constant matching TRUK's.
 *
 * NoInputEverYieldsADirectionThatCannotMoveSupply is the load-bearing one, and it deliberately does
 * NOT assert disjointness: the method returns a single-valued enum, so "it cannot answer twice" is
 * not a property a test can fail. What matters is that every direction returned can actually MOVE
 * supply on arrival — because these two directions are offered by separate IOrderTargeters at
 * priority 6 and 7, and a targeter that accepts a click it cannot act on both draws a cursor over a
 * no-op and silently vetoes Repairable at priority 5.
 *
 * Pure integer/boolean decisions; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyTransferMathTest
	{
		const int Capacity = 750;
		const int HostCapacity = 2250;

		// TRUK's shipping SupplyProvider.RestockThreshold. The production code reads this off the
		// transport rather than restating it; the constant here is only so the boundary cases below can
		// name the number the user ruled on.
		const int Threshold = 50;

		// A Centre with room to receive and stock to give, so a case that means to exercise the
		// TRANSPORT's side is not silently decided by the host's.
		static SupplyTransferDirection Resolve(bool forceMove, int carried)
		{
			return SupplyTransferMath.ResolveDirection(
				forceMove, carried, Capacity, Threshold, HostCapacity / 2, HostCapacity, true, true);
		}

		// ---- The four cases the ruling names ----

		[Test]
		public void LoadedTruckOnNormalOrderFillsTheCentre()
		{
			Assert.That(Resolve(false, Capacity), Is.EqualTo(SupplyTransferDirection.ToHost));
		}

		[Test]
		public void PartiallyLoadedTruckOnNormalOrderStillFillsTheCentre()
		{
			// "has supply worth giving" is the test, not "is full" — a truck that has served anybody is
			// the common case, and gating delivery on a full load is what made the feature unreachable
			// before.
			Assert.That(Resolve(false, Capacity / 2), Is.EqualTo(SupplyTransferDirection.ToHost));
		}

		[Test]
		public void EmptyTruckOnNormalOrderIsServedInstead()
		{
			Assert.That(Resolve(false, 0), Is.EqualTo(SupplyTransferDirection.ToTruck));
		}

		// ---- The threshold boundary the user ruled on: at or under 50 receives, above 50 gives ----

		[Test]
		public void AtTheThresholdTheTruckIsServedNotDrained()
		{
			// EXACTLY 50. The ruling is inclusive on the receiving side, and this is the assertion that
			// says so — an off-by-one here is invisible in play and turns the last 50 supply of every
			// truck into a dribble-and-refill loop, which is the behaviour the threshold was chosen to
			// prevent.
			Assert.That(Resolve(false, Threshold), Is.EqualTo(SupplyTransferDirection.ToTruck));
		}

		[Test]
		public void OneAboveTheThresholdTheTruckGives()
		{
			Assert.That(Resolve(false, Threshold + 1), Is.EqualTo(SupplyTransferDirection.ToHost));
		}

		[Test]
		public void AThresholdOfZeroRestoresTheLiteralEmptyMeansZeroReading()
		{
			// The parameter is the transport's own tuned value, so a transport configured with no
			// threshold must still behave sanely rather than depending on the ruling's 50.
			Assert.That(
				SupplyTransferMath.ResolveDirection(
					false, 1, Capacity, 0, HostCapacity / 2, HostCapacity, hostAbsorbs: true, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.ToHost));

			Assert.That(
				SupplyTransferMath.ResolveDirection(
					false, 0, Capacity, 0, HostCapacity / 2, HostCapacity, hostAbsorbs: true, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.ToTruck));
		}

		[Test]
		public void ForceMoveIsServedRegardlessOfLoad()
		{
			Assert.That(Resolve(true, Capacity - 1), Is.EqualTo(SupplyTransferDirection.ToTruck));
			Assert.That(Resolve(true, 1), Is.EqualTo(SupplyTransferDirection.ToTruck));
			Assert.That(Resolve(true, 0), Is.EqualTo(SupplyTransferDirection.ToTruck));
		}

		// ---- No cursor over a no-op ----

		[Test]
		public void ForceMoveOnAFullTruckIsRefusedSoRepairCanHaveTheClick()
		{
			// Nothing left to receive. Refusing is not merely tidy: it is the ONLY way the pre-existing
			// repair gesture stays reachable, because Repairable's targeter sits below both of these at
			// priority 5 and only ever sees clicks that neither direction claimed. Steering this case to
			// Restock instead — as the predecessor of this method did — sent the truck on a drive that
			// transferred nothing and repaired nothing, under an enter cursor that promised service.
			Assert.That(Resolve(true, Capacity), Is.EqualTo(SupplyTransferDirection.None));
		}

		[Test]
		public void AHostThatCannotAbsorbTakesNoDelivery()
		{
			Assert.That(
				SupplyTransferMath.ResolveDirection(
					false, Capacity, Capacity, Threshold, 0, HostCapacity, hostAbsorbs: false, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.None));
		}

		[Test]
		public void AHostThatCannotDockServesNobody()
		{
			// A ground crate absorbs but cannot dock a truck; an empty truck clicking one must not
			// resolve to ToTruck, which is the path that assumes an arrival gate.
			Assert.That(
				SupplyTransferMath.ResolveDirection(
					false, 0, Capacity, Threshold, HostCapacity, HostCapacity, hostAbsorbs: true, hostDocks: false),
				Is.EqualTo(SupplyTransferDirection.None));
		}

		// ---- The host's pool decides too, in BOTH directions ----

		[Test]
		public void ALoadedTruckIsRefusedByACentreWithNoRoom()
		{
			// THE FIRST THING A PLAYER DOES WITH THIS GESTURE. SupplyProvider initialises currentSupply
			// from TotalSupply, so a Centre deployed from an LCCV starts FULL — and the first cut of
			// ResolveDirection could not see that, so it drew the wrench, drove the truck the whole way,
			// and transferred nothing.
			Assert.That(
				SupplyTransferMath.ResolveDirection(
					false, Capacity, Capacity, Threshold, HostCapacity, HostCapacity, hostAbsorbs: true, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.None));
		}

		[Test]
		public void ALoadedTruckIsAcceptedByACentreWithOneUnitOfRoom()
		{
			// The boundary on the other side of the same test, so the guard cannot be satisfied by
			// refusing everything.
			Assert.That(
				SupplyTransferMath.ResolveDirection(
					false, Capacity, Capacity, Threshold, HostCapacity - 1, HostCapacity, hostAbsorbs: true, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.ToHost));
		}

		[Test]
		public void AnEmptyTruckIsRefusedByADrainedCentre()
		{
			// The mirror. NearestRestockHost has always required CurrentSupply > 0 when picking a host
			// automatically; the targeter never did, so the click promised service a drained Centre
			// could not give.
			Assert.That(
				SupplyTransferMath.ResolveDirection(
					false, 0, Capacity, Threshold, 0, HostCapacity, hostAbsorbs: true, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.None));
		}

		[Test]
		public void ForceMoveIsRefusedByADrainedCentreToo()
		{
			Assert.That(
				SupplyTransferMath.ResolveDirection(
					true, 1, Capacity, Threshold, 0, HostCapacity, hostAbsorbs: true, hostDocks: true),
				Is.EqualTo(SupplyTransferDirection.None));
		}

		// ---- ArrivalTolerance: what stops a delivery teleporting ----

		[Test]
		public void ArrivalToleranceCoversTheDiagonalApproachToAThreeByThree()
		{
			// A truck parked legitimately at the diagonal corner of a 3x3 Centre sits at dx=2, dy=2 from
			// the centre cell it was aimed at. A flat margin of 2 REJECTS that (4+4 > 4), which would
			// refuse every real delivery; the footprint radius is what makes the allowance correct.
			var tolerance = SupplyTransferMath.ArrivalTolerance(hostFootprintCells: 3, approachMarginCells: 2);
			Assert.That(tolerance, Is.EqualTo(3));
			Assert.That(SupplyDropMath.ArrivedAtDropCell(2, 2, tolerance), Is.True, "diagonal approach to a 3x3");
			Assert.That(SupplyDropMath.ArrivedAtDropCell(0, 0, tolerance), Is.True, "parked on the centre cell");
		}

		[Test]
		public void ArrivalToleranceStillRejectsATruckThatNeverLeft()
		{
			// The case the guard exists for: an unreachable Centre completes the Move in ~2 ticks at the
			// cell the truck was already on, which must not read as arrival.
			var tolerance = SupplyTransferMath.ArrivalTolerance(hostFootprintCells: 3, approachMarginCells: 2);
			Assert.That(SupplyDropMath.ArrivedAtDropCell(12, 9, tolerance), Is.False);
			Assert.That(SupplyDropMath.ArrivedAtDropCell(4, 0, tolerance), Is.False, "just past the allowance");
		}

		[Test]
		public void ArrivalToleranceDegradesToTheBareMarginForANonBuildingHost()
		{
			Assert.That(SupplyTransferMath.ArrivalTolerance(0, 2), Is.EqualTo(2));
		}

		// ---- AmountToRestock: the mirror direction, which shipped with no arrival term at all ----
		//
		// RestockSupply is documented as the exact mirror of DeliverSupply, but only DeliverSupply ever
		// carried an arrival guard. These pin the missing half. The first case is the one that matters,
		// and note what it asserts: not "the truck takes less" but "the truck takes NOTHING". An
		// unreachable Move completes in about two ticks at the truck's own cell (Move.cs:173-177), so a
		// transfer keyed to completion otherwise fires from anywhere on the map.

		// A 3x3 Logistics Centre approached with the shipping margin: tolerance 3, so 9 squared-cells.
		const int CentreFootprint = 3;
		const int Margin = SupplyTransferMath.DefaultApproachMarginCells;

		static bool Arrived(int dx, int dy)
		{
			return SupplyTransferMath.ArrivedAtHost(dx, dy, CentreFootprint, Margin);
		}

		[Test]
		public void RestockTakesNothingWhenTheTruckNeverReachedTheCentre()
		{
			// 20 cells away — the truck was refused a path and completed its Move where it stood.
			Assert.That(
				SupplyTransferMath.AmountToRestock(Arrived(20, 0), 500, Capacity, HostCapacity),
				Is.EqualTo(0),
				"a truck that never reached the Centre must take NOTHING: an unreachable Move completes "
				+ "at the truck's own cell, so without an arrival term a restock drains a Logistics "
				+ "Centre from anywhere on the map");
		}

		[Test]
		public void RestockStillFillsTheTruckOnTheDiagonalCornerApproach()
		{
			// The guard must not be so tight that it refuses a legitimate park alongside a 3x3: dx=2,
			// dy=2 is 8, inside the tolerance of 9. Getting this wrong breaks the feature in the
			// direction that is hardest to diagnose — every restock silently refusing, with the truck
			// sitting at the Centre and its tank still empty.
			Assert.That(
				SupplyTransferMath.AmountToRestock(Arrived(2, 2), 500, Capacity, HostCapacity),
				Is.EqualTo(250),
				"a truck parked on the diagonal corner of a 3x3 Centre has arrived and takes its shortfall");
		}

		[Test]
		public void RestockIsCappedByWhatTheCentreActuallyHas()
		{
			// No free refills — the mirror of AmountToDeliver's headroom cap, so a truck leaves partially
			// full rather than inventing supply the Centre never had.
			Assert.That(SupplyTransferMath.AmountToRestock(true, 500, Capacity, 90), Is.EqualTo(90));
		}

		[Test]
		public void RestockNeverCreatesSupplyAndNeverGoesNegative()
		{
			Assert.That(SupplyTransferMath.AmountToRestock(true, Capacity, Capacity, HostCapacity), Is.EqualTo(0));
			Assert.That(SupplyTransferMath.AmountToRestock(true, 500, Capacity, 0), Is.EqualTo(0));

			// An over-full transport (AddSupply is documented as able to exceed TotalSupply) must yield a
			// refusal, not a negative transfer that would credit the truck out of thin air.
			Assert.That(SupplyTransferMath.AmountToRestock(true, 900, Capacity, HostCapacity), Is.EqualTo(0));
		}

		// ---- The cursors the call site actually passes ----

		[Test]
		public void TheTwoDirectionsCarryDifferentCursors()
		{
			// Read off the Info defaults rather than restated, because the defect being pinned is that
			// both targeters were constructed with the SAME field. A test that asserted two literals
			// would have passed throughout that bug.
			var info = new DropsSupplyCacheInfo();
			Assert.That(info.DeliverSupplyCursor, Is.EqualTo("goldwrench"));
			Assert.That(info.RestockCursor, Is.EqualTo("enter"));
			Assert.That(info.DeliverSupplyCursor, Is.Not.EqualTo(info.RestockCursor));
		}

		[Test]
		public void NoInputEverYieldsADirectionThatCannotMoveSupply()
		{
			// NOT a disjointness test — a single-valued enum return cannot answer twice, so asserting
			// that it does not is asserting nothing. What is worth pinning is the property the two
			// targeters actually depend on: every direction this returns must be one that CAN move
			// supply on arrival, because a direction that cannot is a cursor over a no-op and, at
			// priority 6/7, a silent veto over Repairable at 5.
			foreach (var forceMove in new[] { false, true })
				foreach (var carried in new[] { 0, 1, Threshold, Threshold + 1, Capacity / 2, Capacity })
					foreach (var hostSupply in new[] { 0, 1, HostCapacity / 2, HostCapacity })
						foreach (var absorbs in new[] { false, true })
							foreach (var docks in new[] { false, true })
							{
								var d = SupplyTransferMath.ResolveDirection(
									forceMove, carried, Capacity, Threshold, hostSupply, HostCapacity, absorbs, docks);

								if (d == SupplyTransferDirection.ToHost)
								{
									Assert.That(forceMove, Is.False, "a force-move must never deliver");
									Assert.That(absorbs, Is.True, "only an absorbing host takes a delivery");
									Assert.That(carried, Is.GreaterThan(Threshold), "a transport at or below the threshold receives");
									Assert.That(
										SupplyTransferMath.AmountToDeliver(carried, hostSupply, HostCapacity),
										Is.GreaterThan(0),
										"a delivery direction must move a positive amount");
								}

								if (d == SupplyTransferDirection.ToTruck)
								{
									Assert.That(docks, Is.True, "only a docking host serves a transport");
									Assert.That(carried, Is.LessThan(Capacity), "a full transport cannot be served");
									Assert.That(hostSupply, Is.GreaterThan(0), "a drained host cannot serve");
								}
							}
		}

		// ---- AmountToDeliver: the partial/partial policy lives here and nowhere else ----

		[Test]
		public void DeliversWhatTheCentreHasHeadroomFor()
		{
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 2000, 2250), Is.EqualTo(250));
		}

		[Test]
		public void DeliversTheWholeLoadWhenItFits()
		{
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 0, 2250), Is.EqualTo(750));
		}

		[Test]
		public void DeliversNothingIntoAFullCentre()
		{
			// A FLOOR, NOT A SANCTIONED OUTCOME. ResolveDirection now refuses the click outright when the
			// Centre has no room, so this arithmetic should be unreachable through the gesture — an
			// earlier revision of this file asserted the same zero while the direction still said ToHost,
			// which had the effect of pinning the defect as correct behaviour. Kept because the transfer
			// re-reads both pools on arrival, after a drive during which the Centre may have filled.
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 2250, 2250), Is.EqualTo(0));
		}

		[Test]
		public void NeverCreatesSupplyAndNeverGoesNegative()
		{
			Assert.That(SupplyTransferMath.AmountToDeliver(0, 100, 2250), Is.EqualTo(0));

			// An over-full host (AddSupply is documented as able to exceed TotalSupply) must yield a
			// refusal, not a negative transfer that would credit the truck out of thin air.
			Assert.That(SupplyTransferMath.AmountToDeliver(750, 3000, 2250), Is.EqualTo(0));
		}
	}
}
