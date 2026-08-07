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

		static bool Evac(bool wasEvacuating, int hold, int dangerAtTruck, int dangerAtDestination = 0) =>
			SupplyLogisticsMath.EvacuateWithDwell(wasEvacuating, hold, dangerAtTruck, dangerAtDestination,
				Threshold, Hysteresis);

		[Test]
		public void EvacDwell_ReleaseIgnoresTheDestination_SoItCannotLatch()
		{
			// THE REGRESSION THIS FILE EXISTS TO PREVENT, and the one the first version of the fix shipped.
			// The release test must read ONLY terms the retreat itself moves. A destination reading is not
			// one: retreating changes where the TRUCK is, never what the destination reads. When the release
			// ORed it in, any destination in [ReleaseLevel, Threshold) — i.e. every value the selection gate
			// admits, since that gate is at ReleaseLevel — made the release permanently true however far the
			// truck drove, so the truck re-retreated every scan. That is the reported bug at full amplitude.
			//
			// The original pins all passed dangerAtDestination: 0, which is exactly why they missed it. Any
			// new release assertion must exercise a NON-ZERO destination.
			Assert.That(Evac(wasEvacuating: true, hold: 0, dangerAtTruck: 0, dangerAtDestination: 50),
				Is.False, "a cold truck releases even with a warm destination — the destination is not a responsive term");
			Assert.That(Evac(wasEvacuating: true, hold: 0, dangerAtTruck: 0, dangerAtDestination: 59),
				Is.False, "and at the top of the band the selection gate admits");

			// The truck's own reading still governs the release in both directions.
			Assert.That(Evac(wasEvacuating: true, hold: 0, dangerAtTruck: 45, dangerAtDestination: 0),
				Is.True, "the truck's own reading at the release level still holds the evac");
		}

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
		public void DestinationDanger_RelievedDestinationIsNotRead_SoTheEntryTestCannotLatch()
		{
			// The relief valve hands back the least-dangerous NEEDY cluster when nothing passed the danger
			// gate — and near a contested frontier that is the ordinary path, not a corner case, so the
			// reading it returns is routinely at or above the entry threshold. Feeding it to the entry test
			// pins the branch true on every scan whatever the truck does (entry short-circuits ahead of both
			// the dwell and the release), so the truck legs to the SR, drifts out of follow range, releases,
			// re-selects the same cluster through the valve and re-enters: parked at the SR resupplying
			// nobody, which is the starvation the valve exists to prevent.
			Assert.That(SupplyLogisticsMath.DestinationDanger(destinationWasGated: true, 50), Is.EqualTo(50),
				"a gated destination is read as-is");
			Assert.That(SupplyLogisticsMath.DestinationDanger(destinationWasGated: false, 70), Is.EqualTo(0),
				"an ungated (relieved) destination contributes nothing");

			// The composed property: a relieved cluster deep in a firefight must NOT evacuate a cold truck.
			var relieved = SupplyLogisticsMath.DestinationDanger(destinationWasGated: false, 70);
			Assert.That(Evac(wasEvacuating: false, hold: 0, dangerAtTruck: 0, dangerAtDestination: relieved),
				Is.False, "the truck sets off toward a relieved cluster instead of refusing to move");

			// ...and the abort criterion for that approach is undiminished: its OWN cell going hot still
			// pulls it back on the scan that sees it, which is what makes approaching safe to allow.
			Assert.That(Evac(wasEvacuating: false, hold: 0, dangerAtTruck: Threshold, dangerAtDestination: relieved),
				Is.True, "the truck's own reading still aborts the approach at full strength");
		}

		[Test]
		public void EvacDwell_DestinationTermStillTriggersEntry_WhenTheFrontArrivesOnIt()
		{
			// The destination term survives on the ENTRY side only. The caller gates selection at
			// ReleaseLevel, so a destination can only reach the entry threshold by going hot between scans —
			// a genuine "conditions changed" evac, which is what the branch was written for. Pinned so the
			// gate is never mistaken for a reason to drop the term entirely.
			Assert.That(Evac(wasEvacuating: false, hold: 0, dangerAtTruck: 0, dangerAtDestination: Threshold),
				Is.True, "a destination that goes hot after selection still evacuates the truck");

			// But strictly below the entry threshold it must not, or the gate band is fiction.
			Assert.That(Evac(wasEvacuating: false, hold: 0, dangerAtTruck: 0, dangerAtDestination: Threshold - 1),
				Is.False, "a destination inside the band does not pull a following truck out");
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
			// read hot re-took the retreat on EVERY scan (that term does not fall when the truck moves), and
			// flipped back to following the moment selection found a cooler cluster — forward, back, repeat.
			//
			// The destination is held at 50 THROUGHOUT: inside the band the selection gate admits, and the
			// value that latched the first version of the fix. The cycle must still settle.
			const int WarmDestination = 50;
			var hold = 0;
			var evacuating = false;

			// Scan 1: truck drives into fire. Enters evac, arms the dwell. Caller issues one retreat leg.
			var now = Evac(evacuating, hold, dangerAtTruck: 80, dangerAtDestination: WarmDestination);
			Assert.That(now, Is.True, "scan 1: enters evac");
			hold = SupplyLogisticsMath.StepEvacDwell(hold, now && !evacuating, dwellScans: 1);
			evacuating = now;
			Assert.That(hold, Is.EqualTo(1));

			// Scan 2: the leg is still being driven and the truck has already cooled. Pre-damper this is where
			// the branch flipped back and the maneuver restarted. The dwell holds it.
			now = Evac(evacuating, hold, dangerAtTruck: 20, dangerAtDestination: WarmDestination);
			Assert.That(now, Is.True, "scan 2: held — branch not re-decided mid-leg");
			hold = SupplyLogisticsMath.StepEvacDwell(hold, now && !evacuating, dwellScans: 1);
			Assert.That(hold, Is.EqualTo(0), "and the dwell counts down, it does not re-arm");

			// Scan 3: free to re-decide. The truck is clear of the deadband, so it follows again — EVEN THOUGH
			// the destination still reads 50. ONE retreat for one danger episode: the fixed point the pre-fix
			// code never reached, and the one the latch took away again.
			now = Evac(evacuating, hold, dangerAtTruck: 20, dangerAtDestination: WarmDestination);
			Assert.That(now, Is.False, "scan 3: released despite the warm destination");
		}
	}
}
