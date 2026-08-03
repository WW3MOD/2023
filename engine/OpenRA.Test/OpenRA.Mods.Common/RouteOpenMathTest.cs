#region Copyright & License Information
/*
 * WW3MOD route-open math test — frontline-influence Phase 6 (engineer route-opening).
 *
 * Pins the pure Phase-6 decision math (RouteOpenMath) on synthetic inputs, no World:
 *   - the dispatch TRIGGER fires only on {enabled + profile-built + a weakest sector + a repairable avenue
 *     IN that weakest sector}, and every no-op case (flag off, no profile, no front, sector-not-weakest) is
 *     pinned false;
 *   - the per-hut retry cooldown / bounded-attempt / mission-timeout boundaries flip exactly where documented;
 *   - the screen-size clamp never blocks the dispatch (floors at 0, caps at what is available).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class RouteOpenMathTest
	{
		// ---------- Dispatch trigger ----------

		[Test]
		public void DispatchFiresOnWeakestSectorWithRepairableAvenue()
		{
			// All four conditions hold: enabled, profile built, a real weakest sector (2), and its avenue is a
			// repairable destroyed crossing ⇒ open the route.
			Assert.That(RouteOpenMath.ShouldDispatch(enabled: true, hasProfile: true, weakestSector: 2,
				repairableAvenueInWeakest: true), Is.True, "weakest sector + repairable avenue ⇒ dispatch");
		}

		[Test]
		public void DispatchNoOpsOnEveryMissingCondition()
		{
			Assert.Multiple(() =>
			{
				// Flag off ⇒ inert (byte-identical), whatever the profile says.
				Assert.That(RouteOpenMath.ShouldDispatch(false, true, 2, true), Is.False, "flag off ⇒ no-op");

				// Profile not yet built ⇒ nothing to read.
				Assert.That(RouteOpenMath.ShouldDispatch(true, false, 2, true), Is.False, "no profile ⇒ no-op");

				// No believed front (−1 sentinel) ⇒ no sector to push into.
				Assert.That(RouteOpenMath.ShouldDispatch(true, true, FrontlineProfileMath.NoSector, true),
					Is.False, "no front ⇒ no-op");

				// A repairable avenue exists but NOT in the weakest sector (caller passes false) ⇒ don't open a
				// route away from the weak point.
				Assert.That(RouteOpenMath.ShouldDispatch(true, true, 2, false),
					Is.False, "repairable crossing but not in the weakest sector ⇒ no-op");
			});
		}

		// ---------- Per-hut retry cooldown ----------

		[Test]
		public void CooldownEligibleWhenNoPriorFailureOrFullyElapsed()
		{
			Assert.Multiple(() =>
			{
				// Never failed here ⇒ always eligible (the lastFailTick is irrelevant).
				Assert.That(RouteOpenMath.CooldownElapsed(hasPriorFailure: false, lastFailTick: 999,
					currentTick: 0, cooldownTicks: 900), Is.True, "no prior failure ⇒ eligible");

				// Failed at t=100, cooldown 900: eligible exactly at t=1000 (boundary inclusive), not at t=999.
				Assert.That(RouteOpenMath.CooldownElapsed(true, 100, 999, 900), Is.False, "still cooling down");
				Assert.That(RouteOpenMath.CooldownElapsed(true, 100, 1000, 900), Is.True, "cooldown elapsed (inclusive)");
			});
		}

		// ---------- Bounded retry ----------

		[Test]
		public void RetryBudgetBlocksAfterMaxAttempts()
		{
			Assert.Multiple(() =>
			{
				// max 3: attempts 0,1,2 may try; 3 is spent.
				Assert.That(RouteOpenMath.CanAttempt(0, 3), Is.True);
				Assert.That(RouteOpenMath.CanAttempt(2, 3), Is.True);
				Assert.That(RouteOpenMath.CanAttempt(3, 3), Is.False, "budget exhausted");

				// max 0 = unbounded ⇒ never blocks.
				Assert.That(RouteOpenMath.CanAttempt(99, 0), Is.True, "0 ⇒ unbounded");
			});
		}

		[Test]
		public void AttemptCountIncrementsOnFailureAndResetsOnSuccess()
		{
			Assert.Multiple(() =>
			{
				// Failure bumps the count (bounded later by CanAttempt).
				Assert.That(RouteOpenMath.NextAttemptCount(0, success: false), Is.EqualTo(1), "first failure ⇒ 1");
				Assert.That(RouteOpenMath.NextAttemptCount(2, success: false), Is.EqualTo(3), "each failure increments");

				// Success resets to 0, so a later re-destroyed bridge is a fresh target — even from an exhausted count.
				Assert.That(RouteOpenMath.NextAttemptCount(3, success: true), Is.EqualTo(0), "success clears failure memory");

				// After a success-reset the hut is eligible again (was budget-exhausted at attempts==max).
				Assert.That(RouteOpenMath.CanAttempt(RouteOpenMath.NextAttemptCount(3, true), maxAttempts: 3), Is.True,
					"reset re-enables a previously budget-exhausted hut");
			});
		}

		// ---------- Mission timeout ----------

		[Test]
		public void MissionTimeoutFlipsAtBoundaryAndDisables()
		{
			Assert.Multiple(() =>
			{
				// start 500, timeout 1500 ⇒ times out at t=2000 (inclusive), not before.
				Assert.That(RouteOpenMath.MissionTimedOut(500, 1999, 1500), Is.False, "within budget");
				Assert.That(RouteOpenMath.MissionTimedOut(500, 2000, 1500), Is.True, "timed out (inclusive)");

				// timeout <= 0 disables the valve entirely.
				Assert.That(RouteOpenMath.MissionTimedOut(500, 999999, 0), Is.False, "0 ⇒ never times out");
			});
		}

		// ---------- Screen-size clamp ----------

		[Test]
		public void ScreenSizeClampsToAvailabilityAndNeverGoesNegative()
		{
			Assert.Multiple(() =>
			{
				Assert.That(RouteOpenMath.ClampScreenSize(desired: 3, available: 5), Is.EqualTo(3), "enough free ⇒ full screen");
				Assert.That(RouteOpenMath.ClampScreenSize(3, 1), Is.EqualTo(1), "capped to what is free");
				Assert.That(RouteOpenMath.ClampScreenSize(3, 0), Is.EqualTo(0), "none free ⇒ engineer alone (not blocked)");
				Assert.That(RouteOpenMath.ClampScreenSize(0, 5), Is.EqualTo(0), "desired 0 ⇒ no screen");
			});
		}
	}
}
