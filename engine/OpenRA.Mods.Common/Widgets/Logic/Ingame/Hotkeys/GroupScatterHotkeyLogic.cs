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
			public string OrderType; // "Move", "AttackMove", "ForceAttack", "CaptureActor", etc.
			public bool IsActorTarget; // true for orders targeting specific actors
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

			return PerformGroupScatter(world, selectedActors);
		}

		// Public for the test harness (Test.GroupScatter Lua binding) so the spread
		// can be exercised in headless tests without a key press / live selection.
		public static bool PerformGroupScatter(World world, IList<Actor> selectedActors)
		{
			if (selectedActors.Count == 0)
				return false;

			// Aggregate waypoints across ALL selected actors. Different actors may be at
			// different points in their order chain (e.g., a faster unit may have completed
			// its first order already), so taking the max from a single unit can drop waypoints
			// that only the slowest units still hold. We dedupe by (Cell, OrderType) and
			// preserve the order they appeared in the longest chain.
			//
			// Only units that contributed at least one waypoint participate in the redistribution.
			// CollectWaypoints filters out automatic activities (autotargeting, nudges, …), so a
			// unit whose chain holds only auto-behaviours yields an empty list and is excluded —
			// Shift-G is for redistributing human-given orders, not for press-ganging idlers.
			var bestChain = new List<Waypoint>();
			var allChains = new List<List<Waypoint>>();
			var participants = new List<Actor>();
			foreach (var actor in selectedActors)
			{
				var actorWaypoints = CollectWaypoints(world, actor);
				if (actorWaypoints.Count == 0)
					continue;

				allChains.Add(actorWaypoints);
				participants.Add(actor);
				if (actorWaypoints.Count > bestChain.Count)
					bestChain = actorWaypoints;
			}

			var waypoints = new List<Waypoint>(bestChain);
			var seen = new HashSet<(CPos, string)>(waypoints.Select(w => (w.Cell, w.OrderType)));

			// Append any waypoints that other units still have but the longest chain dropped.
			foreach (var chain in allChains)
				foreach (var wp in chain)
					if (seen.Add((wp.Cell, wp.OrderType)))
						waypoints.Add(wp);

			if (waypoints.Count < 2 || participants.Count == 0)
			{
				TextNotificationsManager.AddFeedbackLine($"Group Scatter requires at least 2 queued waypoints (found {waypoints.Count}).");
				return true;
			}

			// Split waypoints into segments of consecutive same-type orders
			var segments = BuildSegments(waypoints);

			// Stop only the participating units — units excluded for lack of human-given
			// orders keep doing whatever they were doing (autotarget, nudge response, …).
			foreach (var unit in participants)
				world.IssueOrder(new Order("Stop", unit, false));

			// Process each segment in order, always queuing (Stop already cleared activities)
			foreach (var segment in segments)
				DistributeSegment(world, participants, segment);

			var segmentDesc = string.Join(" → ", segments.Select(s => $"{s.Waypoints.Count}x {s.OrderType}"));
			TextNotificationsManager.AddFeedbackLine($"Scattered {participants.Count} units: {segmentDesc}");
			return true;
		}

		static List<Waypoint> CollectWaypoints(World world, Actor actor)
		{
			var waypoints = new List<Waypoint>();
			var activity = actor.CurrentActivity;

			while (activity != null)
			{
				var wp = ExtractWaypoint(world, activity, actor);
				if (wp.HasValue)
					waypoints.Add(wp.Value);

				activity = activity.NextActivity;
			}

			return waypoints;
		}

		// Cohesion rewrites each grouped Move/AttackMove target to a per-unit formation SLOT and
		// discards the human click point. Recover it: map the slot cell back to the order point the
		// unit was spread around (CohesionSlotMemory), so Shift-G redistributes the MAIN points the
		// player clicked, not the near-identical slot cells. Non-cohesion units (no trait / no
		// record for this cell) keep the raw destination cell — the historical behaviour.
		static CPos ResolveOrderPoint(Actor actor, CPos slotCell)
		{
			var memory = actor.TraitOrDefault<CohesionSlotMemory>();
			if (memory != null && memory.TryGetOrderPointForSlot(slotCell, out var orderPoint))
				return orderPoint;

			return slotCell;
		}

		static Waypoint? ExtractWaypoint(World world, Activity activity, Actor actor)
		{
			// AttackMoveActivity — use cached OriginalDestination (most reliable)
			if (activity is AttackMoveActivity attackMove)
			{
				if (attackMove.OriginalDestination.HasValue)
				{
					var cell = ResolveOrderPoint(actor, attackMove.OriginalDestination.Value);
					return new Waypoint
					{
						Cell = cell,
						Target = Target.FromCell(world, cell),
						OrderType = "AttackMove",
						IsActorTarget = false
					};
				}

				// Fallback: TargetLineNodes (shouldn't be needed but just in case)
				return ExtractFromTargetLineNodes(world, activity, actor, "AttackMove");
			}

			// SmartMoveActivity wraps Move via IWrapMove (SmartMove trait)
			// Use the cached original destination — more reliable than TargetLineNodes
			if (activity is SmartMoveActivity smartMove)
			{
				if (smartMove.OriginalDestination.HasValue)
				{
					var cell = ResolveOrderPoint(actor, smartMove.OriginalDestination.Value);
					return new Waypoint
					{
						Cell = cell,
						Target = Target.FromCell(world, cell),
						OrderType = "Move",
						IsActorTarget = false
					};
				}

				return ExtractFromTargetLineNodes(world, activity, actor, "Move");
			}

			// Direct Move activity
			if (activity is Move move && move.Destination.HasValue)
			{
				var cell = ResolveOrderPoint(actor, move.Destination.Value);
				return new Waypoint
				{
					Cell = cell,
					Target = Target.FromCell(world, cell),
					OrderType = "Move",
					IsActorTarget = false
				};
			}

			// Covers Attack (AttackFrontal), AttackFollow.AttackActivity, AttackOmni.SetTarget, FlyAttack (AttackAircraft).
			// Only AttackSource.Default came from a human order — AutoTarget engagements and
			// AttackMove opportunity fire are auto-behaviours that must NOT be redistributed.
			if (activity is IAttackActivity attackActivity && attackActivity.Source == AttackSource.Default)
			{
				var t = attackActivity.Target;
				var orderName = attackActivity.ForceAttack ? "ForceAttack" : "Attack";

				if (t.Type == TargetType.Actor && t.Actor != null && !t.Actor.IsDead)
					return new Waypoint
					{
						Cell = t.Actor.Location,
						Target = t,
						OrderType = orderName,
						IsActorTarget = true
					};

				if (t.Type == TargetType.Terrain)
					return new Waypoint
					{
						Cell = world.Map.CellContaining(t.CenterPosition),
						Target = t,
						OrderType = orderName,
						IsActorTarget = false
					};
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

			// Enter-derived activities (RideTransport, EnterAsCrew, CaptureActor, Repairable, …)
			// are unit-specific — a unit chose to enter THIS transport / capture THIS actor.
			// Re-distributing them across the rest of the selection forces units who never
			// intended to enter/capture to do so. Observed bugs:
			//   • BMP (cargo) + outside infantry + Shift-G → all infantry march into the BMP
			//   • Garrisoned soldiers selectable via portholes → outside soldiers march into
			//     the building
			// The single waypoint vs. N-units distributor branch broadcasts to everyone, which
			// is correct for terrain Move but wrong for Enter. Drop them from the spread pool.
			if (activity is Enter)
				return null;

			// Anything else is presumed automatic (Nudge from being shoved, ScaredyCat panic,
			// HeliEmergencyLanding, scripted drags, …). Shift-G only redistributes orders the
			// human explicitly issued — unrecognised activities don't qualify.
			return null;
		}

		static Waypoint? ExtractFromTargetLineNodes(World world, Activity activity, Actor actor, string orderType)
		{
			foreach (var node in activity.TargetLineNodes(actor))
			{
				if (node.Target.Type == TargetType.Terrain)
				{
					var cell = ResolveOrderPoint(actor, world.Map.CellContaining(node.Target.CenterPosition));
					return new Waypoint
					{
						Cell = cell,
						Target = Target.FromCell(world, cell),
						OrderType = orderType,
						IsActorTarget = false
					};
				}
			}

			return null;
		}

		static List<Segment> BuildSegments(List<Waypoint> waypoints)
		{
			var segments = new List<Segment>();

			string currentType = null;
			List<Waypoint> currentWps = null;
			var currentIsActor = false;

			foreach (var wp in waypoints)
			{
				// Skip waypoints with null order type (unknown Enter subclasses)
				if (wp.OrderType == null)
					continue;

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

		// Distribute waypoints among units — works for both terrain and actor targets. Units are
		// bucketed per waypoint first, then each bucket is issued as ONE order: a multi-unit bucket
		// for a terrain Move/AttackMove goes out as a GROUPED order (Subject=null, GroupedActors set)
		// so it re-enters the normal pipeline and IModifyGroupOrder (cohesion) re-spreads that group
		// around its point — the user's "then each group can spread out due to cohesion" clause.
		static void DistributeSegment(World world, IList<Actor> units, Segment segment)
		{
			var waypoints = segment.Waypoints;
			var buckets = new List<Actor>[waypoints.Count];
			for (var i = 0; i < buckets.Length; i++)
				buckets[i] = new List<Actor>();

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
						buckets[bestWp].Add(unit);
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
						buckets[bestWp].Add(unit);
						wpCapacity[bestWp]--;
					}
				}
			}

			// Cohesion only interprets terrain Move/AttackMove; attack/actor-target segments are
			// issued per-unit as before (grouping them would gain nothing and cohesion ignores them).
			var groupable = !segment.IsActorTarget && (segment.OrderType == "Move" || segment.OrderType == "AttackMove");

			for (var i = 0; i < waypoints.Count; i++)
			{
				var bucket = buckets[i];
				if (bucket.Count == 0)
					continue;

				var target = waypoints[i].Target;
				if (groupable && bucket.Count > 1)
					world.IssueOrder(new Order(segment.OrderType, null, target, true, null, bucket.ToArray()));
				else
					foreach (var unit in bucket)
						world.IssueOrder(new Order(segment.OrderType, unit, target, true));
			}
		}
	}
}
