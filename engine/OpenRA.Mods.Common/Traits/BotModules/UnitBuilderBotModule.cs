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

		[Desc("EXPERIMENTAL (early-econ behaviour 3): don't call in a TRANSPORT (a carrier with no weapon,",
			"e.g. the tran/halo transport helicopter) unless there is real LIFT DEMAND — infantry actually",
			"available and waiting for a ride — and no idle transport we already own can serve it. The frozen",
			"path buys transports on a flat lottery weight with no demand test at all, so they are called in",
			"during the opening and then park at the Supply Route for the whole match (River Zeta issue 4).",
			"NOTE — 'idle transport we already own' is the raw IsIdle test, NOT a launchability test: a",
			"chip-damaged transport the squad launcher can never pick still counts as spare capacity here and",
			"so defers a replacement buy. That is bounded, not permanent — the squad module's use-or-evac",
			"(EvacuateIdleTransports) retires the unlaunchable airframe at its idle window, owned drops, and",
			"this gate then authorises the replacement. Without that counterpart flag the deferral IS permanent.",
			"Composes with CompositionDirected rather than fighting it: helicopter pools stay deliberately",
			"absent from UnitTargetShares (deferred to their own builder twins) and this gate is applied at",
			"BOTH post-pick sites, so it behaves identically whether the pick came from the deficit picker or",
			"the legacy lottery. Default false ⇒ frozen production, byte-identical for normal/rush/turtle/stable.")]
		public readonly bool GateTransportOnDemand = false;

		[Desc("Transport actor types gated by GateTransportOnDemand (e.g. tran, halo). Inert unless that flag is set.")]
		public readonly HashSet<string> TransportUnitTypes = new HashSet<string>();

		[Desc("Passengers a transport mission needs before lift demand counts as real. Should match the consuming",
			"squad module's TransportMinInfantry — a stray rifleman is not a reason to call in an airframe.",
			"Only used when GateTransportOnDemand is set.")]
		public readonly int TransportMinPassengers = 4;

		[Desc("Radius (map cells) around the bot's own Supply Route inside which infantry count as liftable",
			"RESERVE for the demand gate. MUST match the consuming squad module's LiftReserveZoneRadiusCells:",
			"this count predicts the load HelicopterSquadBotModule will actually assemble, and a disagreement",
			"either buys an airframe the launcher can never fill or starves it of one it needs. 0 or less = no",
			"spatial restriction. Only used when GateTransportOnDemand is set.")]
		public readonly int LiftReserveZoneRadiusCells = 14;

		[Desc("Actor types of the bot's home Supply Route, used to anchor the lift reserve zone.",
			"Only used when GateTransportOnDemand is set.")]
		public readonly HashSet<string> SupplyRouteTypes = new HashSet<string> { "supplyroute" };

		[Desc("EXPERIMENTAL (composition-directed purchasing): replace the ground-unit LOTTERY with a",
			"census-vs-target deficit pick. The frozen path buys uniformly at random (idleUnitCount stays 0",
			"under IgnoreGroundUnits, so buildRandom is always true), which makes the STANDING composition",
			"proportional to each type's LIFETIME — long-lived rear-line mortars accumulate. With this on, the",
			"module measures what it owns (per-mille of army VALUE), compares against UnitTargetShares, and buys",
			"the type furthest below target. Default false ⇒ the legacy ternary runs unchanged, including the",
			"LocalRandom draw count and order, so normal/rush/turtle/@stable are byte-identical.")]
		public readonly bool CompositionDirected = false;

		[Desc("Target share (per-mille of army VALUE) per actor type. These are TARGETS, not the ceilings",
			"UnitsToBuild weights are. Only types listed here are eligible for the composition-directed pick —",
			"an unlisted buyable type is simply never chosen by it (that is how helicopter pools stay deferred).",
			"Inert unless CompositionDirected is set. Values need not sum to 1000; they are apportioned.")]
		public readonly Dictionary<string, int> UnitTargetShares = null;

		[Desc("Own actor type -> role token (frontline/antitank/antiair/mortar/support/...). Used only as the",
			"row/column key for CounterMatrixPct; a type with no role simply receives no counter bias.")]
		public readonly Dictionary<string, string> UnitRoles = null;

		[Desc("Counter bias, keyed \"enemyclass>ownrole\" (e.g. \"armor>antitank: 40\"). Enemy classes are the",
			"existing 3-way believed classification: air, armor, infantry. The value is a percent contribution",
			"scaled by that class's share of the believed enemy force, summed per own-role and clamped to",
			"+/-CounterBiasMaxPct before being applied to the target shares.")]
		public readonly Dictionary<string, int> CounterMatrixPct = null;

		[Desc("Clamp on the summed counter bias, in percent of the base target share. 0 disables the bias.")]
		public readonly int CounterBiasMaxPct = 200;

		[Desc("Integer EMA alpha (percent) for the believed enemy class shares. Lower = smoother/slower.")]
		public readonly int ThreatSmoothingAlphaPct = 20;

		[Desc("Believed enemy class shares (per-mille) strictly below this contribute NO counter bias —",
			"a single scouted unit must not re-plan the army.")]
		public readonly int ThreatDeadbandPerMille = 30;

		[Desc("EXPERIMENTAL (composition ceilings): stop the three lanes that buy PAST UnitTargetShares.",
			"All three effects are inert without this flag:",
			"  * a cycle where composed types are buildable but all priced out or at their UnitLimit DECLINES",
			"    instead of falling back to ChooseRandomUnitToBuild — that fallback is a uniform lottery, i.e.",
			"    exactly the lifetime-proportional drift CompositionDirected exists to remove, so taking it",
			"    whenever the bot is momentarily broke quietly re-admits the bug;",
			"  * the external-request FIFO (counter-composition buys from AdaptiveProductionBotModule) is folded",
			"    under the targets, so a side lane cannot buy past them. The PRIORITY lane is deliberately NOT",
			"    bounded — the capture-supply floor must stay able to out-compete — and types named in",
			"    CompositionCeilingExemptTypes are exempt on the FIFO lane too;",
			"  * the module's OWN deficit pick drops every class STRICTLY over target",
			"    (ForceCompositionMath.ApplyCeilingEligibility), so an over-target class is never bought at all.",
			"That third lane is NOT the no-op it looks like. Restricting the argmax and then falling BACK to the",
			"unrestricted one would be (see ForceCompositionMath.SelectDeficit); restricting it and DECLINING is",
			"not, because eligibility is affordability-filtered. In the low-cash band the only eligible member of",
			"a queue is its CHEAPEST type, so the unrestricted argmax degenerates into 'buy the cheapest thing'",
			"every cycle without limit — measured live as a bot that filled its Supply Route with 10+ humvees",
			"(450, the cheapest composed vehicle) against a 40‰ target while never banking enough for the armour",
			"core. Declining banks the cash instead.",
			"Default false ⇒ the frozen fallback, the unbounded FIFO and the unrestricted argmax all run",
			"unchanged, so normal/rush/turtle/@stable keep their RNG draw count and order.")]
		public readonly bool CompositionEnforceTargetCeiling = false;

		[Desc("Actor types whose EXTERNAL requests bypass the composition ceiling on the FIFO lane. The",
			"capture-supply floor is the intended member: its availability contract ('a capturer must be",
			"obtainable when there is something to capture') outranks the composition shape, and its own",
			"alive-or-pending floor already bounds it. Naming the type here is deliberate rather than relying",
			"on CaptureCoordinatorBotModule.TecnRequestPriority being set — that flag lives in another file and",
			"its FIFO fall-back path (CaptureCoordinatorBotModule.MaintainTecnFloor) would otherwise be",
			"silently ceiling-bounded the moment someone turns it off. Inert unless",
			"CompositionEnforceTargetCeiling is set.")]
		public readonly HashSet<string> CompositionCeilingExemptTypes = new HashSet<string>();

		[Desc("Own units currently granted this condition are EXCLUDED from the composition census.",
			"An evacuating unit is leaving the battlefield, so it is not force-in-being and must not",
			"suppress a replacement buy. Empty ⇒ no exclusion.")]
		public readonly string CensusExcludeCondition = "evacuating";

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). UnitQueues holds production-queue
			// Type tokens (Vehicle/Infantry/Aircraft), NOT actor names, so it stays ordinal.
			ActorNameCase.NormalizeKeysInPlace(UnitsToBuild);
			ActorNameCase.NormalizeKeysInPlace(UnitLimits);
			ActorNameCase.NormalizeKeysInPlace(UnitDelays);
			ActorNameCase.NormalizeInPlace(ResupplyUnitTypes);
			ActorNameCase.NormalizeInPlace(AntiAirUnitTypes);
			ActorNameCase.NormalizeInPlace(TransportUnitTypes);
			ActorNameCase.NormalizeInPlace(CompositionCeilingExemptTypes);

			// UnitTargetShares is keyed by actor name and compared against Info.Name (always lowercase, see
			// conventions.md) — the same case-mismatch trap the sets above are hardened against. UnitRoles
			// (string values, no ActorNameCase overload) and the CounterMatrixPct "enemyclass>ownrole" tokens
			// are lowercased where the module flattens them in Created.
			ActorNameCase.NormalizeKeysInPlace(UnitTargetShares);
		}

		public override object Create(ActorInitializer init) { return new UnitBuilderBotModule(init.Self, this); }
	}

	public class UnitBuilderBotModule : ConditionalTrait<UnitBuilderBotModuleInfo>, IBotTick, IBotNotifyIdleBaseUnits, IBotRequestUnitProduction, IBotRequestPriorityUnitProduction, IGameSaveTraitData
	{
		public const int FeedbackTime = 30; // ticks; = a bit over 1s. must be >= netlag.

		readonly World world;
		readonly Player player;

		readonly List<string> queuedBuildRequests = new List<string>();

		// WW3MOD @experimental: high-priority requests, drained BEFORE the normal FIFO and the blind lottery so
		// a capture-supply floor out-competes combat buys for the queue slot. Stays EMPTY unless a caller opts in
		// via IBotRequestPriorityUnitProduction — no @experimental capture floor requesting here ⇒ the list is
		// never populated ⇒ BotTick is byte-identical for normal/rush/turtle/@stable. Transient (not persisted):
		// the requester re-issues each scan, so a save/load simply re-derives it.
		readonly List<string> priorityBuildRequests = new List<string>();

		IBotRequestPauseUnitProduction[] requestPause;
		int idleUnitCount;

		int ticks;

		// ===== Composition-directed purchasing (@experimental; all null/empty when CompositionDirected is off) =====
		// The Dictionaries are flattened ONCE in Created into ordinal-ordered arrays: the runtime math must never
		// depend on Dictionary enumeration order (it is unspecified and can differ between runs of the same seed).
		// compositionTypes[i] is the actor name of ordinal slot i; every other array is parallel to it.
		string[] compositionTypes;
		int[] compositionTargets;      // designer target share per slot, per-mille (pre-bias).
		int[] compositionRoleIndex;    // slot -> index into compositionRoles, or -1 for "no role".
		string[] compositionRoles;     // ordinal-sorted distinct role tokens.
		int[,] counterMatrix;          // [enemy class, role index] percent, from CounterMatrixPct.
		int[] smoothedThreatShares;    // persisted EMA state, parallel to ThreatClasses.
		BeliefStore beliefStore;
		bool compositionInitialized;
		bool beliefStoreResolved;
		int lastThreatTick = -1;

		// The believed-enemy classification is the existing 3-way split (see AdaptiveProductionBotModule
		// .ClassifyContact); this array fixes both the ordinal slot order and the YAML key spelling.
		static readonly string[] ThreatClasses = { "air", "armor", "infantry" };

		public UnitBuilderBotModule(Actor self, UnitBuilderBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void Created(Actor self)
		{
			requestPause = self.Owner.PlayerActor.TraitsImplementing<IBotRequestPauseUnitProduction>().ToArray();

			InitializeComposition();
		}

		// Flatten the composition config into ordinal-ordered arrays exactly once. Skipped entirely when the
		// flag is off ⇒ no allocation, no belief-store lookup, nothing to diverge on the frozen path.
		void InitializeComposition()
		{
			if (compositionInitialized)
				return;

			compositionInitialized = true;

			if (!Info.CompositionDirected || Info.UnitTargetShares == null || Info.UnitTargetShares.Count == 0)
				return;

			// An all-zero (or all-negative) share table is a CONFIG ERROR, not a policy. SharesPerMille would
			// apportion it to all zeros, every deficit would tie at 0, and the ordinal tie-break would then buy
			// the lowest-ordinal eligible type forever. Stay inactive (legacy picker) and say so once, instead
			// of silently shipping a degenerate purchase plan.
			if (!Info.UnitTargetShares.Values.Any(v => v > 0))
			{
				AIUtils.BotDebug("{0} CompositionDirected is set but no UnitTargetShares entry is positive — falling back to the legacy picker.", player);
				return;
			}

			// Ordinal sort by actor name: the slot order is then a pure function of the config text.
			compositionTypes = Info.UnitTargetShares.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
			compositionTargets = new int[compositionTypes.Length];
			compositionRoleIndex = new int[compositionTypes.Length];

			// Role token per type (lowercased here — UnitRoles has string values, so ActorNameCase's
			// Dictionary<string,int> key-normalizer does not apply to it).
			var rolesByType = new Dictionary<string, string>();
			if (Info.UnitRoles != null)
				foreach (var kv in Info.UnitRoles)
					rolesByType[kv.Key.ToLowerInvariant()] = kv.Value.ToLowerInvariant();

			compositionRoles = rolesByType.Values.Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray();

			for (var i = 0; i < compositionTypes.Length; i++)
			{
				compositionTargets[i] = Info.UnitTargetShares[compositionTypes[i]];
				compositionRoleIndex[i] = rolesByType.TryGetValue(compositionTypes[i], out var role)
					? Array.IndexOf(compositionRoles, role)
					: -1;
			}

			// Targets are apportioned once so the runtime compares like with like (census is per-mille too).
			compositionTargets = ForceCompositionMath.SharesPerMille(compositionTargets);

			// The YAML matrix is keyed by ROLE, but the target vector the bias is applied to is indexed by
			// TYPE — so parse into a role matrix and then EXPAND it into type columns here, once. Doing this
			// at flatten time is what keeps the per-buy math a plain array read (and stops a role/type index
			// mix-up from silently disabling the bias: ApplyCounterBias requires column count == targets).
			var roleMatrix = new int[ThreatClasses.Length, compositionRoles.Length];
			if (Info.CounterMatrixPct != null)
			{
				foreach (var kv in Info.CounterMatrixPct)
				{
					var key = kv.Key.ToLowerInvariant();
					var sep = key.IndexOf('>');
					if (sep <= 0 || sep >= key.Length - 1)
						continue;

					var enemyClass = Array.IndexOf(ThreatClasses, key.Substring(0, sep).Trim());
					var ownRole = Array.IndexOf(compositionRoles, key.Substring(sep + 1).Trim());
					if (enemyClass < 0 || ownRole < 0)
						continue;

					roleMatrix[enemyClass, ownRole] = kv.Value;
				}
			}

			counterMatrix = new int[ThreatClasses.Length, compositionTypes.Length];
			for (var i = 0; i < compositionTypes.Length; i++)
			{
				var role = compositionRoleIndex[i];
				if (role < 0)
					continue;

				for (var c = 0; c < ThreatClasses.Length; c++)
					counterMatrix[c, i] = roleMatrix[c, role];
			}

			smoothedThreatShares = new int[ThreatClasses.Length];
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
				// @experimental priority requests first — before the normal FIFO and the blind lottery below —
				// so a capture-supply floor claims the queue slot ahead of combat buys. Empty for every profile
				// that never opts in ⇒ this block is skipped ⇒ byte-identical.
				// PEEK-DON'T-POP: only remove the head once BuildUnit has ACTUALLY issued the order. When the
				// Infantry queue is busy this cycle BuildUnit returns false and the request STAYS at the head, so
				// it is retried next cycle and claims the NEXT free slot — instead of being silently discarded
				// while combat buys churn the queue (the measured supply-side deadlock). The drain still runs
				// before the FIFO/lottery below, so the surviving priority item pre-empts the next free slot; an
				// in-progress build is never cancelled (BuildUnit only takes an empty queue).
				if (priorityBuildRequests.Count > 0 && BuildUnit(bot, priorityBuildRequests[0]))
					priorityBuildRequests.RemoveAt(0);

				// The FIFO carries counter-composition buys (AdaptiveProductionBotModule). Those ride the
				// single-name BuildUnit overload, which applies NO UnitsToBuild / UnitDelays / UnitLimits and
				// no composition test — so without the ceiling below a counter class is bought without bound.
				// The request is consumed either way (as before): the requester re-issues each of its own
				// cycles, so dropping a refused one keeps the list from growing behind a permanent ceiling.
				var buildRequest = queuedBuildRequests.FirstOrDefault();
				if (buildRequest != null)
				{
					if (!RequestIsOverCompositionCeiling(buildRequest))
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

		bool IBotRequestPriorityUnitProduction.RequestPriorityUnitProduction(IBot bot, string requestedActor)
		{
			// Reject when this twin is condition-disabled: its BotTick never runs (ModularBot ticks only enabled
			// modules), so a request accepted here would never be drained/built — it would only inflate the
			// caller's pending count forever (the measured pending=82 / alive=0 deadlock). The caller routes to
			// the first twin that returns true, guaranteeing the request lands on the enabled UnitBuilder.
			if (IsTraitDisabled)
				return false;

			priorityBuildRequests.Add(requestedActor);
			return true;
		}

		int IBotRequestUnitProduction.RequestedProductionCount(IBot bot, string requestedActor)
		{
			// Count BOTH lists so a requester's alive-or-pending gate sees priority requests in flight too.
			// priorityBuildRequests is empty on every non-opted-in profile ⇒ same result as the frozen path.
			return priorityBuildRequests.Count(r => r == requestedActor)
				+ queuedBuildRequests.Count(r => r == requestedActor);
		}

		void BuildUnit(IBot bot, string category, bool buildRandom)
		{
			// Pick a free queue
			var queue = AIUtils.FindQueuesByCategory(player)[category].FirstOrDefault(q => !q.AllQueued().Any());
			if (queue == null)
				return;

			// @experimental composition-directed pick. When the flag is off the legacy ternary below is reached
			// unchanged — same branches, same LocalRandom draws, same order — so the frozen profiles are
			// byte-identical. When it is on, ChooseByDeficit draws ZERO random and falls back to the SAME
			// ternary for a queue it has no opinion about (e.g. a heli-only queue with no target shares).
			var unit = Info.CompositionDirected ?
				ChooseByDeficit(queue, buildRandom) :
				(buildRandom ? ChooseRandomUnitToBuild(queue) : ChooseUnitToBuild(queue));

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

			if (Info.GateTransportOnDemand && Info.TransportUnitTypes.Contains(name) && !ShouldBuyTransport(unit))
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

		// Behaviour 3: is calling in one more transport justified? Refuses unless there is a full minimum
		// load of infantry actually waiting AND no idle transport we already own could carry them
		// (transports-first). The decision itself lives in TransportEmploymentMath; this only samples world
		// state. Both counts are ORDER-INDEPENDENT (plain sums over a filter, and the candidate tally is
		// capped at the airframe's own MaxWeight), so world.Actors' iteration order cannot leak into the
		// decision — the determinism invariant the byte-identity argument rests on.
		bool ShouldBuyTransport(ActorInfo transportInfo)
		{
			// A gated type that carries nothing has no lift-demand model — leave the frozen decision alone
			// rather than silently banning it.
			var cargo = transportInfo.TraitInfoOrDefault<CargoInfo>();
			if (cargo == null)
				return true;

			CPos? srCell = null;
			foreach (var a in world.Actors)
			{
				if (a.Owner == player && !a.IsDead && a.IsInWorld && Info.SupplyRouteTypes.Contains(a.Info.Name))
				{
					srCell = a.Location;
					break;
				}
			}

			var owned = 0;
			var idle = 0;
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				if (!Info.TransportUnitTypes.Contains(a.Info.Name))
					continue;

				owned++;

				// A transport mid-load or mid-delivery is NOT spare capacity; only a genuinely unoccupied one
				// counts against the transports-first test. IsUnoccupiedAirframe, not Actor.IsIdle: a transport
				// hovering at the SR carries FlyIdle forever, so the old test counted ZERO idle transports
				// always — the transports-first branch could never fire and only UnitLimits capped the buy.
				if (AIUtils.IsUnoccupiedAirframe(a))
					idle++;
			}

			// Lift demand. Must agree with the consuming squad module's CountLiftCandidates or the two halves of
			// the transport policy contradict each other (buy an airframe the launcher will never load, or refuse
			// one it is starving for). Same predicate: infantry of a compatible cargo type inside the SR reserve
			// bubble and not claimed by another module. NOT Actor.IsIdle — infantry on the line engage through
			// AutoTarget and are never idle, so the old world-wide idle scan almost never reached
			// TransportMinPassengers and the demand gate refused essentially every call-in.
			var goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
			var candidates = 0;
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				if (!a.Info.HasTraitInfo<Render.WithInfantryBodyInfo>() || !a.Info.HasTraitInfo<MobileInfo>())
					continue;

				if (!cargo.Types.Overlaps(a.GetAllTargetTypes()))
					continue;

				if (goalGuard != null && !goalGuard.IsTraitDisabled && goalGuard.Ledger.IsCommitted(a, world.WorldTick))
					continue;

				if (srCell.HasValue
					&& !TransportEmploymentMath.InReserveZone((a.Location - srCell.Value).LengthSquared, Info.LiftReserveZoneRadiusCells))
					continue;

				if (++candidates >= cargo.MaxWeight)
					break;
			}

			// Reuse the configured per-type ceiling as the transport cap. NOTE what this does and does NOT buy:
			// UnitLimits counts WORLD ACTORS only, so a call-in still sitting in the production queue is
			// invisible to it — and this cap inherits exactly that blind spot. It is therefore not what stops a
			// concurrent second call-in. UnitDelays does not either: it is an ABSOLUTE world-tick opening
			// threshold, not a repeat cooldown. The real serializer is BuildUnit's empty-queue precondition
			// (it only picks a queue with nothing already queued), and with one Aircraft queue per Supply Route
			// that admits no second simultaneous call-in; once ProductionFromMapEdge spawns the unit into the
			// world, UnitLimits bounds the rest. The residual is multi-SR (a second captured SR = a second
			// queue), and it is bounded by UnitLimits at spawn. 0 ⇒ no ceiling configured.
			var cap = 0;
			if (Info.UnitLimits != null && Info.UnitLimits.TryGetValue(transportInfo.Name, out var limit))
				cap = limit;

			return TransportEmploymentMath.ShouldBuy(owned, cap, idle, candidates, Info.TransportMinPassengers);
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

		// In cases where we want to build a specific unit but don't know the queue name (because there's more than one possibility).
		// Returns true iff an order was actually issued (a matching queue was free). The priority-drain caller uses
		// this to peek-don't-pop: a request that can't be placed this cycle (busy queue) stays queued for retry.
		bool BuildUnit(IBot bot, string name)
		{
			var actorInfo = world.Map.Rules.Actors[name];
			if (actorInfo == null)
				return false;

			var buildableInfo = actorInfo.TraitInfoOrDefault<BuildableInfo>();
			if (buildableInfo == null)
				return false;

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
				return true;
			}

			return false;
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

		// ===== Composition-directed pick (@experimental) =====
		// Buy the type the army is furthest BELOW its target share, instead of drawing uniformly at random.
		// Zero random draws. Returns null only when the legacy fallback also declines; a queue this pass has no
		// opinion about (nothing eligible — e.g. a pool with no UnitTargetShares entries at all) falls back to
		// the legacy ternary so purchase VOLUME is never reduced by the flag.
		ActorInfo ChooseByDeficit(ProductionQueue queue, bool buildRandom)
		{
			if (compositionTypes == null || compositionTypes.Length == 0)
				return buildRandom ? ChooseRandomUnitToBuild(queue) : ChooseUnitToBuild(queue);

			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var census = ForceCompositionMath.SharesPerMille(CensusValues());
			var targets = ForceCompositionMath.ApplyCounterBias(compositionTargets, UpdateThreatShares(),
				counterMatrix, Info.CounterBiasMaxPct, Info.ThreatDeadbandPerMille);

			// Materialize once: BuildableItems() is a lazy query and eligibility probes it per slot.
			var buildableNames = new HashSet<string>(buildableThings.Select(b => b.Name));

			var budget = AvailableBudget();
			var eligible = new bool[compositionTypes.Length];
			var anyComposedTypeInQueue = false;
			for (var i = 0; i < compositionTypes.Length; i++)
			{
				eligible[i] = IsCompositionCandidateEligible(compositionTypes[i], buildableNames, budget);
				anyComposedTypeInQueue |= buildableNames.Contains(compositionTypes[i]);
			}

			// CEILING on our OWN pick (experimental). Without this the affordability filter above turns the
			// argmax into "buy the cheapest composed type in this queue" whenever cash is in the low band, no
			// matter how far over target that type already sits — see ApplyCeilingEligibility. anyComposedTypeInQueue
			// is measured before this, so stripping the over-target slots routes into the decline path below
			// (bank the cash) rather than the uniform-lottery fallback.
			if (Info.CompositionEnforceTargetCeiling)
				eligible = ForceCompositionMath.ApplyCeilingEligibility(targets, census, eligible);

			var idx = ForceCompositionMath.SelectDeficit(targets, census, eligible);
			if (idx < 0)
			{
				// Nothing eligible — but for two very different reasons, and the frozen path conflates them.
				// "No composed type is buildable from this queue at all" is genuinely no opinion (a heli-only
				// pool) and must fall back so purchase volume is unchanged. "Composed types ARE buildable but
				// every one is priced out or at its UnitLimit" is a DECISION not to buy: falling back there
				// draws the uniform lottery, which buys by lifetime and rebuilds the very drift this lane
				// exists to remove — and since a bot spends to zero routinely, the affordability case alone
				// makes that fallback frequent enough to dominate the outcome.
				if (ForceCompositionMath.ShouldDeclineCycle(Info.CompositionEnforceTargetCeiling, false, anyComposedTypeInQueue))
				{
					// Invisible buys were the original diagnostic problem; don't replace them with invisible
					// non-buys. One line per declined cycle, on the same channel as the selection log.
					AIUtils.BotDebug("{0} composition-directed DECLINE: {1} composed types buildable, none eligible (priced out or at UnitLimit)",
						player, queue.Info.Type);
					return null;
				}

				return buildRandom ? ChooseRandomUnitToBuild(queue) : ChooseUnitToBuild(queue);
			}

			LogCompositionChoice(compositionTypes[idx], ForceCompositionMath.DeficitAt(targets, census, idx), census, targets);

			return world.Map.Rules.Actors[compositionTypes[idx]];
		}

		// Is an externally requested call-in ALREADY at or over its composition target? Only the FIFO drain
		// asks; the priority drain (capture-supply floor) is exempt by construction, and named types are
		// additionally exempt on this lane via CompositionCeilingExemptTypes. A type with no target share —
		// helicopters, MCVs, harvesters — has no composition opinion and is never refused here.
		//
		// The request under test is STILL on queuedBuildRequests when this is called (BotTick removes it after),
		// and CensusValues credits both request lists, so its own cost is in the census and has to come back out
		// — otherwise the rule reads "would be over AFTER this buy" and refuses a class still legitimately short
		// by less than one unit's worth of share. Consequence worth stating plainly: this predicate and the
		// deficit pick therefore evaluate DIFFERENT census bases by exactly one candidate's cost. That is
		// deliberate, not an inconsistency — the pick is choosing what to add, this is judging what already
		// stands.
		bool RequestIsOverCompositionCeiling(string name)
		{
			if (!Info.CompositionEnforceTargetCeiling || compositionTypes == null)
				return false;

			if (Info.CompositionCeilingExemptTypes.Contains(name))
				return false;

			var slot = Array.IndexOf(compositionTypes, name);
			if (slot < 0)
				return false;

			if (!world.Map.Rules.Actors.TryGetValue(name, out var actorInfo))
				return false;

			var targets = ForceCompositionMath.ApplyCounterBias(compositionTargets, UpdateThreatShares(),
				counterMatrix, Info.CounterBiasMaxPct, Info.ThreatDeadbandPerMille);

			var over = ForceCompositionMath.RequestExceedsCeiling(CensusValues(), slot, UnitCost(actorInfo), targets);
			if (over)
				AIUtils.BotDebug("{0} composition ceiling REFUSED external request: {1} is already at or over target",
					player, name);

			return over;
		}

		// Own-force census in VALUE (ValuedInfo.Cost), bucketed into the ordinal slots. Two deliberate details:
		//   * EVACUATING units are excluded — a unit flying/driving off the map to refund its cost is not
		//     force-in-being, and counting it would suppress the very replacement buy it should trigger.
		//   * PENDING call-ins are credited at FULL cost. The purchase cycle is 30 ticks but a reinforcement
		//     takes far longer to walk in from the map edge, so without this the bot would re-pick the same
		//     deficit type every cycle and badly overshoot it before the first one ever arrives.
		// The accumulation is additive into fixed slots, so it is order-independent by construction (same
		// argument AdaptiveProductionBotModule uses for its believed-value sums) — no sort needed for determinism.
		int[] CensusValues()
		{
			var values = new int[compositionTypes.Length];
			var excludeCondition = Info.CensusExcludeCondition;
			var hasExclude = !string.IsNullOrEmpty(excludeCondition);

			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;

				var slot = Array.IndexOf(compositionTypes, a.Info.Name);
				if (slot < 0)
					continue;

				if (hasExclude && a.GetConditionCount(excludeCondition) > 0)
					continue;

				values[slot] += UnitCost(a.Info);
			}

			// Pending credit: everything already queued/in-progress on the module's own queues, plus both
			// request lists. The queue BuildUnit selected is empty by construction (it filters on
			// !AllQueued().Any()), so crediting only "this queue" would always be a no-op — the credit has to
			// span the module's queues to actually damp the overshoot it exists to prevent.
			var queues = AIUtils.FindQueuesByCategory(player);
			foreach (var category in Info.UnitQueues)
			{
				foreach (var q in queues[category])
				{
					foreach (var item in q.AllQueued())
					{
						var slot = Array.IndexOf(compositionTypes, item.Item);
						if (slot >= 0 && world.Map.Rules.Actors.TryGetValue(item.Item, out var pendingInfo))
							values[slot] += UnitCost(pendingInfo);
					}
				}
			}

			CreditTransportedUnits(values, excludeCondition, hasExclude);

			CreditRequests(values, priorityBuildRequests);
			CreditRequests(values, queuedBuildRequests);

			return values;
		}

		// TRANSPORTED / GARRISONED units are ABSENT from world.Actors entirely, so the loop above cannot see
		// them — boarding REMOVES the passenger from the world dictionary (RideTransport.cs:85 `w.Remove(self)`,
		// right after Cargo.Load), it does not merely clear IsInWorld. Both loaders are live on the profile that
		// enables this: MountedTransportBotModule loads infantry into bradley/bmp2/m113, and GarrisonBotModule
		// garrisons infantry into buildings. Without this pass every loaded unit reads as a PERMANENT deficit and
		// the bot re-buys it forever — self-reinforcing, and able to re-create the exact mortar pile-up this lane
		// exists to fix (mortar infantry is transportable).
		//
		// Cargo is the single authoritative container for both cases: a garrison's shelter soldiers live in the
		// building's own Cargo (GarrisonManager.cs:185/:1209 — shelterPassengers mirrors it), and a soldier
		// DEPLOYED to a firing port is `cargo.Unload`ed and re-added to the world (:341/:372), so it is already
		// counted by the main loop. The two sources are therefore disjoint: no double-count against world.Actors,
		// and none against the pending-queue/request credit either (that counts things not yet delivered).
		void CreditTransportedUnits(int[] values, string excludeCondition, bool hasExclude)
		{
			foreach (var pair in world.ActorsWithTrait<Cargo>())
			{
				var transport = pair.Actor;
				if (transport.IsDead || !transport.IsInWorld)
					continue;

				// Look inside our own and allied containers only. This misses nothing, because a container
				// holding THIS player's soldiers is always player- or ally-owned: a garrison claims its
				// building for the entering soldier's owner on the way in (GarrisonManager.OnPassengerEntered
				// -> ChangeOwnerInPlace, :253-262) and reverts/transfers only once occupants leave (:320-332),
				// and vehicle transports are loaded by MountedTransportBotModule pairing the bot's own infantry
				// with its own carriers. (Cargo.CanLoad itself does NOT gate on ownership — it tests only
				// LoadingBlocked, the ICargoCanLoadFilter hooks, and space, Cargo.cs:279-289.) Restricting the
				// scan also keeps the census off enemy cargo — the fog-legality constraint is that this pass
				// reads the player's OWN force, not hidden enemy state.
				if (transport.Owner != player && player.RelationshipWith(transport.Owner) != PlayerRelationship.Ally)
					continue;

				// NOTE: passengers are deliberately NOT IsInWorld-checked — being out of the world is precisely
				// what makes them invisible to the main census loop.
				foreach (var passenger in pair.Trait.Passengers)
				{
					if (passenger.Owner != player || passenger.IsDead)
						continue;

					var slot = Array.IndexOf(compositionTypes, passenger.Info.Name);
					if (slot < 0)
						continue;

					if (hasExclude && passenger.GetConditionCount(excludeCondition) > 0)
						continue;

					values[slot] += UnitCost(passenger.Info);
				}
			}
		}

		void CreditRequests(int[] values, List<string> requests)
		{
			foreach (var request in requests)
			{
				var slot = Array.IndexOf(compositionTypes, request);
				if (slot >= 0 && world.Map.Rules.Actors.TryGetValue(request, out var requestInfo))
					values[slot] += UnitCost(requestInfo);
			}
		}

		// Believed enemy value per class -> per-mille shares -> integer EMA. FOG-LEGAL: the belief store is the
		// player's own memory of what it has legally seen (never ground truth). Recomputed at most once per world
		// tick so the two BuildUnit calls in one BotTick cycle share one EMA step instead of double-advancing it.
		int[] UpdateThreatShares()
		{
			if (!beliefStoreResolved)
			{
				beliefStoreResolved = true;
				beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
			}

			if (beliefStore == null || world.WorldTick == lastThreatTick)
				return smoothedThreatShares;

			lastThreatTick = world.WorldTick;

			var values = new int[ThreatClasses.Length];
			foreach (var c in beliefStore.Contacts(player))
			{
				if (!world.Map.Rules.Actors.TryGetValue(c.TypeName, out var ai))
					continue;

				var value = UnitCost(ai) * c.Confidence / 100;
				if (value <= 0)
					continue;

				var cls = ClassifyBelievedContact(ai);
				if (cls >= 0)
					values[cls] += value;
			}

			smoothedThreatShares = ForceCompositionMath.SmoothShares(smoothedThreatShares,
				ForceCompositionMath.SharesPerMille(values), Info.ThreatSmoothingAlphaPct);

			return smoothedThreatShares;
		}

		// Index into ThreatClasses, or -1 for "not a mobile threat". Classify by the ENEMY unit's own type, not
		// by what its weapon can target — so an attack helicopter is AIR (answered by AA), never ground.
		// Mirrors AdaptiveProductionBotModule.ClassifyContact (private there; the two must stay in step).
		static int ClassifyBelievedContact(ActorInfo ai)
		{
			if (ai.HasTraitInfo<AircraftInfo>())
				return 0; // air

			if (!ai.HasTraitInfo<MobileInfo>())
				return -1; // structures / immobile

			return ai.HasTraitInfo<Render.WithInfantryBodyInfo>() ? 2 : 1; // infantry : armor
		}

		// A slot is eligible only if the buy would ACTUALLY go through — every gate BuildUnit applies after the
		// pick is checked here too, so an ineligible type is skipped rather than silently wasting the cycle
		// (BuildUnit's post-pick gates `return` without buying anything).
		bool IsCompositionCandidateEligible(string name, HashSet<string> buildableNames, long budget)
		{
			if (!buildableNames.Contains(name))
				return false;

			if (!world.Map.Rules.Actors.TryGetValue(name, out var actorInfo))
				return false;

			if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
				return false;

			if (Info.UnitDelays != null && Info.UnitDelays.TryGetValue(name, out var delay) && delay > world.WorldTick)
				return false;

			if (Info.UnitLimits != null && Info.UnitLimits.TryGetValue(name, out var limit) &&
				world.Actors.Count(a => a.Owner == player && a.Info.Name == name) >= limit)
				return false;

			if (Info.GateResupplyOnAmmoNeed && Info.ResupplyUnitTypes.Contains(name) && !AnyFieldedUnitNeedsResupply())
				return false;

			if (Info.ScaleAntiAirToThreat && Info.AntiAirUnitTypes.Contains(name) && !ShouldBuildMoreAntiAir())
				return false;

			if (Info.GateTransportOnDemand && Info.TransportUnitTypes.Contains(name) && !ShouldBuyTransport(actorInfo))
				return false;

			if (!CompositionNeedMath.Affordable(budget, UnitCost(actorInfo), 100))
				return false;

			return HasAdequateAirUnitReloadBuildings(actorInfo);
		}

		// Observability channel: one line per composition-directed selection, so autotest log-mining can chart
		// the standing composition against its targets without a benchmark run. Top 3 by census share — the
		// over-accumulating types are exactly the ones that need to show up in the chart.
		void LogCompositionChoice(string chosen, int deficit, int[] census, int[] targets)
		{
			var top = Enumerable.Range(0, compositionTypes.Length)
				.OrderByDescending(i => census[i])
				.ThenBy(i => i)
				.Take(3)
				.Select(i => compositionTypes[i] + " " + census[i] + "/" + targets[i]);

			AIUtils.BotDebug("{0} composition-directed buy: {1} (deficit {2}) [top {3}]",
				player, chosen, deficit, string.Join(", ", top));
		}

		long AvailableBudget()
		{
			var res = player.PlayerActor.TraitOrDefault<PlayerResources>();
			return res != null ? (long)res.Cash + res.Resources : 0;
		}

		static int UnitCost(ActorInfo ai)
		{
			var valued = ai.TraitInfoOrDefault<ValuedInfo>();
			return valued?.Cost ?? 0;
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

			var data = new List<MiniYamlNode>()
			{
				new MiniYamlNode("QueuedBuildRequests", FieldSaver.FormatValue(queuedBuildRequests.ToArray())),
				new MiniYamlNode("IdleUnitCount", FieldSaver.FormatValue(idleUnitCount))
			};

			// Composition-directed EMA state. Only emitted when the flag is on, so a frozen-profile save is
			// byte-identical to before. Reloading without it would restart the threat smoothing from zero and
			// briefly un-bias the targets.
			if (smoothedThreatShares != null)
				data.Add(new MiniYamlNode("SmoothedThreatShares", FieldSaver.FormatValue(smoothedThreatShares)));

			return data;
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

			// Restore only into a live composition array — a save written with the flag on must not resurrect
			// state on a profile that has it off (the arrays stay null there and nothing reads them anyway).
			var threatSharesNode = data.Nodes.FirstOrDefault(n => n.Key == "SmoothedThreatShares");
			if (threatSharesNode != null && smoothedThreatShares != null)
			{
				var restored = FieldLoader.GetValue<int[]>("SmoothedThreatShares", threatSharesNode.Value.Value);
				if (restored != null && restored.Length == smoothedThreatShares.Length)
					smoothedThreatShares = restored;
			}
		}
	}
}
