#region Copyright & License Information
/*
 * WW3MOD supply-truck logistics (@experimental) — sector assignment + evac geometry test.
 *
 * Pins the decisions SupplyFollowerBotModule turns into Move orders when the @experimental keys are on, so
 * "trucks spread across sectors and evacuate the fire" can't silently regress:
 *   (1) SECTOR SPREAD — trucks claim DISTINCT clusters (neediest first); only double up when trucks
 *       outnumber in-range clusters; out-of-range clusters are never claimed.
 *   (2) DETERMINISM — identical synced inputs give an identical assignment (no random draws), and the
 *       result is independent of anything but the caller's stable truck order.
 *   (3) EVAC DECISION — pull back when the higher of the truck / cluster danger meets the threshold.
 *   (4) EVAC GEOMETRY — the retreat point sits the retreat distance toward the SR, clamped to not overshoot.
 * Pure math over synthetic positions; no world mounted.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyLogisticsMathTest
	{
		const int Cell = 1024;

		static int Cells(int n) => n * Cell;

		static WPos Pos(int xCells, int yCells) => new(Cells(xCells), Cells(yCells), 0);

		static SupplyLogisticsMath.Sector Sector(int xCells, int yCells, int need) =>
			new(Pos(xCells, yCells), need);

		// A generous follow range so eligibility is not the thing under test unless a case places a sector far.
		const int MaxFollow = 100 * Cell;

		[Test]
		public void Spread_TwoTrucks_TakeTwoDistinctClusters_NeediestFirst()
		{
			// Two trucks co-located at origin; two clusters, one needier. Each truck should take a DISTINCT
			// cluster, and the neediest is claimed first.
			var trucks = new List<WPos> { Pos(0, 0), Pos(0, 0) };
			var sectors = new List<SupplyLogisticsMath.Sector>
			{
				Sector(10, 0, need: 5),   // index 0 — less needy
				Sector(0, 10, need: 20),  // index 1 — needier
			};

			var assign = SupplyLogisticsMath.AssignSectors(trucks, sectors, MaxFollow);

			Assert.That(assign[0], Is.EqualTo(1), "first truck takes the neediest cluster");
			Assert.That(assign[1], Is.EqualTo(0), "second truck takes the OTHER cluster (dedup), not the same one");
		}

		[Test]
		public void Spread_DoublesUp_OnlyWhenTrucksOutnumberClusters()
		{
			// Three trucks, two clusters. Two trucks take the two distinct clusters; the third has no unserved
			// cluster left and doubles up on the neediest in-range one.
			var trucks = new List<WPos> { Pos(0, 0), Pos(0, 0), Pos(0, 0) };
			var sectors = new List<SupplyLogisticsMath.Sector>
			{
				Sector(10, 0, need: 20),  // index 0 — neediest
				Sector(0, 10, need: 5),   // index 1
			};

			var assign = SupplyLogisticsMath.AssignSectors(trucks, sectors, MaxFollow);

			Assert.That(assign[0], Is.EqualTo(0), "truck 0 → neediest");
			Assert.That(assign[1], Is.EqualTo(1), "truck 1 → the other (distinct) cluster");
			Assert.That(assign[2], Is.EqualTo(0), "truck 2 doubles up on the neediest only after all are served");
		}

		[Test]
		public void Spread_NearestUnservedWins_WhenNeedTies()
		{
			// Equal need → the closer cluster is preferred. Truck 0 at origin, truck 1 far east.
			var trucks = new List<WPos> { Pos(0, 0), Pos(30, 0) };
			var sectors = new List<SupplyLogisticsMath.Sector>
			{
				Sector(2, 0, need: 10),   // index 0 — near truck 0
				Sector(30, 0, need: 10),  // index 1 — on top of truck 1
			};

			var assign = SupplyLogisticsMath.AssignSectors(trucks, sectors, MaxFollow);

			Assert.That(assign[0], Is.EqualTo(0), "truck 0 takes its nearest on a need tie");
			Assert.That(assign[1], Is.EqualTo(1), "truck 1 takes its nearest distinct cluster");
		}

		[Test]
		public void Spread_OutOfRangeClusters_AreNeverClaimed()
		{
			// The only cluster is beyond the follow range → the truck gets nothing.
			var trucks = new List<WPos> { Pos(0, 0) };
			var sectors = new List<SupplyLogisticsMath.Sector> { Sector(50, 0, need: 100) };

			var assign = SupplyLogisticsMath.AssignSectors(trucks, sectors, 10 * Cell);

			Assert.That(assign[0], Is.EqualTo(SupplyLogisticsMath.NoSector), "an out-of-range cluster is not served");
		}

		[Test]
		public void Spread_UnservedElsewhere_BeatsAServedNearer_Cluster()
		{
			// Truck 1 is nearest to cluster 0 (already served by truck 0) but should still prefer the farther
			// UNSERVED cluster 1 — dedup dominates distance.
			var trucks = new List<WPos> { Pos(0, 0), Pos(1, 0) };
			var sectors = new List<SupplyLogisticsMath.Sector>
			{
				Sector(1, 0, need: 50),   // index 0 — neediest, nearest to both
				Sector(8, 0, need: 10),   // index 1 — farther, less needy, but unserved
			};

			var assign = SupplyLogisticsMath.AssignSectors(trucks, sectors, MaxFollow);

			Assert.That(assign[0], Is.EqualTo(0), "truck 0 claims the neediest");
			Assert.That(assign[1], Is.EqualTo(1), "truck 1 prefers the farther UNSERVED cluster over the served near one");
		}

		[Test]
		public void Spread_IsDeterministic_SameInputsSameAssignment()
		{
			var trucks = new List<WPos> { Pos(3, 4), Pos(-2, 7), Pos(5, -1) };
			var sectors = new List<SupplyLogisticsMath.Sector>
			{
				Sector(4, 4, need: 12),
				Sector(-2, 6, need: 12),
				Sector(6, 0, need: 30),
			};

			var a1 = SupplyLogisticsMath.AssignSectors(trucks, sectors, MaxFollow);
			var a2 = SupplyLogisticsMath.AssignSectors(trucks, sectors, MaxFollow);

			Assert.That(a1, Is.EqualTo(a2), "assignment is a pure function of its inputs");
		}

		[Test]
		public void Evacuate_TriggersOnEitherReading()
		{
			Assert.That(SupplyLogisticsMath.ShouldEvacuate(dangerAtTruck: 70, dangerAtCluster: 0, threshold: 60),
				Is.True, "high danger at the truck itself triggers evac");
			Assert.That(SupplyLogisticsMath.ShouldEvacuate(dangerAtTruck: 0, dangerAtCluster: 90, threshold: 60),
				Is.True, "high danger at the cluster centroid triggers evac");
			Assert.That(SupplyLogisticsMath.ShouldEvacuate(dangerAtTruck: 60, dangerAtCluster: 0, threshold: 60),
				Is.True, "the threshold is inclusive (>=)");
			Assert.That(SupplyLogisticsMath.ShouldEvacuate(dangerAtTruck: 30, dangerAtCluster: 45, threshold: 60),
				Is.False, "ambient danger below the threshold does not evac");
		}

		[Test]
		public void RetreatTarget_StepsTowardSR_ByTheRetreatDistance()
		{
			var truck = Pos(20, 0);
			var sr = Pos(0, 0);

			var retreat = SupplyLogisticsMath.RetreatTarget(truck, sr, 8 * Cell);

			// On the truck→SR bearing (due west), 8 cells closer to the SR.
			Assert.That(retreat.Y, Is.EqualTo(0), "retreat stays on the truck→SR bearing");
			Assert.That(retreat.X, Is.EqualTo(12 * Cell), "retreat sits the retreat distance toward the SR");
			Assert.That((retreat - sr).HorizontalLength, Is.LessThan((truck - sr).HorizontalLength),
				"the truck ends up closer to the SR than it started");
		}

		[Test]
		public void RetreatTarget_ClampsToSR_WhenRetreatExceedsDistance()
		{
			var truck = Pos(5, 0);
			var sr = Pos(0, 0);

			// Retreat farther than the truck is from the SR → clamp exactly to the SR, never overshoot past it.
			var retreat = SupplyLogisticsMath.RetreatTarget(truck, sr, 50 * Cell);

			Assert.That(retreat, Is.EqualTo(sr), "an over-long retreat clamps to the SR");
		}

		[Test]
		public void RetreatTarget_DegenerateSameCell_ReturnsSR()
		{
			var here = Pos(7, 7);
			Assert.That(SupplyLogisticsMath.RetreatTarget(here, here, 4 * Cell), Is.EqualTo(here),
				"truck already at the SR stays put (no division by zero)");
		}
	}
}
