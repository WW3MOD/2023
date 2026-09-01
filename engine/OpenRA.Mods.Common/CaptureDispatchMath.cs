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

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// Pure assignment maths for dispatching capture units at capturable structures.
	/// Deliberately free of Actor/World so it can be pinned in NUnit — this repo has no
	/// test infrastructure that constructs a World, so anything not extracted to here is
	/// unverifiable outside a live game.
	/// </summary>
	public static class CaptureDispatchMath
	{
		public const int Unassigned = -1;

		/// <summary>Cost that marks a capturer/target pair as not permitted at all.</summary>
		public const long Infeasible = long.MaxValue;

		/// <summary>
		/// <para>Assign capturers to targets so that the LAST capture finishes as early as possible.</para>
		///
		/// <para>Capturers walk in parallel, so the time until every target is taken is the time of the
		/// SINGLE LONGEST walk, not the sum. Minimising a sum (what nearest-first greedy effectively
		/// chases) is therefore optimising the wrong number: greedy will happily hand the one short
		/// trip to the capturer that was the only candidate for the long one, and strand it.
		/// This is the linear bottleneck assignment problem, and it is solved here EXACTLY —
		/// binary search over the distinct edge costs, testing each threshold with a maximum
		/// bipartite matching (Kuhn's augmenting paths).</para>
		///
		/// <para>Ties on the bottleneck are then broken towards lower TOTAL cost by a 2-swap pass. That
		/// pass is a heuristic and is NOT guaranteed to find the minimum-sum solution among the
		/// bottleneck-optimal ones — see <see cref="ReduceTotalCost"/>. Only the bottleneck itself
		/// is optimal.</para>
		/// </summary>
		/// <param name="cost">cost[capturer, target]. Use <see cref="Infeasible"/> for a forbidden pair.</param>
		/// <returns>assignment[capturer] = target index, or <see cref="Unassigned"/>.</returns>
		public static int[] Assign(long[,] cost)
		{
			ArgumentNullException.ThrowIfNull(cost);

			var capturers = cost.GetLength(0);
			var targets = cost.GetLength(1);

			var assignment = new int[capturers];
			for (var i = 0; i < capturers; i++)
				assignment[i] = Unassigned;

			if (capturers == 0 || targets == 0)
				return assignment;

			// Every distinct feasible cost is a candidate bottleneck; the optimum is one of them.
			var candidates = new List<long>();
			for (var i = 0; i < capturers; i++)
				for (var j = 0; j < targets; j++)
					if (cost[i, j] != Infeasible)
						candidates.Add(cost[i, j]);

			if (candidates.Count == 0)
				return assignment;

			candidates.Sort();

			// How many pairs can be matched at all? The bottleneck must not cost us a pairing.
			var best = MaximumMatching(cost, Infeasible, targets, capturers, out _);
			if (best == 0)
				return assignment;

			// Smallest threshold that still achieves `best` pairings.
			var lo = 0;
			var hi = candidates.Count - 1;
			var bottleneck = candidates[hi];
			while (lo <= hi)
			{
				var mid = lo + ((hi - lo) / 2);
				if (MaximumMatching(cost, candidates[mid], targets, capturers, out _) == best)
				{
					bottleneck = candidates[mid];
					hi = mid - 1;
				}
				else
					lo = mid + 1;
			}

			MaximumMatching(cost, bottleneck, targets, capturers, out var targetToCapturer);

			for (var j = 0; j < targets; j++)
				if (targetToCapturer[j] != Unassigned)
					assignment[targetToCapturer[j]] = j;

			ReduceTotalCost(cost, bottleneck, assignment);

			return assignment;
		}

		/// <summary>
		/// <para>Among assignments that keep every edge within <paramref name="bottleneck"/>, walk downhill
		/// on total cost with 2-swaps (swap two capturers' targets; or move a target to a currently
		/// idle capturer). Terminates because the total strictly decreases and is bounded below.</para>
		///
		/// <para>This is a LOCAL search: it reaches a 2-opt local minimum, which need not be the global
		/// minimum-sum bottleneck-optimal assignment. It exists only so that slack capturers are not
		/// sent on visibly silly walks when the bottleneck leaves a free choice; the guarantee this
		/// function carries is that it never RAISES the bottleneck, not that the sum is optimal.</para>
		/// </summary>
		static void ReduceTotalCost(long[,] cost, long bottleneck, int[] assignment)
		{
			var capturers = assignment.Length;
			var improved = true;

			while (improved)
			{
				improved = false;

				for (var a = 0; a < capturers && !improved; a++)
				{
					for (var b = 0; b < capturers && !improved; b++)
					{
						if (a == b)
							continue;

						var ta = assignment[a];
						var tb = assignment[b];
						if (ta == Unassigned && tb == Unassigned)
							continue;

						// Feasibility first: an Infeasible edge is long.MaxValue and would overflow the
						// sum below into a negative, which reads as an improvement.
						var newA = Edge(cost, a, tb);
						var newB = Edge(cost, b, ta);
						if (newA > bottleneck || newB > bottleneck)
							continue;

						if (newA + newB >= Edge(cost, a, ta) + Edge(cost, b, tb))
							continue;

						assignment[a] = tb;
						assignment[b] = ta;
						improved = true;
					}
				}
			}
		}

		/// <summary>Cost of a capturer/target pair, treating "no target" as free.</summary>
		static long Edge(long[,] cost, int capturer, int target)
		{
			if (target == Unassigned)
				return 0;

			var c = cost[capturer, target];
			return c == Infeasible ? Infeasible : c;
		}

		/// <summary>
		/// Maximum bipartite matching over edges costing at most <paramref name="threshold"/>.
		/// Iteration is in index order throughout, so the result is deterministic for a given matrix —
		/// which is what lets the NUnit fixtures assert an exact assignment rather than just its cost.
		/// </summary>
		static int MaximumMatching(long[,] cost, long threshold, int targets, int capturers, out int[] targetToCapturer)
		{
			targetToCapturer = new int[targets];
			for (var j = 0; j < targets; j++)
				targetToCapturer[j] = Unassigned;

			var matched = 0;
			var seen = new bool[targets];
			for (var i = 0; i < capturers; i++)
			{
				Array.Clear(seen, 0, targets);
				if (TryAugment(cost, threshold, i, seen, targetToCapturer, targets))
					matched++;
			}

			return matched;
		}

		static bool TryAugment(long[,] cost, long threshold, int capturer, bool[] seen, int[] targetToCapturer, int targets)
		{
			for (var j = 0; j < targets; j++)
			{
				if (seen[j])
					continue;

				var c = cost[capturer, j];
				if (c == Infeasible || c > threshold)
					continue;

				seen[j] = true;
				if (targetToCapturer[j] == Unassigned
					|| TryAugment(cost, threshold, targetToCapturer[j], seen, targetToCapturer, targets))
				{
					targetToCapturer[j] = capturer;
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Build the cost matrix from world positions. Straight-line distance stands in for travel
		/// time, which is exact only while every capturer moves at the same speed over comparable
		/// ground — true for a pool of one unit type (technicians), and wrong the moment a slower or
		/// faster capturer joins the pool or a wall sits between a capturer and its target. Real path
		/// lengths would need a pathfinder query per pair and are not worth a frame hitch here.
		/// </summary>
		public static long[,] CostMatrix(IReadOnlyList<WPos> capturers, IReadOnlyList<WPos> targets)
		{
			ArgumentNullException.ThrowIfNull(capturers);
			ArgumentNullException.ThrowIfNull(targets);

			var cost = new long[capturers.Count, targets.Count];
			for (var i = 0; i < capturers.Count; i++)
				for (var j = 0; j < targets.Count; j++)
					cost[i, j] = (targets[j] - capturers[i]).Length;

			return cost;
		}

		/// <summary>
		/// <para>The "already committed" rule, as a pure predicate.</para>
		///
		/// <para>A capturer is available for dispatch when it is not already carrying a capture order for
		/// some OTHER structure. Stealing a technician off an in-flight capture would make this
		/// feature worse than selecting one by hand, because a technician is CONSUMED by a successful
		/// capture (^CapturesNeutralBuildings sets ConsumedByCapture: true) — a stolen one does not
		/// come back to finish the job it left.</para>
		///
		/// <para>A capturer already heading at <paramref name="targetId"/> counts as available, so that
		/// re-issuing the same dispatch is idempotent rather than pulling in a second technician and
		/// spending two units on one building.</para>
		/// </summary>
		/// <param name="committedTargetId">Target the capturer already has a capture order for, or 0 for none.</param>
		public static bool IsAvailableFor(uint committedTargetId, uint targetId)
		{
			return committedTargetId == 0 || committedTargetId == targetId;
		}

		/// <summary>
		/// True when some capturer already holds a capture order for <paramref name="targetId"/>, so a
		/// fresh dispatch at it would spend a second consumable technician on a building that is
		/// already covered.
		/// </summary>
		public static bool IsAlreadyCovered(IReadOnlyList<uint> committedTargetIds, uint targetId)
		{
			ArgumentNullException.ThrowIfNull(committedTargetIds);

			for (var i = 0; i < committedTargetIds.Count; i++)
				if (committedTargetIds[i] != 0 && committedTargetIds[i] == targetId)
					return true;

			return false;
		}
	}
}
