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
	[Desc("Intent-aware formation interpreter for grouped Move/AttackMove orders.",
		"Classifies the click point against Map.DensityLayer cover and dispatches to one of three",
		"formation strategies: Open (no nearby cover → traditional box formation), SpreadInside",
		"(centered on density-rich area → top-K cover cells with min-spacing), or EdgeLine (click",
		"is offset from a cover patch → units form a line perpendicular to the density gradient,",
		"anchored just inside the cover side). All slots are computed once per order from the",
		"click point itself, then actor i (sorted by ActorID) takes slot i.")]
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

		[Desc("Sample radius in cells used by the intent classifier to read density distribution",
			"around the click. A 4-cell radius means a 9x9 sample window.")]
		public readonly int IntentSampleRadius = 4;

		[Desc("Total density in the sample window below which the intent is Open (no usable cover).",
			"A single t01 trunk contributes 10, so 15 = ~1.5 trunks worth of nearby cover.")]
		public readonly int OpenDensityThreshold = 15;

		[Desc("Squared centroid offset (in cells) above which the intent is EdgeLine.",
			"2 = centroid more than ~sqrt(2) ≈ 1.4 cells from click → click is offset from cover.",
			"Lower values trip EdgeLine more eagerly; higher values prefer SpreadInside.")]
		public readonly int EdgeOffsetThresholdCellsSq = 2;

		[Desc("EdgeLine anchor advance, expressed as a percentage of the gradient magnitude.",
			"100 = anchor sits at the cover centroid (line passes through cover center).",
			"75 = three quarters of the way from click toward centroid (line at the inner edge).",
			"50 = halfway (line at the rough cover boundary). 200 = pushed past centroid.",
			"For most claims, 100 yields a line that runs through the densest cover row.")]
		public readonly int EdgeAdvancePercent = 100;

		[Desc("Search radius in cells when SpreadInside collects candidate cover cells around the",
			"click. 4 = a 9x9 search window.")]
		public readonly int SpreadSearchRadius = 4;

		[Desc("Distance penalty multiplied by chebyshev cells when ranking SpreadInside candidates.",
			"Higher = more aggressive preference for cells close to the click; lower = picks high",
			"CoverScore cells anywhere in the search window. 5 means a candidate 3 cells from",
			"click needs CoverScore 15 over a click-adjacent candidate to win.")]
		public readonly int SpreadDistancePenalty = 5;

		[Desc("Bias toward cells on the group-centroid's side of the click. Multiplied by the",
			"chebyshev distance from each candidate cell to the group's centroid. When the squad",
			"is approaching cover from outside, this pulls slot picks toward the near edge so",
			"units don't get assigned to far-side cells they can't path to through dense cover.",
			"Set to 0 to disable group-side biasing.")]
		public readonly int SpreadGroupPenalty = 2;

		[Desc("Chebyshev distance threshold (cells) above which a SpreadInside intent is reclassified",
			"as Approach: the squad is far enough from a cover click that they need to march to it",
			"first. Approach lays the formation at the cover boundary between group and click — but",
			"this overrides the cover-cluster behavior, so we want it conservative. Bumped to 12",
			"so it only triggers for genuinely long marches across the map; medium-range clicks now",
			"stay in SpreadInside, where the bidder picks top-CoverScore cells near the click.")]
		public readonly int ApproachGroupDistanceCells = 12;

		[Desc("When true, slot candidates are filtered by Mobile.CanStayInCell on the subject. Cells",
			"the subject's locomotor can't park on (impassable terrain, building/tree footprints,",
			"narrow obstacles) are skipped. Set false for testing the pre-filter behaviour.")]
		public readonly bool FilterByPathability = true;

		public override object Create(ActorInitializer init) { return new CohesionMoveModifier(this); }
	}

	public class CohesionMoveModifier : IModifyGroupOrder
	{
		readonly CohesionMoveModifierInfo info;

		public CohesionMoveModifier(CohesionMoveModifierInfo info)
		{
			this.info = info;
		}

		enum Intent { Open, SpreadInside, EdgeLine, Approach }

		void GetSpacing(CohesionMode mode, out int colSpacing, out int rowSpacing)
		{
			switch (mode)
			{
				case CohesionMode.Tight:
					colSpacing = info.TightColSpacing;
					rowSpacing = info.TightRowSpacing;
					return;
				case CohesionMode.Spread:
					colSpacing = info.SpreadColSpacing;
					rowSpacing = info.SpreadRowSpacing;
					return;
				default:
					colSpacing = info.LooseColSpacing;
					rowSpacing = info.LooseRowSpacing;
					return;
			}
		}

		static int SafeDensity(Map map, CPos cell)
		{
			if (map.DensityLayer == null || !map.DensityLayer.IsValidCoordinate(cell.X, cell.Y))
				return 0;

			return map.DensityLayer[cell];
		}

		// Cover score for a candidate cell: sum of 8-neighbor density. Cells with density>0 on
		// themselves are excluded — they're usually impassable footprints we don't want to bid
		// for. Passable cells adjacent to dense actors get the highest scores.
		static int CoverScore(Map map, CPos cell)
		{
			if (SafeDensity(map, cell) > 0)
				return 0;

			var sum = 0;
			for (var dy = -1; dy <= 1; dy++)
			{
				for (var dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;

					sum += SafeDensity(map, new CPos(cell.X + dx, cell.Y + dy));
				}
			}

			return sum;
		}

		// Classify the click by walking a sample window around it. Returns the centroid offset
		// in cells (signed) so the EdgeLine branch can use it as the gradient direction.
		Intent ClassifyIntent(Map map, CPos clickCell, out int centroidDxCells, out int centroidDyCells)
		{
			var sampleRadius = info.IntentSampleRadius;
			var totalDensity = 0;
			var weightedX = 0;
			var weightedY = 0;

			for (var dy = -sampleRadius; dy <= sampleRadius; dy++)
			{
				for (var dx = -sampleRadius; dx <= sampleRadius; dx++)
				{
					var d = SafeDensity(map, new CPos(clickCell.X + dx, clickCell.Y + dy));
					if (d == 0)
						continue;

					totalDensity += d;
					weightedX += dx * d;
					weightedY += dy * d;
				}
			}

			centroidDxCells = 0;
			centroidDyCells = 0;

			if (totalDensity < info.OpenDensityThreshold)
				return Intent.Open;

			// Integer-round the centroid offset so the gradient is whole-cell stable.
			centroidDxCells = RoundDiv(weightedX, totalDensity);
			centroidDyCells = RoundDiv(weightedY, totalDensity);

			var offsetMagSq = centroidDxCells * centroidDxCells + centroidDyCells * centroidDyCells;
			if (offsetMagSq >= info.EdgeOffsetThresholdCellsSq)
				return Intent.EdgeLine;

			return Intent.SpreadInside;
		}

		static int RoundDiv(int numerator, int denominator)
		{
			if (denominator == 0)
				return 0;

			var half = denominator / 2;
			return numerator >= 0
				? (numerator + half) / denominator
				: -((-numerator + half) / denominator);
		}

		// Open / fallback formation: legacy box layout centered on click, oriented along the
		// centroid→target axis. This is the v0 behavior, preserved for clicks where no cover
		// signal warrants a smarter strategy.
		CPos[] ComputeBoxSlots(Map map, CPos clickCell, WPos targetPos, Actor[] sortedActors,
			int colSpacing, int rowSpacing)
		{
			var n = sortedActors.Length;

			// Move direction: centroid → click.
			long cx = 0;
			long cy = 0;
			for (var i = 0; i < n; i++)
			{
				cx += sortedActors[i].CenterPosition.X;
				cy += sortedActors[i].CenterPosition.Y;
			}

			cx /= n;
			cy /= n;

			var moveDirX = targetPos.X - (int)cx;
			var moveDirY = targetPos.Y - (int)cy;
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
					moveLen = 1024;
			}

			var perpX = -moveDirY;
			var perpY = moveDirX;

			var cols = (int)Math.Ceiling(Math.Sqrt(n * 2.0));
			cols = Math.Min(cols, n);
			cols = Math.Max(cols, 2);

			var slots = new CPos[n];
			for (var i = 0; i < n; i++)
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

				var slotPos = new WPos(targetPos.X + offsetX, targetPos.Y + offsetY, targetPos.Z);
				slots[i] = map.Clamp(map.CellContaining(slotPos));
			}

			return slots;
		}

		// SpreadInside: rank passable cover cells in the click neighborhood by CoverScore minus
		// chebyshev penalties to (a) the click and (b) the group's centroid. The group penalty
		// pulls slot picks toward the squad's side when the click is deep in cover the squad has
		// to traverse to reach — avoids piling assignments on far-side cells the pathfinder can't
		// reach through a dense cluster. Filtered through Mobile.CanStayInCell.
		CPos[] ComputeSpreadSlots(Map map, CPos clickCell, CPos groupCentroid, int n, int minSpacingCells, Mobile subjectMobile)
		{
			var radius = info.SpreadSearchRadius;
			var distancePenalty = info.SpreadDistancePenalty;
			var groupPenalty = info.SpreadGroupPenalty;
			var candidates = new List<(int Effective, int RawScore, int Chebyshev, CPos Cell)>();

			for (var dy = -radius; dy <= radius; dy++)
			{
				for (var dx = -radius; dx <= radius; dx++)
				{
					var cell = new CPos(clickCell.X + dx, clickCell.Y + dy);
					var raw = CoverScore(map, cell);
					if (raw <= 0)
						continue;

					if (info.FilterByPathability && subjectMobile != null && !subjectMobile.CanStayInCell(cell))
						continue;

					var cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
					var groupCheb = Math.Max(Math.Abs(cell.X - groupCentroid.X), Math.Abs(cell.Y - groupCentroid.Y));
					var effective = raw - cheb * distancePenalty - groupCheb * groupPenalty;
					candidates.Add((effective, raw, cheb, cell));
				}
			}

			candidates.Sort((a, b) =>
			{
				if (b.Effective != a.Effective)
					return b.Effective.CompareTo(a.Effective);
				if (a.Chebyshev != b.Chebyshev)
					return a.Chebyshev.CompareTo(b.Chebyshev);
				if (a.Cell.X != b.Cell.X)
					return a.Cell.X.CompareTo(b.Cell.X);
				return a.Cell.Y.CompareTo(b.Cell.Y);
			});

			var slots = new List<CPos>(n);
			foreach (var (_, _, _, cell) in candidates)
			{
				if (slots.Count >= n)
					break;

				var ok = true;
				foreach (var s in slots)
				{
					if (Math.Max(Math.Abs(cell.X - s.X), Math.Abs(cell.Y - s.Y)) < minSpacingCells)
					{
						ok = false;
						break;
					}
				}

				if (ok)
					slots.Add(cell);
			}

			// Second pass: if we ran out of well-spaced candidates, relax the spacing constraint
			// and pick more cover cells. Better to bunch up in cover than spread out into open
			// ground.
			if (slots.Count < n)
			{
				foreach (var (_, _, _, cell) in candidates)
				{
					if (slots.Count >= n)
						break;

					if (slots.Contains(cell))
						continue;

					slots.Add(cell);
				}
			}

			// Last resort: pad with the click cell itself.
			while (slots.Count < n)
				slots.Add(clickCell);

			return slots.ToArray();
		}

		// EdgeLine: place units in a single line perpendicular to the density gradient, anchored
		// one cell along the gradient direction (i.e. just inside the cover edge). The line is
		// oriented so units face *out* of the cover toward open ground.
		CPos[] ComputeEdgeLineSlots(Map map, CPos clickCell, int gradXCells, int gradYCells,
			int n, int colSpacing, Mobile subjectMobile)
		{
			var gradLenSq = gradXCells * gradXCells + gradYCells * gradYCells;
			if (gradLenSq == 0)
				return ComputeOpenLine(map, clickCell, n, colSpacing);

			var gradLen = Math.Sqrt(gradLenSq);
			var unitX = gradXCells / gradLen;
			var unitY = gradYCells / gradLen;

			// Perpendicular axis (90° CCW): (-unitY, unitX). Either direction works since the
			// line is symmetric — pick one consistently.
			var perpUX = -unitY;
			var perpUY = unitX;

			// Advance scales with gradient magnitude so the line lands AT the cover, not just one
			// cell toward it. A click 3 cells from cover with 100% advance puts the anchor 3
			// cells along the gradient — right at the centroid of nearby density.
			var advance = gradLen * info.EdgeAdvancePercent / 100.0;
			var anchorX = clickCell.X + (int)Math.Round(unitX * advance);
			var anchorY = clickCell.Y + (int)Math.Round(unitY * advance);

			var spacingCells = colSpacing / 1024.0;

			var slots = new CPos[n];
			for (var i = 0; i < n; i++)
			{
				var t = (2.0 * i - (n - 1)) * 0.5 * spacingCells;
				var x = anchorX + (int)Math.Round(perpUX * t);
				var y = anchorY + (int)Math.Round(perpUY * t);
				var ideal = map.Clamp(new CPos(x, y));

				// Pathability nudge: if the ideal line cell is impassable, walk back along the
				// gradient toward the click until we find a passable cell or run out of slack.
				slots[i] = NudgeToPassable(map, ideal, -unitX, -unitY, subjectMobile);
			}

			return slots;
		}

		// Walk up to 3 cells in (dx, dy) direction looking for a cell the subject can park on.
		// Returns the original cell if no passable alternative is found within range.
		static CPos NudgeToPassable(Map map, CPos start, double dx, double dy, Mobile subjectMobile)
		{
			if (subjectMobile == null)
				return start;

			if (subjectMobile.CanStayInCell(start))
				return start;

			for (var step = 1; step <= 3; step++)
			{
				var cand = map.Clamp(new CPos(
					start.X + (int)Math.Round(dx * step),
					start.Y + (int)Math.Round(dy * step)));
				if (subjectMobile.CanStayInCell(cand))
					return cand;
			}

			return start;
		}

		// Approach: the squad is far from a cover click. Walk from the group's centroid toward
		// the click along the direction vector; find the first cell where CoverScore transitions
		// from 0 to positive (the cover boundary). Place units in a line perpendicular to the
		// approach direction at that boundary cell. This produces a defensive line that the
		// squad can actually reach in one continuous march — no path through dense cover needed.
		CPos[] ComputeApproachSlots(Map map, CPos clickCell, CPos groupCentroid, int n, int colSpacing, Mobile subjectMobile)
		{
			var dxCells = clickCell.X - groupCentroid.X;
			var dyCells = clickCell.Y - groupCentroid.Y;
			var distCells = Math.Sqrt(dxCells * dxCells + dyCells * dyCells);

			if (distCells < 1)
				return ComputeOpenLine(map, clickCell, n, colSpacing);

			var unitX = dxCells / distCells;
			var unitY = dyCells / distCells;

			// Walk from group toward click; the boundary is the first passable cell where
			// CoverScore > 0 (i.e. cell has density-bearing neighbors). Step in unit increments
			// up to the click distance.
			CPos boundary = clickCell;
			var maxSteps = (int)Math.Ceiling(distCells);
			for (var step = 1; step <= maxSteps; step++)
			{
				var sx = groupCentroid.X + (int)Math.Round(unitX * step);
				var sy = groupCentroid.Y + (int)Math.Round(unitY * step);
				var cand = map.Clamp(new CPos(sx, sy));
				if (CoverScore(map, cand) > 0)
				{
					boundary = cand;
					break;
				}
			}

			// Perpendicular (rotate 90° CCW): (-unitY, unitX).
			var perpUX = -unitY;
			var perpUY = unitX;

			var spacingCells = colSpacing / 1024.0;
			var slots = new CPos[n];
			for (var i = 0; i < n; i++)
			{
				var t = (2.0 * i - (n - 1)) * 0.5 * spacingCells;
				var x = boundary.X + (int)Math.Round(perpUX * t);
				var y = boundary.Y + (int)Math.Round(perpUY * t);
				var ideal = map.Clamp(new CPos(x, y));

				// Nudge back toward group if line cell is impassable.
				slots[i] = NudgeToPassable(map, ideal, -unitX, -unitY, subjectMobile);
			}

			return slots;
		}

		// Fallback when EdgeLine has no gradient: place a horizontal line through the click.
		static CPos[] ComputeOpenLine(Map map, CPos clickCell, int n, int colSpacing)
		{
			var spacingCells = colSpacing / 1024.0;
			var slots = new CPos[n];
			for (var i = 0; i < n; i++)
			{
				var t = (2.0 * i - (n - 1)) * 0.5 * spacingCells;
				var x = clickCell.X + (int)Math.Round(t);
				slots[i] = map.Clamp(new CPos(x, clickCell.Y));
			}

			return slots;
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

			var map = subject.World.Map;
			var targetPos = individualOrder.Target.CenterPosition;
			var clickCell = map.Clamp(map.CellContaining(targetPos));

			// PITFALL: cohesion mode is read from the SUBJECT not aggregated. Mixed-mode groups
			// see inconsistent spacing — each actor uses its own mode and recomputes. In practice
			// grouped units share a mode, so this is acceptable for v1.
			var autoTarget = subject.TraitOrDefault<AutoTarget>();
			var mode = autoTarget?.CohesionValue ?? CohesionMode.Loose;
			GetSpacing(mode, out var colSpacing, out var rowSpacing);

			var intent = ClassifyIntent(map, clickCell, out var gradX, out var gradY);
			var subjectMobile = subject.TraitOrDefault<Mobile>();

			// Group centroid (used by SpreadInside to bias slot picks toward the squad's side,
			// and to detect the Approach case where the squad is far from a cover click).
			long groupCx = 0;
			long groupCy = 0;
			for (var i = 0; i < n; i++)
			{
				groupCx += validActors[i].CenterPosition.X;
				groupCy += validActors[i].CenterPosition.Y;
			}

			var groupCentroid = map.Clamp(map.CellContaining(new WPos(
				(int)(groupCx / n), (int)(groupCy / n), 0)));

			// Reclassify SpreadInside as Approach when the group is well separated from the
			// click. Pathfinding through dense cover usually fails, so anchoring the formation at
			// the boundary between group and click delivers a cleaner result.
			if (intent == Intent.SpreadInside)
			{
				var groupClickCheb = Math.Max(
					Math.Abs(clickCell.X - groupCentroid.X),
					Math.Abs(clickCell.Y - groupCentroid.Y));
				if (groupClickCheb > info.ApproachGroupDistanceCells)
					intent = Intent.Approach;
			}

			CPos[] slots;
			switch (intent)
			{
				case Intent.SpreadInside:
					var minSpacingCells = Math.Max(1, colSpacing / 1024);
					slots = ComputeSpreadSlots(map, clickCell, groupCentroid, n, minSpacingCells, subjectMobile);
					break;

				case Intent.EdgeLine:
					slots = ComputeEdgeLineSlots(map, clickCell, gradX, gradY, n, colSpacing, subjectMobile);
					break;

				case Intent.Approach:
					slots = ComputeApproachSlots(map, clickCell, groupCentroid, n, colSpacing, subjectMobile);
					break;

				case Intent.Open:
				default:
					slots = ComputeBoxSlots(map, clickCell, targetPos, validActors, colSpacing, rowSpacing);
					break;
			}

			if (idx >= slots.Length)
				return individualOrder;

			// Remember the assigned slot on the subject so the leash (CohesionSlotMemory) can
			// walk it back to position if it gets nudged out by a passing unit.
			subject.TraitOrDefault<CohesionSlotMemory>()?.Assign(slots[idx], subject.World.WorldTick);

			return individualOrder.WithTarget(Target.FromCell(subject.World, slots[idx]));
		}
	}
}
