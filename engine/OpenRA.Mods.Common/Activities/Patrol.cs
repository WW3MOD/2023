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
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// Loops through waypoints using attack-move behavior.
	/// If last waypoint is near first (within 2 cells), loops circularly: A→B→C→D→A→B→...
	/// Otherwise, bounces back and forth: A→B→C→D→C→B→A→B→...
	/// </summary>
	public class PatrolActivity : Activity
	{
		readonly CPos[] waypoints;
		readonly bool isCircular;
		readonly IMove move;
		readonly Color targetLineColor = Color.Cyan;

		int currentIndex;
		int direction = 1; // 1 = forward, -1 = backward (for bounce mode)

		public PatrolActivity(Actor self, CPos[] waypoints)
		{
			this.waypoints = waypoints;
			move = self.Trait<IMove>();

			// Circular if last waypoint is within 2 cells of first
			if (waypoints.Length >= 3)
			{
				var dx = waypoints[waypoints.Length - 1].X - waypoints[0].X;
				var dy = waypoints[waypoints.Length - 1].Y - waypoints[0].Y;
				isCircular = dx * dx + dy * dy <= 4;
			}

			currentIndex = 0;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || waypoints.Length == 0)
				return true;

			// Queue attack-move to next waypoint
			var destination = waypoints[currentIndex];
			var attackMove = self.TraitOrDefault<AttackMove>();
			if (attackMove != null)
			{
				QueueChild(new AttackMoveActivity(self, () => move.MoveTo(destination, 8, targetLineColor: targetLineColor)));
			}
			else
			{
				// Fallback: plain move if no AttackMove trait
				QueueChild(move.MoveTo(destination, 8, targetLineColor: targetLineColor));
			}

			// Advance to next waypoint
			if (isCircular)
			{
				currentIndex = (currentIndex + 1) % waypoints.Length;
			}
			else
			{
				currentIndex += direction;
				if (currentIndex >= waypoints.Length)
				{
					direction = -1;
					currentIndex = waypoints.Length - 2;
					if (currentIndex < 0)
						currentIndex = 0;
				}
				else if (currentIndex < 0)
				{
					direction = 1;
					currentIndex = 1;
					if (currentIndex >= waypoints.Length)
						currentIndex = 0;
				}
			}

			return false;
		}

		public IEnumerable<CPos> GetPatrolWaypoints()
		{
			return waypoints;
		}
	}
}
