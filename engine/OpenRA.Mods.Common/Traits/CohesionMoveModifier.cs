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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Creates box formations for grouped move orders based on each unit's CohesionMode.",
		"Tight: close spacing. Loose: moderate spacing with row stagger. Spread: wide spacing.")]
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

		public override object Create(ActorInitializer init) { return new CohesionMoveModifier(this); }
	}

	public class CohesionMoveModifier : IModifyGroupOrder
	{
		readonly CohesionMoveModifierInfo info;

		public CohesionMoveModifier(CohesionMoveModifierInfo info)
		{
			this.info = info;
		}

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

		Order IModifyGroupOrder.ModifyGroupOrder(Order individualOrder, Actor subject, Actor[] allGroupedActors)
		{
			if (subject == null || subject.IsDead || !subject.IsInWorld)
				return individualOrder;

			var orderString = individualOrder.OrderString;
			if (orderString != "Move" && orderString != "AttackMove")
				return individualOrder;

			// Count valid actors and find our index (sorted by ActorID for stable ordering)
			var n = 0;
			for (var i = 0; i < allGroupedActors.Length; i++)
			{
				var a = allGroupedActors[i];
				if (a != null && !a.IsDead && a.IsInWorld)
					n++;
			}

			// No offset for single units — this is the key fix for single-unit displacement
			if (n <= 1)
				return individualOrder;

			// Build sorted valid actor list
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

			// Compute group centroid for formation orientation
			var centroidX = 0L;
			var centroidY = 0L;
			for (var i = 0; i < n; i++)
			{
				centroidX += validActors[i].CenterPosition.X;
				centroidY += validActors[i].CenterPosition.Y;
			}

			centroidX /= n;
			centroidY /= n;

			// Movement direction: centroid → target
			var moveDirX = targetPos.X - (int)centroidX;
			var moveDirY = targetPos.Y - (int)centroidY;
			var moveLenSq = (long)moveDirX * moveDirX + (long)moveDirY * moveDirY;
			int moveLen;

			// For very short moves (regroup/DoNow), use North as default orientation
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

			// Perpendicular direction (90° CCW rotation of movement direction)
			var perpX = -moveDirY;
			var perpY = moveDirX;

			// Grid dimensions: wide box formation (~2:1 width-to-depth ratio)
			var cols = (int)Math.Ceiling(Math.Sqrt(n * 2.0));
			cols = Math.Min(cols, n);
			cols = Math.Max(cols, 2);

			// Grid position for this unit
			var row = idx / cols;
			var col = idx % cols;
			var unitsInRow = Math.Min(cols, n - row * cols);

			// Per-unit spacing based on cohesion mode
			var autoTarget = subject.TraitOrDefault<AutoTarget>();
			var mode = autoTarget?.CohesionValue ?? CohesionMode.Loose;
			GetSpacing(mode, out var colSpacing, out var rowSpacing);

			// Center the row: (2*col - (unitsInRow-1)) * spacing / 2
			// Using integer math to avoid floating point
			var perpOffset = (2 * col - (unitsInRow - 1)) * colSpacing / 2;

			// Stagger odd rows by half column spacing for checkerboard pattern
			if (row % 2 == 1)
				perpOffset += colSpacing / 2;

			// Depth: rows behind the front line (negative = behind target toward centroid)
			var depthOffset = -row * rowSpacing;

			if (perpOffset == 0 && depthOffset == 0)
				return individualOrder;

			// Convert formation offsets to world coordinates
			// Perpendicular component: along perpDir (normalized by moveLen)
			var offsetX = (int)((long)perpOffset * perpX / moveLen);
			var offsetY = (int)((long)perpOffset * perpY / moveLen);

			// Depth component: along moveDir (normalized by moveLen)
			if (depthOffset != 0)
			{
				offsetX += (int)((long)depthOffset * moveDirX / moveLen);
				offsetY += (int)((long)depthOffset * moveDirY / moveLen);
			}

			// Skip negligible offsets
			if (Math.Abs(offsetX) < 256 && Math.Abs(offsetY) < 256)
				return individualOrder;

			var newPos = new WPos(targetPos.X + offsetX, targetPos.Y + offsetY, targetPos.Z);
			var newCell = subject.World.Map.CellContaining(newPos);
			newCell = subject.World.Map.Clamp(newCell);

			return individualOrder.WithTarget(Target.FromCell(subject.World, newCell));
		}
	}
}
