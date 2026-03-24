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
	[Desc("Offsets group move targets based on each unit's CohesionMode, so units spread out instead of converging.")]
	public class CohesionMoveModifierInfo : TraitInfo
	{
		[Desc("Fraction of relative offset preserved for Tight mode (0-100).")]
		public readonly int TightFactor = 20;

		[Desc("Fraction of relative offset preserved for Loose mode (0-100).")]
		public readonly int LooseFactor = 50;

		[Desc("Fraction of relative offset preserved for Spread mode (0-100).")]
		public readonly int SpreadFactor = 100;

		[Desc("Maximum offset distance in cells for Tight mode.")]
		public readonly int TightMaxCells = 1;

		[Desc("Maximum offset distance in cells for Loose mode.")]
		public readonly int LooseMaxCells = 3;

		[Desc("Maximum offset distance in cells for Spread mode.")]
		public readonly int SpreadMaxCells = 5;

		[Desc("Order strings to apply cohesion offsets to.")]
		public readonly string[] AffectedOrders = { "Move", "AttackMove" };

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
		/// Called from Mobile.ResolveOrder and AttackMove.ResolveOrder.
		/// The offset pushes the target away from the unit's current position toward
		/// where the unit "should be" to maintain formation spread.
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

			var info = modifier.info;

			int factor, maxCells;
			switch (mode)
			{
				case CohesionMode.Loose:
					factor = info.LooseFactor;
					maxCells = info.LooseMaxCells;
					break;
				case CohesionMode.Spread:
					factor = info.SpreadFactor;
					maxCells = info.SpreadMaxCells;
					break;
				default:
					return targetCell;
			}

			if (factor == 0)
				return targetCell;

			// Calculate offset: vector from target to unit's current position
			// This preserves the unit's "side" of the formation
			var targetPos = self.World.Map.CenterOfCell(targetCell);
			var offsetX = self.CenterPosition.X - targetPos.X;
			var offsetY = self.CenterPosition.Y - targetPos.Y;

			// Scale by factor
			offsetX = offsetX * factor / 100;
			offsetY = offsetY * factor / 100;

			// Cap to max distance
			var maxDist = maxCells * 1024;
			var offsetLengthSq = (long)offsetX * offsetX + (long)offsetY * offsetY;
			var maxDistSq = (long)maxDist * maxDist;

			if (offsetLengthSq > maxDistSq && offsetLengthSq > 0)
			{
				var len = (int)Math.Sqrt(offsetLengthSq);
				offsetX = offsetX * maxDist / len;
				offsetY = offsetY * maxDist / len;
			}

			// If offset is negligible (less than half a cell), skip
			if (Math.Abs(offsetX) < 512 && Math.Abs(offsetY) < 512)
				return targetCell;

			// Apply offset
			var newPos = new WPos(targetPos.X + offsetX, targetPos.Y + offsetY, targetPos.Z);
			var newCell = self.World.Map.CellContaining(newPos);
			return self.World.Map.Clamp(newCell);
		}

		// IModifyGroupOrder: for AI grouped orders (SquadManager etc.)
		Order IModifyGroupOrder.ModifyGroupOrder(Order individualOrder, Actor subject, Actor[] allGroupedActors)
		{
			if (subject == null || subject.IsDead || !subject.IsInWorld)
				return individualOrder;

			if (!info.AffectedOrders.Contains(individualOrder.OrderString))
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
