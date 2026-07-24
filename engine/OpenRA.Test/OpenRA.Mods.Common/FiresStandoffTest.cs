#region Copyright & License Information
/*
 * WW3MOD fires doctrine (PIPELINE item 11) — artillery standoff geometry test.
 *
 * Pins the decisions PoiOffensiveBotModule.OrderFiresStandoff turns into per-piece AttackMove
 * orders, so the "hold at range, rain fire, back off if the target closes" behaviour can't
 * silently regress:
 *   (1) STANDOFF RADIUS — max weapon range pulled in by the margin, floored for a degenerate piece.
 *   (2) ANCHOR PLACEMENT — the standoff point sits at that radius from the target on the bearing back
 *       toward the piece (so it backs straight off), pulling a too-far piece IN and a too-close piece OUT.
 *   (3) BAND / REPOSITION — inside [inner, maxRange] the piece holds and keeps firing; outside it must
 *       reposition (too far = advance to fire, too close = retreat), with hysteresis on the near edge.
 *   (4) DETERMINISM — identical synced inputs give the identical anchor + decision (no random draws).
 * Pure math over synthetic positions; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FiresStandoffTest
	{
		const int Cell = 1024;

		static int Cells(int n) => n * Cell;

		static WPos Pos(int xCells, int yCells) => new(Cells(xCells), Cells(yCells), 0);

		// Standoff params reused across the band/anchor pins: maxRange 10c, margin 2c, hysteresis 2c,
		// floor 3c → standoff radius 8c, inner band edge 6c.
		const int MaxRange = 10 * Cell;
		const int Margin = 2 * Cell;
		const int Hysteresis = 2 * Cell;
		const int Floor = 3 * Cell;

		[Test]
		public void StandoffRadius_IsMaxRangeMinusMargin_FlooredForDegenerateRange()
		{
			Assert.That(FiresStandoffMath.StandoffRadius(MaxRange, Margin, Floor), Is.EqualTo(8 * Cell),
				"radius should be max range minus the margin");

			// A piece whose range is at/under the margin would anchor on top of the target — the floor guards it.
			Assert.That(FiresStandoffMath.StandoffRadius(Cells(1), Margin, Floor), Is.EqualTo(3 * Cell),
				"a sub-margin range must clamp to the floor, not go to/below zero");
		}

		[Test]
		public void Anchor_SitsAtStandoffRadius_OnTheTargetToUnitBearing()
		{
			var target = Pos(0, 0);
			var unit = Pos(12, 0); // due east, 12c out (beyond the 8c standoff)

			var anchor = FiresStandoffMath.StandoffAnchor(target, unit, MaxRange, Margin, Floor);

			// On the same (east) bearing: y stays on the axis, x is the positive standoff radius.
			Assert.That(anchor.Y, Is.EqualTo(0), "anchor must stay on the target→unit bearing");
			Assert.That((anchor - target).HorizontalLength, Is.EqualTo(8 * Cell),
				"anchor must sit exactly at the standoff radius from the target");
			Assert.That(anchor.X, Is.LessThan(unit.X), "a too-far piece's anchor is nearer the target (advance to fire)");
		}

		[Test]
		public void Anchor_PushesATooClosePieceOutward()
		{
			var target = Pos(0, 0);
			var unit = Pos(4, 0); // 4c out — inside the 8c standoff (danger has closed in)

			var anchor = FiresStandoffMath.StandoffAnchor(target, unit, MaxRange, Margin, Floor);

			Assert.That(anchor.X, Is.GreaterThan(unit.X), "a too-close piece's anchor is farther from the target (retreat a leg)");
			Assert.That((anchor - target).HorizontalLength, Is.EqualTo(8 * Cell));
		}

		[Test]
		public void Anchor_DegenerateUnitOnTarget_IsDeterministicNorthOffset()
		{
			var target = Pos(5, 5);

			var anchor = FiresStandoffMath.StandoffAnchor(target, target, MaxRange, Margin, Floor);

			// Zero bearing → fixed north (−Y) offset at the standoff radius, so two clients still agree.
			Assert.That(anchor, Is.EqualTo(new WPos(target.X, target.Y - 8 * Cell, 0)));
		}

		[Test]
		public void Anchor_PreservesDiagonalBearing()
		{
			var target = Pos(0, 0);
			var unit = Pos(20, 20); // north-east diagonal, well beyond standoff

			var anchor = FiresStandoffMath.StandoffAnchor(target, unit, MaxRange, Margin, Floor);

			// Still on the diagonal (x == y, both positive) and at the standoff radius within integer rounding.
			Assert.That(anchor.X, Is.EqualTo(anchor.Y), "diagonal bearing must be preserved");
			Assert.That(anchor.X, Is.GreaterThan(0));
			Assert.That((anchor - target).HorizontalLength, Is.EqualTo(8 * Cell).Within(2),
				"anchor sits at the standoff radius (±integer rounding)");
		}

		[Test]
		public void NeedsReposition_FalseInsideBand_TrueOutside()
		{
			var target = Pos(0, 0);

			// In band: 7c out — between inner (6c) and maxRange (10c). Hold and keep firing.
			Assert.That(FiresStandoffMath.NeedsReposition(target, Pos(7, 0), MaxRange, Margin, Hysteresis, Floor),
				Is.False, "a piece within its standoff band holds and fires");

			// Too far: 11c out — beyond max weapon range. Advance to get into range.
			Assert.That(FiresStandoffMath.NeedsReposition(target, Pos(11, 0), MaxRange, Margin, Hysteresis, Floor),
				Is.True, "beyond max range the piece must close up to fire");

			// Too close: 5c out — inside the inner band edge (6c). Back off.
			Assert.That(FiresStandoffMath.NeedsReposition(target, Pos(5, 0), MaxRange, Margin, Hysteresis, Floor),
				Is.True, "inside the inner band the piece must retreat");
		}

		[Test]
		public void NeedsReposition_HysteresisHoldsTheNearEdge()
		{
			var target = Pos(0, 0);

			// Exactly on the inner edge (radius 8c − hysteresis 2c = 6c) is still in-band (hold).
			Assert.That(FiresStandoffMath.NeedsReposition(target, new WPos(6 * Cell, 0, 0), MaxRange, Margin, Hysteresis, Floor),
				Is.False, "the inner edge itself is in-band — hysteresis stops edge chatter");

			// One unit inside the edge trips the retreat.
			Assert.That(FiresStandoffMath.NeedsReposition(target, new WPos(6 * Cell - 1, 0, 0), MaxRange, Margin, Hysteresis, Floor),
				Is.True, "just inside the inner edge the piece repositions");
		}

		[Test]
		public void Deterministic_SameInputsGiveSameResult()
		{
			var target = Pos(3, -7);
			var unit = Pos(17, 4);

			var a1 = FiresStandoffMath.StandoffAnchor(target, unit, MaxRange, Margin, Floor);
			var a2 = FiresStandoffMath.StandoffAnchor(target, unit, MaxRange, Margin, Floor);
			Assert.That(a1, Is.EqualTo(a2), "anchor is a pure function of its inputs");

			var r1 = FiresStandoffMath.NeedsReposition(target, unit, MaxRange, Margin, Hysteresis, Floor);
			var r2 = FiresStandoffMath.NeedsReposition(target, unit, MaxRange, Margin, Hysteresis, Floor);
			Assert.That(r1, Is.EqualTo(r2), "reposition decision is a pure function of its inputs");
		}
	}
}
