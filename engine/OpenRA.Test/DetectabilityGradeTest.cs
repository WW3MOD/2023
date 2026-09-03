#region Copyright & License Information
/*
 * WW3MOD detectability-grade tests — the pure half of the graded visibility diamond.
 *
 * The diamond drawn over a unit is Detectability.Grade(concealment, spotted, ...). Everything that decides
 * WHICH diamond is in that one function, so it is the whole of what can be pinned without a World: the trait
 * around it only supplies the two inputs and picks a colour.
 *
 * The load-bearing property is the anti-wallhack one. Bands 0-3 read the unit's OWN posture and can never
 * reach Spotted; only the caller's `spotted` flag can, and that flag is produced by a predicate that already
 * refuses to count an enemy the viewing player has not themselves spotted. So "an enemy sees us but we have
 * not seen him" arrives here as spotted:false, and the tests below assert that no concealment value turns
 * that into a raised grade.
 *
 * The band ceilings are asserted at their SHIPPED values rather than recomputed from the constants, so a
 * retune has to come here and say so instead of silently re-deriving a passing test.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;

namespace OpenRA.Test
{
	[TestFixture]
	public class DetectabilityGradeTest
	{
		// The mods/ww3mod/rules/defaults.yaml ^UnitIndicators values, which are also the C# defaults.
		const int ConcealedCeiling = 3;
		const int LowCeiling = 5;
		const int ModerateCeiling = 7;

		static DetectabilityGrade Grade(int concealment, bool spotted = false)
		{
			return Detectability.Grade(concealment, spotted, ConcealedCeiling, LowCeiling, ModerateCeiling);
		}

		[Test]
		public void ScaleEndsAgreeWithTheDetectableClamp()
		{
			// If these two ever disagree the readout starts grading on levels Detectable cannot produce, and
			// a whole band goes dead without anything failing.
			Assert.That(Detectability.MinimumConcealment, Is.EqualTo(Detectable.ClampConcealment(int.MinValue)),
				"Detectability floor must be the floor Detectable actually clamps to.");
			Assert.That(Detectability.MaximumConcealment, Is.EqualTo(Detectable.ClampConcealment(int.MaxValue)),
				"Detectability ceiling must be the ceiling Detectable actually clamps to.");
			Assert.That(Detectability.MaximumConcealment, Is.EqualTo(9),
				"MapLayers.VisionLayers - 2. If this moved, the shipped band ceilings need re-tuning too.");
		}

		[Test]
		public void ExposureInvertsConcealment()
		{
			Assert.That(Detectability.Exposure(1), Is.EqualTo(9), "Nothing hiding the unit is maximum exposure.");
			Assert.That(Detectability.Exposure(9), Is.EqualTo(1), "Best concealment the clamp allows is minimum exposure.");
			Assert.That(Detectability.Exposure(5), Is.EqualTo(5), "The scale is symmetric about its midpoint.");
		}

		[Test]
		public void ExposureClampsOutOfRangeInputs()
		{
			// CurrentVisibility is 0 for the frame before Detectable's first tick, and a future modifier could
			// overshoot. Neither may fall off the end of the ladder.
			Assert.That(Detectability.Exposure(0), Is.EqualTo(9));
			Assert.That(Detectability.Exposure(-5), Is.EqualTo(9));
			Assert.That(Detectability.Exposure(12), Is.EqualTo(1));
		}

		[Test]
		public void BothEndsOfTheScale()
		{
			Assert.That(Grade(Detectability.MaximumConcealment), Is.EqualTo(DetectabilityGrade.Concealed),
				"Prone, dug in, in cover, veteran — the bottom of the readout.");
			Assert.That(Grade(Detectability.MinimumConcealment), Is.EqualTo(DetectabilityGrade.High),
				"Standing in the open, moving or firing — the top of the posture-only range.");
			Assert.That(Grade(Detectability.MinimumConcealment, spotted: true), Is.EqualTo(DetectabilityGrade.Spotted),
				"Actually seen is the top of the whole scale.");
		}

		// exposure -> band, walked across every boundary. Concealment is 10 - exposure.
		[TestCase(1, DetectabilityGrade.Concealed)]
		[TestCase(2, DetectabilityGrade.Concealed)]
		[TestCase(3, DetectabilityGrade.Concealed)]
		[TestCase(4, DetectabilityGrade.Low)]
		[TestCase(5, DetectabilityGrade.Low)]
		[TestCase(6, DetectabilityGrade.Moderate)]
		[TestCase(7, DetectabilityGrade.Moderate)]
		[TestCase(8, DetectabilityGrade.High)]
		[TestCase(9, DetectabilityGrade.High)]
		public void EveryExposureLevelLandsInItsShippedBand(int exposure, DetectabilityGrade expected)
		{
			var concealment = Detectability.MinimumConcealment + Detectability.MaximumConcealment - exposure;
			Assert.That(Detectability.Exposure(concealment), Is.EqualTo(exposure), "test setup");
			Assert.That(Grade(concealment), Is.EqualTo(expected));
		}

		[Test]
		public void EveryBandIsReachable()
		{
			// A ceiling set equal to the one below it would silently delete a band. With four bands in nine
			// levels that is an easy retune to make by accident.
			var seen = new System.Collections.Generic.HashSet<DetectabilityGrade>();
			for (var concealment = Detectability.MinimumConcealment; concealment <= Detectability.MaximumConcealment; concealment++)
				seen.Add(Grade(concealment));

			Assert.That(seen, Is.EquivalentTo(new[]
			{
				DetectabilityGrade.Concealed, DetectabilityGrade.Low,
				DetectabilityGrade.Moderate, DetectabilityGrade.High
			}), "Every posture band must be reachable at the shipped ceilings.");
		}

		[Test]
		public void GradeNeverFallsAsExposureRises()
		{
			var previous = DetectabilityGrade.Concealed;
			for (var exposure = 1; exposure <= Detectability.MaximumConcealment; exposure++)
			{
				var concealment = Detectability.MinimumConcealment + Detectability.MaximumConcealment - exposure;
				var grade = Grade(concealment);
				Assert.That(grade, Is.GreaterThanOrEqualTo(previous),
					$"The readout must not go backwards: exposure {exposure} graded below exposure {exposure - 1}.");
				previous = grade;
			}
		}

		[Test]
		public void AnUnspottedObserverCannotRaiseTheGrade()
		{
			// THE ANTI-WALLHACK CASE. A soldier standing in the open, watched by an enemy the viewing player
			// has not spotted, reaches this function as (low concealment, spotted:false) — because the caller's
			// spotted predicate refuses to count an observer we cannot see. He must read as exposed by his own
			// posture and NOT as Spotted; Spotted would announce a watcher the player has no right to know about.
			for (var concealment = Detectability.MinimumConcealment; concealment <= Detectability.MaximumConcealment; concealment++)
			{
				Assert.That(Grade(concealment), Is.Not.EqualTo(DetectabilityGrade.Spotted),
					$"Posture alone must never reach Spotted (concealment {concealment}).");
				Assert.That(Grade(concealment), Is.LessThanOrEqualTo(DetectabilityGrade.High));
			}
		}

		[Test]
		public void BeingSpottedOverridesEveryPosture()
		{
			// The converse of the case above, and the reason Spotted sits at the top rather than in the ladder:
			// a soldier who is dug in AND has been seen is seen. Concealment must not talk him back down.
			for (var concealment = Detectability.MinimumConcealment; concealment <= Detectability.MaximumConcealment; concealment++)
				Assert.That(Grade(concealment, spotted: true), Is.EqualTo(DetectabilityGrade.Spotted),
					$"Spotted must win at concealment {concealment}.");
		}

		[Test]
		public void SpottedIsStrictlyTheTopOfTheOrder()
		{
			// The trait compares grades with >= to choose the fill and the draw threshold, so the enum order is
			// load-bearing rather than cosmetic.
			Assert.That(DetectabilityGrade.Concealed, Is.LessThan(DetectabilityGrade.Low));
			Assert.That(DetectabilityGrade.Low, Is.LessThan(DetectabilityGrade.Moderate));
			Assert.That(DetectabilityGrade.Moderate, Is.LessThan(DetectabilityGrade.High));
			Assert.That(DetectabilityGrade.High, Is.LessThan(DetectabilityGrade.Spotted));
		}
	}
}
