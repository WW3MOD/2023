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

		[Desc("Human-only 'dispersed' Spread column spacing (WDist), used for grouped moves a human",
			"(non-bot) player issues in Spread cohesion. HAND-TUNED, deliberately NOT derived from any",
			"weapon's blast radius (DP-2) so it stays stable across weapon rebalances. 4096 = 4 cells",
			"centre-to-centre: with WW3MOD area warheads spreading on the order of 1.5-2 cells, a",
			"4-cell interval keeps the next unit outside the lethal ring of a single shell centred on",
			"its neighbour — the 'one shell, one casualty' feel — while staying a coherent squad (the",
			"count-aware footprint cap still bounds total span). Bot-owned Spread moves keep",
			"SpreadColSpacing unchanged, so the frozen AI benchmark is byte-identical.")]
		public readonly int SpreadHumanColSpacing = 4096;

		[Desc("Human-only 'dispersed' Spread row depth (WDist) for the box formation's front-to-back",
			"rows. Companion to SpreadHumanColSpacing; see that field. 3072 = 3 cells.")]
		public readonly int SpreadHumanRowSpacing = 3072;

		// Count-aware footprint caps (WDist). The box formation's per-slot offsets grow linearly
		// with unit count, so without a cap a large Spread group fans across the whole map (the
		// "spread way too much" bug). These bound the TOTAL span between the outermost slot centers
		// regardless of count: once (cols-1)*colSpacing would exceed MaxWidth, colSpacing shrinks so
		// the span stays at MaxWidth (same for depth via MaxDepth/rowSpacing). Concrete cell values:
		// Tight ~8x5, Loose ~11x6, Spread ~13x7 cells. Both these caps and the base spacings above
		// are monotonic across modes, so effective spacing stays Tight < Loose < Spread for every n.
		[Desc("Max total formation width (WDist, span between outermost slot centers) for Tight mode.")]
		public readonly int TightMaxWidth = 8192;

		[Desc("Max total formation depth (WDist, front-to-back span) for Tight mode.")]
		public readonly int TightMaxDepth = 5120;

		[Desc("Max total formation width (WDist) for Loose mode.")]
		public readonly int LooseMaxWidth = 11264;

		[Desc("Max total formation depth (WDist) for Loose mode.")]
		public readonly int LooseMaxDepth = 6144;

		[Desc("Max total formation width (WDist) for Spread mode.")]
		public readonly int SpreadMaxWidth = 13312;

		[Desc("Max total formation depth (WDist) for Spread mode.")]
		public readonly int SpreadMaxDepth = 7168;

		[Desc("Floor (WDist) the count-aware footprint cap will not shrink per-slot spacing below,",
			"so slots stay on distinct cells and never overlap. 1024 = one cell.")]
		public readonly int MinSlotSpacing = 1024;

		[Desc("Sample radius in cells used by the intent classifier to read density distribution",
			"around the click. A 4-cell radius means a 9x9 sample window.")]
		public readonly int IntentSampleRadius = 4;

		[Desc("Total density in the sample window below which the intent is Open (no usable cover).",
			"A single t01 trunk contributes 10, so 15 = ~1.5 trunks worth of nearby cover.")]
		public readonly int OpenDensityThreshold = 15;

		[Desc("Squared centroid offset (in cells) above which the intent is EdgeLine.",
			"9 = centroid more than ~3 cells from click → click is clearly off to one side of cover.",
			"Clicks within ~3 cells of cover centroid stay SpreadInside (cluster into the trees).",
			"Lower values trip EdgeLine more eagerly; higher values prefer SpreadInside. Raised from",
			"2 → 9 on 260513 — SpreadInside delivers the clearer 'take cover in trees' formation,",
			"while EdgeLine fired too readily for clicks right at the cluster edge and produced",
			"a perpendicular line that looked indistinguishable from the legacy directional box.")]
		public readonly int EdgeOffsetThresholdCellsSq = 9;

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

		[Desc("Search radius in cells around each ideal line position when EdgeLine/Approach lay",
			"their slots. 2 = a 5x5 window — each unit can deviate up to 2 cells from the geometric",
			"line to find better cover. Larger = more cover-bias but slot positions wander further",
			"from the line shape.")]
		public readonly int LineSlotSearchRadius = 2;

		[Desc("Human-only widened cover-search radius (cells) for Loose cohesion (DP-3, cover-first).",
			"When a human (non-bot) player issues a grouped move in Loose ('fight from cover'), each",
			"line slot searches this radius for cover instead of LineSlotSearchRadius, and cover is",
			"allowed to win over line shape freely: a unit takes any strictly-better reachable cover",
			"cell even if that bends the line. 4 = a 9x9 window. Where no cover exists the line stays",
			"clean (we never degrade a formation on purpose). Bot-owned moves keep LineSlotSearchRadius,",
			"so the frozen AI benchmark is byte-identical.")]
		public readonly int LooseHumanCoverSearchRadius = 4;

		[Desc("Distance penalty (per chebyshev cell from the ideal line position) when ranking",
			"candidate slots for EdgeLine/Approach. Higher = stick closer to the geometric line.",
			"Lower = more aggressive snap onto cover even if it bends the line. 5 means a candidate",
			"2 cells off-line needs CoverScore 10 to beat an on-line candidate with zero cover.")]
		public readonly int LineSlotDistancePenalty = 5;

		[Desc("Treeline detection: the cover distribution in the classifier window is treated as a",
			"line (→ EdgeLine, laid ALONG the cover) when its major spread axis is at least this many",
			"times the cross axis. A round blob has ratio ~1 and stays SpreadInside; a treeline has a",
			"much larger ratio. 2.5 = major axis spread 2.5x the cross axis.")]
		public readonly float TreelineAnisotropyRatio = 2.5f;

		[Desc("Treeline detection: minimum major-axis spread (variance, in cells²) before a click is",
			"treated as a treeline. Guards against calling a tiny 2-3 trunk cluster a 'line'. 2 ≈ the",
			"cover fills a ~3-cell-long band along its major axis.")]
		public readonly float TreelineMinSpreadSq = 2f;

		public override object Create(ActorInitializer init) { return new CohesionMoveModifier(this); }
	}

	public class CohesionMoveModifier : IModifyGroupOrder
	{
		readonly CohesionMoveModifierInfo info;

		// Per-order memoization of the slot layout + nearest-slot matching. ModifyGroupOrder is
		// invoked once per subject, but UnitOrders.ProcessOrder dispatches all N subjects of a single
		// grouped order back-to-back on the sim thread, and the full slot array + matching are
		// provably identical across those N calls. We compute them for the FIRST subject and let the
		// rest read their row — turning N redundant O(n²·log n) matchings into one. The cache is a
		// pure memo keyed on deterministic sim state (see TryReadCache); a different order/tick/group/
		// mode misses the key and recomputes, so nothing leaks across orders and no RNG is involved.
		uint[] cacheActorIds;
		CPos[] cacheAssignedByIdx;
		int cacheTick;
		CPos cacheClick;
		CohesionMode cacheMode;
		string cacheOrder;

		public CohesionMoveModifier(CohesionMoveModifierInfo info)
		{
			this.info = info;
		}

		enum Intent { Open, SpreadInside, EdgeLine, Approach }

		void GetSpacing(CohesionMode mode, bool isHuman, out int colSpacing, out int rowSpacing)
		{
			switch (mode)
			{
				case CohesionMode.Tight:
					colSpacing = info.TightColSpacing;
					rowSpacing = info.TightRowSpacing;
					return;
				case CohesionMode.Spread:
					// DP-2: humans get the hand-tuned 'dispersed' interval; bots keep the frozen values.
					colSpacing = isHuman ? info.SpreadHumanColSpacing : info.SpreadColSpacing;
					rowSpacing = isHuman ? info.SpreadHumanRowSpacing : info.SpreadRowSpacing;
					return;
				default:
					colSpacing = info.LooseColSpacing;
					rowSpacing = info.LooseRowSpacing;
					return;
			}
		}

		void GetMaxExtent(CohesionMode mode, out int maxWidth, out int maxDepth)
		{
			switch (mode)
			{
				case CohesionMode.Tight:
					maxWidth = info.TightMaxWidth;
					maxDepth = info.TightMaxDepth;
					return;
				case CohesionMode.Spread:
					maxWidth = info.SpreadMaxWidth;
					maxDepth = info.SpreadMaxDepth;
					return;
				default:
					maxWidth = info.LooseMaxWidth;
					maxDepth = info.LooseMaxDepth;
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
		// in cells (signed) so the EdgeLine branch can use it as the gradient direction, plus a
		// non-zero (lineAlongX, lineAlongY) when the cover forms a LINE (treeline) — the direction
		// units should be strung ALONG. Both are 0 when no line is detected.
		Intent ClassifyIntent(Map map, CPos clickCell, out int centroidDxCells, out int centroidDyCells,
			out int lineAlongX, out int lineAlongY)
		{
			var sampleRadius = info.IntentSampleRadius;
			var totalDensity = 0;
			var weightedX = 0;
			var weightedY = 0;

			// Second raw moments (about the window centre) for the covariance / anisotropy test.
			long sumXX = 0;
			long sumYY = 0;
			long sumXY = 0;

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
					sumXX += (long)dx * dx * d;
					sumYY += (long)dy * dy * d;
					sumXY += (long)dx * dy * d;
				}
			}

			centroidDxCells = 0;
			centroidDyCells = 0;
			lineAlongX = 0;
			lineAlongY = 0;

			if (totalDensity < info.OpenDensityThreshold)
				return Intent.Open;

			// Integer-round the centroid offset so the gradient is whole-cell stable.
			centroidDxCells = RoundDiv(weightedX, totalDensity);
			centroidDyCells = RoundDiv(weightedY, totalDensity);

			// Covariance of the density about its centroid (parallel-axis theorem: Cov = E[XY] - E[X]E[Y]).
			var mx = weightedX / (double)totalDensity;
			var my = weightedY / (double)totalDensity;
			var cxx = sumXX / (double)totalDensity - mx * mx;
			var cyy = sumYY / (double)totalDensity - my * my;
			var cxy = sumXY / (double)totalDensity - mx * my;

			// Eigenvalues of the symmetric covariance [[cxx,cxy],[cxy,cyy]]: lambda = tr/2 ± sqrt(...).
			// lambda1 is the spread along the major axis, lambda2 along the cross axis (both in cells²).
			var tr = cxx + cyy;
			var disc = Math.Sqrt(Math.Max(0.0, tr * tr / 4.0 - (cxx * cyy - cxy * cxy)));
			var lambda1 = tr / 2.0 + disc;
			var lambda2 = tr / 2.0 - disc;

			// Treeline: the cover is elongated (major spread dominates the cross axis) AND has real
			// length. Route it to EdgeLine even when the centroid offset is ~0 (a click centred ON a
			// treeline has symmetric density → tiny offset → would otherwise fall to SpreadInside and
			// scatter). Lay the line along the major eigenvector.
			if (lambda1 >= info.TreelineMinSpreadSq && lambda1 >= info.TreelineAnisotropyRatio * Math.Max(lambda2, 0.0))
			{
				// Major eigenvector of the covariance. For cxy≈0 the axes are aligned; pick the
				// larger-variance axis. Otherwise (lambda1 - cyy, cxy) is the unnormalised major axis.
				double vx, vy;
				if (Math.Abs(cxy) > 1e-6)
				{
					vx = lambda1 - cyy;
					vy = cxy;
				}
				else if (cxx >= cyy)
				{
					vx = 1.0;
					vy = 0.0;
				}
				else
				{
					vx = 0.0;
					vy = 1.0;
				}

				var vlen = Math.Sqrt(vx * vx + vy * vy);
				if (vlen > 0)
				{
					// Scale to a small integer cell-space direction (×4 then round) — the layout code
					// re-normalises, it just needs a stable non-zero direction.
					lineAlongX = (int)Math.Round(vx / vlen * 4.0);
					lineAlongY = (int)Math.Round(vy / vlen * 4.0);
					if (lineAlongX == 0 && lineAlongY == 0)
						lineAlongX = 1;

					return Intent.EdgeLine;
				}
			}

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
			int colSpacing, int rowSpacing, int maxWidth, int maxDepth)
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

			var rows = (n + cols - 1) / cols;

			// Count-aware footprint cap: shrink per-slot spacing so the box's total width/depth stay
			// under the per-mode maximum, no matter how many units. The raw offsets below grow
			// linearly with cols/rows (bounded only by map.Clamp), so a large Spread group would
			// otherwise fan across the whole map. Because both the base spacing and these caps are
			// monotonic across modes, effective spacing stays Tight < Loose < Spread for every n.
			if (cols > 1 && (long)(cols - 1) * colSpacing > maxWidth)
				colSpacing = Math.Max(info.MinSlotSpacing, maxWidth / (cols - 1));
			if (rows > 1 && (long)(rows - 1) * rowSpacing > maxDepth)
				rowSpacing = Math.Max(info.MinSlotSpacing, maxDepth / (rows - 1));

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
		// one cell along the gradient direction (i.e. just inside the cover edge). Per-slot
		// CoverScore-aware bidding via LayCoverAwareLine, so units actually land behind trunks
		// instead of in a dead-straight geometric line that ignores nearby cover.
		CPos[] ComputeEdgeLineSlots(Map map, CPos clickCell, int gradXCells, int gradYCells,
			int lineAlongX, int lineAlongY, int centroidDxCells, int centroidDyCells,
			int n, int colSpacing, Mobile subjectMobile, int coverRadius, bool coverFirst)
		{
			// Treeline path: the classifier detected an elongated cover distribution. String the
			// units ALONG the cover's major axis (lineAlong), anchored at the cover centroid, so a
			// click on/near a treeline yields a line strung down the treeline rather than a scatter.
			if (lineAlongX != 0 || lineAlongY != 0)
			{
				var alongLen = Math.Sqrt((double)lineAlongX * lineAlongX + (double)lineAlongY * lineAlongY);
				var alongUX = lineAlongX / alongLen;
				var alongUY = lineAlongY / alongLen;
				var anchor = map.Clamp(new CPos(clickCell.X + centroidDxCells, clickCell.Y + centroidDyCells));

				// LayCoverAwareLine strings slots along the axis perpendicular to its `forward` arg.
				// To lay ALONG (alongUX,alongUY) we pass forward = its perpendicular (alongUY,-alongUX);
				// that perpendicular also points across the treeline, a sane "into cover" nudge dir.
				return LayCoverAwareLine(map, anchor, alongUY, -alongUX, n, colSpacing, subjectMobile, coverRadius, coverFirst);
			}

			var gradLenSq = gradXCells * gradXCells + gradYCells * gradYCells;
			if (gradLenSq == 0)
				return ComputeOpenLine(map, clickCell, n, colSpacing);

			var gradLen = Math.Sqrt(gradLenSq);
			var unitX = gradXCells / gradLen;
			var unitY = gradYCells / gradLen;

			// Advance scales with gradient magnitude so the line lands AT the cover, not just one
			// cell toward it. A click 3 cells from cover with 100% advance puts the anchor 3
			// cells along the gradient — right at the centroid of nearby density.
			var advance = gradLen * info.EdgeAdvancePercent / 100.0;
			var anchorX = clickCell.X + (int)Math.Round(unitX * advance);
			var anchorY = clickCell.Y + (int)Math.Round(unitY * advance);

			return LayCoverAwareLine(map, new CPos(anchorX, anchorY), unitX, unitY, n, colSpacing, subjectMobile, coverRadius, coverFirst);
		}

		// Lay N slots in a line perpendicular to (forwardX, forwardY), anchored at `anchor`. For
		// each ideal line position, search a small neighborhood and pick the best-CoverScore
		// passable cell, with min-spacing against earlier picks. `forward` is the direction toward
		// cover (away from the squad) — used for the pathability fallback when no neighborhood
		// pick is viable.
		CPos[] LayCoverAwareLine(Map map, CPos anchor, double forwardX, double forwardY,
			int n, int colSpacing, Mobile subjectMobile, int coverRadius, bool coverFirst)
		{
			// Perpendicular axis (90° CCW): (-forwardY, forwardX). Symmetric — direction is
			// arbitrary as long as slot ordering is consistent.
			var perpUX = -forwardY;
			var perpUY = forwardX;

			var spacingCells = colSpacing / 1024.0;
			var minSpacing = Math.Max(1, colSpacing / 1024);

			var slots = new CPos[n];
			var taken = new List<CPos>(n);

			for (var i = 0; i < n; i++)
			{
				var t = (2.0 * i - (n - 1)) * 0.5 * spacingCells;
				var idealX = anchor.X + (int)Math.Round(perpUX * t);
				var idealY = anchor.Y + (int)Math.Round(perpUY * t);
				var ideal = map.Clamp(new CPos(idealX, idealY));

				slots[i] = PickCoverSlotNear(map, ideal, subjectMobile, taken, minSpacing,
					-forwardX, -forwardY, coverRadius, coverFirst);
				taken.Add(slots[i]);
			}

			return slots;
		}

		// For an ideal line position, search a 5x5 window and pick the passable cell with the
		// highest score = CoverScore - chebyshev*LineSlotDistancePenalty.
		//
		// Cover beats geometry (problem §3 / behavior F): min-spacing is a soft constraint. We track
		// the best spacing-respecting cell AND, separately, the best cell that actually has cover
		// (ignoring min-spacing, but never stacking on an already-taken cell). If the spacing-clean
		// pick would leave the unit in the open while a cover cell is available nearby, we bend the
		// line and take the cover cell — a unit standing behind a trunk one cell too close beats a
		// unit ejected into open ground to keep the line straight. Falls back to NudgeToPassable
		// when nothing in the window is passable.
		CPos PickCoverSlotNear(Map map, CPos ideal, Mobile subjectMobile, List<CPos> taken,
			int minSpacing, double backX, double backY, int coverRadius, bool coverFirst)
		{
			var radius = coverRadius;
			var distancePenalty = info.LineSlotDistancePenalty;

			var bestScore = int.MinValue;
			var bestCell = ideal;
			var bestCover = 0;
			var found = false;

			// Relaxed-spacing best cover cell (only exact-overlap with a taken slot is disqualifying).
			var bestCoverScore = int.MinValue;
			var bestCoverCell = ideal;
			var bestCoverRaw = 0;
			var coverFound = false;

			for (var dy = -radius; dy <= radius; dy++)
			{
				for (var dx = -radius; dx <= radius; dx++)
				{
					var cand = map.Clamp(new CPos(ideal.X + dx, ideal.Y + dy));

					if (info.FilterByPathability && subjectMobile != null && !subjectMobile.CanStayInCell(cand))
						continue;

					var cover = CoverScore(map, cand);
					var cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
					var score = cover - cheb * distancePenalty;

					var minTaken = int.MaxValue;
					foreach (var s in taken)
					{
						var chebTaken = Math.Max(Math.Abs(cand.X - s.X), Math.Abs(cand.Y - s.Y));
						if (chebTaken < minTaken)
							minTaken = chebTaken;
					}

					// Relaxed cover candidate: has cover and isn't sitting exactly on a taken slot.
					if (cover > 0 && minTaken > 0 && (!coverFound || score > bestCoverScore))
					{
						bestCoverScore = score;
						bestCoverCell = cand;
						bestCoverRaw = cover;
						coverFound = true;
					}

					// Spacing-respecting candidate.
					if (minTaken >= minSpacing && (!found || score > bestScore))
					{
						bestScore = score;
						bestCell = cand;
						bestCover = cover;
						found = true;
					}
				}
			}

			// If the tidy pick has no cover but a cover cell is reachable, bend the line into cover.
			// DP-3 cover-first (human Loose): also bend when a strictly-better cover cell is reachable,
			// even if the tidy pick already had some cover — every unit takes the best cover it can,
			// line shape yielding to positioning. The coverFirst term is false everywhere else, so the
			// bot / human-Spread condition reduces to exactly the prior "bend only when tidy has none".
			if (coverFound && (!found || bestCover <= 0 || (coverFirst && bestCoverRaw > bestCover)))
				return bestCoverCell;

			if (found)
				return bestCell;

			// Nothing in the neighborhood was passable (or everything collided with taken slots).
			// Fall back to walking back along the gradient as before.
			return NudgeToPassable(map, ideal, backX, backY, subjectMobile);
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

		// Approach: the squad is far from a cover click. Walk BACKWARD from the click toward the
		// group's centroid; the boundary is the first cell from the click side that has cover.
		// That cell is the cover patch closest to the destination — i.e. the one the squad is
		// heading TO. Place units in a line perpendicular to the approach direction there.
		//
		// PITFALL (2026-05): the previous implementation walked group→click and stopped at the
		// first CoverScore>0 cell. When the squad was already adjacent to cover (e.g. spawn-camped
		// next to a tree cluster), step=1 tripped immediately and slots anchored right next to
		// the starting position — units never reached far clicks. Walking click→group reverses
		// that bias and keeps the "approach" intent honest.
		CPos[] ComputeApproachSlots(Map map, CPos clickCell, CPos groupCentroid, int n, int colSpacing, Mobile subjectMobile,
			int coverRadius, bool coverFirst)
		{
			var dxCells = clickCell.X - groupCentroid.X;
			var dyCells = clickCell.Y - groupCentroid.Y;
			var distCells = Math.Sqrt(dxCells * dxCells + dyCells * dyCells);

			if (distCells < 1)
				return ComputeOpenLine(map, clickCell, n, colSpacing);

			var unitX = dxCells / distCells;
			var unitY = dyCells / distCells;

			// Walk back from click toward group. Start at the click itself (step=0) so a click
			// already in cover anchors at the click. If nothing along the path has cover, the
			// boundary stays at the click — Approach degenerates to an open line at destination,
			// which is the right behavior for a long march into open ground.
			CPos boundary = clickCell;
			var maxSteps = (int)Math.Ceiling(distCells);
			for (var step = 0; step <= maxSteps; step++)
			{
				var sx = clickCell.X - (int)Math.Round(unitX * step);
				var sy = clickCell.Y - (int)Math.Round(unitY * step);
				var cand = map.Clamp(new CPos(sx, sy));
				if (CoverScore(map, cand) > 0)
				{
					boundary = cand;
					break;
				}
			}

			return LayCoverAwareLine(map, boundary, unitX, unitY, n, colSpacing, subjectMobile, coverRadius, coverFirst);
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
			var tick = subject.World.WorldTick;

			// Stance-identity gate (PIPELINE 5). The three cohesion stances get distinct HUMAN-facing
			// identities (DP-1 Tight=vanilla, DP-2 Spread=dispersed, DP-3 Loose=cover-first), but
			// bot-owned grouped moves must stay byte-identical to preserve the frozen AI benchmark
			// (default AI cohesion is Loose — AutoTarget.InitialCohesionAI). isHuman mirrors the
			// existing human/AI seam (AutoTarget.Created/ResolveOrder) and reads only synced player
			// state (Owner.IsBot/Playable): no RNG, identical on every client, so it never desyncs.
			var isHuman = subject.Owner.Playable && !subject.Owner.IsBot;

			// DP-1: Tight = classic/vanilla for humans. ALL cohesion adjustments are OFF — the grouped
			// order passes through unmodified, exactly like stock OpenRA (every unit converges on the
			// click). Clear any stale slot leash first so a slot from a prior Loose/Spread order can't
			// drag the unit back. Placed before the cache so Tight-human never reads/writes it.
			if (isHuman && mode == CohesionMode.Tight)
			{
				subject.TraitOrDefault<CohesionSlotMemory>()?.Clear();
				return individualOrder;
			}

			// Cache hit: this order's matching was already computed by an earlier subject in the same
			// ProcessOrder dispatch. Read this subject's assigned cell and skip the whole pipeline.
			if (TryReadCache(validActors, tick, clickCell, mode, orderString, idx, out var cachedCell))
			{
				subject.TraitOrDefault<CohesionSlotMemory>()?.Assign(cachedCell, clickCell, tick, individualOrder.Queued);
				return individualOrder.WithTarget(Target.FromCell(subject.World, cachedCell));
			}

			GetSpacing(mode, isHuman, out var colSpacing, out var rowSpacing);
			GetMaxExtent(mode, out var maxWidth, out var maxDepth);

			var intent = ClassifyIntent(map, clickCell, out var gradX, out var gradY, out var lineAlongX, out var lineAlongY);
			var subjectMobile = subject.TraitOrDefault<Mobile>();

			// DP-3: Loose = cover-first for humans. Widen the per-slot cover search and let cover win
			// over line shape freely. Every other case (bots, human Spread) keeps LineSlotSearchRadius
			// and the tidy-first bend, so their line layouts are byte-identical to before.
			var coverRadius = isHuman && mode == CohesionMode.Loose ? info.LooseHumanCoverSearchRadius : info.LineSlotSearchRadius;
			var coverFirst = isHuman && mode == CohesionMode.Loose;

			// Hard span bound for line formations (symptom a). The box cap (1eb644de) lives inside
			// ComputeBoxSlots and never covered EdgeLine/Approach/OpenLine, whose width is (n-1)*spacing
			// and grows without limit — a large Spread group strings clear across the screen. Shrink
			// the per-slot spacing so the outermost slots stay within maxWidth, mirroring the box cap.
			var lineColSpacing = colSpacing;
			if (n > 1 && (long)(n - 1) * lineColSpacing > maxWidth)
				lineColSpacing = Math.Max(info.MinSlotSpacing, maxWidth / (n - 1));

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
					slots = ComputeEdgeLineSlots(map, clickCell, gradX, gradY, lineAlongX, lineAlongY,
						gradX, gradY, n, lineColSpacing, subjectMobile, coverRadius, coverFirst);
					break;

				case Intent.Approach:
					slots = ComputeApproachSlots(map, clickCell, groupCentroid, n, lineColSpacing, subjectMobile, coverRadius, coverFirst);
					break;

				case Intent.Open:
				default:
					slots = ComputeBoxSlots(map, clickCell, targetPos, validActors, colSpacing, rowSpacing, maxWidth, maxDepth);
					break;
			}

			if (idx >= slots.Length)
				return individualOrder;

			// Position-aware slot assignment for the WHOLE squad at once (deterministic nearest-
			// matching, replacing slots[idx]-by-ActorID which sends the lowest-ID unit to the leftmost
			// slot regardless of where it stands → units criss-cross). Computed once here and cached
			// for the order's remaining subjects.
			var matching = AssignAll(validActors, slots);

			var assignedByIdx = new CPos[n];
			for (var i = 0; i < n; i++)
			{
				var s = matching[i];
				assignedByIdx[i] = s >= 0 ? slots[s] : slots[Math.Min(i, slots.Length - 1)];
			}

			StoreCache(validActors, tick, clickCell, mode, orderString, assignedByIdx);

			var cell = assignedByIdx[idx];

			// Remember the assigned slot on the subject so the leash (CohesionSlotMemory) can
			// walk it back to position if it gets nudged out by a passing unit.
			subject.TraitOrDefault<CohesionSlotMemory>()?.Assign(cell, clickCell, tick, individualOrder.Queued);

			return individualOrder.WithTarget(Target.FromCell(subject.World, cell));
		}

		// Read the memoized per-idx assignment if the cache was filled for THIS exact order. The key
		// is the full deterministic identity of the grouped order: world tick, click cell, cohesion
		// mode, order string, and the ID-sorted actor set (compared element-wise, not hashed, so
		// distinct groups can never collide). All of these are identical on every client and in
		// replay, so a hit returns byte-identical data everywhere and a miss recomputes.
		bool TryReadCache(Actor[] sortedActors, int tick, CPos click, CohesionMode mode, string order,
			int idx, out CPos cell)
		{
			cell = default;
			if (cacheAssignedByIdx == null || cacheActorIds == null)
				return false;

			if (cacheTick != tick || cacheClick != click || cacheMode != mode || cacheOrder != order)
				return false;

			if (cacheActorIds.Length != sortedActors.Length || idx >= cacheAssignedByIdx.Length)
				return false;

			for (var i = 0; i < sortedActors.Length; i++)
				if (cacheActorIds[i] != sortedActors[i].ActorID)
					return false;

			cell = cacheAssignedByIdx[idx];
			return true;
		}

		void StoreCache(Actor[] sortedActors, int tick, CPos click, CohesionMode mode, string order,
			CPos[] assignedByIdx)
		{
			var ids = new uint[sortedActors.Length];
			for (var i = 0; i < ids.Length; i++)
				ids[i] = sortedActors[i].ActorID;

			cacheActorIds = ids;
			cacheAssignedByIdx = assignedByIdx;
			cacheTick = tick;
			cacheClick = click;
			cacheMode = mode;
			cacheOrder = order;
		}

		// Deterministic position-aware assignment of the whole squad to slots. Builds every (actor,
		// slot) distance edge and repeatedly claims the globally-shortest unclaimed edge (greedy
		// minimum matching), tie-breaking on actor index then slot index. Because validActors is
		// ActorID-sorted and slots are identical for a given order, the edge list and matching are a
		// pure function of the inputs WITHOUT any RNG — determinism (a non-negotiable) is preserved.
		// Returns actorSlot[i] = slot index for the actor at ID-sorted index i (-1 if unmatched).
		static int[] AssignAll(Actor[] sortedActors, CPos[] slots)
		{
			var n = sortedActors.Length;
			var slotCount = slots.Length;

			var edges = new List<(long DistSq, int Actor, int Slot)>(n * slotCount);
			for (var a = 0; a < n; a++)
			{
				var loc = sortedActors[a].Location;
				for (var s = 0; s < slotCount; s++)
				{
					var dx = (long)(loc.X - slots[s].X);
					var dy = (long)(loc.Y - slots[s].Y);
					edges.Add((dx * dx + dy * dy, a, s));
				}
			}

			edges.Sort((p, q) =>
			{
				if (p.DistSq != q.DistSq)
					return p.DistSq.CompareTo(q.DistSq);
				if (p.Actor != q.Actor)
					return p.Actor.CompareTo(q.Actor);
				return p.Slot.CompareTo(q.Slot);
			});

			var actorSlot = new int[n];
			for (var i = 0; i < n; i++)
				actorSlot[i] = -1;

			var slotTaken = new bool[slotCount];
			var assigned = 0;
			foreach (var (_, a, s) in edges)
			{
				if (assigned >= n)
					break;

				if (actorSlot[a] >= 0 || slotTaken[s])
					continue;

				actorSlot[a] = s;
				slotTaken[s] = true;
				assigned++;
			}

			return actorSlot;
		}
	}
}
