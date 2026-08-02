#region Copyright & License Information
/*
 * WW3MOD HeliMissionPinMath tests — squad-Brain Phase 4 strategic-target pinning.
 *
 * Pure-logic tests of the attack-heli strategic-target-pinning decision math that
 * stops the heli FSM's 5-tick predicate flapping from churning the squad's STRATEGIC
 * destination (root cause C, design §1.3/§3.3). Where MissionCommitmentMath governs a
 * ground offense AXIS, HeliMissionPinMath governs a heli squad's pinned OBJECTIVE — the
 * same "commit and HOLD; release only on an explicit abort trigger" contract, over the
 * trigger subset a heli FSM can feed without the full Brain's score/danger plumbing.
 *
 * The headline is Hold_PersistsWhileValidAndInsideWindow: a valid pin inside its commit
 * window is HELD; only an explicit trigger releases it. Like PoiOffenseMath /
 * MissionCommitmentMath / HeliDangerNav, the math is a pure static class validated without
 * a World, so it ports verbatim into the future SquadBrain.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Squads;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeliMissionPinMathTest
	{
		// ---------- headline: hold while valid and inside the window ----------

		[Test]
		public void Hold_PersistsWhileValidAndInsideWindow()
		{
			// A valid objective, committed at tick 100, current 500, window 1200: nowhere near the backstop
			// and still valid ⇒ the pin is HELD across every intervening FSM micro-transition.
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 100, 500, 1200), Is.EqualTo(HeliPinState.Hold));
		}

		[Test]
		public void Hold_WindowDisabled_HoldsIndefinitelyWhileValid()
		{
			// Window <= 0 disables the time valve (hold purely on validity), matching MissionCommitmentMath's
			// window: even an ancient commit holds while the objective is valid.
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 0, 1_000_000, 0), Is.EqualTo(HeliPinState.Hold));
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 0, 1_000_000, -5), Is.EqualTo(HeliPinState.Hold));
		}

		// ---------- trigger 1: objective invalid ----------

		[Test]
		public void Trigger1_InvalidObjective_ReleasesImmediately()
		{
			// A dead / gone / no-longer-enemy objective releases the instant it goes invalid — regardless of
			// how fresh the commit is or whether the window would otherwise hold.
			Assert.That(HeliMissionPinMath.EvaluatePin(false, 0, 0, 0), Is.EqualTo(HeliPinState.Release));
			Assert.That(HeliMissionPinMath.EvaluatePin(false, 100, 101, 1200), Is.EqualTo(HeliPinState.Release));
		}

		// ---------- backstop: bounded commit window ----------

		[Test]
		public void CommitWindow_ElapsedForcesReplan_BoundaryExact()
		{
			// window 1200: still held at 1199 ticks elapsed, released at exactly 1200 (>= boundary, matching
			// MissionCommitmentMath.CommitWindow_ElapsedForcesReplan).
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 0, 1199, 1200), Is.EqualTo(HeliPinState.Hold));
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 0, 1200, 1200), Is.EqualTo(HeliPinState.Release));
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 0, 1201, 1200), Is.EqualTo(HeliPinState.Release));
		}

		[Test]
		public void CommitWindow_MeasuredFromCommitTick_NotAbsolute()
		{
			// Elapsed is currentTick - commitTick: a pin committed at 5000 with window 300 holds at 5299,
			// releases at 5300 — the valve is relative to the commit, not the absolute clock.
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 5000, 5299, 300), Is.EqualTo(HeliPinState.Hold));
			Assert.That(HeliMissionPinMath.EvaluatePin(true, 5000, 5300, 300), Is.EqualTo(HeliPinState.Release));
		}

		[Test]
		public void InvalidObjective_OutranksWindow()
		{
			// Trigger 1 short-circuits before the window is consulted: invalid releases even well inside the window.
			Assert.That(HeliMissionPinMath.EvaluatePin(false, 0, 10, 1200), Is.EqualTo(HeliPinState.Release));
		}

		// ---------- trigger 2 (heli form, §3.3 N5): objective-too-hot with no divert ----------

		[Test]
		public void Trigger2_ObjectiveTooHot_NoSoftTarget_Aborts()
		{
			// Objective itself too hot AND nowhere to divert = abort the pin (do not loop back onto an
			// unassailable objective after withdrawing).
			Assert.That(HeliMissionPinMath.ObjectiveTooHotAbort(objectiveTooHot: true, softTargetAvailable: false),
				Is.True);
		}

		[Test]
		public void Trigger2_ObjectiveTooHot_WithSoftTarget_IsExecutorLiberty_NotAbort()
		{
			// Too hot BUT a soft target exists: the FSM swaps to the soft target and resumes toward the
			// objective — executor-local liberty, NOT a mission abort (§3.3 N5). The pin survives.
			Assert.That(HeliMissionPinMath.ObjectiveTooHotAbort(objectiveTooHot: true, softTargetAvailable: true),
				Is.False);
		}

		[Test]
		public void Trigger2_ObjectiveNotTooHot_NeverAborts()
		{
			// A cool objective never aborts on this trigger regardless of soft-target availability.
			Assert.That(HeliMissionPinMath.ObjectiveTooHotAbort(objectiveTooHot: false, softTargetAvailable: false),
				Is.False);
			Assert.That(HeliMissionPinMath.ObjectiveTooHotAbort(objectiveTooHot: false, softTargetAvailable: true),
				Is.False);
		}

		// ---------- anti-thrash across an eval sequence ----------

		[Test]
		public void AntiThrash_ValidPinHeldForWholeApproachWindow()
		{
			// Simulate 30 consecutive 5-tick FSM evals of a squad committed at tick 0 with a valid objective and
			// a 1200-tick window. The pin must be HELD every eval — the pure-math analogue of "the strategic
			// destination outlives the micro-transitions" (no re-pick just because the FSM flapped).
			var releases = 0;
			for (var eval = 0; eval < 30; eval++)
			{
				var currentTick = eval * 5; // 0..145, well inside the 1200 window
				if (HeliMissionPinMath.EvaluatePin(true, 0, currentTick, 1200) == HeliPinState.Release)
					releases++;
			}

			Assert.That(releases, Is.EqualTo(0), "a valid, in-window pin is never released on FSM flapping alone");
		}
	}
}
