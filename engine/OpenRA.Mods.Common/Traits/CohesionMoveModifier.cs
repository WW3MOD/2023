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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Offsets group move targets based on each unit's CohesionMode.",
		"Tight: converge on target. Loose: 2 staggered rows. Spread: single wide line.")]
	public class CohesionMoveModifierInfo : TraitInfo
	{
		[Desc("Percentage of perpendicular offset to preserve for Loose mode.")]
		public readonly int LooseScalePercent = 50;

		[Desc("Percentage of perpendicular offset to preserve for Spread mode.")]
		public readonly int SpreadScalePercent = 100;

		[Desc("Max perpendicular half-width in cells for Loose mode.")]
		public readonly int LooseSpreadCells = 2;

		[Desc("Max perpendicular half-width in cells for Spread mode.")]
		public readonly int SpreadSpreadCells = 4;

		[Desc("Depth between front and back row in cells (Loose stagger).")]
		public readonly int LooseRowDepth = 2;

		[Desc("Minimum move distance in cells below which cohesion offset is skipped.")]
		public readonly int MinMoveCells = 2;

		public override object Create(ActorInitializer init) { return new CohesionMoveModifier(this); }
	}

	public class CohesionMoveModifier : IModifyGroupOrder
	{
		readonly CohesionMoveModifierInfo info;

		public CohesionMoveModifier(CohesionMoveModifierInfo info)
		{
			this.info = info;
		}

		/// <summary>
		/// Apply cohesion offset to a move target for an individual unit.
		/// Uses perpendicular projection: each unit's left/right offset from the
		/// movement axis is preserved, creating a natural line formation.
		/// Loose mode adds 2-row stagger with depth offset.
		/// </summary>
		public static CPos ApplyCohesionOffset(Actor self, CPos targetCell)
		{
			var autoTarget = self.TraitOrDefault<AutoTarget>();
			if (autoTarget == null)
				return targetCell;

			var mode = autoTarget.CohesionValue;
			if (mode == CohesionMode.Tight)
				return targetCell;

			var modifier = self.World.WorldActor.TraitOrDefault<CohesionMoveModifier>();
			if (modifier == null)
				return targetCell;

			var mInfo = modifier.info;

			int scalePct, maxHalfWidthWDist;
			switch (mode)
			{
				case CohesionMode.Loose:
					scalePct = mInfo.LooseScalePercent;
					maxHalfWidthWDist = mInfo.LooseSpreadCells * 1024;
					break;
				case CohesionMode.Spread:
					scalePct = mInfo.SpreadScalePercent;
					maxHalfWidthWDist = mInfo.SpreadSpreadCells * 1024;
					break;
				default:
					return targetCell;
			}

			if (maxHalfWidthWDist == 0)
				return targetCell;

			var targetPos = self.World.Map.CenterOfCell(targetCell);
			var unitPos = self.CenterPosition;

			// Movement direction vector (target - unit)
			var moveDirX = targetPos.X - unitPos.X;
			var moveDirY = targetPos.Y - unitPos.Y;

			// If too close, perpendicular is unreliable — skip
			var minDist = mInfo.MinMoveCells * 1024;
			var moveLenSq = (long)moveDirX * moveDirX + (long)moveDirY * moveDirY;
			if (moveLenSq < (long)minDist * minDist)
				return targetCell;

			// Perpendicular direction (90° CCW rotation of moveDir)
			var perpX = -moveDirY;
			var perpY = moveDirX;

			// |perpDir| == |moveDir|
			var perpLen = (int)Exts.ISqrt(moveLenSq);
			if (perpLen == 0)
				return targetCell;

			// Project unit's offset from target onto perpendicular axis
			// dot(unitPos - targetPos, perpDir)
			var toUnitX = unitPos.X - targetPos.X;
			var toUnitY = unitPos.Y - targetPos.Y;
			var dot = (long)toUnitX * perpX + (long)toUnitY * perpY;

			// Signed perpendicular component in WDist
			var perpComponent = (int)(dot / perpLen);

			// Scale by cohesion factor
			var scaled = perpComponent * scalePct / 100;

			// Clamp to max half-width
			if (scaled > maxHalfWidthWDist)
				scaled = maxHalfWidthWDist;
			else if (scaled < -maxHalfWidthWDist)
				scaled = -maxHalfWidthWDist;

			// Depth stagger for Loose mode: alternate units into back row
			var depthWDist = 0;
			if (mode == CohesionMode.Loose && self.ActorID % 2 == 1)
			{
				// Back row: push behind front row along negative movement direction
				depthWDist = -(mInfo.LooseRowDepth * 1024);

				// Stagger sideways by half a cell for the checkerboard pattern
				scaled += 512;
			}

			// Compute final offset vector
			// Perpendicular component: along normalized perpDir
			// Depth component: along normalized moveDir (negative = behind)
			var offsetX = (int)((long)scaled * perpX / perpLen);
			var offsetY = (int)((long)scaled * perpY / perpLen);

			if (depthWDist != 0)
			{
				// moveDir normalized = (moveDirX, moveDirY) / perpLen (same length)
				offsetX += (int)((long)depthWDist * moveDirX / perpLen);
				offsetY += (int)((long)depthWDist * moveDirY / perpLen);
			}

			// If offset is negligible, skip
			if (Math.Abs(offsetX) < 256 && Math.Abs(offsetY) < 256)
				return targetCell;

			var newPos = new WPos(targetPos.X + offsetX, targetPos.Y + offsetY, targetPos.Z);
			var newCell = self.World.Map.CellContaining(newPos);
			return self.World.Map.Clamp(newCell);
		}

		// IModifyGroupOrder: for AI grouped orders (SquadManager etc.)
		Order IModifyGroupOrder.ModifyGroupOrder(Order individualOrder, Actor subject, Actor[] allGroupedActors)
		{
			if (subject == null || subject.IsDead || !subject.IsInWorld)
				return individualOrder;

			var orderString = individualOrder.OrderString;
			if (orderString != "Move" && orderString != "AttackMove")
				return individualOrder;

			var targetPos = individualOrder.Target.CenterPosition;
			var targetCell = subject.World.Map.CellContaining(targetPos);
			var newCell = ApplyCohesionOffset(subject, targetCell);

			if (newCell == targetCell)
				return individualOrder;

			return individualOrder.WithTarget(Target.FromCell(subject.World, newCell));
		}
	}
}
