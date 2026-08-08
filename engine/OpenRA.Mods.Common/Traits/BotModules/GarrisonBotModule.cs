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
	[Desc("Sends idle passengers to garrison friendly-or-neutral GarrisonManager buildings near the base.",
		"NOT infantry-and-defence-structures only, whatever the name suggests: GarrisonActorTypes is unset in",
		"WW3MOD so the unit side falls back to any Passenger holder (narrowed per building by the CanEnter",
		"cargo-type match), and ^CivBuilding carries GarrisonManager so the building side is dominated by",
		"neutral civilian houses. RequireBelievedThreat is what makes it a defensive reaction rather than an",
		"unconditional drain on the idle pool.")]
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

		[Desc("Require a REASON to garrison: only hold a building a believed enemy weapon can actually reach.",
			"Without this the module carries no enemy, danger, belief, influence or POI term at all —",
			"PrioritizeExposed below is a List.Sort comparator, a reordering that never removes a candidate, so",
			"with no enemies every building scores 0 and the pairing degenerates to an arbitrary house near",
			"baseCenter. That is the idle-technician-in-a-civilian-house bug, and it fires on the bot's very",
			"first tick (scanCountdown defaults to 0). Reads the fog-legal believed DangerFieldLayer.GroundDanger,",
			"NOT ThreatMapManager — the latter's FindActorsInCircle is omniscient and would be a fog leak on the",
			"fog-respecting profiles. NOT narrowed by bot type: this is a bug-class fix on a module every profile",
			"shares, so @stable inherits it. It reaches the players that Participate in the influence stack",
			"(@experimental + @stable) and therefore have a believed-danger signal at all; legacy/normal bots",
			"build no field, read 0 danger everywhere, and keep the old behaviour rather than having the module",
			"silently disabled. Default false ⇒ a profile that omits the field is unchanged.")]
		public readonly bool RequireBelievedThreat = false;

		[Desc("Believed anti-ground danger at a building's cell at/above which garrisoning it is worth a soldier.",
			"Only read when RequireBelievedThreat is active. 1 = 'any believed weapon envelope reaches here'.")]
		public readonly int MinBelievedDanger = 1;

		[Desc("Un-garrison once the believed threat that justified the garrison has passed, so cover is TEMPORARY",
			"rather than terminal. A garrisoned bot unit is otherwise lost for the match: no bot module anywhere",
			"issues Unload at a garrison building (every Unload site in BotModules targets a carrier the issuing",
			"module owns a task for), while ReleaseFinishedClaims hands the blackboard claim back the moment the",
			"unit leaves the world — so the books show a free unit and the battlefield shows nothing. Orderable",
			"because the order is issued at the BUILDING and ValidateOrder compares the subject owner's ClientIndex",
			"against the sender's — a map player such as Neutral is assigned the ADMIN client index (Player.cs:191)",
			"and bot orders ride that same stream, so a still-neutral house validates. (GarrisonManager's",
			"DynamicOwnership does flip the house to us on entry, but that flip is NOT what makes this orderable.)",
			"That equality is the one load-bearing assumption: it holds while the bot host IS the admin client",
			"(Player.cs:225 activates bot logic only on the host). Should the two ever diverge, ValidateOrder drops",
			"the Unload and the release pass re-issues it every scan forever — fail-safe, since no unit is lost and",
			"nothing is corrupted, but a silent permanent no-op, so suspect this first if garrisons stop releasing.",
			"Rides the same danger-field-availability gate as RequireBelievedThreat, and is inert without it.")]
		public readonly bool ReleaseWhenThreatClears = false;

		[Desc("Minimum ticks a building must have been observed garrisoned before release is eligible. Entry and",
			"release are exactly complementary predicates over an INTEGER field that is fully restamped each",
			"recompute, so at a kernel's outermost ring one step of confidence decay moves the contour across the",
			"threshold — without a dwell the pair flaps and a soldier walks in and out on a multi-scan cycle.",
			"Raising MinBelievedDanger does NOT fix that: every threshold has its own ±1 outermost contour, so a",
			"higher bar moves the flap instead of damping it. A two-threshold dead band (enter >= T_in, release <",
			"T_out) would, but it is unavailable at MinBelievedDanger: 1 — there is no room below 1 for a T_out, so",
			"a band would mean raising ENTRY, a doctrine change rather than a debounce. Hence damping on TIME,",
			"which also stays correct if the kernel intensities are ever retuned. 750 ticks is ~45 s at the mod",
			"default Timestep of 60 ms (16.67 ticks/s, mod.yaml GameSpeeds DefaultSpeed: default). Delays only",
			"leaving cover; entering is unaffected. Only read when ReleaseWhenThreatClears is active.")]
		public readonly int MinGarrisonDwellTicks = 750;

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

		// Believed-threat gate. Deliberately NOT narrowed by bot type: this is a bug-class fix on a module every
		// profile shares, so every profile that can supply the signal gets it (CLAUDE.md — @stable inherits
		// improvements, never gated off on purpose). Participates is a RESOURCE test, not a withholding one:
		// legacy/normal bots build no danger field at all, so GroundDanger reads 0 for them everywhere and a
		// gate would silently disable the module instead of improving it — they keep the legacy behaviour until
		// there is an honest signal to gate them with.
		//
		// It must be Participates and NOT "has a field been built yet", even though the latter looks like the
		// more direct question. Fields are created lazily on a participant's first RecomputePlayer, which the
		// deterministic stagger (UpdateInterval/3) plus round-robin puts several ticks in, whereas this module
		// scans on tick 1 (scanCountdown defaults to 0) — a field-existence test would therefore fail OPEN for
		// precisely the opening window the bug was reported in. Participates is knowable at tick 0, and a
		// participant whose field is not built yet honestly reads "no believed threat", which it is.
		//
		// CaptureCoordinatorBotModule.ResolveReserveAnchor answers the same question with the OPPOSITE polarity —
		// it DOES require the field to exist — and both are right, because both fail closed toward doing nothing:
		// here that means suppressing the garrison, there it means issuing no muster order. The pair is coupled,
		// though, and the coupling runs one way: during the opening window the capturer reserve is silent, so a
		// technician with no capture target is idle and unclaimed, and this gate is the only thing keeping that
		// state harmless. Loosening it re-opens the reserve's hole as well as its own.
		DangerFieldLayer dangerField;

		bool ThreatGateActive =>
			Info.RequireBelievedThreat && dangerField != null && InfluenceStack.Participates(player);

		bool WorthGarrisoning(Actor building)
			=> dangerField.GroundDanger(player, building.Location) >= Info.MinBelievedDanger;

		/// <summary>Does this building currently hold at least one of OUR soldiers? Both halves are needed: the
		/// shelter occupants are Cargo passengers, while a soldier deployed to a firing port is in-world and is
		/// NOT in the cargo list (GarrisonManager.cs:342 unloads it from Cargo when it deploys).</summary>
		static bool OccupiedBy(Actor building, Player owner)
		{
			var cargo = building.TraitOrDefault<Cargo>();
			if (cargo != null)
				foreach (var p in cargo.Passengers)
					if (p != null && !p.IsDead && p.Owner == owner)
						return true;

			var garrison = building.TraitOrDefault<GarrisonManager>();
			if (garrison?.PortStates != null)
				foreach (var ps in garrison.PortStates)
					if (ps.DeployedSoldier != null && !ps.DeployedSoldier.IsDead && ps.DeployedSoldier.Owner == owner)
						return true;

			return false;
		}

		static string GarrisonObjectiveKey(Actor building) => "garrison:" + building.ActorID;

		// Buildings OBSERVED to hold one of our soldiers, mapped to the tick we first saw it occupied.
		//
		// Derived from the world each scan rather than written at order time, and that distinction is the whole
		// point: an order-time entry names a building the unit has not reached yet, so a release pass firing in
		// the window between the order and the arrival would queue an Unload against an EMPTY building — which
		// Cargo drops (CanUnload requires !IsEmpty, Cargo.cs:265-271) — and then forget the building, after which
		// the unit walks in and nothing can ever get it out again. That is exactly the terminal garrison this
		// change exists to remove, so the tracked set has to mean "occupied", never "ordered".
		//
		// The tick is the dwell clock (see MinGarrisonDwellTicks).
		readonly Dictionary<Actor, int> garrisonedSince = new Dictionary<Actor, int>();

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

			dangerField = Info.RequireBelievedThreat
				? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;

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

			ReleaseFinishedClaims();

			// Both before any early return below, or a garrison could never be released once the threat that
			// justified it has moved on. Sync first: the release pass reads the dwell clock this maintains.
			SyncGarrisonedBuildings();
			ReleaseClearedGarrisons(bot);

			// Find garrisonable buildings near our base
			var garrisonableBuildings = world.ActorsHavingTrait<GarrisonManager>()
				.Where(a => !a.IsDead && a.IsInWorld
					&& (a.Owner == player || a.Owner.RelationshipWith(player) == PlayerRelationship.Neutral)
					&& (a.Location - baseCenter).Length <= Info.MaxGarrisonRadius)
				.ToList();

			// The reason-to-garrison gate. Note this is a FILTER, which PrioritizeExposed below is not.
			if (ThreatGateActive)
				garrisonableBuildings.RemoveAll(a => !WorthGarrisoning(a));

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

				// Issue garrison order (EnterTransport is how infantry enter garrisoned buildings).
				// Refused ⇒ claim nothing, commit nothing, keep the unit in the available pool and do not
				// spend an order slot: a `garrison:` claim is RankTasking, so predicate (a) would defend it
				// against every other module for a unit that was never sent anywhere.
				if (!bot.QueueOrder(new Order("EnterTransport", infantry, Target.FromActor(building), false)))
					continue;

				// Episode-bounded (MaxOrdersPerTick per ScanInterval), and it carries the believed danger that
				// justified the order — so a live match can answer "was there a reason?" without a batch run.
				// Paired with the release line below; the two bracket the unit's time out of the fight.
				Log.Write("debug",
					$"[garrison] enter player={player.PlayerName} unit={infantry.Info.Name}#{infantry.ActorID} " +
					$"building={building.Info.Name}#{building.ActorID} cell={building.Location} " +
					$"danger={(ThreatGateActive ? dangerField.GroundDanger(player, building.Location) : -1)} " +
					$"tick={world.WorldTick}");

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

				// Deliberately NOT recorded as garrisoned here — the unit has not arrived. SyncGarrisonedBuildings
				// picks the building up once it is actually occupied; see garrisonedSince.
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

		/// <summary>Reconcile the tracked set against what is actually in the buildings: adopt any garrisonable
		/// building now holding one of ours (starting its dwell clock), and drop any that no longer does — the
		/// occupants left, died, or the building did. Runs every scan, so a building we never ordered into (one
		/// of OUR units that got there some other way) is tracked too, which is correct: the release pass asks
		/// "is a unit of mine sitting in a house for no reason", not "did I put it there".
		///
		/// <para>Scope is strictly our own units — OccupiedBy tests <c>Owner == owner</c>, so an ALLY's soldier
		/// neither adopts a building nor holds one adopted, and we never order an ally's garrison out.</para>
		///
		/// <para>Known one-tick blind spot, harmless: GarrisonManager.RecallToShelter nulls DeployedSoldier
		/// (:408) but defers the cargo load to a frame-end task (:414-428), so for one tick a recalled soldier is
		/// in neither half of OccupiedBy and its building is briefly dropped. It cannot reintroduce the terminal
		/// garrison — the adopt loop below re-scans every GarrisonManager actor unconditionally, so the building
		/// is picked up again on the next scan. The only cost is a reset dwell clock, and it resets during
		/// combat, which is exactly when no release is wanted anyway.</para></summary>
		void SyncGarrisonedBuildings()
		{
			// The tracked set exists only to feed the release pass, so a profile that never releases does no work
			// here at all — not merely no observable work (the same early-out discipline StageFreePool opens with).
			if (!Info.ReleaseWhenThreatClears || !ThreatGateActive)
			{
				garrisonedSince.Clear();
				return;
			}

			if (garrisonedSince.Count > 0)
			{
				List<Actor> gone = null;
				foreach (var b in garrisonedSince.Keys)
					if (b.IsDead || !b.IsInWorld || !OccupiedBy(b, player))
						(gone ??= new List<Actor>()).Add(b);

				if (gone != null)
					foreach (var b in gone)
						garrisonedSince.Remove(b);
			}

			foreach (var b in world.ActorsHavingTrait<GarrisonManager>())
				if (!b.IsDead && b.IsInWorld && !garrisonedSince.ContainsKey(b) && OccupiedBy(b, player))
					garrisonedSince[b] = world.WorldTick;
		}

		/// <summary>Eject the garrison of every tracked building whose believed threat has since cleared, so a
		/// soldier goes back to being a soldier. The Unload lands on the BUILDING (GarrisonManager.cs:1338 ejects
		/// port soldiers, Cargo.cs:248-253 queues UnloadCargo for the shelter) — the passengers themselves are out
		/// of the world and unorderable, which is exactly why nothing else could ever recover them.
		///
		/// <para>The entry is NOT removed here. Removal is SyncGarrisonedBuildings' job, on the next scan, once
		/// the building is observably empty — so an Unload that did not take effect is simply re-issued rather
		/// than silently forgotten. Only buildings already observed occupied are ever candidates, so the Unload
		/// always has something to unload.</para>
		///
		/// <para>Deliberately uncapped, unlike the assign pass's MaxOrdersPerTick. The asymmetry is intended:
		/// the cap exists to stop the module draining the idle pool faster than the rest of the AI can react,
		/// and releasing has the opposite sign — it RETURNS units. Capping it would strand the overflow in cover
		/// for another scan for no benefit. The order count is bounded anyway by the tracked set, and ModularBot
		/// drains its queue at a fixed rate regardless.</para></summary>
		void ReleaseClearedGarrisons(IBot bot)
		{
			if (!Info.ReleaseWhenThreatClears || !ThreatGateActive || garrisonedSince.Count == 0)
				return;

			var tick = world.WorldTick;

			foreach (var kv in garrisonedSince)
			{
				if (WorthGarrisoning(kv.Key))
					continue;

				// Dwell. Entry (>= MinBelievedDanger) and release (< MinBelievedDanger) are exactly
				// complementary by construction, so without this the pair flaps: the danger field is cleared and
				// fully restamped each recompute, and the stamped contribution is INTEGER, so at a kernel's
				// outermost ring one step of confidence decay moves the whole contour across the threshold.
				//
				// Note precisely what this does and does not claim. A single WIDER threshold cannot fix it: every
				// threshold has its own outermost ±1 contour, so raising the bar moves the flap rather than
				// damping it. A two-threshold DEAD BAND (enter >= T_in, release < T_out with T_out < T_in) genuinely
				// would — a ±1 wobble cannot cross a 2-wide band — and that form is simply unavailable at the
				// shipped config, because MinBelievedDanger is 1 and there is no room below it for a T_out; a band
				// would mean raising entry, which is a doctrine change, not a debounce. So the damping is on TIME:
				// it bounds the flap FREQUENCY, which is the part that shows on screen, and it does so without any
				// assumption about the field's scale — it stays correct if the kernel intensities are ever retuned.
				//
				// Asymmetric on purpose: it delays only LEAVING cover. Entering stays instantaneous, so a real
				// threat is still answered on the next scan.
				if (tick - kv.Value < Info.MinGarrisonDwellTicks)
					continue;

				bot.QueueOrder(new Order("Unload", kv.Key, false));

				Log.Write("debug",
					$"[garrison] release player={player.PlayerName} building={kv.Key.Info.Name}#{kv.Key.ActorID} " +
					$"cell={kv.Key.Location} held-ticks={tick - kv.Value} tick={tick}");
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
			garrisonedSince.Clear();
		}
	}
}
