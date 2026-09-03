#region Copyright & License Information
/*
 * WW3MOD — forward Logistics Center siting for the @experimental bot.
 *
 * Closes the TODO that stood at rules/ai/ai.yaml ("LCCV deployment needs custom strategic logic
 * (evaluate need, find safe location, escort)"). LOGISTICSCENTER is `Prerequisites: ~disabled`, so
 * transforming an LCCV is the ONLY way any player obtains one — nothing else in the mod produces the
 * building. Until now no bot module named `lccv` at all, so the @experimental bot fielded supply
 * trucks (750 supply) and never the Centre (2250, 3x on both axes) that its infantry runs dry without.
 *
 * TWO HALVES, BOTH HERE, because neither works alone:
 *   BUY  — a demand-gated request through IBotRequestPriorityUnitProduction.
 *   SITE — descend the frontier-distance gradient from the Supply Route to a standoff BEHIND the
 *          believed line, land on a cell that is both standable for the LCCV and legal for the 3x3
 *          building, then deploy. Cutting the resupply round-trip is the entire point, so a Centre
 *          built next to the SR it was meant to relieve is a 3000-credit no-op.
 *
 * WHY THE BUY IS NOT A UnitFloors ENTRY — THE ARITHMETIC, NOT A PREFERENCE. Both the floor lane
 * (UnitBuilderBotModule.ChooseBelowFloor) and UnitLimits count OwnedOrPending(name) for ONE actor
 * name. The LCCV is CONSUMED by its own transform: Transform.DoTransform calls self.Dispose(), so a
 * successful deploy returns the `lccv` census to zero while the thing it bought stands on the map as
 * a `logisticscenter`. `UnitFloors: lccv: 1` therefore reads "below floor" forever and re-buys at
 * 3000 credits without bound, and `UnitLimits: lccv: 1` never binds because it counts the same
 * emptied census. A standing-population floor is the wrong instrument for a unit that is spent rather
 * than kept. The demand gate below counts the DEPLOYED form as satisfying the need, which is the same
 * shape as CaptureCoordinatorBotModule.MaintainTecnFloor ("we never spend budget on a TECN with
 * nothing to capture") and is the mechanism rules/ai/ai-america.yaml already blesses for a consumable.
 *
 * WHY McvManagerBotModule WAS NOT REUSED. It is compiled but referenced nowhere in mods/, and it is
 * RA-shaped in three ways that are not tuning: (1) ShouldBuildMCV requires McvFactoryTypes on the map
 * and WW3MOD has no factories; (2) ChooseMcvDeployLocation centres its annulus on a construction yard
 * — GetRandomBaseCenter falls back to initialBaseCenter, i.e. the REAR, which is the opposite of the
 * requirement here; (3) DeployMcv notifies every IBotPositionsUpdated with UpdatedBaseCenter/
 * UpdatedDefenseCenter at the MCV's cell, which would drag the whole bot's notion of its base onto a
 * forward truck. Only its order pair is worth keeping and it is two lines.
 *
 * DETERMINISM (influence-stack invariant): zero random draws. Actors are walked in ActorID order, the
 * descent is ForwardStagingMath's fixed-neighbour integer walk, and the site search is
 * FiresStandoffMath's Chebyshev-ring scan. Gate enable-ai-experimental; @stable never instantiates it.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: buys an LCCV and transforms it into a forward Logistics Center.",
		"Sites it at a standoff behind the believed frontline — near enough to cut the resupply",
		"round-trip, inside believed-own ground rather than on the line. Claims the LCCV through the",
		"shared PoiGoalGuard ledger (objective logistics:<id>) so no combat module recruits a 3000-cost",
		"unarmed truck mid-transit. Gate enable-ai-experimental.")]
	public class LogisticsCenterBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between evaluations. A deploy is a move plus a transform, both slow, so a fast",
			"cadence buys nothing and only risks re-ordering a truck that is already driving.")]
		public readonly int ScanInterval = 100;

		[Desc("Actor types treated as deployable logistics MCVs. Named explicitly rather than detected",
			"from Transforms so this module can never adopt some future transforming unit by accident.")]
		public readonly HashSet<string> McvActorTypes = new() { "lccv" };

		[Desc("Actor types that COUNT AS the deployed result. This is what makes the demand gate correct",
			"for a consumable: the LCCV disappears into one of these, so the need must be measured",
			"against the building, never against the truck that is gone.")]
		public readonly HashSet<string> CenterActorTypes = new() { "logisticscenter" };

		[Desc("How many Logistics Centers to maintain. Counted as deployed centres + LCCVs in transit +",
			"pending production requests, so exactly one is bought and no second is ordered while the",
			"first is still driving.")]
		public readonly int DesiredCenters = 1;

		[Desc("Cash+resources below which no request is issued. A 3000-credit item queued while broke",
			"occupies the Vehicle queue slot and stalls the combat buys behind it, so the module waits",
			"until it can actually pay rather than parking a stalled order in front of the army.")]
		public readonly int MinCashToRequest = 3000;

		[Desc("USER RULING 2026-09-03: require DEMONSTRATED need before spending 3000 on a Centre, instead",
			"of buying one the moment the quota is unfilled and the money is there. See",
			"LogisticsCenterDemandMath for the model and the ruling it implements. False reproduces the",
			"pre-2026-09-03 answer exactly (quota + MinCashToRequest, no need model, no capture veto) so",
			"the whole thing can be A/B'd from YAML. Defaulted TRUE, not false, because a default-off fix",
			"would be a gate whose only purpose is withholding it (CLAUDE.md: @stable inherits improvements).",
			"CORRECTION 2026-09-03: this Desc previously said the trait 'is declared only under",
			"enable-ai-experimental — it is not shared with @stable, so there is no benchmark baseline for a",
			"false default to protect'. That is wrong. LogisticsCenterBotModule@stable exists (ai.yaml:3095)",
			"and omits this key, so @stable INHERITS the true default and its behaviour did move. Deliberate",
			"and allowed, but it means the next @stable benchmark baseline must be re-taken knowingly rather",
			"than assumed unchanged.")]
		public readonly bool RequireDemand = true;

		[Desc("Actor types priced as the ALTERNATIVE purchase — the thing given up by buying a Centre.",
			"The main battle tank: abrams is 2500 and t90 2400 against the Centre's 3000, which is the",
			"user's 'it costs more than a tank'. Cost is read from the RULESET at decision time, never",
			"hard-coded, so a balance pass that reprices the tank moves this decision with it. The most",
			"expensive resolvable type wins, so a faction fielding several still compares against its",
			"real main line.")]
		public readonly HashSet<string> TankActorTypes = new() { "abrams", "t90" };

		[Desc("How far from the Supply Route, in map cells, a rearmable unit must be to count as a FORWARD",
			"customer — one whose rearm round-trip a forward Centre would actually shorten. Units nearer",
			"than this are inside the beachhead's own catchment and a Centre saves them nothing, which is",
			"precisely the opening the ruling forbids buying in.")]
		public readonly int ForwardCustomerCells = 12;

		[Desc("The walk, in map cells, that SURVIVES the Centre: from the fighting line back to a Centre",
			"sited a standoff behind it. Subtracted from the customers' distance to the Supply Route to",
			"give the trip actually saved. An estimate rather than a derivation — StandoffCells is in",
			"COARSE control-grid cells and converting it here would import the grid/map-cell confusion",
			"this module's own siting comments warn about.")]
		public readonly int ResidualTripCells = 6;

		[Desc("How far from the Supply Route, in map cells, a capturable Centre may be and still VETO a",
			"purchase. Bounded on purpose: three shipped maps place neutral Centres in pairs, one per",
			"side, so an unbounded veto lets the ENEMY's Centre — which this bot will never reach — block",
			"the buy for the whole match, turning 'prefer the free one' into 'never own one'.")]
		public readonly int CaptureConsiderCells = 40;

		[Desc("Ticks after a production request before another may be issued for the same need. The",
			"priority lane retries its own head until built, so re-requesting faster only inflates the",
			"pending count this module's own gate reads.")]
		public readonly int RequestStaleTicks = 600;

		[Desc("Ticks a ledger commitment is held. Must exceed ScanInterval by enough that a commitment",
			"cannot lapse between two evaluations — a lapsed claim is an unarmed truck the offence FSM",
			"is free to recruit mid-transit.")]
		public readonly int CommitmentTicks = 400;

		[Desc("Frontier distance, in COARSE control-grid cells, at which the forward descent stops. This",
			"is the standoff BEHIND the believed line: larger is further back and safer. Shares its",
			"meaning with PoiOffensiveBotModule.StagingStandoffCells.")]
		public readonly int StandoffCells = 4;

		[Desc("Maximum descent steps toward the front. Bounds the walk on a large map; the walk also",
			"terminates on its own because frontier distance strictly decreases each accepted step.")]
		public readonly int MaxDescentSteps = 24;

		[Desc("Believed anti-ground danger, in DANGER UNITS, above which a grid cell is refused as a",
			"step. Converted to raw field units at the call site. 0 or less disables the guard.")]
		public readonly int DangerSafeUnits = 30;

		[Desc("How far from the ideal cell to search for one that is both standable for the LCCV and",
			"legal for the 3x3 building. Matches BotTerrain.EngineRelocationCells so this module's cell",
			"and the engine's relocated cell are the same cell.")]
		public readonly int SiteSearchCells = BotTerrain.EngineRelocationCells;

		[Desc("Ticks a cell that REFUSED a deploy is not retried at. A refusal is usually something",
			"parked in the footprint, which clears; blacklisting forever would burn the map down to no",
			"legal site on a busy front.")]
		public readonly int RefusedCellCooldownTicks = 500;

		[Desc("USER RULING 2026-09-03: 'Bots needs to learn how to resupply the LC.' Send a loaded supply",
			"truck to a Centre that has run down, using the SAME DeliverSupply order a human issues by",
			"left-clicking a loaded truck on one — see LogisticsCenterRestockMath for the trace showing the",
			"human path already works and that no bot module ever issued the order.")]
		public readonly bool RestockCenters = true;

		[Desc("Actor types treated as supply trucks for the restock errand. Matches",
			"SupplyFollowerBotModule.SupplyTruckTypes; a type not on BOTH lists is either never dispatched",
			"or never handed back.")]
		public readonly HashSet<string> SupplyTruckActorTypes = new() { "truk" };

		[Desc("Stock level, in PER MILLE of the Centre's capacity, below which a delivery is dispatched.",
			"Not zero, deliberately: waiting for empty means waiting until the Centre has already failed",
			"the units standing at it, which is the reported symptom. 500 sends a truck at half empty.")]
		public readonly int CenterRestockThresholdPerMille = 500;

		[Desc("Smallest transfer worth a truck's whole errand. Also the anti-oscillation term: a truck that",
			"delivers a trickle drops below the follower's RestockThreshold, is released as spent, and is",
			"immediately re-dispatched.")]
		public readonly int MinDeliverySupply = 250;

		[Desc("Furthest a truck will be pulled off its follow duty to make a delivery. A truck hauled",
			"across the map is one not serving the army it was following, and it arrives after the need.")]
		public readonly int MaxDeliveryDistanceCells = 40;

		[Desc("Cells from the chosen site at which the truck counts as ARRIVED and deploys where it",
			"stands. Not leniency: Mobile.NearestMoveableCell can park a bot's destination up to",
			"BotTerrain.EngineRelocationCells away, and a module that insists on its exact cell re-issues",
			"the same Move forever. Kept far below that reach so 'arrived' still means the site.")]
		public readonly int ArrivalRadiusCells = 2;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase): the ruleset lowercases actor names but
			// YAML keeps its authored case, so `LCCV` would otherwise match nothing.
			ActorNameCase.NormalizeInPlace(McvActorTypes);
			ActorNameCase.NormalizeInPlace(CenterActorTypes);
			ActorNameCase.NormalizeInPlace(TankActorTypes);
			ActorNameCase.NormalizeInPlace(SupplyTruckActorTypes);
		}

		public override object Create(ActorInitializer init) { return new LogisticsCenterBotModule(init.Self, this); }
	}

	public class LogisticsCenterBotModule : ConditionalTrait<LogisticsCenterBotModuleInfo>, IBotTick
	{
		readonly World world;
		readonly Player player;

		PoiGoalGuard goalGuard;
		bool goalGuardResolved;
		ControlField controlField;
		bool controlFieldResolved;
		DangerFieldLayer dangerField;
		bool dangerFieldResolved;
		PoiMap poiMap;
		bool poiMapResolved;

		IBotRequestUnitProduction[] unitProducers;
		IBotRequestPriorityUnitProduction[] priorityProducers;
		bool priorityProducersResolved;

		// LCCVs this module holds a ledger claim on, so the claim is dropped precisely when the truck
		// transforms, dies, or the module shuts down.
		readonly HashSet<Actor> claimed = new();

		// Cells that refused a deploy, with the tick the refusal expires. Keyed by cell rather than by
		// actor: the obstruction belongs to the ground, and a replacement LCCV sent to the same spot
		// would otherwise repeat the refusal.
		readonly Dictionary<CPos, int> refusedCells = new();
		readonly List<CPos> refusedExpired = new();

		BotBlackboard blackboard;
		bool blackboardResolved;

		// Trucks dispatched to refill a Centre: truck -> the Centre it was sent to. Keyed by truck because
		// the claim is per-truck and the release must be too; the Centre is held so the errand can be ended
		// when THAT building dies or is captured away, not merely when the truck stops.
		readonly Dictionary<Actor, Actor> deliveryErrands = new();
		readonly List<Actor> endedErrands = new();

		const string DeliveryClaim = "logistics-delivery";

		int scanCountdown;
		int lastRequestTick = -1;

		public LogisticsCenterBotModule(Actor self, LogisticsCenterBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			// Deterministic initial offset, NOT a LocalRandom draw — a control game that never
			// instantiates this module must keep its random stream untouched.
			scanCountdown = Info.ScanInterval;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			ResolveTraits();

			// REFRESH THE CLAIM EVERY TICK, EVALUATE ONLY ON THE CADENCE. A third party can delete this
			// module's claim between two evaluations: GoalGuardLedger.Release is keyed on the ACTOR and
			// not on the objective, so StancePositioningExecutor.ReleaseManagement drops whatever claim
			// the actor holds regardless of who wrote it. On a 100-tick cadence that would leave the
			// truck unclaimed — and so recruitable — for up to 100 ticks. A dictionary write per tick is
			// far cheaper than that exposure.
			RefreshClaims();

			if (--scanCountdown > 0)
				return;

			scanCountdown = Info.ScanInterval;
			Evaluate(bot);
		}

		void ResolveTraits()
		{
			if (!goalGuardResolved)
			{
				goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
				goalGuardResolved = true;
			}

			if (!controlFieldResolved)
			{
				controlField = world.WorldActor.TraitOrDefault<ControlField>();
				controlFieldResolved = true;
			}

			if (!dangerFieldResolved)
			{
				dangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();
				dangerFieldResolved = true;
			}

			if (!poiMapResolved)
			{
				poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
				poiMapResolved = true;
			}

			if (!blackboardResolved)
			{
				// Same resolution as SupplyFollowerBotModule's, including the !IsTraitDisabled filter: a
				// disabled blackboard answers the trait query but arbitrates nothing, and claiming against
				// it would mean believing a truck was reserved when the follower could still see it.
				blackboard = player.PlayerActor.TraitsImplementing<BotBlackboard>().FirstOrDefault(b => !b.IsTraitDisabled);
				blackboardResolved = true;
			}
		}

		void RefreshClaims()
		{
			if (goalGuard == null || claimed.Count == 0)
				return;

			var tick = world.WorldTick;
			foreach (var mcv in claimed)
				if (!mcv.IsDead && mcv.IsInWorld)
					goalGuard.Ledger.Commit(mcv, "logistics:" + mcv.ActorID.ToString(), tick, Info.CommitmentTicks);
		}

		// A disabled module must not leave units committed behind it, or the offence FSM sees a truck
		// that is permanently spoken for by nobody.
		protected override void TraitDisabled(Actor self)
		{
			if (goalGuard != null)
				foreach (var a in claimed)
					goalGuard.Ledger.Release(a);

			// The blackboard claims too, and for the same reason: a disabled module that keeps them leaves
			// every dispatched truck reserved to a module that will never tick again, so SupplyFollower
			// never sees them and the bot silently loses its supply fleet.
			if (blackboard != null)
				foreach (var truck in deliveryErrands.Keys)
					if (truck != null && blackboard.IsUnitClaimedBy(truck, DeliveryClaim))
						blackboard.ReleaseUnit(truck);

			deliveryErrands.Clear();
			claimed.Clear();
			refusedCells.Clear();
		}

		void Evaluate(IBot bot)
		{
			var tick = world.WorldTick;

			ExpireRefusals(tick);

			// Drop claims on trucks that died or TRANSFORMED. A successful deploy disposes the LCCV, so
			// this is the ordinary success path and not only the failure one.
			claimed.RemoveWhere(a =>
			{
				if (!a.IsDead && a.IsInWorld)
					return false;

				goalGuard?.Ledger.Release(a);
				return true;
			});

			var mcvs = world.Actors
				.Where(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.McvActorTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.ToList();

			var centers = world.Actors
				.Count(a => a.Owner == player
					&& !a.IsDead
					&& a.IsInWorld
					&& Info.CenterActorTypes.Contains(a.Info.Name));

			MaintainCenterDemand(bot, mcvs.Count, centers, tick);

			foreach (var mcv in mcvs)
				TaskMcv(bot, mcv, tick);

			MaintainCenterStock(bot, tick);
		}

		/// <summary><para>Keep a deployed Centre STOCKED, by sending it a loaded supply truck.</para>
		///
		/// <para>An unstocked Centre is worth nothing — the reported symptom is units driving to one and
		/// then sitting at it — so buying one and siting it correctly is only two thirds of the job. This
		/// issues the SAME "DeliverSupply" order a human issues by left-clicking a loaded truck on a Centre
		/// (DropsSupplyCache.ResolveOrder:312), so bot and human go down one code path and the bot cannot
		/// acquire a private transfer rule that drifts from what the player sees.</para></summary>
		void MaintainCenterStock(IBot bot, int tick)
		{
			if (!Info.RestockCenters)
				return;

			ReleaseFinishedErrands();

			var centers = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& Info.CenterActorTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.ToList();

			if (centers.Count == 0)
				return;

			foreach (var center in centers)
			{
				var provider = center.TraitOrDefault<SupplyProvider>();
				if (provider == null)
					continue;

				if (!LogisticsCenterRestockMath.CentreNeedsRestock(
						provider.CurrentSupply, provider.Info.TotalSupply, Info.CenterRestockThresholdPerMille))
					continue;

				// Already have a truck on the way to THIS Centre. Re-ordering here is the order-spam that
				// resets a drive every scan and never arrives.
				if (deliveryErrands.ContainsValue(center))
					continue;

				var headroom = provider.Info.TotalSupply - provider.CurrentSupply;
				var truck = ChooseDeliveryTruck(center, headroom);
				if (truck == null)
					continue;

				// CLAIM BEFORE ORDERING. SupplyFollowerBotModule drops any truck claimed by another module
				// from its roster entirely (IsClaimedByOtherModule), so this is what stops the two of us
				// issuing competing Move orders to one truck every scan.
				if (blackboard != null && !blackboard.ClaimUnit(truck, DeliveryClaim))
					continue;

				deliveryErrands[truck] = center;

				// UNQUEUED: this supersedes whatever follow order the truck was running, which is the point
				// — the delivery is the more urgent errand and a queued one would run after a follow that
				// may never end.
				bot.QueueOrder(new Order("DeliverSupply", truck, Target.FromActor(center), false));

				var truckSupply = truck.TraitOrDefault<SupplyProvider>();
				Log.Write("debug",
					$"[logistics] player={player.PlayerName} deliver truck={truck.ActorID} "
					+ $"center={center.ActorID}@{center.Location.X},{center.Location.Y} "
					+ $"center-supply={provider.CurrentSupply}/{provider.Info.TotalSupply} "
					+ $"truck-supply={(truckSupply != null ? truckSupply.CurrentSupply : -1)} tick={tick}");
			}
		}

		/// <summary>The nearest loaded truck worth pulling off follow duty for this Centre, or null. Ranked
		/// by LogisticsCenterRestockMath.DispatchRank (distance dominant, load as tie-break) with ActorID as
		/// the final tie-break, so world iteration order cannot decide — the determinism invariant.</summary>
		Actor ChooseDeliveryTruck(Actor center, int headroom)
		{
			Actor best = null;
			var bestRank = long.MaxValue;
			var bestId = uint.MaxValue;

			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				if (!Info.SupplyTruckActorTypes.Contains(a.Info.Name))
					continue;

				// Someone else's truck this scan. Note the asymmetry with our own claim: a truck we already
				// hold is excluded above by deliveryErrands, so this only skips OTHER modules' work.
				if (blackboard != null)
				{
					var claimant = blackboard.GetUnitClaimant(a);
					if (claimant != null && claimant != DeliveryClaim)
						continue;
				}

				if (deliveryErrands.ContainsKey(a))
					continue;

				var sp = a.TraitOrDefault<SupplyProvider>();
				if (sp == null || sp.CountsAsEmpty)
					continue;

				var distance = (a.Location - center.Location).Length;
				if (!LogisticsCenterRestockMath.WorthDispatching(
						sp.CurrentSupply, headroom, distance, Info.MinDeliverySupply, Info.MaxDeliveryDistanceCells))
					continue;

				var rank = LogisticsCenterRestockMath.DispatchRank(
					distance, LogisticsCenterRestockMath.TransferableAmount(sp.CurrentSupply, headroom));

				if (rank < bestRank || (rank == bestRank && a.ActorID < bestId))
				{
					best = a;
					bestRank = rank;
					bestId = a.ActorID;
				}
			}

			return best;
		}

		/// <summary>Hand back every truck whose errand has ended. Releasing is the half that goes wrong: a
		/// module that claims and forgets leaves the unit alive-and-claimed forever, invisible to every
		/// other claim-respecting module. See LogisticsCenterRestockMath.ErrandEnded for the conditions and
		/// why truck-idle releases rather than retries.</summary>
		void ReleaseFinishedErrands()
		{
			if (deliveryErrands.Count == 0)
				return;

			endedErrands.Clear();

			foreach (var kv in deliveryErrands)
			{
				var truck = kv.Key;
				var center = kv.Value;

				var truckGone = truck == null || truck.IsDead || !truck.IsInWorld;
				var centerGone = center == null || center.IsDead || !center.IsInWorld || center.Owner != player;

				var truckSupply = truckGone ? null : truck.TraitOrDefault<SupplyProvider>();
				var centerSupply = centerGone ? null : center.TraitOrDefault<SupplyProvider>();

				var truckEmpty = truckSupply == null || truckSupply.CountsAsEmpty;
				var centerFull = centerSupply != null && centerSupply.CurrentSupply >= centerSupply.Info.TotalSupply;

				if (LogisticsCenterRestockMath.ErrandEnded(
						truckGone, centerGone, truckEmpty, centerFull, !truckGone && truck.IsIdle))
					endedErrands.Add(truck);
			}

			foreach (var truck in endedErrands)
			{
				deliveryErrands.Remove(truck);
				if (truck != null && blackboard != null && blackboard.IsUnitClaimedBy(truck, DeliveryClaim))
					blackboard.ReleaseUnit(truck);
			}
		}

		void ExpireRefusals(int tick)
		{
			if (refusedCells.Count == 0)
				return;

			refusedExpired.Clear();
			foreach (var kv in refusedCells)
				if (tick >= kv.Value)
					refusedExpired.Add(kv.Key);

			foreach (var cell in refusedExpired)
				refusedCells.Remove(cell);
		}

		// THE DEMAND GATE. Counts the DEPLOYED form, the truck still driving, and anything already
		// requested — see the header for why a UnitFloors entry cannot express this.
		void MaintainCenterDemand(IBot bot, int mcvsAlive, int centers, int tick)
		{
			if (Info.DesiredCenters <= 0)
				return;

			unitProducers ??= player.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>().ToArray();
			if (unitProducers.Length == 0)
				return;

			var buildType = ResolveBuildType();
			if (buildType == null)
				return;

			var pending = unitProducers.Sum(u => u.RequestedProductionCount(bot, buildType));
			var held = centers + mcvsAlive + pending;
			if (held >= Info.DesiredCenters)
				return;

			// Don't park an unaffordable 3000-credit order in front of the combat buys.
			var res = player.PlayerActor.TraitOrDefault<PlayerResources>();
			var funds = res != null ? (long)res.Cash + res.Resources : 0;
			if (funds < Info.MinCashToRequest)
				return;

			if (lastRequestTick >= 0 && tick - lastRequestTick < Info.RequestStaleTicks)
				return;

			// THE DEMAND TERMS (user ruling 2026-09-03). Everything above is the pre-existing quota and
			// affordability floor; everything below is the need model that was missing entirely, which is
			// why the bot bought a Centre ~6 s in with full-ammo infantry standing on the beachhead.
			var capturable = CapturableCentersWithinReach();
			var (forwardValue, needPerMille, forwardCells) = MeasureForwardCustomers();
			var tankCost = ResolveTankCost();

			if (!LogisticsCenterDemandMath.ShouldRequestCenter(
					held, Info.DesiredCenters, capturable,
					funds, UnitCostOf(buildType), tankCost,
					forwardValue, needPerMille, forwardCells, Info.ResidualTripCells,
					Info.RequireDemand))
			{
				// Logged at the refusal, with every term the decision read. The diagnosis this needed was
				// "which number said no", and a line that recomputes later can disagree with the decision
				// it claims to explain.
				Log.Write("debug",
					$"[logistics] player={player.PlayerName} refuse-buy held={held} capturable={capturable} "
					+ $"fwd-value={forwardValue} need-permille={needPerMille} fwd-cells={forwardCells} "
					+ $"tank={tankCost} funds={funds} tick={tick}");
				return;
			}

			// ROUTE TO THE FIRST PRODUCER THAT ACCEPTS. A player carries several UnitBuilder twins, all
			// but one condition-disabled per game; a disabled twin answers the interface but never ticks,
			// so handing it the request deadlocks the need — its pending count climbs while nothing is
			// ever built. Only the priority lane reports acceptance, and it is also peek-don't-pop, so a
			// busy Vehicle queue retries the request instead of silently discarding it.
			if (!priorityProducersResolved)
			{
				priorityProducers = player.PlayerActor.TraitsImplementing<IBotRequestPriorityUnitProduction>().ToArray();
				priorityProducersResolved = true;
			}

			foreach (var p in priorityProducers)
			{
				if (p.RequestPriorityUnitProduction(bot, buildType))
				{
					lastRequestTick = tick;
					Log.Write("debug",
						$"[logistics] player={player.PlayerName} request={buildType} centers={centers} "
						+ $"mcvs={mcvsAlive} pending={pending} funds={funds} tick={tick}");
					return;
				}
			}
		}

		// Resolve the buildable MCV name against the live Vehicle queue rather than trusting the config
		// list: the queue and its prerequisites may not be up on the first scans, and caching a null
		// would pin the module off for the match.
		string ResolveBuildType()
		{
			var buildable = AIUtils.FindQueuesByCategory(player)["Vehicle"]
				.SelectMany(q => q.BuildableItems())
				.Select(a => a.Name)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			return Info.McvActorTypes.OrderBy(t => t, StringComparer.Ordinal)
				.FirstOrDefault(t => buildable.Contains(t));
		}

		int UnitCostOf(string actorName)
		{
			if (actorName == null || !world.Map.Rules.Actors.TryGetValue(actorName, out var ai))
				return 0;

			return ai.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
		}

		/// <summary>What a tank costs right now, read from the ruleset rather than written down here, so a
		/// balance pass that reprices the main battle tank moves the opportunity-cost comparison with it.
		/// The dearest resolvable type wins: a faction fielding several should compare the Centre against
		/// its real main line, not against the cheapest thing in the list.</summary>
		int ResolveTankCost()
		{
			var cost = 0;
			foreach (var name in Info.TankActorTypes)
			{
				var c = UnitCostOf(name);
				if (c > cost)
					cost = c;
			}

			return cost;
		}

		/// <summary><para>Capturable Centres near enough to be worth waiting for instead of buying.</para>
		///
		/// <para>Read from the SAME list CaptureCoordinatorBotModule dispatches from — PoiMap.GetCaptureTargets
		/// — and that agreement is the point rather than convenience. A stricter source here would let this
		/// module buy a 3000-credit Centre while the capture module is already driving a technician at a
		/// free one, which is the exact waste being fixed. Consequence stated plainly: GetCaptureTargets is
		/// map-wide and not fog-filtered, so this inherits that module's omniscience about POI existence.
		/// No new omniscience is introduced, and DistanceCells is measured from this player's own Supply
		/// Route by PoiMap itself.</para></summary>
		int CapturableCentersWithinReach()
		{
			if (poiMap == null)
				return 0;

			var n = 0;
			foreach (var poi in poiMap.GetCaptureTargets(player))
				if (Info.CenterActorTypes.Contains(poi.Actor.Info.Name)
					&& poi.DistanceCells <= Info.CaptureConsiderCells)
					n++;

			return n;
		}

		/// <summary><para>The forward customers a Centre would actually serve: total credit value, their mean
		/// ammo need in per mille, and their mean distance from the Supply Route in cells.</para>
		///
		/// <para>A customer is a unit whose Rearmable lists a CenterActorType — infantry carry
		/// `RearmActors: truk, supplycache, logisticscenter` (infantry.yaml), so the set is defined by the
		/// ruleset and not by a second list here that could drift from it. FORWARD is ForwardCustomerCells
		/// from the SR: nearer units are inside the beachhead's own catchment and a forward Centre saves
		/// them nothing.</para>
		///
		/// <para>Need uses ResupplyDemand.UnitNeed, the same missing/capacity metric SupplyProvider itself
		/// uses and the same one the supply-truck gate reads, so the two economy decisions cannot disagree
		/// about how empty a soldier is. MEAN rather than max, deliberately: one dry scout must not price a
		/// 3000-credit depot for an otherwise full army, and the value model is about how much of the army's
		/// time is going into round-trips, which is an average.</para>
		///
		/// <para>Returns (0, 0, 0) when there are no forward customers — no army forward, nothing to save,
		/// and ForwardResupplyValue is zero for that reason rather than by a special case.</para></summary>
		(int Value, int NeedPerMille, int DistanceCells) MeasureForwardCustomers()
		{
			var srCell = poiMap?.OwnSupplyRoute(player)?.Location;
			if (srCell == null)
				return (0, 0, 0);

			var value = 0;
			var needSum = 0L;
			var distSum = 0L;
			var count = 0;

			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				var rearmable = a.TraitOrDefault<Rearmable>();
				if (rearmable == null || !rearmable.Info.RearmActors.Overlaps(Info.CenterActorTypes))
					continue;

				var pools = rearmable.RearmableAmmoPools;
				if (pools == null || pools.Length == 0)
					continue;

				var cells = (a.Location - srCell.Value).Length;
				if (cells < Info.ForwardCustomerCells)
					continue;

				var need = ResupplyDemand.UnitNeed(pools.Select(p => (p.Info.Ammo, p.CurrentAmmoCount, p.Info.SupplyValue)));

				value += a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
				needSum += (int)(need * 1000);
				distSum += cells;
				count++;
			}

			if (count == 0)
				return (0, 0, 0);

			return (value, (int)(needSum / count), (int)(distSum / count));
		}

		void TaskMcv(IBot bot, Actor mcv, int tick)
		{
			// Claim it whatever we decide below: an unarmed 3000-cost truck must never be recruited as
			// combat mass, and that is true while it drives as much as while it waits.
			if (goalGuard != null && claimed.Add(mcv))
				goalGuard.Ledger.Commit(mcv, "logistics:" + mcv.ActorID.ToString(), tick, Info.CommitmentTicks);

			// Busy: it is driving to a site we already chose. Re-ordering here is the order-spam that
			// resets a move every scan and never arrives.
			if (!mcv.IsIdle)
				return;

			var transforms = mcv.TraitOrDefault<Transforms>();
			if (transforms == null)
				return;

			// EMP (Transforms.PauseOnCondition: empdisable on LCCV) or a disabled trait. WAIT — do not
			// blacklist below and do not re-site: the ground is fine, the truck is stunned, and parking
			// a perfectly good cell because of a temporary condition on the unit would walk the site
			// away from the front for no reason.
			if (transforms.IsTraitPaused || transforms.IsTraitDisabled)
				return;

			var placeable = PlacementTestFor(mcv);
			if (placeable == null)
				return;

			var site = ChooseSite(mcv);
			if (site == null)
				return;

			// ARRIVAL IS A RADIUS, NOT AN EQUALITY, and that is load-bearing rather than lenient. Both
			// order paths run the destination through Mobile.NearestMoveableCell, which can silently park
			// the truck up to EngineRelocationCells away; a module that insists on its own exact cell then
			// re-issues the same Move every scan forever and never deploys — the measured trap in
			// BotTerrain's notes. So judge the cell the truck ACTUALLY reached.
			if (ArrivedAt(mcv.Location, site.Value))
			{
				// Placement is re-tested HERE because the truck's cell is not necessarily the one the site
				// search blessed, and because the world moved while it drove.
				if (!placeable(mcv.Location))
				{
					// Arrived and the 3x3 will not fit — usually something parked in the footprint. Park
					// this cell so the next scan sites elsewhere instead of re-testing the same
					// obstruction, and order the deploy anyway for its one useful side effect: the
					// unqueued path issues ClearBlockersOrders to move whatever is standing there, so a
					// self-inflicted block clears itself for the retry.
					refusedCells[mcv.Location] = tick + Info.RefusedCellCooldownTicks;
					Log.Write("debug",
						$"[logistics] player={player.PlayerName} mcv={mcv.ActorID} deploy-refused "
						+ $"cell={mcv.Location.X},{mcv.Location.Y} tick={tick}");
					bot.QueueOrder(new Order("DeployTransform", mcv, false));
					return;
				}

				// UNQUEUED, deliberately. Transforms.DeployTransform only consults CanDeploy on the
				// unqueued path; a queued deploy defers the whole judgement to execution time inside the
				// Transform activity, where the refusal is silent to this module. Ordering it at the cell
				// we already occupy is the one place the outcome is observable.
				bot.QueueOrder(new Order("DeployTransform", mcv, false));
				Log.Write("debug",
					$"[logistics] player={player.PlayerName} mcv={mcv.ActorID} deploy "
					+ $"cell={mcv.Location.X},{mcv.Location.Y} tick={tick}");
				return;
			}

			bot.QueueOrder(new Order("Move", mcv, Target.FromCell(world, site.Value), false));
			Log.Write("debug",
				$"[logistics] player={player.PlayerName} mcv={mcv.ActorID} move-to-site "
				+ $"cell={site.Value.X},{site.Value.Y} from={mcv.Location.X},{mcv.Location.Y} tick={tick}");
		}

		// Chebyshev, matching how the engine's own relocation annulus is shaped.
		bool ArrivedAt(CPos at, CPos site)
		{
			var dx = Math.Abs(at.X - site.X);
			var dy = Math.Abs(at.Y - site.Y);
			return Math.Max(dx, dy) <= Info.ArrivalRadiusCells;
		}

		/// <summary>The cell this LCCV should deploy on, or null for "no forward site this scan — hold".
		/// Descends the frontier-distance gradient from the Supply Route toward the nearest believed
		/// front, stopping a standoff short of it, then lands on a cell that satisfies BOTH halves of
		/// what a deploy needs: ground the truck can stand on, and a 3x3 the building may legally
		/// occupy.</summary>
		CPos? ChooseSite(Actor mcv)
		{
			// The gradient IS the "behind our own frontier" read — the SR sits deep in the rear at a
			// large frontier distance and each accepted step reduces it — so with no control field there
			// is nothing fog-legal to steer by and the module must hold rather than fall back on an
			// omniscient source or on a blind guess near the SR.
			if (controlField == null || !controlField.HasField(player) || poiMap == null)
				return null;

			var srCell = poiMap.OwnSupplyRoute(player)?.Location;
			if (srCell == null)
				return null;

			var passable = BotTerrain.PassableFor(mcv);
			var placeable = PlacementTestFor(mcv);
			if (placeable == null)
				return null;

			// A cell is a SITE only if the truck can stand there AND the building fits there. Composing
			// both into the one predicate the ring search uses is what stops the search returning a cell
			// the caller is then obliged to reject — the failure mode ForwardStagingMath's `passable`
			// argument exists for, where a deterministic search re-derives the same rejected cell every
			// scan and the module is dark for as long as the field holds still.
			bool CanSite(CPos c) => passable(c) && !refusedCells.ContainsKey(c) && placeable(c);

			var cellSize = controlField.Info.CellSize;
			var sgx = InfluenceGridMath.MapToGrid(cellSize, srCell.Value.X);
			var sgy = InfluenceGridMath.MapToGrid(cellSize, srCell.Value.Y);

			var dangerSafe = Info.DangerSafeUnits > 0 && dangerField != null
				? dangerField.GroundDangerUnitsToField(Info.DangerSafeUnits)
				: -1;

			var (agx, agy) = ForwardStagingMath.StagingCell(sgx, sgy,
				Info.StandoffCells, dangerSafe, Info.MaxDescentSteps,
				(gx, gy) => controlField.FrontierDistanceAt(player, gx, gy),
				(gx, gy) => dangerField != null ? dangerField.GroundDanger(player, controlField.GridCellToMapCell(gx, gy)) : 0,
				(gx, gy) => gx >= 0 && gx < controlField.GridWidth && gy >= 0 && gy < controlField.GridHeight,
				(gx, gy) => passable(controlField.GridCellToMapCell(gx, gy)));

			// COMPARED IN GRID SPACE — the space the descent actually ran in. Converting to map cells
			// first and comparing against the SR silently inverts this test, because GridCellToMapCell
			// returns the grid cell CENTRE and the round trip only reproduces the input on some
			// coordinate parities (see ForwardStagingMath.TryResolveAnchorCell).
			//
			// A stalled descent means a flat/unpopulated field — no believed enemy anywhere yet — or a
			// front already on top of the SR. HOLD, do not deploy: a Centre placed at the Supply Route
			// is 3000 credits spent to shorten a round-trip that was already zero, and it is the one
			// outcome this module exists to avoid. The truck simply waits for the field to populate.
			if (agx == sgx && agy == sgy)
				return null;

			var ideal = controlField.GridCellToMapCell(agx, agy);

			if (!BotTerrain.TryNearestStandable(ideal, Info.SiteSearchCells,
					world.Map.Contains, CanSite, out var cell))
				return null;

			return cell;
		}

		/// <summary>The deploy test the engine will actually apply, asked at an ARBITRARY cell rather
		/// than at the truck's current one. Transforms.CanDeploy hard-codes self.Location, so it can
		/// only answer "here"; siting has to ask "there" before spending a drive on it. The predicate
		/// underneath is the same call CanDeploy makes — World.CanPlaceBuilding at cell +
		/// Transforms.Offset, ignoring the LCCV's own footprint — so the site search and the engine's
		/// eventual refusal cannot disagree on anything but the cell. Null when this actor has no
		/// resolvable transform target. The trait and rules lookups are hoisted out of the returned
		/// closure because the ring search calls it once per candidate cell.</summary>
		Func<CPos, bool> PlacementTestFor(Actor mcv)
		{
			var transforms = mcv.TraitOrDefault<Transforms>();
			if (transforms == null)
				return null;

			if (!world.Map.Rules.Actors.TryGetValue(transforms.Info.IntoActor, out var actorInfo))
				return null;

			var bi = actorInfo.TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return _ => true;

			var offset = transforms.Info.Offset;
			return c => world.CanPlaceBuilding(c + offset, actorInfo, bi, mcv);
		}
	}
}
