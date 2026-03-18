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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	[ChromeLogicArgsHotkeys("GroupScatterKey")]
	public class GroupScatterHotkeyLogic : SingleHotkeyBaseLogic
	{
		readonly World world;

		[ObjectCreator.UseCtor]
		public GroupScatterHotkeyLogic(Widget widget, ModData modData, WorldRenderer worldRenderer, World world, Dictionary<string, MiniYaml> logicArgs)
			: base(widget, modData, "GroupScatterKey", "WORLD_KEYHANDLER", logicArgs)
		{
			this.world = world;
		}

		protected override bool OnHotkeyActivated(KeyInput e)
		{
			if (world.IsGameOver)
				return false;

			var selectedActors = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead)
				.ToList();

			if (selectedActors.Count == 0)
				return false;

			// Collect all queued move waypoints from the first selected actor
			// (all selected units have the same queued orders when shift-clicking)
			var waypoints = CollectMoveWaypoints(selectedActors.First());

			if (waypoints.Count < 2)
			{
				TextNotificationsManager.AddFeedbackLine("Group Scatter requires at least 2 queued waypoints.");
				return true;
			}

			// Group selected units by type (actor name)
			var unitsByType = selectedActors
				.GroupBy(a => a.Info.Name)
				.ToList();

			// For each unit type, distribute waypoints evenly
			foreach (var group in unitsByType)
			{
				var units = group.ToList();
				DistributeWaypoints(units, waypoints);
			}

			TextNotificationsManager.AddFeedbackLine($"Scattered {selectedActors.Count} units across {waypoints.Count} waypoints.");
			return true;
		}

		List<CPos> CollectMoveWaypoints(Actor actor)
		{
			var waypoints = new List<CPos>();
			var activity = actor.CurrentActivity;

			while (activity != null)
			{
				if (activity is Move move && move.Destination.HasValue)
					waypoints.Add(move.Destination.Value);
				else
				{
					// For aircraft, extract target position from Fly activities
					var targets = activity.GetTargets(actor);
					foreach (var t in targets)
					{
						if (t.Type == TargetType.Terrain || t.Type == TargetType.FrozenActor || t.Type == TargetType.Actor)
						{
							var cell = world.Map.CellContaining(t.CenterPosition);
							waypoints.Add(cell);
							break;
						}
					}
				}

				activity = activity.NextActivity;
			}

			return waypoints;
		}

		void DistributeWaypoints(List<Actor> units, List<CPos> waypoints)
		{
			if (units.Count == 0 || waypoints.Count == 0)
				return;

			// Sort units by distance to first waypoint for sensible assignment
			var sortedUnits = units
				.OrderBy(a => (a.Location - waypoints[0]).LengthSquared)
				.ToList();

			// Cancel existing orders for all units in this group
			foreach (var unit in sortedUnits)
				world.IssueOrder(new Order("Stop", unit, false));

			// Assign waypoints round-robin to units
			// If more waypoints than units: each unit gets multiple waypoints (queued)
			// If more units than waypoints: multiple units go to same waypoint
			for (var i = 0; i < waypoints.Count; i++)
			{
				var unitIndex = i % sortedUnits.Count;
				var unit = sortedUnits[unitIndex];
				var queued = i >= sortedUnits.Count; // Queue if this unit already has an assignment

				var target = Target.FromCell(world, waypoints[i]);
				world.IssueOrder(new Order("Move", unit, target, queued));
			}
		}
	}
}
