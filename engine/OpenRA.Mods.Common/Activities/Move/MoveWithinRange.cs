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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class MoveWithinRange : MoveAdjacentTo
	{
		readonly WDist maxRange;
		readonly WDist minRange;

		private int checkTick = 0;

		public MoveWithinRange(Actor self, in Target target, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: base(self, target, initialTargetPosition, targetLineColor)
		{
			this.minRange = minRange;
			this.maxRange = maxRange;
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

		// protected override IEnumerable<CPos> CandidateMovementCells(Actor self)
		// {
		// 	return map.FindTilesInAnnulus(lastVisibleTargetLocation, minCells, maxCells)
		// 		.Where(c => Mobile.CanStayInCell(c) && AtCorrectRange(map.CenterOfSubCell(c, Mobile.FromSubCell)));
		// }

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
