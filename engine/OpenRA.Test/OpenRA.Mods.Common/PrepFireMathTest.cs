#region Copyright & License Information
/*
 * WW3MOD PrepFireMath / AdvanceUnderCoverMath tests — fires doctrine Phases 2+3 (@experimental).
 *
 * Pure-logic pins for the two predicates that sequence a barrage ahead of an assault: Phase 2 holds the screen
 * at a start line for a bounded prep window (PrepFireMath.ShouldHoldScreen), Phase 3 releases it EARLY once the
 * objective is observably suppressed (AdvanceUnderCoverMath.ScreenMayAdvance). Both are world-free statics, so
 * the whole decision is validated here without a game run — the fires design's non-gated acceptance leg
 * (WORKSPACE/plans/260803_fires_cycle_design.md §3 Phase 2/3; the in-game legs remain user-gated).
 *
 * The headline pins are the FAIL-OPEN edges: a zero prep window, an axis already inside the assault radius, and
 * the hard release at window expiry. Each is a way the hold could otherwise strand a screen standing still, so
 * each is asserted explicitly rather than left to the composition.
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

		[Test]
		public void ShouldHoldScreen_HoldsInApproachBandUntilWindowElapses()
		{
			// Approaching (centroid well outside the assault radius) with the window still open ⇒ hold and prep.
			Assert.That(PrepFireMath.ShouldHoldScreen(40, AssaultRadius, 0, PrepMax), Is.True, "window just opened");
			Assert.That(PrepFireMath.ShouldHoldScreen(40, AssaultRadius, PrepMax - 1, PrepMax), Is.True, "last tick of the window");
		}

		[Test]
		public void ShouldHoldScreen_ReleasesAtWindowExpiry()
		{
			// The bounded countdown: at and past prepMaxTicks the screen assaults regardless of anything else.
			Assert.That(PrepFireMath.ShouldHoldScreen(40, AssaultRadius, PrepMax, PrepMax), Is.False, "expiry releases");
			Assert.That(PrepFireMath.ShouldHoldScreen(40, AssaultRadius, PrepMax * 10, PrepMax), Is.False, "long past expiry");
		}

		[Test]
		public void ShouldHoldScreen_NeverHoldsInsideAssaultRadius()
		{
			// A screen that has already closed is committed — prep must not pull it back, at the boundary or inside.
			Assert.That(PrepFireMath.ShouldHoldScreen(AssaultRadius, AssaultRadius, 0, PrepMax), Is.False, "on the radius");
			Assert.That(PrepFireMath.ShouldHoldScreen(1, AssaultRadius, 0, PrepMax), Is.False, "well inside");
			Assert.That(PrepFireMath.ShouldHoldScreen(0, AssaultRadius, 0, PrepMax), Is.False, "on the objective");
		}

		[Test]
		public void ShouldHoldScreen_ZeroOrNegativeWindowIsDisabled()
		{
			// Fail-open: a mis-set window disables prep rather than holding forever.
			Assert.That(PrepFireMath.ShouldHoldScreen(40, AssaultRadius, 0, 0), Is.False, "zero window");
			Assert.That(PrepFireMath.ShouldHoldScreen(40, AssaultRadius, 0, -1), Is.False, "negative window");
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
			// No deadlock: an objective that never suppresses (empty, or defenders immune) is still assaulted.
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(0, SuppressThreshold, PrepMax, PrepMax), Is.True, "expiry beats suppression");
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(0, SuppressThreshold, 0, 0), Is.True, "zero window is disabled");
			Assert.That(AdvanceUnderCoverMath.ScreenMayAdvance(0, 0, 0, PrepMax), Is.True, "zero threshold releases immediately");
		}

		[Test]
		public void PhaseComposition_NeverHoldsPastTheWindow()
		{
			// The consumer's effective hold is ShouldHoldScreen AND NOT ScreenMayAdvance. Pin the two properties the
			// composition must have: suppression can only ever SHORTEN the hold, and the window still bounds it.
			bool Holds(int suppression, int elapsed)
				=> PrepFireMath.ShouldHoldScreen(40, AssaultRadius, elapsed, PrepMax)
					&& !AdvanceUnderCoverMath.ScreenMayAdvance(suppression, SuppressThreshold, elapsed, PrepMax);

			Assert.That(Holds(0, 0), Is.True, "un-suppressed objective, window open ⇒ prep");
			Assert.That(Holds(SuppressThreshold, 0), Is.False, "suppressed ⇒ early release (shorter than Phase 2 alone)");
			Assert.That(Holds(0, PrepMax), Is.False, "window elapsed ⇒ release even un-suppressed");

			// Phase 3 must never EXTEND the hold beyond what Phase 2 alone would allow.
			for (var elapsed = 0; elapsed <= PrepMax * 2; elapsed += 25)
			{
				var phase2Only = PrepFireMath.ShouldHoldScreen(40, AssaultRadius, elapsed, PrepMax);
				Assert.That(Holds(0, elapsed), Is.EqualTo(phase2Only), $"un-suppressed matches Phase 2 at elapsed={elapsed}");
				Assert.That(Holds(SuppressThreshold, elapsed) && !phase2Only, Is.False, $"never holds where Phase 2 released at elapsed={elapsed}");
			}
		}
	}
}
