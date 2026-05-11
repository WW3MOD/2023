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
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Spawn base actor at the spawnpoint and support units in an annulus around the base actor. Both are defined at MPStartUnits. Attach this to the world actor.")]
	public class SpawnStartingUnitsInfo : TraitInfo, Requires<StartingUnitsInfo>, NotBefore<LocomotorInfo>, ILobbyOptions
	{
		public readonly string StartingUnitsClass = "none";

		[Desc("Descriptive label for the starting units option in the lobby.")]
		public readonly string DropdownLabel = "Starting Units";

		[Desc("Tooltip description for the starting units option in the lobby.")]
		public readonly string DropdownDescription = "The units that players start the game with";

		[Desc("Prevent the starting units option from being changed in the lobby.")]
		public readonly bool DropdownLocked = false;

		[Desc("Whether to display the starting units option in the lobby.")]
		public readonly bool DropdownVisible = true;

		[Desc("Display order for the starting units option in the lobby.")]
		public readonly int DropdownDisplayOrder = 6;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			var startingUnits = new Dictionary<string, string>();

			// Duplicate classes are defined for different race variants
			foreach (var t in map.WorldActorInfo.TraitInfos<StartingUnitsInfo>())
				startingUnits[t.Class] = t.ClassName;

			if (startingUnits.Count > 0)
				yield return new LobbyOption("startingunits", DropdownLabel, DropdownDescription, DropdownVisible, DropdownDisplayOrder,
					startingUnits, StartingUnitsClass, DropdownLocked);
		}

		public override object Create(ActorInitializer init) { return new SpawnStartingUnits(this); }
	}

	public class SpawnStartingUnits : IWorldLoaded
	{
		readonly SpawnStartingUnitsInfo info;

		public SpawnStartingUnits(SpawnStartingUnitsInfo info)
		{
			this.info = info;
		}

		public void WorldLoaded(World world, WorldRenderer wr)
		{
			foreach (var p in world.Players)
				if (p.Playable)
					SpawnUnitsForPlayer(world, p);
		}

		void SpawnUnitsForPlayer(World w, Player p)
		{
			var spawnClass = p.PlayerReference.StartingUnitsClass ?? w.LobbyInfo.GlobalSettings
				.OptionOrDefault("startingunits", info.StartingUnitsClass);

			var unitGroup = w.Map.Rules.Actors[SystemActors.World].TraitInfos<StartingUnitsInfo>()
				.Where(g => g.Class == spawnClass && g.Factions != null && g.Factions.Contains(p.Faction.InternalName))
				.RandomOrDefault(w.SharedRandom);

			if (unitGroup == null)
				throw new InvalidOperationException($"No starting units defined for faction {p.Faction.InternalName} with class {spawnClass}");

			if (unitGroup.BaseActor != null)
			{
				var facing = unitGroup.BaseActorFacing.HasValue ? unitGroup.BaseActorFacing.Value : new WAngle(w.SharedRandom.Next(1024));
				w.CreateActor(unitGroup.BaseActor.ToLowerInvariant(), new TypeDictionary
				{
					new LocationInit(p.HomeLocation + unitGroup.BaseActorOffset),
					new OwnerInit(p),
					new SkipMakeAnimsInit(),
					new FacingInit(facing),
				});
			}

			if (unitGroup.SupportActors.Length == 0)
				return;

			var supportSpawnCells = w.Map.FindTilesInAnnulus(p.HomeLocation, unitGroup.InnerSupportRadius + 1, unitGroup.OuterSupportRadius);

			// PITFALL (2026-05): a starting unit must spawn in a connected passable region big enough
			// to maneuver, otherwise it can land in a small pocket inside impassable terrain (e.g. one
			// or two open cells deep in a forest) and be stuck. Checking a single neighbor is not enough —
			// the neighbor itself can be in the same tiny pocket. Bounded BFS gives a real escape guarantee.
			const int MinReachableCells = 16;
			bool HasUsableEscapeRegion(IPositionableInfo posInfo, CPos start)
			{
				var visited = new HashSet<CPos> { start };
				var queue = new Queue<CPos>();
				queue.Enqueue(start);

				while (queue.Count > 0 && visited.Count < MinReachableCells)
				{
					var cell = queue.Dequeue();
					for (var dy = -1; dy <= 1; dy++)
						for (var dx = -1; dx <= 1; dx++)
						{
							if (dx == 0 && dy == 0)
								continue;
							var n = cell + new CVec(dx, dy);
							if (!w.Map.Contains(n) || visited.Contains(n))
								continue;
							if (!posInfo.CanEnterCell(w, null, n))
								continue;
							visited.Add(n);
							if (visited.Count >= MinReachableCells)
								return true;
							queue.Enqueue(n);
						}
				}

				return visited.Count >= MinReachableCells;
			}

			foreach (var s in unitGroup.SupportActors)
			{
				var actorRules = w.Map.Rules.Actors[s.ToLowerInvariant()];
				var ip = actorRules.TraitInfo<IPositionableInfo>();
				var candidates = supportSpawnCells.Shuffle(w.SharedRandom).ToList();
				var validCell = candidates.FirstOrDefault(c => ip.CanEnterCell(w, null, c) && HasUsableEscapeRegion(ip, c));

				// Fallback for very tight maps: accept any enterable cell rather than dropping the unit.
				if (validCell == CPos.Zero)
					validCell = candidates.FirstOrDefault(c => ip.CanEnterCell(w, null, c));

				if (validCell == CPos.Zero)
				{
					Log.Write("debug", $"No cells available to spawn starting unit {s} for player {p}");
					continue;
				}

				var subCell = ip.SharesCell ? w.ActorMap.FreeSubCell(validCell) : 0;
				var facing = unitGroup.SupportActorsFacing.HasValue ? unitGroup.SupportActorsFacing.Value : new WAngle(w.SharedRandom.Next(1024));

				w.CreateActor(s.ToLowerInvariant(), new TypeDictionary
				{
					new OwnerInit(p),
					new LocationInit(validCell),
					new SubCellInit(subCell),
					new FacingInit(facing),
				});
			}
		}
	}
}
