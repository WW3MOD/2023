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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class MoveWithinRange : MoveAdjacentTo
	{
		readonly Target target;
		readonly WDist maxRange;
		readonly WDist minRange;
		readonly Map map;
		readonly int maxCells;
		readonly int minCells;

		int checkTick = 0;

		public MoveWithinRange(Actor self, in Target target, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: base(self, target, initialTargetPosition, targetLineColor)
		{
			this.target = target;
			this.minRange = minRange;
			this.maxRange = maxRange;
			map = self.World.Map;
			maxCells = (maxRange.Length + 1023) / 1024;
			minCells = minRange.Length / 1024;
		}

		protected override bool ShouldStop(Actor self)
		{
			if (checkTick-- <= 0)
			{
				// We are now in range. Don't move any further!
				// HACK: This works around the pathfinder not returning the shortest path
				var result = Target.Type != TargetType.Invalid
					&& AtCorrectRange(self.CenterPosition)
					&& Mobile.CanInteractWithGroundLayer(self)
					&& Mobile.CanStayInCell(self.Location)
					&& CheckFireSolution(self);

				checkTick = result ? 0 : 10; // Check every 10 ticks to reduce lag, reset to 0 (instant check) if stopping so that next target checks immediately.

				return result;
			}
			else
				return false;
		}

		protected override bool ShouldRepath(Actor self, CPos targetLocation)
		{
			return lastVisibleTargetLocation != targetLocation && (!AtCorrectRange(self.CenterPosition)
				|| !Mobile.CanInteractWithGroundLayer(self) || !Mobile.CanStayInCell(self.Location));
		}

		protected override List<CPos> CalculatePathToTarget(Actor self, BlockedByActor check)
		{
			// PERF: Assume that candidate cells don't change within a tick to avoid repeated queries
			// when Move enumerates different BlockedByActor values.
			if (searchCellsTick != self.World.WorldTick)
			{
				SearchCells.Clear();
				searchCellsTick = self.World.WorldTick;
				foreach (var cell in map.FindTilesInAnnulus(lastVisibleTargetLocation, minCells, maxCells))
					if (Mobile.CanStayInCell(cell) && Mobile.CanEnterCell(cell) && AtCorrectRange(map.CenterOfSubCell(cell, Mobile.FromSubCell)))
						SearchCells.Add(cell);
			}

			if (SearchCells.Count == 0)
				return PathFinder.NoPath;

			return Mobile.PathFinder.FindPathToTargetCells(self, self.Location, SearchCells, check);

			// 1010 ?
			// if (target.Type != TargetType.Invalid && lastVisibleTargetLocation != new CPos(target.CenterPosition.X, target.CenterPosition.Y) && (!AtCorrectRange(self.CenterPosition)))
			// 	return map.FindTilesInAnnulus(lastVisibleTargetLocation, minCells, maxCells)
			// 		.Where(c => Mobile.CanStayInCell(c) && AtCorrectRange(map.CenterOfSubCell(c, Mobile.FromSubCell)));

			// return base.CandidateMovementCells(self);
		}

		bool AtCorrectRange(WPos origin)
		{
			return Target.IsInRange(origin, maxRange) && !Target.IsInRange(origin, minRange);
		}

		bool CheckFireSolution(Actor self)
		{
			// AnyBlocking freezes Attack, height check is wrong it seems but either way unit never tries to move, as it does when out of range.
			// Add NoBlockingActors function seperately
			return self.TraitOrDefault<IndirectFire>() != null // If the actor can fire over BlockingActors || No blocking actors between target
				|| !BlocksProjectiles.AnyBlockingActorsBetween(
					self,
					Target.CenterPosition,
					new WDist(1),
					out var _);
		}
	}
}
