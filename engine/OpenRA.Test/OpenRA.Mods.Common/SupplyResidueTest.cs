#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Contract for SupplyProvider.ResidueVerdict — the exact predicate the live scan calls
	/// (UpdateTarget) to drive the residueUnusable latch. Inputs mirror what a greatest-need
	/// scan produces: whether a serviceable unit cleared MinNeedThreshold (a best target was
	/// picked), and whether an unaffordable needy unit is in reach. This pins the real latch
	/// rule — including the mixed case where a near-full affordable unit does NOT keep a
	/// residue "usable" — so regressions break a unit test rather than a playtest.
	/// </summary>
	[TestFixture]
	public class SupplyResidueTest
	{
		[Test]
		public void DrainedProviderCountsAsEmpty()
		{
			// No supply at all → empty regardless of nearby demand.
			Assert.That(SupplyProvider.ResidueVerdict(0, serviceableNeedyPresent: false, unaffordableNeedyPresent: false), Is.True);
			Assert.That(SupplyProvider.ResidueVerdict(-5, serviceableNeedyPresent: true, unaffordableNeedyPresent: true), Is.True);
		}

		[Test]
		public void ServiceableUnitMakesResidueUsable()
		{
			// A reachable unit we can afford met the need threshold → not residue, keep serving.
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: true, unaffordableNeedyPresent: false), Is.False);
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: true, unaffordableNeedyPresent: true), Is.False);
		}

		[Test]
		public void UnaffordableDemandWithNoServiceableUnitIsUnusableResidue()
		{
			// Demand exists but we can't afford a batch for anyone → evacuate.
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: false, unaffordableNeedyPresent: true), Is.True);
		}

		[Test]
		public void MixedNearFullAffordablePlusUnaffordableNeedyCountsAsEmpty()
		{
			// The case the old pure predicate got backwards: the only affordable unit is
			// near-full (below MinNeedThreshold, so no best target was picked → serviceable
			// = false), while a needy unit we can't afford is also present. Live latches true;
			// this test locks that in.
			Assert.That(SupplyProvider.ResidueVerdict(40, serviceableNeedyPresent: false, unaffordableNeedyPresent: true), Is.True);
		}

		[Test]
		public void NoDemandLeavesLatchUnchanged()
		{
			// Supply remains, nobody needs anything in reach → indeterminate (null). The caller
			// leaves the latch as-is: a waiting truck stays waiting; an evacuating one stays so.
			Assert.That(SupplyProvider.ResidueVerdict(200, serviceableNeedyPresent: false, unaffordableNeedyPresent: false), Is.Null);
		}

		[Test]
		public void ServiceableWinsOverUnaffordableWhenBothPresent()
		{
			// If we can serve someone, the residue is usable even if another unit is
			// unaffordable — serviceable takes precedence.
			Assert.That(SupplyProvider.ResidueVerdict(1, serviceableNeedyPresent: true, unaffordableNeedyPresent: true), Is.False);
		}

		// ---------------------------------------------------------------------------------------
		// The DWELL that sits between the verdict above and the latch (2026-08-08). The verdict is
		// re-decided every ScanInterval = 7 ticks and flips BOTH ways, while its consumer
		// (DropsSupplyCache.ITick) re-checks CountsAsEmpty EVERY tick and queues RotateToEdge — a
		// drive off the map and a sale — within one tick of it reading true. Undamped, one bad
		// 7-tick sample sold the truck.
		//
		// SCOPE OF THESE TESTS, STATED RATHER THAN IMPLIED: they pin the rule, not the wiring. The
		// test project constructs no World and no Actor (checked: nothing under OpenRA.Test does),
		// so SupplyProvider.UpdateTarget — the single call site — is not reachable from NUnit, and
		// reverting THE CALL SITE alone would leave every test below green. What is defended
		// structurally instead is that there is exactly one assignment of residueUnusable from a
		// verdict, and it goes through StepResidueConfirmations/ResidueLatched. A reviewer adding a
		// second one is the failure mode no test here catches.
		// ---------------------------------------------------------------------------------------

		static bool RunScans(int required, params bool?[] verdicts)
		{
			var latched = false;
			var confirmations = 0;
			foreach (var v in verdicts)
			{
				confirmations = SupplyProvider.StepResidueConfirmations(confirmations, v, required);
				latched = SupplyProvider.ResidueLatched(latched, v, confirmations, required);
			}

			return latched;
		}

		[Test]
		public void LatchingNeedsConsecutiveConfirmations()
		{
			// Four unusable verdicts in a row are still not enough at ResidueConfirmScans = 5; the
			// fifth latches. This is the test that goes red if the dwell is weakened or removed.
			Assert.That(RunScans(5, true), Is.False);
			Assert.That(RunScans(5, true, true), Is.False);
			Assert.That(RunScans(5, true, true, true), Is.False);
			Assert.That(RunScans(5, true, true, true, true), Is.False);
			Assert.That(RunScans(5, true, true, true, true, true), Is.True);
		}

		[Test]
		public void OneUsableVerdictDestroysTheAccumulatedEvidence()
		{
			// Four trues, then a single "someone can be served", then four more trues: still not
			// latched, because the counter reset. Red if the reset is dropped and the count merely
			// pauses — which would let an oscillating verdict latch by accumulating across gaps.
			Assert.That(RunScans(5, true, true, true, true, false, true, true, true, true), Is.False);
			Assert.That(RunScans(5, true, true, true, true, false, true, true, true, true, true), Is.True);
		}

		[Test]
		public void ClearingIsNeverDamped()
		{
			// Latched, then one usable verdict → clear on that same scan, no counting down. The
			// asymmetry is deliberate: latching sells the truck (irreversible), clearing only
			// resumes serving (undone by the next scan). Red if someone symmetrises the damper.
			Assert.That(RunScans(5, true, true, true, true, true, false), Is.False);
		}

		[Test]
		public void NoDemandHoldsBothLatchAndEvidence()
		{
			// null = no demand in reach. It must neither confirm nor deny: the latch holds (the
			// pre-existing contract) and so does the count, so scans with nobody around neither
			// advance nor undo a pending judgement.
			Assert.That(SupplyProvider.ResidueLatched(true, null, 0, 5), Is.True);
			Assert.That(SupplyProvider.ResidueLatched(false, null, 4, 5), Is.False);
			Assert.That(SupplyProvider.StepResidueConfirmations(3, null, 5), Is.EqualTo(3));

			// Interleaved nulls neither latch on their own nor reset the evidence.
			Assert.That(RunScans(5, true, null, true, null, true, null, true), Is.False);
			Assert.That(RunScans(5, true, null, true, null, true, null, true, true), Is.True);
		}

		[Test]
		public void ZeroOrOneRequiredIsTheUndampedBaseline()
		{
			// The documented off-switch: the field can be turned back to the pre-damper behaviour.
			Assert.That(RunScans(1, true), Is.True);
			Assert.That(RunScans(0, true), Is.True);
			Assert.That(RunScans(-3, true), Is.True);
		}

		[Test]
		public void ConfirmationsSaturateAtTheRequirement()
		{
			// A long-latched truck must stay exactly one usable verdict away from clearing, not
			// however many scans it spent latched — and the counter must not grow without bound.
			var confirmations = 0;
			for (var i = 0; i < 50; i++)
				confirmations = SupplyProvider.StepResidueConfirmations(confirmations, true, 5);

			Assert.That(confirmations, Is.EqualTo(5));
			Assert.That(SupplyProvider.StepResidueConfirmations(confirmations, false, 5), Is.EqualTo(0));
		}

		[Test]
		public void DrainedTruckDoesNotWaitOutTheDwell()
		{
			// The dwell governs the RESIDUE judgement only. A genuinely empty truck evacuates through
			// CountsAsEmpty's independent `currentSupply <= 0` term, so nothing here can delay it —
			// and Tick early-returns on that case before UpdateTarget runs at all. Pinned because
			// "the damper strands empty trucks at the front" is the obvious fear this design invites.
			Assert.That(SupplyProvider.ResidueVerdict(0, serviceableNeedyPresent: false, unaffordableNeedyPresent: false), Is.True);
		}
	}
}
