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
using OpenRA.Activities;
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

		// Tracks a waypoint and what order type it was (Move vs AttackMove)
		struct Waypoint
		{
			public CPos Cell;
			public string OrderType; // "Move" or "AttackMove"
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

			// Collect waypoints from the first selected actor's activity queue
			// (all selected units share the same queued orders from shift-clicking)
			var waypoints = CollectWaypoints(selectedActors.First());

			if (waypoints.Count < 2)
			{
				TextNotificationsManager.AddFeedbackLine("Group Scatter requires at least 2 queued waypoints.");
				return true;
			}

			// Distribute: assign each unit exactly one waypoint
			// If more units than waypoints, multiple units share waypoints (spread evenly)
			// If more waypoints than units, each unit still gets one (closest unassigned)
			DistributeWaypoints(selectedActors, waypoints);

			TextNotificationsManager.AddFeedbackLine($"Scattered {selectedActors.Count} units across {waypoints.Count} waypoints.");
			return true;
		}

		List<Waypoint> CollectWaypoints(Actor actor)
		{
			var waypoints = new List<Waypoint>();
			var activity = actor.CurrentActivity;

			while (activity != null)
			{
				var wp = ExtractWaypoint(activity, actor);
				if (wp.HasValue)
					waypoints.Add(wp.Value);

				activity = activity.NextActivity;
			}

			return waypoints;
		}

		Waypoint? ExtractWaypoint(Activity activity, Actor actor)
		{
			// Direct Move activity
			if (activity is Move move && move.Destination.HasValue)
				return new Waypoint { Cell = move.Destination.Value, OrderType = "Move" };

			// AttackMove wraps a Move in its child
			if (activity is AttackMoveActivity)
			{
				// Get target from the activity's GetTargets
				var targets = activity.GetTargets(actor);
				foreach (var t in targets)
				{
					if (t.Type == TargetType.Terrain)
						return new Waypoint { Cell = world.Map.CellContaining(t.CenterPosition), OrderType = "AttackMove" };
				}

				// If no child target yet, check TargetLineNodes for the destination
				var lineNodes = activity.TargetLineNodes(actor);
				foreach (var node in lineNodes)
				{
					if (node.Target.Type == TargetType.Terrain)
						return new Waypoint { Cell = world.Map.CellContaining(node.Target.CenterPosition), OrderType = "AttackMove" };
				}
			}

			// Fly activity (aircraft)
			if (activity.GetType().Name == "Fly")
			{
				var targets = activity.GetTargets(actor);
				foreach (var t in targets)
				{
					if (t.Type == TargetType.Terrain)
						return new Waypoint { Cell = world.Map.CellContaining(t.CenterPosition), OrderType = "Move" };
				}
			}

			return null;
		}

		void DistributeWaypoints(List<Actor> units, List<Waypoint> waypoints)
		{
			if (units.Count == 0 || waypoints.Count == 0)
				return;

			// Stop all units first
			foreach (var unit in units)
				world.IssueOrder(new Order("Stop", unit, false));

			// Greedy nearest-waypoint assignment:
			// Each unit gets exactly one waypoint. Assignments minimize crossing paths.
			var remainingUnits = new List<Actor>(units);
			var waypointAssignments = new Dictionary<int, List<Actor>>(); // waypoint index -> units

			for (var i = 0; i < waypoints.Count; i++)
				waypointAssignments[i] = new List<Actor>();

			if (units.Count <= waypoints.Count)
			{
				// More waypoints than units: each unit gets the closest unassigned waypoint
				var availableWaypoints = new List<int>(Enumerable.Range(0, waypoints.Count));

				foreach (var unit in units)
				{
					var bestWp = -1;
					var bestDist = int.MaxValue;

					foreach (var wpIdx in availableWaypoints)
					{
						var dist = (unit.Location - waypoints[wpIdx].Cell).LengthSquared;
						if (dist < bestDist)
						{
							bestDist = dist;
							bestWp = wpIdx;
						}
					}

					if (bestWp >= 0)
					{
						waypointAssignments[bestWp].Add(unit);
						availableWaypoints.Remove(bestWp);
					}
				}
			}
			else
			{
				// More units than waypoints: distribute units evenly across waypoints
				// Use greedy nearest assignment, allowing multiple units per waypoint
				var wpCapacity = new int[waypoints.Count];
				var baseCount = units.Count / waypoints.Count;
				var remainder = units.Count % waypoints.Count;

				// Set capacity for each waypoint
				for (var i = 0; i < waypoints.Count; i++)
					wpCapacity[i] = baseCount + (i < remainder ? 1 : 0);

				// Sort units by position for stable assignment
				var sortedUnits = units.OrderBy(a => a.ActorID).ToList();

				foreach (var unit in sortedUnits)
				{
					var bestWp = -1;
					var bestDist = int.MaxValue;

					for (var i = 0; i < waypoints.Count; i++)
					{
						if (wpCapacity[i] <= 0)
							continue;

						var dist = (unit.Location - waypoints[i].Cell).LengthSquared;
						if (dist < bestDist)
						{
							bestDist = dist;
							bestWp = i;
						}
					}

					if (bestWp >= 0)
					{
						waypointAssignments[bestWp].Add(unit);
						wpCapacity[bestWp]--;
					}
				}
			}

			// Issue orders
			foreach (var kvp in waypointAssignments)
			{
				var wp = waypoints[kvp.Key];
				var target = Target.FromCell(world, wp.Cell);

				foreach (var unit in kvp.Value)
					world.IssueOrder(new Order(wp.OrderType, unit, target, false));
			}
		}
	}
}
