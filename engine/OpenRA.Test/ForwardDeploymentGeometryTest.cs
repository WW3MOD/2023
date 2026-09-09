#region Copyright & License Information
/*
 * WW3MOD forward-deployment geometry tests — where a pre-positioned force stands and which way it looks.
 *
 * The Forward Deployment lobby option places a force part of the way from the player's own spawn point toward
 * the nearest enemy's, facing the enemy. Two things in that sentence are easy to get silently wrong and neither
 * is visible without a running game:
 *
 *  - the CENTRE is integer arithmetic on cell coordinates, and C# integer division truncates toward ZERO rather
 *    than flooring, so the advance is not symmetric under negation unless it is written to be. A rule that
 *    advances 35% going east and 34% going west is a rule that puts the two players' forces at different
 *    distances from the line between them.
 *  - the BEARING is a WAngle, which in this codebase runs COUNTERCLOCKWISE from north over [0, 1024)
 *    (DOCS/reference/conventions.md). Get the handedness wrong and every deployed unit faces its own rear;
 *    get the north convention wrong and they face sideways. Both look deliberate on screen.
 *
 * Both are pure functions of integers, so this pins them with no map, no world and no frame.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ForwardDeploymentGeometryTest
	{
		static CPos Center(int hx, int hy, int ex, int ey, int pct)
		{
			return ForwardDeploymentGeometry.AdvancedCenter(new CPos(hx, hy), new CPos(ex, ey), pct);
		}

		[TestCase(0, 0, 100, 0, 0, 0, 0)]
		[TestCase(0, 0, 100, 0, 35, 35, 0)]
		[TestCase(0, 0, 100, 0, 100, 100, 0)]
		[TestCase(0, 0, 0, 100, 35, 0, 35)]
		[TestCase(10, 10, 110, 210, 50, 60, 110)]
		public void CenterInterpolatesTowardTheEnemy(int hx, int hy, int ex, int ey, int pct, int cx, int cy)
		{
			Assert.That(Center(hx, hy, ex, ey, pct), Is.EqualTo(new CPos(cx, cy)));
		}

		[Test]
		public void ZeroAdvanceIsTheHomeLocation()
		{
			// The retreat search's floor. If this ever stopped being exactly home, the fallback would stop
			// being "the deployment we already ship" and become a third, untested placement.
			foreach (var e in new[] { new CPos(50, 0), new CPos(-50, 0), new CPos(0, 50), new CPos(-13, 77) })
				Assert.That(ForwardDeploymentGeometry.AdvancedCenter(new CPos(20, 30), e, 0), Is.EqualTo(new CPos(20, 30)));
		}

		[Test]
		public void FullAdvanceIsTheEnemyLocation()
		{
			Assert.That(Center(20, 30, -13, 77, 100), Is.EqualTo(new CPos(-13, 77)));
		}

		[Test]
		public void AdvanceIsSymmetricUnderNegation()
		{
			// C# integer division truncates toward zero, so `delta * pct / 100` moves the same NUMBER of cells
			// whichever way the delta points. A floor-based implementation would be one cell further in the
			// negative direction and would deploy the two sides asymmetrically about the midline.
			const int Home = 500;
			for (var pct = 0; pct <= 100; pct++)
			{
				for (var d = 1; d <= 200; d++)
				{
					var forward = ForwardDeploymentGeometry.AdvancedCenter(new CPos(Home, Home), new CPos(Home + d, Home + d), pct);
					var backward = ForwardDeploymentGeometry.AdvancedCenter(new CPos(Home, Home), new CPos(Home - d, Home - d), pct);
					Assert.That(forward.X - Home, Is.EqualTo(Home - backward.X), $"pct {pct}, distance {d}");
					Assert.That(forward.Y - Home, Is.EqualTo(Home - backward.Y), $"pct {pct}, distance {d}");
				}
			}
		}

		[Test]
		public void CenterNeverOvershootsEitherEndpoint()
		{
			// Why the centre needs no bounds check of its own: for any advance in [0, 100] the result is a
			// convex combination of two cells that are already inside the map.
			foreach (var ex in new[] { -97, -13, 0, 5, 61 })
				foreach (var ey in new[] { -61, -1, 0, 42, 130 })
					for (var pct = 0; pct <= 100; pct += 5)
					{
						var c = ForwardDeploymentGeometry.AdvancedCenter(CPos.Zero, new CPos(ex, ey), pct);
						Assert.That(c.X, Is.InRange(System.Math.Min(0, ex), System.Math.Max(0, ex)));
						Assert.That(c.Y, Is.InRange(System.Math.Min(0, ey), System.Math.Max(0, ey)));
					}
		}

		[Test]
		public void RetreatStepsDescendFromTheFullAdvanceToZero()
		{
			const int Pct = 35;
			const int Steps = 5;
			var advances = Enumerable.Range(0, Steps + 1)
				.Reverse()
				.Select(s => ForwardDeploymentGeometry.AdvanceAtStep(Pct, Steps, s))
				.ToArray();

			Assert.That(advances, Is.EqualTo(new[] { 35, 28, 21, 14, 7, 0 }));
			Assert.That(advances.First(), Is.EqualTo(Pct));
			Assert.That(advances.Last(), Is.Zero, "the search must bottom out at the home location");
			Assert.That(advances, Is.Ordered.Descending);
		}

		// WAngle is COUNTERCLOCKWISE from north over [0, 1024): 0 north, 256 west, 512 south, 768 east.
		// DOCS/reference/conventions.md — "units on the LEFT facing right → Facing: 768 (East)".
		[TestCase(0, -1, 0)]
		[TestCase(-1, 0, 256)]
		[TestCase(0, 1, 512)]
		[TestCase(1, 0, 768)]
		public void BearingMatchesTheCardinalConvention(int dx, int dy, int expected)
		{
			var from = new WPos(10240, 10240, 0);
			var to = new WPos(10240 + dx * 4096, 10240 + dy * 4096, 0);
			Assert.That(ForwardDeploymentGeometry.BearingToward(from, to).Angle, Is.EqualTo(expected));
		}

		[Test]
		public void BearingIsCounterclockwiseNotClockwise()
		{
			// The single assertion that catches a handedness flip. North-east is one eighth of the way round
			// from north going COUNTERCLOCKWISE through west, i.e. 896 — not 128. A clockwise implementation
			// passes every cardinal test above and fails only here and on the other three diagonals.
			var from = new WPos(0, 0, 0);
			Assert.That(ForwardDeploymentGeometry.BearingToward(from, new WPos(4096, -4096, 0)).Angle, Is.EqualTo(896), "north-east");
			Assert.That(ForwardDeploymentGeometry.BearingToward(from, new WPos(-4096, -4096, 0)).Angle, Is.EqualTo(128), "north-west");
			Assert.That(ForwardDeploymentGeometry.BearingToward(from, new WPos(-4096, 4096, 0)).Angle, Is.EqualTo(384), "south-west");
			Assert.That(ForwardDeploymentGeometry.BearingToward(from, new WPos(4096, 4096, 0)).Angle, Is.EqualTo(640), "south-east");
		}

		[Test]
		public void BearingIgnoresHeightAndScale()
		{
			// The aim point is a cell centre, which carries the terrain height. A bearing that let Z in would
			// swing with the slope between the two spawn points.
			var flat = ForwardDeploymentGeometry.BearingToward(new WPos(0, 0, 0), new WPos(3000, -7000, 0));
			var steep = ForwardDeploymentGeometry.BearingToward(new WPos(0, 0, 0), new WPos(3000, -7000, 9000));
			Assert.That(steep, Is.EqualTo(flat));

			var near = ForwardDeploymentGeometry.BearingToward(new WPos(0, 0, 0), new WPos(3, -7, 0));
			var far = ForwardDeploymentGeometry.BearingToward(new WPos(0, 0, 0), new WPos(300000, -700000, 0));
			Assert.That(far, Is.EqualTo(near), "same direction at any magnitude below the ArcTan overflow ceiling");
		}

		[Test]
		public void BearingOnACoincidentAimPointIsNorthRatherThanEast()
		{
			// WVec.Yaw only guards LengthSquared == 0, so a delta that is zero horizontally but not vertically
			// would fall through ArcTan(0, 0) and come back 768 — a confident, wrong "face east".
			Assert.That(ForwardDeploymentGeometry.BearingToward(new WPos(5, 5, 0), new WPos(5, 5, 0)), Is.EqualTo(WAngle.Zero));
			Assert.That(ForwardDeploymentGeometry.BearingToward(new WPos(5, 5, 0), new WPos(5, 5, 2048)), Is.EqualTo(WAngle.Zero));
		}

		[Test]
		public void NearestEnemyPrefersTheClosestAndBreaksTiesOnOrder()
		{
			var home = new CPos(10, 10);
			var found = ForwardDeploymentGeometry.TryFindNearest(home,
				new[] { new CPos(60, 10), new CPos(10, 25), new CPos(90, 90) }, out var nearest);

			Assert.That(found, Is.True);
			Assert.That(nearest, Is.EqualTo(new CPos(10, 25)));

			// Equidistant candidates: the first wins, so every client picks the same one from the same
			// player enumeration.
			ForwardDeploymentGeometry.TryFindNearest(home, new[] { new CPos(10, 40), new CPos(40, 10) }, out var tied);
			Assert.That(tied, Is.EqualTo(new CPos(10, 40)));
		}

		[Test]
		public void NearestEnemyReportsFailureRatherThanTheOriginOnAnEmptySet()
		{
			// (0,0) is a legal cell on ww3mod maps, so a sentinel return value cannot be told from a real hit —
			// the same trap the support-actor search carries a PITFALL comment about.
			var found = ForwardDeploymentGeometry.TryFindNearest(new CPos(7, 9), System.Array.Empty<CPos>(), out var nearest);
			Assert.That(found, Is.False);
			Assert.That(nearest, Is.EqualTo(new CPos(7, 9)), "no enemy leaves the home location untouched");
		}

		[Test]
		public void NearestEnemyDoesNotOverflowAtMapScale()
		{
			// Squared distances are accumulated as long. On an int accumulator a pair of far-apart cells on a
			// large map would still be fine, but the guard is cheap and the failure would be a silently wrong
			// choice rather than a crash.
			var found = ForwardDeploymentGeometry.TryFindNearest(new CPos(-46000, -46000),
				new[] { new CPos(46000, 46000), new CPos(45999, 46000) }, out var nearest);

			Assert.That(found, Is.True);
			Assert.That(nearest, Is.EqualTo(new CPos(45999, 46000)));
		}
	}
}
