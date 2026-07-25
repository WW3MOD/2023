#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
	/// Pins the pure Stage-2 halt-before-contact decision (PIPELINE item 8). The world-touching parts
	/// (GetConditionCount gate read + the group-detection scan) live in AttackMoveActivity; the
	/// combinator that turns those into "halt vs engage" is <see cref="AmbushTactics.ShouldHaltBeforeContact"/>
	/// so it can be exercised here with no simulation harness.
	///
	/// The load-bearing invariant for the ship's default-off / byte-identity contract is the FIRST test:
	/// with the gate OFF the decision is ALWAYS "engage" (false) regardless of the other inputs — that is
	/// exactly why @stable / control bots, and every un-opted-in unit, keep the stock attack-move path.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/AmbushTactics.cs
	/// </summary>
	[TestFixture]
	public class AmbushTacticsTest
	{
		[Test]
		public void GateOffNeverHalts()
		{
			// The byte-identity guarantee: tacticsEnabled == false ⇒ false for EVERY combination of the
			// remaining inputs, so the original engage path always runs when the gate is not granted.
			foreach (var stance in new[] { UnitStance.HoldFire, UnitStance.Ambush, UnitStance.FireAtWill })
				foreach (var hasTarget in new[] { false, true })
					foreach (var detected in new[] { false, true })
						Assert.That(
							AmbushTactics.ShouldHaltBeforeContact(false, stance, hasTarget, detected),
							Is.False,
							$"gate off must never halt (stance={stance}, hasTarget={hasTarget}, detected={detected})");
		}

		[Test]
		public void OnlyAmbushStanceHalts()
		{
			// FireAtWill / HoldFire units never halt even with the gate on, a target present and unseen.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.FireAtWill, true, false), Is.False);
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.HoldFire, true, false), Is.False);

			// Ambush + gate on + valid target + still unseen ⇒ the one combination that halts.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, true, false), Is.True);
		}

		[Test]
		public void NoTargetNeverHalts()
		{
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, false, false), Is.False);
		}

		[Test]
		public void DetectedGroupEngagesInsteadOfHalting()
		{
			// Once the ambush is blown (a group member is visible to the enemy) the unit must NOT hold
			// fire from an exposed position — it falls through to the immediate engage path.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, true, true), Is.False);
		}

		[Test]
		public void UndetectedAmbushWithTargetHalts()
		{
			// The positive case, stated on its own for clarity: gate on, Ambush, target present, group
			// still unseen ⇒ halt into the idle ambush.
			Assert.That(AmbushTactics.ShouldHaltBeforeContact(true, UnitStance.Ambush, true, false), Is.True);
		}

		// ────────────────────────────────────────────────────────────────────────────────────────
		// Stage 3 — stationary literal-ambush state machine (design §5.2). The world-touching parts
		// (kill-zone scan, fog filter, range/velocity sampling, gate read) live in AutoTarget; the pure
		// decision core below is pinned with no simulation harness.
		// ────────────────────────────────────────────────────────────────────────────────────────

		// ── Worthwhile score (design §3.2 — the value/threat split) ──

		[Test]
		public void ContactScoreWeightsThreatAndValue()
		{
			// weightThreat·threat + weightValue·value.
			Assert.That(AmbushTactics.ContactScore(10, 20, 3, 5), Is.EqualTo(3 * 10 + 5 * 20));
			Assert.That(AmbushTactics.ContactScore(0, 0, 7, 9), Is.EqualTo(0));
		}

		[Test]
		public void UndefendedTruckStillScoresWorthwhile()
		{
			// THE §3.2 point: a supply truck projects no threat (threat term 0) but carries economic value.
			// A value-blind danger metric would ignore it; the split keeps it worthwhile. With any positive
			// value weight the truck's contribution is strictly > 0.
			var truck = AmbushTactics.ContactScore(threatValue: 0, cellValue: 500, weightThreat: 1, weightValue: 1);
			Assert.That(truck, Is.GreaterThan(0));
			Assert.That(truck, Is.EqualTo(500));
		}

		// ── Range prediction (trigger 3 geometry) ──

		[Test]
		public void RadialSpeedFromSamplesIsSignedAndGuardsInterval()
		{
			// Opening (range grew) ⇒ positive; closing ⇒ negative; zero/neg interval ⇒ 0 (no divide).
			Assert.That(AmbushTactics.RadialSpeedPerTick(1000, 1500, 25), Is.EqualTo(20));
			Assert.That(AmbushTactics.RadialSpeedPerTick(1500, 1000, 25), Is.EqualTo(-20));
			Assert.That(AmbushTactics.RadialSpeedPerTick(1000, 2000, 0), Is.EqualTo(0));
			Assert.That(AmbushTactics.RadialSpeedPerTick(1000, 2000, -5), Is.EqualTo(0));
		}

		[Test]
		public void PredictedRangeExtrapolatesLinearly()
		{
			Assert.That(AmbushTactics.PredictedRange(1000, 20, 10), Is.EqualTo(1200));
			Assert.That(AmbushTactics.PredictedRange(1000, -20, 10), Is.EqualTo(800));
		}

		[Test]
		public void PredictedToExitRangeFiresWhenExtrapolationLeavesRange()
		{
			// maxRange 5000. Opening fast enough to be predicted outside within K ⇒ true.
			Assert.That(AmbushTactics.PredictedToExitRange(4800, 20, 20, 5000), Is.True);   // 4800 + 400 = 5200 > 5000
			// Same range but closing ⇒ never predicted to exit.
			Assert.That(AmbushTactics.PredictedToExitRange(4800, -20, 20, 5000), Is.False);  // 4800 - 400 = 4400
			// Already out of range trivially satisfies.
			Assert.That(AmbushTactics.PredictedToExitRange(5200, 0, 20, 5000), Is.True);
			// Stationary well inside range ⇒ false.
			Assert.That(AmbushTactics.PredictedToExitRange(2000, 0, 20, 5000), Is.False);
		}

		// ── Hysteresis primitives ──

		[Test]
		public void DegradeSampleRespectsEpsilonBand()
		{
			// Only an opening BEYOND the epsilon band counts as a degrade — jitter inside the band does not.
			Assert.That(AmbushTactics.IsDegradeSample(1300, 1000, 256), Is.True);   // +300 > 256
			Assert.That(AmbushTactics.IsDegradeSample(1200, 1000, 256), Is.False);  // +200 within band
			Assert.That(AmbushTactics.IsDegradeSample(1000, 1000, 256), Is.False);  // no motion
			Assert.That(AmbushTactics.IsDegradeSample(700, 1000, 256), Is.False);   // closing
		}

		[Test]
		public void SustainCounterIncrementsThenResetsOnMiss()
		{
			var c = 0;
			c = AmbushTactics.UpdateSustainCounter(c, true);   // 1
			c = AmbushTactics.UpdateSustainCounter(c, true);   // 2
			Assert.That(c, Is.EqualTo(2));
			c = AmbushTactics.UpdateSustainCounter(c, false);  // reset — a single miss breaks the streak
			Assert.That(c, Is.EqualTo(0));
			c = AmbushTactics.UpdateSustainCounter(c, true);   // 1 again
			Assert.That(c, Is.EqualTo(1));
		}

		// ── Overrun (trigger 5) ──

		[Test]
		public void OverrunFiresAtOrInsideThreshold()
		{
			Assert.That(AmbushTactics.IsOverrun(2048, 2048), Is.True);   // exactly at threshold
			Assert.That(AmbushTactics.IsOverrun(1000, 2048), Is.True);   // inside
			Assert.That(AmbushTactics.IsOverrun(3000, 2048), Is.False);  // still outside stand-off
		}

		// ── The trigger table (EvaluateSpring), design §5.2 precedence 1→5 ──

		// A "hold" input set: nothing satisfied. Individual tests flip one axis at a time.
		static AmbushSpringTrigger Eval(
			bool detected = false, bool damaged = false,
			bool predictedExit = false, int score = 0, int minSpring = 100,
			int degradeSamples = 0, int reqDegrade = 2,
			int highSamples = 0, int reqHigh = 2, bool overrun = false)
		{
			return AmbushTactics.EvaluateSpring(detected, damaged, predictedExit, score, minSpring,
				degradeSamples, reqDegrade, highSamples, reqHigh, overrun);
		}

		[Test]
		public void NothingSatisfiedHoldsFire()
		{
			Assert.That(Eval(), Is.EqualTo(AmbushSpringTrigger.None));
		}

		[Test]
		public void Trigger1DetectionSprings()
		{
			Assert.That(Eval(detected: true), Is.EqualTo(AmbushSpringTrigger.Detected));
		}

		[Test]
		public void Trigger2DamageSprings()
		{
			Assert.That(Eval(damaged: true), Is.EqualTo(AmbushSpringTrigger.Damaged));
		}

		[Test]
		public void Trigger3RequiresExitAndScoreAndHysteresis()
		{
			// All three conjuncts present ⇒ fires.
			Assert.That(Eval(predictedExit: true, score: 150, minSpring: 100, degradeSamples: 2, reqDegrade: 2),
				Is.EqualTo(AmbushSpringTrigger.BestStrikeDegrading));

			// Missing the exit prediction ⇒ hold.
			Assert.That(Eval(predictedExit: false, score: 150, degradeSamples: 2, reqDegrade: 2),
				Is.EqualTo(AmbushSpringTrigger.None));

			// Below the worthwhile floor ⇒ hold (not worth breaking concealment for a departing scrap).
			Assert.That(Eval(predictedExit: true, score: 50, minSpring: 100, degradeSamples: 2, reqDegrade: 2),
				Is.EqualTo(AmbushSpringTrigger.None));

			// Only ONE degrade sample (hysteresis not met) ⇒ hold — guards the oscillation/noise case (§3.6).
			Assert.That(Eval(predictedExit: true, score: 150, degradeSamples: 1, reqDegrade: 2),
				Is.EqualTo(AmbushSpringTrigger.None));
		}

		[Test]
		public void Trigger4SaturationSpringsWhenSustained()
		{
			// The caller only advances highSamples while score ≥ HighSpringThreshold; here we assert the
			// counter → spring mapping. Sustained ⇒ fire; not yet sustained ⇒ hold.
			Assert.That(Eval(highSamples: 2, reqHigh: 2), Is.EqualTo(AmbushSpringTrigger.Saturation));
			Assert.That(Eval(highSamples: 1, reqHigh: 2), Is.EqualTo(AmbushSpringTrigger.None));
		}

		[Test]
		public void Trigger5OverrunSprings()
		{
			Assert.That(Eval(overrun: true), Is.EqualTo(AmbushSpringTrigger.Overrun));
		}

		[Test]
		public void PrecedenceDetectionBeatsEverything()
		{
			// With every trigger simultaneously satisfiable, detection (1) is reported first.
			Assert.That(
				Eval(detected: true, damaged: true, predictedExit: true, score: 999, minSpring: 100,
					degradeSamples: 5, reqDegrade: 2, highSamples: 5, reqHigh: 2, overrun: true),
				Is.EqualTo(AmbushSpringTrigger.Detected));
		}

		[Test]
		public void PrecedenceDamageBeatsScoreTriggers()
		{
			// Damage (2) outranks the score-derived 3/4/5 — an ambush that has taken fire commits now.
			Assert.That(
				Eval(damaged: true, predictedExit: true, score: 999, degradeSamples: 5, highSamples: 5, overrun: true),
				Is.EqualTo(AmbushSpringTrigger.Damaged));
		}

		[Test]
		public void PrecedenceDegradingBeatsSaturationAndOverrun()
		{
			Assert.That(
				Eval(predictedExit: true, score: 150, minSpring: 100, degradeSamples: 2, reqDegrade: 2,
					highSamples: 5, reqHigh: 2, overrun: true),
				Is.EqualTo(AmbushSpringTrigger.BestStrikeDegrading));
		}

		[Test]
		public void PrecedenceSaturationBeatsOverrun()
		{
			Assert.That(Eval(highSamples: 2, reqHigh: 2, overrun: true), Is.EqualTo(AmbushSpringTrigger.Saturation));
		}

		// ── Degenerate cases from design §3.6, stated as named behaviours ──

		[Test]
		public void DegenerateEnemyStopsSpringsViaSaturation()
		{
			// Enemy halts in the kill zone: score never DECREASES so trigger 3 (degrading) never fires, but
			// the score sits ≥ HighSpringThreshold and the caller's high-counter saturates ⇒ trigger 4.
			Assert.That(
				Eval(predictedExit: false, degradeSamples: 0, highSamples: 2, reqHigh: 2),
				Is.EqualTo(AmbushSpringTrigger.Saturation));
		}

		[Test]
		public void DegenerateOscillationDoesNotSpringOnNoise()
		{
			// Range jitter that never accumulates the required consecutive degrade samples must NOT spring
			// trigger 3, even with a worthwhile score and a momentary exit prediction.
			Assert.That(
				Eval(predictedExit: true, score: 500, minSpring: 100, degradeSamples: 1, reqDegrade: 2),
				Is.EqualTo(AmbushSpringTrigger.None));
		}

		[Test]
		public void DegenerateFastConvoySpringsOnPredictedExit()
		{
			// A fast passer: once two consecutive opening samples establish the trend, the K-tick exit
			// PREDICTION fires trigger 3 without waiting for the target to actually leave range — the
			// look-ahead is what compensates for the coarse sample cadence (§3.6 fast-convoy case).
			Assert.That(
				Eval(predictedExit: true, score: 300, minSpring: 100, degradeSamples: 2, reqDegrade: 2),
				Is.EqualTo(AmbushSpringTrigger.BestStrikeDegrading));
		}
	}
}
