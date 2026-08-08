#region Copyright & License Information
/*
 * WW3MOD diagonal-squeeze geometry — the cell arithmetic behind "can a vehicle drive between two tank traps"
 * (pure functions, no world state).
 *
 * WHY THIS IS SPLIT OUT: Locomotor.IsDiagonalSqueeze is two separable things — this geometry, which decides
 * WHICH cells a diagonal step passes between, and a cache lookup that decides whether those cells are blocked.
 * The lookup needs a live World and is exercised by the autotest scenario; the geometry does not and can be
 * pinned in NUnit. Splitting them is what makes the load-bearing properties testable without starting a game.
 *
 * THE PROPERTY THAT MATTERS MOST is that Shoulders never returns srcNode or destNode. Locomotor's completeness
 * argument — that the both-shoulders rule cannot break DensePathGraph.DirectedNeighbors pruning — rests on one
 * shoulder of every discarded diagonal being the cell currently being expanded, which is passable by
 * construction. If Shoulders could ever return an endpoint, that argument collapses and the pathfinder starts
 * losing reachable cells. It is also the exact claim that led to declining a proposed `cell == srcNode`
 * short-circuit during review as dead code, so it is pinned rather than left as an assertion in a comment.
 *
 * DIRECTION INDEPENDENCE is the other pinned property: the shoulder PAIR for src->dest must equal the pair for
 * dest->src. DirectedNeighbors prunes expansions assuming reachability does not depend on approach direction, so
 * an asymmetric rule would make a cell reachable one way and not the other.
 *
 * DETERMINISM (influence-stack invariant): integer arithmetic only, no floating point, no random draws, no
 * collection iteration. Two clients over the same synced state compute identical results.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class DiagonalSqueezeGeometry
	{
		/// <summary>
		/// True when the step from <paramref name="srcNode"/> to <paramref name="destNode"/> is a single diagonal,
		/// i.e. it crosses the corner point shared by four cells. Orthogonal steps, zero-length steps, longer steps
		/// and steps that change movement layer are all false — none of them passes through a corner.
		/// </summary>
		public static bool IsCornerCrossing(CPos srcNode, CPos destNode)
		{
			// A tunnel/bridge transition is a layer change, not a move across the grid, so it has no shoulders.
			if (srcNode.Layer != destNode.Layer)
				return false;

			var dx = destNode.X - srcNode.X;
			var dy = destNode.Y - srcNode.Y;

			// Squaring avoids a pair of Math.Abs calls and rejects 0 and >1 in the same test.
			return dx * dx == 1 && dy * dy == 1;
		}

		/// <summary>
		/// The two cells a diagonal step squeezes between: the other two corners of the 2x2 block spanned by
		/// <paramref name="srcNode"/> and <paramref name="destNode"/>. Never returns either endpoint, and returns
		/// the same pair whichever way the step is taken. Only meaningful when
		/// <see cref="IsCornerCrossing(CPos, CPos)"/> holds.
		/// </summary>
		public static (CPos First, CPos Second) Shoulders(CPos srcNode, CPos destNode)
		{
			// Mixing one coordinate from each endpoint is what makes these the OTHER two corners: each shoulder
			// differs from srcNode in exactly one axis and from destNode in the other, so it can equal neither.
			return (new CPos(srcNode.X, destNode.Y, srcNode.Layer),
				new CPos(destNode.X, srcNode.Y, srcNode.Layer));
		}
	}
}
