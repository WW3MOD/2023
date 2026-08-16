#region Copyright & License Information
/*
 * WW3MOD MoveAccelerationMath tests — the movement accelerator, converted from IEEE floating point to exact
 * integers.
 *
 * WHAT IS BEING PINNED: not a behaviour, a PROPERTY. Move is lockstep simulation, so the accelerator must produce
 * bit-identical results on every client. Floating point cannot promise that, and the quantity it fed
 * (Mobile.CurrentSpeed) carries no [Sync], so any divergence stayed invisible until it flipped a cell transition
 * one tick early on one machine.
 *
 * The exhaustive tests below sweep the whole reachable input domain — maxSpeed 1..1400 (a superset of every
 * Mobile speed the mod configures, the largest being well inside it, and terrain/speed modifiers scale it down),
 * currentSpeed 0..maxSpeed, and stepCount 1..12 (the mod configures arrays of length 2, 3, 4, 5, 7 and 9; the
 * engine default is 3).
 *
 * The reference implementation is the textbook definition of a ceiling — a floor division plus a remainder test —
 * deliberately a DIFFERENT formulation from the (a + b - 1) / b optimisation under test, so the two agreeing is
 * evidence rather than a tautology.
 *
 * HEADLINE RESULT: the integer form agrees with the removed float form on EVERY input in that domain. The
 * conversion is pure determinism hygiene with no behaviour change on this runtime, which also means the
 * "one third rounds to the wrong acceleration step" bug the investigation predicted does not exist — see
 * AccelerationStepIndex_AtExactThirds_TakesTheStepTheRatioNames for the arithmetic of why.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Activities;

namespace OpenRA.Test
{
	[TestFixture]
	public class MoveAccelerationMathTest
	{
		const int MaxSpeedSweep = 1400;
		const int MaxStepCount = 12;

		/// <summary>The exact value the accelerator is supposed to compute, written the obvious way:
		/// ceil(a*L/S) - 1, floored at 0. Independent of the formula under test.</summary>
		static int ReferenceStepIndex(int currentSpeed, int maxSpeed, int stepCount)
		{
			var numerator = (long)currentSpeed * stepCount;
			var floor = numerator / maxSpeed;
			var ceiling = numerator % maxSpeed == 0 ? floor : floor + 1;
			var index = (int)ceiling - 1;
			return index > 0 ? index : 0;
		}

		/// <summary>The expression this change removed, reproduced verbatim from Move.cs:566-568.</summary>
		static int LegacyFloatStepIndex(int currentSpeed, int maxSpeed, int stepCount)
		{
			var currentAcceleration = ((float)currentSpeed / (float)maxSpeed * (float)stepCount) - 1f;
			var flooredValue = (int)Math.Ceiling((double)currentAcceleration);
			return flooredValue >= 0 ? flooredValue : 0;
		}

		// ---------- AccelerationStepIndex ----------

		[Test]
		public void AccelerationStepIndex_MatchesExactCeilingAcrossTheWholeDomain()
		{
			for (var stepCount = 1; stepCount <= MaxStepCount; stepCount++)
			{
				for (var maxSpeed = 1; maxSpeed <= MaxSpeedSweep; maxSpeed++)
				{
					for (var currentSpeed = 0; currentSpeed <= maxSpeed; currentSpeed++)
					{
						var actual = MoveAccelerationMath.AccelerationStepIndex(currentSpeed, maxSpeed, stepCount);
						var expected = ReferenceStepIndex(currentSpeed, maxSpeed, stepCount);
						if (actual != expected)
							Assert.Fail($"AccelerationStepIndex({currentSpeed}, {maxSpeed}, {stepCount}) = {actual}, exact ceiling = {expected}");
					}
				}
			}
		}

		[Test]
		public void AccelerationStepIndex_StaysInRangeUnderTheCallersPrecondition()
		{
			// Move only reaches this branch with 0 <= currentSpeed < maxSpeed: a faster unit decelerates instead,
			// and an equal one skips the branch. Under that precondition the result must be a valid array index,
			// because Move uses it to index AccelerationSteps with no bounds check of its own.
			for (var stepCount = 1; stepCount <= MaxStepCount; stepCount++)
			{
				for (var maxSpeed = 1; maxSpeed <= MaxSpeedSweep; maxSpeed++)
				{
					for (var currentSpeed = 0; currentSpeed < maxSpeed; currentSpeed++)
					{
						var index = MoveAccelerationMath.AccelerationStepIndex(currentSpeed, maxSpeed, stepCount);
						if (index < 0 || index >= stepCount)
							Assert.Fail($"AccelerationStepIndex({currentSpeed}, {maxSpeed}, {stepCount}) = {index}, outside [0, {stepCount - 1}]");
					}
				}
			}
		}

		[Test]
		public void AccelerationStepIndex_AgreesWithTheOldFloatFormAcrossTheWholeDomain()
		{
			// THE BEHAVIOUR-CHANGE SURFACE OF THIS COMMIT, AND IT IS EMPTY. The conversion was expected to differ at
			// exact ratios — see AccelerationStepIndex_AtExactThirds for why it does not — so this sweep is what
			// establishes that removing the float changed no outcome the game can actually reach. If it ever starts
			// failing, the accelerator's behaviour has moved and every balance baseline taken before that point is
			// stale.
			//
			// This is a claim about THIS runtime. It is not, and cannot be, a claim about every runtime: the reason
			// the float is being removed is precisely that IEEE evaluation is free to vary (contraction, widening)
			// where exact integers are not. A machine on which this test fails is a machine that was silently
			// disagreeing with this one.
			var disagreements = new List<string>();
			for (var stepCount = 1; stepCount <= MaxStepCount; stepCount++)
			{
				for (var maxSpeed = 1; maxSpeed <= MaxSpeedSweep; maxSpeed++)
				{
					for (var currentSpeed = 0; currentSpeed <= maxSpeed; currentSpeed++)
					{
						var integer = MoveAccelerationMath.AccelerationStepIndex(currentSpeed, maxSpeed, stepCount);
						var legacy = LegacyFloatStepIndex(currentSpeed, maxSpeed, stepCount);
						if (integer != legacy && disagreements.Count < 20)
							disagreements.Add($"({currentSpeed}, {maxSpeed}, {stepCount}): integer {integer} vs float {legacy}");
					}
				}
			}

			Assert.That(disagreements, Is.Empty);
		}

		[Test]
		public void AccelerationStepIndex_AtExactThirds_TakesTheStepTheRatioNames()
		{
			// The knife edge — the input class where the removed float came closest to selecting the wrong step, and
			// the one the desync investigation predicted was already selecting it. It ISN'T, and the reason is worth
			// keeping: 18f/54f is 0.33333334, which does round UP, but the product 0.33333334 * 3 = 1.0000000298
			// then rounds BACK to exactly 1.0f, because floats in [1, 2) are spaced 2^-23 = 1.19e-7 apart and the
			// excess is 2.98e-8 — under half a step. So Ceiling saw 0.0, not 1e-7, and the old code picked band 0
			// after all. The suspected arithmetic bug does not exist; only the determinism hazard did.
			//
			// Pinned because it is the tightest margin in the domain: any future change to this math is most likely
			// to break here first.
			Assert.Multiple(() =>
			{
				Assert.That(MoveAccelerationMath.AccelerationStepIndex(18, 54, 3), Is.EqualTo(0),
					"18/54 is exactly one third of a 3-step ramp, so it sits at the top of band 0");
				Assert.That(LegacyFloatStepIndex(18, 54, 3), Is.EqualTo(0),
					"the removed float form agreed here — the one-third bug was predicted but does not reproduce");

				// Two thirds of the same ramp, and the same boundary on a 9-step curve (the M1 Abrams' array).
				Assert.That(MoveAccelerationMath.AccelerationStepIndex(36, 54, 3), Is.EqualTo(1));
				Assert.That(MoveAccelerationMath.AccelerationStepIndex(18, 54, 9), Is.EqualTo(2),
					"18*9/54 = 3 exactly, so band 2");
			});
		}

		[Test]
		public void AccelerationStepIndex_StandingStartTakesTheFirstStep()
		{
			// ceil(0) - 1 is -1; the floor at 0 is what makes a stopped unit accelerate at all.
			Assert.Multiple(() =>
			{
				Assert.That(MoveAccelerationMath.AccelerationStepIndex(0, 54, 3), Is.EqualTo(0));
				Assert.That(MoveAccelerationMath.AccelerationStepIndex(1, 54, 3), Is.EqualTo(0));
				Assert.That(MoveAccelerationMath.AccelerationStepIndex(0, 1, 1), Is.EqualTo(0));
			});
		}

		// ---------- RedirectSpeedRetained ----------

		[Test]
		public void RedirectSpeedRetained_IsBitIdenticalToTheOldFloatForm()
		{
			// This conversion is determinism hygiene with NO behaviour change, and that claim is checked rather
			// than asserted: the divisor is 256, a power of two, so (angleDiff - 256) / 256f was exactly
			// representable and the product stayed inside float's 24-bit mantissa on every reachable input.
			var mismatches = new List<string>();
			for (var angleDiff = 257; angleDiff <= 512; angleDiff++)
			{
				for (var penalty = 0; penalty <= 100; penalty++)
				{
					var turnFraction = (angleDiff - 256) / 256f;
					var legacy = 100 - (int)(turnFraction * (100 - penalty));
					var integer = MoveAccelerationMath.RedirectSpeedRetained(angleDiff, penalty);
					if (legacy != integer)
						mismatches.Add($"angleDiff {angleDiff}, penalty {penalty}: float {legacy} vs integer {integer}");
				}
			}

			Assert.That(mismatches, Is.Empty);
		}

		[Test]
		public void RedirectSpeedRetained_ScalesFromNoPenaltyAtNinetyDegreesToFullAtOneEighty()
		{
			Assert.Multiple(() =>
			{
				// The mod's only configured value (infantry.yaml:51).
				Assert.That(MoveAccelerationMath.RedirectSpeedRetained(512, 50), Is.EqualTo(50), "a full reversal keeps the penalty value");
				Assert.That(MoveAccelerationMath.RedirectSpeedRetained(384, 50), Is.EqualTo(75), "halfway between 90 and 180 keeps three quarters");
				Assert.That(MoveAccelerationMath.RedirectSpeedRetained(257, 50), Is.EqualTo(100), "just past 90 is still effectively free");
				Assert.That(MoveAccelerationMath.RedirectSpeedRetained(512, 0), Is.EqualTo(0), "a penalty of 0 stops the unit dead on a reversal");
				Assert.That(MoveAccelerationMath.RedirectSpeedRetained(512, 100), Is.EqualTo(100), "a penalty of 100 disables the mechanic");
			});
		}
	}
}
