#region Copyright & License Information
/*
 * WW3MOD defence-in-depth echelon (builds on PIPELINE item 11) — line-order geometry test.
 *
 * Pins the decisions PoiOffensiveBotModule.OrderFiresStandoff turns into per-piece AttackMove orders when
 * EchelonPositioning is on, so "artillery holds behind the screen by its range surplus" can't silently regress:
 *   (1) ECHELON DEPTH — range surplus over the screen plus the buffer, floored, surplus clamped at 0.
 *   (2) ANCHOR PLACEMENT — the anchor sits that depth behind the screen centroid, offset AWAY from the target,
 *       so the piece is always on the friendly side of the line (farther from the enemy than the screen).
 *   (3) HOLD BAND — inside the tolerance the piece holds and keeps firing; outside it repositions.
 *   (4) DETERMINISM — identical synced inputs give the identical anchor + decision (no random draws).
 * Pure math over synthetic positions; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class EchelonMathTest
	{
		const int Cell = 1024;

		static int Cells(int n) => n * Cell;

		static WPos Pos(int xCells, int yCells) => new(Cells(xCells), Cells(yCells), 0);

		// Echelon params reused across the pins: buffer 1c, minDepth 3c.
		const int Buffer = 1 * Cell;
		const int MinDepth = 3 * Cell;

		[Test]
		public void Depth_IsRangeSurplusPlusBuffer_FlooredAtMinDepth()
		{
			// Artillery (20c) outranges a 5c tank engagement by 15c → holds 15c + 1c buffer = 16c behind.
			Assert.That(EchelonMath.EchelonDepth(Cells(20), Cells(5), Buffer, MinDepth), Is.EqualTo(16 * Cell),
				"depth = (own range - screen range) + buffer");

			// A piece that does NOT outrange the screen still sits at least the floor back (surplus clamps at 0,
			// buffer alone would be below the floor).
			Assert.That(EchelonMath.EchelonDepth(Cells(4), Cells(10), Buffer, MinDepth), Is.EqualTo(3 * Cell),
				"a piece not outranging the screen holds at the min-depth floor");

			// Equal ranges: surplus 0, depth = max(minDepth, buffer) = minDepth here.
			Assert.That(EchelonMath.EchelonDepth(Cells(8), Cells(8), Buffer, MinDepth), Is.EqualTo(3 * Cell),
				"equal-range piece falls to the floor");
		}

		[Test]
		public void Anchor_SitsBehindTheScreen_AwayFromTheTarget()
		{
			var target = Pos(0, 0);
			var screen = Pos(5, 0); // screen 5c east of the target (toward the enemy)

			var anchor = EchelonMath.EchelonAnchor(screen, target, 16 * Cell);

			// On the same (east) bearing, offset further east — farther from the target than the screen.
			Assert.That(anchor.Y, Is.EqualTo(0), "anchor stays on the target→screen bearing");
			Assert.That(anchor.X, Is.GreaterThan(screen.X), "anchor is behind the screen, away from the target");
			Assert.That((anchor - screen).HorizontalLength, Is.EqualTo(16 * Cell),
				"anchor sits exactly the echelon depth behind the screen line");
			Assert.That((anchor - target).HorizontalLength, Is.GreaterThan((screen - target).HorizontalLength),
				"the piece ends up farther from the enemy than its screen (friendly side of the line)");
		}

		[Test]
		public void Anchor_DegenerateScreenOnTarget_IsDeterministicNorthOffset()
		{
			var target = Pos(7, 7);

			var anchor = EchelonMath.EchelonAnchor(target, target, 9 * Cell);

			// Zero bearing → fixed north (−Y) offset at the depth, so two clients still agree.
			Assert.That(anchor, Is.EqualTo(new WPos(target.X, target.Y - 9 * Cell, 0)));
		}

		[Test]
		public void Anchor_PreservesDiagonalBearing()
		{
			var target = Pos(0, 0);
			var screen = Pos(6, 6); // north-east diagonal

			var anchor = EchelonMath.EchelonAnchor(screen, target, 10 * Cell);

			// Still on the diagonal (x == y, both beyond the screen) and the depth behind it (±integer rounding).
			Assert.That(anchor.X, Is.EqualTo(anchor.Y), "diagonal bearing preserved");
			Assert.That(anchor.X, Is.GreaterThan(screen.X));
			Assert.That((anchor - screen).HorizontalLength, Is.EqualTo(10 * Cell).Within(2),
				"anchor sits the depth behind the screen (±integer rounding)");
		}

		[Test]
		public void NeedsReposition_FalseInsideTolerance_TrueOutside()
		{
			var anchor = Pos(10, 10);
			var tolerance = 2 * Cell;

			// 1c off the anchor — inside the 2c tolerance. Hold and keep firing.
			Assert.That(EchelonMath.NeedsReposition(anchor, Pos(11, 10), tolerance),
				Is.False, "a piece within tolerance of its anchor holds");

			// 3c off — beyond tolerance. Reposition.
			Assert.That(EchelonMath.NeedsReposition(anchor, Pos(13, 10), tolerance),
				Is.True, "beyond tolerance the piece repositions to its echelon anchor");
		}

		[Test]
		public void Deterministic_SameInputsGiveSameResult()
		{
			var screen = Pos(4, -6);
			var target = Pos(-3, 9);

			var d1 = EchelonMath.EchelonDepth(Cells(22), Cells(7), Buffer, MinDepth);
			var d2 = EchelonMath.EchelonDepth(Cells(22), Cells(7), Buffer, MinDepth);
			Assert.That(d1, Is.EqualTo(d2), "depth is a pure function of its inputs");

			var a1 = EchelonMath.EchelonAnchor(screen, target, d1);
			var a2 = EchelonMath.EchelonAnchor(screen, target, d1);
			Assert.That(a1, Is.EqualTo(a2), "anchor is a pure function of its inputs");
		}
	}
}
