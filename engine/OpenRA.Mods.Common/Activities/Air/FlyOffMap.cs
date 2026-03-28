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

using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class FlyOffMap : Activity
	{
		readonly Aircraft aircraft;
		readonly Target target;
		readonly bool hasTarget;
		int endingDelay;

		public FlyOffMap(Actor self, int endingDelay = 25)
		{
			aircraft = self.Trait<Aircraft>();
			ChildHasPriority = false;
			this.endingDelay = endingDelay;
		}

		public FlyOffMap(Actor self, in Target target, int endingDelay = 25)
			: this(self, endingDelay)
		{
			this.target = target;
			hasTarget = true;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (hasTarget)
			{
				QueueChild(new Fly(self, target));
				QueueChild(new FlyForward(self));
				return;
			}

			// VTOLs must take off first if they're not at cruise altitude
			if (aircraft.Info.VTOL && self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition) != aircraft.Info.CruiseAltitude)
				QueueChild(new TakeOff(self));

			// Fly toward closest point in the SpawnArea evacuation zone, then off-map
			var edgeTarget = FindClosestEvacEdge(self) ?? self.World.Map.ChooseClosestEdgeCell(self.Owner.HomeLocation);
			QueueChild(new Fly(self, Target.FromCell(self.World, edgeTarget)));
			QueueChild(new FlyForward(self));
		}

		/// <summary>
		/// Find the edge cell in the aircraft evacuation zone (~15 tiles either side of SpawnArea)
		/// that is closest to the aircraft, so it takes the shortest path off-map.
		/// </summary>
		static CPos? FindClosestEvacEdge(Actor self)
		{
			var spawnArea = FindOwnerSpawnArea(self);
			if (!spawnArea.HasValue)
				return null;

			// Wide zone: ~30 edge cells around the spawn point (15 each side)
			var candidates = self.World.Map.GetSpawnCandidatesOnSameEdge(spawnArea.Value, 30);
			if (candidates.Length == 0)
				return null;

			// Pick the candidate closest to the aircraft for shortest exit path
			return candidates.OrderBy(c => (self.Location - c).LengthSquared).First();
		}

		static CPos? FindOwnerSpawnArea(Actor self)
		{
			var ownSR = self.World.ActorsHavingTrait<ProductionFromMapEdge>()
				.FirstOrDefault(a => !a.IsDead && a.IsInWorld && a.Owner == self.Owner);
			var anchor = ownSR?.Location ?? self.Owner.HomeLocation;

			var spawnAreas = self.World.ActorsWithTrait<SpawnArea>()
				.Where(a => !a.Actor.IsDead && a.Actor.IsInWorld)
				.Select(a => a.Actor)
				.ToList();

			if (spawnAreas.Count == 0)
				return null;

			CPos? closest = null;
			var closestDist = int.MaxValue;
			foreach (var sa in spawnAreas)
			{
				var dist = (anchor - sa.Location).LengthSquared;
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = sa.Location;
				}
			}

			return closest;
		}

		public override bool Tick(Actor self)
		{
			// Refuse to take off if it would land immediately again.
			if (aircraft.ForceLanding)
				Cancel(self);

			if (IsCanceling)
				return true;

			if (!self.World.Map.Contains(self.Location) && --endingDelay < 0)
				ChildActivity.Cancel(self);

			return TickChild(self);
		}
	}
}
