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

		[Desc("Phase 2 commit-on-order audit (§4): recruit infantry only from the ledger-checked free pool AND",
			"commit each garrisoned unit to the shared PoiGoalGuard ledger (key garrison:<buildingId>). Today this",
			"module is ledger-blind — its only lock is BotBlackboard.ClaimUnit, invisible to the POI stack, so it",
			"and offense can both grab the same infantry. IMPORTANT: this is a SHARED enable-ai-any module (one",
			"instance runs for BOTH bots and for Normal/legacy), so the flag alone can't confine it to @experimental",
			"— the commit + ledger-read fire ONLY for the @experimental player (explicit BotType gate, §6). Off /",
			"non-experimental / no PoiGoalGuard ⇒ inert ⇒ byte-identical for @stable / Normal / legacy.")]
		public readonly bool CommitGarrisonedUnits = false;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase).
			ActorNameCase.NormalizeInPlace(GarrisonActorTypes);
		}

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

		// Phase 2 commit-on-order (§4). Shared enable-ai-any module: goalGuard is resolved ONLY for the
		// @experimental player with the flag on (isExperimentalBot gates every use via ShouldCommitShared),
		// so Normal / legacy / @stable stay byte-identical. Null for every other profile.
		PoiGoalGuard goalGuard;
		bool isExperimentalBot;

		bool LedgerActive => CommitOnOrderMath.ShouldCommitShared(
			Info.CommitGarrisonedUnits, goalGuard != null && !goalGuard.IsTraitDisabled, isExperimentalBot);

		static string GarrisonObjectiveKey(Actor building) => "garrison:" + building.ActorID;

		// Track which buildings we've already assigned garrison orders to avoid spamming
		readonly Dictionary<Actor, int> garrisonedBuildings = new Dictionary<Actor, int>();

		// Units this module currently holds a BotBlackboard claim on. The claim used to be write-only — taken
		// at order time and never released — which permanently removed the unit from every other module's pool
		// (they all skip actors GetUnitClaimant reports to someone else). Combined with the over-wide
		// eligibility fallback below that froze SUPPLY TRUCKS for the rest of the match. Held here so the
		// claims can be released when the errand ends, and dropped wholesale in TraitDisabled.
		readonly HashSet<Actor> claimedUnits = new HashSet<Actor>();

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

			// Commit-on-order (§4): resolve the shared ledger only for the @experimental bot when the flag is on.
			isExperimentalBot = player.BotType == InfluenceStack.ExperimentalBotType;
			goalGuard = Info.CommitGarrisonedUnits
				? player.PlayerActor.TraitOrDefault<PoiGoalGuard>() : null;

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

			ReleaseFinishedClaims();

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
					&& !IsClaimedByOtherModule(a)
						// Commit-on-order (§4): for @experimental, also skip units another POI-stack writer
						// (offense / capture / defense) already committed in the shared ledger. Inert otherwise.
						&& (!LedgerActive || !goalGuard.Ledger.IsCommitted(a, world.WorldTick)))
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

				// Find the closest eligible infantry THIS building can actually accept. The cargo-type match
				// is the same test Passenger.ResolveOrder applies when the order lands, so without it the
				// module happily issues EnterTransport orders that are guaranteed no-ops — and then claims
				// the unit for an errand that can never start. That is how supply trucks got frozen: TRUK
				// inherits Passenger (CargoType: Vehicle) from ^WheeledVehicle, so it passed the
				// PassengerInfo eligibility fallback, while garrison buildings take Types: Infantry.
				var infantry = availableInfantry
					.Where(a => CanEnter(a, cargo))
					.OrderBy(a => (a.Location - building.Location).LengthSquared)
					.FirstOrDefault();

				if (infantry == null)
					continue;

				// Issue garrison order (EnterTransport is how infantry enter garrisoned buildings)
				bot.QueueOrder(new Order("EnterTransport", infantry, Target.FromActor(building), false));

				// Claim the unit so other modules don't steal it. Recorded so ReleaseFinishedClaims can hand
				// it back when the errand ends — an unreleased claim is a permanently unusable unit.
				if (blackboard != null && blackboard.ClaimUnit(infantry, "garrison"))
					claimedUnits.Add(infantry);

				// Commit-on-order (§4): also stake it in the SHARED ledger (garrison:<buildingId>) so the POI
				// stack (which doesn't read the blackboard) defers too. Released via ledger TTL / Prune once the
				// unit is inside the building (it leaves the world, so it's unorderable anyway). @experimental only.
				if (LedgerActive)
					goalGuard.Ledger.Commit(infantry, GarrisonObjectiveKey(building), world.WorldTick, goalGuard.DefaultCommitmentTicks);

				availableInfantry.Remove(infantry);

				if (!garrisonedBuildings.ContainsKey(building))
					garrisonedBuildings[building] = 0;
				garrisonedBuildings[building]++;

				ordersIssued++;
			}
		}

		/// <summary>Can this unit actually be loaded into this building? Mirrors Passenger.IsCorrectCargoType
		/// (Passenger.cs:113-121) — the check the order itself runs when it lands — so the module stops issuing
		/// EnterTransport orders that are guaranteed to be dropped, and stops claiming units for them.</summary>
		static bool CanEnter(Actor passenger, Cargo cargo)
		{
			if (cargo.LoadingBlocked)
				return false;

			var cargoType = passenger.Info.TraitInfoOrDefault<PassengerInfo>()?.CargoType;
			return cargoType != null && cargo.Info.Types.Contains(cargoType);
		}

		/// <summary>Hand back the claims on units whose garrison errand is over, so they return to the pool the
		/// other modules recruit from. Released when the unit is dead, has left the world (it is inside the
		/// building — the errand SUCCEEDED, and a sheltered passenger is unorderable so the claim buys nothing),
		/// or has gone idle again (the order completed, failed, or was overridden by another writer).
		///
		/// <para>Without this the claim was permanent: GarrisonBotModule never called ReleaseUnit anywhere, so
		/// any unit it ever ordered was invisible to every other module for the rest of the match.</para></summary>
		void ReleaseFinishedClaims()
		{
			if (blackboard == null || claimedUnits.Count == 0)
				return;

			var finished = claimedUnits.Where(a => a == null || a.IsDead || !a.IsInWorld || a.IsIdle).ToList();
			foreach (var a in finished)
			{
				if (a != null)
					blackboard.ReleaseUnit(a);

				claimedUnits.Remove(a);
			}
		}

		bool IsGarrisonEligible(Actor a)
		{
			// Only use specified actor types, or if none specified, anything that can be a passenger.
			if (Info.GarrisonActorTypes.Count > 0)
				return Info.GarrisonActorTypes.Contains(a.Info.Name);

			// NOTE this fallback is wider than the trait's name suggests: it admits ANY Passenger holder, not
			// just infantry. In WW3MOD ^WheeledVehicle grants Passenger (vehicles.yaml:116-123), so supply
			// trucks and other vehicles reach it. GarrisonActorTypes is unset in mod YAML, so this IS the live
			// path. The narrowing that makes it correct is the per-building CanEnter cargo-type match at the
			// pairing site — do not drop it and rely on this predicate alone.
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
			if (blackboard != null)
				foreach (var a in claimedUnits)
					blackboard.ReleaseUnit(a);

			claimedUnits.Clear();
			garrisonedBuildings.Clear();
		}
	}
}
