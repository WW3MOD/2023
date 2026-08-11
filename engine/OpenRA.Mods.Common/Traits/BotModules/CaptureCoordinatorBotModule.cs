#region Copyright & License Information
/*
 * WW3MOD CaptureCoordinatorBotModule — experimental AI.
 *
 * Replaces CaptureManagerBotModule for experimental bots. Three behaviours over the
 * legacy module:
 *
 *  1. Target scoring is INCOME-WEIGHTED (OILB=50, FCOM=100, BIO=150)
 *     rather than the legacy sell-value sort. MISS/HOSP (no income)
 *     score lower.
 *  2. Each capture dispatch also pulls K nearby idle friendlies and
 *     attack-moves them to the target as ESCORT. Engineer no longer
 *     walks alone.
 *  3. Defense pass: every DefenseScanInterval ticks, for each own
 *     capturable structure under threat (enemy army value > friendly
 *     army value in the neighbourhood), summon defenders.
 *
 * Coexists with the legacy CaptureManagerBotModule — experimental YAML gates the
 * legacy ones to enable-ai-legacy-only so they don't double-fire.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: coordinates capture of income structures with escort + defense.")]
	public class CaptureCoordinatorBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Actor types that can capture other actors (via `Captures`). Empty = disabled.")]
		public readonly HashSet<string> CapturingActorTypes = new();

		[Desc("Actor types that can be targeted for capturing. Empty = all eligible.")]
		public readonly HashSet<string> CapturableActorTypes = new();

		[Desc("Tick budget for the capture scan + dispatch pass.")]
		public readonly int ScanInterval = 75;

		[Desc("Tick budget for the defense scan over own captured structures.")]
		public readonly int DefenseScanInterval = 150;

		[Desc("Max number of candidate targets considered each scan.")]
		public readonly int MaximumCaptureTargetOptions = 10;

		[Desc("Whether to filter targets by fog visibility. WW3MOD bots are typically omniscient; default false.")]
		public readonly bool CheckCaptureTargetsForVisibility = false;

		[Desc("Player relationships eligible as capture targets.")]
		public readonly PlayerRelationship CapturableRelationships = PlayerRelationship.Enemy | PlayerRelationship.Neutral;

		[Desc("Per-actor-type income weights. Lookup by lowercased actor name. ",
			"Unlisted types get DefaultIncomeWeight.")]
		public readonly Dictionary<string, int> IncomeWeights = new();

		[Desc("Income weight used when a target type is not listed in IncomeWeights.")]
		public readonly int DefaultIncomeWeight = 10;

		[Desc("WAVE A (@experimental) SUPPLY-DEPOT CAPTURE TIER. A Logistics Centre is the ONLY thing a ground",
			"vehicle can rearm at (economy.md), so taking one is what keeps a dry armoured force in the fight —",
			"but it earns no cash, so it falls to DefaultIncomeWeight and ranks level with the worthless MISS/HOSP",
			"civilian structures. When ON, types in SupplyDepotActorTypes are scored at their own explicit tier",
			"(SupplyDepotIncomeWeight) placed BELOW every cash-producing building: a depot is worth taking, but",
			"never at the cost of an income POI. Weight only — membership still comes from CapturableActorTypes, and",
			"an explicit IncomeWeights entry always wins. OFF by default = byte-identical; only the @experimental",
			"block turns it on.")]
		public readonly bool CaptureSupplyDepots = false;

		[Desc("Actor types treated as rearm/resupply depots by the CaptureSupplyDepots tier. Lookup by lowercased",
			"actor name. Only read when CaptureSupplyDepots.")]
		public readonly HashSet<string> SupplyDepotActorTypes = new();

		[Desc("Income weight for SupplyDepotActorTypes when CaptureSupplyDepots is on. Set this BELOW the lowest",
			"CASH-PRODUCING IncomeWeights entry (oilb: 50 in current tuning) so a depot never outbids a real income",
			"building, and above DefaultIncomeWeight so it outranks the no-income civilian structures that share that",
			"default. It deliberately sits ABOVE the non-cash IncomeWeights entries (miss: 10, hosp: 20) — those are",
			"listed to hold them DOWN, not because they earn anything.")]
		public readonly int SupplyDepotIncomeWeight = 25;

		[Desc("Number of cells over which target-distance score halves (rough decay scale).")]
		public readonly int DistanceHalfLifeCells = 20;

		[Desc("Radius (cells) around a target inside which enemy presence reduces its safety score.")]
		public readonly int SafetyEnemyScanRadiusCells = 6;

		[Desc("Safety multiplier (x100) when no enemies near target.")]
		public readonly int SafetyMultiplierSafe = 100;

		[Desc("Safety multiplier (x100) when 1-2 enemies near target.")]
		public readonly int SafetyMultiplierMild = 40;

		[Desc("Safety multiplier (x100) when 3+ enemies near target.")]
		public readonly int SafetyMultiplierHostile = 10;

		[Desc("Actor types that may be pulled in as escorts for captures and defenders for own structures.",
			"Empty = any idle friendly mobile unit except the capturers themselves.")]
		public readonly HashSet<string> SupportingUnitTypes = new();

		[Desc("Number of escort units to attach to each capture dispatch.")]
		public readonly int EscortSize = 2;

		[Desc("Max recruit radius (cells) when searching for idle escort/defender units around the capturer or threatened structure.")]
		public readonly int SupportRecruitRadiusCells = 40;

		[Desc("Withhold a unit from escort/defender duty while ANY of its ammo pools sits below this per-mille of",
			"capacity. Both dispatches are an AttackMove across open ground — a dry escort protects the capturer",
			"from nothing and dies on the way. Matches SupplyFollowerBotModule.HuntStarvingThresholdPerMille.",
			"0 = OFF, the shipped default, so the frozen @stable.tecn twin (which omits this field) recruits",
			"regardless of ammo state. No longer byte-identical: StarvingRecruitGate additionally withholds a",
			"unit that is mid-resupply, unconditionally and on both profiles — see the gate.")]
		public readonly int StarvingRecruitThresholdPerMille = 0;

		[Desc("Radius (cells) inside which enemy army value is counted when evaluating threat to own structures.")]
		public readonly int DefenseEnemyScanRadiusCells = 12;

		[Desc("Radius (cells) inside which friendly army value is counted when evaluating defense.")]
		public readonly int DefenseFriendlyScanRadiusCells = 6;

		[Desc("Number of defenders to summon to a threatened structure per defense tick.")]
		public readonly int DefenseSummonCount = 3;

		[Desc("Minimum enemy army value (engine $) within DefenseEnemyScanRadius to trigger a defense summon.")]
		public readonly int DefenseEnemyValueThreshold = 200;

		[Desc("Keep at least this many owned capturers (TECN) alive-or-pending by requesting production ",
			"when a capture target exists but no capturer is free. 0 = disabled (production left to the shared unit builder).")]
		public readonly int TecnFloor = 0;

		[Desc("Combat-quality budget split (@experimental): cap the alive-or-pending capturer (TECN) floor at this",
			"percent of the current COMBAT army, so the capture budget can never crowd out combat production when the",
			"army is thin. 100 (default) = INERT — the clamp never binds, the combat-army count is not even computed,",
			"and the floor is byte-identical to today. Below 100 shifts budget toward combat (e.g. 50 = capturers may",
			"not exceed half the combat army). Never RAISES the floor. Only consulted when < 100, so @stable / any",
			"non-opting config stays byte-identical.")]
		public readonly int TecnFloorArmyShareCapPct = 100;

		[Desc("Capture-supply un-deadlock (@experimental): re-issue a floor production request once the",
			"outstanding request has gone UNDELIVERED for this many ticks (the shared build FIFO can sit on a",
			"lone pending request while combat buys churn the queue, so alive+pending>=floor suppresses all",
			"re-requests forever). Tick-based, no wall-clock. 0 = DISABLED = frozen behaviour (request only",
			"while alive+pending < floor). NOTE (b8d2e601, 2026-08-02): @stable now sets this to 200",
			"(ai.yaml CaptureCoordinatorBotModule@stable.tecn), so it no longer takes the default path — only a config omitting the knob is frozen.")]
		public readonly int TecnRequestStaleTicks = 0;

		[Desc("Capture-supply scaling (@experimental): scale TecnFloor to the number of reachable NEUTRAL money",
			"POIs (~one capturer per free oil derrick), clamped to [TecnFloor, TecnFloorMax]. Off = the static",
			"TecnFloor above (frozen). NOTE (b8d2e601, 2026-08-02): @stable now sets this true (ai.yaml CaptureCoordinatorBotModule@stable.tecn),",
			"so it no longer takes the default path — only a config omitting the flag is byte-identical.")]
		public readonly bool ScaleTecnFloorToPois = false;

		[Desc("Capture-supply scaling cap (@experimental): upper bound on the POI-scaled floor (see",
			"ScaleTecnFloorToPois). Should be >= TecnFloor. Only consulted when ScaleTecnFloorToPois is set.")]
		public readonly int TecnFloorMax = 0;

		[Desc("Capture-supply priority (@experimental): route the floor production request through the",
			"IBotRequestPriorityUnitProduction path so it OUT-PRIORITISES combat-unit buys for the queue slot",
			"(the floor cannot be starved by combat production). Off = the ordinary request path (frozen).",
			"NOTE (b8d2e601, 2026-08-02): @stable now sets this true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so it no longer takes the",
			"default path — only a config omitting the flag is byte-identical.")]
		public readonly bool TecnRequestPriority = false;

		[Desc("Capture fan-out (@experimental): in the PoiMap-ordered capture pass, EXCLUDE targets already",
			"being captured by an in-flight committed capturer so N free capturers fan out to N DISTINCT neutral",
			"oilbs instead of clustering onto one already claimed. Requires the goal-guard ledger. Off = frozen",
			"(no exclusion). NOTE (b8d2e601, 2026-08-02): @stable now sets this true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so it no",
			"longer takes the default path — only a config omitting the flag is byte-identical.")]
		public readonly bool CaptureFanoutEnabled = false;

		[Desc("Experimental (default false = frozen): for captures farther than TransportCaptureMinDistanceCells,",
			"request a mounted ride from MountedTransportBotModule (TECN-first ferrying) instead of walking the",
			"capturer on foot. Falls back to on-foot when no carrier is free.")]
		public readonly bool UseTransportForDistantCaptures = false;

		[Desc("Minimum distance (cells) from the capturer to the target before a mounted ride is requested.",
			"Nearer targets are walked on foot as before. Only used when UseTransportForDistantCaptures is set.")]
		public readonly int TransportCaptureMinDistanceCells = 12;

		[Desc("EXPERIMENTAL: derive the capturer pool from UnitRoleResolver (role == CaptureSpecialist)",
			"instead of the CapturingActorTypes name list — cures 'wrong unit sent to capture' by class,",
			"since only neutral-tech capturers (Captures targeting the neutral capture type) are dispatched.",
			"Same TECN set for the current roster; robust to roster edits. Default false = frozen list",
			"behaviour. NOTE (b8d2e601, 2026-08-02): the @stable twin now sets this true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so it",
			"resolves roles too — only a config omitting the flag keeps the frozen list path.")]
		public readonly bool UseUnitRoles = false;

		[Desc("Influence stack (capture migration): order capture targets off the BELIEVED anti-ground danger",
			"field (DangerFieldLayer) instead of the OMNISCIENT InfluenceMap threat grid PoiMap bakes into the",
			"capture score. When on, GetCaptureTargets is asked for a threat-NEUTRAL base score (no omniscient",
			"read) and this module re-applies a fog-legal believed-danger damp — threat LOWERS a target's",
			"capture-ordering score, so a capturer is not sent first into a believed weapon envelope. Completes",
			"the @experimental fog migration for capture ordering. OFF by default so a config omitting the flag",
			"keeps the omniscient ordering. NOTE (b8d2e601, 2026-08-02): the @stable twin now sets this true",
			"(ai.yaml CaptureCoordinatorBotModule@stable.tecn) as well as @experimental, so @stable is no longer byte-identical here — both twins",
			"run the fog-legal repoint. Inert (falls back to the omniscient path) if no DangerFieldLayer exists.")]
		public readonly bool StrategicCaptureRepointEnabled = false;

		[Desc("Capture migration: believed anti-ground danger (DangerFieldLayer.GroundDanger) at/below which a",
			"target cell counts as SAFE. IN DANGER UNITS (100 = one reference contact at point-blank), NOT raw",
			"field units and NOT the InfluenceMap scale; sits above the Stage-C territory baseline so ambient",
			"'deep enemy ground' danger doesn't damp every capture.")]
		public readonly int BelievedDangerMildUnits = 30;

		[Desc("Capture migration: believed anti-ground danger at/below which a target is MILD (above it is",
			"HOSTILE — inside a dense believed weapon envelope). Boundary between the mild and hostile damp",
			"buckets. IN DANGER UNITS: 100 = a full reference contact's worth of envelope over the target.")]
		public readonly int BelievedDangerHostileUnits = 100;

		[Desc("Capture migration: ordering multiplier (x100) at SAFE believed danger. Default 100 = inert.")]
		public readonly int BelievedDangerSafeMultiplier = 100;

		[Desc("Capture migration: ordering multiplier (x100) at MILD believed danger. <100 damps a probed",
			"approach so safer captures sort first. Default 100 = inert.")]
		public readonly int BelievedDangerMildMultiplier = 100;

		[Desc("Capture migration: ordering multiplier (x100) at HOSTILE believed danger (dense believed weapon",
			"envelope). <100 strongly damps sending a capturer into believed fire first. Default 100 = inert.")]
		public readonly int BelievedDangerHostileMultiplier = 100;

		[Desc("Lever-4 diagnostics (experimental only): each capture scan, emit a TWO-SIDED ownership snapshot —",
			"for every non-spectating player, the set of income-derrick (CaptureManager) actors it currently owns.",
			"Integrated over ticks this is the capture-income timeseries proxy that separates 'captured later' from",
			"'held shorter' and makes the capture race legible on BOTH bots (the [exp-capture] commit markers only",
			"track this player's own capturers). Diagnostics only — no decision reads it, zero RNG, zero sim effect.",
			"Default false on the engine class, so a config omitting the flag emits nothing. NOTE (b8d2e601,",
			"2026-08-02): the @stable twin now sets this true too (ai.yaml CaptureCoordinatorBotModule@stable.tecn) — telemetry is emitted on BOTH",
			"bots, not just @experimental. Diagnostics only, so no sim divergence follows from it.")]
		public readonly bool CaptureTelemetryEnabled = false;

		[Desc("Contest-aware support (Option A, capture-contest lever): when a capture target's neighbourhood OR an",
			"owned derrick's neighbourhood reads CONTESTED — believed anti-ground danger above ContestedDangerUnits,",
			"or the control-field ring around it reads believed-ENEMY — dispatch ContestedEscortSize escorts instead of",
			"EscortSize and pre-summon defenders at ContestedDefenseEnemyValueThreshold instead of DefenseEnemyValueThreshold.",
			"Reads only fog-legal believed fields (DangerFieldLayer / ControlField); zero RNG. Default false on the engine",
			"class, so a config omitting the flag is frozen. NOTE (b8d2e601, 2026-08-02): the @stable twin now sets this",
			"true (ai.yaml CaptureCoordinatorBotModule@stable.tecn) alongside @experimental — @stable is no longer frozen or byte-identical on this lever.")]
		public readonly bool ContestAwareSupportEnabled = false;

		[Desc("Escort size for a CONTESTED capture target (see ContestAwareSupportEnabled). Uncontested targets keep",
			"EscortSize. Only consulted when ContestAwareSupportEnabled; defaults to 2 = today's EscortSize so an",
			"un-tuned experimental build is unchanged.")]
		public readonly int ContestedEscortSize = 2;

		[Desc("Minimum enemy army value (engine $) to summon defenders when the owned derrick reads CONTESTED. Set",
			"BELOW DefenseEnemyValueThreshold to pre-summon before a light re-capture probe crosses the normal gate.",
			"Only consulted when ContestAwareSupportEnabled; defaults to 200 = today's DefenseEnemyValueThreshold.")]
		public readonly int ContestedDefenseEnemyValueThreshold = 200;

		[Desc("Believed anti-ground danger (DangerFieldLayer.GroundDanger) above which a target/derrick cell counts as",
			"CONTESTED for support sizing. IN DANGER UNITS (100 = one reference contact at point-blank), NOT raw field",
			"units and NOT the InfluenceMap scale; sits above the Stage-C territory baseline so ambient 'deep enemy",
			"ground' danger doesn't flag every capture contested.")]
		public readonly int ContestedDangerUnits = 30;

		[Desc("Escort right-sizing lever (income lever): scale the capture escort DOWN by believed threat at the target.",
			"A derrick in our own verified-safe territory near our SR is captured with the technician ALONE (no combat",
			"units reserved to babysit it), a mildly-exposed one with a small LightEscortSize, and a contested one keeps",
			"the full escort (ContestedEscortSize/EscortSize). Reduction ONLY — it never raises an escort, and never",
			"shrinks a target IsContestedNeighbourhood already flags, so it composes with ContestAwareSupportEnabled",
			"rather than fighting it. Reads only fog-legal believed fields (ControlField + DangerFieldLayer); if either",
			"is absent the lever is inert (no reduction). Zero RNG. Default OFF on the engine class, so a config omitting",
			"the flag is frozen. NOTE (b8d2e601, 2026-08-02): the @stable twin now sets this true (ai.yaml CaptureCoordinatorBotModule@stable.tecn) as well",
			"as @experimental — @stable right-sizes escorts too and is no longer frozen or byte-identical here.")]
		public readonly bool EscortTierSizingEnabled = false;

		[Desc("Escort right-sizing: ring-averaged believed control score (ControlField, positive = ours) at/above which a",
			"target's surroundings count STRONGLY-OURS — the ownership half of the NONE (technician-alone) tier. Sampled",
			"one grid cell past the anchor footprint (like the Stage-F balance-of-power read), NOT the target's own cell.",
			"Only consulted when EscortTierSizingEnabled.")]
		public readonly int SafeControlScoreThreshold = 300;

		[Desc("Escort right-sizing: believed anti-ground danger (DangerFieldLayer.GroundDanger) at/below which a target",
			"cell counts LOW-DANGER — the safety half of the NONE tier. IN DANGER UNITS (100 = one reference contact",
			"at point-blank), above the Stage-C territory baseline. Only consulted when EscortTierSizingEnabled.",
			"Set BELOW ContestedDangerUnits on purpose, which it was not before 2026-08-09. With the two equal, the",
			"danger axis was binary — a cell was either contested (Full) or verified-quiet (eligible for None), with",
			"nothing in between — so the LIGHT tier could only ever be reached via the control-ring or distance",
			"conjuncts, never via 'mildly exposed', which is the case the tier's own Desc describes. The gap between",
			"the two numbers IS the mild band. (This did not make LIGHT unreachable — the NONE tier also requires a",
			"strongly-ours ring and proximity to our SR — so it was a missing band, not dead code.)")]
		public readonly int SafeDangerUnits = 10;

		[Desc("Escort right-sizing: a target within this many cells of our own SR counts NEAR for the NONE tier (an",
			"oil derrick on our doorstep). Distance is measured fog-legally from our SR (PoiMap's DistanceCells).",
			"<= 0 disables the distance gate. Only consulted when EscortTierSizingEnabled.")]
		public readonly int SafeMaxDistanceFromSRCells = 24;

		[Desc("Escort right-sizing: escort count for the LIGHT tier (mildly-exposed open ground). The final escort is",
			"min(this, the contested/normal size) so the lever only ever reduces. Only consulted when EscortTierSizingEnabled.")]
		public readonly int LightEscortSize = 2;

		[Desc("Phase 2 commit-on-order audit (§4): also COMMIT recruited escorts and defenders to the shared",
			"PoiGoalGuard ledger — under disjoint keys capture-escort:<targetId> / capture-defend:<structureId> —",
			"not just the capturer. Today only the capturer is committed (IssueCaptureOrder); escorts (DispatchEscort)",
			"and defenders (QueueDefenseOrders) are recruited from the ledger-checked pool, ordered, then left",
			"UNCOMMITTED, so the offense free pool re-grabs them on its next eval — a ledger-blind steal channel.",
			"Committing them at order time closes it (commit-on-order, the coexistence invariant). Recruits already",
			"come only from the free pool (FindIdleSupportersNear checks IsCommitted); release is via the shared",
			"ledger TTL / Prune (a disembarked or fought-out support unit re-enters the pool on expiry, same backstop",
			"the capturer uses). Default false ⇒ escorts/defenders stay uncommitted ⇒ byte-identical @stable/legacy;",
			"set only on CaptureCoordinatorBotModule@experimental.")]
		public readonly bool CommitSupportUnits = false;

		[Desc("Retreat-when-done (orderless-at-hostile-location bug class): when a capture COMPLETES (target now",
			"ours) or the target is GONE (destroyed/uncapturable), send the surviving capturer BACK to our Supply",
			"Route instead of leaving it idle at the captured structure, typically deep in neutral/enemy territory.",
			"A CaptureSpecialist has no AttackBase, so it is EXCLUDED from every combat free pool and from this",
			"module's own escort/defender recruitment — nothing re-collects an idle TECN, so without this it is a",
			"free kill parked at the front. A TECN consumed by the capture is already dead/not-in-world and is",
			"skipped. Deterministic (SR lookup + Move order), zero RNG. Engine default false keeps any profile",
			"that OMITS the field byte-identical (legacy/normal); both live .tecn twins enable it DELIBERATELY —",
			"this is a bug-class fix, not a tuning lever, so it is turned on wherever the module actually runs.")]
		public readonly bool RetreatCapturerWhenDone = false;

		[Desc("Actor types of the bot's home Supply Route — the retreat anchor for RetreatCapturerWhenDone.",
			"Mirrors MountedTransportBotModuleInfo.SupplyRouteTypes.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("Unit-purpose: give a capturer this scan could NOT dispatch — no scoreable capture target at all, or",
			"none it CanTarget — an explicit disposition instead of dropping it on the floor. Today the undispatched",
			"remainder of QueueCaptureOrdersFromPoiMap is simply discarded: no Move, no park, no rally, no claim. That",
			"leaves a CaptureSpecialist standing wherever it arrived, IsIdle and unclaimed in BOTH registries — which",
			"is precisely the pool GarrisonBotModule recruits from, and is how idle technicians ended up garrisoning",
			"rear civilian houses for the rest of the match.",
			"",
			"NOTE this module, not PoiOffensiveBotModule, is the only possible owner: StageFreePool cannot stage a",
			"capturer at ANY point in the offense path, because BuildFreePool → IsEligibleCombatUnit narrows to role",
			"MainBattle/IndirectFire under UseUnitRoles, and a capturer is CaptureSpecialist. Making the offense's",
			"targets==0 early return fall through does not reach this unit class.",
			"",
			"The disposition is a reserve muster BEHIND the believed frontier — the same ForwardStagingMath descent",
			"the offense stages its line pool with, seeded from our own SR and standing off further. With a flat or",
			"unpopulated control field (the opening, no believed contact) the descent returns its seed, so the",
			"capturer holds at the beachhead: correct doctrine for an unarmed consumable with nothing to take and",
			"nowhere scouted. As a front forms it follows one bound behind, ready when a derrick is uncovered.",
			"Reads only fog-legal believed fields (ControlField + DangerFieldLayer); zero RNG. Never ledger-committed",
			"— a reserve capturer must stay instantly re-dispatchable. Default false ⇒ byte-identical when off.",
			"",
			"SCOPE: the PoiMap dispatch path only. The legacy no-PoiMap fallback still discards its own remainder,",
			"and is deliberately left alone — WW3MOD always has the PoiMap world trait (rules/world.yaml), so that",
			"branch is unreachable here and wiring it would be adding untested code to a dead path. A mod that runs",
			"this module WITHOUT a PoiMap gets the old discard behaviour.")]
		public readonly bool StageIdleCapturers = false;

		[Desc("Idle-capturer reserve: halt this many coarse frontier cells short of the believed line. Deliberately",
			"larger than PoiOffensiveBotModule.StagingStandoffCells (6) — a 250-cost consumable that dies to one",
			"burst stands off further than a rifle squad. Only read when StageIdleCapturers is on.")]
		public readonly int ReserveStandoffCells = 10;

		[Desc("Idle-capturer reserve: believed anti-ground danger above which a descent step is refused, so the",
			"reserve never musters inside a believed weapon envelope. IN DANGER UNITS (100 = one reference contact",
			"at point-blank). 0 = 'outside every believed envelope' and converts losslessly, since 0 units is 0 raw",
			"field units at any scale; negative disables the danger guard. Only read when StageIdleCapturers is on.")]
		public readonly int ReserveDangerSafeUnits = 0;

		[Desc("Idle-capturer reserve: descent step budget, so the walk is never a free search.")]
		public readonly int ReserveMaxDescentSteps = 64;

		[Desc("Idle-capturer reserve: keep the previously adopted anchor unless the newly resolved one moved at",
			"least this many map cells (Chebyshev). The anchor is a COARSE grid cell mapped back through",
			"GridCellToMapCell, so a one-grid-cell field wobble displaces the destination by a whole CellSize and",
			"re-lays the entire reserve. Mirrors PoiOffensiveBotModule.StagingHysteresisCells, which exists for",
			"exactly this reason. 0 disables the damping (every resolve is adopted).")]
		public readonly int ReserveHysteresisCells = 3;

		[Desc("Idle-capturer reserve: ring spacing, in map cells, of the fan-out around the reserve anchor. Without",
			"a fan-out every reserved capturer is sent to the SAME cell and they clog it. Mirrors",
			"PoiOffensiveBotModule.StagingSpreadStepCells; the ring count is bounded so the widest ring stays",
			"strictly inside the standoff. 0 disables the fan-out (all capturers muster on the anchor cell).")]
		public readonly int ReserveSpreadStepCells = 2;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase). CapturableActorTypes is one of the
			// "half-guarded" fields — the query sites lowercase the actor name but the set was built
			// case-sensitively; normalizing the set closes that gap.
			ActorNameCase.NormalizeInPlace(CapturingActorTypes);
			ActorNameCase.NormalizeInPlace(CapturableActorTypes);
			ActorNameCase.NormalizeInPlace(SupportingUnitTypes);
			ActorNameCase.NormalizeKeysInPlace(IncomeWeights);
			ActorNameCase.NormalizeInPlace(SupplyDepotActorTypes);
		}

		public override object Create(ActorInitializer init) { return new CaptureCoordinatorBotModule(init.Self, this); }
	}

	public class CaptureCoordinatorBotModule : ConditionalTrait<CaptureCoordinatorBotModuleInfo>, IBotTick, INotifyKilled, INotifyActorDisposing
	{
		readonly World world;
		readonly Player player;
		readonly Predicate<Actor> unitCannotBeOrderedOrIsIdle;
		readonly int maximumCaptureTargetOptions;

		// Per-unit commitment ledger (Phase 0/1). When present it REPLACES the
		// IsIdle-based re-eligibility below: a committed TECN is skipped even when
		// its activity flickers idle mid-walk, so its CaptureActor order is never
		// overwritten. Resolved lazily on first tick (sibling player trait).
		PoiGoalGuard goalGuard;
		bool goalGuardResolved;

		// POI-strategy Phase 2: when present, PoiMap supplies the capture-target
		// ORDERING (value x distance x threat, scored from this player's SR),
		// replacing this module's own per-target scan below. Resolved lazily on
		// first tick (world trait). A missing PoiMap degrades to the legacy
		// internal scoring path so the module still works standalone.
		PoiMap poiMap;
		bool poiMapResolved;

		// Influence stack (capture migration): the believed anti-ground danger field, resolved ONLY when
		// StrategicCaptureRepointEnabled, so a config leaving the flag off never touches it. NOTE (b8d2e601,
		// 2026-08-02): @stable now sets the flag true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so @stable DOES resolve and read this
		// field — it is not an experimental-only path. When present it replaces the omniscient InfluenceMap
		// threat baked into the capture score with a fog-legal damp.
		DangerFieldLayer dangerField;
		bool dangerFieldResolved;

		// Contest-aware support (Option A): the believed control + anti-ground danger fields, resolved ONLY when
		// ContestAwareSupportEnabled, so a config leaving the flag off never touches them. NOTE (b8d2e601,
		// 2026-08-02): @stable now sets the flag true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so @stable resolves these too. Kept
		// separate from `dangerField` above (tied to StrategicCaptureRepointEnabled) so the two levers stay
		// independently gated.
		ControlField contestControlField;
		DangerFieldLayer contestDangerField;
		bool contestFieldsResolved;

		// Escort right-sizing (EscortTierSizingEnabled): the believed control + anti-ground danger fields, resolved
		// ONLY when the lever is on, so a config leaving it off never touches them. NOTE (b8d2e601, 2026-08-02):
		// @stable now sets the lever true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so @stable resolves these as well. Kept separate from the
		// two references above so this reduction lever is independently gated from StrategicCaptureRepoint and
		// ContestAwareSupport.
		ControlField tierControlField;
		DangerFieldLayer tierDangerField;
		bool tierFieldsResolved;

		// Idle-capturer reserve (StageIdleCapturers): same fields again, resolved independently so this lever is
		// gated on its own. reserveCells is the per-unit last-issued destination — the dedup that keeps a capturer
		// walking to the anchor instead of restarting the move every scan.
		ControlField reserveControlField;
		DangerFieldLayer reserveDangerField;
		bool reserveFieldsResolved;
		readonly Dictionary<Actor, CPos> reserveCells = new();

		// Last anchor actually ADOPTED, for the hysteresis in ResolveReserveAnchor. Without it a one-grid-cell
		// wobble in the frontier field moves the destination by a whole CellSize and re-issues every reserve move.
		CPos? lastReserveAnchor;

		// Case-insensitive copy of Info.CapturableActorTypes, built once, so the telemetry hot loop matches actor
		// names without a per-actor ToLowerInvariant() allocation. Empty stays empty (= match all capturables).
		HashSet<string> capturableTypesCI;
		HashSet<string> CapturableTypesForTelemetry =>
			capturableTypesCI ??= new HashSet<string>(Info.CapturableActorTypes, StringComparer.OrdinalIgnoreCase);

		// LEGACY FALLBACK ONLY (guard not wired): capturers we've already issued
		// orders to; cleaned when they become idle again. This is the thrash-prone
		// path the guard exists to replace — kept so a missing PoiGoalGuard trait
		// degrades gracefully instead of crashing.
		readonly List<Actor> activeCapturers = new();

		// Defender bookings — actor → tick they were summoned. Stale entries removed on tick.
		readonly Dictionary<Actor, int> defenderBookings = new();

		// The ammo term (StarvingRecruitThresholdPerMille); see StarvingRecruitGate.
		readonly StarvingRecruitGate ammoGate = new("capture-support");

		ActorIndex.OwnerAndNamesAndTrait<CapturesInfo> capturingActors;

		// Role-model capturer pool (Phase 4b). When UseUnitRoles is set, the capturingActors index AND the
		// capturer NAME set are rebuilt ONCE on first tick from the CaptureSpecialist role class instead of
		// CapturingActorTypes, so EVERY consumer — the index-backed pool below and the five name-list sites
		// (early-return, ResolveTecnBuildType, defense-pass friendly exclusion, escort-recruit exclusion,
		// killed-handler rescan) — becomes class-driven. Resolved lazily because the world-trait resolver's
		// cache is only guaranteed populated by the first BotTick.
		UnitRoleResolver resolver;
		bool resolverResolved;

		// The current capturer name set: the role-derived set once rebuilt (role mode), else the frozen
		// Info.CapturingActorTypes. Returns the identical HashSet instance when the flag is off, so the
		// legacy path (Count / Contains / FirstOrDefault) stays byte-identical.
		HashSet<string> capturerNames;
		HashSet<string> CapturerNames => capturerNames ?? Info.CapturingActorTypes;

		// TECN availability floor (cycle 2). When Info.TecnFloor > 0 the coordinator
		// pulls production of its own capturer via the IBotRequestUnitProduction queue
		// (the shared UnitBuilder's request path bypasses the share/UnitLimits gates),
		// so a run can't field zero TECNs while derricks sit uncaptured. Resolved lazily:
		// unitProducers = the player's request sinks; tecnBuildType = the single capturer
		// name this player can actually build (faction-correct, no hardcoding).
		IBotRequestUnitProduction[] unitProducers;
		string tecnBuildType;

		// Capture-supply priority (TecnRequestPriority): the priority-request sinks, resolved lazily ONLY when the
		// flag is on, so a config leaving it off never touches them. NOTE (b8d2e601, 2026-08-02): @stable now sets
		// TecnRequestPriority (ai.yaml CaptureCoordinatorBotModule@stable.tecn) and TecnRequestStaleTicks=200 (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so on @stable these sinks
		// ARE resolved and the staleness read below is LIVE, not inert. The tick of the last floor request feeds the
		// tick-based staleness re-issue; written unconditionally but only READ when the staleness knob is on, so it
		// stays inert only for a config that omits both knobs.
		IBotRequestPriorityUnitProduction[] priorityProducers;
		bool priorityProducersResolved;
		int lastFloorRequestTick = int.MinValue;

		// TECN-first ferrying (experimental): the player's enabled MountedTransportBotModule.
		// Resolved lazily — the module is split into @stable/@experimental twins, so we pick the
		// enabled instance (TraitOrDefault would throw on the two-instance player actor).
		MountedTransportBotModule transportModule;
		bool transportModuleResolved;

		int captureScanCountdown;
		int defenseScanCountdown;

		public CaptureCoordinatorBotModule(Actor self, CaptureCoordinatorBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;

			if (world.Type == WorldType.Editor)
				return;

			unitCannotBeOrderedOrIsIdle = a => a.Owner != player || a.IsDead || !a.IsInWorld || a.IsIdle;
			maximumCaptureTargetOptions = Math.Max(1, Info.MaximumCaptureTargetOptions);

			capturingActors = new ActorIndex.OwnerAndNamesAndTrait<CapturesInfo>(world, Info.CapturingActorTypes, player);
		}

		protected override void TraitEnabled(Actor self)
		{
			// Stagger initial fire so all AIs don't tick the heavy scans on the same frame.
			captureScanCountdown = world.LocalRandom.Next(0, Info.ScanInterval);
			defenseScanCountdown = world.LocalRandom.Next(0, Info.DefenseScanInterval);
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			// Bookings expire after one defense interval — that's enough time for the
			// defender to walk in, engage, and either die or pop back to idle. Keeps
			// the same actor from being booked again every single tick.
			var staleBookingTick = world.WorldTick - Info.DefenseScanInterval;
			var staleKeys = defenderBookings
				.Where(kv => kv.Value < staleBookingTick || kv.Key.IsDead || !kv.Key.IsInWorld)
				.Select(kv => kv.Key)
				.ToList();
			foreach (var k in staleKeys)
				defenderBookings.Remove(k);

			// Resolve the shared goal-guard ledger up-front so BOTH the defense and
			// capture passes honour the §5.6 unit-claim: escort/defender recruitment
			// must not grab a unit the offense module has already committed to an axis.
			if (!goalGuardResolved)
			{
				goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
				goalGuardResolved = true;
			}

			// Rebuild the capturer pool from the role model ONCE (experimental only). The
			// CaptureSpecialist class (Captures targeting the neutral-tech type) replaces the
			// CapturingActorTypes name list as the single source feeding every pool consumer below.
			// Same TECN set today; robust to roster edits. See WORKSPACE/DISCOVERIES.md (2026-07-24).
			if (!resolverResolved)
			{
				resolverResolved = true;
				if (Info.UseUnitRoles)
				{
					resolver = world.WorldActor.TraitOrDefault<UnitRoleResolver>();
					if (resolver != null)
					{
						var roleNames = resolver.NamesWithRole(UnitRole.CaptureSpecialist).ToHashSet();
						capturerNames = roleNames;
						capturingActors.Dispose();
						capturingActors = new ActorIndex.OwnerAndNamesAndTrait<CapturesInfo>(world, roleNames, player);
					}
				}
			}

			if (--captureScanCountdown <= 0)
			{
				captureScanCountdown = Info.ScanInterval;
				QueueCaptureOrders(bot);
			}

			if (--defenseScanCountdown <= 0)
			{
				defenseScanCountdown = Info.DefenseScanInterval;
				QueueDefenseOrders(bot);
			}
		}

		// ============================================================
		// CAPTURE PASS
		// ============================================================

		void QueueCaptureOrders(IBot bot)
		{
			// Lever-4 diagnostics: two-sided derrick-ownership snapshot (see CaptureTelemetryEnabled). Fired before
			// the capturer-pool early-out so it records the race even on scans where this player has no free capturer.
			if (Info.CaptureTelemetryEnabled)
				LogOwnershipSnapshot();

			// PITFALL: CapturingActorTypes is load-bearing even in role mode — it is the on/off switch on the
			// off/@stable path AND the fallback whenever the resolver is absent or the first-tick rebuild
			// hasn't run yet (CapturerNames returns it until capturerNames is populated). Emptying it disables
			// capture on every path that falls back to it, so keep it non-empty in ai.yaml.
			if (CapturerNames.Count == 0)
				return;

			// Per-TECN diagnostic: each scan, log every owned capturer's state.
			// User reports "orders gets overwritten" — this log lets us see the
			// idle-flip cadence + which activity is running. If we see a TECN
			// flip from CaptureActor → <none> → CaptureActor between scans, we
			// know the inner activity is failing; if we see new orders going
			// out to a TECN that already had one, the issuing logic is the bug.
			foreach (var a in capturingActors.Actors)
			{
				var activity = a.CurrentActivity?.GetType().Name ?? "<none>";
				var committed = goalGuard != null && goalGuard.Ledger.IsCommitted(a, world.WorldTick);
				var commitN = goalGuard != null ? goalGuard.Ledger.CommitCountFor(a) : activeCapturers.Contains(a) ? 1 : 0;
				Log.Write("debug",
					$"[exp-capture] pre-scan player={player.PlayerName} actor={a.Info.Name}@{a.Location} idle={a.IsIdle} activity={activity} committed={committed} commitN={commitN} tick={world.WorldTick}");
			}

			// goalGuard resolved in BotTick (shared with the defense pass).
			var useGuard = goalGuard != null && !goalGuard.IsTraitDisabled;
			HashSet<Actor> retreatedThisScan = null;
			if (useGuard)
				retreatedThisScan = ReconcileGuardCommitments(bot);
			else
				activeCapturers.RemoveAll(unitCannotBeOrderedOrIsIdle);

			// A TECN is available for a NEW capture order only if it's idle AND not
			// already committed. The guard path leaves a committed-but-idle-flickering
			// TECN alone (no re-issue); the legacy path falls back to the active list.
			// A TECN retreated home THIS scan (RetreatCapturerWhenDone) still reads IsIdle, so exclude
			// it too — otherwise a fresh CaptureActor would queue behind its retreat Move and yank it
			// back to the front on a wasteful round trip.
			var idleCapturers = capturingActors.Actors
				.Where(a => a.IsIdle && a.Info.HasTraitInfo<IPositionableInfo>()
					&& (retreatedThisScan == null || !retreatedThisScan.Contains(a))
					&& (useGuard
						? !goalGuard.Ledger.IsCommitted(a, world.WorldTick)
						: !activeCapturers.Contains(a)))
				.Select(a => new TraitPair<CaptureManager>(a, a.TraitOrDefault<CaptureManager>()))
				.Where(tp => tp.Trait != null)
				.ToArray();

			if (idleCapturers.Length == 0)
			{
				// M-2: no capturer free to dispatch this scan. Quantifies the F-1
				// production/survival gap — how long the pool sits empty or fully
				// committed while derricks go uncaptured.
				var totalTecns = capturingActors.Actors.Count;
				var committedCount = useGuard
					? capturingActors.Actors.Count(a => goalGuard.Ledger.IsCommitted(a, world.WorldTick))
					: activeCapturers.Count;
				Log.Write("debug",
					$"[exp-capture] no-idle-capturers player={player.PlayerName} total-tecns={totalTecns} committed={committedCount} idle=0 tick={world.WorldTick}");

				// Cycle 2: no capturer is free this scan. If the alive-or-pending TECN
				// pool has dropped below the floor AND a derrick is still worth taking,
				// pull production so a run can't field zero capturers while targets sit
				// uncaptured. Demand-gated here (not unconditional) so we never spend
				// budget on a TECN with nothing to capture.
				if (Info.TecnFloor > 0)
					MaintainTecnFloor(bot);

				return;
			}

			// Escorts already recruited THIS TICK (their AttackMove is queued but
			// IsIdle is still true) — shared by both selection paths so a second
			// capturer doesn't re-pick them.
			var escortsRecruitedThisTick = new HashSet<Actor>();

			// Preferred path: let PoiMap order the targets (value x distance x
			// threat from our SR). Falls back to the legacy per-target scan below
			// if the world has no PoiMap trait.
			if (!poiMapResolved)
			{
				poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
				poiMapResolved = true;
			}

			if (poiMap != null)
			{
				QueueCaptureOrdersFromPoiMap(bot, idleCapturers, useGuard, escortsRecruitedThisTick);
				return;
			}

			// Collect all targetable candidates across all eligible owners.
			var candidates = new List<Actor>();
			foreach (var otherPlayer in world.Players)
			{
				if (otherPlayer.Spectating)
					continue;
				if (!Info.CapturableRelationships.HasRelationship(player.RelationshipWith(otherPlayer)))
					continue;

				var actorPool = Info.CheckCaptureTargetsForVisibility
					? GetVisibleActorsBelongingToPlayer(otherPlayer)
					: GetActorsThatCanBeOrderedByPlayer(otherPlayer);

				foreach (var actor in actorPool)
				{
					if (Info.CapturableActorTypes.Count > 0
						&& !Info.CapturableActorTypes.Contains(actor.Info.Name.ToLowerInvariant()))
						continue;

					var cm = actor.TraitOrDefault<CaptureManager>();
					if (cm == null)
						continue;

					if (!idleCapturers.Any(tp => tp.Trait.CanTarget(cm)))
						continue;

					candidates.Add(actor);
				}
			}

			if (candidates.Count == 0)
				return;

			// Score every (capturer, candidate) pair; assign greedily by score.
			// We keep the per-capturer top-N candidates to avoid an N×M blow-up on big maps.
			var availableCapturers = new List<TraitPair<CaptureManager>>(idleCapturers);
			var alreadyTargetedThisTick = new HashSet<Actor>();

			while (availableCapturers.Count > 0)
			{
				var capturer = availableCapturers[0];

				Actor bestTarget = null;
				long bestScore = long.MinValue;

				var considered = 0;
				foreach (var target in candidates.OrderByDescending(a => GetIncomeWeight(a)).Take(maximumCaptureTargetOptions))
				{
					if (alreadyTargetedThisTick.Contains(target))
						continue;

					var s = ScoreTarget(capturer.Actor, target);
					if (s > bestScore)
					{
						bestScore = s;
						bestTarget = target;
					}

					if (++considered >= maximumCaptureTargetOptions)
						break;
				}

				if (bestTarget == null)
					break;

				// Legacy no-PoiMap path: no fog-legal SR distance available, so pass -1 (unknown) — the
				// right-sizing lever treats unknown distance as failing the near-SR gate (never sends a lone capturer).
				IssueCaptureOrder(bot, capturer.Actor, bestTarget, useGuard, escortsRecruitedThisTick, bestScore, -1);
				alreadyTargetedThisTick.Add(bestTarget);
				availableCapturers.RemoveAt(0);
			}
		}

		// Lever-4 two-sided capture telemetry (diagnostics only, emitted by the @experimental instance). For every
		// non-spectating player, log the income-derrick (CaptureManager) actors it currently owns. Integrated over
		// the scan cadence this is the ownership timeseries that distinguishes "captured later" (ownership flips to
		// us later) from "held shorter" (ownership flips away from us sooner) — the H1-vs-H2 disambiguator the
		// attribution recon flagged as missing. Ground-truth read, but NOTHING here feeds a bot decision, so the
		// no-fog-cheating rule (which governs behaviour reads) is not engaged; zero RNG, zero sim side-effect.
		// SCOPE: the ownership timeseries is the shipped §4.D artifact; the commit-tick/capturer-count half of §4.D
		// is deliberately deferred (the [exp-capture] issue/commit markers already carry per-dispatch timing).
		// PERF: a SINGLE world.Actors pass bucketed by owner (not a Where-scan per player), matching names against
		// the pre-built case-insensitive set so no per-actor string is allocated — telemetry fires every
		// ScanInterval throughout long benchmark runs, exactly where GC churn hurts.
		void LogOwnershipSnapshot()
		{
			var types = CapturableTypesForTelemetry;
			var byOwner = new Dictionary<Player, List<Actor>>();
			foreach (var a in world.Actors)
			{
				if (a.IsDead || !a.IsInWorld || !a.Info.HasTraitInfo<CaptureManagerInfo>())
					continue;
				if (types.Count > 0 && !types.Contains(a.Info.Name))
					continue;

				if (!byOwner.TryGetValue(a.Owner, out var list))
					byOwner[a.Owner] = list = new List<Actor>();
				list.Add(a);
			}

			// Emit in world.Players order, skipping spectators — identical output ordering to the per-player scan.
			foreach (var owner in world.Players)
			{
				if (owner.Spectating || !byOwner.TryGetValue(owner, out var derricks))
					continue;

				var held = string.Join(",", derricks.Select(a => $"{a.Info.Name}#{a.ActorID}@{a.Location}"));
				Log.Write("debug",
					$"[exp-capture] ownership-snapshot observer={player.PlayerName} owner={owner.PlayerName} count={derricks.Count} held={held} tick={world.WorldTick}");
			}
		}

		// ============================================================
		// TECN AVAILABILITY FLOOR (cycle 2)
		// ============================================================

		// Called from the M-2 branch (no free capturer this scan). Keeps the
		// alive-or-pending capturer pool at >= Info.TecnFloor by requesting ONE
		// capturer through the shared UnitBuilder's IBotRequestUnitProduction queue.
		// That request path is processed first each build cycle and bypasses both the
		// UnitsToBuild share test AND UnitLimits (single-name BuildUnit overload), so a
		// request out-competes the blind production lottery for the queue slot whenever
		// the queue is free. When TecnRequestPriority is on the request rides the priority
		// path, which the UnitBuilder drains BEFORE the FIFO/lottery and now peek-don't-pops
		// (a busy-queue cycle keeps the request queued for the next free slot rather than
		// dropping it), so a single in-flight request reliably delivers — the ShouldRequestTecn
		// in-flight cap then keeps pending bounded to the floor.
		void MaintainTecnFloor(IBot bot)
		{
			unitProducers ??= player.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>().ToArray();
			if (unitProducers.Length == 0)
				return;

			// Resolve the faction-correct capturer name once it becomes buildable.
			// Don't cache a null result — the Infantry queue / prereqs may not be live
			// on the first scan, so keep retrying until a buildable name is found.
			tecnBuildType ??= ResolveTecnBuildType();
			if (tecnBuildType == null)
				return;

			// (b) Floor scaling: the static TecnFloor, or one capturer per reachable NEUTRAL money POI clamped
			// to [TecnFloor, TecnFloorMax]. Off (a config omitting the flag) ⇒ EffectiveFloor returns Info.TecnFloor.
			// NOTE (b8d2e601, 2026-08-02): @stable sets ScaleTecnFloorToPois true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so @stable takes
			// the scaled branch — the POI count IS computed there.
			var floor = CaptureSupplyMath.EffectiveFloor(Info.ScaleTecnFloorToPois, Info.TecnFloor,
				Info.ScaleTecnFloorToPois ? CountReachableNeutralMoneyPois() : 0, Info.TecnFloorMax);

			// Combat-quality budget split: optionally clamp the floor to a share of the combat army so capture
			// demand can't crowd out combat production. Inert at 100 (the default) — the army count is skipped
			// entirely, so @stable / any non-opting config is byte-identical.
			if (Info.TecnFloorArmyShareCapPct < 100)
				floor = CaptureSupplyMath.ClampFloorToArmyShare(floor, CountOwnCombatArmy(), Info.TecnFloorArmyShareCapPct);

			var alive = capturingActors.Actors.Count;
			var pending = unitProducers.Sum(u => u.RequestedProductionCount(bot, tecnBuildType));

			// (a) Re-request gate + un-deadlock. With TecnRequestStaleTicks == 0 this is EXACTLY the frozen
			// `alive + pending < floor` test; with it on, an undelivered pending request is re-issued once it
			// has gone stale (tick-based), so the floor can't sit forever behind a starved queue slot.
			if (!CaptureSupplyMath.ShouldRequestTecn(floor, alive, pending,
				world.WorldTick, lastFloorRequestTick, Info.TecnRequestStaleTicks))
				return;

			// Only pull a capturer if there is actually something to capture.
			if (!CaptureTargetExists())
				return;

			// (c) Priority: when opted in, route through the priority sink so the request out-competes combat
			// buys for the queue slot. Falls back to the ordinary path (frozen behaviour) if not opted in or no
			// priority sink exists.
			var issuedPriority = false;
			if (Info.TecnRequestPriority)
			{
				if (!priorityProducersResolved)
				{
					priorityProducers = player.PlayerActor.TraitsImplementing<IBotRequestPriorityUnitProduction>().ToArray();
					priorityProducersResolved = true;
				}

				// Route to the FIRST producer that ACCEPTS (returns true). A player carries several UnitBuilder
				// twins (normal / experimental / air), all but one condition-disabled per game; a disabled twin
				// answers the interface but never ticks, so handing it the request deadlocks the floor (its
				// pending count climbs while nothing is ever built — the measured pending=82 / alive=0). Skipping
				// to the first accepting (enabled) twin lands the request on the UnitBuilder that will drain it.
				foreach (var p in priorityProducers)
				{
					if (p.RequestPriorityUnitProduction(bot, tecnBuildType))
					{
						issuedPriority = true;
						break;
					}
				}
			}

			if (!issuedPriority)
				unitProducers[0].RequestUnitProduction(bot, tecnBuildType);

			lastFloorRequestTick = world.WorldTick;
			Log.Write("debug",
				$"[exp-capture] tecn-floor-request player={player.PlayerName} type={tecnBuildType} alive={alive} pending={pending} floor={floor} priority={issuedPriority} tick={world.WorldTick}");
		}

		// Combat-quality budget split: count this player's fielded COMBAT units — armed (AttackBase) and
		// ground-mobile, excluding aircraft. Capturers/logistics have no AttackBase and don't count. Only called
		// when the army-share cap is active (< 100), so the frozen path never pays for the scan. Deterministic
		// integer count over the world actor list; zero RNG.
		int CountOwnCombatArmy()
		{
			var n = 0;
			foreach (var a in world.Actors)
			{
				if (a.Owner != player || a.IsDead || !a.IsInWorld)
					continue;
				if (!a.Info.HasTraitInfo<AttackBaseInfo>() || !a.Info.HasTraitInfo<MobileInfo>())
					continue;
				if (a.Info.HasTraitInfo<AircraftInfo>())
					continue;

				n++;
			}

			return n;
		}

		// (b) Count reachable NEUTRAL money POIs — free oil derricks worth ~one capturer each — from the same
		// PoiMap-ordered candidate list the capture pass uses. Neutral income structures only (Capture action,
		// IncomeStructure kind, Neutral owner): enemy-owned income is defended, and Supply Routes are not money.
		// Deterministic: iterates the sorted target list, integer count. Only called when ScaleTecnFloorToPois.
		int CountReachableNeutralMoneyPois()
		{
			if (!poiMapResolved)
			{
				poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
				poiMapResolved = true;
			}

			if (poiMap == null)
				return 0;

			var count = 0;
			foreach (var poi in OrderedCaptureTargets())
			{
				if (poi.Kind != PoiKind.IncomeStructure || poi.Action != PoiAction.Capture)
					continue;

				var target = poi.Actor;
				if (target == null || target.IsDead || !target.IsInWorld)
					continue;

				if (player.RelationshipWith(target.Owner) == PlayerRelationship.Neutral)
					count++;
			}

			return count;
		}

		// The player's faction can build exactly one of the capturer types (e.g.
		// nato → tecn.america). Intersect CapturingActorTypes with what the player's
		// Infantry queue can actually build; the generic ~disabled `tecn` is filtered
		// out because it isn't buildable, and a wrong-faction name can't be returned.
		string ResolveTecnBuildType()
		{
			var buildable = AIUtils.FindQueuesByCategory(player)["Infantry"]
				.SelectMany(q => q.BuildableItems())
				.Select(a => a.Name)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			return CapturerNames.FirstOrDefault(t => buildable.Contains(t));
		}

		// Cheap existence check for the demand gate — is any eligible capturable
		// structure still out there? Uses PoiMap's ranked list when present (it already
		// excludes our own POIs), else a direct scan honouring the module's filters.
		bool CaptureTargetExists()
		{
			if (!poiMapResolved)
			{
				poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
				poiMapResolved = true;
			}

			if (poiMap != null)
				return OrderedCaptureTargets().Count > 0;

			foreach (var otherPlayer in world.Players)
			{
				if (otherPlayer.Spectating)
					continue;
				if (!Info.CapturableRelationships.HasRelationship(player.RelationshipWith(otherPlayer)))
					continue;

				foreach (var actor in GetActorsThatCanBeOrderedByPlayer(otherPlayer))
				{
					if (Info.CapturableActorTypes.Count > 0
						&& !Info.CapturableActorTypes.Contains(actor.Info.Name.ToLowerInvariant()))
						continue;
					if (actor.Info.HasTraitInfo<CaptureManagerInfo>())
						return true;
				}
			}

			return false;
		}

		// Capture-target ordering honouring the fog-migration gate. Default path (flag off) returns PoiMap's
		// frozen omniscient GetCaptureTargets ordering VERBATIM — byte-identical. NOTE (b8d2e601, 2026-08-02):
		// @stable sets StrategicCaptureRepointEnabled true (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so @stable does NOT take that default
		// path — it orders fog-legally like @experimental. When
		// StrategicCaptureRepointEnabled AND a DangerFieldLayer exists, PoiMap is asked for a threat-NEUTRAL
		// base score (no omniscient read at all) and the believed anti-ground danger field re-orders it
		// fog-legally below. Assumes poiMap != null (every caller resolves it first).
		List<ScoredPoi> OrderedCaptureTargets()
		{
			if (!dangerFieldResolved)
			{
				dangerField = Info.StrategicCaptureRepointEnabled
					? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;
				dangerFieldResolved = true;
			}

			var repoint = Info.StrategicCaptureRepointEnabled && dangerField != null;
			if (!repoint)
				return poiMap.GetCaptureTargets(player);

			return RescaleCaptureByBelievedDanger(poiMap.GetCaptureTargets(player, suppressOmniscientThreat: true));
		}

		// Re-order the (threat-neutral) capture targets by the BELIEVED anti-ground danger field: believed
		// danger LOWERS a target's capture-ordering score (safe sorts first, a dense believed weapon envelope
		// last), the fog-legal replacement for the omniscient InfluenceMap threat PoiMap used to bake in. The
		// EnemyInfluence field is repurposed to carry the sampled ground danger for the log line. Pure factor
		// (PoiScoring.BelievedThreatFactor) draws ZERO random; re-sorts with the SAME comparator PoiMap uses.
		List<ScoredPoi> RescaleCaptureByBelievedDanger(List<ScoredPoi> targets)
		{
			var scaled = new List<ScoredPoi>(targets.Count);
			foreach (var p in targets)
			{
				var groundDanger = dangerField.GroundDanger(player, p.Location);
				var mul = PoiScoring.BelievedThreatFactor(groundDanger,
					dangerField.GroundDangerUnitsToField(Info.BelievedDangerMildUnits),
					dangerField.GroundDangerUnitsToField(Info.BelievedDangerHostileUnits),
					Info.BelievedDangerSafeMultiplier, Info.BelievedDangerMildMultiplier,
					Info.BelievedDangerHostileMultiplier);

				var newScore = p.Score * mul / 100;
				scaled.Add(new ScoredPoi(p.Actor, p.Kind, p.Action, p.Value,
					p.DistanceCells, groundDanger, newScore));
			}

			scaled.Sort((a, b) => PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
				b.Score, b.DistanceCells, b.Actor.ActorID));
			return scaled;
		}

		// PoiMap-ordered capture selection (Phase 2). PoiMap has already ranked the
		// capture targets by value x distance x threat from our SR; we just walk
		// that ranking and assign the NEAREST free, uncommitted, able capturer to
		// each. This replaces the legacy per-target scan with a single strategic
		// ordering shared across the experimental AI (and reused verbatim by Phase 3 offense).
		void QueueCaptureOrdersFromPoiMap(IBot bot, TraitPair<CaptureManager>[] idleCapturers, bool useGuard, HashSet<Actor> escortsRecruitedThisTick)
		{
			var available = new List<TraitPair<CaptureManager>>(idleCapturers);

			var targets = OrderedCaptureTargets();

			// (d) Fan-out: drop targets already being captured by an in-flight committed capturer so the
			// newly-free capturers this scan fan out to DISTINCT neutral oilbs instead of clustering onto one
			// already claimed (the measured 2-TECN→1-oilb waste). Deterministic — the in-flight set is built
			// from the synced capturer index and SelectDistinctTargets preserves the ranked order, querying only
			// set membership. Off / no guard ⇒ the frozen unfiltered list.
			if (Info.CaptureFanoutEnabled && useGuard)
			{
				var inFlight = BuildInFlightCaptureTargetIds();
				if (inFlight.Count > 0)
				{
					var orderedIds = new List<uint>(targets.Count);
					foreach (var poi in targets)
						if (poi.Actor != null)
							orderedIds.Add(poi.Actor.ActorID);

					var keep = new HashSet<uint>(
						CaptureFanoutMath.SelectDistinctTargets(orderedIds, inFlight, targets.Count));
					targets = targets.Where(t => t.Actor != null && keep.Contains(t.Actor.ActorID)).ToList();
				}
			}

			var topDesc = targets.Count > 0
				? $"{targets[0].Actor?.Info.Name}@{targets[0].Location} action={targets[0].Action} score={targets[0].Score}"
				: "<none>";
			Log.Write("debug",
				$"[exp-capture] poimap-scan player={player.PlayerName} idleCapturers={idleCapturers.Length} targets={targets.Count} top={topDesc} tick={world.WorldTick}");

			foreach (var poi in targets)
			{
				if (available.Count == 0)
					break;

				var target = poi.Actor;
				if (target == null || target.IsDead || !target.IsInWorld)
					continue;

				// Respect the module's own targeting relationships (PoiMap already
				// excludes our own POIs, but a captured-since-scan target may now be
				// ours) — the CanTarget check below is the authoritative filter.
				var cm = target.TraitOrDefault<CaptureManager>();
				if (cm == null)
					continue;

				var bestIndex = -1;
				var bestDistSq = long.MaxValue;
				for (var i = 0; i < available.Count; i++)
				{
					if (!available[i].Trait.CanTarget(cm))
						continue;

					var distSq = (available[i].Actor.CenterPosition - target.CenterPosition).LengthSquared;
					if (distSq < bestDistSq)
					{
						bestDistSq = distSq;
						bestIndex = i;
					}
				}

				if (bestIndex < 0)
					continue;

				IssueCaptureOrder(bot, available[bestIndex].Actor, target, useGuard, escortsRecruitedThisTick, poi.Score, poi.DistanceCells);
				available.RemoveAt(bestIndex);
			}

			// Whatever is left got no order above — the state that had no owner. Give it one.
			StageIdleCapturersReserve(bot, available, targets.Count);
		}

		/// <summary>Muster the capturers this scan could not dispatch at a reserve anchor behind the believed
		/// frontier, rather than discarding them. See StageIdleCapturers for why this module has to own it.</summary>
		void StageIdleCapturersReserve(IBot bot, List<TraitPair<CaptureManager>> undispatched, int targetCount)
		{
			if (!Info.StageIdleCapturers || undispatched.Count == 0)
				return;

			var anchor = ResolveReserveAnchor();
			if (anchor == null)
				return;

			// Prune the memory to units still ours, so a dead/consumed capturer can't pin an entry.
			if (reserveCells.Count > 0)
			{
				var stale = reserveCells.Keys.Where(a => a.IsDead || !a.IsInWorld || a.Owner != player).ToList();
				foreach (var a in stale)
					reserveCells.Remove(a);
			}

			// Bound the fan-out so the widest ring stays STRICTLY inside the standoff — a reserve slot must never
			// sit forward of the frontier the anchor descent already cleared of believed danger, because
			// SpreadCell is not danger-guarded per cell. Same invariant, and same arithmetic, as StageFreePool.
			var standoffMapCells = Info.ReserveStandoffCells * reserveControlField.Info.CellSize;
			var maxRings = ForwardStagingMath.MaxSpreadRings(standoffMapCells, Info.ReserveSpreadStepCells);

			// ActorID order so the issue sequence cannot depend on the pool's composition.
			foreach (var tp in undispatched.OrderBy(p => p.Actor.ActorID))
			{
				var unit = tp.Actor;

				// Slot by a STABLE per-unit key rather than list position, so a capturer leaving the reserve
				// (dispatched, or consumed by a capture) does not re-slot everyone else and re-issue their moves.
				// Without any fan-out at all every reserved capturer is sent to the identical cell and clogs it.
				var slot = ForwardStagingMath.StableSlot(unit.ActorID, maxRings);
				var (cx, cy) = ForwardStagingMath.SpreadCell(anchor.Value.X, anchor.Value.Y, slot,
					Info.ReserveSpreadStepCells, (mx, my) => world.Map.Contains(new CPos(mx, my)));
				var target = new CPos(cx, cy);

				// Re-issue only when the destination CHANGED (newly reserved, or the anchor advanced with the
				// front), so a capturer already walking up keeps its order instead of restarting every scan.
				if (reserveCells.TryGetValue(unit, out var prev) && prev == target)
					continue;

				if (unit.Location == target)
				{
					reserveCells[unit] = target;
					continue;
				}

				// Only remember the slot if the order was accepted; otherwise the dedup above suppresses the
				// re-issue and the capturer never walks up until the anchor drifts past the hysteresis.
				if (!bot.QueueOrder(new Order("Move", unit, Target.FromCell(world, target), false)))
					continue;

				reserveCells[unit] = target;

				// Bounded by definition: one line per capturer per anchor CHANGE, not per scan. This is the
				// line that answers "did the unit that stopped garrisoning get a purpose instead?" from
				// ordinary play — reason distinguishes "nothing to capture" from "nothing I can capture".
				Log.Write("debug",
					$"[exp-capture] reserve player={player.PlayerName} unit={unit.Info.Name}#{unit.ActorID} " +
					$"from={unit.Location} to={target} anchor={anchor.Value} " +
					$"reason={(targetCount == 0 ? "no-targets" : "no-cantarget")} " +
					$"targets={targetCount} tick={world.WorldTick}");
			}
		}

		/// <summary>The reserve muster cell: steepest descent on the believed frontier-distance gradient from our
		/// own SR, halting ReserveStandoffCells short of the line and never stepping into a believed anti-ground
		/// danger envelope. Same primitive as PoiOffensiveBotModule's forward staging, seeded and tuned for an
		/// unarmed consumable. Returns the SR cell itself when the field is flat (no believed contact — the
		/// opening), which is the intended "hold at the beachhead". Null only when there is no field or no SR,
		/// in which case we have nothing honest to say and issue nothing.
		///
		/// <para>NOTE the HasField conjunct below is the opposite polarity to GarrisonBotModule.ThreatGateActive,
		/// which deliberately does NOT test it. Both are correct because both fail CLOSED toward doing nothing,
		/// but they are the same question answered oppositely and the difference is load-bearing: there, a
		/// not-yet-built field must read "no believed threat" so the garrison gate SUPPRESSES on tick 1; here, a
		/// not-yet-built field means the descent has no gradient to walk, so there is no honest destination and
		/// the muster stays silent. The consequence is a real coupling worth naming — in the opening window this
		/// module issues nothing, so a capturer with no target is idle and unclaimed, and it is the garrison gate
		/// that keeps that state harmless. Weakening that gate re-opens this hole, not only its own.</para></summary>
		CPos? ResolveReserveAnchor()
		{
			if (!reserveFieldsResolved)
			{
				reserveControlField = world.WorldActor.TraitOrDefault<ControlField>();
				reserveDangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();
				reserveFieldsResolved = true;
			}

			if (reserveControlField == null || !reserveControlField.HasField(player))
			{
				lastReserveAnchor = null;
				return null;
			}

			var sr = FindOwnSupplyRoute();
			if (sr == null)
			{
				lastReserveAnchor = null;
				return null;
			}

			var (sgx, sgy) = reserveControlField.MapCellToGridCell(sr.Location);
			var (agx, agy) = ForwardStagingMath.StagingCell(sgx, sgy,
				Info.ReserveStandoffCells,
				Info.ReserveDangerSafeUnits <= 0 || reserveDangerField == null
					? Info.ReserveDangerSafeUnits
					: reserveDangerField.GroundDangerUnitsToField(Info.ReserveDangerSafeUnits),
				Info.ReserveMaxDescentSteps,
				(gx, gy) => reserveControlField.FrontierDistanceAt(player, gx, gy),
				(gx, gy) => reserveDangerField != null
					? reserveDangerField.GroundDanger(player, reserveControlField.GridCellToMapCell(gx, gy)) : 0,
				(gx, gy) => gx >= 0 && gx < reserveControlField.GridWidth
					&& gy >= 0 && gy < reserveControlField.GridHeight);

			var candidate = reserveControlField.GridCellToMapCell(agx, agy);

			// Hold the adopted anchor until the new one has moved far enough to be worth re-laying the reserve for.
			// Compared in MAP space, matching ResolveStagingAnchor — the threshold is expressed in map cells, and
			// unlike that method's no-move test this is a distance comparison, so the grid-centre parity trap the
			// forward-muster resolver documents does not apply here.
			if (lastReserveAnchor.HasValue
				&& !ForwardStagingMath.AnchorShifted(lastReserveAnchor.Value.X, lastReserveAnchor.Value.Y,
					candidate.X, candidate.Y, Info.ReserveHysteresisCells))
				return lastReserveAnchor;

			lastReserveAnchor = candidate;
			return candidate;
		}

		// (d) Actor IDs of capture targets currently claimed by an in-flight committed capturer. Iterates the
		// synced capturer index; a committed capturer's objective "capture:<id>" contributes its target id. Only
		// membership of the returned set is queried by the fan-out filter, so no hash enumeration order feeds a
		// sim decision. Empty when no guard / no committed capturer ⇒ the fan-out filter is a no-op.
		HashSet<uint> BuildInFlightCaptureTargetIds()
		{
			var ids = new HashSet<uint>();
			if (goalGuard == null)
				return ids;

			foreach (var tecn in capturingActors.Actors)
			{
				if (!goalGuard.Ledger.IsCommitted(tecn, world.WorldTick))
					continue;

				if (goalGuard.Ledger.TryGetObjective(tecn, out var objective)
					&& TryParseCaptureTargetId(objective, out var id))
					ids.Add(id);
			}

			return ids;
		}

		// Issue a capture order + record the commitment so the TECN is not
		// re-ordered while it walks in (the anti-thrash gate), then recruit escort.
		void IssueCaptureOrder(IBot bot, Actor capturer, Actor target, bool useGuard, HashSet<Actor> escortsRecruitedThisTick, long score, int distanceFromSRCells)
		{
			// TECN-first ferrying: for a DISTANT target, try to hand the capturer a mounted ride.
			// When it succeeds the transport module owns the movement AND re-issues CaptureActor on
			// unload, so we skip the on-foot order here. Ledger commitment + escort still fire so
			// deconfliction and support are identical to the on-foot path.
			// The ferry attempt and this on-foot order are ALTERNATIVES, not a chain — nothing may couple
			// them. Both are Protected, and CaptureActor is outside the suppressible whitelist entirely, so
			// neither can be refused; the check keeps the claim below honest regardless, because committing a
			// capturer at RankMission for a unit that received no order would have predicate (a) defend a
			// phantom claim with the highest rank in the table.
			var ferried = Info.UseTransportForDistantCaptures && TryFerryCapture(bot, capturer, target);
			if (!ferried && !bot.QueueOrder(new Order("CaptureActor", capturer, Target.FromActor(target), true)))
				return;

			if (useGuard)
				goalGuard.Ledger.Commit(capturer, CaptureObjectiveKey(target), world.WorldTick, goalGuard.DefaultCommitmentTicks);
			else
				activeCapturers.Add(capturer);

			// Recruit escort — fire-and-forget; if no escort available, capture proceeds alone.
			DispatchEscort(bot, capturer, target, escortsRecruitedThisTick, distanceFromSRCells);

			Log.Write("debug",
				$"[exp-capture] issue player={player.PlayerName} actor={capturer.Info.Name}@{capturer.Location} → {target.Info.Name}@{target.Location} score={score} ferried={ferried} tick={world.WorldTick}");

			AIUtils.BotDebug("AI ({0}): exp-capture — {1} → {2} (score={3}, ferried={4})",
				player.ClientIndex, capturer.Info.Name, target.Info.Name, score, ferried);
		}

		// Request a mounted ride for a distant capture. Returns true only when a carrier was
		// reserved (the transport module then drives + re-issues CaptureActor on unload).
		bool TryFerryCapture(IBot bot, Actor capturer, Actor target)
		{
			var distCells = (target.CenterPosition - capturer.CenterPosition).Length / 1024;
			if (distCells < Info.TransportCaptureMinDistanceCells)
				return false;

			if (!transportModuleResolved)
			{
				transportModule = player.PlayerActor.TraitsImplementing<MountedTransportBotModule>()
					.FirstOrDefault(m => !m.IsTraitDisabled);
				transportModuleResolved = true;
			}

			return transportModule != null && transportModule.TryReserveCaptureFerry(bot, capturer, target);
		}

		// Objective key stored in the goal-guard ledger. Namespaced string form
		// ("capture:<actorId>") — greppable in logs and v3-portable. The actor id
		// lets us resolve the target back to check whether the capture is done.
		static string CaptureObjectiveKey(Actor target) => "capture:" + target.ActorID;

		// Phase 2 commit-on-order audit: disjoint ledger keys for the SUPPORT units (escorts / structure
		// defenders), kept distinct from the capturer's "capture:" grammar AND from PoiGarrison's "defend:"
		// so every commitment is attributable to exactly one executor (audit requirement (d)).
		static string CaptureEscortObjectiveKey(Actor target) => "capture-escort:" + target.ActorID;
		static string CaptureDefendObjectiveKey(Actor structure) => "capture-defend:" + structure.ActorID;

		static bool TryParseCaptureTargetId(string objective, out uint id)
		{
			id = 0;
			if (string.IsNullOrEmpty(objective))
				return false;
			var colon = objective.IndexOf(':');
			return colon >= 0 && uint.TryParse(objective.AsSpan(colon + 1), out id);
		}

		Actor FindOwnSupplyRoute()
		{
			return world.Actors.FirstOrDefault(a =>
				a.Owner == player && !a.IsDead && a.IsInWorld
				&& Info.SupplyRouteTypes.Contains(a.Info.Name));
		}

		// Release commitments that are done or stale so the TECN re-enters the pool:
		//   * TECN dead / no longer ours              → Prune's keep predicate drops it
		//   * commitment expired (walked its window)  → Prune drops it
		//   * target captured (now ours) / gone       → explicit Release below
		// Everything else stays committed → NOT re-ordered this scan (anti-thrash).
		// Returns the set of TECNs issued a retreat-home order THIS scan (empty unless
		// RetreatCapturerWhenDone) so the caller can skip re-selecting them for a fresh capture the
		// same scan — a just-released TECN still reads IsIdle, and a CaptureActor queued behind the
		// retreat Move would drag it back out on a wasteful round trip.
		HashSet<Actor> ReconcileGuardCommitments(IBot bot)
		{
			var tick = world.WorldTick;
			var retreated = new HashSet<Actor>();

			// M-3 (expired): Prune drops expired commitments but doesn't report which,
			// so snapshot the about-to-expire ones for live capturers first. A live
			// tracked capturer that holds an objective yet reads !IsCommitted is expired
			// (dead/unowned capturers are gone from the index and covered by M-1).
			// reason=expired confirms F-2 — the commitment window was too short.
			foreach (var tecn in capturingActors.Actors)
			{
				if (goalGuard.Ledger.TryGetObjective(tecn, out var expiredObj)
					&& !goalGuard.Ledger.IsCommitted(tecn, tick))
					Log.Write("debug",
						$"[exp-capture] commitment-released player={player.PlayerName} actor={tecn.Info.Name} objective={expiredObj} reason=expired tick={tick}");
			}

			goalGuard.Ledger.Prune(tick, a => !a.IsDead && a.IsInWorld && a.Owner == player);

			foreach (var tecn in capturingActors.Actors)
			{
				if (!goalGuard.Ledger.TryGetObjective(tecn, out var objective))
					continue;

				var target = TryParseCaptureTargetId(objective, out var id) ? world.GetActorById(id) : null;
				var stillCapturable = target != null && !target.IsDead && target.IsInWorld
					&& Info.CapturableRelationships.HasRelationship(player.RelationshipWith(target.Owner));

				// target.Owner == player after we capture → relationship no longer
				// Enemy/Neutral → stillCapturable false → commitment released.
				// M-3 (captured/gone): target still exists but no longer capturable
				// means we captured it; a missing/dead target means it was destroyed.
				if (!stillCapturable)
				{
					var reason = target != null && !target.IsDead && target.IsInWorld ? "captured" : "gone";
					Log.Write("debug",
						$"[exp-capture] commitment-released player={player.PlayerName} actor={tecn.Info.Name} objective={objective} reason={reason} tick={tick}");
					goalGuard.Ledger.Release(tecn);

					// Retreat-when-done (orderless-at-hostile-location bug class): the capturer's job is over but it
					// is now parked at the (just-captured or destroyed) target, deep in contested territory, and no
					// combat free pool will re-collect a CaptureSpecialist. Send it home to our SR. queued=false so
					// it cancels any lingering (now-invalid) CaptureActor activity. A consumed capturer fails the
					// alive/in-world guard and is skipped. Inert (byte-identical) when the flag is off.
					if (Info.RetreatCapturerWhenDone && !tecn.IsDead && tecn.IsInWorld && tecn.Owner == player)
					{
						var ownSR = FindOwnSupplyRoute();
						if (ownSR != null)
						{
							// Unmarked ⇒ Protected. `retreated` is a one-shot latch, so a dropped order would
							// park the TECN at the captured target for good; the return check stays as the
							// standing convention even though this order cannot currently be refused.
							if (!bot.QueueOrder(new Order("Move", tecn, Target.FromCell(world, ownSR.Location), false)))
								continue;

							retreated.Add(tecn);
							AIUtils.BotDebug("AI ({0}): capture-coordinator — {1} done ({2}), retreating to SR {3}",
								player.ClientIndex, tecn.Info.Name, reason, ownSR.Location);
						}
					}
				}
			}

			return retreated;
		}

		long ScoreTarget(Actor capturer, Actor target)
		{
			// Income value — flat lookup, baseline from YAML.
			var income = GetIncomeWeight(target);

			// Distance decay. distFactor in [10, 1000]; closer = higher.
			var distCells = Math.Max(1, (target.CenterPosition - capturer.CenterPosition).Length / 1024);
			var halfLife = Math.Max(1, Info.DistanceHalfLifeCells);
			// distFactor = halfLife * 100 / (halfLife + distCells)  →  at distCells=halfLife: 50; at distCells=0: 100.
			var distFactor = halfLife * 100 / (halfLife + distCells);

			// Safety — count enemies near the target.
			var safetyRadius = WDist.FromCells(Info.SafetyEnemyScanRadiusCells);
			var nearbyEnemies = world.FindActorsInCircle(target.CenterPosition, safetyRadius)
				.Count(a => !a.IsDead && a.IsInWorld
					&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& a.Info.HasTraitInfo<ITargetableInfo>());

			int safetyFactor;
			if (nearbyEnemies == 0)
				safetyFactor = Info.SafetyMultiplierSafe;
			else if (nearbyEnemies <= 2)
				safetyFactor = Info.SafetyMultiplierMild;
			else
				safetyFactor = Info.SafetyMultiplierHostile;

			// Combined long score keeps headroom for big maps.
			return (long)income * distFactor * safetyFactor;
		}

		int GetIncomeWeight(Actor target)
		{
			var name = target.Info.Name.ToLowerInvariant();
			if (Info.IncomeWeights.TryGetValue(name, out var v))
				return v;

			// Wave A supply-depot tier: a rearm depot earns no cash, so without this it shares DefaultIncomeWeight
			// with the worthless civilian structures. Checked only AFTER an explicit IncomeWeights entry, so YAML
			// tuning always wins. Off ⇒ falls through to the default exactly as before.
			if (Info.CaptureSupplyDepots && Info.SupplyDepotActorTypes.Contains(name))
				return Info.SupplyDepotIncomeWeight;

			return Info.DefaultIncomeWeight;
		}

		// Eight cardinal+diagonal unit steps — the control-ring sample directions (fixed order, zero RNG).
		static readonly (int Dx, int Dy)[] RingDirections =
		{
			(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
		};

		// Contest-aware support (Option A): does the neighbourhood of `cell` read CONTESTED from the fog-legal
		// believed fields? Two signals, either sufficient: (1) believed anti-ground danger over the cell exceeds
		// ContestedDangerUnits (a believed weapon envelope actively reaches it); (2) the control-field RING one
		// step past the anchor footprint reads believed-ENEMY (we think the enemy holds ground next to the derrick).
		// The ring — not the target's own cell — is sampled because an owned/enemy site anchor floors its own grid
		// cell (Stage-C), so the own cell is uninformative. ENEMY only (not gray/Contested) is deliberate: a derrick
		// in unheld no-man's-land is NOT flagged contested, so the larger escort / earlier defense stay SURGICAL to
		// genuinely disputed derricks (the measured deficit is on the both-capture seeds) rather than inflating
		// support on every open-ground capture and thinning the offense (the design's combat-net-swing guardrail).
		// Returns false immediately when the lever is off, so a config omitting it never resolves a field. NOTE
		// (b8d2e601, 2026-08-02): @stable now sets ContestAwareSupportEnabled (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so this early-out
		// does NOT fire on @stable — it resolves the fields and is byte-divergent from the old frozen behaviour.
		// Pure reads, zero RNG.
		// ACCEPTED GAP: an unescorted WEAPONLESS enemy probe (a lone engineer) trips neither channel — it stamps no
		// danger kernel and doesn't paint control presence (that needs an armed unit) — so a derrick can be re-taken
		// by a light probe without the larger escort / earlier defense engaging. Accepted; the 4.D ownership
		// timeseries reveals derricks lost this way, which would motivate a follow-up lever if it shows up.
		bool IsContestedNeighbourhood(CPos cell)
		{
			if (!Info.ContestAwareSupportEnabled)
				return false;

			if (!contestFieldsResolved)
			{
				contestControlField = world.WorldActor.TraitOrDefault<ControlField>();
				contestDangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();
				contestFieldsResolved = true;
			}

			if (contestDangerField != null
				&& contestDangerField.GroundDanger(player, cell)
					> contestDangerField.GroundDangerUnitsToField(Info.ContestedDangerUnits))
				return true;

			if (contestControlField != null && contestControlField.HasField(player))
			{
				var (gx, gy) = contestControlField.MapCellToGridCell(cell);
				var r = contestControlField.Info.AnchorRadiusCells + 1;
				foreach (var (dx, dy) in RingDirections)
					if (contestControlField.OwnerAt(player, gx + dx * r, gy + dy * r) == ControlOwner.Enemy)
						return true;
			}

			return false;
		}

		void DispatchEscort(IBot bot, Actor capturer, Actor target, HashSet<Actor> alreadyRecruited, int distanceFromSRCells)
		{
			// Contest-aware sizing (Option A): a contested target gets ContestedEscortSize (larger) instead of the
			// flat EscortSize, so a derrick under believed enemy pressure is not walked in with the same two guards
			// as an uncontested one. When the lever is off, contested is always false → wantEscort == Info.EscortSize
			// → byte-identical to today.
			var contested = IsContestedNeighbourhood(target.Location);
			var wantEscort = contested ? Info.ContestedEscortSize : Info.EscortSize;

			// Escort right-sizing (income lever): scale the party DOWN by believed threat at the target. Applied ONLY
			// to targets not already flagged contested — a contested derrick keeps its (possibly larger) escort, so
			// this composes with ContestAwareSupport rather than undoing it. NONE ⇒ the technician goes alone and NO
			// combat units are reserved (they stay idle for the offense / other captures); LIGHT ⇒ min with the small
			// escort so the lever only ever reduces. Lever off / contested / a missing field all keep the value above.
			// The tier->count mapping is the REDUCTION-ONLY guarantee (None->0, Light->min(want, LightEscortSize),
			// Full->want) and is extracted to EscortSizingMath.ResolveEscortCount so an NUnit pin holds the invariant
			// "the lever never RAISES an escort" — the load-bearing Math.Min lives there. Byte-identical to the
			// inline mapping it replaces.
			var tier = EscortSizingMath.EscortTier.Full;
			if (Info.EscortTierSizingEnabled && !contested)
			{
				tier = ResolveEscortTier(target.Location, distanceFromSRCells);
				wantEscort = EscortSizingMath.ResolveEscortCount(wantEscort, tier, Info.LightEscortSize);
			}

			if (wantEscort <= 0)
				return;

			var recruits = FindIdleSupportersNear(capturer.CenterPosition, wantEscort, alreadyRecruited);
			if (recruits.Length == 0)
				return;

			if (!bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, target.Location), false, groupedActors: recruits)))
				return;

			foreach (var r in recruits)
				alreadyRecruited.Add(r);

			// Phase 2 commit-on-order (§4): stake each escort in the shared ledger so no other writer
			// (offense's BuildFreePool, LayeredDefence) can poach it mid-approach. Gate mirrors the byte-identity
			// contract — off ⇒ no commit ⇒ frozen path. Recruits already came from the ledger-checked free pool.
			if (CommitOnOrderMath.ShouldCommit(Info.CommitSupportUnits, goalGuard != null && !goalGuard.IsTraitDisabled))
			{
				var key = CaptureEscortObjectiveKey(target);
				foreach (var r in recruits)
					goalGuard.Ledger.Commit(r, key, world.WorldTick, goalGuard.DefaultCommitmentTicks);
			}

			AIUtils.BotDebug("AI ({0}): exp-capture — escort dispatched ({1} units → {2}, contested={3}, tier={4})",
				player.ClientIndex, recruits.Length, target.Info.Name, contested, tier);
		}

		// Escort right-sizing (EscortTierSizingEnabled): bucket a capture target into an escort tier from the fog-legal
		// believed fields. The ring-averaged control score is sampled one cell PAST the anchor footprint (the target's
		// own cell is anchor-floored — Stage-C — so it is uninformative; same reason the Stage-F balance-of-power read
		// samples the ring). Requires BOTH fields: a missing ControlField/DangerFieldLayer (or no control field for this
		// player yet) returns Full so the capture keeps its full escort rather than gambling a lone technician on an
		// unverifiable read. Resolved lazily and only when the lever is on, so a config leaving it off never touches
		// them. NOTE (b8d2e601, 2026-08-02): @stable now sets EscortTierSizingEnabled (ai.yaml CaptureCoordinatorBotModule@stable.tecn), so @stable
		// resolves these fields and tiers its escorts here too.
		EscortSizingMath.EscortTier ResolveEscortTier(CPos cell, int distanceFromSRCells)
		{
			if (!tierFieldsResolved)
			{
				tierControlField = world.WorldActor.TraitOrDefault<ControlField>();
				tierDangerField = world.WorldActor.TraitOrDefault<DangerFieldLayer>();
				tierFieldsResolved = true;
			}

			if (tierControlField == null || !tierControlField.HasField(player) || tierDangerField == null)
				return EscortSizingMath.EscortTier.Full;

			var (gx, gy) = tierControlField.MapCellToGridCell(cell);
			var r = tierControlField.Info.AnchorRadiusCells + 1;
			long sum = 0;
			foreach (var (dx, dy) in RingDirections)
				sum += tierControlField.ScoreAt(player, gx + dx * r, gy + dy * r);
			var ringControl = (int)(sum / RingDirections.Length);

			var groundDanger = tierDangerField.GroundDanger(player, cell);

			return EscortSizingMath.Resolve(ringControl, groundDanger, distanceFromSRCells,
				Info.SafeControlScoreThreshold, tierDangerField.GroundDangerUnitsToField(Info.SafeDangerUnits),
				Info.SafeMaxDistanceFromSRCells,
				tierControlField.Info.GrayBand, tierDangerField.GroundDangerUnitsToField(Info.ContestedDangerUnits));
		}

		// ============================================================
		// DEFENSE PASS
		// ============================================================

		void QueueDefenseOrders(IBot bot)
		{
			// Own capturables (must have CaptureManager AND a relevant income role).
			var owned = world.Actors
				.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld
					&& (Info.CapturableActorTypes.Count == 0
						|| Info.CapturableActorTypes.Contains(a.Info.Name.ToLowerInvariant()))
					&& a.Info.HasTraitInfo<CaptureManagerInfo>())
				.ToList();

			if (owned.Count == 0)
				return;

			var enemyRadius = WDist.FromCells(Info.DefenseEnemyScanRadiusCells);
			var friendlyRadius = WDist.FromCells(Info.DefenseFriendlyScanRadiusCells);

			foreach (var structure in owned)
			{
				var enemies = world.FindActorsInCircle(structure.CenterPosition, enemyRadius)
					.Where(a => !a.IsDead && a.IsInWorld
						&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy)
					.ToList();

				if (enemies.Count == 0)
					continue;

				// Contest-aware pre-summon (Option A): a derrick whose neighbourhood reads contested triggers
				// defenders at the lower ContestedDefenseEnemyValueThreshold, so a light re-capture probe below the
				// normal $200 gate is met before it flips the structure. Lever off ⇒ contested false ⇒ the threshold
				// is Info.DefenseEnemyValueThreshold ⇒ byte-identical to today.
				var contested = IsContestedNeighbourhood(structure.Location);
				var enemyValueThreshold = contested
					? Info.ContestedDefenseEnemyValueThreshold : Info.DefenseEnemyValueThreshold;

				var enemyValue = enemies.Sum(a => a.GetSellValue());
				if (enemyValue < enemyValueThreshold)
					continue;

				var friendlies = world.FindActorsInCircle(structure.CenterPosition, friendlyRadius)
					.Where(a => !a.IsDead && a.IsInWorld
						&& a.Owner == player
						&& !CapturerNames.Contains(a.Info.Name))
					.ToList();
				var friendlyValue = friendlies.Sum(a => a.GetSellValue());

				if (friendlyValue >= enemyValue)
					continue;

				var defenders = FindIdleSupportersNear(structure.CenterPosition, Info.DefenseSummonCount);
				if (defenders.Length == 0)
					continue;

				// A dropped order must not leave a ledger claim (or a bespoke booking) behind: that would
				// reserve a unit nobody ever moved, and predicate (a) would then defend the phantom claim
				// against every other module.
				if (!bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, structure.Location), false, groupedActors: defenders)))
					continue;

				foreach (var d in defenders)
					defenderBookings[d] = world.WorldTick;

				// Phase 2 commit-on-order (§4): defenderBookings is a BESPOKE lock only this module honours
				// (FindIdleSupportersNear), invisible to offense/LayeredDefence — so a summoned defender is
				// free for them to steal. Commit it in the SHARED ledger too (disjoint capture-defend:<id> key)
				// so every writer defers. Off ⇒ no commit ⇒ frozen path (defenderBookings unchanged either way).
				if (CommitOnOrderMath.ShouldCommit(Info.CommitSupportUnits, goalGuard != null && !goalGuard.IsTraitDisabled))
				{
					var key = CaptureDefendObjectiveKey(structure);
					foreach (var d in defenders)
						goalGuard.Ledger.Commit(d, key, world.WorldTick, goalGuard.DefaultCommitmentTicks);
				}

				AIUtils.BotDebug("AI ({0}): exp-capture — defense summoned ({1} units → {2}, enemyVal={3})",
					player.ClientIndex, defenders.Length, structure.Info.Name, enemyValue);
			}
		}

		// ============================================================
		// SHARED HELPERS
		// ============================================================

		Actor[] FindIdleSupportersNear(WPos around, int wantCount, HashSet<Actor> exclude = null)
		{
			if (wantCount <= 0)
				return Array.Empty<Actor>();

			var recruitRadius = WDist.FromCells(Info.SupportRecruitRadiusCells);

			IEnumerable<Actor> pool = world.FindActorsInCircle(around, recruitRadius)
				.Where(a => !a.IsDead && a.IsInWorld
					&& a.Owner == player
					&& a.IsIdle
					&& !defenderBookings.ContainsKey(a)
					&& (exclude == null || !exclude.Contains(a))
					&& !CapturerNames.Contains(a.Info.Name)
					// Shared unit-claim (§5.6): never poach a unit the offense module
					// (or any module) has committed in the goal-guard ledger.
					&& (goalGuard == null || !goalGuard.Ledger.IsCommitted(a, world.WorldTick))
					&& a.Info.HasTraitInfo<IPositionableInfo>()
					&& a.Info.HasTraitInfo<AttackBaseInfo>()
					// A starving escort is an escort in name only. Inert at 0. Last in the chain so the
					// withhold log only fires for units that were otherwise recruitable.
					&& !ammoGate.Withhold(a, Info.StarvingRecruitThresholdPerMille));

			if (Info.SupportingUnitTypes.Count > 0)
				pool = pool.Where(a => Info.SupportingUnitTypes.Contains(a.Info.Name));

			return pool
				.OrderBy(a => (a.CenterPosition - around).LengthSquared)
				.Take(wantCount)
				.ToArray();
		}

		IEnumerable<Actor> GetVisibleActorsBelongingToPlayer(Player owner)
		{
			foreach (var actor in GetActorsThatCanBeOrderedByPlayer(owner))
				if (actor.CanBeViewedByPlayer(player))
					yield return actor;
		}

		IEnumerable<Actor> GetActorsThatCanBeOrderedByPlayer(Player owner)
		{
			foreach (var actor in world.Actors)
				if (actor.Owner == owner && !actor.IsDead && actor.IsInWorld)
					yield return actor;
		}

		// Player-actor INotifyKilled (Health.notifyKilledPlayer) fires with `self` =
		// the dying OWNED actor. When one of OUR capturers dies we (1) reset the scan
		// countdown so the next BotTick re-dispatches immediately instead of waiting up
		// to ScanInterval ticks (F-3), and (2) emit the M-1 death marker so failing
		// runs are legible (the commitment is still in the ledger here — Prune hasn't
		// run yet — so we can report the objective it was pursuing).
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (IsTraitDisabled || world.Type == WorldType.Editor)
				return;

			if (self.Owner != player || !CapturerNames.Contains(self.Info.Name))
				return;

			var committed = goalGuard != null && goalGuard.Ledger.IsCommitted(self, world.WorldTick);
			var objective = goalGuard != null && goalGuard.Ledger.TryGetObjective(self, out var obj) ? obj : "<none>";
			Log.Write("debug",
				$"[exp-capture] tecn-killed player={player.PlayerName} actor={self.Info.Name}@{self.Location} committed={committed} objective={objective} tick={world.WorldTick}");

			captureScanCountdown = 0;
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			capturingActors.Dispose();
		}
	}
}
