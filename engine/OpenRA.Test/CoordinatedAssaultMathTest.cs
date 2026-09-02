#region Copyright & License Information
/*
 * WW3MOD CoordinatedAssaultMath tests — coordinated assaults (@experimental).
 *
 * Pure-logic pins for the gate that stops the bot trickling units into a defence: an axis at its start line
 * must out-mass what it BELIEVES it faces (MassSufficient) and then wait for a quorum of its peers before the
 * set steps off together (ShouldHoldForSync over QuorumMet). All three are world-free statics, so the whole
 * decision is validated here with no game run (influence-stack.md §Invariants).
 *
 * The headline pins are the FAIL-OPEN edges, because every one of them is a way this gate could otherwise
 * freeze an army: the flag off, a zero window, the hard release at window expiry, a lone axis with nobody to
 * synchronize with, and a disabled quorum. The bounded window is asserted to release BOTH hold reasons — the
 * under-massed arm as well as the waiting-for-peers arm — since a release that only covered one would let an
 * axis be handed from one hold to the other and wait twice, which is the deadlock shape in disguise.
 *
 * MassSufficient is pinned RELATIVE rather than absolute on purpose. The absolute advance-strength floor
 * (MinAdvanceStrength) is the shape that already failed in this module — it parked small axes in the rear for
 * whole matches because a two-hull axis can never clear an absolute bar — so the "small axis facing nothing
 * still commits" case is asserted explicitly as the property that keeps this gate terminating.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CoordinatedAssaultMathTest
	{
		// Mirrors PoiOffensiveBotModuleInfo's shipped defaults so the pins double as documentation of the knobs.
		const int MassRatio = 150;
		const int Quorum = 60;
		const int Window = 300;

		[Test]
		public void MassSufficient_IsARatioAgainstTheBelievedDefence()
		{
			// 150% means "bring half again what you think is there".
			Assert.That(CoordinatedAssaultMath.MassSufficient(1500, 1000, MassRatio), Is.True, "exactly 1.5x clears the bar");
			Assert.That(CoordinatedAssaultMath.MassSufficient(1501, 1000, MassRatio), Is.True, "above the bar");
			Assert.That(CoordinatedAssaultMath.MassSufficient(1499, 1000, MassRatio), Is.False, "a hair under still trickles");
			Assert.That(CoordinatedAssaultMath.MassSufficient(400, 1000, MassRatio), Is.False, "the classic trickle: a fraction of the defence");
		}

		[Test]
		public void MassSufficient_NeverBecomesAnAbsoluteFloor()
		{
			// THE property that keeps this gate terminating, and the one MinAdvanceStrength lacked: an axis that
			// believes it faces nothing is sufficient AT ANY SIZE, so an unopposed walk-in stays a walk-in and a
			// small axis is never parked in the rear waiting for a mass it will never be funded.
			Assert.That(CoordinatedAssaultMath.MassSufficient(1, 0, MassRatio), Is.True, "one unit, nothing believed ⇒ go");
			Assert.That(CoordinatedAssaultMath.MassSufficient(0, 0, MassRatio), Is.True, "degenerate both-zero ⇒ go");
			Assert.That(CoordinatedAssaultMath.MassSufficient(1, -5, MassRatio), Is.True, "a negative belief sum is still 'nothing there'");

			// A non-positive ratio disables the mass test outright (fail-open, as every other edge).
			Assert.That(CoordinatedAssaultMath.MassSufficient(1, 100000, 0), Is.True, "ratio 0 disables the test");
			Assert.That(CoordinatedAssaultMath.MassSufficient(1, 100000, -1), Is.True, "negative ratio disables the test");
		}

		[Test]
		public void MassSufficient_DoesNotOverflowOnLateGameArmyValues()
		{
			// Both operands are build-value sums scaled by 100 / a percentage, so a late-game army on both sides
			// would overflow a 32-bit product. Pinned because the widening is invisible at the call site.
			Assert.That(CoordinatedAssaultMath.MassSufficient(int.MaxValue, int.MaxValue, MassRatio), Is.False,
				"equal huge sums: 1.0x does not clear a 1.5x bar");
			Assert.That(CoordinatedAssaultMath.MassSufficient(int.MaxValue, 1, MassRatio), Is.True,
				"huge own force against a token belief clears it");
		}

		[Test]
		public void QuorumMet_IsAPercentageNotUnanimity()
		{
			// The straggler property: one pinned axis among four must not be able to hold the other three.
			Assert.That(CoordinatedAssaultMath.QuorumMet(3, 4, Quorum), Is.True, "3 of 4 = 75% clears a 60% bar");
			Assert.That(CoordinatedAssaultMath.QuorumMet(2, 4, Quorum), Is.False, "2 of 4 = 50% does not");
			Assert.That(CoordinatedAssaultMath.QuorumMet(3, 5, Quorum), Is.True, "3 of 5 = 60% exactly clears it");

			// 100 is a legal request for unanimity — the caller's window remains its backstop.
			Assert.That(CoordinatedAssaultMath.QuorumMet(3, 4, 100), Is.False, "unanimity: 3 of 4 is not enough");
			Assert.That(CoordinatedAssaultMath.QuorumMet(4, 4, 100), Is.True, "unanimity: all four ready");

			// Fail-open on a degenerate census or a disabled quorum.
			Assert.That(CoordinatedAssaultMath.QuorumMet(0, 0, Quorum), Is.True, "empty census ⇒ open");
			Assert.That(CoordinatedAssaultMath.QuorumMet(0, -1, Quorum), Is.True, "negative census ⇒ open");
			Assert.That(CoordinatedAssaultMath.QuorumMet(0, 4, 0), Is.True, "quorum 0 disables the sync arm");
		}

		[Test]
		public void ShouldHoldForSync_HoldsAnUnderMassedAxisEvenWhenAlone()
		{
			// The anti-trickle arm proper. An axis that cannot fight what it faces holds whether or not it has
			// company, and whether or not a quorum is already met — a ready quorum is not a reason to send a unit
			// that loses.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 1, 0, Quorum, 0, Window), Is.True,
				"alone and under-massed ⇒ gather first");
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 4, 4, Quorum, 0, Window), Is.True,
				"under-massed holds even with quorum already met");
		}

		[Test]
		public void ShouldHoldForSync_HoldsAMassedAxisUntilItsPeersAreReady()
		{
			// The synchronization arm: massed, but the others are not there yet.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, true, 4, 1, Quorum, 0, Window), Is.True,
				"1 of 4 ready ⇒ wait for the others");
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, true, 4, 2, Quorum, 0, Window), Is.True,
				"2 of 4 = 50%, still under the 60% bar");

			// Quorum reached ⇒ the whole set releases on this same evaluation pass. This is the coordination.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, true, 4, 3, Quorum, 0, Window), Is.False,
				"3 of 4 clears quorum ⇒ step off together");
		}

		[Test]
		public void ShouldHoldForSync_NeverHoldsALoneMassedAxis()
		{
			// Nobody to synchronize WITH, so waiting out a window would buy nothing and cost the whole window.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, true, 1, 1, Quorum, 0, Window), Is.False,
				"the only axis at a start line presses on");
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, true, 0, 0, Quorum, 0, Window), Is.False,
				"degenerate empty census ⇒ open");
		}

		[Test]
		public void ShouldHoldForSync_TheWindowReleasesBOTHHoldReasons()
		{
			// THE load-bearing safety property. Whichever arm is holding, the countdown ends it — so an axis can
			// never be handed from the mass hold to the sync hold and wait twice, and no combination of knobs
			// expresses a permanent hold.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 4, 0, Quorum, Window, Window), Is.False,
				"under-massed, window elapsed ⇒ attack anyway");
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, true, 4, 1, Quorum, Window, Window), Is.False,
				"peers not ready, window elapsed ⇒ attack anyway");
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 4, 0, Quorum, Window + 500, Window), Is.False,
				"well past expiry stays released");

			// The last tick of the window still holds — the release is at >=, not >.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 4, 0, Quorum, Window - 1, Window), Is.True,
				"one tick short of expiry still holds");
		}

		[Test]
		public void ShouldHoldForSync_FailsOpenOnEveryDisablingEdge()
		{
			// Flag off is the C# default and what the @stable twin reads ⇒ byte-identical, no hold, ever.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(false, false, 4, 0, Quorum, 0, Window), Is.False,
				"disabled ⇒ never holds, even under-massed with no peers ready");

			// A mis-set window disables the gate outright rather than holding forever.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 4, 0, Quorum, 0, 0), Is.False,
				"zero window ⇒ gate off");
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 4, 0, Quorum, 0, -1), Is.False,
				"negative window ⇒ gate off");

			// Quorum disabled leaves the mass arm live but never waits for peers.
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, true, 4, 0, 0, 0, Window), Is.False,
				"quorum 0 ⇒ a massed axis never waits");
			Assert.That(CoordinatedAssaultMath.ShouldHoldForSync(true, false, 4, 0, 0, 0, Window), Is.True,
				"quorum 0 does NOT disable the mass arm");
		}
	}
}
