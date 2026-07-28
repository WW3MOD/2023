#region Copyright & License Information
/*
 * WW3MOD path string-pulling math test — pipeline item 28, recon §Q7(b).
 *
 * Pins the pure geometry the Move activity relies on to corner-cut the RENDERED line while leaving the
 * A* cell path, reservations and per-cell pop cadence untouched: floor-div cell derivation, the integer
 * line-of-walk DDA (incl. the diagonal corner-squeeze guard), farthest-visible-waypoint selection, and
 * the integer sightline projection with its bounded visual-vs-reserved divergence.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Activities;

namespace OpenRA.Test
{
	[TestFixture]
	public class PathStringPullingTest
	{
		static Func<CPos, WPos> Center => PathStringPulling.CellCenter;

		static Func<CPos, bool> AllOpen => _ => true;

		static Func<CPos, bool> Blocking(params CPos[] blocked)
		{
			var set = new HashSet<CPos>(blocked);
			return c => !set.Contains(c);
		}

		static CPos C(int x, int y) => new(x, y);

		[Test]
		public void FloorDivAndCellRoundTrip()
		{
			Assert.Multiple(() =>
			{
				Assert.That(PathStringPulling.FloorDiv(2047, 1024), Is.EqualTo(1));
				Assert.That(PathStringPulling.FloorDiv(1024, 1024), Is.EqualTo(1));
				Assert.That(PathStringPulling.FloorDiv(-1, 1024), Is.EqualTo(-1), "floor, not truncate-toward-zero");
				Assert.That(PathStringPulling.FloorDiv(-1024, 1024), Is.EqualTo(-1));

				// CellCenter matches Map.CenterOfCell for Rectangular; CellContaining is its inverse.
				Assert.That(PathStringPulling.CellCenter(C(3, 2)), Is.EqualTo(new WPos(3584, 2560, 0)));
				Assert.That(PathStringPulling.CellContaining(new WPos(3584, 2560, 0)), Is.EqualTo(C(3, 2)));
				Assert.That(PathStringPulling.CellContaining(new WPos(-1, -1, 0)), Is.EqualTo(C(-1, -1)));
			});
		}

		[Test]
		public void LineOfWalkStraightCorridor()
		{
			// Horizontal run crosses (0,0),(1,0),(2,0).
			var from = Center(C(0, 0));
			var to = Center(C(2, 0));
			Assert.Multiple(() =>
			{
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, AllOpen), Is.True, "open corridor clears");
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, Blocking(C(1, 0))), Is.False, "mid cell blocks");
				Assert.That(PathStringPulling.LineOfWalkClear(from, from, Blocking(C(5, 5))), Is.True, "same cell, unrelated block");
				Assert.That(PathStringPulling.LineOfWalkClear(from, from, Blocking(C(0, 0))), Is.False, "start cell itself blocked");
			});
		}

		[Test]
		public void LineOfWalkCrossesEveryTouchedCell()
		{
			// Shallow diagonal center(0,0) -> center(2,1) touches (0,0),(1,0),(1,1),(2,1).
			var from = Center(C(0, 0));
			var to = Center(C(2, 1));
			Assert.Multiple(() =>
			{
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, AllOpen), Is.True);
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, Blocking(C(1, 1))), Is.False, "orthogonal step cell counts");
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, Blocking(C(2, 0))), Is.True, "untouched cell is irrelevant");
			});
		}

		[Test]
		public void LineOfWalkForbidsDiagonalCornerSqueeze()
		{
			// center(0,0) -> center(1,1) passes exactly through the (1024,1024) lattice corner.
			var from = Center(C(0, 0));
			var to = Center(C(1, 1));
			Assert.Multiple(() =>
			{
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, AllOpen), Is.True, "open corner is walkable");
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, Blocking(C(1, 0), C(0, 1))), Is.False,
					"cannot squeeze between two blocked cells sharing the corner");
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, Blocking(C(1, 0))), Is.False,
					"a single blocked corner neighbour still forbids the diagonal clip");
				Assert.That(PathStringPulling.LineOfWalkClear(from, to, Blocking(C(0, 1))), Is.False,
					"guard is symmetric in the two corner neighbours");
			});
		}

		[Test]
		public void FarthestVisiblePicksFurthestClearWaypoint()
		{
			var from = Center(C(0, 0));
			var upcoming = new List<CPos> { C(1, 0), C(2, 1), C(3, 1) };
			Assert.Multiple(() =>
			{
				Assert.That(PathStringPulling.FarthestVisible(from, upcoming, 8, Center, AllOpen), Is.EqualTo(2),
					"all clear -> farthest index");

				// Block a cell the k=2 sightline needs (its corner-guard neighbour (2,0)) but the k=1 sightline
				// never touches: the shortcut shortens to index 1.
				var blockFar = Blocking(C(2, 0));
				var far = PathStringPulling.FarthestVisible(from, upcoming, 8, Center, blockFar);
				Assert.That(far, Is.EqualTo(1), "a blocker past index 1 shortens the shortcut");

				Assert.That(PathStringPulling.FarthestVisible(from, upcoming, 1, Center, AllOpen), Is.EqualTo(0),
					"lookahead of 1 disables any shortcut");
			});
		}

		[Test]
		public void ProjectOntoSightlineIsExactInteger()
		{
			var from = Center(C(0, 0));                 // (512,512)

			// Collinear boundary projects onto itself.
			var straightTarget = Center(C(2, 0));       // (2560,512)
			var boundary = new WPos(1024, 512, 0);      // between (0,0) and (1,0)
			Assert.That(PathStringPulling.ProjectOntoSightline(from, boundary, straightTarget), Is.EqualTo(boundary),
				"boundary already on the sightline is unchanged");

			// Tilted sightline toward (4,2): the boundary's shadow is a pinned integer point.
			var tiltTarget = Center(C(4, 2));           // (4608,2560)
			var shadow = PathStringPulling.ProjectOntoSightline(from, boundary, tiltTarget);
			Assert.That(shadow, Is.EqualTo(new WPos(921, 716, 0)), "pinned integer projection");

			// Degenerate: target behind from -> stay put.
			var behind = new WPos(-1024, 512, 0);
			Assert.That(PathStringPulling.ProjectOntoSightline(from, boundary, behind), Is.EqualTo(from),
				"boundary not ahead of the sightline -> render at from");
		}

		[Test]
		public void ShadowDivergenceStaysBounded()
		{
			// Walk a 45-degree zig-zag (E, NE, E, NE, ...) and confirm every rendered shadow stays within one
			// cell of the reserved geometric boundary it replaces — the documented visual-vs-reserved bound.
			var zig = new List<CPos>
			{
				C(0, 0), C(1, 0), C(2, 1), C(3, 1), C(4, 2), C(5, 2), C(6, 3)
			};

			var maxDivergenceSq = 0L;
			for (var i = 1; i < zig.Count - 1; i++)
			{
				var from = Center(zig[i - 1]);
				var geomBoundary = WPos.Lerp(Center(zig[i - 1]), Center(zig[i]), 1, 2);
				var upcoming = zig.GetRange(i, zig.Count - i);
				var smoothed = PathStringPulling.SmoothTarget(from, geomBoundary, upcoming, 8, Center, AllOpen);

				var dx = (long)smoothed.X - geomBoundary.X;
				var dy = (long)smoothed.Y - geomBoundary.Y;
				maxDivergenceSq = Math.Max(maxDivergenceSq, dx * dx + dy * dy);
			}

			// Bound: the hard clamp keeps every shadow within half a cell of its reserved boundary.
			Assert.That(maxDivergenceSq, Is.LessThanOrEqualTo(
				(long)PathStringPulling.DefaultMaxDivergence * PathStringPulling.DefaultMaxDivergence),
				"rendered shadow never diverges past the clamp from its reserved boundary");
		}

		[Test]
		public void ClampDivergenceCapsTheOffset()
		{
			var geomTo = new WPos(1000, 2000, 0);

			// Within the cap: unchanged.
			var near = new WPos(1000 + 300, 2000, 0);
			Assert.That(PathStringPulling.ClampDivergence(near, geomTo, 512), Is.EqualTo(near));

			// Beyond the cap: pulled back onto the cap radius (integer, along the same direction).
			var far = new WPos(1000 + 900, 2000, 0);
			var clamped = PathStringPulling.ClampDivergence(far, geomTo, 512);
			Assert.Multiple(() =>
			{
				Assert.That(clamped, Is.EqualTo(new WPos(1000 + 512, 2000, 0)), "clamped to the cap along +x");

				// maxDivergence 0 disables the clamp entirely.
				Assert.That(PathStringPulling.ClampDivergence(far, geomTo, 0), Is.EqualTo(far));
			});
		}

		[Test]
		public void SmoothingIsInertWhenDisabledOrDeadEnd()
		{
			var from = Center(C(0, 0));
			var boundary = new WPos(1024, 512, 0);
			var upcoming = new List<CPos> { C(1, 0), C(2, 0) };
			Assert.Multiple(() =>
			{
				// maxLookahead <= 1 is the toggle-off / no-shortcut path: returns the geometric target verbatim.
				Assert.That(PathStringPulling.SmoothTarget(from, boundary, upcoming, 1, Center, AllOpen),
					Is.EqualTo(boundary));

				// Empty upcoming: nothing to pull toward.
				Assert.That(PathStringPulling.SmoothTarget(from, boundary, new List<CPos>(), 8, Center, AllOpen),
					Is.EqualTo(boundary));

				// Determinism: identical inputs -> byte-identical output.
				var a = PathStringPulling.SmoothTarget(from, boundary, upcoming, 8, Center, AllOpen);
				var b = PathStringPulling.SmoothTarget(from, boundary, upcoming, 8, Center, AllOpen);
				Assert.That(a, Is.EqualTo(b));
			});
		}
	}
}
