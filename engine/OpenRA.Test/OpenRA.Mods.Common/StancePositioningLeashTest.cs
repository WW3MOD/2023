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
	/// Pins the pure Manhattan leash predicate that the residual-B1 walk-back fix relies on
	/// (WORKSPACE/plans/260729_b1_walkback_brief.md). The executor's ITick uses it two ways: the BARE
	/// leash (radius = LeashRadius) for a settled anchor, and the MARGIN band (radius = LeashRadius +
	/// AdjustLeashMargin) to decide, WHILE Adjusting, whether a strayed unit is on our own leashed
	/// adjustment move or has been externally redirected. The world-touching parts (issuing the Move,
	/// clearing the slot, the ledger claim) cannot be unit-tested in isolation; the geometry can, so —
	/// following the project idiom (StanceCoverPositioningTest, CohesionStanceMathTest) — the predicate
	/// is a pure static (StancePositioningExecutor.WithinManhattan) pinned here.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/StancePositioningExecutor.cs
	/// </summary>
	[TestFixture]
	public class StancePositioningLeashTest
	{
		static bool Within(int dx, int dy, int radius)
		{
			// anchor fixed at a non-origin cell so any accidental anchor-drop in the predicate surfaces.
			var anchor = new CPos(20, 30);
			return StancePositioningExecutor.WithinManhattan(new CPos(anchor.X + dx, anchor.Y + dy), anchor, radius);
		}

		[Test]
		public void AnchorCellIsAlwaysWithin()
		{
			Assert.That(Within(0, 0, 0), Is.True);
			Assert.That(Within(0, 0, 4), Is.True);
		}

		[Test]
		public void UsesManhattanNotChebyshevOrEuclidean()
		{
			// (3,3) is Chebyshev 3 and Euclidean ~4.24 but Manhattan 6: it must be OUTSIDE a radius-4
			// disk. This is the property ChooseTarget's leash loop (|dx|+|dy| <= lr) depends on.
			Assert.That(Within(3, 3, 4), Is.False, "(3,3) is Manhattan 6 — outside a radius-4 leash");
			Assert.That(Within(4, 0, 4), Is.True, "(4,0) is Manhattan 4 — on the leash boundary");
			Assert.That(Within(2, 2, 4), Is.True, "(2,2) is Manhattan 4 — on the leash boundary");
		}

		[Test]
		public void BoundaryIsInclusive()
		{
			Assert.That(Within(4, 0, 4), Is.True, "exactly on the radius is within");
			Assert.That(Within(5, 0, 4), Is.False, "one past the radius is outside");
		}

		[Test]
		public void SignsAreSymmetric()
		{
			// Manhattan is sign-agnostic: the four axis boundary cells behave identically, so the abort
			// band cannot be biased toward one map direction.
			Assert.That(Within(4, 0, 4), Is.True);
			Assert.That(Within(-4, 0, 4), Is.True);
			Assert.That(Within(0, 4, 4), Is.True);
			Assert.That(Within(0, -4, 4), Is.True);
			Assert.That(Within(5, 0, 4), Is.False);
			Assert.That(Within(-5, 0, 4), Is.False);
			Assert.That(Within(0, 5, 4), Is.False);
			Assert.That(Within(0, -5, 4), Is.False);
		}

		// ── The residual-B1 abort band: LeashRadius(4) + AdjustLeashMargin(2) = Manhattan 6 ──

		[Test]
		public void MarginBandAdmitsBoundedPathingExcursionsBeyondTheLeash()
		{
			// The whole point of the margin: a unit on its OWN adjustment move (dest is WithinLeash=4)
			// may detour up to the margin outside the leash while routing around obstacles. Cells at
			// Manhattan 5 and 6 are inside the band, so such an excursion must NOT abort.
			const int band = 4 + 2;
			Assert.That(Within(5, 0, band), Is.True, "Manhattan 5 — a 1-cell detour past the leash, still ours");
			Assert.That(Within(3, 3, band), Is.True, "Manhattan 6 — a 2-cell detour past the leash, still ours");
			Assert.That(Within(6, 0, band), Is.True, "Manhattan 6 on-axis — the band boundary, inclusive");
		}

		[Test]
		public void MarginBandCatchesAPlayerRedirectBeyondTheBand()
		{
			// A player redirect drives the unit toward a far cell; once it is past the band (Manhattan
			// > 6) it cannot be on our own leashed move, so ITick aborts and clears the slot before the
			// unit idles. Manhattan 7 is the first caught ring; the recorded 5–14-cell redirect window
			// crosses this ring during the move.
			const int band = 4 + 2;
			Assert.That(Within(7, 0, band), Is.False, "Manhattan 7 — past the band, an external redirect");
			Assert.That(Within(4, 4, band), Is.False, "Manhattan 8 — clearly a redirect");
			Assert.That(Within(0, 14, band), Is.False, "far end of the recorded redirect window");
		}

		[Test]
		public void MarginBandIsStrictlyWiderThanTheBareLeash()
		{
			// The margin must genuinely widen the abort threshold, else the Adjusting guard collapses
			// back to the bare-leash behaviour that false-aborts every pathing detour. A cell just past
			// the bare leash (Manhattan 5) is OUT of the bare leash but IN the band.
			Assert.That(Within(5, 0, 4), Is.False, "out of the bare leash");
			Assert.That(Within(5, 0, 4 + 2), Is.True, "in of the margin band");
		}
	}
}
