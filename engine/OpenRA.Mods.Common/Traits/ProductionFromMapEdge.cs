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
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Produce a unit on the closest map edge cell and move into the world.")]
	sealed class ProductionFromMapEdgeInfo : ProductionInfo
	{
		[Desc("Number of candidate edge cells to consider for spawning (center + sides).")]
		public readonly int SpawnCandidateCount = 5;

		public override object Create(ActorInitializer init) { return new ProductionFromMapEdge(init, this); }
	}

	sealed class ProductionFromMapEdge : Production
	{
		readonly ProductionFromMapEdgeInfo edgeInfo;
		readonly CPos? spawnLocation;
		RallyPoint rp;

		// Round-robin index for distributing spawns across candidate cells
		int nextCandidateIndex;

		public ProductionFromMapEdge(ActorInitializer init, ProductionInfo info)
			: base(init, info)
		{
			edgeInfo = (ProductionFromMapEdgeInfo)info;

			var spawnLocationInit = init.GetOrDefault<ProductionSpawnLocationInit>(info);
			if (spawnLocationInit != null)
				spawnLocation = spawnLocationInit.Value;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			rp = self.TraitOrDefault<RallyPoint>();
		}

		/// <summary>Find the closest SpawnArea actor to this building.</summary>
		static CPos? FindClosestSpawnArea(Actor self)
		{
			var spawnAreas = self.World.ActorsWithTrait<SpawnArea>()
				.Where(a => !a.Actor.IsDead && a.Actor.IsInWorld)
				.Select(a => a.Actor)
				.ToList();

			if (spawnAreas.Count == 0)
				return null;

			Actor closest = null;
			var closestDist = int.MaxValue;
			foreach (var sa in spawnAreas)
			{
				var dist = (self.Location - sa.Location).LengthSquared;
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = sa;
				}
			}

			return closest?.Location;
		}

		public override bool Produce(Actor self, ActorInfo producee, string productionType, TypeDictionary inits, int refundableValue)
		{
			if (IsTraitDisabled || IsTraitPaused)
				return false;

			var aircraftInfo = producee.TraitInfoOrDefault<AircraftInfo>();
			var mobileInfo = producee.TraitInfoOrDefault<MobileInfo>();

			var hasRallyPoint = rp != null && rp.Path.Count > 0;

			var location = spawnLocation;
			if (!location.HasValue)
			{
				// Aircraft spawn at map edge near the SpawnArea (with round-robin distribution).
				// Uses SpawnArea as the edge hint so helicopters appear at the correct map edge,
				// not the edge nearest to the SR building (which could be any edge).
				if (aircraftInfo != null)
				{
					var spawnAreaHint = FindClosestSpawnArea(self);
					var searchOrigin = spawnAreaHint ?? self.Location;
					var candidates = self.World.Map.GetSpawnCandidatesOnSameEdge(searchOrigin, edgeInfo.SpawnCandidateCount);
					if (candidates.Length > 0)
					{
						// Round-robin across candidate edge cells for distributed spawning
						var idx = nextCandidateIndex % candidates.Length;
						location = candidates[idx];
						nextCandidateIndex = (idx + 1) % candidates.Length;
					}
					else
						location = self.World.Map.ChooseClosestEdgeCell(searchOrigin);
				}

				// Ground units: use SpawnArea as a hint for which edge to spawn near.
				// Uses round-robin across candidate cells for fast, distributed spawning.
				if (mobileInfo != null)
				{
					var spawnAreaHint = FindClosestSpawnArea(self);
					var searchOrigin = spawnAreaHint ?? self.Location;

					CPos[] candidates;
					if (spawnAreaHint.HasValue)
						candidates = self.World.Map.GetSpawnCandidatesOnSameEdge(searchOrigin, edgeInfo.SpawnCandidateCount);
					else
					{
						// No SpawnArea: legacy behavior, find closest matching edge cell (any edge).
						// Don't gate on path-to-rally — see comment below.
						var legacyCell = self.World.Map.ChooseClosestMatchingEdgeCell(searchOrigin,
							c => mobileInfo.CanEnterCell(self.World, null, c));
						if (legacyCell != default)
							location = legacyCell;
						candidates = null;
					}

					if (candidates != null && candidates.Length > 0)
					{
						// Round-robin: start from the next candidate index and wrap around.
						// This distributes spawns evenly across all candidate cells instead of
						// always piling onto center and waiting for it to clear.
						// Rally-point reachability is intentionally NOT checked here: a bad rally
						// (in water, walled in, etc.) must not block production. The unit will
						// spawn and MoveTo with evaluateNearestMovableCell:true picks the closest
						// reachable cell to the destination — same behaviour as a manual move.
						location = null;
						for (var attempt = 0; attempt < candidates.Length; attempt++)
						{
							var idx = (nextCandidateIndex + attempt) % candidates.Length;
							if (mobileInfo.CanEnterCell(self.World, null, candidates[idx]))
							{
								location = candidates[idx];
								nextCandidateIndex = (idx + 1) % candidates.Length;
								break;
							}
						}

						// All candidates blocked — will retry next tick
					}
				}
			}

			// No suitable spawn location could be found, so production has failed.
			if (!location.HasValue)
				return false;

			var pos = self.World.Map.CenterOfCell(location.Value);

			// If aircraft, spawn at cruise altitude
			if (aircraftInfo != null)
				pos += new WVec(0, 0, aircraftInfo.CruiseAltitude.Length);

			// Build the per-waypoint plan:
			// - Rally point set: replay each waypoint with its order type (Move / AttackMove / ForceMove)
			// - No rally point: drive to the supply route building as a plain Move
			var waypoints = hasRallyPoint
				? rp.Path
				: new List<RallyPointWaypoint> { new(self.Location, RallyOrderType.Move) };

			var initialFacing = self.World.Map.FacingBetween(location.Value, waypoints[0].Cell, WAngle.Zero);

			self.World.AddFrameEndTask(w =>
			{
				var td = new TypeDictionary();
				foreach (var init in inits)
					td.Add(init);

				td.Add(new LocationInit(location.Value));
				td.Add(new CenterPositionInit(pos));
				td.Add(new FacingInit(initialFacing));

				var newUnit = self.World.CreateActor(producee.Name, td);

				var move = newUnit.TraitOrDefault<IMove>();
				if (move != null)
				{
					foreach (var wp in waypoints)
						newUnit.QueueActivity(BuildWaypointActivity(newUnit, move, wp));
				}

				if (!self.IsDead)
					foreach (var t in self.TraitsImplementing<INotifyProduction>())
						t.UnitProduced(self, newUnit, waypoints[0].Cell);

				var notifyOthers = self.World.ActorsWithTrait<INotifyOtherProduction>();
				foreach (var notify in notifyOthers)
					notify.Trait.UnitProducedByOther(notify.Actor, self, newUnit, productionType, td);
			});

			return true;
		}

		// Replay a rally-point waypoint as the matching unit-side activity. Aircraft
		// don't have a meaningful AttackMove path, so they fall back to plain Move
		// regardless of the waypoint type — keeps existing helicopter/plane flight
		// behavior intact when SR rallies get modifier-tagged. The target-line color
		// passed here is what the produced unit shows when it is selected — matches
		// the convention for player-issued orders (Move = Green, AttackMove = OrangeRed,
		// ForceMove = DeepSkyBlue).
		static OpenRA.Activities.Activity BuildWaypointActivity(Actor self, IMove move, RallyPointWaypoint wp)
		{
			switch (wp.OrderType)
			{
				case RallyOrderType.AttackMove when move is Mobile:
					return new AttackMoveActivity(self,
						() => move.MoveTo(wp.Cell, 1, evaluateNearestMovableCell: true, targetLineColor: Color.OrangeRed));

				case RallyOrderType.ForceMove:
					return move.MoveTo(wp.Cell, 2, evaluateNearestMovableCell: true, targetLineColor: Color.DeepSkyBlue);

				case RallyOrderType.Move:
				default:
					return move.MoveTo(wp.Cell, 2, evaluateNearestMovableCell: true, targetLineColor: Color.Green);
			}
		}
	}

	public class ProductionSpawnLocationInit : ValueActorInit<CPos>
	{
		public ProductionSpawnLocationInit(TraitInfo info, CPos value)
			: base(info, value) { }
	}
}
