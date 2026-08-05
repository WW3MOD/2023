#region Copyright & License Information
/*
 * WW3MOD SpawnFlowMath tests — @experimental SR flow shape (fresh-reinforcement commit doctrine).
 *
 * Pure-logic pins for the gate that implements the user's 2026-08-05 SR-flow-shape decision: fresh spawns
 * ADVANCE IMMEDIATELY, SINGLY instead of forward-assembling with a capped wait at the forward muster.
 *
 * Two things are pinned here without a game:
 *   1. The gate itself — the byte-identity contract: flag OFF (the C# default, and what the @stable twin
 *      reads) ⇒ never suppresses, so the shipped forward-assemble shape is untouched.
 *   2. The COMPOSITION the engine actually evaluates (PoiOffensiveBotModule.DamperShouldHold: the gate
 *      NAND'd with RetreatDamperMath.ShouldHold), because that is where the behaviour lives. The composed
 *      fixtures pin both halves of the intent: the fill-completion massing hold (arm b) disappears, and the
 *      post-retreat dwell (arm a) plus the "never delays a genuine withdrawal" guard survive intact.
 *
 * The sequence fixture is the one that would catch a regression to the old shape: it walks an axis filling
 * up over successive evals and asserts it advances on EVERY one, where the forward-assemble shape holds it
 * until the fill completes or the massing budget is spent.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SpawnFlowMathTest
	{
		// Damper knobs mirroring the live @experimental profile (ai.yaml): dwell 3, floor 1200, cap 6.
		const int Dwell = 3;
		const int Floor = 1200;
		const int Cap = 6;

		// Fill levels are kept ENGINE-REACHABLE: step 9 of Reevaluate retires any axis below MinAxisSize (3 in
		// the @experimental profile) before CommitAndOrder is called, so the damper never sees a 1-of-N axis.
		// The massing window an axis can actually be observed in is [MinAxisSize, AllocatedSize).
		const int MinAxisSize = 3;
		const int Allocated = 6;

		// Exactly the composition PoiOffensiveBotModule.DamperShouldHold evaluates. Kept in one place so a
		// change to the engine's wiring that these fixtures no longer model is visible as a diff here too.
		static bool DamperShouldHold(bool immediateCommit, RetreatDecision retreat, int readvanceHold,
			bool nearRally, int ownStrength, int currentUnits, int allocatedUnits, int fillHoldEvals)
		{
			return !SpawnFlowMath.SuppressMassingHold(immediateCommit, readvanceHold)
				&& RetreatDamperMath.ShouldHold(retreat, readvanceHold, nearRally, ownStrength, Floor,
					currentUnits, allocatedUnits, fillHoldEvals, Cap);
		}

		// ---------- The gate ----------

		[Test]
		public void SuppressMassingHold_OffIsAlwaysFalse()
		{
			Assert.Multiple(() =>
			{
				Assert.That(SpawnFlowMath.SuppressMassingHold(false, 0), Is.False,
					"flag off ⇒ frozen: the forward-assemble shape is untouched");
				Assert.That(SpawnFlowMath.SuppressMassingHold(false, Dwell), Is.False);
			});
		}

		[Test]
		public void SuppressMassingHold_OnSuppressesOnlyWhenNoDwellIsArmed()
		{
			Assert.Multiple(() =>
			{
				Assert.That(SpawnFlowMath.SuppressMassingHold(true, 0), Is.True,
					"no dwell armed ⇒ the only reachable hold is the massing arm ⇒ suppress it");

				// The conjunct that bounds the blast radius to the reinforcement flow: with a dwell armed the
				// hold ShouldHold would return is arm (a), which this doctrine does not touch.
				Assert.That(SpawnFlowMath.SuppressMassingHold(true, 1), Is.False);
				Assert.That(SpawnFlowMath.SuppressMassingHold(true, Dwell), Is.False);
			});
		}

		// ---------- Composed: the massing hold is gone ----------

		[Test]
		public void ImmediateCommit_StillFillingAxisAdvancesInsteadOfMustering()
		{
			// A newly-formed axis standing in the SR bubble: near the rally, below the strength floor, holding
			// 3 of the 6 units the allocator promised it, budget untouched. This is the fresh-reinforcement
			// case the fork record is about.
			Assert.That(DamperShouldHold(false, RetreatDecision.Engaged, 0, true, 300, MinAxisSize, Allocated, 0),
				Is.True, "forward-assemble (flag off): the axis waits at the muster for the rest of its allocation");

			Assert.That(DamperShouldHold(true, RetreatDecision.Engaged, 0, true, 300, MinAxisSize, Allocated, 0),
				Is.False, "advance immediately, singly: it commits straight to the objective at 3 of 6");
		}

		[Test]
		public void ImmediateCommit_HoldsNowhereInTheMassingBubbleRegardlessOfFillOrBudget()
		{
			// Sweep the whole state space arm (b) reads — fill level and remaining budget — since the doctrine
			// is "zero assembly", not "less assembly".
			for (var units = MinAxisSize; units <= Allocated; units++)
			{
				for (var held = 0; held <= Cap; held++)
				{
					Assert.That(DamperShouldHold(true, RetreatDecision.Engaged, 0, true, 300, units, Allocated, held),
						Is.False, $"immediate commit must never hold to mass (units={units} held={held})");
				}
			}
		}

		// ---------- Composed: what the doctrine deliberately leaves alone ----------

		[Test]
		public void ImmediateCommit_PostRetreatDwellStillHolds()
		{
			// Arm (a) is retreat-oscillation damping for a unit set that has already fought and withdrawn — not
			// reinforcement assembly. Suppressing it would resurrect the advance/lose/retreat ping-pong.
			Assert.Multiple(() =>
			{
				Assert.That(DamperShouldHold(true, RetreatDecision.Engaged, Dwell, false, 5000, Allocated, Allocated, 0), Is.True,
					"a just-retreated axis still dwells before re-advancing");
				Assert.That(DamperShouldHold(true, RetreatDecision.Engaged, 1, false, 5000, Allocated, Allocated, 0), Is.True,
					"the last dwell eval holds too");
			});
		}

		[Test]
		public void ImmediateCommit_NeverDelaysAGenuineWithdrawal()
		{
			// The damper's load-bearing safety property, re-pinned through the new composition: a Retreating
			// axis is owned by the retreat path under EITHER arm of the fork.
			Assert.Multiple(() =>
			{
				Assert.That(DamperShouldHold(true, RetreatDecision.Retreating, 0, true, 300, MinAxisSize, Allocated, 0), Is.False);
				Assert.That(DamperShouldHold(false, RetreatDecision.Retreating, 0, true, 300, MinAxisSize, Allocated, 0), Is.False);
			});
		}

		[Test]
		public void ImmediateCommit_ForwardAxisIsUnaffected()
		{
			// Arm (b) already required nearRally, so an axis at the line was never held to mass. The gate must
			// not change that reading in either direction.
			Assert.Multiple(() =>
			{
				Assert.That(DamperShouldHold(true, RetreatDecision.Engaged, 0, false, 300, MinAxisSize, Allocated, 0), Is.False);
				Assert.That(DamperShouldHold(false, RetreatDecision.Engaged, 0, false, 300, MinAxisSize, Allocated, 0), Is.False);
			});
		}

		// ---------- The sequence a regression to the old shape would fail ----------

		[Test]
		public void Sequence_ReinforcementsCommitOnEveryEvalWhileTheAxisFills()
		{
			// An axis massing at the SR while its allocation of 6 walks up one unit per eval, from the 3 it needs
			// to exist at all. Under the shipped forward-assemble shape it holds until the fill completes (or the
			// budget is spent); under the user's pick it advances from the first eval, arriving piecemeal.
			var fillHoldEvals = 0;
			var immediateHolds = 0;

			for (var units = MinAxisSize; units <= Allocated; units++)
			{
				var hold = DamperShouldHold(true, RetreatDecision.Engaged, 0, true, 300, units, Allocated, fillHoldEvals);
				if (hold)
					immediateHolds++;

				var stillMassing = RetreatDamperMath.FillIncomplete(units, Allocated);
				fillHoldEvals = RetreatDamperMath.StepFillHold(fillHoldEvals, hold, stillMassing);
			}

			Assert.That(immediateHolds, Is.EqualTo(0), "zero assembly: not one eval spent waiting at the muster");
			Assert.That(fillHoldEvals, Is.EqualTo(0),
				"no hold was ever taken, so the massing budget is never drawn down");

			// The same walk under the flag OFF must still show the shipped behaviour — this is the byte-identity
			// half, and it is what fails if the gate is ever wired to fire unconditionally.
			fillHoldEvals = 0;
			var assembleHolds = 0;
			for (var units = MinAxisSize; units <= Allocated; units++)
			{
				var hold = DamperShouldHold(false, RetreatDecision.Engaged, 0, true, 300, units, Allocated, fillHoldEvals);
				if (hold)
					assembleHolds++;

				var stillMassing = RetreatDamperMath.FillIncomplete(units, Allocated);
				fillHoldEvals = RetreatDamperMath.StepFillHold(fillHoldEvals, hold, stillMassing);
			}

			Assert.That(assembleHolds, Is.EqualTo(3),
				"forward-assemble holds on each under-filled eval (3,4,5 of 6) and releases on the full one");
		}
	}
}
