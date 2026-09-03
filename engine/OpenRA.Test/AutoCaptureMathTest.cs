#region Copyright & License Information
/*
 * WW3MOD autonomous capture maths (2026-09-03).
 *
 * WHAT THIS PINS. Two questions, kept apart on purpose because they are answered by two DIFFERENT
 * stance enums on AutoTarget:
 *
 *   MAY it act at all?   UnitStance       HoldFire / Ambush / FireAtWill
 *   HOW FAR will it go?  EngagementStance HoldPosition / Defensive / Hunt
 *
 * That orthogonality is the whole design. The question posed was "Fire at will, or Hunt?", and the
 * answer the code supports is "both, on different axes": the fire stance gates, the engagement
 * stance sizes. A fresh unit is FireAtWill + Defensive (AutoTarget.cs:75,167), which is what makes
 * the behaviour default-ON at the conservative radius without a single YAML field being set.
 *
 * THE OFF SWITCHES ARE ASSERTED, both of them, because "default ON" is only safe if turning it off
 * really works: HoldFire (per unit, in game) and HoldPosition (per unit, radius 0). The mod-wide
 * one is AutoCaptureNearbyInfo.Enabled and is not maths, so it is not here.
 *
 * TARGET CHOICE is nearest, with value breaking ties among structures that are near-equally close.
 * The band is measured from the NEAREST candidate rather than pairwise — pairwise closeness is not
 * transitive, and a chain of structures would drag the whole map into one tie group and silently
 * turn the rule into pure highest-value, which is the opposite of the brief.
 *
 * WHAT NO FIXTURE HERE COVERS. That an idle technician actually notices a derrick in a running
 * game: nothing in OpenRA.Test can build a World. That is the autotest scenario's job.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AutoCaptureMathTest
	{
		const int Cell = 1024;

		static AutoCaptureMath.Candidate At(int distanceCells, int value, uint id)
		{
			return new AutoCaptureMath.Candidate(distanceCells * Cell, value, id);
		}

		[Test]
		public void HoldFireIsTheOffSwitchAndNothingElseIs()
		{
			Assert.Multiple(() =>
			{
				Assert.That(AutoCaptureMath.StancePermitsAutoCapture(UnitStance.HoldFire), Is.False,
					"HoldFire must switch autonomous capture off. It is the per-unit off switch the " +
					"feature ships with, and the only one a player can reach mid-match.");

				// The shipped default. If this ever returns false the behaviour is off for everybody
				// and the "default is that they do capture" requirement is silently unmet.
				Assert.That(AutoCaptureMath.StancePermitsAutoCapture(UnitStance.FireAtWill), Is.True);

				// Ambush means "do not give away my position by shooting first" — a statement about
				// opening fire. A technician's pistol is not what anyone is hiding, and treating
				// Ambush as an off switch would make the behaviour vanish for a stance players use
				// for positioning.
				Assert.That(AutoCaptureMath.StancePermitsAutoCapture(UnitStance.Ambush), Is.True);
			});
		}

		[Test]
		public void HoldPositionNeverVentures()
		{
			var radius = AutoCaptureMath.RadiusCellsForStance(EngagementStance.HoldPosition, 8, 20);

			Assert.Multiple(() =>
			{
				Assert.That(radius, Is.EqualTo(0),
					"HoldPosition must yield no radius at all. 'Stay put' has to mean stay put or the " +
					"stance stops being trustworthy for the one job players use it for.");

				// Belt and braces: a 0 radius must actually admit nothing, including a structure the
				// unit is standing on top of.
				Assert.That(AutoCaptureMath.WithinRadius(0, radius), Is.False,
					"a zero radius still admitted a structure at zero distance, so HoldPosition is a " +
					"very small leash rather than an off switch.");
			});
		}

		[Test]
		public void HuntVenturesFurtherThanDefensive()
		{
			// The graded-eagerness claim itself. Asserted as an ordering rather than as two literals so
			// the radii stay tunable without the test becoming a second place to edit them.
			var defensive = AutoCaptureMath.RadiusCellsForStance(EngagementStance.Defensive, 8, 20);
			var hunt = AutoCaptureMath.RadiusCellsForStance(EngagementStance.Hunt, 8, 20);

			Assert.Multiple(() =>
			{
				Assert.That(defensive, Is.EqualTo(8));
				Assert.That(hunt, Is.EqualTo(20));
				Assert.That(hunt, Is.GreaterThan(defensive),
					"Hunt must reach further than Defensive, or the stance grading conveys nothing and " +
					"the player has no way to ask for more eagerness.");
			});
		}

		[Test]
		public void TheDefensiveRadiusIsConservative()
		{
			// The brief was explicit that a technician should take what is near it and not go hunting.
			// A default radius that crept up to map-crossing size would meet every other assertion here
			// while breaking the actual requirement, so the ceiling is pinned.
			Assert.That(AutoCaptureMath.RadiusCellsForStance(EngagementStance.Defensive, 8, 20),
				Is.LessThanOrEqualTo(10),
				"the default-stance radius is no longer conservative. The requirement was that an " +
				"untouched technician does not venture far to find structures.");
		}

		[Test]
		public void TheNearestStructureWinsWhenNothingIsClose()
		{
			// Distances far apart relative to the 3-cell band: value must not override plain proximity.
			var candidates = new List<AutoCaptureMath.Candidate>
			{
				At(10, 5000, 1),   // far but lucrative
				At(2, 0, 2),       // near and worthless
			};

			Assert.That(AutoCaptureMath.SelectBest(candidates, 3 * Cell), Is.EqualTo(1),
				"a structure 8 cells further away won on value alone. Value is a TIE-BREAK for " +
				"near-equal distances, not a term that can outweigh distance outright — otherwise a " +
				"technician walks past a derrick at its feet to reach a better one across the map.");
		}

		[Test]
		public void ValueBreaksTheTieWhenDistancesAreNegligible()
		{
			// Both inside the 3-cell band of the nearest, so they count as equally near and the more
			// valuable wins. This is the "or highest value, if the distance is negligible" half.
			var candidates = new List<AutoCaptureMath.Candidate>
			{
				At(5, 25, 1),
				At(7, 50, 2),
			};

			Assert.That(AutoCaptureMath.SelectBest(candidates, 3 * Cell), Is.EqualTo(1),
				"two structures within the tie band did not resolve on value.");
		}

		[Test]
		public void TheTieBandIsMeasuredFromTheNearestNotPairwise()
		{
			// 0, 2, 4 cells with a 3-cell band. Pairwise chaining would make all three one group and
			// hand it to the most valuable, at 4 cells. Measuring from the nearest admits only 0 and 2,
			// so the 4-cell one cannot win however rich it is.
			var candidates = new List<AutoCaptureMath.Candidate>
			{
				At(0, 10, 1),
				At(2, 20, 2),
				At(4, 9000, 3),
			};

			Assert.That(AutoCaptureMath.SelectBest(candidates, 3 * Cell), Is.EqualTo(1),
				"the tie band chained: a structure outside the band of the NEAREST candidate won " +
				"because it was within a band of something else. Closeness is not transitive, and " +
				"chaining drags the whole map into one tie group — which turns nearest-with-tiebreak " +
				"into pure highest-value.");
		}

		[Test]
		public void AZeroBandIsPureNearestFirst()
		{
			var candidates = new List<AutoCaptureMath.Candidate>
			{
				At(5, 1, 1),
				At(6, 9999, 2),
			};

			Assert.That(AutoCaptureMath.SelectBest(candidates, 0), Is.EqualTo(0),
				"a zero tie band must degrade to plain nearest-first; it is a legitimate configuration " +
				"and is why the band is a field rather than a constant.");
		}

		[Test]
		public void EqualCandidatesResolveByActorIdSoEveryClientAgrees()
		{
			// There is no RNG anywhere in this path and there must not be: the trait ships enabled, so
			// drawing from the synced random stream would shift it for control games too. ActorID is
			// the only total tie-break left, and it has to actually be applied or two clients can pick
			// different structures and desync.
			var candidates = new List<AutoCaptureMath.Candidate>
			{
				At(5, 50, 77),
				At(5, 50, 12),
			};

			Assert.That(AutoCaptureMath.SelectBest(candidates, 3 * Cell), Is.EqualTo(1),
				"two identical candidates did not resolve to the lower ActorID.");
		}

		[Test]
		public void SelectionIsIndependentOfTheOrderCandidatesArriveIn()
		{
			// FindActorsInCircle's ordering is not something this code should depend on. Reversing the
			// input must not change the answer, or the pick becomes a function of spatial-index
			// iteration order and stops being reproducible.
			var forward = new List<AutoCaptureMath.Candidate> { At(5, 25, 1), At(7, 50, 2), At(20, 9000, 3) };
			var backward = new List<AutoCaptureMath.Candidate> { At(20, 9000, 3), At(7, 50, 2), At(5, 25, 1) };

			var a = forward[AutoCaptureMath.SelectBest(forward, 3 * Cell)];
			var b = backward[AutoCaptureMath.SelectBest(backward, 3 * Cell)];

			Assert.That(a.ActorId, Is.EqualTo(b.ActorId),
				"reversing the candidate list changed the pick, so the selection depends on iteration " +
				"order rather than on a total ordering.");
		}

		[Test]
		public void AnEmptyScanSelectsNothing()
		{
			Assert.That(AutoCaptureMath.SelectBest(Array.Empty<AutoCaptureMath.Candidate>(), 3 * Cell),
				Is.EqualTo(AutoCaptureMath.NoTarget));
		}

		[Test]
		public void ARadiusAdmitsWhatIsInsideItAndRejectsWhatIsOutside()
		{
			Assert.Multiple(() =>
			{
				Assert.That(AutoCaptureMath.WithinRadius(8 * Cell, 8), Is.True, "the boundary is inclusive");
				Assert.That(AutoCaptureMath.WithinRadius(8 * Cell + 1, 8), Is.False);
				Assert.That(AutoCaptureMath.WithinRadius(Cell, -1), Is.False,
					"a negative radius must admit nothing rather than wrapping into a huge one");
			});
		}
	}
}
