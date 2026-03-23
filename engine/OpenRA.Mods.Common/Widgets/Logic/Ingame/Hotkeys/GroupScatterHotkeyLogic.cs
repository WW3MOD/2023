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

		struct Waypoint
		{
			public CPos Cell;
			public Target Target;
			public string OrderType; // "Move", "AttackMove", "Attack", "ForceAttack"
			public bool IsActorTarget; // true for Attack/ForceAttack on specific actors
		}

		// A segment is a run of consecutive same-type orders
		struct Segment
		{
			public string OrderType;
			public bool IsActorTarget;
			public List<Waypoint> Waypoints;
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

			// Try all selected actors and use the one with the most waypoints.
			// Different actors may be at different points in their order chain
			// (e.g., a faster unit may have completed its first order already).
			var waypoints = new List<Waypoint>();
			foreach (var actor in selectedActors)
			{
				var actorWaypoints = CollectWaypoints(actor);
				if (actorWaypoints.Count > waypoints.Count)
					waypoints = actorWaypoints;
			}

			if (waypoints.Count < 2)
			{
				TextNotificationsManager.AddFeedbackLine($"Group Scatter requires at least 2 queued waypoints (found {waypoints.Count}).");
				return true;
			}

			// Split waypoints into segments of consecutive same-type orders
			var segments = BuildSegments(waypoints);

			// Stop all units first
			foreach (var unit in selectedActors)
				world.IssueOrder(new Order("Stop", unit, false));

			// Process each segment in order, queuing after the previous
			var isFirstSegment = true;
			foreach (var segment in segments)
			{
				if (segment.IsActorTarget)
				{
					// Actor-targeted orders (Attack/ForceAttack): ALL units get ALL targets, shuffled per unit
					IssueActorTargetSegment(selectedActors, segment, !isFirstSegment);
				}
				else
				{
					// Position-targeted orders (Move/AttackMove): distribute among units
					IssuePositionSegment(selectedActors, segment, !isFirstSegment);
				}

				isFirstSegment = false;
			}

			var segmentDesc = string.Join(" → ", segments.Select(s => $"{s.Waypoints.Count}x {s.OrderType}"));
			TextNotificationsManager.AddFeedbackLine($"Scattered {selectedActors.Count} units: {segmentDesc}");
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
				return new Waypoint
				{
					Cell = move.Destination.Value,
					Target = Target.FromCell(world, move.Destination.Value),
					OrderType = "Move",
					IsActorTarget = false
				};

			// SmartMoveActivity wraps Move via IWrapMove (SmartMove trait)
			// Use the cached original destination — more reliable than TargetLineNodes
			// which can fail if the inner Move's destination was modified during execution
			if (activity is SmartMoveActivity smartMove)
			{
				if (smartMove.OriginalDestination.HasValue)
					return new Waypoint
					{
						Cell = smartMove.OriginalDestination.Value,
						Target = Target.FromCell(world, smartMove.OriginalDestination.Value),
						OrderType = "Move",
						IsActorTarget = false
					};

				return ExtractFromTargetLineNodes(activity, actor, "Move");
			}

			// AttackMove wraps a Move in its child
			if (activity is AttackMoveActivity)
			{
				// First try GetTargets (works when child is a Move, not attacking)
				var targets = activity.GetTargets(actor);
				foreach (var t in targets)
				{
					if (t.Type == TargetType.Terrain)
						return new Waypoint
						{
							Cell = world.Map.CellContaining(t.CenterPosition),
							Target = t,
							OrderType = "AttackMove",
							IsActorTarget = false
						};
				}

				// Fallback: TargetLineNodes (works even when engaging enemies)
				return ExtractFromTargetLineNodes(activity, actor, "AttackMove");
			}

			// Attack activity (force-attack or auto-attack on an actor)
			if (activity is Attack)
			{
				var targets = activity.GetTargets(actor);
				foreach (var t in targets)
				{
					if (t.Type == TargetType.Actor && t.Actor != null && !t.Actor.IsDead)
						return new Waypoint
						{
							Cell = t.Actor.Location,
							Target = t,
							OrderType = "ForceAttack",
							IsActorTarget = true
						};

					if (t.Type == TargetType.Terrain)
						return new Waypoint
						{
							Cell = world.Map.CellContaining(t.CenterPosition),
							Target = t,
							OrderType = "ForceAttack",
							IsActorTarget = false
						};
				}
			}

			// Fly activity (aircraft move)
			if (activity is Fly)
			{
				var targets = activity.GetTargets(actor);
				foreach (var t in targets)
				{
					if (t.Type == TargetType.Terrain)
						return new Waypoint
						{
							Cell = world.Map.CellContaining(t.CenterPosition),
							Target = t,
							OrderType = "Move",
							IsActorTarget = false
						};
				}
			}

			// General fallback: try TargetLineNodes for any unrecognized wrapper activities
			return ExtractFromTargetLineNodes(activity, actor, "Move");
		}

		Waypoint? ExtractFromTargetLineNodes(Activity activity, Actor actor, string orderType)
		{
			foreach (var node in activity.TargetLineNodes(actor))
			{
				if (node.Target.Type == TargetType.Terrain)
					return new Waypoint
					{
						Cell = world.Map.CellContaining(node.Target.CenterPosition),
						Target = node.Target,
						OrderType = orderType,
						IsActorTarget = false
					};
			}

			return null;
		}

		List<Segment> BuildSegments(List<Waypoint> waypoints)
		{
			var segments = new List<Segment>();

			string currentType = null;
			List<Waypoint> currentWps = null;
			var currentIsActor = false;

			foreach (var wp in waypoints)
			{
				if (wp.OrderType != currentType)
				{
					if (currentWps != null && currentWps.Count > 0)
						segments.Add(new Segment { OrderType = currentType, IsActorTarget = currentIsActor, Waypoints = currentWps });

					currentType = wp.OrderType;
					currentIsActor = wp.IsActorTarget;
					currentWps = new List<Waypoint>();
				}

				currentWps.Add(wp);
			}

			if (currentWps != null && currentWps.Count > 0)
				segments.Add(new Segment { OrderType = currentType, IsActorTarget = currentIsActor, Waypoints = currentWps });

			return segments;
		}

		// Position-based orders: distribute waypoints among units (each unit gets one per segment)
		void IssuePositionSegment(List<Actor> units, Segment segment, bool queued)
		{
			var waypoints = segment.Waypoints;

			if (units.Count <= waypoints.Count)
			{
				// More waypoints than units: each unit gets the closest unassigned waypoint
				var available = new List<int>(Enumerable.Range(0, waypoints.Count));

				foreach (var unit in units)
				{
					var bestWp = -1;
					var bestDist = int.MaxValue;

					foreach (var wpIdx in available)
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
						var target = Target.FromCell(world, waypoints[bestWp].Cell);
						world.IssueOrder(new Order(segment.OrderType, unit, target, queued));
						available.Remove(bestWp);
					}
				}
			}
			else
			{
				// More units than waypoints: spread units evenly across waypoints
				var wpCapacity = new int[waypoints.Count];
				var baseCount = units.Count / waypoints.Count;
				var remainder = units.Count % waypoints.Count;

				for (var i = 0; i < waypoints.Count; i++)
					wpCapacity[i] = baseCount + (i < remainder ? 1 : 0);

				foreach (var unit in units)
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
						var target = Target.FromCell(world, waypoints[bestWp].Cell);
						world.IssueOrder(new Order(segment.OrderType, unit, target, queued));
						wpCapacity[bestWp]--;
					}
				}
			}
		}

		// Actor-targeted orders (Attack): all units get all targets, shuffled per unit
		void IssueActorTargetSegment(List<Actor> units, Segment segment, bool queued)
		{
			var rng = new Random();
			var waypoints = segment.Waypoints;

			foreach (var unit in units)
			{
				// Shuffle order of targets per unit so they don't all focus the same one first
				var shuffled = waypoints.OrderBy(_ => rng.Next()).ToList();
				var first = true;

				foreach (var wp in shuffled)
				{
					world.IssueOrder(new Order(wp.OrderType, unit, wp.Target, queued || !first));
					first = false;
				}
			}
		}
	}
}
