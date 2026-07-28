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
using System.Linq;
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

		// ---------- Conflict-resolution driver (ResolveConcealmentSlots) ----------
		//
		// These pin the `taken`-set seeding invariant the DISCOVERIES entry calls the subtle part:
		// re-seating one unit must never land it on another unit's cell, own-cell freeing removes
		// exactly one occurrence (so upstream duplicates survive), and prior picks block later units.
		// The map/mobile-dependent chooser is replaced with a pure fake so the driver is exercised in
		// isolation. `Stay` keeps every unit put; `Preferring` moves a unit to a desired cell only
		// when the driver has not already reserved it — exactly the contract PickConcealedCellNear honours.

		static CPos Stay(CPos assigned, IReadOnlyList<CPos> taken) => assigned;

		static System.Func<CPos, IReadOnlyList<CPos>, CPos> Preferring(IDictionary<CPos, CPos> desired)
		{
			return (assigned, taken) =>
			{
				if (!desired.TryGetValue(assigned, out var want))
					return assigned;

				// The chooser must respect the driver's exclusion set (never return a reserved cell).
				return taken.Any(t => t == want) ? assigned : want;
			};
		}

		[Test]
		public void DistinctSlotsNeverCollideAfterRefinement()
		{
			// Three units all want the same hot cover cell. The driver reserves it for whoever reaches
			// it first (slot order); the others are blocked and keep their positions. No two share a cell.
			var slots = new[] { new CPos(0, 0), new CPos(0, 2), new CPos(2, 0) };
			var hot = new CPos(1, 1);
			var refined = CohesionMoveModifier.ResolveConcealmentSlots(slots,
				Preferring(new Dictionary<CPos, CPos> { { slots[0], hot }, { slots[1], hot }, { slots[2], hot } }));

			Assert.That(refined[0], Is.EqualTo(hot));         // first claimant wins
			Assert.That(refined[1], Is.EqualTo(new CPos(0, 2)));
			Assert.That(refined[2], Is.EqualTo(new CPos(2, 0)));
			Assert.That(refined.Distinct().Count(), Is.EqualTo(3), "refined slots must stay pairwise distinct");
		}

		[Test]
		public void EarlyPickCannotTakeALaterUnitsOriginalCell()
		{
			// The reviewer's explicit case: unit 0 would prefer to sit on unit 1's ORIGINAL cell.
			// Because `taken` is seeded with every original slot, that cell is reserved during unit 0's
			// turn, so unit 0 is blocked and stays — the squad never double-occupies unit 1's spot.
			var slots = new[] { new CPos(0, 0), new CPos(2, 2) };
			var refined = CohesionMoveModifier.ResolveConcealmentSlots(slots,
				Preferring(new Dictionary<CPos, CPos> { { slots[0], new CPos(2, 2) } }));

			Assert.That(refined[0], Is.EqualTo(new CPos(0, 0)));   // blocked from (2,2)
			Assert.That(refined[1], Is.EqualTo(new CPos(2, 2)));
			Assert.That(refined[0], Is.Not.EqualTo(refined[1]));
		}

		[Test]
		public void AVacatedOriginalCellBecomesReusableByALaterUnit()
		{
			// Freeing is scoped but real: once unit 0 moves off its original cell, that cell is no
			// longer reserved, so a later unit may legitimately take it — no permanent phantom block.
			var slots = new[] { new CPos(0, 0), new CPos(1, 1) };
			var free = new CPos(5, 5);
			var refined = CohesionMoveModifier.ResolveConcealmentSlots(slots,
				Preferring(new Dictionary<CPos, CPos> { { slots[0], free }, { slots[1], new CPos(0, 0) } }));

			Assert.That(refined[0], Is.EqualTo(free));            // unit 0 moved away
			Assert.That(refined[1], Is.EqualTo(new CPos(0, 0)));  // unit 1 took the vacated cell
			Assert.That(refined.Distinct().Count(), Is.EqualTo(2));
		}

		[Test]
		public void SeedsEveryOriginalSlotThenReAddsPriorPicks()
		{
			// Capture the exclusion set handed to each choose call (all units stay). The set must be
			// every OTHER original slot on the first pass, with each processed slot's pick re-added for
			// the units after it — the exact seed/free/re-occupy sequence the invariant depends on.
			var slots = new[] { new CPos(0, 0), new CPos(1, 0), new CPos(2, 0) };
			var seen = new List<CPos[]>();

			CohesionMoveModifier.ResolveConcealmentSlots(slots, (assigned, taken) =>
			{
				seen.Add(taken.ToArray());
				return assigned;
			});

			// Order within each snapshot is an implementation detail; assert set membership.
			Assert.That(seen[0], Is.EquivalentTo(new[] { slots[1], slots[2] }));           // A's turn: {B,C}
			Assert.That(seen[1], Is.EquivalentTo(new[] { slots[2], slots[0] }));           // B's turn: {C, A(re-added)}
			Assert.That(seen[2], Is.EquivalentTo(new[] { slots[0], slots[1] }));           // C's turn: {A, B}
		}

		[Test]
		public void OwnCellFreeingRemovesExactlyOneOccurrenceSoDuplicatesSurvive()
		{
			// Upstream formation branches can emit a duplicate cell (e.g. click-cell padding). Freeing
			// the current slot's own cell must remove EXACTLY ONE occurrence, so the duplicate held by
			// the sibling slot stays reserved — the pass never multiplies or silently drops it.
			var d = new CPos(1, 1);
			var slots = new[] { d, d };
			var seen = new List<CPos[]>();

			var refined = CohesionMoveModifier.ResolveConcealmentSlots(slots, (assigned, taken) =>
			{
				seen.Add(taken.ToArray());
				return assigned;
			});

			// On slot 0's turn, the sibling duplicate is still present (one removed, one remains).
			Assert.That(seen[0], Is.EqualTo(new[] { d }));
			// The duplicate is preserved, not multiplied: exactly the two cells we started with.
			Assert.That(refined, Is.EqualTo(new[] { d, d }));
		}
	}
}
