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
	[Desc("Sends idle infantry to garrison friendly defense structures and nearby buildings.")]
	public class GarrisonBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Actor types eligible for garrisoning (infantry only).")]
		public readonly HashSet<string> GarrisonActorTypes = new HashSet<string>();

		[Desc("Maximum number of garrison orders to issue per scan.")]
		public readonly int MaxOrdersPerTick = 3;

		[Desc("Delay (in ticks) between garrison scans.")]
		public readonly int ScanInterval = 150;

		[Desc("Maximum distance in cells from base to look for buildings to garrison.")]
		public readonly int MaxGarrisonRadius = 20;

		[Desc("Prefer buildings closer to enemies (uses ThreatMapManager if available).")]
		public readonly bool PrioritizeExposed = true;

		public override object Create(ActorInitializer init) { return new GarrisonBotModule(init.Self, this); }
	}

	public class GarrisonBotModule : ConditionalTrait<GarrisonBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		IBot bot;
		BotBlackboard blackboard;
		ThreatMapManager threatMap;
		CPos baseCenter;
		int scanCountdown;
		bool initialized;

		// Track which buildings we've already assigned garrison orders to avoid spamming
		readonly Dictionary<Actor, int> garrisonedBuildings = new Dictionary<Actor, int>();

		public GarrisonBotModule(Actor self, GarrisonBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		void IBotEnabled.BotEnabled(IBot bot)
		{
			this.bot = bot;
		}

		void Initialize()
		{
			if (initialized)
				return;

			threatMap = world.WorldActor.TraitOrDefault<ThreatMapManager>();
			blackboard = player.PlayerActor.TraitsImplementing<BotBlackboard>().FirstOrDefault(b => !b.IsTraitDisabled);

			var bases = world.ActorsHavingTrait<Building>()
				.Where(a => a.Owner == player)
				.ToList();

			baseCenter = bases.Count > 0
				? bases.Random(world.LocalRandom).Location
				: player.HomeLocation;

			initialized = true;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (--scanCountdown > 0)
				return;

			scanCountdown = Info.ScanInterval;
			Initialize();

			// Clean up dead buildings from tracking
			var deadBuildings = garrisonedBuildings.Keys.Where(a => a.IsDead || !a.IsInWorld).ToList();
			foreach (var b in deadBuildings)
				garrisonedBuildings.Remove(b);

			// Find garrisonable buildings near our base
			var garrisonableBuildings = world.ActorsHavingTrait<GarrisonManager>()
				.Where(a => !a.IsDead && a.IsInWorld
					&& (a.Owner == player || a.Owner.RelationshipWith(player) == PlayerRelationship.Neutral)
					&& (a.Location - baseCenter).Length <= Info.MaxGarrisonRadius)
				.ToList();

			if (garrisonableBuildings.Count == 0)
				return;

			// Sort by priority: buildings closer to enemy threat first
			if (Info.PrioritizeExposed && threatMap != null)
			{
				garrisonableBuildings.Sort((a, b) =>
				{
					var threatA = threatMap.GetThreat(a.Location, player);
					var threatB = threatMap.GetThreat(b.Location, player);
					return threatB.CompareTo(threatA); // Higher threat = more exposed = higher priority
				});
			}

			// Find available infantry to garrison
			var availableInfantry = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player
					&& a.IsIdle
					&& !a.IsDead
					&& a.IsInWorld
					&& IsGarrisonEligible(a)
					&& !IsClaimedByOtherModule(a))
				.ToList();

			if (availableInfantry.Count == 0)
				return;

			var ordersIssued = 0;

			foreach (var building in garrisonableBuildings)
			{
				if (ordersIssued >= Info.MaxOrdersPerTick)
					break;

				var cargo = building.TraitOrDefault<Cargo>();
				if (cargo == null || !cargo.HasSpace(1))
					continue;

				// Find the closest eligible infantry
				var infantry = availableInfantry
					.OrderBy(a => (a.Location - building.Location).LengthSquared)
					.FirstOrDefault();

				if (infantry == null)
					break;

				// Issue garrison order (EnterTransport is how infantry enter garrisoned buildings)
				bot.QueueOrder(new Order("EnterTransport", infantry, Target.FromActor(building), false));

				// Claim the unit so other modules don't steal it
				if (blackboard != null)
					blackboard.ClaimUnit(infantry, "garrison");

				availableInfantry.Remove(infantry);

				if (!garrisonedBuildings.ContainsKey(building))
					garrisonedBuildings[building] = 0;
				garrisonedBuildings[building]++;

				ordersIssued++;
			}
		}

		bool IsGarrisonEligible(Actor a)
		{
			// Only use specified infantry types, or if none specified, any infantry with Passenger trait
			if (Info.GarrisonActorTypes.Count > 0)
				return Info.GarrisonActorTypes.Contains(a.Info.Name);

			// Default: any infantry that can be a passenger
			return a.Info.HasTraitInfo<PassengerInfo>();
		}

		bool IsClaimedByOtherModule(Actor a)
		{
			if (blackboard == null)
				return false;

			var claimant = blackboard.GetUnitClaimant(a);
			return claimant != null && claimant != "garrison";
		}

		protected override void TraitDisabled(Actor self)
		{
			garrisonedBuildings.Clear();
		}
	}
}
