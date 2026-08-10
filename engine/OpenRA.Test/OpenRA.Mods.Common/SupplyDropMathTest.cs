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
 *
 * WHAT THESE PINS DO **NOT** COVER — stated here because a false coverage claim is worse than an admitted
 * gap, and a previous version of this file made one. Everything below is a KNOWN, DELIBERATE gap:
 *
 *   * THE FLEET-DUMP MECHANISM ITSELF. The pins cover the PREDICATE — that committed supply counts toward
 *     redundancy, and that ground and in-flight sum rather than max. They do NOT cover the plumbing that
 *     produces the in-flight figure: the mid-scan write (`dropTarget[truck] = anchor`) read back by LATER
 *     trucks in the same loop. Delete that write, or pass a literal 0 for inFlight at the call site, and
 *     every pin here stays green while the fleet dumps again.
 *   * ANCHOR PASSABILITY (the wrong-cell-dump fix, first line of defence). Needs a Locomotor and a Map, so
 *     it cannot be reached without mounting a world. Reverting both WaypointPassable grant-tests in
 *     ResolveDropAnchor leaves every pin green. The three ArrivedAtDropCell pins cover the SECOND line of
 *     defence only — they are not a substitute.
 *   * THE REVOKE PATH. Its decision ("dispatched, and no longer justified") is too thin to pin without
 *     writing another tautology; the defect was that the branch did not EXIST, which is a code-presence
 *     property, not a value property. This is the gap I would most want closed, because the failure is
 *     SILENT on @stable — a truck completes an errand on a withdrawn decision and nothing in the log says
 *     so unless you already suspect it. Closing it needs a module-level harness, not another pure pin.
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

		// ---- the void condition: the latch the re-issue dedup created ----
		//
		// These replace an earlier pin that asserted ShouldIssueDrop(false, 12, 34, 12, 34) is True. That was
		// a TAUTOLOGY over the function's own signature — passing alreadyDispatched: false short-circuits on
		// the first term — so it stayed green whether or not the module voided anything, while its comment
		// read as though it covered the void. Coverage that cannot fail is worse than an admitted gap,
		// because the next audit counts it.

		[Test]
		public void ErrandStillRunning_DispatchedAndBusy_IsRunning()
		{
			Assert.That(SupplyDropMath.ErrandStillRunning(true, false), Is.True);
		}

		[Test]
		public void ErrandIdleWhileStillLoaded_IsNotRunning_SoTheRecordIsVoid()
		{
			// THE LATCH. The truck was dispatched and has gone idle still holding its load, so the errand
			// ended WITHOUT unloading — arrival on an occupied cell, or a destination that went unreachable
			// after issue. Both refusals are designed to self-correct by re-issuing next scan; the dedup
			// deleted that, parking a fully-loaded truck on its anchor forever. Deleting the module's void
			// block makes this direction unreachable in production, which is what the pin defends.
			Assert.That(SupplyDropMath.ErrandStillRunning(true, true), Is.False);
		}

		[Test]
		public void ErrandNeverDispatched_IsNotRunning()
		{
			// Both polarities of the idle term, so the predicate cannot be reduced to `!idle`.
			Assert.That(SupplyDropMath.ErrandStillRunning(false, true), Is.False);
			Assert.That(SupplyDropMath.ErrandStillRunning(false, false), Is.False);
		}

		[Test]
		public void VoidedRecord_ThenReissues_TheTwoHalvesCompose()
		{
			// The repair end to end at the predicate layer: a finished-without-effect errand is not running,
			// so the caller voids its record, and the retry to the same cell then goes out. Composing them
			// here is what stops the two halves being individually green and jointly useless.
			var running = SupplyDropMath.ErrandStillRunning(true, true);
			Assert.That(running, Is.False);
			Assert.That(SupplyDropMath.ShouldIssueDrop(running, 12, 34, 12, 34), Is.True);
		}

		[Test]
		public void DangerSelectsDrop_QuietField_IsSafeSoTheTruckKeepsItsCargo()
		{
			// The regression this exists to stop: with the anchor fixed, the drop fired on a front with no
			// believed enemy at all and the truck emptied itself into an empty field. An unstamped field has
			// median 0 — an UNDEFINED scale, not a small one — and must read safe whatever the percentage.
			Assert.That(SupplyDropMath.DangerSelectsDrop(0, 0, 853, 100), Is.False);
			Assert.That(SupplyDropMath.DangerSelectsDrop(0, 0, 853, 1), Is.False);
		}

		[Test]
		public void DangerSelectsDrop_FloorBeatsTheRatio_SoAThinFieldCannotManufactureDanger()
		{
			// The relative test's known hole: on a nearly quiet field a tiny median makes almost anything
			// "above median". The floor is checked FIRST precisely so that ratio can never be reached.
			Assert.That(SupplyDropMath.DangerSelectsDrop(200, 10, 853, 100), Is.False);

			// And the floor only ever declares SAFE — clearing it still requires the relative test to agree.
			Assert.That(SupplyDropMath.DangerSelectsDrop(900, 100000, 853, 100), Is.False);
		}

		[Test]
		public void DangerSelectsDrop_HotCluster_SelectsDropAndLeave()
		{
			// The measured danger case: cluster at 462,272 against a field whose median is far below it.
			Assert.That(SupplyDropMath.DangerSelectsDrop(462272, 100000, 853, 100), Is.True);

			// Exactly at the bar counts as dangerous — the boundary is inclusive, matching every other
			// level test in this stack.
			Assert.That(SupplyDropMath.DangerSelectsDrop(100000, 100000, 853, 100), Is.True);
		}

		[Test]
		public void DangerSelectsDrop_PercentMovesTheBarAndZeroIsTreatedAsFullMedian()
		{
			// A percentage below 100 makes the mode switch earlier, above 100 later.
			Assert.That(SupplyDropMath.DangerSelectsDrop(60000, 100000, 853, 50), Is.True);
			Assert.That(SupplyDropMath.DangerSelectsDrop(60000, 100000, 853, 200), Is.False);

			// 0 or negative is read as 100 rather than as "no requirement", so a config typo cannot turn
			// every cell above the floor into a drop.
			Assert.That(SupplyDropMath.DangerSelectsDrop(60000, 100000, 853, 0), Is.False);
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
