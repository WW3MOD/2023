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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Manages AI minelayer unit related with " + nameof(Minelayer) + " traits.",
		"When enemy damage AI's actors, the location of conflict will be recorded,",
		"If a location is a valid spot, it will add/merge to favorite location for usage later")]
	public class MinelayerBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Enemy target types to ignore when add the minefield location to conflict location.")]
		public readonly BitSet<TargetableType> IgnoredEnemyTargetTypes = default;

		[Desc("Victim target types that considering conflict location as enemy location instead of victim location.")]
		public readonly BitSet<TargetableType> UseEnemyLocationTargetTypes = default;

		[ActorReference(typeof(MinelayerInfo))]
		[Desc("Actors with " + nameof(Minelayer) + " trait that this module is allowed to task.",
			"EMPTY (the default) means EVERY actor carrying the trait, which is the upstream behaviour.",
			"Set it whenever the trait is shared with a unit whose mines are a secondary payload: in WW3MOD",
			"^E6 engineers carry Minelayer over the same AmmoPool that Demolition spends, so an unset list",
			"spends the demolition charges laying mines and walks non-combat support into a contested lane.")]
		public readonly HashSet<string> MinelayingActorTypes = default;

		[Desc("Find this amount of suitable actors and lay mine to a location.")]
		public readonly int MaxPerAssign = 1;

		[Desc("Scan suitable actors and target in this interval.")]
		public readonly int ScanTick = 320;

		[Desc("Radius per mine laying order.")]
		public readonly int MineFieldRadius = 1;

		[Desc("Minefield location is cancelled if those whose target type belong to allied nearby.")]
		public readonly BitSet<TargetableType> AwayFromAlliedTargetTypes = default;

		[Desc("Minefield location is cancelled if those whose target type belong to enemy nearby.")]
		public readonly BitSet<TargetableType> AwayFromEnemyTargetTypes = default;

		[Desc("Minefield location check distance to AwayFromAlliedTargettype and AwayFromEnemyTargettype.",
			"In addition, if any emeny actor within this range and minefield location is not cancelled,",
			"minelayer will try lay mines at the 3/4 path to minefield location")]
		public readonly int AwayFromCellDistance = 9;

		[Desc("Merge conflict point minefield position to a favorite minefield position if within this range and closest.",
			"If favorite minefield positions is at the max of 5, we always merge it to closest regardless of this")]
		public readonly int FavoritePositionDistance = 6;

		[Desc("Pick the cold-start minefield location from the strategic anchors " + nameof(PoiMap) + " scores",
			"(enemy Supply Route first, then enemy income/utility) instead of from a random enemy actor.",
			"FALSE (the default) keeps the upstream behaviour byte-for-byte.",
			"WHY THIS EXISTS: the upstream cold start reads world.Actors directly, which is an omniscient",
			"scan the fog-respecting profiles forbid — and it is ALSO self-defeating, because Minelayer's",
			"own order handler filters the field to cells the owner has explored, so a target chosen through",
			"fog lays nothing at all and reports no error. A random enemy is not a lane either: it is re-drawn",
			"every ScanTick, so consecutive scans point down different corridors.",
			"FAILS CLOSED: with this set and no PoiMap or no anchor, the cold start is SKIPPED rather than",
			"falling back to the omniscient scan — an unreachable module beats a cheating one.")]
		public readonly bool StrategicAnchorColdStart = false;

		public override object Create(ActorInitializer init) { return new MinelayerBotModule(init.Self, this); }
	}

	public class MinelayerBotModule : ConditionalTrait<MinelayerBotModuleInfo>, IBotTick, IBotRespondToAttack
	{
		const int MaxPositionCacheLength = 5;
		const int RepeatedAltertTicks = 40;

		readonly World world;
		readonly Player player;
		readonly Predicate<Actor> unitCannotBeOrdered;
		readonly Predicate<Actor> unitCannotBeOrderedOrIsBusy;
		readonly CPos?[] conflictPositionQueue;
		readonly CPos?[] favoritePositions;

		int minAssignRoleDelayTicks;
		int conflictPositionLength;
		int favoritePositionsLength;
		int currentFavoritePositionIndex;
		int alertedTicks;

		PathFinder pathFinder;
		PoiMap poiMap;

		public MinelayerBotModule(Actor self, MinelayerBotModuleInfo info)
		: base(info)
		{
			world = self.World;
			player = self.Owner;
			unitCannotBeOrdered = a => a == null || a.IsDead || !a.IsInWorld || a.Owner != player;
			unitCannotBeOrderedOrIsBusy = a => unitCannotBeOrdered(a) || !a.IsIdle;
			conflictPositionQueue = new CPos?[MaxPositionCacheLength] { null, null, null, null, null };
			favoritePositions = new CPos?[MaxPositionCacheLength] { null, null, null, null, null };
		}

		protected override void TraitEnabled(Actor self)
		{
			// Avoid all AIs reevaluating assignments on the same tick, randomize their initial evaluation delay.
			minAssignRoleDelayTicks = world.LocalRandom.Next(0, Info.ScanTick);
			alertedTicks = 0;
			conflictPositionLength = 0;
			favoritePositionsLength = 0;
			currentFavoritePositionIndex = 0;
			pathFinder = self.World.WorldActor.Trait<PathFinder>();

			// TraitOrDefault, not Trait: PoiMap is a WW3MOD experimental-stack trait and this module must
			// still load on a world that has none. StrategicAnchorColdStart is what decides whether a
			// missing one is fatal to the cold start, not this lookup.
			poiMap = self.World.WorldActor.TraitOrDefault<PoiMap>();
		}

		// The minelayers this module is allowed to task. An empty MinelayingActorTypes means "every actor
		// carrying the trait" (upstream behaviour); a populated one is a whitelist.
		TraitPair<Minelayer>[] AvailableMinelayers()
		{
			return world.ActorsWithTrait<Minelayer>()
				.Where(at => !unitCannotBeOrderedOrIsBusy(at.Actor)
					&& (Info.MinelayingActorTypes == null || Info.MinelayingActorTypes.Count == 0
						|| Info.MinelayingActorTypes.Contains(at.Actor.Info.Name)))
				.ToArray();
		}

		// The cold-start target: where to aim when nothing has been fought over yet. Returns null when there
		// is no legal target, and the caller then lays nothing this scan.
		CPos? ColdStartTargetCell()
		{
			if (!Info.StrategicAnchorColdStart)
			{
				// Upstream: a random enemy actor, read straight off world.Actors.
				var enemies = world.Actors.Where(a => IsPreferredEnemyUnit(a)).ToArray();
				return enemies.Length == 0 ? null : enemies.Random(world.LocalRandom).Location;
			}

			if (poiMap == null)
				return null;

			// Mirrors LaneAmbushBotModule's anchor selection exactly, including suppressOmniscientThreat:
			// the anchor POSITIONS are public map facts, but the threat term sampled at them is not.
			// Enemy Supply Routes (Pressure) are the reinforcement sources and so the corridors worth
			// mining; enemy income/utility (Attack) is the fallback before any SR is an offensive target.
			var offensive = poiMap.GetOffensiveTargets(player, suppressOmniscientThreat: true);
			var anchor = offensive.FirstOrDefault(p => p.Action == PoiAction.Pressure);
			if (anchor.Actor == null)
				anchor = offensive.FirstOrDefault(p => p.Action == PoiAction.Attack);

			return anchor.Actor == null ? null : anchor.Location;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (alertedTicks > 0)
				alertedTicks--;

			if (--minAssignRoleDelayTicks <= 0)
			{
				minAssignRoleDelayTicks = Info.ScanTick;

				var minelayingPosition = CPos.Zero;
				var useFavoritePosition = false;
				var layMineOnHalfway = false;

				while (conflictPositionLength > 0)
				{
					minelayingPosition = conflictPositionQueue[0].Value;
					var (hasInvalidActors, hasEnemyNearby) = HasInvalidActorInCircle(world.Map.CenterOfCell(minelayingPosition), WDist.FromCells(Info.AwayFromCellDistance));
					if (hasInvalidActors)
						DequeueFirstConflictPosition();
					else
					{
						layMineOnHalfway = hasEnemyNearby;
						break;
					}
				}

				TraitPair<Minelayer>[] minelayers = null;

				if (conflictPositionLength == 0)
				{
					// If enemy turtle themselves at base and we don't have valid position recorded,
					// we will try find a location that at the middle of pathfinding cells
					if (favoritePositionsLength == 0)
					{
						minelayers = AvailableMinelayers();
						if (minelayers.Length == 0)
							return;

						// Ordering is load-bearing for RNG parity: on the default path this is where the
						// enemy draw happens, and it must stay AFTER the empty-pool return above.
						var coldStart = ColdStartTargetCell();
						if (coldStart == null)
							return;

						foreach (var minelayer in minelayers)
						{
							var cells = pathFinder.FindPathToTargetCell(
								minelayer.Actor, new[] { minelayer.Actor.Location }, coldStart.Value, BlockedByActor.Immovable, laneBias: false);
							if (cells != null && cells.Count != 0)
							{
								AIUtils.BotDebug($"{player}: try find a location to lay mine.");
								EnqueueConflictPosition(cells[cells.Count / 2]);

								// We don't do other things in this tick, just find new location and abort
								return;
							}
						}

						return;
					}
					else
					{
						while (favoritePositionsLength > 0)
						{
							minelayingPosition = favoritePositions[currentFavoritePositionIndex].Value;
							var (hasInvalidActors, hasEnemyNearby) = HasInvalidActorInCircle(world.Map.CenterOfCell(minelayingPosition), WDist.FromCells(Info.AwayFromCellDistance));
							if (hasInvalidActors)
							{
								DeleteCurrentFavoritePosition();
								if (favoritePositionsLength == 0)
									return;
							}
							else
							{
								layMineOnHalfway = hasEnemyNearby;
								useFavoritePosition = true;
								break;
							}
						}
					}
				}

				minelayers ??= AvailableMinelayers();

				if (minelayers.Length == 0)
					return;

				var orderedActors = new List<Actor>();

				foreach (var minelayer in minelayers)
				{
					var cells = pathFinder.FindPathToTargetCell(
						minelayer.Actor, new[] { minelayer.Actor.Location }, minelayingPosition, BlockedByActor.Immovable, laneBias: false);
					if (cells != null && cells.Count != 0)
					{
						orderedActors.Add(minelayer.Actor);

						// if there is enemy actor nearby, we will try to lay mine on
						// 3/4 distance to desired position (the path cell is reversed)
						if (layMineOnHalfway)
						{
							minelayingPosition = cells[cells.Count * 1 / 4];
							layMineOnHalfway = false;
						}

						if (orderedActors.Count >= Info.MaxPerAssign)
							break;
					}
				}

				if (orderedActors.Count > 0)
				{
					if (useFavoritePosition)
					{
						AIUtils.BotDebug($"{player}: Use favorite position {minelayingPosition} at index {currentFavoritePositionIndex}");
						NextFavoritePositionIndex();
					}
					else
					{
						DequeueFirstConflictPosition();
						AddPositionToFavoritePositions(minelayingPosition);
						AIUtils.BotDebug($"{player}: Use in time conflict position {minelayingPosition}");
					}

					var vec = new CVec(Info.MineFieldRadius, Info.MineFieldRadius);
					bot.QueueOrder(
						new Order(
							"PlaceMinefield",
							null,
							Target.FromCell(world, minelayingPosition + vec),
							false,
							groupedActors: orderedActors.ToArray())
						{ ExtraLocation = minelayingPosition - vec });
					bot.QueueOrder(
						new Order(
							"Move",
							null,
							Target.FromCell(world, orderedActors[0].Location),
							true,
							groupedActors: orderedActors.ToArray()));
				}
				else
				{
					if (useFavoritePosition)
						DeleteCurrentFavoritePosition();
					else
						DequeueFirstConflictPosition();
				}
			}
		}

		void DequeueFirstConflictPosition()
		{
			for (var i = 1; i < conflictPositionLength; i++)
				conflictPositionQueue[i - 1] = conflictPositionQueue[i];

			conflictPositionQueue[conflictPositionLength - 1] = null;
			conflictPositionLength--;
		}

		void DeleteCurrentFavoritePosition()
		{
			for (var i = currentFavoritePositionIndex; i < favoritePositionsLength - 1; i++)
				favoritePositions[i] = favoritePositions[i + 1];
			favoritePositions[favoritePositionsLength - 1] = null;

			if (--favoritePositionsLength > 0)
				currentFavoritePositionIndex %= favoritePositionsLength;
		}

		void AddPositionToFavoritePositions(CPos cpos)
		{
			var favoriteDistSquare = Info.FavoritePositionDistance * Info.FavoritePositionDistance;
			var closestIndex = 0;
			var closestDistSquare = int.MaxValue;
			for (var i = 0; i < favoritePositionsLength; i++)
			{
				var lengthsquare = (favoritePositions[i].Value - cpos).LengthSquared;
				if (lengthsquare < closestDistSquare)
				{
					closestIndex = i;
					closestDistSquare = lengthsquare;
				}
			}

			// Add new if there is space
			if (closestDistSquare > favoriteDistSquare && favoritePositionsLength < favoritePositions.Length)
			{
				favoritePositions[favoritePositionsLength] = cpos;
				favoritePositionsLength++;
			}
			else
			{
				var pos = favoritePositions[closestIndex].Value;
				favoritePositions[closestIndex] = (pos - cpos) / 2 + cpos;
			}
		}

		void NextFavoritePositionIndex()
		{
			currentFavoritePositionIndex = (currentFavoritePositionIndex + 1) % favoritePositionsLength;
		}

		bool IsPreferredEnemyUnit(Actor actor)
		{
			if (actor == null || actor.IsDead || player.RelationshipWith(actor.Owner) != PlayerRelationship.Enemy || actor.Info.HasTraitInfo<HuskInfo>())
				return false;

			var targetTypes = actor.GetEnabledTargetTypes();
			return !targetTypes.IsEmpty && !targetTypes.Overlaps(Info.IgnoredEnemyTargetTypes);
		}

		(bool HasInvalidActors, bool HasEnemyNearby) HasInvalidActorInCircle(WPos pos, WDist dist)
		{
			var hasInvalidActor = false;
			var hasEnemyActor = false;
			hasInvalidActor = world.FindActorsInCircle(pos, dist).Any(actor =>
			{
				if (actor.Owner.RelationshipWith(player) == PlayerRelationship.Ally)
				{
					var targetTypes = actor.GetEnabledTargetTypes();
					return !targetTypes.IsEmpty && targetTypes.Overlaps(Info.AwayFromAlliedTargetTypes);
				}

				if (actor.Owner.RelationshipWith(player) == PlayerRelationship.Enemy)
				{
					hasEnemyActor = true;
					var targetTypes = actor.GetEnabledTargetTypes();
					return !targetTypes.IsEmpty && targetTypes.Overlaps(Info.AwayFromEnemyTargetTypes);
				}

				return false;
			});

			return (hasInvalidActor, hasEnemyActor);
		}

		void EnqueueConflictPosition(CPos cell)
		{
			if (conflictPositionLength < MaxPositionCacheLength)
			{
				conflictPositionQueue[conflictPositionLength] = cell;
				conflictPositionLength++;
			}
			else
				conflictPositionQueue[MaxPositionCacheLength - 1] = cell;
		}

		void IBotRespondToAttack.RespondToAttack(IBot bot, Actor self, AttackInfo e)
		{
			if (alertedTicks > 0 || !IsPreferredEnemyUnit(e.Attacker))
				return;

			alertedTicks = RepeatedAltertTicks;

			var hasInvalidActor = HasInvalidActorInCircle(self.CenterPosition, WDist.FromCells(Info.AwayFromCellDistance)).HasInvalidActors;
			if (hasInvalidActor)
				return;

			var targetTypes = self.GetEnabledTargetTypes();
			CPos pos;
			if (!targetTypes.IsEmpty && targetTypes.Overlaps(Info.UseEnemyLocationTargetTypes))
				pos = e.Attacker.Location;
			else
				pos = self.Location;

			EnqueueConflictPosition(pos);
		}
	}
}
