#region Copyright & License Information
/*
 * WW3MOD capture-reclaim — take our own cleared base back (decision math test).
 *
 * Pins the three decisions CaptureCoordinatorBotModule relies on to recover from the c513f358 eviction rule
 * (a soldier clears ANY enemy building to Neutral; only a technician re-owns one):
 *   (1) CombinedCaptureDemand folds the reclaim backlog into the capturer-floor demand, and returns the
 *       money-POI count VERBATIM when the lever is off — the off-switch contract;
 *   (2) IsSafeToReclaim refuses to walk an unarmed consumable into a base still under believed fire, with a
 *       negative ceiling as the disable escape hatch and an INCLUSIVE boundary;
 *   (3) ReclaimBudget stops the preempting reclaim pass draining the capturer pool to empty, so the ranked
 *       PoiMap pass is never starved to zero;
 *   (4) UnmetReclaimDemand reports the shortfall that should pull production on a scan that already
 *       dispatched — measured against candidates COVERED (dispatched + in flight), not against leftover free
 *       capturers, which makes the gate fire unconditionally — and never goes negative.
 * Pure integer comparisons, zero RNG — two clients over the same synced state decide identically.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CaptureReclaimMathTest
	{
		// ---------- CombinedCaptureDemand ----------

		[Test]
		public void DemandOff_ReturnsMoneyPoiCountVerbatim()
		{
			// The off-switch contract: a config omitting ReclaimNeutralisedStructures must read exactly the
			// number the floor read before this feature existed, backlog notwithstanding.
			Assert.That(CaptureReclaimMath.CombinedCaptureDemand(3, 9, false), Is.EqualTo(3));
			Assert.That(CaptureReclaimMath.CombinedCaptureDemand(0, 9, false), Is.EqualTo(0));
		}

		[Test]
		public void DemandOn_AddsReclaimBacklogToMoneyPois()
		{
			// A technician is CONSUMED by each capture, so backlog is per-body demand, not a reusable pool.
			Assert.That(CaptureReclaimMath.CombinedCaptureDemand(3, 4, true), Is.EqualTo(7));
		}

		[Test]
		public void DemandOn_WithNoFreeDerricksLeft_IsDrivenEntirelyByTheBacklog()
		{
			// THE REGRESSION CASE. Every free derrick on the map is taken, so the money-POI count is 0 and the
			// pre-feature floor read zero demand — while eight of our own buildings lie neutral. Recovery has to
			// be fundable from the backlog alone or the bot never buys the technician that would take them back.
			Assert.That(CaptureReclaimMath.CombinedCaptureDemand(0, 8, true), Is.EqualTo(8));
		}

		[Test]
		public void DemandOn_WithNothingToReclaim_MatchesTheOffAnswer()
		{
			// An opted-in bot whose base is intact must not read MORE demand than a frozen one.
			Assert.That(CaptureReclaimMath.CombinedCaptureDemand(5, 0, true),
				Is.EqualTo(CaptureReclaimMath.CombinedCaptureDemand(5, 0, false)));
		}

		// ---------- IsSafeToReclaim ----------

		[Test]
		public void QuietTargetIsSafe_HotTargetIsNot()
		{
			Assert.That(CaptureReclaimMath.IsSafeToReclaim(0, 300), Is.True);
			Assert.That(CaptureReclaimMath.IsSafeToReclaim(299, 300), Is.True);
			Assert.That(CaptureReclaimMath.IsSafeToReclaim(301, 300), Is.False,
				"a base still swarming with the raid must not receive an unarmed technician");
		}

		[Test]
		public void CeilingIsInclusive()
		{
			// Exactly at the threshold is still safe, so a ceiling set at the ambient territory baseline does
			// not refuse every target in our own back yard.
			Assert.That(CaptureReclaimMath.IsSafeToReclaim(300, 300), Is.True);
		}

		[Test]
		public void NegativeCeilingDisablesTheGate()
		{
			// The escape hatch: reclaim regardless of believed danger.
			Assert.That(CaptureReclaimMath.IsSafeToReclaim(int.MaxValue, -1), Is.True);
		}

		[Test]
		public void ZeroCeilingAdmitsOnlyTargetsOutsideEveryBelievedEnvelope()
		{
			// 0 units is 0 raw field units at any scale, so this converts losslessly.
			Assert.That(CaptureReclaimMath.IsSafeToReclaim(0, 0), Is.True);
			Assert.That(CaptureReclaimMath.IsSafeToReclaim(1, 0), Is.False);
		}

		// ---------- ReclaimBudget ----------

		[Test]
		public void BudgetLeavesOneCapturerForTheRankedPass()
		{
			// THE STARVATION CASE. Reclaim runs first and used to drain the pool: three technicians, three
			// formerly-ours structures, and a free derrick next door got nobody. Reclaim keeps priority — it
			// still takes every body but one — but the ranked pass is never starved to zero.
			Assert.That(CaptureReclaimMath.ReclaimBudget(3, 2), Is.EqualTo(2));
			Assert.That(CaptureReclaimMath.ReclaimBudget(5, 1), Is.EqualTo(4));
		}

		[Test]
		public void BudgetIsTheWholePoolWhenTheRankedPassHasNothingToDo()
		{
			// Reserving against an empty ranked list would just idle a capturer.
			Assert.That(CaptureReclaimMath.ReclaimBudget(4, 0), Is.EqualTo(4));
		}

		[Test]
		public void SingleCapturerGoesToReclaim()
		{
			// Reclaim is the priority; one body cannot be split, so it wins the tie rather than being reserved
			// away and leaving the backlog untouched.
			Assert.That(CaptureReclaimMath.ReclaimBudget(1, 9), Is.EqualTo(1));
			Assert.That(CaptureReclaimMath.ReclaimBudget(0, 9), Is.EqualTo(0));
		}

		// ---------- UnmetReclaimDemand ----------

		[Test]
		public void ShortfallIsBacklogMinusCovered()
		{
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(8, 3), Is.EqualTo(5));
		}

		[Test]
		public void FullyCoveredBacklogReportsNoShortfall()
		{
			// THE REGRESSION THIS SIGNATURE EXISTS TO PREVENT. Three candidates dispatched with three capturers
			// is fully covered — shortfall 0. Passing "free capturers left over" here instead would give 3,
			// because dispatching consumes the very capturers being counted, making the gate fire on every scan
			// that did any work at all.
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(3, 3), Is.EqualTo(0));
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(2, 5), Is.EqualTo(0),
				"more covered than candidates must not report negative demand");
		}

		[Test]
		public void InFlightCandidatesCountAsCovered()
		{
			// Five candidates, two already being walked to and one dispatched now ⇒ two still need a body.
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(5, 3), Is.EqualTo(2));
		}

		[Test]
		public void EmptyBacklogNeverPullsProduction()
		{
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(0, 0), Is.EqualTo(0));
		}
	}
}
