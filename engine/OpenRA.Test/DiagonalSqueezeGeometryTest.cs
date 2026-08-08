#region Copyright & License Information
/*
 * WW3MOD DiagonalSqueezeGeometry tests — the cell arithmetic behind "can a vehicle drive between two tank traps".
 *
 * These pin the two properties the pathfinder's COMPLETENESS rests on, neither of which the autotest scenario can
 * reach. Locomotor argues that the both-shoulders rule cannot break DensePathGraph.DirectedNeighbors pruning
 * because one shoulder of every discarded diagonal is the cell currently being expanded, which is passable by
 * construction. That argument needs Shoulders to never return an endpoint (or the "expanding cell" step is
 * meaningless) and to be direction-independent (or a cell is reachable one way and not the other). A regression in
 * either would not look like a bug — units would silently fail to path to reachable cells.
 *
 * The scenario covers the other half: whether a real vehicle on a real map is actually stopped. Between them there
 * is no need to start a game to know the geometry is right.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DiagonalSqueezeGeometryTest
	{
		static readonly CVec[] Diagonals =
		{
			new CVec(1, 1), new CVec(1, -1), new CVec(-1, 1), new CVec(-1, -1)
		};

		static readonly CVec[] Orthogonals =
		{
			new CVec(1, 0), new CVec(-1, 0), new CVec(0, 1), new CVec(0, -1)
		};

		// ---------- IsCornerCrossing ----------

		[Test]
		public void IsCornerCrossing_TrueForAllFourDiagonals()
		{
			var src = new CPos(10, 10);
			Assert.Multiple(() =>
			{
				foreach (var d in Diagonals)
					Assert.That(DiagonalSqueezeGeometry.IsCornerCrossing(src, src + d), Is.True,
						$"diagonal step {d} should cross a corner");
			});
		}

		[Test]
		public void IsCornerCrossing_FalseForOrthogonalSteps()
		{
			// The rule must never fire on a straight step. If it did, two obstacles flanking a corridor would close
			// the corridor itself rather than just its corner — over-blocking on every map with a walled lane.
			var src = new CPos(10, 10);
			Assert.Multiple(() =>
			{
				foreach (var d in Orthogonals)
					Assert.That(DiagonalSqueezeGeometry.IsCornerCrossing(src, src + d), Is.False,
						$"orthogonal step {d} passes between no cells");
			});
		}

		[Test]
		public void IsCornerCrossing_FalseForZeroAndLongerSteps()
		{
			var src = new CPos(10, 10);
			Assert.Multiple(() =>
			{
				Assert.That(DiagonalSqueezeGeometry.IsCornerCrossing(src, src), Is.False, "a zero step");
				Assert.That(DiagonalSqueezeGeometry.IsCornerCrossing(src, new CPos(12, 12)), Is.False, "two cells out");
				Assert.That(DiagonalSqueezeGeometry.IsCornerCrossing(src, new CPos(11, 12)), Is.False, "a knight's move");
			});
		}

		[Test]
		public void IsCornerCrossing_FalseAcrossMovementLayers()
		{
			// A tunnel or bridge transition changes layer rather than crossing the grid, so it has no shoulders.
			// Treating it as a corner would let two obstacles on the ground layer block a tunnel mouth above them.
			var ground = new CPos(10, 10, 0);
			var tunnel = new CPos(11, 11, 1);
			Assert.That(DiagonalSqueezeGeometry.IsCornerCrossing(ground, tunnel), Is.False);
		}

		// ---------- Shoulders ----------

		[Test]
		public void Shoulders_AreNeverTheEndpoints()
		{
			// THE load-bearing property. Locomotor's completeness argument identifies one shoulder with the cell
			// being expanded, which is passable by construction; if a shoulder could be an endpoint the argument
			// collapses and the pathfinder can silently drop reachable cells. This is also the claim that made a
			// proposed `cell == srcNode` short-circuit dead code during review, so it is pinned, not assumed.
			var src = new CPos(10, 10);
			Assert.Multiple(() =>
			{
				foreach (var d in Diagonals)
				{
					var dest = src + d;
					var (first, second) = DiagonalSqueezeGeometry.Shoulders(src, dest);

					Assert.That(first, Is.Not.EqualTo(src), $"{d}: first shoulder is the source");
					Assert.That(first, Is.Not.EqualTo(dest), $"{d}: first shoulder is the destination");
					Assert.That(second, Is.Not.EqualTo(src), $"{d}: second shoulder is the source");
					Assert.That(second, Is.Not.EqualTo(dest), $"{d}: second shoulder is the destination");
					Assert.That(first, Is.Not.EqualTo(second), $"{d}: the two shoulders coincide");
				}
			});
		}

		[Test]
		public void Shoulders_AreDirectionIndependent()
		{
			// DirectedNeighbors prunes expansions assuming reachability does not depend on approach direction. An
			// asymmetric shoulder pair would make a corner passable one way and blocked the other, which shows up as
			// a unit that can leave a pocket but not re-enter it.
			var src = new CPos(10, 10);
			Assert.Multiple(() =>
			{
				foreach (var d in Diagonals)
				{
					var dest = src + d;
					var forward = DiagonalSqueezeGeometry.Shoulders(src, dest);
					var reverse = DiagonalSqueezeGeometry.Shoulders(dest, src);

					// Same unordered pair; the tuple order is allowed to swap.
					Assert.That(
						(forward.First == reverse.First && forward.Second == reverse.Second) ||
						(forward.First == reverse.Second && forward.Second == reverse.First),
						Is.True,
						$"{d}: shoulder pair differs by direction");
				}
			});
		}

		[Test]
		public void Shoulders_AreTheOtherTwoCornersOfTheBlock()
		{
			// Concrete case, so a reader can see the intended geometry rather than infer it: stepping from 20,16 to
			// 21,15 squeezes between 20,15 and 21,16 — the tank trap pair from the autotest scenario.
			var (first, second) = DiagonalSqueezeGeometry.Shoulders(new CPos(20, 16), new CPos(21, 15));

			Assert.Multiple(() =>
			{
				Assert.That(first, Is.EqualTo(new CPos(20, 15)));
				Assert.That(second, Is.EqualTo(new CPos(21, 16)));
			});
		}

		[Test]
		public void Shoulders_InheritTheSourceLayer()
		{
			// Shoulders are looked up in the locomotor's per-layer blocking cache, so a shoulder built on the wrong
			// layer would read another layer's occupancy — blocking a ground move on what is in a tunnel.
			var src = new CPos(10, 10, 3);
			var (first, second) = DiagonalSqueezeGeometry.Shoulders(src, new CPos(11, 11, 3));

			Assert.Multiple(() =>
			{
				Assert.That(first.Layer, Is.EqualTo((byte)3));
				Assert.That(second.Layer, Is.EqualTo((byte)3));
			});
		}
	}
}
