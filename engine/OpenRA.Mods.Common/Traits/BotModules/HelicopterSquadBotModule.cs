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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits.BotModules.Squads;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Manages AI helicopter squads with role-based behavior (attack, scout, transport).",
		"Helicopters are grouped into squads based on their AIHelicopterRole trait and managed independently from ground units.")]
	public class HelicopterSquadBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Minimum attack helicopters needed before launching an attack mission.")]
		public readonly int AttackSquadSize = 2;

		[Desc("Random bonus added to attack squad size.")]
		public readonly int AttackSquadSizeBonus = 1;

		[Desc("Ticks between launching attack missions.")]
		public readonly int AttackCooldown = 900;

		[Desc("Doctrine (default false = frozen pairing): allow launching an attack-heli mission BELOW the",
			"randomised preferred size — down to MinAttackSquadSize — instead of benching helis until a full",
			"pair/trio is ready. A single attack heli is already a large investment; waiting for a second is",
			"too restrictive unless income is high. OFF by default so legacy/normal/@stable stay byte-identical",
			"(the preferred-size RNG draw is unchanged); only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool AllowSoloAttackHeli = false;

		[Desc("Smallest attack-heli count that may launch a mission when AllowSoloAttackHeli is set.",
			"1 = a lone heli deploys rather than idling. Only used when AllowSoloAttackHeli is set.")]
		public readonly int MinAttackSquadSize = 1;

		[Desc("Spendable resources (Cash + Resources) at or above which income counts as HIGH: with a solo",
			"heli ready and no second yet, the bot WAITS to accumulate a pair (it can afford to mass); below",
			"it, the lone heli is committed rather than benched. ~one attack heli's cost. Only used when",
			"AllowSoloAttackHeli is set.")]
		public readonly int PairUpIncomeThreshold = 6000;

		[Desc("Ticks between scout missions.")]
		public readonly int ScoutInterval = 400;

		[Desc("Ticks between transport missions.")]
		public readonly int TransportInterval = 600;

		[Desc("Minimum infantry to load before launching a transport mission.")]
		public readonly int TransportMinInfantry = 4;

		[Desc("Ticks a dispatched transport heli waits for its ordered passengers to actually board before",
			"the mission is resolved without a full load. If at least one passenger is aboard when this",
			"elapses the heli delivers a partial load; if NONE boarded the mission is ABORTED (cargo never",
			"embarked — a lost/poached/killed load) and the heli returns to the idle pool instead of flying",
			"the delivery leg EMPTY. Mirrors MountedTransportBotModuleInfo.LoadingTimeoutTicks. Bug-class:",
			"the empty-delivery fix is a correctness change and applies to every profile (see the staged",
			"loading in TryLaunchTransportMission / AdvanceTransportTasks).")]
		public readonly int TransportLoadTimeoutTicks = 1500;

		[Desc("Commit each ordered transport passenger to the shared PoiGoalGuard ledger (key transport:<heliId>)",
			"the moment the board order is issued, so no other bot module poaches a soldier while it is walking",
			"to the heli — the 'passengers poached en route to boarding' half of the empty-delivery bug. Released",
			"on delivery dispatch, on abort, and on teardown. OFF by default so legacy/@stable stay byte-identical",
			"(no ledger interaction); only HelicopterSquadBotModule@experimental turns it on. Mirrors",
			"MountedTransportBotModuleInfo.CommitPassengers.")]
		public readonly bool CommitTransportPassengers = false;

		[Desc("Maximum number of active helicopter squads at once.")]
		public readonly int MaxActiveSquads = 3;

		[Desc("EXPERIMENTAL transport employment (default 0 = frozen): reserved transport-mission slots, so lift",
			"stops competing with the attack loop for MaxActiveSquads. TryLaunchTransportMission bails on",
			"`activeSquads.Count >= MaxActiveSquads` but a transport mission never ADDS to activeSquads — so once",
			"the attack loop holds MaxActiveSquads squads, lift is starved PERMANENTLY while the counter never",
			"reflects a single transport mission. That asymmetry (not a missing role case — the launcher does",
			"select Role == Transport) is why bought transports idle at the SR all match. When > 0 the launcher",
			"uses this reserved slice instead; 0 keeps the frozen shared-budget gate ⇒ byte-identical.")]
		public readonly int TransportMissionSlots = 0;

		[Desc("EXPERIMENTAL transport employment (default false = frozen): USE-OR-EVAC. A transport heli that has",
			"been idle past TransportIdleEvacuateTicks with no lift it can fly — no load waiting, no free mission",
			"slot, or not mission-READY (a chip-damaged transport can never be picked: ReEngageHealthPercent is 90",
			"and there is no AI repair host) — is evacuated to reserves, banking",
			"its salvage refund and ending its upkeep drain, instead of parking at the Supply Route for the rest",
			"of the match. Terminal by design — no hold-and-recheck. Independent of EvacuateWhenIdle, which covers",
			"only the ATTACK roles (the idle evaluator's role filter admits AttackHeavy/AttackLight and skips",
			"Transport entirely, so nothing ever retired a transport). OFF by default so @stable is byte-identical.")]
		public readonly bool EvacuateIdleTransports = false;

		[Desc("Consecutive idle ticks a transport heli must sit with no flyable lift before it is evacuated to",
			"reserves. Only used when EvacuateIdleTransports is set; 0 disables the evac branch.",
			"CONFIG COUPLING: 'flyable' means the reserved-slice launcher could take it, so with",
			"TransportMissionSlots at 0 (the frozen shared-budget launcher) Employ is UNREACHABLE — every",
			"transport evacuates at this window even when the frozen launcher could have flown it. Set both",
			"together, or neither. SECOND COUPLING: RiskWeightedDropSite must also be on — the frozen drop-site",
			"picker filters on threat < 50 and can stay closed indefinitely, re-opening the Employ-shadows-Evacuate",
			"pin via the unfolded dropZone residual; the risk-weighted picker is a weight, not a filter.")]
		public readonly int TransportIdleEvacuateTicks = 900;

		[Desc("Ticks between checking helicopter pool for new assignments.")]
		public readonly int ScanInterval = 100;

		[Desc("Ticks between updating active squads.")]
		public readonly int SquadUpdateInterval = 5;

		[Desc("Recon: minimum distance (map cells) from the scout's own Supply Route a recon target must",
			"be, so scout helis sweep OUT over the map instead of hovering above home. Mirrors",
			"ScoutBotModule.MinScoutDistance.")]
		public readonly int ScoutMinDistanceCells = 15;

		[Desc("Recon: minimum spacing (map cells) between the recon targets handed to two scout helis in",
			"the same pass, so multiple littlebirds fan out to DISTINCT areas instead of stacking on one",
			"cell. Only relevant when more than one scout heli is idle at once.")]
		public readonly int ScoutTargetSpacingCells = 12;

		[Desc("Careful scout employment (default false = frozen). The littlebird is a fragile troop-carrier/",
			"scout, not a strike aircraft — so route + recon-target selection respects the KNOWN (fog-legal)",
			"anti-air danger field and a penetration bound: a recon cell whose believed air-danger — or whose",
			"straight flight path's believed air-danger — exceeds ScoutAirDangerSafeThreshold is REJECTED, and",
			"a cell farther than ScoutMaxDistanceCells from home is rejected so a lone scout does not dive deep",
			"into unscouted territory and get shot down. Reads DangerFieldLayer (Stage B), never the omniscient",
			"threat grid. OFF by default so legacy/@stable stay byte-identical (the picker is unchanged); only",
			"HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool CarefulScoutEmployment = false;

		[Desc("Careful scout employment: believed air-danger at or below which a recon cell / flight path is",
			"treated as safe. 0 = strictly outside every believed anti-air envelope (the air channel carries no",
			"territory baseline, so 0 is a true 'no believed AA can shoot a heli here' test). Only used when",
			"CarefulScoutEmployment is set.")]
		public readonly int ScoutAirDangerSafeThreshold = 0;

		[Desc("Careful scout employment: maximum distance (map cells) from the scout's own home a recon target",
			"may be — the penetration bound against blindly scouting the enemy backfield alone. Unscouted cells",
			"carry no belief data (air-danger reads 0), so this geometry cap is what 'no deep penetration into",
			"unscouted territory' rests on before first contact. 0 = no cap (danger gate only). Only used when",
			"CarefulScoutEmployment is set.")]
		public readonly int ScoutMaxDistanceCells = 0;

		[Desc("Skip the full-ammo readiness gate when launching missions. WW3MOD attack helis only refill",
			"at an hpad and the mod builds none, so a heli below full ammo can NEVER become mission-ready —",
			"no squad ever forms and the helicopters idle forever. This is the squad-path twin of the",
			"production-side SkipRearmBuildingCheck trap. OFF by default so legacy/normal/stable behaviour is",
			"unchanged; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool SkipRearmReadyCheck = false;

		[Desc("Use standoff (attack-move) engagement for attack-heli squads. When on, the squad FSM issues",
			"AttackMove toward the target cell instead of a bare Attack on a single (possibly distant) target,",
			"so AutoTarget engages the nearest in-range threat at weapon standoff and the squad only advances",
			"when clear — helis stop and fire at missile range instead of overflying nearer enemies to reach a",
			"distant target. OFF by default so legacy/normal/stable behaviour is byte-identical; only",
			"HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool StandoffEngagement = false;

		[Desc("Influence stack Stage D: consume the per-player ANTI-AIR danger field (DangerFieldLayer) so",
			"attack-heli squads route AROUND believed AA, leash their standoff to the AA-safe envelope, and",
			"withdraw/re-route when a NEW AA threat lights up on the field mid-flight. Rides on top of",
			"StandoffEngagement (only takes effect while that is on). OFF by default so legacy/normal/stable",
			"behaviour is byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool DangerFieldAvoidance = false;

		[Desc("Stage-D: cell air-danger at or below which a cell is treated as AA-safe (leash target /",
			"acceptable detour). 0 = strictly outside every believed anti-air envelope. Air-only tunable.")]
		public readonly int AirDangerSafeThreshold = 0;

		[Desc("Stage-D: air-danger at the squad's own position above which a newly-believed AA is taken to",
			"cover the squad and it withdraws / re-routes. Above SafeThreshold so leash grazing does not flap.")]
		public readonly int AirDangerSpikeThreshold = 30;

		[Desc("Stage-D: how far from the target (cells) to search for an AA-safe standoff cell to leash to.")]
		public readonly int AirDangerLeashCells = 6;

		[Desc("Stage-D: lateral offset magnitude (cells) for detour waypoints that route around AA.")]
		public readonly int AirDangerDetourCells = 6;

		[Desc("Stage-D: ring radius (cells) searched for the safest air-aware retreat cell on withdraw.")]
		public readonly int AirDangerRetreatCells = 12;

		[Desc("Influence-stack frontier standoff: hold the attack-heli standoff at least this many COARSE",
			"control-field cells behind the believed enemy frontier (ControlField distance-to-enemy-region).",
			"When the leashed engage cell lands closer than this, it is walked rearward (bounded) toward the",
			"squad so helis hold BEHIND the believed front line, not on it. Rides on StandoffEngagement.",
			"0 = OFF (default; byte-identical). Inert until a ControlField is populated for this player.")]
		public readonly int MinFrontierDistanceCells = 0;

		[Desc("Experimental (default false = frozen): when an idle attack heli is still loitering",
			"within ForwardStagingMaxDistanceCells of its own Supply Route and no squad has formed,",
			"push it forward to a pre-contact staging cell (a fraction of the way from the SR toward",
			"the top PoiMap offensive target) instead of leaving it hovering at the SR corner. Mirrors",
			"MountedTransportBotModule.DeliverBeforeContact. OFF by default so normal/rush/turtle/stable",
			"stay byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool ForwardStaging = false;

		[Desc("Fraction (percent) of the SR->top-offensive-POI distance used as the staging cell.",
			"50 = halfway between our SR and the top offensive POI. Clamp well short of contact so",
			"ammo-carrying, target-less helis do not stage into believed AA. Only used when ForwardStaging is set.")]
		public readonly int ForwardStagingPct = 40;

		[Desc("Only stage attack helis whose distance from the SR is at or below this (map cells).",
			"Helis already forward (e.g. a low-ammo heli that returned near the front) are left alone.",
			"Only used when ForwardStaging is set.")]
		public readonly int ForwardStagingMaxDistanceCells = 8;

		[Desc("Actor types of the bot's home Supply Route — used to anchor the staging vector.",
			"Mirrors MountedTransportBotModuleInfo.SupplyRouteTypes.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Experimental mission-employment (default false = frozen): treat attack helis as hit-and-run",
			"assets with an exit strategy. An attack heli that goes idle with no believed worthwhile target",
			"— or is spent (out of ammo) with no rearm host to refill at — is EVACUATED to reserves via the",
			"map edge (RotateToEdge), reclaiming its salvage value (full Cost with ammo, less spent-ammo value)",
			"and stopping its upkeep drain, instead of parking at the SR/staging corner forever (the corner-idle",
			"bug). A believed target instead keeps the heli HELD for the squad mission loop. Fog-legal: the",
			"'worthwhile target' read is the belief store, never ground truth. OFF by default so normal/rush/",
			"turtle/stable stay byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool EvacuateWhenIdle = false;

		[Desc("Consecutive idle ticks an attack heli must loiter near home with no believed worthwhile target",
			"before it is evacuated to reserves. Spent-with-no-rearm helis evac immediately (this gate does not",
			"apply to them). Only used when EvacuateWhenIdle is set.")]
		public readonly int EvacuateIdleTicks = 500;

		[Desc("Radius (map cells) around the own Supply Route within which an idle, target-less heli counts as",
			"'loitering at home' and becomes evac-eligible. A heli forward of this (e.g. mid-withdraw near the",
			"front) is left to the squad FSM. Only used when EvacuateWhenIdle is set.")]
		public readonly int EvacuateHomeRadiusCells = 12;

		[Desc("Max distance (map cells) from a heli to a believed enemy contact for that contact to count as a",
			"worthwhile mission target: a target in range HOLDS the heli for a mission, none in range makes it",
			"evac-eligible. Only used when EvacuateWhenIdle is set.")]
		public readonly int MissionTargetRangeCells = 60;

		[Desc("Mission-complete evac (experimental, default false = frozen). Extend the idle evac so an attack",
			"heli that finished a mission FORWARD (beyond EvacuateHomeRadiusCells) and has since gone idle past",
			"the window with NO believed worthwhile target evacuates to reserves too, instead of loitering at the",
			"front indefinitely with no follow-up mission. RotateToEdge routes it toward its OWN Supply-Route edge",
			"(friendly side), banking the salvage refund and ending the upkeep drain. Without this, only helis",
			"idling within the home radius are reclaimed. Only used when EvacuateWhenIdle is set; OFF by default so",
			"the frozen forward-hold behaviour is preserved for any profile that does not opt in.")]
		public readonly bool EvacuateForwardIdle = false;

		[Desc("Phase 4 strategic-target pinning (experimental, default false = frozen). Pin an attack-heli",
			"squad's STRATEGIC objective in the squad separate from its tactical TargetActor: the FSM keeps its",
			"5-tick standoff/danger-nav micro AND the bounded too-hot soft-swap, but a lapsed tactical target,",
			"a too-hot cell, or a withdraw no longer churn the strategic destination — the squad resumes toward",
			"the pinned objective instead of re-picking the nearest enemy (root cause C, design §1.3/§3.3). The",
			"pin releases ONLY on an abort trigger: objective invalid / objective-too-hot-with-no-divert / stalled",
			"(unreachable) / the bounded commit window below. OFF by default so legacy/normal/rush/turtle/stable are",
			"byte-identical; only HelicopterSquadBotModule@experimental turns it on. Mirrors the offense module's",
			"MissionCommitmentEnabled gating.")]
		public readonly bool StrategicTargetPinning = false;

		[Desc("Bounded commit-window backstop (ticks) for a pinned strategic objective (design §3.3 TTL valve).",
			"A pin held longer than this releases so an objective that never resolves cannot trap the squad",
			"forever. 0 = OFF (hold purely on the abort triggers), matching MissionCommitmentMath's window valve.",
			"Only used when StrategicTargetPinning is set.")]
		public readonly int PinCommitWindowTicks = 0;

		[Desc("Flight-path hysteresis (experimental, default false = frozen). Smooth attack-heli movement so it",
			"reads as DELIBERATE instead of indecisive: the squad only re-issues a move / attack-move path order",
			"when the recomputed destination has shifted at least FlightPathHysteresisCells from the leg it is",
			"already committed to (or the leg completed), rather than re-pathing on every 5-tick re-eval. Affects",
			"ONLY order-issue cadence — the destination itself is still chosen by the existing standoff / danger-nav",
			"/ frontier logic, so the first-contact AA gate, strategic-target pin, and evacuation are untouched. OFF",
			"by default so legacy/@stable stay byte-identical; only HelicopterSquadBotModule@experimental turns it on.")]
		public readonly bool FlightPathHysteresis = false;

		[Desc("Flight-path hysteresis: minimum Chebyshev (chessboard) cell distance the recomputed destination",
			"must shift before a new path order is issued mid-leg. Below it the squad holds its committed",
			"destination; a completed leg (unit idle) always re-issues. Larger = more deliberate but slower to",
			"track a relocating objective. Only used when FlightPathHysteresis is set.")]
		public readonly int FlightPathHysteresisCells = 3;

		[Desc("Risk/reachability-weighted transport drop-site selection (experimental, default false = frozen).",
			"The frozen picker drops infantry at the single omniscient WEAKEST enemy cell, which can land the drop",
			"deep behind the enemy Supply Route (a lone cheap unit there reads 'weakest') — unreachable and lethal.",
			"When set, the picker instead RANKS a candidate set (the weak cell + the top believed offensive POIs)",
			"by a fog-legal risk/reachability score and drops at the best: cells deep in BELIEVED-enemy territory,",
			"far from our own SR, or inside a believed danger envelope are heavily penalised versus reachable flank",
			"/ side POIs. A weight, not a filter — a deep cell can still win if it is the least-bad option. Reads",
			"ControlField / DangerFieldLayer (belief-side) + own-SR distance ONLY, never ground-truth enemy",
			"positions. OFF by default so legacy/@stable stay byte-identical; only @experimental turns it on.")]
		public readonly bool RiskWeightedDropSite = false;

		[Desc("Risk-weighted drop: number of top believed offensive POIs added to the drop-site candidate set",
			"(the reachable flank/side alternatives to the omniscient weak cell). Only used when RiskWeightedDropSite is set.")]
		public readonly int DropSiteCandidatePois = 6;

		[Desc("Risk-weighted drop: penalty weight (x100) applied to believed-ENEMY control DEPTH at a candidate",
			"cell (ControlField negative score magnitude — how far behind the believed front / enemy SR anchor it",
			"sits). The dominant term against deep-behind-enemy-SR drops. Only used when RiskWeightedDropSite is set.")]
		public readonly int DropEnemyControlWeight = 100;

		[Desc("Risk-weighted drop: penalty weight (x100) applied to believed danger (ground + air DangerFieldLayer",
			"readings) at a candidate cell. Only used when RiskWeightedDropSite is set.")]
		public readonly int DropDangerWeight = 100;

		[Desc("Risk-weighted drop: penalty per map cell of distance from our own Supply Route to the candidate",
			"(reachability — a farther drop means a longer, more exposed flight). Only used when RiskWeightedDropSite is set.")]
		public readonly int DropReachWeight = 5;

		public override object Create(ActorInitializer init) { return new HelicopterSquadBotModule(init.Self, this); }
	}

	public class HelicopterSquadBotModule : ConditionalTrait<HelicopterSquadBotModuleInfo>, IBotTick, IBotEnabled
	{
		readonly World world;
		readonly Player player;

		readonly List<Squad> activeSquads = new List<Squad>();
		readonly List<Actor> idleHelicopters = new List<Actor>();
		readonly HashSet<Actor> managedHelicopters = new HashSet<Actor>();
		readonly Dictionary<Actor, CPos> stagedTo = new Dictionary<Actor, CPos>();

		// Reused scratch for the rotating-recon scout picker so a scan allocates nothing per tick:
		// believed-POI cells gathered once per pass, and the targets already handed out this pass
		// (so a second scout is fanned to a distinct area). Only touched on the scout path.
		readonly List<CPos> poiScratchCells = new List<CPos>();
		readonly List<CPos> assignedScratch = new List<CPos>();

		// EvacuateWhenIdle bookkeeping (experimental). Consecutive idle ticks per managed heli, a reused
		// scratch list of believed-contact cells so the worthwhile-target scan allocates nothing per tick,
		// and the set of helis currently flying their evac (RotateToEdge) — excluded from re-adoption and
		// recruitment so the evac is never cancelled by a squad order. `enemyEverObserved` latches once the
		// belief store has ever held a contact, gating the target-less evac branch. All only ever touched
		// on the EvacuateWhenIdle path ⇒ inert (byte-identical) when the flag is off.
		readonly Dictionary<Actor, int> idleTicks = new Dictionary<Actor, int>();
		readonly List<CPos> targetScratch = new List<CPos>();
		readonly HashSet<Actor> evacuating = new HashSet<Actor>();
		bool enemyEverObserved;

		// Transport-role helis dispatched on a delivery, tracked until their cargo is confirmed unloaded.
		// The dispatch queues an immediate Move home for the common (successful-unload) case; this set is the
		// safety net for the rare case where the drop cell is unlandable, so UnloadCargo completes WITHOUT
		// unloading (Cargo.CanUnload false → UnloadCargo.cs:161 returns done) and the queued Move would fly the
		// heli home still LOADED. EnsureTransportsUnload re-issues Unload wherever it ends up, so a full
		// transport never idles loaded. (The mounted path's dispatch-time CanUnload gate does NOT transfer
		// here: at dispatch the passengers are only just ordered to board, so cargo is empty and the heli is
		// not at the drop yet — CanUnload would always be false and would delete the retreat entirely.)
		readonly HashSet<Actor> transportsAwaitingUnload = new HashSet<Actor>();

		// Empty-delivery fix: transport helis that have been ORDERED to load but have not yet confirmed a
		// full/partial cargo aboard. A dispatched transport now stages Loading -> Delivering: it does not fly
		// the delivery leg until AdvanceTransportTasks confirms cargo actually embarked, so a heli whose
		// passengers were killed/poached/never-boarded aborts instead of delivering nothing. Once dispatched it
		// leaves this map and (loaded) joins transportsAwaitingUnload for the existing retreat/safety net.
		readonly Dictionary<Actor, TransportLoadTask> transportTasks = new Dictionary<Actor, TransportLoadTask>();

		IBot bot;
		SquadManagerBotModule squadManagerRef;
		ThreatMapManager threatMap;
		PoiMap poiMap;
		BeliefStore beliefStore;
		BotBlackboard blackboard;

		// Per-unit commitment ledger (shared PoiGoalGuard). Resolved ONLY when CommitTransportPassengers is on,
		// so the frozen/@stable path never looks it up ⇒ byte-identical. Null when the player has no PoiGoalGuard
		// ⇒ every commit/release below is inert. Mirrors MountedTransportBotModule.goalGuard.
		PoiGoalGuard goalGuard;

		// Fog-legal believed anti-air danger field (Stage B). Read ONLY on the careful-scout path; resolving the
		// reference is behaviour-inert, so leaving it always-resolved does not affect byte-identity.
		DangerFieldLayer dangerField;

		// Fog-legal believed-territory control field (Stage C). Read ONLY on the risk-weighted drop-site path;
		// resolving the reference is behaviour-inert. Null ⇒ the control term is skipped (reachability + danger
		// still reshape), and the whole path is gated by RiskWeightedDropSite anyway ⇒ byte-identical when off.
		ControlField controlField;

		// Per-pass careful-scout config, set at the top of TryLaunchScoutMission and read by ConsiderReconCandidate
		// (same reused-scratch idiom as poiScratchCells). scoutAirDangerAt is null when the lever is off / no field,
		// which is the signal ConsiderReconCandidate uses to skip the danger gate entirely (byte-identical).
		Func<CPos, int> scoutAirDangerAt;
		long scoutMaxDistSq;
		int scoutAirSafeThreshold;

		bool initialized;

		int scanCountdown;
		int attackCooldown;
		int scoutCooldown;
		int transportCooldown;
		int squadUpdateCountdown;

		// A transport heli that has been ordered to load but has not yet confirmed cargo aboard. Only the
		// short Loading phase lives here; on dispatch the heli moves to transportsAwaitingUnload.
		sealed class TransportLoadTask
		{
			public Actor Transport;
			public CPos DropZone;
			public int StateChangedAtTick;
			public HashSet<Actor> ReservedPassengers = new HashSet<Actor>();
		}

		public HelicopterSquadBotModule(Actor self, HelicopterSquadBotModuleInfo info)
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

			// Find SquadManager on player actor for Squad construction (required by Squad class)
			squadManagerRef = player.PlayerActor.TraitsImplementing<SquadManagerBotModule>()
				.FirstOrDefault(s => !s.IsTraitDisabled);

			threatMap = world.WorldActor.TraitOrDefault<ThreatMapManager>();
			poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
			beliefStore = world.WorldActor.TraitOrDefault<BeliefStore>();
			dangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();
			controlField = world.WorldActor.TraitOrDefault<ControlField>();
			blackboard = player.PlayerActor.TraitsImplementing<BotBlackboard>()
				.FirstOrDefault(b => !b.IsTraitDisabled);

			// Resolve the shared commitment ledger only when the transport-passenger commit lever is on, so the
			// frozen path never touches it ⇒ byte-identical (mirrors MountedTransportBotModule).
			goalGuard = Info.CommitTransportPassengers
				? player.PlayerActor.TraitOrDefault<PoiGoalGuard>() : null;

			initialized = true;
		}

		void IBotTick.BotTick(IBot bot)
		{
			Initialize();

			// Update active squads frequently
			if (--squadUpdateCountdown <= 0)
			{
				squadUpdateCountdown = Info.SquadUpdateInterval;
				UpdateSquads();
			}

			// Scan for new helicopters less frequently
			if (--scanCountdown <= 0)
			{
				scanCountdown = Info.ScanInterval;
				FindNewHelicopters();
				CleanUpHelicopters();
				StageIdleHelicopters();
				MarkScoutExploration();
				AdvanceTransportTasks(bot);
				EnsureTransportsUnload(bot);
			}

			// Attack missions
			if (--attackCooldown <= 0)
			{
				attackCooldown = Info.AttackCooldown;
				TryLaunchAttackMission();
			}

			// Scout missions
			if (--scoutCooldown <= 0)
			{
				scoutCooldown = Info.ScoutInterval;
				TryLaunchScoutMission();
			}

			// Transport missions
			if (--transportCooldown <= 0)
			{
				transportCooldown = Info.TransportInterval;
				TryLaunchTransportMission();
			}

			// Mission-employment: evacuate idle/spent attack helis to reserves (experimental-only,
			// default off ⇒ byte-identical). Runs every tick so the idle-duration gate counts game
			// ticks, not scan intervals.
			EvaluateIdleHelicopters();
		}

		void FindNewHelicopters()
		{
			// Exclude helis currently flying their evac: re-adopting one (it left managedHelicopters when
			// evacuated) would put it back in the idle pool and a squad order would cancel the RotateToEdge,
			// so the heli would fight without ammo and never stop draining upkeep. The set is only ever
			// populated on the EvacuateWhenIdle path, so this clause is a no-op (byte-identical) when off.
			var helicopters = world.ActorsHavingTrait<AIHelicopterRole>()
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& !managedHelicopters.Contains(a) && !evacuating.Contains(a));

			foreach (var h in helicopters)
			{
				managedHelicopters.Add(h);

				// Claim in blackboard to prevent other modules from taking it
				if (blackboard != null)
					blackboard.ClaimUnit(h, "helicopter");

				// Add to idle pool if not rearming
				if (!idleHelicopters.Contains(h))
					idleHelicopters.Add(h);
			}
		}

		void CleanUpHelicopters()
		{
			// Remove dead/destroyed helicopters
			managedHelicopters.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld);
			idleHelicopters.RemoveAll(a => a == null || a.IsDead || !a.IsInWorld);

			// Drop staged entries the moment a heli dies OR leaves the idle pool (recruited into a
			// squad). A returning heli is re-eligible for staging only when near the SR again (§distance
			// gate). Only ever populated on the ForwardStaging path, so this is a no-op when the flag is off.
			foreach (var a in stagedTo.Keys.ToList())
				if (a == null || a.IsDead || !a.IsInWorld || !idleHelicopters.Contains(a))
					stagedTo.Remove(a);

			// Drop idle-tick counters for helis that died or left management (evacuated / disowned).
			// Only ever populated on the EvacuateWhenIdle path, so this is a no-op when the flag is off.
			foreach (var a in idleTicks.Keys.ToList())
				if (a == null || a.IsDead || !a.IsInWorld || !managedHelicopters.Contains(a))
					idleTicks.Remove(a);

			// Drop evacuating helis once they have left the world (RotateToEdge disposes them at the map
			// edge). Predicate-based ⇒ iteration-order-independent. No-op when the flag is off.
			evacuating.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld);

			// Drop dead/gone/DISOWNED transports from the awaiting-unload tracker (EnsureTransportsUnload also
			// prunes, but keep the hygiene at the same choke point as the other sets). The owner clause mirrors
			// AdvanceTransportTasks: a captured transport we keep tracking would pin a reserved mission slot
			// (ActiveTransportMissions) for the rest of the match. No-op when none are tracked.
			transportsAwaitingUnload.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld || a.Owner != player);

			// Drop dead/gone transports mid-load: release any ledger commitment for their reserved passengers
			// (so a soldier that survived the heli's death re-enters offense's free pool) and forget the task.
			// AdvanceTransportTasks also prunes, but do it at the same choke point as the other sets.
			foreach (var a in transportTasks.Keys.ToList())
			{
				if (a == null || a.IsDead || !a.IsInWorld)
				{
					ReleaseTaskPassengers(transportTasks[a]);
					transportTasks.Remove(a);
				}
			}

			// Clean up squads
			PruneSquads();

			// Return idle helicopters from disbanded squads back to pool
			foreach (var h in managedHelicopters)
			{
				if (h.IsDead || !h.IsInWorld)
					continue;

				// A transport that is mid-load (Loading task live) or mid-delivery (loaded, awaiting unload) is
				// deliberately NOT idle-pooled: it must not be re-picked for a second mission while it still holds
				// or is boarding cargo. It re-enters the pool via AdvanceTransportTasks (abort) or once the delivery
				// completes (EnsureTransportsUnload drops it from transportsAwaitingUnload when its cargo empties).
				if (transportTasks.ContainsKey(h) || transportsAwaitingUnload.Contains(h))
					continue;

				var inSquad = false;
				foreach (var squad in activeSquads)
				{
					if (squad.Units.Contains(h))
					{
						inSquad = true;
						break;
					}
				}

				if (!inSquad && !idleHelicopters.Contains(h))
					idleHelicopters.Add(h);
			}
		}

		// Spendable resources for the pair-up income gate (Cash + Resources). 0 if no PlayerResources.
		int SpendableResources()
		{
			var pr = player.PlayerActor.TraitOrDefault<PlayerResources>();
			return pr != null ? pr.Cash + pr.Resources : 0;
		}

		Actor FindOwnSupplyRoute()
		{
			return world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
		}

		// Pre-contact forward staging (experimental, ForwardStaging). Push idle attack helis that
		// are still loitering near the SR forward to a fraction of the SR->top-POI vector, so they
		// stage toward the fight instead of hovering at the SR corner. Deterministic: PoiMap query
		// + integer vector math, ZERO random draws. Fully skipped (byte-identical) when the flag is off.
		void StageIdleHelicopters()
		{
			if (!Info.ForwardStaging)
				return;

			var ownSR = FindOwnSupplyRoute();
			if (ownSR == null)
				return;
			var srCell = ownSR.Location;

			var stageCell = ForwardStagingCell(srCell);
			if (!stageCell.HasValue)
				return;

			var maxDistSq = (long)Info.ForwardStagingMaxDistanceCells * Info.ForwardStagingMaxDistanceCells;

			foreach (var h in idleHelicopters)
			{
				if (h.IsDead || !h.IsInWorld || !h.IsIdle)
					continue;
				if (stagedTo.ContainsKey(h))
					continue;

				// Attack helis only — scouts/transports have their own mission paths.
				var role = h.TraitOrDefault<AIHelicopterRole>();
				if (role == null)
					continue;
				var r = role.Info.Role;
				if (r != HelicopterAIRole.AttackHeavy && r != HelicopterAIRole.AttackLight)
					continue;

				// Same readiness definition the squad launch uses (health gate always applies;
				// ammo gate bypassed under SkipRearmReadyCheck exactly as for TryLaunchAttackMission).
				if (!IsReadyForMission(h))
					continue;

				// Only stage helis still loitering near the SR — leave forward/returned helis alone.
				if ((h.Location - srCell).LengthSquared > maxDistSq)
					continue;

				bot.QueueOrder(new Order("Move", h, Target.FromCell(world, stageCell.Value), false));
				stagedTo[h] = stageCell.Value;

				AIUtils.BotDebug("AI ({0}): heli forward-staging {1} {2} -> {3}",
					player.ClientIndex, h.Info.Name, h.Location, stageCell.Value);
			}
		}

		// Staging-cell math — mirrors MountedTransportBotModule.PreContactStagingCell exactly.
		// The pure WPos interpolation is extracted to HeliStagingMath (NUnit-pinned, world-free).
		CPos? ForwardStagingCell(CPos srCell)
		{
			if (poiMap == null)
				return null;

			var targets = poiMap.GetOffensiveTargets(player);
			if (targets.Count == 0)
				return null;

			var srPos = world.Map.CenterOfCell(srCell);
			var tgtPos = world.Map.CenterOfCell(targets[0].Location);
			var stagePos = HeliStagingMath.StagePos(srPos, tgtPos, Info.ForwardStagingPct);
			var cell = world.Map.CellContaining(stagePos);
			return world.Map.Contains(cell) ? cell : (CPos?)null;
		}

		// Drop dead/not-in-world/foreign members from every active squad and remove squads left
		// with no units. Mirrors the engine-standard SquadManagerBotModule.CleanSquads. MUST run
		// before UpdateSquads: a squad state tick that reaches a Disposed member throws
		// ("Attempted to get trait from destroyed object") the instant it touches a trait
		// (GetRole/health/ammo). Pruning only on the slow ScanInterval is not enough — members die
		// between scans and the 5-tick squad update would iterate the stale list first.
		void PruneSquads()
		{
			for (var i = activeSquads.Count - 1; i >= 0; i--)
			{
				var squad = activeSquads[i];
				squad.Units.RemoveAll(a => a == null || a.IsDead || !a.IsInWorld || a.Owner != player);

				if (!squad.IsValid)
					activeSquads.RemoveAt(i);
			}
		}

		void UpdateSquads()
		{
			PruneSquads();

			foreach (var squad in activeSquads)
				squad.Update();
		}

		void TryLaunchAttackMission()
		{
			if (activeSquads.Count >= Info.MaxActiveSquads)
				return;

			if (squadManagerRef == null)
				return;

			// Get idle attack helicopters
			var attackHelicopters = idleHelicopters
				.Where(h =>
				{
					var role = h.TraitOrDefault<AIHelicopterRole>();
					if (role == null)
						return false;

					var r = role.Info.Role;
					return r == HelicopterAIRole.AttackHeavy || r == HelicopterAIRole.AttackLight;
				})
				.Where(h => IsReadyForMission(h))
				.ToList();

			// Preferred (pairing) size. The RNG draw is kept in the same place with the same arguments so
			// the frozen path stays byte-identical for @stable / legacy.
			var preferredSize = Info.AttackSquadSize + world.LocalRandom.Next(Info.AttackSquadSizeBonus + 1);
			var ready = attackHelicopters.Count;

			// Frozen doctrine: launch ONLY once the full preferred pair/trio is ready, else wait.
			// Solo doctrine (experimental): a lone attack heli is already a big investment — don't bench it
			// forever waiting for a twin. Commit down to MinAttackSquadSize, holding out for a pair only when
			// income is high enough to afford massing a second. HeliPackageMath keeps this pure/NUnit-pinned.
			int launchSize;
			if (ready >= preferredSize)
				launchSize = preferredSize;
			else if (Info.AllowSoloAttackHeli
				&& HeliPackageMath.ShouldLaunchPartial(ready, preferredSize, Info.MinAttackSquadSize, SpendableResources(), Info.PairUpIncomeThreshold))
				launchSize = ready;
			else
				return;

			// Create a helicopter attack squad
			var squad = new Squad(bot, squadManagerRef, SquadType.Helicopter);

			var assigned = 0;
			foreach (var h in attackHelicopters)
			{
				if (assigned >= launchSize)
					break;

				squad.Units.Add(h);
				idleHelicopters.Remove(h);
				assigned++;
			}

			activeSquads.Add(squad);
		}

		// Rotating recon employment for scout helis (littlebirds). Root-cause fix for the corner-park bug:
		// the old picker read GetExplorationAge but NEVER called MarkExplored on the heli path, so every
		// never-visited cell stayed at the int.MaxValue sentinel and the strict `age > bestAge` from 0
		// locked onto the FIRST in-bounds grid cell (a fixed map corner) every single mission — the scout
		// was re-issued a Move to the identical corner it already sat on forever, and only ONE scout was
		// ever tasked. Now EVERY idle scout is handed a DISTINCT rotating destination (a believed POI or
		// the stalest far area), the destination + the scout's trail are marked explored so staleness
		// evolves, and a deterministic far-first tie-break spreads the sweep from the opening (before any
		// cell is explored) instead of camping (0,0). Shared code ⇒ fixes BOTH bot profiles. Zero RNG.
		void TryLaunchScoutMission()
		{
			// Scouts are singletons (no Squad is ever formed for them), so they are NOT gated by the
			// active-squad cap or the squad manager — the old early-returns on those benched recon
			// whenever 3 attack/transport squads were live. Task every ready idle scout instead.
			var scouts = idleHelicopters
				.Where(h =>
				{
					var role = h.TraitOrDefault<AIHelicopterRole>();
					return role != null && role.Info.Role == HelicopterAIRole.Scout;
				})
				.Where(IsReadyForMission)
				.OrderBy(h => h.ActorID)
				.ToList();

			if (scouts.Count == 0)
				return;

			var ownSR = FindOwnSupplyRoute();
			var homeCell = ownSR?.Location ?? player.HomeLocation;
			var minDistSq = (long)Info.ScoutMinDistanceCells * Info.ScoutMinDistanceCells;
			var spacingSq = (long)Info.ScoutTargetSpacingCells * Info.ScoutTargetSpacingCells;

			// Careful scout employment (experimental-only, default off ⇒ byte-identical). Build the per-pass
			// safety config once: a fog-legal air-danger sampler (off-map = Impassable so no route steers off the
			// playable area) plus the penetration cap and safe threshold. scoutAirDangerAt stays null — the skip
			// signal for ConsiderReconCandidate — when the lever is off OR no field exists yet.
			if (Info.CarefulScoutEmployment && dangerField != null)
			{
				scoutAirDangerAt = c => world.Map.Contains(c) ? dangerField.AirDanger(player, c) : HeliDangerNav.Impassable;
				scoutAirSafeThreshold = Info.ScoutAirDangerSafeThreshold;
			}
			else
				scoutAirDangerAt = null;

			// The penetration cap is pure geometry, so it applies even before any belief data exists (the
			// 'no deep dive into unscouted territory' bound). 0 ⇒ no cap. Only consulted when the lever is on.
			scoutMaxDistSq = Info.CarefulScoutEmployment
				? (long)Info.ScoutMaxDistanceCells * Info.ScoutMaxDistanceCells : 0;

			// Believed POIs to keep intel fresh on (fog-legal: map-fact structures + enemy SR).
			// suppressOmniscientThreat keeps the picker off the omniscient InfluenceMap threat grid —
			// we only read the POI LOCATIONS, so this is fog-legal for @experimental and inert for @stable.
			poiScratchCells.Clear();
			if (poiMap != null)
				foreach (var p in poiMap.GetOffensiveTargets(player, suppressOmniscientThreat: true))
					poiScratchCells.Add(p.Location);

			assignedScratch.Clear();

			foreach (var scout in scouts)
			{
				var target = PickReconTarget(scout, homeCell, minDistSq, spacingSq);
				if (!target.HasValue)
					continue;

				bot.QueueOrder(new Order("Move", scout, Target.FromCell(world, target.Value), false));
				idleHelicopters.Remove(scout);
				assignedScratch.Add(target.Value);

				// Mark the destination explored so its staleness resets and the NEXT scout / next pass
				// picks a genuinely different area (mirrors ScoutBotModule) — the missing call that let
				// the old code re-issue the SAME corner every mission.
				threatMap?.MarkExplored(target.Value);

				AIUtils.BotDebug("AI ({0}): heli recon {1} {2} -> {3}",
					player.ClientIndex, scout.Info.Name, scout.Location, target.Value);
			}
		}

		// Pick the highest-desirability recon destination for one scout: a believed POI or the stalest
		// far area, excluding cells near home or within spacing of a target already handed out this pass.
		// Deterministic (fixed iteration order + strict-greater first-wins), zero RNG.
		CPos? PickReconTarget(Actor scout, CPos homeCell, long minDistSq, long spacingSq)
		{
			CPos? best = null;
			var bestScore = int.MinValue;

			// Source 1: believed POIs — purposeful recon of known enemy/neutral anchors.
			foreach (var c in poiScratchCells)
				ConsiderReconCandidate(c, scout, homeCell, minDistSq, spacingSq, true, ref best, ref bestScore);

			// Source 2: stale-area sweep over the coarse exploration grid.
			if (threatMap != null)
				for (var gx = 0; gx < threatMap.GridWidth; gx++)
					for (var gy = 0; gy < threatMap.GridHeight; gy++)
						ConsiderReconCandidate(threatMap.GridToMapCell(gx, gy), scout, homeCell, minDistSq, spacingSq, false, ref best, ref bestScore);

			return best;
		}

		void ConsiderReconCandidate(CPos cell, Actor scout, CPos homeCell, long minDistSq, long spacingSq,
			bool isPoi, ref CPos? best, ref int bestScore)
		{
			if (!world.Map.Contains(cell))
				return;

			// Keep recon out over the map, not hovering above home.
			if ((cell - homeCell).LengthSquared < minDistSq)
				return;

			// Fan multiple scouts out: skip a cell too close to one already handed out this pass.
			foreach (var a in assignedScratch)
				if ((cell - a).LengthSquared < spacingSq)
					return;

			// Careful scout employment (experimental-only): reject a recon leg that is too deep (penetration cap)
			// or that would send / fly the fragile littlebird through a believed anti-air envelope. Fog-legal —
			// air-danger reads 0 for unscouted cells, so this only avoids KNOWN AA and leans on the geometry cap
			// before contact. Skipped entirely (byte-identical) when the lever is off (cap 0 AND sampler null).
			if (scoutMaxDistSq > 0 || scoutAirDangerAt != null)
			{
				var distFromHomeSq = (long)(cell - homeCell).LengthSquared;
				var destAir = scoutAirDangerAt != null ? scoutAirDangerAt(cell) : 0;
				var pathMax = scoutAirDangerAt != null
					? HeliDangerNav.PathMaxAirDanger(scout.Location, cell, scoutAirDangerAt) : 0;

				if (!ReconSafetyMath.Acceptable(destAir, pathMax, scoutAirSafeThreshold, distFromHomeSq, scoutMaxDistSq))
					return;
			}

			var age = threatMap?.GetExplorationAge(cell) ?? ScoutReconMath.MaxTrackedAge;
			var score = ScoutReconMath.Score(age, IsEdgeCell(cell), isPoi, (cell - scout.Location).Length);

			if (score > bestScore)
			{
				bestScore = score;
				best = cell;
			}
		}

		// Edge cells are likely enemy approach routes — worth extra recon weight (mirrors ScoutBotModule).
		bool IsEdgeCell(CPos cell)
		{
			var b = world.Map.Bounds;
			return cell.X < b.Left + 5 || cell.X > b.Right - 5
				|| cell.Y < b.Top + 5 || cell.Y > b.Bottom - 5;
		}

		// Refresh the shared exploration grid from where the scout helis actually are, so their trails
		// register as freshly explored and successive recon legs rotate to new stale areas (mirrors
		// ScoutBotModule.cs:110). Deterministic: an int-grid write of the synced WorldTick, zero RNG.
		void MarkScoutExploration()
		{
			if (threatMap == null)
				return;

			foreach (var h in managedHelicopters)
			{
				if (h == null || h.IsDead || !h.IsInWorld)
					continue;

				var role = h.TraitOrDefault<AIHelicopterRole>();
				if (role != null && role.Info.Role == HelicopterAIRole.Scout)
					threatMap.MarkExplored(h.Location);
			}
		}

		// Transport missions in flight right now: staged-to-load plus dispatched-awaiting-unload. This is the
		// occupancy the reserved slice is measured against (a plain count ⇒ order-independent).
		int ActiveTransportMissions => transportTasks.Count + transportsAwaitingUnload.Count;

		void TryLaunchTransportMission()
		{
			// STARVATION FIX (experimental, TransportMissionSlots > 0). The frozen gate below shares
			// MaxActiveSquads with the attack loop, yet a transport mission never increments activeSquads —
			// so three live attack squads block lift forever and the transports we bought never fly. The
			// reserved slice bounds transport missions against their OWN occupancy instead. 0 ⇒ frozen path.
			if (Info.TransportMissionSlots > 0)
			{
				if (!TransportEmploymentMath.MissionSlotAvailable(ActiveTransportMissions, Info.TransportMissionSlots))
					return;
			}
			else if (activeSquads.Count >= Info.MaxActiveSquads)
				return;

			if (squadManagerRef == null)
				return;

			// Get idle transport helicopter
			var transport = idleHelicopters
				.Where(h =>
				{
					var role = h.TraitOrDefault<AIHelicopterRole>();
					return role != null && role.Info.Role == HelicopterAIRole.Transport;
				})
				.Where(h => IsReadyForMission(h))
				.FirstOrDefault();

			if (transport == null)
				return;

			// Check if transport has cargo capability
			var cargo = transport.TraitOrDefault<Cargo>();
			if (cargo == null)
				return;

			// Find idle infantry near base to load. Defense-in-depth: also skip any soldier already committed in
			// the shared ledger (another transport's boarding walk, a capture/garrison/offense task) so heli
			// transport and MountedTransport are mutually poach-safe by construction — mirrors
			// MountedTransportBotModule.BuildFreePool, which likewise selects on more than IsIdle. goalGuard is
			// null unless CommitTransportPassengers is on, so the extra clause is inert (byte-identical) when off.
			var infantry = world.ActorsHavingTrait<Mobile>()
				.Where(a => a.Owner == player
					&& !a.IsDead && a.IsInWorld
					&& a.IsIdle
					&& a.Info.HasTraitInfo<WithInfantryBodyInfo>()
					&& cargo.Info.Types.Overlaps(a.GetAllTargetTypes())
					&& (goalGuard == null || !goalGuard.Ledger.IsCommitted(a, world.WorldTick)))
				.Take(cargo.Info.MaxWeight)
				.ToList();

			if (infantry.Count < Info.TransportMinInfantry)
				return;

			// Find a front-line drop zone. Risk-weighted picker (experimental) reshapes the candidate ranking by a
			// fog-legal risk/reachability score so drops stop landing deep behind the enemy SR; default off ⇒ the
			// frozen single-weakest-enemy-cell path below runs byte-identically.
			CPos? dropZone = null;

			if (Info.RiskWeightedDropSite)
				dropZone = PickRiskWeightedDropZone();
			else if (threatMap != null)
			{
				// Find an enemy-adjacent cell that isn't too dangerous
				var weakCell = threatMap.FindWeakestEnemyCell(player);
				if (weakCell != CPos.Zero)
				{
					var threat = threatMap.GetThreat(weakCell, player);
					if (threat < 50)
						dropZone = weakCell;
				}
			}

			if (!dropZone.HasValue)
				return;

			// EMPTY-DELIVERY FIX (bug-class, ungated — applies to every profile that runs this module). The old
			// code ordered the infantry to EnterTransport and, in the SAME pass, queued the transport's whole
			// delivery chain (Move drop -> Unload -> Move home). Those queued orders sit on the transport's OWN
			// (empty) activity queue, so the Move to the drop began IMMEDIATELY — the heli flew off before any
			// soldier had walked over and boarded, delivered nothing, and returned. Now we only ORDER the load
			// here and record a Loading task; AdvanceTransportTasks confirms cargo is actually aboard before it
			// dispatches the delivery leg, and ABORTS (returns the heli to the pool) if nobody boarded in time —
			// a transport never flies a delivery empty again.
			foreach (var inf in infantry)
				bot.QueueOrder(new Order("EnterTransport", inf, Target.FromActor(transport), false));

			var task = new TransportLoadTask
			{
				Transport = transport,
				DropZone = dropZone.Value,
				StateChangedAtTick = world.WorldTick,
				ReservedPassengers = new HashSet<Actor>(infantry),
			};
			transportTasks[transport] = task;

			// Protect the boarding walk: commit each ordered passenger to the shared ledger so no other module
			// poaches a soldier while it heads for the heli (the 'poached en route' half of the bug). Gated /
			// inert when CommitTransportPassengers is off ⇒ byte-identical. Released on dispatch / abort / death.
			CommitTaskPassengers(task);

			idleHelicopters.Remove(transport);

			AIUtils.BotDebug("AI ({0}): transport heli {1} loading {2} pax for drop at {3}",
				player.ClientIndex, transport.Info.Name, infantry.Count, dropZone.Value);
		}

		// Risk/reachability-weighted transport drop-site selection (experimental, RiskWeightedDropSite). Ranks a
		// candidate set — the omniscient weakest-enemy cell PLUS the top believed offensive POIs (reachable flank
		// targets) — by a FOG-LEGAL score (TransportDropSiteMath.ScoreDrop) that penalises believed-enemy control
		// depth, believed danger, and distance from our own SR, and returns the best. A weight, not a filter: even
		// a deep-behind-enemy cell can win if nothing better exists. Belief-side reads only (ControlField /
		// DangerFieldLayer) + own-SR distance — never a ground-truth enemy position. Deterministic: fixed candidate
		// order (weak cell first, then POIs in PoiMap's score order), first-wins on ties, zero RNG.
		CPos? PickRiskWeightedDropZone()
		{
			var ownSR = FindOwnSupplyRoute();
			var srCell = ownSR?.Location;

			CPos? best = null;
			var bestScore = int.MinValue;

			void Consider(CPos cell)
			{
				if (!world.Map.Contains(cell))
					return;

				// Belief-side territory: negative ControlField score = believed enemy; deep behind the enemy SR
				// anchor floors to ~-800. 0 when no field (control term simply drops out — reach/danger still rank).
				var believedControl = 0;
				if (controlField != null && controlField.HasField(player))
				{
					var (gx, gy) = controlField.MapCellToGridCell(cell);
					believedControl = controlField.ScoreAt(player, gx, gy);
				}

				var groundDanger = dangerField != null ? dangerField.GroundDanger(player, cell) : 0;
				var airDanger = dangerField != null ? dangerField.AirDanger(player, cell) : 0;

				// Reachability: distance from our OWN SR (a public/own fact, fog-legal). 0 when the SR is gone.
				var reachCells = srCell.HasValue ? TransportDropSiteMath.CellDistance(srCell.Value, cell) : 0;

				var score = TransportDropSiteMath.ScoreDrop(believedControl, groundDanger, airDanger, reachCells,
					Info.DropEnemyControlWeight, Info.DropDangerWeight, Info.DropReachWeight);

				if (score > bestScore)
				{
					bestScore = score;
					best = cell;
				}
			}

			// Candidate 1: the frozen weakest-enemy cell (kept, so the reshaping never strictly loses the old
			// option — it can still win when it is genuinely the least-risky reachable drop).
			if (threatMap != null)
			{
				var weakCell = threatMap.FindWeakestEnemyCell(player);
				if (weakCell != CPos.Zero && threatMap.GetThreat(weakCell, player) < 50)
					Consider(weakCell);
			}

			// Candidates 2..: the top believed offensive POIs — the reachable flank/side alternatives.
			if (poiMap != null)
			{
				var pois = poiMap.GetOffensiveTargets(player);
				var take = Math.Min(Info.DropSiteCandidatePois, pois.Count);
				for (var i = 0; i < take; i++)
					Consider(pois[i].Location);
			}

			return best;
		}

		// Advance every transport heli that is mid-load: dispatch the delivery only once cargo has actually
		// embarked (verified via Cargo.PassengerCount), deliver a partial load on timeout if at least one boarded,
		// or ABORT an empty load (nobody boarded — killed / poached / never reached the heli) by returning the heli
		// to the idle pool. Deterministic: ActorID-ordered iteration, zero RNG. Inert (no tasks) for attack/scout-
		// only profiles ⇒ byte-identical there; the staged loading itself is the ungated correctness fix.
		void AdvanceTransportTasks(IBot bot)
		{
			if (transportTasks.Count == 0)
				return;

			foreach (var h in transportTasks.Keys.OrderBy(a => a.ActorID).ToList())
			{
				var task = transportTasks[h];

				// Dead / gone / disowned transport mid-load — release the boarding claims and forget it.
				if (h == null || h.IsDead || !h.IsInWorld || h.Owner != player)
				{
					ReleaseTaskPassengers(task);
					transportTasks.Remove(h);
					continue;
				}

				var cargo = h.TraitOrDefault<Cargo>();
				if (cargo == null)
				{
					ReleaseTaskPassengers(task);
					transportTasks.Remove(h);
					continue;
				}

				var aboard = cargo.PassengerCount;
				var ticksLoading = world.WorldTick - task.StateChangedAtTick;

				switch (TransportLoadMath.Decide(aboard, Info.TransportMinInfantry, ticksLoading, Info.TransportLoadTimeoutTicks))
				{
					case TransportLoadDecision.Dispatch:
						DispatchTransportDelivery(bot, task);
						break;

					case TransportLoadDecision.Abort:
						// Nobody boarded before the timeout — the load evaporated. Do NOT fly the empty delivery;
						// release the ledger claims and return the heli to the idle pool for a later attempt.
						AIUtils.BotDebug("AI ({0}): transport heli {1} load aborted empty after {2} ticks — returning to pool",
							player.ClientIndex, h.Info.Name, ticksLoading);
						ReleaseTaskPassengers(task);
						transportTasks.Remove(h);
						if (!idleHelicopters.Contains(h))
							idleHelicopters.Add(h);
						break;

					// Wait: still boarding, still within the timeout — leave the task alone.
				}
			}
		}

		// Dispatch a confirmed-loaded transport on its delivery: fly to the drop, unload, then withdraw to our SR
		// (the WW3MOD retreat-on-unload). Mirrors the pre-fix order chain, but now issued ONLY after cargo is
		// verified aboard. Hands the heli to transportsAwaitingUnload so EnsureTransportsUnload can re-dump the
		// cargo in the rare unlandable-drop case (the queued Move alone would otherwise fly it home still loaded).
		void DispatchTransportDelivery(IBot bot, TransportLoadTask task)
		{
			var transport = task.Transport;

			bot.QueueOrder(new Order("Move", transport, Target.FromCell(world, task.DropZone), false));
			bot.QueueOrder(new Order("Unload", transport, queued: true));

			var ownSR = FindOwnSupplyRoute();
			if (ownSR != null)
				bot.QueueOrder(new Order("Move", transport, Target.FromCell(world, ownSR.Location), queued: true));

			transportsAwaitingUnload.Add(transport);

			// Passengers are aboard now (removed from the world), so they can no longer be poached — drop their
			// ledger claim so it never outlives the boarding window. Idempotent / inert when commit was off.
			ReleaseTaskPassengers(task);
			transportTasks.Remove(transport);

			AIUtils.BotDebug("AI ({0}): transport heli {1} delivering {2} pax to {3}",
				player.ClientIndex, transport.Info.Name, transport.Trait<Cargo>().PassengerCount, task.DropZone);
		}

		// Phase-2 commit-on-order (mirrors MountedTransportBotModule). Objective key namespaces the CARRIER so it
		// is disjoint from every other executor's keys and from a second heli's. Inert (goalGuard null) when the
		// CommitTransportPassengers lever is off ⇒ byte-identical frozen path.
		static string TransportObjectiveKey(Actor transport) => "transport:" + transport.ActorID;

		void CommitTaskPassengers(TransportLoadTask task)
		{
			if (!CommitOnOrderMath.ShouldCommit(Info.CommitTransportPassengers, goalGuard != null && !goalGuard.IsTraitDisabled))
				return;

			// TTL must outlast the whole boarding window: the load task lives until TransportLoadTimeoutTicks, so
			// a claim of only DefaultCommitmentTicks (300) would lapse mid-board and a still-walking soldier could
			// be poached (MountedTransport.BuildFreePool does not require IsIdle). Cover the longer of the two so
			// the claim holds for as long as the pax might be walking. Released early on dispatch/abort/death.
			var ttl = Math.Max(goalGuard.DefaultCommitmentTicks, Info.TransportLoadTimeoutTicks);
			var key = TransportObjectiveKey(task.Transport);
			foreach (var pax in task.ReservedPassengers)
				goalGuard.Ledger.Commit(pax, key, world.WorldTick, ttl);
		}

		void ReleaseTaskPassengers(TransportLoadTask task)
		{
			if (goalGuard == null || goalGuard.IsTraitDisabled)
				return;

			foreach (var pax in task.ReservedPassengers)
				goalGuard.Ledger.Release(pax);
		}

		// Safety net for the pre-queued transport retreat: confirm each dispatched transport heli actually
		// unloaded. Common path — Unload empties the cargo and the queued Move flies it home — is a no-op here
		// (empty ⇒ dropped from tracking). Rare path — the drop cell was unlandable so UnloadCargo finished
		// without unloading and the heli flew home LOADED — is caught here: re-issue Unload wherever it now
		// sits (typically the open SR area, so it dumps safely) rather than leaving a full transport idle and
		// loaded. Deterministic: ActorID-ordered, zero RNG. Inert (byte-identical) until a transport is
		// actually dispatched, so attack/scout-only profiles are unaffected.
		void EnsureTransportsUnload(IBot bot)
		{
			if (transportsAwaitingUnload.Count == 0)
				return;

			foreach (var h in transportsAwaitingUnload.OrderBy(a => a.ActorID).ToList())
			{
				// Owner check mirrors AdvanceTransportTasks: a CAPTURED transport is no longer ours to order,
				// and leaving it tracked would pin a reserved mission slot (ActiveTransportMissions) forever.
				if (h == null || h.IsDead || !h.IsInWorld || h.Owner != player)
				{
					transportsAwaitingUnload.Remove(h);
					continue;
				}

				var cargo = h.TraitOrDefault<Cargo>();
				if (cargo == null || cargo.IsEmpty())
				{
					// Delivered (and already retreating/home via the queued Move) — done tracking.
					transportsAwaitingUnload.Remove(h);
					continue;
				}

				// Still loaded: only act once it is idle (the delivery/return chain has run to its end and
				// left it loaded), so we never interrupt an in-progress unload or flight. Re-issue Unload to
				// dump the cargo where it sits; keep tracking until the cargo actually empties.
				if (h.IsIdle)
					bot.QueueOrder(new Order("Unload", h, false));
			}
		}

		bool IsReadyForMission(Actor h)
		{
			if (h.IsDead || !h.IsInWorld)
				return false;

			// A heli flying its evac must never be recruited/staged — that would cancel the RotateToEdge.
			// Empty set when EvacuateWhenIdle is off ⇒ byte-identical for every other profile.
			if (evacuating.Contains(h))
				return false;

			// Check HP
			var health = h.TraitOrDefault<IHealth>();
			if (health != null)
			{
				var role = h.TraitOrDefault<AIHelicopterRole>();
				var reEngagePercent = role != null ? role.Info.ReEngageHealthPercent : 80;
				if (health.HP * 100 / health.MaxHP < reEngagePercent)
					return false;
			}

			// Check ammo — unless the rearm-ready gate is bypassed. WW3MOD attack helis rearm only
			// at an hpad (none built), so requiring full ammo permanently benches any heli that
			// dipped below full and no squad ever forms. SkipRearmReadyCheck lets them launch anyway.
			if (!Info.SkipRearmReadyCheck)
			{
				var ammoPools = h.TraitsImplementing<AmmoPool>().ToArray();
				var rearmable = h.TraitOrDefault<Rearmable>();
				if (ammoPools.Length > 0 && rearmable != null)
				{
					foreach (var ap in ammoPools)
					{
						if (!ap.HasFullAmmo)
							return false;
					}
				}
			}

			// Check if currently rearming
			if (!h.IsIdle)
			{
				var activity = h.CurrentActivity;
				if (activity != null && activity.GetType().Name == "Resupply")
					return false;
			}

			return true;
		}

		// Mission-employment evac (experimental, EvacuateWhenIdle). An attack heli that is genuinely idle
		// (empty activity queue — so never one mid-mission, mid-withdraw, or already flying its evac) is
		// evacuated to reserves when it has no further use: either it is spent with no rearm host, or it has
		// loitered near home past the patience window with no believed worthwhile target. Evac reclaims the
		// heli's salvage value (RotateToEdge → GetSellValue: full Cost with ammo, less spent-ammo value) and
		// stops its upkeep drain — the "helicopters are perfect for short hit-and-run missions" model, and the
		// fix for the SR/staging corner-idle park. Deterministic: belief-store + integer geometry, ActorID-
		// ordered iteration, ZERO random draws. Fully skipped (byte-identical) when the flag is off.
		void EvaluateIdleHelicopters()
		{
			// Two independent policies share this walk: EvacuateWhenIdle retires spent/target-less ATTACK helis,
			// EvacuateIdleTransports retires unemployable TRANSPORTS (use-or-evac). Either alone is enough to
			// enter; neither ⇒ frozen, byte-identical.
			if ((!Info.EvacuateWhenIdle && !Info.EvacuateIdleTransports) || managedHelicopters.Count == 0)
				return;

			// Latch first contact: once the belief store has EVER held an enemy contact, the target-less
			// evac branch is allowed. Before first contact the bot cannot know where the enemy is, so
			// anticipatory helis are HELD (and staged forward) rather than evac'd and re-bought — this
			// matters because EvacuateHomeRadiusCells (12) > ForwardStagingMaxDistanceCells (8), so a
			// forward-staged, target-less heli is otherwise inside the evac-eligible home radius.
			if (!enemyEverObserved && beliefStore != null && beliefStore.Contacts(player).Count > 0)
				enemyEverObserved = true;

			var ownSR = FindOwnSupplyRoute();
			var homeRadiusSq = (long)Info.EvacuateHomeRadiusCells * Info.EvacuateHomeRadiusCells;
			var missionRangeSq = (long)Info.MissionTargetRangeCells * Info.MissionTargetRangeCells;

			// managedHelicopters is a HashSet (iteration order not guaranteed); sort by synced ActorID
			// for lockstep-deterministic order. OrderBy buffers, and ToList snapshots, so Evacuate can
			// mutate the set safely inside the loop.
			foreach (var h in managedHelicopters.OrderBy(a => a.ActorID).ToList())
			{
				if (h == null || h.IsDead || !h.IsInWorld)
					continue;

				var role = h.TraitOrDefault<AIHelicopterRole>();
				if (role == null)
					continue;
				var r = role.Info.Role;

				// Transports run the use-or-evac policy on their own flag/window; attack helis run the
				// hit-and-run employment policy. Scouts are claimed by neither and stay untouched. Before
				// this branch existed the filter admitted ONLY the two attack roles, so a transport could
				// never be retired at all — the confirmed half of the idle-transport bug.
				if (r == HelicopterAIRole.Transport)
				{
					EvaluateIdleTransport(h);
					continue;
				}

				if (!Info.EvacuateWhenIdle)
					continue;

				if (r != HelicopterAIRole.AttackHeavy && r != HelicopterAIRole.AttackLight)
					continue;

				// Only act on a heli with an empty activity queue. A heli executing a mission
				// (attack-move / attack / return) or already flying its evac is never idle, so the
				// active FSM arc is never disturbed.
				if (!h.IsIdle)
				{
					idleTicks[h] = 0;
					continue;
				}

				var ticks = (idleTicks.TryGetValue(h, out var t) ? t : 0) + 1;
				idleTicks[h] = ticks;

				var hasUsableAmmo = HasUsableAmmo(h);
				var canRearm = CanRearm(h);
				var nearHome = ownSR == null || (h.Location - ownSR.Location).LengthSquared <= homeRadiusSq;
				var hasTarget = HasWorthwhileBelievedTarget(h, missionRangeSq);

				if (HeliEmploymentMath.Decide(hasUsableAmmo, canRearm, hasTarget, enemyEverObserved, nearHome, ticks, Info.EvacuateIdleTicks, Info.EvacuateForwardIdle)
					== HeliDisposition.Evacuate)
					Evacuate(h);
			}
		}

		// USE-OR-EVAC for a transport heli (experimental, EvacuateIdleTransports). A transport that cannot be
		// employed — no full minimum load waiting, no reserved mission slot to fly it in, or not mission-READY
		// — is idle capital parked in a warzone. Past the patience window it evacuates to reserves, banking the
		// salvage refund (RotateToEdge → GetSellValue) and ending its upkeep drain. Terminal: no hold-and-recheck.
		// Employment always outranks retirement (TransportEmploymentMath.Decide), so a transport that could fly
		// a lift this instant is held for TryLaunchTransportMission rather than refunded.
		void EvaluateIdleTransport(Actor h)
		{
			if (!Info.EvacuateIdleTransports)
				return;

			// Mid-load or mid-delivery is employment, not idleness — reset the counter and leave the mission
			// chain alone. (CleanUpHelicopters keeps these two sets pruned at the same choke point.)
			if (transportTasks.ContainsKey(h) || transportsAwaitingUnload.Contains(h))
			{
				idleTicks[h] = 0;
				return;
			}

			if (!IsUnoccupied(h))
			{
				idleTicks[h] = 0;
				return;
			}

			var ticks = (idleTicks.TryGetValue(h, out var t) ? t : 0) + 1;
			idleTicks[h] = ticks;

			var cargo = h.TraitOrDefault<Cargo>();
			var hasDemand = cargo != null
				&& TransportEmploymentMath.HasLiftDemand(CountLiftCandidates(cargo), Info.TransportMinInfantry);
			var slotFree = TransportEmploymentMath.MissionSlotAvailable(ActiveTransportMissions, Info.TransportMissionSlots);

			// INVARIANT: Employ must imply ACTUALLY-LAUNCHABLE. This proxy has to apply every gate the executor
			// (TryLaunchTransportMission) applies, or it can report Employ for a transport the launcher will
			// never pick — and because Employ shadows Evacuate, that pins the airframe at the SR forever, which
			// is the very symptom this behaviour exists to fix. The gate that bites in practice is
			// IsReadyForMission's health check: TRAN/HALO ship ReEngageHealthPercent 90 and there is no AI
			// repair host, so a chip-damaged transport is permanently unpickable. Folding it into the demand
			// argument (rather than into TransportEmploymentMath, which stays pure and world-free) makes an
			// unlaunchable transport fall through to the evac window — it retires, and the demand gate then
			// authorises a healthy replacement.
			// ACCEPTED RESIDUAL: the launcher's dropZone.HasValue precondition is NOT folded in. It is
			// transient in a live game (the drop-site picker recovers as the threat map fills), so treating it
			// as unlaunchable would evac transports over a momentary gap. NOTE this residual is only safe
			// because RiskWeightedDropSite makes the picker a weight, not a filter — see the coupling note on
			// EvacuateIdleTransports. Also not folded: the launcher's squadManagerRef == null bail-out —
			// unreachable in every shipped profile (each faction x profile configures a SquadManagerBotModule
			// twin), and a missing squad manager would kill attack squads long before presenting here.
			var launchable = IsReadyForMission(h);

			if (TransportEmploymentMath.Decide(ticks, Info.TransportIdleEvacuateTicks, hasDemand && launchable, slotFree)
				== TransportDisposition.Evacuate)
				Evacuate(h);
		}

		// Actor.IsIdle (CurrentActivity == null) is the WRONG idleness test for an airframe and is essentially
		// never true for one. With the default IdleBehavior (None) and the helicopter above LandAltitude,
		// Aircraft.OnBecomingIdle queues FlyIdle (Aircraft.cs:936), and FlyIdle.Tick never returns true while
		// nothing is queued behind it (FlyIdle.cs:39-41: remainingTicks -1 and NextActivity null). A transport
		// hovering over its Supply Route therefore carries that activity forever, so a plain !IsIdle test reset
		// idleTicks every tick and TransportIdleEvacuateTicks could never be reached — the transport parked at
		// the SR permanently. Hovering on FlyIdle with nothing queued behind it IS doing nothing.
		static bool IsUnoccupied(Actor h)
		{
			if (h.IsIdle)
				return true;

			var current = h.CurrentActivity;
			return current is FlyIdle && current.NextActivity == null;
		}

		// Infantry available and willing to ride this airframe — the LIFT DEMAND signal. Mirrors the candidate
		// filter TryLaunchTransportMission loads from, so the demand test and the mission it predicts agree.
		// Returns a capped COUNT, so world iteration order cannot affect it (determinism invariant).
		int CountLiftCandidates(Cargo cargo)
		{
			var count = 0;
			foreach (var a in world.ActorsHavingTrait<Mobile>())
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld || !a.IsIdle)
					continue;

				if (!a.Info.HasTraitInfo<WithInfantryBodyInfo>())
					continue;

				if (!cargo.Info.Types.Overlaps(a.GetAllTargetTypes()))
					continue;

				if (goalGuard != null && goalGuard.Ledger.IsCommitted(a, world.WorldTick))
					continue;

				if (++count >= cargo.Info.MaxWeight)
					break;
			}

			return count;
		}

		// True if the heli still has a usable round in any pool. A heli carrying no AmmoPool at all is
		// not ammo-limited, so it never counts as "spent".
		static bool HasUsableAmmo(Actor h)
		{
			var any = false;
			foreach (var ap in h.TraitsImplementing<AmmoPool>())
			{
				any = true;
				if (ap.HasAmmo)
					return true;
			}

			return !any;
		}

		// True if any friendly rearm host this heli could dock at exists in the world. WW3MOD builds no
		// hpad, so this is normally false and a spent heli evacs; written generally so it self-heals if a
		// rearm structure is ever added. Boolean Any ⇒ iteration-order-independent (deterministic).
		bool CanRearm(Actor h)
		{
			var rearmable = h.TraitOrDefault<Rearmable>();
			if (rearmable == null || rearmable.Info.RearmActors.Count == 0)
				return false;

			var hosts = rearmable.Info.RearmActors;
			return world.Actors.Any(a => a.Owner == player && !a.IsDead && a.IsInWorld && hosts.Contains(a.Info.Name));
		}

		// Fog-legal worthwhile-target test: is any BELIEVED enemy contact within mission range of the heli?
		// Reads the belief store (Stage A), never ground truth. No belief field ⇒ we cannot assert a target
		// ⇒ evac-eligible — we do not cheat by peeking through fog.
		bool HasWorthwhileBelievedTarget(Actor h, long missionRangeSq)
		{
			if (beliefStore == null)
				return false;

			targetScratch.Clear();
			foreach (var c in beliefStore.Contacts(player))
				targetScratch.Add(c.Cell);

			return HeliEmploymentMath.AnyTargetWithin(h.Location, targetScratch, missionRangeSq);
		}

		// Evacuate a heli to reserves and drop it from ALL bot management so nothing re-tasks it and cancels
		// the evac (the recruit-cancels-evac hazard PoiOffensive guards against for out-of-ammo ground units).
		void Evacuate(Actor h)
		{
			h.QueueActivity(false, new RotateToEdge(h, true, h.GetSellValue()));

			// Mark evacuating BEFORE dropping from management: FindNewHelicopters / IsReadyForMission both
			// exclude this set, so the heli can never be re-adopted or recruited while flying its evac (the
			// order would cancel the RotateToEdge). Cleared once it leaves the world (CleanUpHelicopters).
			evacuating.Add(h);

			idleHelicopters.Remove(h);
			foreach (var squad in activeSquads)
				squad.Units.Remove(h);
			managedHelicopters.Remove(h);
			stagedTo.Remove(h);
			idleTicks.Remove(h);
			if (blackboard != null)
				blackboard.ReleaseUnit(h);

			AIUtils.BotDebug("AI ({0}): heli evac-to-reserves {1} at {2}",
				player.ClientIndex, h.Info.Name, h.Location);
		}

		protected override void TraitDisabled(Actor self)
		{
			// Release all helicopters
			if (blackboard != null)
				foreach (var h in managedHelicopters)
					if (h != null && !h.IsDead)
						blackboard.ReleaseUnit(h);

			// Release any live boarding claims before dropping the tasks, so a disabled module never leaves a
			// soldier ledger-locked out of the free pool.
			foreach (var task in transportTasks.Values)
				ReleaseTaskPassengers(task);

			managedHelicopters.Clear();
			idleHelicopters.Clear();
			activeSquads.Clear();
			stagedTo.Clear();
			idleTicks.Clear();
			evacuating.Clear();
			transportsAwaitingUnload.Clear();
			transportTasks.Clear();
			enemyEverObserved = false;
		}
	}

	// What to do with a transport heli that is mid-load this eval.
	// Pure, world-free risk/reachability drop-site scoring for the heli transport (experimental
	// RiskWeightedDropSite). Split out for NUnit like TransportLoadMath / HeliEmploymentMath — deterministic,
	// integer-only, zero RNG. Higher score = better drop site. Every input is FOG-LEGAL: believed control
	// (ControlField), believed danger (DangerFieldLayer), and distance from our OWN Supply Route — never a
	// ground-truth enemy position. The score is the negative of a weighted penalty sum, so a cell deep in
	// believed-enemy territory (behind the enemy SR anchor), far from our SR, or inside a believed danger
	// envelope is demoted below a reachable flank POI. A weight, not a filter — nothing is hard-banned.
	public static class TransportDropSiteMath
	{
		/// <summary>Chebyshev (chessboard) cell distance — WW3MOD's map grid is Rectangular (conventions.md),
		/// so max(|dx|,|dy|) is the true "cells away" reachability metric.</summary>
		public static int CellDistance(CPos a, CPos b)
		{
			var dx = a.X - b.X; if (dx < 0) dx = -dx;
			var dy = a.Y - b.Y; if (dy < 0) dy = -dy;
			return dx > dy ? dx : dy;
		}

		/// <summary>Drop-site desirability (higher = better). <paramref name="believedControl"/> is the
		/// ControlField score at the cell (+ ours, − believed enemy, 0 no field); only the ENEMY depth (its
		/// negative magnitude) is penalised, so believed-ours/contested add nothing. <paramref name="groundDanger"/>
		/// + <paramref name="airDanger"/> are the DangerFieldLayer readings. <paramref name="reachCells"/> is the
		/// distance (cells) from our own SR. The control + danger weights are x100 (percent) scalers; the reach
		/// weight is per cell. All penalties subtract, so the return is ≤ 0 and the LEAST-penalised candidate wins.</summary>
		public static int ScoreDrop(int believedControl, int groundDanger, int airDanger, int reachCells,
			int enemyControlWeight, int dangerWeight, int reachWeight)
		{
			var enemyDepth = believedControl < 0 ? -believedControl : 0;
			var danger = (groundDanger < 0 ? 0 : groundDanger) + (airDanger < 0 ? 0 : airDanger);
			var reach = reachCells < 0 ? 0 : reachCells;

			var score = 0;
			score -= enemyDepth * enemyControlWeight / 100;
			score -= danger * dangerWeight / 100;
			score -= reach * reachWeight;
			return score;
		}
	}

	public enum TransportLoadDecision { Wait, Dispatch, Abort }

	// Pure, world-free transport-loading decision math — the empty-delivery gate. Split out for NUnit like the
	// other heli math helpers (CommitOnOrderMath precedent) — deterministic, integer-only, zero RNG. Mirrors the
	// Loading-state logic of MountedTransportBotModule.AdvanceTask.
	public static class TransportLoadMath
	{
		// Decide a loading transport's disposition.
		//   passengersAboard — Cargo.PassengerCount right now.
		//   minPassengers    — the full-load threshold (TransportMinInfantry).
		//   ticksLoading     — ticks since the load order was issued.
		//   loadTimeoutTicks — patience before we stop waiting for a full load.
		// Dispatch once the full load is aboard. On timeout, deliver a partial load if ANY boarded, else ABORT
		// (nobody embarked — never fly the delivery empty). Otherwise keep waiting.
		public static TransportLoadDecision Decide(int passengersAboard, int minPassengers, int ticksLoading, int loadTimeoutTicks)
		{
			if (passengersAboard >= minPassengers)
				return TransportLoadDecision.Dispatch;

			if (ticksLoading > loadTimeoutTicks)
				return passengersAboard > 0 ? TransportLoadDecision.Dispatch : TransportLoadDecision.Abort;

			return TransportLoadDecision.Wait;
		}
	}

	// Pure, world-free careful-scout recon-safety gate for the littlebird. Split out for NUnit like the other heli
	// math helpers — deterministic, integer-only, zero RNG. Reads only what the caller samples from the fog-legal
	// air-danger field (never omniscient), so a candidate is accepted iff it is a survivable recon leg.
	public static class ReconSafetyMath
	{
		// True if a scout may be sent to a candidate recon cell.
		//   destAirDanger      — believed anti-air danger AT the destination cell (0 = no believed AA / unscouted).
		//   pathMaxAirDanger   — worst believed anti-air danger along the straight flight to it.
		//   safeThreshold      — danger at or below which a cell / path is treated as safe.
		//   distFromHomeSq     — squared map-cell distance from the scout's own home.
		//   maxReconRadiusSq   — squared penetration cap (0 = no cap: geometry gate disabled).
		// Reject a cell beyond the penetration cap (the 'don't dive into unscouted enemy backfield' bound), a
		// destination inside a believed AA envelope, or a route that would cross one. All three must pass.
		public static bool Acceptable(int destAirDanger, int pathMaxAirDanger, int safeThreshold,
			long distFromHomeSq, long maxReconRadiusSq)
		{
			if (maxReconRadiusSq > 0 && distFromHomeSq > maxReconRadiusSq)
				return false;

			if (destAirDanger > safeThreshold)
				return false;

			if (pathMaxAirDanger > safeThreshold)
				return false;

			return true;
		}
	}

	// Pure, world-free staging-vector math for HelicopterSquadBotModule forward staging.
	// Split out for NUnit like HeliDangerNav / the influence-stack math classes — deterministic,
	// zero RNG. StagePos mirrors MountedTransportBotModule.PreContactStagingCell's WPos interpolation.
	public static class HeliStagingMath
	{
		// A fraction (percent) of the way from the SR position toward the target position.
		// pct = 0 -> sr, pct = 100 -> tgt, pct = 50 -> midpoint. Integer WVec math, no rounding drift
		// beyond what the shipped MountedTransport pattern already accepts.
		public static WPos StagePos(WPos sr, WPos tgt, int pct)
		{
			return sr + (tgt - sr) * pct / 100;
		}
	}

	// Pure, world-free recon-desirability scoring for the scout-heli rotating-recon picker. Split out for
	// NUnit like HeliStagingMath / HeliEmploymentMath — deterministic, integer-only, overflow-safe, zero RNG.
	public static class ScoutReconMath
	{
		// Never-visited cells report GetExplorationAge == int.MaxValue; clamp so staleness never overflows
		// when bonuses are added and so a single ancient cell cannot swamp the POI/edge/distance shaping.
		public const int MaxTrackedAge = 6000;

		// Edge cells (map approach routes) and believed POIs get flat bonuses; the distance term is a
		// deterministic far-first tie-break that spreads the sweep before any cell has been explored.
		public const int EdgeBonus = 3000;
		public const int PoiBonus = 8000;

		// Desirability of sending a scout to a candidate cell.
		//   age              — GetExplorationAge (int.MaxValue = never visited); clamped to MaxTrackedAge.
		//   isEdge           — cell sits in the map's edge band (likely enemy approach route).
		//   isPoi            — cell is a believed enemy/neutral POI (keep intel fresh, stays in rotation).
		//   distToScoutCells — cells from the scout's current position (far-first tie-break).
		public static int Score(int age, bool isEdge, bool isPoi, int distToScoutCells)
		{
			var staleness = age < 0 || age > MaxTrackedAge ? MaxTrackedAge : age;
			var s = staleness;
			if (isEdge)
				s += EdgeBonus;
			if (isPoi)
				s += PoiBonus;

			if (distToScoutCells > 0)
				s += distToScoutCells;

			return s;
		}
	}

	// Pure, world-free attack-heli package-size doctrine. Split out for NUnit like the other heli math
	// helpers — deterministic, integer-only, zero RNG.
	public static class HeliPackageMath
	{
		// Whether to launch a partial (below-preferred) attack-heli package.
		//   ready         — attack helis ready to launch now.
		//   preferredSize — the randomised pairing target already computed by the caller.
		//   minSize       — smallest package allowed to launch (1 = a lone heli deploys).
		//   spendable     — Cash + Resources.
		//   incomeThresh  — spendable at/above which we can afford to wait for a pair.
		// Launch when at least minSize is ready AND we are not deliberately holding out for a pair. We hold
		// out only when income is high (can afford to mass a second) and we are still short of preferredSize.
		public static bool ShouldLaunchPartial(int ready, int preferredSize, int minSize, int spendable, int incomeThresh)
		{
			if (ready < minSize)
				return false;

			var incomeHigh = spendable >= incomeThresh;
			if (incomeHigh && ready < preferredSize)
				return false;

			return true;
		}
	}

	// What to do with an idle attack heli that is not currently executing a mission.
	public enum HeliDisposition { HoldForMission, Evacuate }

	// Pure, world-free mission-employment decision math for HelicopterSquadBotModule. Split out for NUnit
	// like HeliStagingMath / HeliDangerNav — deterministic, integer-only, zero RNG.
	public static class HeliEmploymentMath
	{
		// Decide the disposition of an idle attack heli.
		//   hasUsableAmmo        — any pool still has a round.
		//   canRearm             — a friendly rearm host exists it could refill at.
		//   hasWorthwhileTarget  — a believed enemy contact is within mission range.
		//   contactEverObserved  — the bot has believed at least one enemy contact at some point (first
		//                          contact has happened). Gates the target-less branch so anticipatory
		//                          helis are not evac'd/re-bought during the opening before any contact.
		//   nearHome             — the heli is loitering within the home radius (at the SR/staging area).
		//   idleTicks            — consecutive ticks the heli has been idle.
		//   evacuateIdleTicks    — patience window before a target-less heli is evacuated.
		//   evacuateForwardIdle  — also evac a target-less idle heli that finished a mission FORWARD (beyond
		//                          the home radius); when false the forward heli is HELD (frozen behaviour).
		public static HeliDisposition Decide(
			bool hasUsableAmmo, bool canRearm, bool hasWorthwhileTarget,
			bool contactEverObserved, bool nearHome, int idleTicks, int evacuateIdleTicks,
			bool evacuateForwardIdle = false)
		{
			// Spent and unable to refill: no combat value remains — bank the salvage and stop the upkeep
			// drain rather than parking a disarmed heli forever. Fires regardless of target/home/window/contact.
			if (!hasUsableAmmo && !canRearm)
				return HeliDisposition.Evacuate;

			// Armed (or able to rearm) but nothing believed worth striking, idle past the patience window:
			// reclaim full value + stop upkeep instead of loitering. Only once first contact has been made —
			// a believed target instead keeps the heli HELD for the squad mission loop. By default this only
			// fires near home; EvacuateForwardIdle extends it to a heli that finished a mission FORWARD so it
			// does not sit at the front indefinitely with no follow-up (it evacs toward its own friendly edge).
			if (contactEverObserved && !hasWorthwhileTarget && (nearHome || evacuateForwardIdle) && idleTicks >= evacuateIdleTicks)
				return HeliDisposition.Evacuate;

			return HeliDisposition.HoldForMission;
		}

		// True if any candidate cell is within maxRangeCellsSq (squared map-cell distance) of the heli.
		// Caller supplies the believed-contact cells; pure integer geometry, deterministic.
		public static bool AnyTargetWithin(CPos heliCell, IReadOnlyList<CPos> candidateCells, long maxRangeCellsSq)
		{
			for (var i = 0; i < candidateCells.Count; i++)
			{
				var d = candidateCells[i] - heliCell;
				if ((long)d.LengthSquared <= maxRangeCellsSq)
					return true;
			}

			return false;
		}
	}
}
