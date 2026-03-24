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
		[Desc("Number of spread slots (determines granularity of spread positions).")]
		public readonly int SpreadSlots = 16;

		[Desc("Spread radius in cells for Tight mode.")]
		public readonly int TightSpreadCells = 0;

		[Desc("Spread radius in cells for Loose mode.")]
		public readonly int LooseSpreadCells = 2;

		[Desc("Spread radius in cells for Spread mode.")]
		public readonly int SpreadSpreadCells = 4;

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
		/// Spreads units in a ring around the target based on their ActorID.
		/// Each unit gets a deterministic position on a circle around the target,
		/// so different units go to different cells even though they share the same order.
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

			int spreadCells;
			switch (mode)
			{
				case CohesionMode.Loose:
					spreadCells = mInfo.LooseSpreadCells;
					break;
				case CohesionMode.Spread:
					spreadCells = mInfo.SpreadSpreadCells;
					break;
				default:
					return targetCell;
			}

			if (spreadCells == 0)
				return targetCell;

			// Use ActorID to deterministically assign a position on a ring around the target.
			// Different units get different slots so they spread out.
			var slots = mInfo.SpreadSlots;
			var slot = (int)(self.ActorID % (uint)slots);
			var angle = slot * 2.0 * Math.PI / slots;

			// Calculate offset in WDist (1 cell = 1024)
			var radius = spreadCells * 1024;
			var offsetX = (int)(Math.Cos(angle) * radius);
			var offsetY = (int)(Math.Sin(angle) * radius);

			// Apply offset to target
			var targetPos = self.World.Map.CenterOfCell(targetCell);
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
