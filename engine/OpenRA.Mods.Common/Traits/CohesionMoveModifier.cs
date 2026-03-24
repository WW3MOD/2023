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

		Order IModifyGroupOrder.ModifyGroupOrder(Order individualOrder, Actor subject, Actor[] allGroupedActors)
		{
			// Guard against null/dead actors
			if (subject == null || subject.IsDead || !subject.IsInWorld)
				return individualOrder;

			// Only modify move-type orders
			if (!info.AffectedOrders.Contains(individualOrder.OrderString))
				return individualOrder;

			// Check if this unit has a cohesion mode set
			var autoTarget = subject.TraitOrDefault<AutoTarget>();
			if (autoTarget == null)
				return individualOrder;

			var mode = autoTarget.CohesionValue;

			// Tight with factor 0 means no offset — default behavior
			var factor = GetFactor(mode);
			if (factor == 0)
				return individualOrder;

			// Only compute offsets for groups of 2+
			var validActors = allGroupedActors.Where(a => a != null && !a.IsDead && a.IsInWorld).ToArray();
			if (validActors.Length < 2)
				return individualOrder;

			// Calculate group centroid
			var centroidX = 0L;
			var centroidY = 0L;
			foreach (var actor in validActors)
			{
				centroidX += actor.CenterPosition.X;
				centroidY += actor.CenterPosition.Y;
			}

			centroidX /= validActors.Length;
			centroidY /= validActors.Length;

			// Calculate this unit's offset from centroid
			var offsetX = subject.CenterPosition.X - (int)centroidX;
			var offsetY = subject.CenterPosition.Y - (int)centroidY;

			// Scale offset by cohesion factor
			offsetX = offsetX * factor / 100;
			offsetY = offsetY * factor / 100;

			// Cap to max distance
			var maxDist = GetMaxDistance(mode);
			var offsetLengthSq = (long)offsetX * offsetX + (long)offsetY * offsetY;
			var maxDistSq = (long)maxDist * maxDist;

			if (offsetLengthSq > maxDistSq && offsetLengthSq > 0)
			{
				var scale = (int)(maxDist * 1024 / (int)Math.Sqrt(offsetLengthSq));
				offsetX = offsetX * scale / 1024;
				offsetY = offsetY * scale / 1024;
			}

			// If offset is negligible (less than half a cell), skip
			if (Math.Abs(offsetX) < 512 && Math.Abs(offsetY) < 512)
				return individualOrder;

			// Apply offset to target position
			var targetPos = individualOrder.Target.CenterPosition;
			var newPos = new WPos(targetPos.X + offsetX, targetPos.Y + offsetY, targetPos.Z);

			// Clamp to map bounds
			var world = subject.World;
			var newCell = world.Map.CellContaining(newPos);
			newCell = world.Map.Clamp(newCell);

			return individualOrder.WithTarget(Target.FromCell(world, newCell));
		}

		int GetFactor(CohesionMode mode)
		{
			switch (mode)
			{
				case CohesionMode.Tight: return info.TightFactor;
				case CohesionMode.Loose: return info.LooseFactor;
				case CohesionMode.Spread: return info.SpreadFactor;
				default: return info.LooseFactor;
			}
		}

		int GetMaxDistance(CohesionMode mode)
		{
			switch (mode)
			{
				case CohesionMode.Tight: return info.TightMaxCells * 1024;
				case CohesionMode.Loose: return info.LooseMaxCells * 1024;
				case CohesionMode.Spread: return info.SpreadMaxCells * 1024;
				default: return info.LooseMaxCells * 1024;
			}
		}
	}
}
