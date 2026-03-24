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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Produce a unit on the closest map edge cell and move into the world.")]
	class ProductionFromMapEdgeInfo : ProductionInfo
	{
		[Desc("Ticks to wait for the preferred center spawn cell before trying adjacent cells.")]
		public readonly int SpawnCenterWaitTicks = 25;

		public override object Create(ActorInitializer init) { return new ProductionFromMapEdge(init, this); }
	}

	class ProductionFromMapEdge : Production
	{
		readonly ProductionFromMapEdgeInfo edgeInfo;
		readonly CPos? spawnLocation;
		readonly IPathFinder pathFinder;
		RallyPoint rp;

		// Track how long we've been waiting for the center spawn cell to free up
		long centerBlockedSinceTick = -1;

		public ProductionFromMapEdge(ActorInitializer init, ProductionInfo info)
			: base(init, info)
		{
			edgeInfo = (ProductionFromMapEdgeInfo)info;
			pathFinder = init.Self.World.WorldActor.Trait<IPathFinder>();

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
				// Aircraft always spawn at map edge (fly in directly)
				if (aircraftInfo != null)
					location = self.World.Map.ChooseClosestEdgeCell(self.Location);

				// Ground units: use SpawnArea as a hint for which edge to spawn near.
				// Only 3 cells are ever considered: the closest edge cell to the SpawnArea (center)
				// and its two immediate neighbors along the edge. Center is strongly preferred —
				// we wait up to SpawnCenterWaitTicks before trying the sides.
				if (mobileInfo != null)
				{
					var locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().First(l => l.Info.Name == mobileInfo.Locomotor);
					var spawnAreaHint = FindClosestSpawnArea(self);
					var firstDest = hasRallyPoint ? rp.Path[0] : self.Location;
					var searchOrigin = spawnAreaHint ?? self.Location;

					// Get the 3 candidate spawn cells (center + 2 adjacent along the edge)
					CPos[] candidates;
					if (spawnAreaHint.HasValue)
						candidates = self.World.Map.GetSpawnCandidatesOnSameEdge(searchOrigin, 3);
					else
					{
						// No SpawnArea: legacy behavior, find closest matching edge cell (any edge)
						var legacyCell = self.World.Map.ChooseClosestMatchingEdgeCell(searchOrigin,
							c => mobileInfo.CanEnterCell(self.World, null, c) && pathFinder.PathExistsForLocomotor(locomotor, c, firstDest));
						if (legacyCell != default)
							location = legacyCell;
						candidates = null;
					}

					if (candidates != null && candidates.Length > 0)
					{
						// Verify path exists from center cell to destination (terrain check, done once)
						var centerCell = candidates[0];
						if (!pathFinder.PathExistsForLocomotor(locomotor, centerCell, firstDest))
						{
							// If center cell can't path to destination, none of the 3 adjacent cells will either
							location = null;
						}
						else if (mobileInfo.CanEnterCell(self.World, null, centerCell))
						{
							// Center cell is free — use it, reset wait timer
							location = centerCell;
							centerBlockedSinceTick = -1;
						}
						else
						{
							// Center is blocked — start or continue waiting
							var currentTick = self.World.WorldTick;
							if (centerBlockedSinceTick < 0)
								centerBlockedSinceTick = currentTick;

							var waited = currentTick - centerBlockedSinceTick;
							if (waited < edgeInfo.SpawnCenterWaitTicks)
							{
								// Still waiting for center to free up — don't spawn yet
								location = null;
							}
							else
							{
								// Waited long enough — try the two side cells
								location = null;
								for (var i = 1; i < candidates.Length; i++)
								{
									if (mobileInfo.CanEnterCell(self.World, null, candidates[i]))
									{
										location = candidates[i];
										centerBlockedSinceTick = -1;
										break;
									}
								}

								// All 3 blocked — keep waiting, retry next tick
							}
						}
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

			// Build the movement destination list:
			// - Rally point set: go directly to rally point waypoints
			// - No rally point: go to supply route building
			var destinations = hasRallyPoint ? rp.Path : new List<CPos> { self.Location };

			var initialFacing = self.World.Map.FacingBetween(location.Value, destinations[0], WAngle.Zero);

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
					foreach (var cell in destinations)
						newUnit.QueueActivity(move.MoveTo(cell, 2, evaluateNearestMovableCell: true));

				if (!self.IsDead)
					foreach (var t in self.TraitsImplementing<INotifyProduction>())
						t.UnitProduced(self, newUnit, destinations[0]);

				var notifyOthers = self.World.ActorsWithTrait<INotifyOtherProduction>();
				foreach (var notify in notifyOthers)
					notify.Trait.UnitProducedByOther(notify.Actor, self, newUnit, productionType, td);
			});

			return true;
		}
	}

	public class ProductionSpawnLocationInit : ValueActorInit<CPos>
	{
		public ProductionSpawnLocationInit(TraitInfo info, CPos value)
			: base(info, value) { }
	}
}
