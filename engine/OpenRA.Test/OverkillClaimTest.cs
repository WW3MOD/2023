#region Copyright & License Information
/*
 * WW3MOD OverkillClaim tests — the release half of overkill prevention.
 *
 * Overkill prevention works by having a shooter RESERVE a share of its target's health before firing, so that
 * other units scanning in the same window see the target as spoken for. The reservation was previously a bare
 * `AverageDamagePercent += n` with no owner and therefore no way back: every commitment pushed the tally up and
 * only the periodic halving in Actor.Tick ever pulled it down. A target under sustained attention read as
 * permanently over-committed, AutoTarget.ChooseTarget declined it, and an AA battery engaged one unit at a time
 * instead of together.
 *
 * The load-bearing property pinned here is that A CLAIM TAKEN AND RELEASED LEAVES NOTHING BEHIND. Every test in
 * this fixture ends by asserting the tally is back where it started, because "the tally returns to zero" is the
 * whole difference between a reservation and a leak. The second property is that ONE SHOOTER IS ONE CLAIM:
 * re-committing replaces rather than stacks, so a unit re-acquiring the same target each rescan cannot inflate
 * the tally by itself.
 *
 * These exercise the real OverkillClaim that Actor delegates to — not a reimplementation of its arithmetic —
 * through the same IOverkillTally seam Actor implements.
 */
#endregion

using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class OverkillClaimTest
	{
		sealed class FakeTally : IOverkillTally
		{
			public int Tally;

			void IOverkillTally.AddIncomingDamage(int percent)
			{
				Tally += percent;
			}

			void IOverkillTally.RemoveIncomingDamage(int percent)
			{
				Tally = OverkillClaimMath.Release(Tally, percent);
			}
		}

		[Test]
		public void ClaimThenRelease_LeavesTheTallyWhereItStarted()
		{
			var target = new FakeTally();
			var shooter = new OverkillClaim();

			shooter.Claim(target, 100);
			Assert.That(target.Tally, Is.EqualTo(100), "committing must make the target look spoken for");

			shooter.Release();

			// THE POINT OF THE FIX. Before it, this read 100 forever (less the periodic halving).
			Assert.That(target.Tally, Is.EqualTo(0), "a claim taken and released must leave nothing behind");
			Assert.That(shooter.IsHeld, Is.False);
		}

		[Test]
		public void ReleasingTwice_DoesNotKeepSubtracting()
		{
			// Every shot fired runs the release path, and most shots have no claim outstanding. A release that
			// kept subtracting would drive other shooters' live claims off the tally.
			var target = new FakeTally();
			var shooter = new OverkillClaim();

			shooter.Claim(target, 60);
			shooter.Release();
			shooter.Release();
			shooter.Release();

			Assert.That(target.Tally, Is.EqualTo(0));
		}

		[Test]
		public void ReleasingWithoutClaiming_IsANoOp()
		{
			var target = new FakeTally();
			((IOverkillTally)target).AddIncomingDamage(70);

			new OverkillClaim().Release();

			Assert.That(target.Tally, Is.EqualTo(70), "a shooter with no claim must not disturb someone else's");
		}

		[Test]
		public void ReCommittingToTheSameTarget_ReplacesRatherThanStacks()
		{
			// The AA case: a unit re-acquires the same aircraft on every 16-32 tick rescan. Under a stacking
			// tally four rescans from ONE shooter would read as four shooters' worth of commitment.
			var target = new FakeTally();
			var shooter = new OverkillClaim();

			shooter.Claim(target, 100);
			shooter.Claim(target, 100);
			shooter.Claim(target, 100);

			Assert.That(target.Tally, Is.EqualTo(100), "one shooter is one claim, however often it re-commits");

			shooter.Release();
			Assert.That(target.Tally, Is.EqualTo(0));
		}

		[Test]
		public void SwitchingTargets_HandsTheOldClaimBack()
		{
			var abandoned = new FakeTally();
			var acquired = new FakeTally();
			var shooter = new OverkillClaim();

			shooter.Claim(abandoned, 80);
			shooter.Claim(acquired, 45);

			Assert.Multiple(() =>
			{
				Assert.That(abandoned.Tally, Is.EqualTo(0), "a target this shooter walked away from is not still reserved");
				Assert.That(acquired.Tally, Is.EqualTo(45));
			});

			shooter.Release();
			Assert.That(acquired.Tally, Is.EqualTo(0));
		}

		[Test]
		public void OneShootersRelease_LeavesAnotherShootersClaimStanding()
		{
			// Two AA on one helicopter. When the first fires, the second is still aiming and its reservation
			// must survive — otherwise a third unit sees a target nobody has committed to.
			var target = new FakeTally();
			var first = new OverkillClaim();
			var second = new OverkillClaim();

			first.Claim(target, 100);
			second.Claim(target, 100);
			Assert.That(target.Tally, Is.EqualTo(200));

			first.Release();
			Assert.That(target.Tally, Is.EqualTo(100), "releasing one claim must not release the other");

			second.Release();
			Assert.That(target.Tally, Is.EqualTo(0));
		}

		[Test]
		public void ANonPositiveClaim_ReservesNothingAndStillReleases()
		{
			// EstimatePercentDamage returns 0 for a weapon that cannot hurt the target. Committing to something
			// harmless must not leave the previous reservation standing.
			var oldTarget = new FakeTally();
			var harmless = new FakeTally();
			var shooter = new OverkillClaim();

			shooter.Claim(oldTarget, 100);
			shooter.Claim(harmless, 0);

			Assert.Multiple(() =>
			{
				Assert.That(oldTarget.Tally, Is.EqualTo(0));
				Assert.That(harmless.Tally, Is.EqualTo(0));
				Assert.That(shooter.IsHeld, Is.False);
			});
		}

		// ---------- OverkillClaimMath.Release ----------

		[Test]
		public void Release_IsClampedAtZeroBecauseTheTallyDecaysUnderneathTheClaim()
		{
			// Actor.Tick halves the tally every 60 ticks, independently of who holds what. A claim of 100 that
			// outlives two halvings is only worth 25 on the tally by the time it is handed back. Subtracting the
			// recorded 100 unclamped would leave -75, and a negative tally makes every future comparison in
			// ChooseTarget nonsense.
			Assert.Multiple(() =>
			{
				Assert.That(OverkillClaimMath.Release(25, 100), Is.EqualTo(0));
				Assert.That(OverkillClaimMath.Release(0, 100), Is.EqualTo(0));
				Assert.That(OverkillClaimMath.Release(100, 100), Is.EqualTo(0));
				Assert.That(OverkillClaimMath.Release(150, 100), Is.EqualTo(50));
			});
		}
	}
}
