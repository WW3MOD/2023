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

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	using Candidate = CohesionMoveModifier.ConcealmentCandidate;

	/// <summary>
	/// Pins the pure ranking core behind PIPELINE item 21 (stance-aware cover positioning) —
	/// CohesionMoveModifier.PickBestConcealmentOffset. The refinement trait is coupled to
	/// Actor/Map/Mobile and cannot be unit-tested in isolation, so — following the project idiom
	/// (DensityModifiesDamage.SelectModifier, CohesionStanceMathTest) — the reviewable arithmetic is
	/// a pure static and pinned here. The concealment SCORE itself is Map.ForestGroundShadow over a
	/// windowed density sum, pinned separately in ForestShadowTest; these tests feed concealment
	/// values directly and assert the SELECTION properties the design relies on: keep-assigned bias,
	/// never trade concealment away, distance-penalised net win, and a deterministic tie-break.
	/// </summary>
	[TestFixture]
	public class StanceCoverPositioningTest
	{
		static (int Dx, int Dy) Pick(int assigned, IReadOnlyList<Candidate> candidates, int margin = 1, int penalty = 2)
		{
			return CohesionMoveModifier.PickBestConcealmentOffset(assigned, candidates, margin, penalty);
		}

		[Test]
		public void OpenTerrainKeepsAssignedCell()
		{
			// No candidate cells at all (a squad ordered to open ground has nothing to move onto):
			// the unit stays exactly where the formation placed it — byte-identical to today.
			Assert.That(Pick(0, new List<Candidate>()), Is.EqualTo((0, 0)));
		}

		[Test]
		public void AllCandidatesEqualOrWorseKeepsAssignedCell()
		{
			// "Never trade concealment away": an ambush unit already on the best local cover cell is
			// not moved to an equal or thinner-cover cell just because one exists nearby.
			var candidates = new List<Candidate>
			{
				new Candidate(1, 0, 5),   // equal to assigned
				new Candidate(0, 1, 3),   // worse
				new Candidate(-1, -1, 0), // open
			};

			Assert.That(Pick(5, candidates), Is.EqualTo((0, 0)));
		}

		[Test]
		public void MovesToAStrictlyBetterConcealedCell()
		{
			// The headline behaviour: a genuinely better hide one cell away wins. assigned=0,
			// candidate concealment 4 at cheb 1 with penalty 2 → effective 2 > 0 → move there.
			var candidates = new List<Candidate> { new Candidate(1, 0, 4) };
			Assert.That(Pick(0, candidates), Is.EqualTo((1, 0)));
		}

		[Test]
		public void MarginSuppressesNegligibleGains()
		{
			// A small concealment gain does not justify uprooting the unit when the margin asks for
			// more. Isolate the margin gate from the distance gate with penalty 0: with margin 3, a
			// +2 concealment candidate is ignored → keep assigned.
			var candidates = new List<Candidate> { new Candidate(1, 0, 7) };
			Assert.That(Pick(5, candidates, margin: 3, penalty: 0), Is.EqualTo((0, 0)));

			// The same candidate clears a margin of 2 (gain 2 >= 2) and is taken.
			Assert.That(Pick(5, candidates, margin: 2, penalty: 0), Is.EqualTo((1, 0)));
		}

		[Test]
		public void DistancePenaltyKeepsUnitsNearTheOrderedSpot()
		{
			// A much richer cell far away must not drag the unit off the ordered spot. assigned=3,
			// candidate concealment 6 at cheb 3 with penalty 2 → effective 6-6=0, not > 3 → stay.
			// This is the conservative "must not wander to distant cover" constraint in action.
			var candidates = new List<Candidate> { new Candidate(3, 0, 6) };
			Assert.That(Pick(3, candidates), Is.EqualTo((0, 0)));

			// Same concealment one cell closer clears the bar: cheb 1 → effective 6-2=4 > 3 → move.
			var closer = new List<Candidate> { new Candidate(1, 0, 6) };
			Assert.That(Pick(3, closer), Is.EqualTo((1, 0)));
		}

		[Test]
		public void NetZeroMoveIsRejected()
		{
			// A candidate whose concealment gain is exactly cancelled by the distance cost gains the
			// unit nothing, so the keep-assigned bias holds: assigned=2, concealment 4 at cheb 1,
			// penalty 2 → effective 2, not strictly > 2 → stay put.
			var candidates = new List<Candidate> { new Candidate(1, 0, 4) };
			Assert.That(Pick(2, candidates), Is.EqualTo((0, 0)));
		}

		[Test]
		public void HigherConcealmentWinsOnEffectiveTie()
		{
			// Two reachable hides with equal net (effective) score: the deeper cover wins. assigned=0.
			// A: cheb 1, concealment 4 → effective 2.  B: cheb 2, concealment 6 → effective 2.
			// Tie on effective, so raw concealment breaks it → B (the deeper hide).
			var candidates = new List<Candidate>
			{
				new Candidate(1, 0, 4),
				new Candidate(2, 0, 6),
			};

			Assert.That(Pick(0, candidates), Is.EqualTo((2, 0)));
		}

		[Test]
		public void TieBreaksDeterministicallyByPositionWhenPenaltyIsZero()
		{
			// With penalty 0, effective == concealment, so equal-cover cells at different offsets tie
			// all the way to the (Dy, Dx)-ascending final key. Order of the input list must not matter.
			var candidates = new List<Candidate>
			{
				new Candidate(1, 0, 5),   // Dy 0, Dx 1
				new Candidate(0, 1, 5),   // Dy 1
				new Candidate(-1, 0, 5),  // Dy 0, Dx -1  ← smallest (Dy,Dx)
			};

			Assert.That(Pick(0, candidates, margin: 1, penalty: 0), Is.EqualTo((-1, 0)));

			// Reversed input yields the identical winner — the tie-break is a pure total order.
			candidates.Reverse();
			Assert.That(Pick(0, candidates, margin: 1, penalty: 0), Is.EqualTo((-1, 0)));
		}

		[Test]
		public void CloserCellWinsAnEffectiveAndConcealmentTieWhenPenaltyIsZero()
		{
			// penalty 0 → two equal-concealment cells tie on effective and concealment; the chebyshev
			// tie-break then prefers the closer cell before the positional key is consulted.
			var candidates = new List<Candidate>
			{
				new Candidate(2, 0, 5),   // cheb 2
				new Candidate(1, 0, 5),   // cheb 1 ← closer wins
			};

			Assert.That(Pick(0, candidates, margin: 1, penalty: 0), Is.EqualTo((1, 0)));
		}
	}
}
