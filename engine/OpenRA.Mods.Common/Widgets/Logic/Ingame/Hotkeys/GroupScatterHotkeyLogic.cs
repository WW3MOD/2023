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

			// CollectWaypoints filters out automatic activities (autotargeting, nudges, …), so
			// a unit whose chain holds only auto-behaviours yields an empty list and is excluded —
			// Shift-G is for redistributing human-given orders, not for press-ganging idlers.
			var allChains = new List<List<Waypoint>>();
			var participants = new List<Actor>();
			foreach (var actor in selectedActors)
			{
				var actorWaypoints = CollectWaypoints(world, actor);
				if (actorWaypoints.Count == 0)
					continue;

				allChains.Add(actorWaypoints);
				participants.Add(actor);
			}

			if (participants.Count == 0)
			{
				TextNotificationsManager.AddFeedbackLine("Group Scatter: no human-issued orders to redistribute.");
				return true;
			}

			// Only redistribute the longest tail that's identical across every participant.
			// That's the "group order" portion (e.g. waypoints just queued onto the whole
			// selection). Anything before the common suffix is unit-specific — likely the
			// result of an earlier spread or per-unit overrides. Re-mixing those would undo
			// the previous distribution and can land units at the wrong cells. Instead we
			// preserve each participant's prefix verbatim and only spread the shared tail.
			var commonSuffixLen = ComputeCommonSuffixLength(allChains);
			var commonSuffix = commonSuffixLen > 0
				? allChains[0].Skip(allChains[0].Count - commonSuffixLen).ToList()
				: new List<Waypoint>();

			if (commonSuffix.Count < 2)
			{
				TextNotificationsManager.AddFeedbackLine($"Group Scatter requires at least 2 shared queued waypoints (found {commonSuffix.Count}).");
				return true;
			}

			// Split the shared tail into segments of consecutive same-type orders
			var segments = BuildSegments(commonSuffix);

			// Stop participating units (clears their entire chain), then re-issue each
			// participant's prefix before the redistributed shared tail goes on top.
			foreach (var unit in participants)
				world.IssueOrder(new Order("Stop", unit, false));

			for (var i = 0; i < participants.Count; i++)
			{
				var unit = participants[i];
				var chain = allChains[i];
				var prefixCount = chain.Count - commonSuffixLen;
				for (var j = 0; j < prefixCount; j++)
				{
					var wp = chain[j];
					world.IssueOrder(new Order(wp.OrderType, unit, wp.Target, true));
				}
			}

			// Process each segment in order, always queuing (prefix is already in the chain)
			foreach (var segment in segments)
				DistributeSegment(world, participants, segment);

			var segmentDesc = string.Join(" → ", segments.Select(s => $"{s.Waypoints.Count}x {s.OrderType}"));
			TextNotificationsManager.AddFeedbackLine($"Scattered {participants.Count} units: {segmentDesc}");
			return true;
		}

		// Returns the length of the longest tail [N waypoints] that's identical across every chain.
		// Matching is by (Cell, OrderType) — the same dedup key BuildSegments / Stop work with.
		static int ComputeCommonSuffixLength(List<List<Waypoint>> chains)
		{
			if (chains.Count == 0)
				return 0;

			var minLen = chains[0].Count;
			for (var i = 1; i < chains.Count; i++)
				if (chains[i].Count < minLen)
					minLen = chains[i].Count;

			var suffix = 0;
			while (suffix < minLen)
			{
				var refWp = chains[0][chains[0].Count - 1 - suffix];
				var allMatch = true;
				for (var i = 1; i < chains.Count; i++)
				{
					var wp = chains[i][chains[i].Count - 1 - suffix];
					if (wp.Cell != refWp.Cell || wp.OrderType != refWp.OrderType)
					{
						allMatch = false;
						break;
					}
				}

				if (!allMatch)
					break;

				suffix++;
			}

			return suffix;
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

		static Waypoint? ExtractWaypoint(World world, Activity activity, Actor actor)
		{
			// AttackMoveActivity — use cached OriginalDestination (most reliable)
			if (activity is AttackMoveActivity attackMove)
			{
				if (attackMove.OriginalDestination.HasValue)
					return new Waypoint
					{
						Cell = attackMove.OriginalDestination.Value,
						Target = Target.FromCell(world, attackMove.OriginalDestination.Value),
						OrderType = "AttackMove",
						IsActorTarget = false
					};

				// Fallback: TargetLineNodes (shouldn't be needed but just in case)
				return ExtractFromTargetLineNodes(world, activity, actor, "AttackMove");
			}

			// SmartMoveActivity wraps Move via IWrapMove (SmartMove trait)
			// Use the cached original destination — more reliable than TargetLineNodes
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

				return ExtractFromTargetLineNodes(world, activity, actor, "Move");
			}

			// Direct Move activity
			if (activity is Move move && move.Destination.HasValue)
				return new Waypoint
				{
					Cell = move.Destination.Value,
					Target = Target.FromCell(world, move.Destination.Value),
					OrderType = "Move",
					IsActorTarget = false
				};

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

		// Distribute waypoints among units — works for both terrain and actor targets
		static void DistributeSegment(World world, IList<Actor> units, Segment segment)
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
						world.IssueOrder(new Order(segment.OrderType, unit, waypoints[bestWp].Target, true));
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
						world.IssueOrder(new Order(segment.OrderType, unit, waypoints[bestWp].Target, true));
						wpCapacity[bestWp]--;
					}
				}
			}
		}
	}
}
