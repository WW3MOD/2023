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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Controls AI unit production.")]
	public class UnitBuilderBotModuleInfo : ConditionalTraitInfo
	{
		// TODO: Investigate whether this might the (or at least one) reason why bots occasionally get into a state of doing nothing.
		// Reason: If this is less than SquadSize, the bot might get stuck between not producing more units due to this,
		// but also not creating squads since there aren't enough idle units.
		[Desc("Only produce units as long as there are less than this amount of units idling inside the base.")]
		public readonly int IdleBaseUnitsMaximum = 12;

		[Desc("Production queues AI uses for producing units.")]
		public readonly HashSet<string> UnitQueues = new HashSet<string> { "Vehicle", "Infantry", "Plane", "Ship", "Aircraft" };

		[Desc("What units to the AI should build.", "What relative share of the total army must be this type of unit.")]
		public readonly Dictionary<string, int> UnitsToBuild = null;

		[Desc("What units should the AI have a maximum limit to train.")]
		public readonly Dictionary<string, int> UnitLimits = null;

		[Desc("When should the AI start train specific units.")]
		public readonly Dictionary<string, int> UnitDelays = null;

		[Desc("If true, skip the rearm building capacity check for aircraft.",
			"Use this when aircraft are produced from a generic production building (e.g. Supply Route)",
			"and don't require a dedicated pad/airfield to be built first.")]
		public readonly bool SkipRearmBuildingCheck = false;

		[Desc("EXPERIMENTAL (early-econ behaviour 1): don't call in resupply units (supply trucks) while",
			"no fielded unit a truck can rearm has meaningful ammo need. A truck bought while every unit",
			"is full just sits as a target. Simple CURRENT-need gate — reads the SAME signal SupplyProvider",
			"uses (missing ammo weighted by SupplyValue over capacity, over units whose Rearmable.RearmActors",
			"lists a ResupplyUnitType). Designed so an anticipated-need model can replace the predicate later.",
			"Default false ⇒ frozen production, byte-identical for the normal/rush/turtle/stable profiles.")]
		public readonly bool GateResupplyOnAmmoNeed = false;

		[Desc("Resupply actor types gated by GateResupplyOnAmmoNeed (e.g. supply trucks). Inert unless that flag is set.")]
		public readonly HashSet<string> ResupplyUnitTypes = new HashSet<string>();

		[Desc("Ammo-need fraction (0-1) at/above which a fielded unit counts as needing resupply.",
			"Mirrors SupplyProvider.MinNeedThreshold so a near-full unit (e.g. 499/500) does not trigger a truck.")]
		public readonly float ResupplyNeedThreshold = 0.05f;

		[Desc("EXPERIMENTAL (early-econ behaviour 2): cap gated AA call-ins (the expensive vehicle SHORAD/",
			"Tunguska) to the OBSERVED enemy air threat. Cheap AA infantry stay ungated as a baseline picket.",
			"observedAir is fog-legal — only enemy aircraft the player can currently see. Prevents fielding",
			"multiple vehicle AA at game start when no air has been seen. Default false ⇒ frozen, byte-identical",
			"for the normal/rush/turtle/stable profiles.")]
		public readonly bool ScaleAntiAirToThreat = false;

		[Desc("Vehicle-AA actor types gated by ScaleAntiAirToThreat. Inert unless that flag is set.")]
		public readonly HashSet<string> AntiAirUnitTypes = new HashSet<string>();

		[Desc("Gated AA units permitted with ZERO observed enemy air (a small standing picket; 0 = none until air is seen).")]
		public readonly int AntiAirBaseline = 0;

		[Desc("Extra gated AA units permitted per observed enemy aircraft.")]
		public readonly int AntiAirPerObservedAir = 1;

		public override object Create(ActorInitializer init) { return new UnitBuilderBotModule(init.Self, this); }
	}

	public class UnitBuilderBotModule : ConditionalTrait<UnitBuilderBotModuleInfo>, IBotTick, IBotNotifyIdleBaseUnits, IBotRequestUnitProduction, IGameSaveTraitData
	{
		public const int FeedbackTime = 30; // ticks; = a bit over 1s. must be >= netlag.

		readonly World world;
		readonly Player player;

		readonly List<string> queuedBuildRequests = new List<string>();

		IBotRequestPauseUnitProduction[] requestPause;
		int idleUnitCount;

		int ticks;

		public UnitBuilderBotModule(Actor self, UnitBuilderBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void Created(Actor self)
		{
			requestPause = self.Owner.PlayerActor.TraitsImplementing<IBotRequestPauseUnitProduction>().ToArray();
		}

		void IBotNotifyIdleBaseUnits.UpdatedIdleBaseUnits(List<Actor> idleUnits)
		{
			idleUnitCount = idleUnits.Count;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (requestPause.Any(rp => rp.PauseUnitProduction))
				return;

			ticks++;

			if (ticks % FeedbackTime == 0)
			{
				var buildRequest = queuedBuildRequests.FirstOrDefault();
				if (buildRequest != null)
				{
					BuildUnit(bot, buildRequest);
					queuedBuildRequests.Remove(buildRequest);
				}

				foreach (var q in Info.UnitQueues)
					BuildUnit(bot, q, idleUnitCount < Info.IdleBaseUnitsMaximum);
			}
		}

		void IBotRequestUnitProduction.RequestUnitProduction(IBot bot, string requestedActor)
		{
			queuedBuildRequests.Add(requestedActor);
		}

		int IBotRequestUnitProduction.RequestedProductionCount(IBot bot, string requestedActor)
		{
			return queuedBuildRequests.Count(r => r == requestedActor);
		}

		void BuildUnit(IBot bot, string category, bool buildRandom)
		{
			// Pick a free queue
			var queue = AIUtils.FindQueuesByCategory(player)[category].FirstOrDefault(q => !q.AllQueued().Any());
			if (queue == null)
				return;

			var unit = buildRandom ?
				ChooseRandomUnitToBuild(queue) :
				ChooseUnitToBuild(queue);

			if (unit == null)
				return;

			var name = unit.Name;

			if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
				return;

			if (Info.UnitDelays != null &&
				Info.UnitDelays.ContainsKey(name) &&
				Info.UnitDelays[name] > world.WorldTick)
				return;

			if (Info.UnitLimits != null &&
				Info.UnitLimits.ContainsKey(name) &&
				world.Actors.Count(a => a.Owner == player && a.Info.Name == name) >= Info.UnitLimits[name])
				return;

			// EXPERIMENTAL early-econ gates (default-off; only the @experimental UnitBuilder twin enables them,
			// so normal/rush/turtle/stable reach QueueOrder byte-identically). Both draw ZERO random.
			if (Info.GateResupplyOnAmmoNeed && Info.ResupplyUnitTypes.Contains(name) && !AnyFieldedUnitNeedsResupply())
				return;

			if (Info.ScaleAntiAirToThreat && Info.AntiAirUnitTypes.Contains(name) && !ShouldBuildMoreAntiAir())
				return;

			bot.QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		// Behaviour 1: is there meaningful ammo need among fielded units a gated truck can rearm? Mirrors
		// SupplyProvider's own metric (ResupplyDemand.UnitNeed) over each such unit's truck-rearmable pools,
		// short-circuiting on the first needy unit. Pure decision in ResupplyDemand; this only reads trait state.
		bool AnyFieldedUnitNeedsResupply()
		{
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null || !rearmable.Info.RearmActors.Overlaps(Info.ResupplyUnitTypes))
					continue;

				var pools = rearmable.RearmableAmmoPools;
				if (pools == null || pools.Length == 0)
					continue;

				var need = ResupplyDemand.UnitNeed(pools.Select(p => (p.Info.Ammo, p.CurrentAmmoCount, p.Info.SupplyValue)));
				if (ResupplyDemand.MeetsThreshold(need, Info.ResupplyNeedThreshold))
					return true;
			}

			return false;
		}

		// Behaviour 2: allow another gated AA unit only while owned count is under the observed-air cap.
		bool ShouldBuildMoreAntiAir()
		{
			var owned = world.Actors.Count(a => a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.AntiAirUnitTypes.Contains(a.Info.Name));

			return AntiAirDemand.ShouldBuildMore(owned, CountObservedEnemyAir(),
				Info.AntiAirBaseline, Info.AntiAirPerObservedAir);
		}

		// Fog-legal enemy-air count: only aircraft the player can currently VIEW (no omniscient read).
		// Mirrors AdaptiveProductionBotModule.ScanEnemyComposition's aircraft branch.
		int CountObservedEnemyAir()
		{
			var count = 0;
			foreach (var actor in world.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null)
					continue;

				if (player.RelationshipWith(actor.Owner) != PlayerRelationship.Enemy)
					continue;

				if (!actor.CanBeViewedByPlayer(player))
					continue;

				if (actor.Info.HasTraitInfo<AircraftInfo>())
					count++;
			}

			return count;
		}

		// In cases where we want to build a specific unit but don't know the queue name (because there's more than one possibility)
		void BuildUnit(IBot bot, string name)
		{
			var actorInfo = world.Map.Rules.Actors[name];
			if (actorInfo == null)
				return;

			var buildableInfo = actorInfo.TraitInfoOrDefault<BuildableInfo>();
			if (buildableInfo == null)
				return;

			ProductionQueue queue = null;
			foreach (var pq in buildableInfo.Queue)
			{
				queue = AIUtils.FindQueuesByCategory(player)[pq].FirstOrDefault(q => !q.AllQueued().Any());
				if (queue != null)
					break;
			}

			if (queue != null)
			{
				bot.QueueOrder(Order.StartProduction(queue.Actor, name, 1));
				AIUtils.BotDebug("{0} decided to build {1} (external request)", queue.Actor.Owner, name);
			}
		}

		ActorInfo ChooseRandomUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var unit = buildableThings.Random(world.LocalRandom);
			return HasAdequateAirUnitReloadBuildings(unit) ? unit : null;
		}

		ActorInfo ChooseUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var myUnits = player.World
				.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == player)
				.Select(a => a.Info.Name).ToList();

			foreach (var unit in Info.UnitsToBuild.Shuffle(world.LocalRandom))
				if (buildableThings.Any(b => b.Name == unit.Key))
					if (myUnits.Count(a => a == unit.Key) * 100 < unit.Value * myUnits.Count)
						if (HasAdequateAirUnitReloadBuildings(world.Map.Rules.Actors[unit.Key]))
							return world.Map.Rules.Actors[unit.Key];

			return null;
		}

		// For mods like RA (number of RearmActors must match the number of aircraft).
		// WW3MOD: Aircraft are called in via Supply Route and don't need a dedicated
		// pad to be produced — SkipRearmBuildingCheck bypasses this for reinforcement-model mods.
		bool HasAdequateAirUnitReloadBuildings(ActorInfo actorInfo)
		{
			if (Info.SkipRearmBuildingCheck)
				return true;

			var aircraftInfo = actorInfo.TraitInfoOrDefault<AircraftInfo>();
			if (aircraftInfo == null)
				return true;

			// If actor isn't Rearmable, it doesn't need a RearmActor to reload
			var rearmableInfo = actorInfo.TraitInfoOrDefault<RearmableInfo>();
			if (rearmableInfo == null)
				return true;

			var countOwnAir = AIUtils.CountActorsWithNameAndTrait<IPositionable>(actorInfo.Name, player);
			var countBuildings = rearmableInfo.RearmActors.Sum(b => AIUtils.CountActorsWithNameAndTrait<Building>(b, player));
			if (countOwnAir >= countBuildings)
				return false;

			return true;
		}

		List<MiniYamlNode> IGameSaveTraitData.IssueTraitData(Actor self)
		{
			if (IsTraitDisabled)
				return null;

			return new List<MiniYamlNode>()
			{
				new MiniYamlNode("QueuedBuildRequests", FieldSaver.FormatValue(queuedBuildRequests.ToArray())),
				new MiniYamlNode("IdleUnitCount", FieldSaver.FormatValue(idleUnitCount))
			};
		}

		void IGameSaveTraitData.ResolveTraitData(Actor self, MiniYaml data)
		{
			if (self.World.IsReplay)
				return;

			var queuedBuildRequestsNode = data.Nodes.FirstOrDefault(n => n.Key == "QueuedBuildRequests");
			if (queuedBuildRequestsNode != null)
			{
				queuedBuildRequests.Clear();
				queuedBuildRequests.AddRange(FieldLoader.GetValue<string[]>("QueuedBuildRequests", queuedBuildRequestsNode.Value.Value));
			}

			var idleUnitCountNode = data.Nodes.FirstOrDefault(n => n.Key == "IdleUnitCount");
			if (idleUnitCountNode != null)
				idleUnitCount = FieldLoader.GetValue<int>("IdleUnitCount", idleUnitCountNode.Value.Value);
		}
	}
}
