#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Cover-aware formation interpreter for grouped Move/AttackMove orders.",
		"Each unit gets an ideal box-formation slot from its CohesionMode spacing, then bids",
		"that slot against nearby high-density cells (trees, buildings, walls) from Map.DensityLayer.",
		"Passable cells adjacent to dense actors score highest, so squads naturally settle next to",
		"cover instead of in the geometric center of the click. Open ground falls through to the",
		"original box formation. Single-unit selections short-circuit to a literal move.")]
	public class CohesionMoveModifierInfo : TraitInfo
	{
		[Desc("Column spacing in WDist for Tight mode.")]
		public readonly int TightColSpacing = 1024;

		[Desc("Row depth in WDist for Tight mode.")]
		public readonly int TightRowSpacing = 1024;

		[Desc("Column spacing in WDist for Loose mode.")]
		public readonly int LooseColSpacing = 2048;

		[Desc("Row depth in WDist for Loose mode.")]
		public readonly int LooseRowSpacing = 1536;

		[Desc("Column spacing in WDist for Spread mode.")]
		public readonly int SpreadColSpacing = 3072;

		[Desc("Row depth in WDist for Spread mode.")]
		public readonly int SpreadRowSpacing = 2560;

		[Desc("Search radius in cells when scanning for cover cells around each unit's ideal slot.",
			"Tight uses radius-2, Loose radius-3, Spread radius-4.")]
		public readonly int TightCoverSearchRadius = 2;
		public readonly int LooseCoverSearchRadius = 3;
		public readonly int SpreadCoverSearchRadius = 4;

		[Desc("How strongly a candidate cell's cover score must beat its distance penalty to win.",
			"Score = CoverScore(cell) - chebyshev(cand, ideal) * DistancePenalty. Higher value = stricter pull.",
			"Chebyshev (grid king-move) distance is used because the bidder reasons in cells. A single-trunk-adjacent",
			"cell (CoverScore 10) wins at distance ceil(10/DistancePenalty); penalty 3 gives a 3-cell pull range.")]
		public readonly int DistancePenalty = 3;

		public override object Create(ActorInitializer init) { return new CohesionMoveModifier(this); }
	}

	public class CohesionMoveModifier : IModifyGroupOrder
	{
		readonly CohesionMoveModifierInfo info;

		public CohesionMoveModifier(CohesionMoveModifierInfo info)
		{
			this.info = info;
		}

		void GetSpacing(CohesionMode mode, out int colSpacing, out int rowSpacing, out int coverRadius)
		{
			switch (mode)
			{
				case CohesionMode.Tight:
					colSpacing = info.TightColSpacing;
					rowSpacing = info.TightRowSpacing;
					coverRadius = info.TightCoverSearchRadius;
					return;
				case CohesionMode.Spread:
					colSpacing = info.SpreadColSpacing;
					rowSpacing = info.SpreadRowSpacing;
					coverRadius = info.SpreadCoverSearchRadius;
					return;
				default:
					colSpacing = info.LooseColSpacing;
					rowSpacing = info.LooseRowSpacing;
					coverRadius = info.LooseCoverSearchRadius;
					return;
			}
		}

		// Cover score for a candidate cell: sum of 8-neighbor densities. Cells with density>0 on
		// themselves (a tree, a wall, a building tile) score 0 — they're usually impassable, so we
		// don't want to bid for them. Passable cells adjacent to dense actors get the highest
		// scores, which produces natural "edge of forest" lines and "in the gap between trees"
		// clusters without needing pathability queries at bid time.
		static int CoverScore(Map map, CPos cell)
		{
			if (map.DensityLayer == null)
				return 0;

			if (!map.DensityLayer.IsValidCoordinate(cell.X, cell.Y))
				return 0;

			if (map.DensityLayer[cell] > 0)
				return 0;

			var sum = 0;
			for (var dy = -1; dy <= 1; dy++)
			{
				for (var dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;

					var n = new CPos(cell.X + dx, cell.Y + dy);
					if (map.DensityLayer.IsValidCoordinate(n.X, n.Y))
						sum += map.DensityLayer[n];
				}
			}

			return sum;
		}

		Order IModifyGroupOrder.ModifyGroupOrder(Order individualOrder, Actor subject, Actor[] allGroupedActors)
		{
			if (subject == null || subject.IsDead || !subject.IsInWorld)
				return individualOrder;

			var orderString = individualOrder.OrderString;
			if (orderString != "Move" && orderString != "AttackMove")
				return individualOrder;

			var n = 0;
			for (var i = 0; i < allGroupedActors.Length; i++)
			{
				var a = allGroupedActors[i];
				if (a != null && !a.IsDead && a.IsInWorld)
					n++;
			}

			// Single-unit short-circuit — preserves exact placement when the player
			// selects just one unit. Per the intent-aware-movement plan, the bidder
			// only fires for genuine group orders.
			if (n <= 1)
				return individualOrder;

			var validActors = new Actor[n];
			var vi = 0;
			for (var i = 0; i < allGroupedActors.Length; i++)
			{
				var a = allGroupedActors[i];
				if (a != null && !a.IsDead && a.IsInWorld)
					validActors[vi++] = a;
			}

			Array.Sort(validActors, (a, b) => a.ActorID.CompareTo(b.ActorID));

			var idx = Array.IndexOf(validActors, subject);
			if (idx < 0)
				return individualOrder;

			var targetPos = individualOrder.Target.CenterPosition;

			var centroidX = 0L;
			var centroidY = 0L;
			for (var i = 0; i < n; i++)
			{
				centroidX += validActors[i].CenterPosition.X;
				centroidY += validActors[i].CenterPosition.Y;
			}

			centroidX /= n;
			centroidY /= n;

			var moveDirX = targetPos.X - (int)centroidX;
			var moveDirY = targetPos.Y - (int)centroidY;
			var moveLenSq = (long)moveDirX * moveDirX + (long)moveDirY * moveDirY;
			int moveLen;

			if (moveLenSq < 512L * 512L)
			{
				moveDirX = 0;
				moveDirY = -1024;
				moveLen = 1024;
			}
			else
			{
				moveLen = (int)Exts.ISqrt(moveLenSq);
				if (moveLen == 0)
					return individualOrder;
			}

			var perpX = -moveDirY;
			var perpY = moveDirX;

			var cols = (int)Math.Ceiling(Math.Sqrt(n * 2.0));
			cols = Math.Min(cols, n);
			cols = Math.Max(cols, 2);

			// PITFALL: cohesion mode is read from the SUBJECT, not aggregated over the group.
			// A heterogeneous group (some Tight, some Loose) will see inconsistent spacing — each
			// actor uses its own mode and computes its own bid order. v1 ships this way for
			// simplicity; in practice grouped units almost always share a mode.
			var autoTarget = subject.TraitOrDefault<AutoTarget>();
			var mode = autoTarget?.CohesionValue ?? CohesionMode.Loose;
			GetSpacing(mode, out var colSpacing, out var rowSpacing, out var coverRadius);

			var map = subject.World.Map;
			var distancePenalty = info.DistancePenalty;

			// Deterministic cover-aware bidding: each actor (called independently per
			// ModifyGroupOrder) re-runs the assignment from idx 0 up to its own slot. Earlier
			// actors' claimed cells are excluded from later candidates, so the resulting per-actor
			// destination is consistent across the parallel calls.
			var claimed = new HashSet<CPos>();
			var myCell = new CPos();

			for (var i = 0; i <= idx; i++)
			{
				var row = i / cols;
				var col = i % cols;
				var unitsInRow = Math.Min(cols, n - row * cols);

				var perpOffset = (2 * col - (unitsInRow - 1)) * colSpacing / 2;
				if (row % 2 == 1)
					perpOffset += colSpacing / 2;

				var depthOffset = -row * rowSpacing;

				var offsetX = (int)((long)perpOffset * perpX / moveLen);
				var offsetY = (int)((long)perpOffset * perpY / moveLen);
				if (depthOffset != 0)
				{
					offsetX += (int)((long)depthOffset * moveDirX / moveLen);
					offsetY += (int)((long)depthOffset * moveDirY / moveLen);
				}

				var idealPos = new WPos(targetPos.X + offsetX, targetPos.Y + offsetY, targetPos.Z);
				var idealCell = map.Clamp(map.CellContaining(idealPos));

				// Bid: start with the ideal cell as baseline, search the radius for a cell that
				// scores higher after a chebyshev-distance penalty. A single-trunk-adjacent
				// candidate (CoverScore 10) wins out to chebyshev ceil(10/DistancePenalty); with
				// the default penalty of 3 that's a 3-cell pull range, matching the Loose
				// cohesion search radius.
				var bestCell = idealCell;
				var bestScore = CoverScore(map, idealCell);

				for (var dy = -coverRadius; dy <= coverRadius; dy++)
				{
					for (var dx = -coverRadius; dx <= coverRadius; dx++)
					{
						if (dx == 0 && dy == 0)
							continue;

						var cand = new CPos(idealCell.X + dx, idealCell.Y + dy);
						if (!map.DensityLayer.IsValidCoordinate(cand.X, cand.Y))
							continue;

						if (claimed.Contains(cand))
							continue;

						var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
						var score = CoverScore(map, cand) - dist * distancePenalty;

						if (score > bestScore)
						{
							bestCell = cand;
							bestScore = score;
						}
					}
				}

				if (i == idx)
				{
					myCell = bestCell;
					break;
				}

				claimed.Add(bestCell);
			}

			return individualOrder.WithTarget(Target.FromCell(subject.World, myCell));
		}
	}
}
