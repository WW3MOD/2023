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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common
{
	public enum BuildingType { Building, Defense }

	public enum WaterCheck { NotChecked, EnoughWater, NotEnoughWater, DontCheck }

	public static class AIUtils
	{
		public static bool IsAreaAvailable<T>(World world, Player player, Map map, int radius, HashSet<string> terrainTypes)
		{
			var cells = world.ActorsHavingTrait<T>().Where(a => a.Owner == player);

			// TODO: Properly check building foundation rather than 3x3 area.
			return cells.Select(a => map.FindTilesInCircle(a.Location, radius)
				.Count(c => map.Contains(c) && terrainTypes.Contains(map.GetTerrainInfo(c).Type) &&
					Util.AdjacentCells(world, Target.FromCell(world, c))
						.All(ac => map.Contains(ac) && terrainTypes.Contains(map.GetTerrainInfo(ac).Type))))
							.Any(availableCells => availableCells > 0);
		}

		// Actor.IsIdle (CurrentActivity == null) is the WRONG idleness test for an AIRFRAME and is
		// essentially never true for one. With the default IdleBehavior (None) and the aircraft above
		// LandAltitude, Aircraft.OnBecomingIdle queues FlyIdle (Aircraft.cs:936), and FlyIdle.Tick never
		// returns true while nothing is queued behind it (FlyIdle.cs:39-41). Actor.Tick runs the queued
		// activity in the SAME tick it fires the becoming-idle notification (Actor.cs:290-299), so a bot
		// module can never observe the null-activity window either. A helicopter hovering over its Supply
		// Route therefore carries FlyIdle forever and every `IsIdle` test applied to it is dead code.
		// Hovering on FlyIdle with nothing queued behind it IS doing nothing.
		public static bool IsUnoccupiedAirframe(Actor a)
		{
			if (a.IsIdle)
				return true;

			var current = a.CurrentActivity;
			return current is Activities.FlyIdle && current.NextActivity == null;
		}

		public static ILookup<string, ProductionQueue> FindQueuesByCategory(Player player)
		{
			return player.World.ActorsWithTrait<ProductionQueue>()
				.Where(a => a.Actor.Owner == player && a.Trait.Enabled)
				.Select(a => a.Trait)
				.ToLookup(pq => pq.Info.Type);
		}

		public static int CountActorsWithNameAndTrait<T>(string actorName, Player owner)
		{
			return owner.World.ActorsHavingTrait<T>().Count(a => a.Owner == owner && a.Info.Name == actorName);
		}

		public static int CountActorByCommonName<TTraitInfo>(
			ActorIndex.OwnerAndNamesAndTrait<TTraitInfo> actorIndex) where TTraitInfo : ITraitInfoInterface
		{
			return actorIndex.Actors.Count(a => !a.IsDead);
		}

		// Compatibility wrappers for older bot modules
		public static int CountBuildingByCommonName(HashSet<string> buildingTypes, Player owner)
		{
			return owner.World.ActorsHavingTrait<BuildingInfo>()
				.Count(a => a.Owner == owner && !a.IsDead && buildingTypes.Contains(a.Info.Name));
		}

		public static int CountActorsWithTrait<T>(Player owner)
		{
			return owner.World.ActorsHavingTrait<T>().Count(a => a.Owner == owner && !a.IsDead);
		}

		public static IEnumerable<Actor> FindEnemiesByCommonName(HashSet<string> actorTypes, Player owner)
		{
			return owner.World.Actors.Where(a => !a.IsDead && a.IsInWorld
				&& actorTypes.Contains(a.Info.Name)
				&& owner.RelationshipWith(a.Owner) == PlayerRelationship.Enemy);
		}

		public static void BotDebug(string format, params object[] args)
		{
			if (Game.Settings.Debug.BotDebug)
				TextNotificationsManager.Debug(format, args);
		}

		// WW3MOD: the bot type a one-click "add bot" lands on. Every such path used to roll
		// Game.CosmeticRandom across all shipped types, so the player could not tell which
		// opponent they had just added (upstream OpenRA #18914). SkirmishLogic already seeded
		// the frozen, benchmark-validated profile on a fresh skirmish, so that is the default
		// the lobby now agrees with instead of contradicting.
		// This lives in ONE place because four call sites make this choice and a silent
		// divergence between them is the bug being fixed. Matches on Type, never on Name.
		public const string DefaultBotType = "stable";

		public static string SelectDefaultBotType(IEnumerable<string> botTypes)
		{
			return botTypes.FirstOrDefault(t => t == DefaultBotType) ?? botTypes.FirstOrDefault();
		}

		public static IEnumerable<Order> ClearBlockersOrders(List<CPos> tiles, Player owner, Actor ignoreActor = null)
		{
			var world = owner.World;
			var adjacentTiles = Util.ExpandFootprint(tiles, true).Except(tiles)
				.Where(world.Map.Contains).ToList();

			var blockers = tiles.SelectMany(world.ActorMap.GetActorsAt)
				.Where(a => a.Owner == owner && a.IsIdle && (ignoreActor == null || a != ignoreActor))
				.Select(a => new TraitPair<IMove>(a, a.TraitOrDefault<IMove>()))
				.Where(x => x.Trait != null);

			foreach (var blocker in blockers)
			{
				CPos moveCell;
				if (blocker.Trait is Mobile mobile)
				{
					var availableCells = adjacentTiles.Where(t => mobile.CanEnterCell(t)).ToList();
					if (availableCells.Count == 0)
						continue;

					moveCell = blocker.Actor.ClosestCell(availableCells);
				}
				else if (blocker.Trait is Aircraft)
					moveCell = blocker.Actor.Location;
				else
					continue;

				yield return new Order("Move", blocker.Actor, Target.FromCell(world, moveCell), false)
				{
					SuppressVisualFeedback = true
				};
			}
		}
	}
}
