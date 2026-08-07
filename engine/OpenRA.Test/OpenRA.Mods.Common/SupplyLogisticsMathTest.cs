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

		// ===== EVAC DAMPER (2026-08-07) =====
		// The bare level test above is memoryless, which made the evac branch a limit cycle: the truck drove
		// part-way forward, was ordered back toward the SR, and repeated without ever delivering. These pin the
		// dwell + release deadband that gives the decision memory, and above all the ASYMMETRY — entering an
		// evac is never delayed, only the return to following is.

		const int Threshold = 60;
		const int Hysteresis = 15; // release level 45

		static bool Evac(bool wasEvacuating, int hold, int dangerAtTruck, int dangerAtCluster = 0) =>
			SupplyLogisticsMath.EvacuateWithDwell(wasEvacuating, hold, dangerAtTruck, dangerAtCluster,
				Threshold, Hysteresis);

		[Test]
		public void EvacDwell_EnteringIsNeverDelayed_EvenWithAHoldStanding()
		{
			// THE LOAD-BEARING SAFETY PROPERTY. A truck standing in fire pulls back on the scan that sees the
			// fire, whatever the counter holds. If this ever fails the damper has become able to turn a
			// withdrawal into a last stand.
			Assert.That(Evac(wasEvacuating: false, hold: 0, dangerAtTruck: Threshold), Is.True,
				"danger at the entry threshold evacuates immediately");
			Assert.That(Evac(wasEvacuating: false, hold: 99, dangerAtTruck: Threshold + 40), Is.True,
				"a standing hold never blocks ENTERING an evac");
		}

		[Test]
		public void EvacDwell_HoldsTheRetreat_WhileTheDwellIsLive()
		{
			// Danger has already dropped well under the release level, but the retreat leg is still being
			// driven. Re-deciding here is exactly the flip that produced the oscillation.
			Assert.That(Evac(wasEvacuating: true, hold: 2, dangerAtTruck: 0), Is.True,
				"an evacuating truck stays on the evac branch while the dwell is live");
		}

		[Test]
		public void EvacDwell_ReleaseNeedsTheDangerToFallThroughTheDeadband()
		{
			// Dwell expired (hold 0), so the branch is free to be re-decided — but only a reading clear of the
			// release level lets the truck follow again. A reading parked between the release level and the
			// threshold keeps it out; that band IS the deadband.
			Assert.That(Evac(wasEvacuating: true, hold: 0, dangerAtTruck: 50), Is.True,
				"danger in the deadband (45..59) does not release an evacuating truck");
			Assert.That(Evac(wasEvacuating: true, hold: 0, dangerAtTruck: 45), Is.True,
				"exactly at the release level still counts as hot");
			Assert.That(Evac(wasEvacuating: true, hold: 0, dangerAtTruck: 44), Is.False,
				"below the release level the truck goes back to following");

			// The same reading must NOT pull a following truck out — that asymmetry is the whole point.
			Assert.That(Evac(wasEvacuating: false, hold: 0, dangerAtTruck: 50), Is.False,
				"a following truck is only pulled out at the full entry threshold");
		}

		[Test]
		public void EvacDwell_ClusterTermStillTriggers_WhenTheFrontMovesOntoTheCluster()
		{
			// The caller filters over-threshold clusters out of SELECTION, so this term can now only fire when
			// the cluster went hot AFTER it was chosen — a genuine "conditions changed" evac, which is what the
			// branch was written for. Pinned so the filter is never mistaken for a reason to drop the term.
			Assert.That(Evac(wasEvacuating: false, hold: 0, dangerAtTruck: 0, dangerAtCluster: Threshold),
				Is.True, "a cluster that goes hot after selection still evacuates the truck");
		}

		[Test]
		public void EvacDwell_CounterArmsOnTheEntryEdgeOnly_AndCountsDown()
		{
			// Arming on the ENTRY EDGE is what bounds the retreat to one leg. A counter re-armed on every
			// evacuating scan could never expire while the truck stayed hot, and the dwell would become a latch.
			Assert.That(SupplyLogisticsMath.StepEvacDwell(0, startedEvacuating: true, dwellScans: 2),
				Is.EqualTo(2), "entering arms the dwell");
			Assert.That(SupplyLogisticsMath.StepEvacDwell(2, startedEvacuating: false, dwellScans: 2),
				Is.EqualTo(1), "a continuing evac counts the dwell down, it does NOT re-arm");
			Assert.That(SupplyLogisticsMath.StepEvacDwell(1, startedEvacuating: false, dwellScans: 2),
				Is.EqualTo(0), "the dwell expires");
			Assert.That(SupplyLogisticsMath.StepEvacDwell(0, startedEvacuating: false, dwellScans: 2),
				Is.EqualTo(0), "and is floored at zero");
		}

		[Test]
		public void EvacDwell_ZeroDwellScans_IsInert()
		{
			Assert.That(SupplyLogisticsMath.StepEvacDwell(5, startedEvacuating: true, dwellScans: 0),
				Is.EqualTo(0), "dwell of 0 disables the damper (the memoryless pre-fix reading)");
		}

		[Test]
		public void ReleaseLevel_FloorsAtOne_SoAMisconfiguredHysteresisCannotLatchForever()
		{
			Assert.That(SupplyLogisticsMath.ReleaseLevel(60, 15), Is.EqualTo(45));
			Assert.That(SupplyLogisticsMath.ReleaseLevel(60, 0), Is.EqualTo(60), "no hysteresis = no deadband");
			Assert.That(SupplyLogisticsMath.ReleaseLevel(60, -5), Is.EqualTo(60), "negative hysteresis ignored");

			// A hysteresis at/over the threshold would drive the release level to <= 0, and since danger reads
			// are never negative "still hot" would then be true on a stone-cold cell — a truck evacuating for
			// the rest of the match. Flooring at 1 means a 0-danger cell always releases.
			Assert.That(SupplyLogisticsMath.ReleaseLevel(60, 60), Is.EqualTo(1));
			Assert.That(SupplyLogisticsMath.ReleaseLevel(60, 500), Is.EqualTo(1));
			Assert.That(Evac(wasEvacuating: true, hold: 0, dangerAtTruck: 0), Is.False,
				"a cold cell always releases");
		}

		[Test]
		public void EvacDwell_FullCycle_SettlesInsteadOfOscillating()
		{
			// The regression this whole damper exists for, walked end to end. Pre-fix, a truck whose cluster
			// read hot re-took the retreat on EVERY scan (the cluster term does not fall when the truck moves),
			// and flipped back to following the moment selection found a cooler cluster — forward, back, repeat.
			// Here the truck drives into danger, retreats ONCE, holds while the leg completes, and settles.
			var hold = 0;
			var evacuating = false;

			// Scan 1: truck drives into fire. Enters evac, arms the dwell. Caller issues the retreat.
			var now = Evac(evacuating, hold, dangerAtTruck: 80);
			Assert.That(now, Is.True, "scan 1: enters evac");
			hold = SupplyLogisticsMath.StepEvacDwell(hold, now && !evacuating, dwellScans: 2);
			evacuating = now;
			Assert.That(hold, Is.EqualTo(2));

			// Scan 2: retreat is under way and the truck has already cooled. Pre-fix this is where the branch
			// flipped back and the maneuver restarted. The dwell holds it, and hold > 0 also tells the caller
			// not to re-issue the Move from the truck's new (nearer) position.
			now = Evac(evacuating, hold, dangerAtTruck: 20);
			Assert.That(now, Is.True, "scan 2: held — branch not re-decided mid-retreat");
			hold = SupplyLogisticsMath.StepEvacDwell(hold, now && !evacuating, dwellScans: 2);
			Assert.That(hold, Is.EqualTo(1), "and the dwell is counting down, not re-arming");

			// Scan 3: dwell decays to 0.
			now = Evac(evacuating, hold, dangerAtTruck: 20);
			Assert.That(now, Is.True, "scan 3: still held");
			hold = SupplyLogisticsMath.StepEvacDwell(hold, now && !evacuating, dwellScans: 2);
			Assert.That(hold, Is.EqualTo(0));

			// Scan 4: free to re-decide, and the truck is clear of the deadband → back to following. ONE
			// retreat was taken for one danger episode; that is the fixed point the pre-fix code never reached.
			now = Evac(evacuating, hold, dangerAtTruck: 20);
			Assert.That(now, Is.False, "scan 4: released — resumes following");
		}
	}
}
