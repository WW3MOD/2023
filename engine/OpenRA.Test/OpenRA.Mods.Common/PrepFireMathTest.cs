#region Copyright & License Information
/*
 * WW3MOD PrepFireMath / AdvanceUnderCoverMath tests — fires doctrine Phases 2+3 (@experimental).
 *
 * Pure-logic pins for the two predicates that sequence a barrage ahead of an assault: Phase 2 holds the screen
 * at a start line while the guns can actually support it (PrepFireMath.ShouldHoldScreen), Phase 3 releases it
 * EARLY once the objective is observably suppressed (AdvanceUnderCoverMath.ScreenMayAdvance, over a per-defender
 * average from NormalizeSuppression). Both are world-free statics, so the whole decision is validated here
 * without a game run — the fires design's non-gated acceptance leg
 * (WORKSPACE/plans/260803_fires_cycle_design.md §3 Phase 2/3; the in-game legs remain user-gated).
 *
 * The headline pins are the FAIL-OPEN edges — a zero prep window, an axis inside the assault radius, an axis
 * BEYOND THE GUNS' REACH (review FIX 6), and the hard release at window expiry. Each is a way the hold could
 * otherwise stall a screen with no barrage landing, so each is asserted explicitly rather than left to the
 * composition. NormalizeSuppression is pinned count-independent (review FIX 7): a raw sum would make the
 * threshold easier the more defenders there are, which is backwards.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class PrepFireMathTest
	{
		// Mirrors PoiOffensiveBotModuleInfo's shipped defaults so the pins double as documentation of the knobs.
		const int AssaultRadius = 15;
		const int PrepMax = 150;
		const int SuppressThreshold = 60;

		// The reach a screen-echeloned battery actually has past the screen: with the guns held EchelonDepth behind
		// it, reach collapses to roughly (screen engagement range - EchelonBuffer), NOT the gun's own range. So the
		// productive hold band on a real axis is only about (AssaultRadius, 21].
		const int FiresReach = 21;

		[Test]
		public void ShouldHoldScreen_HoldsInsideTheProductiveBandUntilTheWindowElapses()
		{
			// Beyond the assault radius but still within the guns' reach, with window left ⇒ hold and prep.
			Assert.That(PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, 0, PrepMax), Is.True, "window just opened");
			Assert.That(PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, PrepMax - 1, PrepMax), Is.True, "last tick of the window");
			Assert.That(PrepFireMath.ShouldHoldScreen(AssaultRadius + 1, AssaultRadius, FiresReach, 0, PrepMax), Is.True, "just outside the assault radius");
			Assert.That(PrepFireMath.ShouldHoldScreen(FiresReach, AssaultRadius, FiresReach, 0, PrepMax), Is.True, "at the very edge of reach");
		}

		[Test]
		public void ShouldHoldScreen_NeverHoldsBeyondTheGunsReach()
		{
			// FIX 6 — the defect this bound exists for. A fresh axis is assigned tens of cells out; with the guns
			// echeloned BEHIND the screen nothing would land on the objective, so a hold there is a pure stall.
			Assert.That(PrepFireMath.ShouldHoldScreen(FiresReach + 1, AssaultRadius, FiresReach, 0, PrepMax), Is.False, "one cell past reach");
			Assert.That(PrepFireMath.ShouldHoldScreen(40, AssaultRadius, FiresReach, 0, PrepMax), Is.False, "typical fresh axis");
			Assert.That(PrepFireMath.ShouldHoldScreen(100, AssaultRadius, FiresReach, 0, PrepMax), Is.False, "cross-map axis");

			// No live gun on the axis (reach 0) can never hold, whatever the distance.
			Assert.That(PrepFireMath.ShouldHoldScreen(18, AssaultRadius, 0, 0, PrepMax), Is.False, "no reach at all");

			// A reach that does not even clear the assault radius leaves an EMPTY band — never holds.
			Assert.That(PrepFireMath.ShouldHoldScreen(AssaultRadius + 1, AssaultRadius, AssaultRadius - 1, 0, PrepMax), Is.False, "empty band");
		}

		[Test]
		public void ShouldHoldScreen_ReleasesAtWindowExpiry()
		{
			// The bounded countdown: at and past prepMaxTicks the screen assaults regardless of anything else.
			Assert.That(PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, PrepMax, PrepMax), Is.False, "expiry releases");
			Assert.That(PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, PrepMax * 10, PrepMax), Is.False, "long past expiry");
		}

		[Test]
		public void ShouldHoldScreen_NeverHoldsInsideAssaultRadius()
		{
			// A screen that has already closed is committed — prep must not pull it back, at the boundary or inside.
			Assert.That(PrepFireMath.ShouldHoldScreen(AssaultRadius, AssaultRadius, FiresReach, 0, PrepMax), Is.False, "on the radius");
			Assert.That(PrepFireMath.ShouldHoldScreen(1, AssaultRadius, FiresReach, 0, PrepMax), Is.False, "well inside");
			Assert.That(PrepFireMath.ShouldHoldScreen(0, AssaultRadius, FiresReach, 0, PrepMax), Is.False, "on the objective");
		}

		[Test]
		public void ShouldHoldScreen_ZeroOrNegativeWindowIsDisabled()
		{
			// Fail-open: a mis-set window disables prep rather than holding forever.
			Assert.That(PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, 0, 0), Is.False, "zero window");
			Assert.That(PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, 0, -1), Is.False, "negative window");
		}

		[Test]
		public void NormalizeSuppression_IsIndependentOfDefenderCount()
		{
			// FIX 7 — the raw SUM made the bar easier the more defenders there were. Five defenders barely rattled
			// (20 stacks each) summed to 100 and would have cleared a threshold of 60; averaged they read 20 and
			// correctly do not.
			Assert.That(AdvanceUnderCoverMath.NormalizeSuppression(100, 5), Is.EqualTo(20), "five lightly rattled defenders");
			Assert.That(AdvanceUnderCoverMath.NormalizeSuppression(100, 5), Is.LessThan(SuppressThreshold), "and that is below the bar");

			// The same per-defender state reads the same whether there is one defender or five — that is the point.
			Assert.That(AdvanceUnderCoverMath.NormalizeSuppression(80, 1), Is.EqualTo(80));
			Assert.That(AdvanceUnderCoverMath.NormalizeSuppression(400, 5), Is.EqualTo(80));
			Assert.That(AdvanceUnderCoverMath.NormalizeSuppression(400, 5), Is.GreaterThanOrEqualTo(SuppressThreshold), "genuinely suppressed clears it");
		}

		[Test]
		public void NormalizeSuppression_NoObservedDefendersReadsZero()
		{
			// A fogged or empty objective is not KNOWN to be soft, so it reads 0 and the prep window (not the
			// suppression read) governs the release. No division by zero.
			Assert.That(AdvanceUnderCoverMath.NormalizeSuppression(0, 0), Is.EqualTo(0));
			Assert.That(AdvanceUnderCoverMath.NormalizeSuppression(50, 0), Is.EqualTo(0));
		}

		[Test]
		public void ScreenMayAdvance_ReleasesAtOrAboveSuppressionThreshold()
		{
			// The Phase-3 headline: the barrage has suppressed the objective, so go NOW rather than wait out the clock.
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(SuppressThreshold, SuppressThreshold, 0, PrepMax), Is.True, "exactly at threshold");
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(SuppressThreshold + 1, SuppressThreshold, 0, PrepMax), Is.True, "above threshold");
		}

		[Test]
		public void ScreenMayAdvance_HoldsBelowThresholdInsideTheWindow()
		{
			// An un-softened objective keeps the screen waiting while the guns still have window left.
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(0, SuppressThreshold, 0, PrepMax), Is.False, "nothing suppressed");
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(SuppressThreshold - 1, SuppressThreshold, PrepMax - 1, PrepMax), Is.False,
				"one short, one tick of window left");
		}

		[Test]
		public void ScreenMayAdvance_HardReleasesAtWindowExpiryRegardlessOfSuppression()
		{
			// No deadlock: an objective that never suppresses (empty, fogged, or defenders immune) is still assaulted.
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(0, SuppressThreshold, PrepMax, PrepMax), Is.True, "expiry beats suppression");
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(0, SuppressThreshold, 0, 0), Is.True, "zero window is disabled");
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(0, 0, 0, PrepMax), Is.True, "zero threshold releases immediately");
		}

		[Test]
		public void PhaseComposition_SuppressionOnlyEverShortensTheHold()
		{
			// The consumer's effective hold is ShouldHoldScreen AND NOT ScreenMayAdvance, re-evaluated on EVERY
			// evaluation pass while the hold is active (FIX 5 — evaluating once at elapsed 0 would sample the
			// objective before a shell had landed). Pin the two properties that composition must have.
			static bool Holds(int suppression, int elapsed)
				=> PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, elapsed, PrepMax)
					&& !AdvanceUnderCoverMath.ScreenMayAdvance(suppression, SuppressThreshold, elapsed, PrepMax);

			Assert.That(Holds(0, 0), Is.True, "un-suppressed objective, window open ⇒ prep");
			Assert.That(Holds(SuppressThreshold, 0), Is.False, "suppressed ⇒ early release (shorter than Phase 2 alone)");
			Assert.That(Holds(0, PrepMax), Is.False, "window elapsed ⇒ release even un-suppressed");

			// Walking the window one re-eval at a time (ReevaluateInterval 100): Phase 3 must never EXTEND the hold
			// beyond what Phase 2 alone would allow, at any elapsed value.
			for (var elapsed = 0; elapsed <= PrepMax * 2; elapsed += 25)
			{
				var phase2Only = PrepFireMath.ShouldHoldScreen(18, AssaultRadius, FiresReach, elapsed, PrepMax);
				Assert.That(Holds(0, elapsed), Is.EqualTo(phase2Only), $"un-suppressed matches Phase 2 at elapsed={elapsed}");
				Assert.That(Holds(SuppressThreshold, elapsed) && !phase2Only, Is.False, $"never holds where Phase 2 released at elapsed={elapsed}");
			}

			// A suppression reading that arrives LATE (the realistic case — shells land after the first pass)
			// still releases the hold on the pass that sees it, which is what re-evaluation buys.
			Assert.That(Holds(0, 100), Is.True, "second pass, still un-suppressed ⇒ keep prepping");
			Assert.That(Holds(SuppressThreshold, 100), Is.False, "second pass, now suppressed ⇒ step off");
		}
	}
}
