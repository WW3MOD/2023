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
		public const string StartingUnitsOptionId = "startingunits";
		public const string ForwardDeploymentOptionId = "forwarddeployment";

		/// <summary>
		/// Class value that means "no force". Matches <see cref="StartingUnitsInfo.Class"/>'s own default and
		/// the `StartingUnits@none` package, and is what both dropdowns default to.
		/// </summary>
		public const string NoUnitsClass = "none";

		public readonly string StartingUnitsClass = NoUnitsClass;

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

		[Desc("Class of the extra force that starts already in position toward the enemy rather than at the",
			"player's own spawn point. `none` disables forward deployment: no cells are searched, no actors",
			"are created and no shared-random numbers are drawn, so a match left at the default is",
			"bit-for-bit the match it was before this option existed.")]
		public readonly string ForwardDeploymentClass = NoUnitsClass;

		[Desc("Descriptive label for the forward deployment option in the lobby.")]
		public readonly string ForwardDeploymentDropdownLabel = "Forward Deployment";

		[Desc("Tooltip description for the forward deployment option in the lobby.")]
		public readonly string ForwardDeploymentDropdownDescription =
			"An extra force that begins already deployed toward the enemy, instead of at your Supply Route";

		[Desc("Prevent the forward deployment option from being changed in the lobby.")]
		public readonly bool ForwardDeploymentDropdownLocked = false;

		[Desc("Whether to display the forward deployment option in the lobby.")]
		public readonly bool ForwardDeploymentDropdownVisible = true;

		[Desc("Display order for the forward deployment option in the lobby.")]
		public readonly int ForwardDeploymentDropdownDisplayOrder = 7;

		[Desc("How far toward the nearest enemy spawn point the forward force deploys, as a percentage of the",
			"distance between the two spawn points.")]
		public readonly int ForwardDeploymentAdvancePercent = ForwardDeploymentGeometry.DefaultAdvancePercent;

		[Desc("Number of shorter advances tried when the ground at the full advance cannot hold the force.",
			"Step 0 is the player's own spawn point, so the worst case is the ordinary home deployment.")]
		public readonly int ForwardDeploymentRetreatSteps = ForwardDeploymentGeometry.DefaultRetreatSteps;

		[Desc("Valid cells the support annulus must offer every support actor type before an advance is",
			"accepted. Below this the search steps back toward the player's own spawn point.")]
		public readonly int ForwardDeploymentMinValidCells = ForwardDeploymentGeometry.DefaultMinValidCells;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			var startingUnits = new Dictionary<string, string>();

			// Duplicate classes are defined for different race variants
			foreach (var t in map.WorldActorInfo.TraitInfos<StartingUnitsInfo>())
				startingUnits[t.Class] = t.ClassName;

			if (startingUnits.Count > 0)
			{
				yield return new LobbyOption(StartingUnitsOptionId, DropdownLabel, DropdownDescription, DropdownVisible, DropdownDisplayOrder,
					startingUnits, StartingUnitsClass, DropdownLocked);

				// The same package classes, chosen independently: a player can hold a platoon at the Supply
				// Route and push a squad forward. `none` is in the dictionary already — it is the class the
				// `StartingUnits@none` package declares — and is this dropdown's default.
				yield return new LobbyOption(ForwardDeploymentOptionId, ForwardDeploymentDropdownLabel, ForwardDeploymentDropdownDescription,
					ForwardDeploymentDropdownVisible, ForwardDeploymentDropdownDisplayOrder,
					startingUnits, ForwardDeploymentClass, ForwardDeploymentDropdownLocked);
			}
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
				.OptionOrDefault(SpawnStartingUnitsInfo.StartingUnitsOptionId, info.StartingUnitsClass);

			var unitGroup = FindUnitGroup(w, p, spawnClass);

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

			SpawnSupportActors(w, p, unitGroup, p.HomeLocation, null);

			SpawnForwardDeployment(w, p);
		}

		StartingUnitsInfo FindUnitGroup(World w, Player p, string spawnClass)
		{
			return w.Map.Rules.Actors[SystemActors.World].TraitInfos<StartingUnitsInfo>()
				.Where(g => g.Class == spawnClass && g.Factions != null && g.Factions.Contains(p.Faction.InternalName))
				.RandomOrDefault(w.SharedRandom);
		}

		/// <summary>
		/// The second force: already in position toward the enemy rather than clustered at the player's own
		/// Supply Route. No BaseActor — the Supply Route is fixed and stays at home (one per player) — and no
		/// per-map data: the centre is derived from the two spawn points and validated against the ground.
		/// </summary>
		void SpawnForwardDeployment(World w, Player p)
		{
			var forwardClass = w.LobbyInfo.GlobalSettings
				.OptionOrDefault(SpawnStartingUnitsInfo.ForwardDeploymentOptionId, info.ForwardDeploymentClass);

			// Return before touching SharedRandom, the rules or the map: with the option at its default this
			// method must be observably absent, right down to the shared-random stream the rest of the match
			// draws from.
			if (forwardClass == SpawnStartingUnitsInfo.NoUnitsClass)
				return;

			var enemyHomes = w.Players
				.Where(q => q.Playable && q != p && !p.IsAlliedWith(q))
				.Select(q => q.HomeLocation);

			// Nearest enemy spawn is the front the commander would be facing. With several enemies this is the
			// nearest one and not, say, the centroid: a centroid can sit behind the player on a map where the
			// enemies bracket them, and it moves when an unrelated slot fills. Ties break on player order,
			// which is identical on every client.
			if (!ForwardDeploymentGeometry.TryFindNearest(p.HomeLocation, enemyHomes, out var enemyHome))
				return;

			var unitGroup = FindUnitGroup(w, p, forwardClass);

			// Unlike the home force this is not fatal. A player with no starting units at all is unplayable, so
			// that path throws; a player with no FORWARD force still has a normal match, and failing world load
			// over an optional lobby extra would take the whole game down for everyone in the lobby.
			if (unitGroup == null)
			{
				Log.Write("debug", $"No forward deployment units defined for faction {p.Faction.InternalName} with class {forwardClass}");
				return;
			}

			if (unitGroup.SupportActors.Length == 0)
				return;

			var center = SelectDeploymentCenter(w, p.HomeLocation, enemyHome, unitGroup);
			var bearing = ForwardDeploymentGeometry.BearingToward(w.Map.CenterOfCell(center), w.Map.CenterOfCell(enemyHome));

			SpawnSupportActors(w, p, unitGroup, center, bearing);
		}

		/// <summary>
		/// Walks the line from the player's spawn point toward the enemy's, from the full advance back toward
		/// home, and takes the first centre whose annulus offers every support actor type at least
		/// <see cref="SpawnStartingUnitsInfo.ForwardDeploymentMinValidCells"/> cells it could actually stand in.
		/// Failing that it takes the best centre it saw. Step 0 is the home location itself, so the search has
		/// a floor that is known to work rather than an error case.
		/// </summary>
		CPos SelectDeploymentCenter(World w, CPos home, CPos enemyHome, StartingUnitsInfo unitGroup)
		{
			var positionables = unitGroup.SupportActors
				.Select(s => s.ToLowerInvariant())
				.Distinct()
				.Select(s => w.Map.Rules.Actors[s].TraitInfo<IPositionableInfo>())
				.ToArray();

			var steps = Math.Max(1, info.ForwardDeploymentRetreatSteps);
			var bestScore = -1;
			var bestCenter = home;

			for (var step = steps; step >= 0; step--)
			{
				var advance = ForwardDeploymentGeometry.AdvanceAtStep(info.ForwardDeploymentAdvancePercent, steps, step);
				var center = ForwardDeploymentGeometry.AdvancedCenter(home, enemyHome, advance);
				var cells = w.Map.FindTilesInAnnulus(center, unitGroup.InnerSupportRadius + 1, unitGroup.OuterSupportRadius).ToList();

				// Score by the WORST-served actor type, not the total: an annulus that is roomy for infantry and
				// closed to every tracked vehicle is not a place to deploy a motorized package.
				var score = int.MaxValue;
				foreach (var ip in positionables)
				{
					var usable = cells.Count(c => ip.CanEnterCell(w, null, c) && HasUsableEscapeRegion(w, ip, c));
					if (usable < score)
						score = usable;
				}

				if (score > bestScore)
				{
					bestScore = score;
					bestCenter = center;
				}

				if (score >= info.ForwardDeploymentMinValidCells)
					return center;
			}

			return bestCenter;
		}

		// PITFALL (2026-05): a starting unit must spawn in a connected passable region big enough
		// to maneuver, otherwise it can land in a small pocket inside impassable terrain (e.g. one
		// or two open cells deep in a forest) and be stuck. Checking a single neighbor is not enough —
		// the neighbor itself can be in the same tiny pocket. Bounded BFS gives a real escape guarantee.
		const int MinReachableCells = 16;

		static bool HasUsableEscapeRegion(World w, IPositionableInfo posInfo, CPos start)
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

		/// <summary>
		/// Scatters <paramref name="unitGroup"/>'s support actors in the package's annulus around
		/// <paramref name="center"/>. <paramref name="facingOverride"/> is the computed bearing for a forward
		/// deployment, and is ignored when the package names its own SupportActorsFacing — an explicit authored
		/// facing outranks a derived one.
		/// </summary>
		void SpawnSupportActors(World w, Player p, StartingUnitsInfo unitGroup, CPos center, WAngle? facingOverride)
		{
			if (unitGroup.SupportActors.Length == 0)
				return;

			var supportSpawnCells = w.Map.FindTilesInAnnulus(center, unitGroup.InnerSupportRadius + 1, unitGroup.OuterSupportRadius);

			foreach (var s in unitGroup.SupportActors)
			{
				var actorRules = w.Map.Rules.Actors[s.ToLowerInvariant()];
				var ip = actorRules.TraitInfo<IPositionableInfo>();
				var candidates = supportSpawnCells.Shuffle(w.SharedRandom).ToList();

				// PITFALL: (0,0) is a legal cell — most ww3mod maps set Bounds at the origin — so
				// FirstOrDefault's default(CPos) cannot be told apart from a real hit. Search by index.
				var validIndex = candidates.FindIndex(c => ip.CanEnterCell(w, null, c) && HasUsableEscapeRegion(w, ip, c));

				// Fallback for very tight maps: accept any enterable cell rather than dropping the unit.
				if (validIndex < 0)
					validIndex = candidates.FindIndex(c => ip.CanEnterCell(w, null, c));

				if (validIndex < 0)
				{
					Log.Write("debug", $"No cells available to spawn starting unit {s} for player {p}");
					continue;
				}

				var validCell = candidates[validIndex];
				// FreeBlockingSubCell, not ActorMap.FreeSubCell: the CanEnterCell test above passes on a field
				// (it routes through Locomotor, which honours Passable), so a field cell can be chosen here —
				// and the unfiltered call then reported every subcell full and returned Invalid. That is not
				// the same as the FullCell used for non-sharing units: Mobile pins FromSubCell/ToSubCell to it
				// and suppresses recalculation (Mobile.cs:337-338), so the unit kept an out-of-range subcell
				// for the rest of the game.
				var subCell = ip.SharesCell ? w.FreeBlockingSubCell(validCell) : 0;

				// One shared bearing for the whole forward force rather than a per-cell aim: the units are
				// standing in a line facing the front, not converging on a point.
				var facing = unitGroup.SupportActorsFacing ?? facingOverride ?? new WAngle(w.SharedRandom.Next(1024));

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
