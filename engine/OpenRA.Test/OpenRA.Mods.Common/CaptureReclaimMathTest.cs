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
 *   (3) UnmetReclaimDemand reports the shortfall that should pull production on a scan that already
 *       dispatched, and never goes negative so the caller can treat it as a plain "> 0" gate.
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

		// ---------- UnmetReclaimDemand ----------

		[Test]
		public void ShortfallIsBacklogMinusFreeCapturers()
		{
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(8, 3), Is.EqualTo(5));
		}

		[Test]
		public void CoveredBacklogReportsNoShortfall()
		{
			// Enough bodies for the work ⇒ nothing to fund, so the caller's "> 0" gate stays shut.
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(2, 2), Is.EqualTo(0));
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(2, 5), Is.EqualTo(0),
				"more free capturers than targets must not report negative demand");
		}

		[Test]
		public void EmptyBacklogNeverPullsProduction()
		{
			Assert.That(CaptureReclaimMath.UnmetReclaimDemand(0, 0), Is.EqualTo(0));
		}
	}
}
