#region Copyright & License Information
/*
 * WW3MOD supply-truck DROP-AND-LEAVE — when-to-drop decision test.
 *
 * Pins the gate SupplyFollowerBotModule turns into a DropSupplyCacheAt errand, so "the truck unloads at a
 * forward supply point instead of shadowing the army" can't silently regress into either failure mode:
 *   (1) EVERY GATE IS LOAD-BEARING — each of the four terms alone can refuse a drop.
 *   (2) THE DROP SWITCHES ITS OWN DECISION OFF — the post-drop state of each responsive term declines.
 *       This is the property that makes the branch memoryless-safe: it needs no "already dropping" latch,
 *       which is the defect species three review rounds found in the evac fix.
 *   (3) MISCONFIGURATION FAILS TOWARD NOT DROPPING — with the one documented exception (a non-positive
 *       redundancy level DISABLES that gate rather than pinning it true, which would disable the mode).
 * Pure math over synthetic readings; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyDropMathTest
	{
		// A loaded truck, real demand, no crate already here, an anchor established: the baseline YES.
		const bool Anchored = true;
		const int Load = 750;
		const int MinSupply = 250;
		const int Starving = 5;
		const int MinStarving = 3;
		const int NoCache = 0;
		const int NoneInFlight = 0;
		const int RedundantCache = 100;

		static bool Drop(
			bool anchored = Anchored,
			int load = Load, int minSupply = MinSupply,
			int starving = Starving, int minStarving = MinStarving,
			int cacheNear = NoCache, int inFlight = NoneInFlight, int redundant = RedundantCache)
			=> SupplyDropMath.ShouldDrop(anchored, load, minSupply, starving, minStarving, cacheNear, inFlight, redundant);

		[Test]
		public void Baseline_LoadedTruck_RealDemand_NoCache_Drops()
		{
			Assert.That(Drop(), Is.True);
		}

		// ---- (1) every gate alone can refuse ----

		[Test]
		public void NoAnchor_NeverDrops_EvenWithOverwhelmingDemand()
		{
			// The descent stalled at the Supply Route (flat belief field, or the front is on top of us). The
			// caller passes false rather than "anchor = the SR", so the truck cannot unload at the beachhead.
			Assert.That(Drop(anchored: false, starving: 1000), Is.False);
		}

		[Test]
		public void UnderLoaded_DoesNotDrop()
		{
			Assert.That(Drop(load: MinSupply - 1), Is.False);
			Assert.That(Drop(load: MinSupply), Is.True, "the load gate is inclusive at the threshold");
		}

		[Test]
		public void InsufficientDemand_DoesNotDrop()
		{
			Assert.That(Drop(starving: MinStarving - 1), Is.False);
			Assert.That(Drop(starving: MinStarving), Is.True, "the demand gate is inclusive at the threshold");
		}

		[Test]
		public void DemandAlreadyCovered_DoesNotDrop()
		{
			Assert.That(Drop(cacheNear: RedundantCache), Is.False, "redundancy is inclusive at the threshold");
			Assert.That(Drop(cacheNear: RedundantCache - 1), Is.True);
		}

		// ---- (2) the drop switches its own decision off, through three independent terms ----

		[Test]
		public void AfterDropping_TheLoadGateAloneRefusesASecondDrop()
		{
			// DropSupplyCacheHere calls SetSupply(0). Even with the demand unchanged and no crate detected,
			// the emptied truck cannot re-trigger.
			Assert.That(Drop(load: 0), Is.False);
		}

		[Test]
		public void AfterDropping_TheRedundancyGateAloneRefusesASecondDrop()
		{
			// The crate the drop just created is exactly what this gate then sees. Pinned with a still-loaded
			// truck and undiminished demand so the refusal is attributable to this term alone.
			Assert.That(Drop(cacheNear: Load), Is.False);
		}

		[Test]
		public void AfterSoldiersRefill_TheDemandGateAloneRefusesASecondDrop()
		{
			Assert.That(Drop(starving: 0), Is.False);
		}

		// ---- (3) misconfiguration direction ----

		[Test]
		public void ZeroMinSupply_IsFlooredAtOne_SoAnEmptyTruckNeverDrops()
		{
			// "0" must not read as "no requirement" and dump an empty crate.
			Assert.That(Drop(load: 0, minSupply: 0), Is.False);
			Assert.That(Drop(load: 1, minSupply: 0), Is.True);
		}

		[Test]
		public void ZeroMinStarving_IsFlooredAtOne_SoNoDemandNeverDrops()
		{
			Assert.That(Drop(starving: 0, minStarving: 0), Is.False);
			Assert.That(Drop(starving: 1, minStarving: 0), Is.True);
		}

		[Test]
		public void NonPositiveRedundancy_DisablesThatGate_RatherThanDisablingTheMode()
		{
			// The one INVERTED knob: a smaller value is stricter, so flooring it at 1 would make any crate
			// nearby veto forever. Read literally, 0 would mean `cacheSupply >= 0` — permanently true, i.e.
			// the whole mode silently off. Both non-positive spellings therefore mean "no redundancy gate".
			Assert.That(Drop(cacheNear: 10_000, redundant: 0), Is.True);
			Assert.That(Drop(cacheNear: 10_000, redundant: -1), Is.True);
		}

		[Test]
		public void NegativeReadings_DoNotDrop()
		{
			// Defensive: a caller that samples a dead provider must not be handed a yes.
			Assert.That(Drop(load: -1), Is.False);
			Assert.That(Drop(starving: -1), Is.False);
		}

		// ---- in-flight accounting: the fleet-dump defect ----

		[Test]
		public void SupplyAlreadyCommittedByOtherTrucks_CountsAsCovered()
		{
			// THE FLEET-DUMP CASE. Nothing is on the ground yet (cacheNear 0) because the first truck is
			// still driving, so every other truck would pass a ground-only gate on the same scan and unload
			// too. Committed supply must count as covered.
			Assert.That(Drop(cacheNear: 0, inFlight: RedundantCache), Is.False);
		}

		[Test]
		public void GroundAndInFlightSupply_SumTowardsRedundancy()
		{
			// Neither half alone reaches the threshold; together they do. Pinned because summing (rather than
			// maxing) is what makes a half-drained crate plus a truck already en route count as covered.
			Assert.That(Drop(cacheNear: 60, inFlight: 0), Is.True);
			Assert.That(Drop(cacheNear: 0, inFlight: 60), Is.True);
			Assert.That(Drop(cacheNear: 60, inFlight: 60), Is.False);
		}

		[Test]
		public void FirstTruckOfAFleetStillDrops_InFlightIsNotSelfCounting()
		{
			// The caller excludes the deciding truck from the sum, so an empty in-flight total must still
			// yield a drop — otherwise the accounting would deadlock the whole mode at zero drops.
			Assert.That(Drop(inFlight: 0), Is.True);
		}

		// ---- arrival check: the wrong-cell-dump defect ----

		[Test]
		public void ArrivedAtDropCell_AcceptsWithinTolerance_InclusiveAtTheBoundary()
		{
			Assert.That(SupplyDropMath.ArrivedAtDropCell(0, 0, 2), Is.True);
			Assert.That(SupplyDropMath.ArrivedAtDropCell(2, 0, 2), Is.True, "inclusive at the tolerance");
			Assert.That(SupplyDropMath.ArrivedAtDropCell(1, 1, 2), Is.True);
		}

		[Test]
		public void ArrivedAtDropCell_RejectsBeyondTolerance()
		{
			// The defect this guards: an unreachable anchor makes Move complete at the truck's CURRENT cell,
			// which can be the whole map away. Refusing keeps the load in the truck.
			Assert.That(SupplyDropMath.ArrivedAtDropCell(3, 0, 2), Is.False);
			Assert.That(SupplyDropMath.ArrivedAtDropCell(2, 2, 2), Is.False, "diagonal distance, not Chebyshev");
			Assert.That(SupplyDropMath.ArrivedAtDropCell(-40, 25, 2), Is.False);
		}

		[Test]
		public void ArrivedAtDropCell_ZeroTolerance_DemandsTheExactCell()
		{
			// A misconfigured tolerance must tighten the check, never widen it.
			Assert.That(SupplyDropMath.ArrivedAtDropCell(0, 0, 0), Is.True);
			Assert.That(SupplyDropMath.ArrivedAtDropCell(1, 0, 0), Is.False);
			Assert.That(SupplyDropMath.ArrivedAtDropCell(1, 0, -5), Is.False);
		}

		// ---- re-issue dedup: the stutter defect ----

		[Test]
		public void ShouldIssueDrop_FirstDispatch_Issues()
		{
			Assert.That(SupplyDropMath.ShouldIssueDrop(false, 0, 0, 12, 34), Is.True);
		}

		[Test]
		public void ShouldIssueDrop_AlreadyEnRouteToTheSameCell_DoesNotReissue()
		{
			// Re-issuing is non-queued, so it cancels the running errand and destroys its unload/restock tail
			// before rebuilding it — a pathfind and up to a cell of backslide every scan, forever.
			Assert.That(SupplyDropMath.ShouldIssueDrop(true, 12, 34, 12, 34), Is.False);
		}

		[Test]
		public void ShouldIssueDrop_ErrandNoLongerRunning_Reissues()
		{
			// The caller clears its record when the truck went idle still holding its load — the errand ended
			// without dropping (blocked cell, or a destination that went unreachable after issue). It then
			// passes alreadyDispatched: false, and the retry must go out: suppressing it would convert a
			// self-correcting refusal into a truck parked on its anchor forever.
			Assert.That(SupplyDropMath.ShouldIssueDrop(false, 12, 34, 12, 34), Is.True);
		}

		[Test]
		public void ShouldIssueDrop_AnchorMoved_ReissuesWithoutAnyValidityFlag()
		{
			// The dedup key is the TARGET CELL, which is what keeps it from becoming a latch: a moved anchor
			// re-issues by itself, so there is no separate "still valid?" boolean that could go stale.
			Assert.That(SupplyDropMath.ShouldIssueDrop(true, 12, 34, 13, 34), Is.True);
			Assert.That(SupplyDropMath.ShouldIssueDrop(true, 12, 34, 12, 35), Is.True);
		}
	}
}
