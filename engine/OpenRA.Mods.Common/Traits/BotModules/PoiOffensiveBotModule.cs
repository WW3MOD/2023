#region Copyright & License Information
/*
 * WW3MOD PoiOffensiveBotModule — experimental AI, POI-strategy Phase 3 (the headline).
 *
 * Replaces the implicit DEATH-BALL with SCORE-FLOATING ATTACK AXES. Where the
 * fixed-wing SquadManager used to scoop the whole ground pool into one squad and
 * march it at the enemy, this module reads PoiMap's offensive ranking and SPLITS
 * the general ground army across the top-scored enemy objectives — enemy income
 * structures (Attack), the enemy Supply Route circle (Pressure), and — per
 * decision #3 — the enemy base competing on the SAME score, with NO privileged
 * base-beeline axis. If a contested derrick outscores the enemy SR, the derrick
 * pulls the units; the base may get no axis at all. Early passive games are the
 * accepted cost of a genuinely decision-making foundation.
 *
 * PIPELINE (scoring -> assignment -> execution):
 *   1. PoiMap.GetOffensiveTargets(player) — value x distance x threat, best first.
 *   2. DesiredAxisCount + AllocateProportional (pure PoiOffenseMath) — how many
 *      axes and how many units each, by score, with a minimum viable axis size.
 *   3. Reconcile against the live axes (sticky, hysteresis-guarded) and issue one
 *      AttackMove per axis, committing each unit through the SHARED goal-guard
 *      ledger so capture / defense / other axes never steal it.
 *
 * UNIT-CLAIM (the §5.6 shared claim, minimal version): every module that owns
 * units consults ONE per-unit ledger (PoiGoalGuard.Ledger). CaptureCoordinator
 * commits TECNs ("capture:<id>"); this module commits combat units
 * ("offense:<targetId>"); a unit committed to anyone is invisible to the others.
 * The experimental fixed-wing SquadManager is set IgnoreGroundUnits so it no longer owns
 * the ground pool at all — this module does.
 *
 * DESIGN INTENT (v3-portable): all decision MATH lives in the pure PoiOffenseMath
 * class (unit-tested in PoiOffenseTest) so it ports verbatim into a future v3
 * brain; only the assignment plumbing (this IBotTick module) is engine-specific.
 * Constants are Info fields so behaviour is YAML-tunable without a rebuild.
 *
 * Gated enable-ai-experimental in ai.yaml — Normal / Rush / Turtle never instantiate it.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits.BotModules.Squads;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: splits the general ground army across PoiMap-scored offensive axes",
		"(enemy income = Attack, enemy Supply Route = Pressure, enemy base competes on score).",
		"Replaces the fixed-wing SquadManager's ground death-ball. Uses the shared PoiGoalGuard",
		"ledger as the unit-claim so capture/defense/offense never fight over units. Gate enable-ai-experimental.")]
	public class PoiOffensiveBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between offense re-evaluations. Slow cadence + the per-unit commitment TTL give",
			"hysteresis so axes don't re-path every scan.")]
		public readonly int ReevaluateInterval = 100;

		[Desc("Rough army-to-axis ratio: one attack axis per this many offensive units (before caps).")]
		public readonly int UnitsPerAxis = 8;

		[Desc("Minimum units for a viable axis — don't dribble single units at objectives.")]
		public readonly int MinAxisSize = 3;

		[Desc("Hard cap on concurrent attack axes regardless of army size.")]
		public readonly int MaxAxes = 4;

		[Desc("EXPERIMENTAL (early-econ behaviour 3): while the match is young use EarlyUnitsPerAxis /",
			"EarlyMinAxisSize instead of UnitsPerAxis / MinAxisSize, so the few early units DISPERSE into",
			"several small packets from the beachhead rather than massing one armada at the Supply Route.",
			"OFF by default so the frozen @stable twin and every legacy profile keep their axis sizing",
			"byte-identical; only PoiOffensiveBotModule@experimental turns it on. Pure gate on the synced tick.")]
		public readonly bool EarlyGameSpread = false;

		[Desc("Duration (sim ticks from game start) of the early-spread window. After it, normal axis sizing resumes.")]
		public readonly int EarlyGameDurationTicks = 4500;

		[Desc("UnitsPerAxis used while EarlyGameSpread is active (smaller ⇒ axes form sooner, more small packets).")]
		public readonly int EarlyUnitsPerAxis = 3;

		[Desc("MinAxisSize used while EarlyGameSpread is active (smaller ⇒ allow smaller early packets).")]
		public readonly int EarlyMinAxisSize = 2;

		[Desc("Commitment lifetime (ticks) for a unit assigned to an axis. While committed the unit is",
			"left on its axis and is invisible to capture/defense/other axes. Refreshed each re-eval",
			"a unit stays on its axis, so it must exceed ReevaluateInterval.")]
		public readonly int AxisCommitmentTicks = 250;

		[Desc("Hysteresis: an existing axis is only abandoned for a fresh POI when that POI outscores",
			"it by more than this percent. Keeps axes sticky against score jitter.")]
		public readonly int ReassignScoreThresholdPct = 30;

		[Desc("Re-issue an axis AttackMove only if the target cell moved by at least this many cells",
			"(or the axis unit set changed). Prevents order spam on a stationary objective.")]
		public readonly int RepathThresholdCells = 3;

		[Desc("Actor types NEVER pulled into an offensive axis (capturers, supply trucks, IFV carriers —",
			"owned by CaptureCoordinator / SupplyFollower / MountedTransport). Aircraft are excluded",
			"automatically by trait.")]
		public readonly HashSet<string> ExcludeUnitTypes = new HashSet<string>();

		[Desc("Skip units whose AmmoPool(s) are ALL empty (evacuating / out-of-ammo). An empty unit",
			"re-tasked onto an axis has its RotateToEdge evac cancelled by the AttackMove and is sent",
			"at the enemy with nothing to shoot. OFF by default so the frozen Stable/Normal controls stay",
			"byte-identical (they keep pulling every unit); only PoiOffensiveBotModule@experimental turns it",
			"on. Mirrors LayeredDefenceBotModule.SkipOutOfAmmoUnits + the CohesionSwitchEnabled default-off pattern.")]
		public readonly bool SkipOutOfAmmoUnits = false;

		[Desc("Master switch for the dispersion doctrine (spread to move, mass to assault). OFF by",
			"default so the frozen Stable/Normal controls keep the pre-dispersion behaviour untouched;",
			"only PoiOffensiveBotModule@experimental turns it on. When off, no SetCohesion is issued.")]
		public readonly bool CohesionSwitchEnabled = false;

		[Desc("Dispersion doctrine — spread to move, mass to assault. While an axis centroid is farther",
			"than this many cells (Chebyshev) from its target it moves in ApproachCohesion; once within",
			"this radius it switches to AssaultCohesion for the final push.")]
		public readonly int AssaultRadiusCells = 15;

		[Desc("Cohesion mode issued to axis units while en route (centroid > AssaultRadiusCells from target).",
			"Set equal to AssaultCohesion (or both to Loose) to reproduce the pre-dispersion baseline.")]
		public readonly CohesionMode ApproachCohesion = CohesionMode.Spread;

		[Desc("Cohesion mode issued to axis units for the assault (centroid within AssaultRadiusCells of target).")]
		public readonly CohesionMode AssaultCohesion = CohesionMode.Tight;

		[Desc("EXPERIMENTAL SR-contestation knob (x100): multiplier applied to the enemy Supply",
			"Route PRESSURE axis score so the enemy SR can compete for an offensive axis. 100 =",
			"inert (byte-identical to the frozen baseline — the enemy SR keeps its raw",
			"GetOffensiveTargets score); above 100 raises it. Only PoiOffensiveBotModule@experimental",
			"sets this above 100. Deny-only: the SUPPLYROUTE has no CaptureManager, so a Pressure",
			"axis emits AttackMove (contest the circle), never a capture. Mirrors the",
			"CohesionSwitchEnabled default-off pattern so the Stable/Normal controls are untouched.")]
		public readonly int SrPressureScoreMultiplier = 100;

		[Desc("EXPERIMENTAL: derive free-pool eligibility from UnitRoleResolver (role is MainBattle or",
			"IndirectFire) instead of the ExcludeUnitTypes name list. Drops SHORAD/MANPADS (ShortRangeAD),",
			"capturers, logistics and scouts off offensive axes by class — the ai.yaml:349 defect cure —",
			"while artillery (IndirectFire) stays eligible until a dedicated fires executor exists. Cargo",
			"carriers (bradley/bmp2/m113) are still excluded so MountedTransportBotModule keeps them. Default",
			"false = frozen list behaviour, so the @stable twin stays byte-identical.")]
		public readonly bool UseUnitRoles = false;

		[Desc("Influence stack Stage E: consume the per-player ANTI-GROUND danger field (DangerFieldLayer)",
			"so an attack axis whose straight approach crosses a defended strongpoint / choke is steered",
			"onto a lateral lane PAST it, then in — attacks flow AROUND known kill zones instead of grinding",
			"head-on. Emits a two-leg AttackMove (lateral waypoint, then the objective). OFF by default so",
			"legacy/normal/stable and the frozen @stable twin stay byte-identical; only",
			"PoiOffensiveBotModule@experimental turns it on. Inert if no DangerFieldLayer / no field yet.")]
		public readonly bool DangerFieldRouting = false;

		[Desc("Stage-E: path ground-danger above which an axis approach is rerouted around the strongpoint.",
			"Must sit ABOVE the Stage-C territory baseline so the ambient 'deep enemy ground' danger does not",
			"detour every axis — only genuine defended cores (dense believed-contact kernels) trigger a detour.")]
		public readonly int GroundDangerSafeThreshold = 40;

		[Desc("Stage-E: lateral offset magnitude (cells) for the flow-around waypoint.")]
		public readonly int GroundDangerDetourCells = 6;

		[Desc("Stage-E: how many lateral steps (× GroundDangerDetourCells) the detour search may probe.")]
		public readonly int GroundDangerDetourSteps = 2;

		[Desc("Influence stack Stage F (strategic repoint): score offensive/expansion axes off the BELIEVED",
			"control field + anti-ground danger field instead of the OMNISCIENT InfluenceMap threat grid.",
			"When on, GetOffensiveTargets is asked for a threat-NEUTRAL base score (no omniscient read) and",
			"this module re-shapes it with (a) the territorial balance-of-power bias read from the control",
			"field — press cells we believe we hold / the enemy holds weakly, damp lunging into believed",
			"strength — and (b) a fog-legal believed-danger damp from the anti-ground danger field. Completes",
			"the @experimental fog migration for attack-axis selection. OFF by default so legacy/normal and the",
			"frozen @stable twin stay byte-identical; only PoiOffensiveBotModule@experimental turns it on.",
			"Inert (falls back to the omniscient path) if no ControlField exists.")]
		public readonly bool StrategicRepointEnabled = false;

		[Desc("Stage-F balance-of-power multiplier (x100) for a target on a cell we BELIEVE WE HOLD",
			"(control score > the field's GrayBand — the enemy's grip there is weak/broken ⇒ press).",
			">100 boosts. Default 100 = inert (frozen), so a bare StrategicRepointEnabled changes only the",
			"threat SOURCE, not the ranking, until the @experimental YAML supplies an active value.")]
		public readonly int BopBoostMultiplier = 100;

		[Desc("Stage-F balance-of-power multiplier (x100) for a target on a cell we BELIEVE THE ENEMY HOLDS",
			"(control score < −GrayBand — committing means lunging into believed strength ⇒ damp). <100 damps.",
			"Default 100 = inert. A contested front (|score| ≤ GrayBand) is always left at x100 (neutral).")]
		public readonly int BopDampMultiplier = 100;

		[Desc("Stage-F believed anti-ground danger (DangerFieldLayer.GroundDanger) at/below which a target",
			"cell counts as SAFE — the fog-legal replacement for the omniscient safe-threat bucket. On the",
			"danger-field intensity scale (throughput-derived), NOT the InfluenceMap scale. Sits above the",
			"Stage-C territory baseline so ambient 'deep enemy ground' danger does not damp every axis.")]
		public readonly int BelievedDangerMildThreshold = 40;

		[Desc("Stage-F believed anti-ground danger at/below which a target cell counts as MILD (above it is",
			"HOSTILE — a dense believed weapon envelope). Boundary between the mild and hostile damp buckets.")]
		public readonly int BelievedDangerHostileThreshold = 120;

		[Desc("Stage-F axis multiplier (x100) at SAFE believed ground danger (≤ BelievedDangerMildThreshold).",
			"Default 100 = inert / neutral (safe ground is not damped).")]
		public readonly int BelievedDangerSafeMultiplier = 100;

		[Desc("Stage-F axis multiplier (x100) at MILD believed ground danger. <100 damps a probed approach.",
			"Default 100 = inert.")]
		public readonly int BelievedDangerMildMultiplier = 100;

		[Desc("Stage-F axis multiplier (x100) at HOSTILE believed ground danger (inside a dense believed",
			"weapon envelope). <100 strongly damps lunging into believed fire. Default 100 = inert.")]
		public readonly int BelievedDangerHostileMultiplier = 100;

		[Desc("frontline-influence Phase 1 (reachability-gated + amphibious-typed targeting): re-shape each",
			"offensive axis score by whether a GROUND force can actually reach the POI from our Supply Route,",
			"read from the CrossingMap terrain model. A POI across an uncrossable river no longer scores as if",
			"adjacent: it is damped for land units unless (a) an intact crossing connects the banks, (b) we own",
			"amphibious units and a water route exists (then the axis is typed amphibious so those units go), or",
			"(c) only a repairable destroyed bridge connects them (reduced, kept on the radar for Phase 6). OFF",
			"by default ⇒ the factor is a constant 100 and axis typing never fires, so legacy/normal and the",
			"frozen @stable twin stay byte-identical. Inert if no CrossingMap exists.")]
		public readonly bool ReachabilityGatingEnabled = false;

		[Desc("Phase-1 score multiplier (x100) for a POI reachable only via a REPAIRABLE (destroyed) bridge and",
			"NOT by our amphibious units — reduced but kept on the radar for the Phase-6 engineer route-opening.",
			"Default 100 = inert (a bare ReachabilityGatingEnabled changes nothing until the YAML supplies < 100).")]
		public readonly int ReachabilityRepairableMultiplier = 100;

		[Desc("Phase-1 score multiplier (x100) for a POI reachable by the amphibious locomotor when we own NO",
			"amphibious units to crew the axis. Default 100 = inert.")]
		public readonly int ReachabilityAmphibiousMultiplier = 100;

		[Desc("Phase-1 score multiplier (x100) for a POI a ground force genuinely cannot reach (no crossing, no",
			"amphibious route). Default 100 = inert; set < the repairable/amphibious multipliers to strongly damp.")]
		public readonly int ReachabilityUnreachableMultiplier = 100;

		[Desc("Emit the per-POI [exp-reach-dist]/[exp-reach] debug lines (two Log.Write per POI per re-eval).",
			"Default OFF — only a per-reeval summary line is logged — so a normal match doesn't flood debug.log;",
			"turn on in an observation scenario to inspect the reachability/through-crossing decision per POI.")]
		public readonly bool DebugReachLogging = false;

		[Desc("EXPERIMENTAL fires doctrine (PIPELINE item 11): hold IndirectFire (artillery) axis members at",
			"weapon standoff during an assault instead of marching them to the objective with the line group.",
			"When on, artillery-role units are peeled off the grouped AttackMove and each is AttackMoved to a",
			"standoff anchor at its own max weapon range (minus FiresStandoffMargin) from the axis target — so",
			"it rains fire from range, follows the assault forward to stay in range, and backs a leg off if the",
			"target closes inside its band; the line units press exactly as before. The runtime gate is the presence",
			"of the UnitRoleResolver world trait (which derives the artillery role), NOT the UseUnitRoles flag;",
			"the feature is inert when the resolver is absent. OFF by default so the frozen @stable twin",
			"and every legacy profile stay byte-identical; only PoiOffensiveBotModule@experimental turns it on.")]
		public readonly bool FiresStandoff = false;

		[Desc("Fires doctrine: pull the standoff anchor this far (WDist) inside the piece's own max weapon",
			"range, so it sits just inside range with a safety cushion rather than at the very edge.")]
		public readonly WDist FiresStandoffMargin = WDist.FromCells(2);

		[Desc("Fires doctrine: hysteresis band (WDist) below the standoff radius. The piece only repositions",
			"when the target closes nearer than (standoff - this); inside the band it holds and keeps firing.",
			"Stops band-edge order chatter.")]
		public readonly WDist FiresStandoffHysteresis = WDist.FromCells(2);

		[Desc("Fires doctrine: floor (WDist) for the standoff radius, guarding a piece whose max range is at or",
			"under the margin (would otherwise anchor on top of the target).")]
		public readonly WDist FiresStandoffFloor = WDist.FromCells(3);

		[Desc("EXPERIMENTAL fires economics (PIPELINE item 19): ammo expected-value fire gate. When on (and",
			"FiresStandoff is on — the gate rides the standoff loop), a ROCKET-artillery piece (UnitRoleResolver",
			"IndirectFireKind.Rocket) holds fire while the best clump of spotted enemies within weapon range would",
			"not repay the salvo's ammo cost, and returns to FireAtWill once a worthy clump is in range. TUBE pieces",
			"are exempt (they may engage singles). OFF by default so the @stable twin stays byte-identical.")]
		public readonly bool FiresEvGate = false;

		[Desc("Fires EV gate: the projected $ damage of the best aim must be at least this percent of the salvo's",
			"ammo cost to fire (100 = plain cost<value; >100 demands a surplus). Only read when FiresEvGate is on.")]
		public readonly int FiresEvMarginPercent = 100;

		[Desc("Fires EV gate: horizontal radius (WDist) of the beaten zone used to gather a salvo's projected clump.",
			"Sized to the real rocket scatter (much wider than a single rocket's warhead spread). Only read when on.")]
		public readonly WDist FiresEvClumpRadius = WDist.FromCells(4);

		[Desc("EXPERIMENTAL fires doctrine Phase 1 (gap G1): CONTINUOUS BOMBARDMENT of believed STATIC positions.",
			"An idle IndirectFire piece with ammo and a believed-static enemy position (defence / garrison /",
			"structure — BeliefStore.Contacts flagged IsStatic) in weapon range takes a STANDING fire mission: it",
			"is AttackMoved to the target-relative FiresStandoff anchor and left to shell that position, INDEPENDENT",
			"of any offensive axis (the residual free pool no axis claimed). Targets come ONLY from the fog-legal",
			"belief store (no omniscient enemy scan); worthiness REUSES the FiresEconMath EV gate (a rocket piece",
			"only fires when the believed-static CLUMP repays its salvo, a tube piece may shell a single static —",
			"the tube/rocket split); positioning REUSES FiresStandoffMath; the piece is committed to the shared",
			"PoiGoalGuard ledger under 'bombard:<targetId>' so no axis double-tasks it, released when it runs dry",
			"(→ evac) or the target leaves the belief set. The decision (assignment + re-target hysteresis + per-",
			"target order cap) is the pure ContinuousBombardMath (NUnit-pinned), zero RNG. OFF by default so the",
			"frozen @stable twin and every legacy profile stay byte-identical; only PoiOffensiveBotModule@experimental",
			"turns it on. Inert when the UnitRoleResolver or the BeliefStore is absent. Reuses FiresEvMarginPercent /",
			"FiresEvClumpRadius for the EV gate.")]
		public readonly bool ContinuousBombardment = false;

		[Desc("Continuous bombardment: cap on how many idle pieces may pile onto ONE believed-static position, so a",
			"whole battery isn't dumped on a single bunker while other positions go unshelled. A piece ALREADY",
			"shelling a position keeps its slot regardless (the cap limits new pile-on). Only read when",
			"ContinuousBombardment is on.")]
		public readonly int BombardMaxPiecesPerTarget = 2;

		[Desc("Continuous bombardment: re-target hysteresis (cells). A piece already shelling a believed-static",
			"position holds it unless another worthy in-range position is closer by MORE THAN this many cells — the",
			"anti-flip-flop discipline that makes fires committed and repeated, not thrashing between targets each",
			"scan. Only read when ContinuousBombardment is on.")]
		public readonly int BombardRetargetHysteresisCells = 4;

		[Desc("Continuous bombardment: ledger commitment lifetime (ticks) for a piece assigned to a standing fire",
			"mission. Refreshed each re-eval a piece keeps its mission, so it must exceed ReevaluateInterval. Only",
			"read when ContinuousBombardment is on.")]
		public readonly int BombardCommitmentTicks = 250;

		[Desc("EXPERIMENTAL defence-in-depth echelon (builds on the fires standoff): position each IndirectFire",
			"piece ECHELONED BEHIND the axis's MainBattle SCREEN line instead of at a ring around the target, so",
			"artillery stays on the friendly side of the tanks/infantry rather than driving alone to the front. The",
			"echelon depth behind the screen centroid = max(EchelonMinDepth, (own max weapon range - the screen's",
			"engagement range) + EchelonBuffer), where the screen's engagement range is the longest weapon reach",
			"among the axis's non-fires (screen) units; the anchor is that depth behind the screen, offset directly",
			"away from the target. OVERRIDE: a piece with NO screen on its axis (pure-artillery axis / deliberately",
			"solo fire tasking) falls back to the target-relative FiresStandoff anchor and goes where the mission",
			"needs. Rides the FiresStandoff peel-off loop, so it needs FiresStandoff on. OFF by default so the frozen",
			"@stable twin stays byte-identical; only PoiOffensiveBotModule@experimental turns it on.")]
		public readonly bool EchelonPositioning = false;

		[Desc("Echelon: additive cushion (WDist) on the range surplus, so a piece sits a touch further back than",
			"'just barely in range of the front'. Only read when EchelonPositioning is on.")]
		public readonly WDist EchelonBuffer = WDist.FromCells(1);

		[Desc("Echelon: floor (WDist) on the echelon depth. Holds an indirect-fire piece that does NOT outrange",
			"the screen (surplus 0) at least this far behind it. Only read when EchelonPositioning is on.")]
		public readonly WDist EchelonMinDepth = WDist.FromCells(3);

		[Desc("Echelon: hold tolerance (WDist) around the echelon anchor. The piece only repositions when it",
			"drifts farther than this from its anchor; inside it holds so AutoTarget keeps firing (stops re-order",
			"chatter as the screen jitters). Only read when EchelonPositioning is on.")]
		public readonly WDist EchelonTolerance = WDist.FromCells(2);

		[Desc("Echelon: fallback screen engagement range (WDist) used when the axis has a screen but none of its",
			"screen units carries a live weapon (degenerate). Only read when EchelonPositioning is on.")]
		public readonly WDist EchelonScreenRangeFallback = WDist.FromCells(5);

		[Desc("EXPERIMENTAL frontier standoff (builds on the echelon): hold each echeloned IndirectFire piece at",
			"least this many COARSE control-field cells behind the believed enemy frontier (ControlField's",
			"distance-to-enemy-region). When the echelon anchor lands closer than this to the front, it is walked",
			"rearward along the anchor axis (bounded — never a free search) so artillery stands off BEHIND the",
			"front line, not on it. 0 = OFF (default; @stable/frozen byte-identical). Suggested ~4 (≈8 map cells).",
			"Needs EchelonPositioning on + a ControlField present; inert until the field is populated for this player.")]
		public readonly int MinFrontierDistanceCells = 0;

		[Desc("EXPERIMENTAL force-preservation (combat-quality lever 1): an axis that is LOSING its fight —",
			"believed local enemy force at least RetreatForceRatioPct% of its own remaining force, SUSTAINED for",
			"RetreatSustainEvals consecutive re-evals — falls back toward friendly control (a grouped AttackMove to",
			"the rally cell = our own Supply Route) instead of grinding to the death at the objective. Believed enemy",
			"force is tallied from the fog-legal BeliefStore (armed contacts within ForceRatioRadiusCells of the axis",
			"centroid, weighted by build cost x confidence); own force is the axis's HEALTH-weighted build value — the",
			"SAME cost scale, so the ratio is meaningful. Hysteresis: a retreating axis COMMITS to the retreat until",
			"it reaches safety or the ratio recovers past ReengageForceRatioPct% (stricter), so it never flip-flops.",
			"Shaped as an ABORT TRIGGER (a force-ratio spike), not a competing order stream — composes with squad",
			"mission-commitment. OFF by default so legacy/normal and the frozen @stable twin stay byte-identical;",
			"only PoiOffensiveBotModule@experimental turns it on. Inert with no BeliefStore / no own Supply Route.")]
		public readonly bool RetreatWhenLosing = false;

		[Desc("EXPERIMENTAL force-preservation (combat-quality lever 2): stop feeding fresh units PIECEMEAL into an",
			"axis whose fight is already lost (the SAME believed-force-ratio 'losing' state as RetreatWhenLosing). A",
			"trickle of reinforcements into a meat grinder is the classic 2x-deaths mechanism; when on, a losing axis",
			"is not topped up — the free units stay in the pool for other axes / a rally instead. Independent of the",
			"retreat-order gate (either lever can run alone). OFF by default = byte-identical.")]
		public readonly bool NoReinforceLostFights = false;

		[Desc("Force-preservation: believed enemy-to-own local force ratio (x100) at/above which an axis counts as",
			"LOSING and (after RetreatSustainEvals) retreats. 200 = retreat once the believed enemy force is 2x our",
			"remaining force. Only read when RetreatWhenLosing / NoReinforceLostFights is on.")]
		public readonly int RetreatForceRatioPct = 200;

		[Desc("Force-preservation hysteresis EXIT (x100): a RETREATING axis only re-engages once the believed enemy",
			"force falls to at/below this percent of its own force (or it reaches safety). Must be <= RetreatForceRatioPct",
			"so the trigger and release bands don't overlap (no advance/retreat oscillation). 120 = re-engage when back",
			"within 20% of parity.")]
		public readonly int ReengageForceRatioPct = 120;

		[Desc("Force-preservation: consecutive losing re-evals required before an axis commits to a retreat (a window",
			"so a single unlucky field read never triggers a fall-back). 1 = retreat on the first losing read.")]
		public readonly int RetreatSustainEvals = 2;

		[Desc("Force-preservation: radius (cells, Chebyshev) around an axis centroid over which believed enemy force",
			"is tallied from the BeliefStore for the losing-ratio test.")]
		public readonly int ForceRatioRadiusCells = 8;

		[Desc("Force-preservation: a retreating axis is 'safe' (and may re-engage) once its centroid is within this",
			"many cells (Chebyshev) of the rally cell (our own Supply Route). Ends the committed retreat.")]
		public readonly int RetreatSafeDistanceCells = 10;

		[Desc("PHASE 2 (@experimental) free-pool FORWARD STAGING. Uncommitted combat units (the free pool) are",
			"walked to a forward muster point a safe standoff BEHIND the believed friendly frontier instead of",
			"idling at the Supply Route where they mustered in (the 'units pool at spawn, clogging the road to the",
			"front' symptom). The staging point is found by steepest descent on the control field's",
			"distance-to-enemy-frontier BFS (from the SR toward the nearest front) and ADVANCES as the front moves.",
			"Units fan out over several cells (deterministic ring spread) — no pile on one cell. Committed",
			"(ledger/axis) and retreating units are never staged. OFF by default so the frozen @stable twin and",
			"every legacy profile keep the reserve idling at the SR byte-identical; only",
			"PoiOffensiveBotModule@experimental turns it on. Inert (reserve idles as before) until a ControlField",
			"exists and its frontier field is populated. Pure ForwardStagingMath (NUnit-pinned), zero RNG.")]
		public readonly bool ForwardStagingEnabled = false;

		[Desc("Forward staging: hold the muster point at least this many COARSE control-field cells behind the",
			"believed enemy frontier (the standoff at which the gradient descent stops). Only read when",
			"ForwardStagingEnabled.")]
		public readonly int StagingStandoffCells = 6;

		[Desc("Forward staging: never route the muster descent into a cell whose believed anti-ground danger",
			"exceeds this (danger-field intensity scale) — keeps the staging point BEHIND defended fronts, not on",
			"them. Set at the same scale as GroundDangerSafeThreshold. Negative disables the danger guard. Only",
			"read when ForwardStagingEnabled.")]
		public readonly int StagingDangerSafeThreshold = 40;

		[Desc("Forward staging: spacing (map cells) between staged units on the deterministic ring spread around",
			"the muster anchor — the anti-clog spread. Only read when ForwardStagingEnabled.")]
		public readonly int StagingSpreadStepCells = 2;

		[Desc("Forward staging: hysteresis (map cells, Chebyshev) — the muster anchor is only re-ADOPTED (and the",
			"formation re-laid) when it advances at least this far from the last adopted anchor, so a small field",
			"wobble doesn't spam staging orders. Only read when ForwardStagingEnabled.")]
		public readonly int StagingHysteresisCells = 3;

		[Desc("Forward staging: bounded budget (coarse cells) on the gradient-descent walk from the SR to the",
			"muster point — a walk-forward, never a free search. Only read when ForwardStagingEnabled.")]
		public readonly int StagingMaxDescentSteps = 64;

		[Desc("PHASE 3 (@experimental) RETREAT-OSCILLATION DAMPER. Builds on RetreatWhenLosing: stops small",
			"early-spread axes ping-ponging into the SR bubble (advance, read losing, fall back, re-form, repeat).",
			"Adds two anti-oscillation gates on TOP of the sustained-streak retreat ENTRY that RetreatWhenLosing",
			"already has: (a) a post-retreat DWELL (RetreatReadvanceDwellEvals) an axis must hold before it may",
			"re-advance on the same target, and (b) an advance-STRENGTH floor (MinAdvanceStrength) below which an",
			"axis still massing near the rally holds/merges rather than trickling 2-3 units forward. NEITHER delays",
			"a genuine retreat — a truly-losing axis still withdraws promptly (that decision is upstream); the",
			"damper only delays RE-advance and filters noise-massing. OFF by default = byte-identical; only",
			"PoiOffensiveBotModule@experimental turns it on. Requires RetreatWhenLosing + a BeliefStore to have any",
			"effect (the FSM it damps runs only then). Pure RetreatDamperMath (NUnit-pinned), zero RNG.")]
		public readonly bool RetreatDamperEnabled = false;

		[Desc("Retreat damper (a): evals an axis must HOLD after completing a retreat before it may re-advance on",
			"the same target — the post-retreat dwell that converts advance/lose/retreat churn into hold-then-push.",
			"0 (default) = inert (no dwell). Only read when RetreatDamperEnabled.")]
		public readonly int RetreatReadvanceDwellEvals = 0;

		[Desc("Retreat damper (b): minimum own force (health-weighted build value, SAME scale as the retreat",
			"force ratio) an axis still massing near the rally must reach before it advances — below it the axis",
			"waits/merges instead of trickling a 2-3-unit packet forward. 0 (default) = inert (no floor). Only read",
			"when RetreatDamperEnabled.")]
		public readonly int MinAdvanceStrength = 0;

		[Desc("PHASE 4 (@experimental) FRONTLINE STRENGTH PROFILE (sensor only — no order-issuing change).",
			"Opts this player in to the ControlField's per-frontier-sector believed OWN-vs-ENEMY strength",
			"profile + avenue (crossing) mapping, so a future consumer can ask 'which frontier sector is the",
			"enemy line thinnest in, and which crossing opens into it.' Phase 4 only BUILDS the sensor (rides",
			"the control field's existing per-player recompute cadence, no new timer); nothing acts on it yet.",
			"OFF by default so the frozen @stable twin, normal, and human games never opt in — the profile",
			"arrays are then never built and they stay byte-identical; only PoiOffensiveBotModule@experimental",
			"turns it on. Inert until a ControlField exists. Pure FrontlineProfileMath (NUnit-pinned), zero RNG.")]
		public readonly bool FrontlineProfileEnabled = false;

		[Desc("PHASE 5 (@experimental) WEAKEST-POINT ATTACK BIAS. Reads the Phase-4 frontline strength profile",
			"(ControlField.WeakestEnemySector + the per-sector believed strength) and BIASES offensive axis",
			"selection toward targets sitting in the believed-thinnest enemy frontier sector — 'push where the",
			"enemy line is weakest.' Implemented as a score MULTIPLIER (WeakestPointBiasMultiplier), not a hard",
			"override: the existing value×distance×threat scoring + deterministic comparator stay authoritative,",
			"so a bare enable (multiplier 100) is inert and the ranking is byte-identical. Opts this player into",
			"the frontline profile the same way FrontlineProfileEnabled does. OFF by default so the frozen @stable",
			"twin / normal / human never opt in ⇒ byte-identical. Inert until a ControlField profile exists. Pure",
			"FrontlineAllocationMath (NUnit-pinned), zero RNG.")]
		public readonly bool WeakestPointBiasEnabled = false;

		[Desc("Phase-5 weakest-point bias: score multiplier (x100) applied to an axis whose target lies in the",
			"believed-weakest enemy frontier sector. >100 boosts the push toward the thin sector. Default 100 =",
			"inert (a bare WeakestPointBiasEnabled changes nothing until the @experimental YAML supplies >100).")]
		public readonly int WeakestPointBiasMultiplier = 100;

		[Desc("PHASE 5 (@experimental) SECTOR POSTURE HOLD. Where the Phase-4 profile reads a target's frontier",
			"sector as TOO STRONG — believed enemy force ≥ SectorPostureHoldRatioPct% of our own believed strength",
			"in that sector — the axis HOLDS/defends (a grouped fall-back to the rally/staging anchor) instead of",
			"pressing into believed strength. Shaped as a HOLD TRIGGER that RIDES the existing retreat/damper",
			"fall-back path (reuses OrderRetreat, no new order writer) and runs ONLY AFTER the genuine-retreat and",
			"damper gates — so it can NEVER block a truly-losing withdrawal (that decision is upstream). Needs a",
			"rally anchor (own Supply Route). OFF by default so the frozen @stable twin / normal / human stay",
			"byte-identical; only PoiOffensiveBotModule@experimental turns it on. Inert until a ControlField profile",
			"exists. Pure FrontlineAllocationMath (NUnit-pinned), zero RNG.")]
		public readonly bool SectorPostureHoldEnabled = false;

		[Desc("Phase-5 posture hold: believed enemy-to-own strength ratio (x100) in a target's frontier sector at/",
			"above which the axis holds rather than presses. 200 = hold once the believed enemy force in the sector",
			"is 2× our own committed strength there. Only read when SectorPostureHoldEnabled; <= 0 disables the hold.")]
		public readonly int SectorPostureHoldRatioPct = 200;

		[Desc("Phase-5 posture hold: minimum believed OWN strength (armed-unit presence count) an axis's CONTACT",
			"sector must carry before the hold ratio is even considered — you cannot HOLD a sector you do not",
			"occupy. Below this floor the axis PRESSES. Guards the degenerate own≈0 case that would otherwise freeze",
			"an offensive axis pushing into believed strength. Only read when SectorPostureHoldEnabled; <= 0 disables",
			"the floor (legacy own=0-vs-enemy ⇒ hold). Presence scale is ~1 per own armed unit in the sector.")]
		public readonly int SectorPostureHoldOwnFloor = 3;

		[Desc("MISSION COMMITMENT (Phase-1 anti-thrash stopgap). Once an axis has been ordered at an objective,",
			"do NOT re-task it on the next re-eval merely because scores jittered — HOLD the mission and leave its",
			"in-flight order alone. A committed axis is released for re-tasking ONLY on an explicit trigger:",
			"objective invalid (target gone/captured), a believed-danger spike at the objective, a rival objective",
			"beating it by MissionBetterOppMarginPct, or the squad ground below MissionIneffectiveNumerator/",
			"Denominator of its commit-time strength. Kills the 'go one way, stop, go the other, loop' churn from",
			"the steady re-issue cadence overwriting live orders. OFF by default so the frozen @stable twin and",
			"every legacy profile keep re-tasking every eval byte-identical; only PoiOffensiveBotModule@experimental",
			"turns it on. Decision math is the pure MissionCommitmentMath (NUnit-pinned), v3-brain-portable.")]
		public readonly bool MissionCommitmentEnabled = false;

		[Desc("Mission commitment: bounded safety re-plan window (ticks from the commit). When > 0 a committed",
			"axis is force-released for re-evaluation once this many ticks elapse even if no other trigger fired.",
			"0 (default) = pure-trigger hold: the mission persists until its objective completes (trigger 1) or a",
			"danger/opportunity/attrition trigger fires. Only read when MissionCommitmentEnabled is on.")]
		public readonly int MissionCommitmentWindowTicks = 0;

		[Desc("Mission commitment trigger 2 (danger spike): percent above the commit-time believed danger the",
			"current danger at the objective must rise to abandon the mission. Scales the reaction to an already-",
			"dangerous commit so ambient baseline jitter doesn't trip it. Only read when MissionCommitmentEnabled.")]
		public readonly int MissionDangerSpikePct = 50;

		[Desc("Mission commitment trigger 2: ABSOLUTE floor (danger-field intensity scale) on the spike margin, so",
			"a fresh weapon envelope appearing over previously-quiet ground (commit danger ≈ 0) still trips the abort.",
			"Set at the mild-danger threshold (a genuine believed weapon envelope, above baseline stacking). Only read",
			"when MissionCommitmentEnabled.")]
		public readonly int MissionDangerSpikeFloor = 40;

		[Desc("Mission commitment trigger 3 (better opportunity): a rival objective must beat the committed axis's",
			"score by strictly MORE than this percent to abandon the current mission. Set ABOVE ReassignScoreThresholdPct",
			"so mission-level re-tasking needs a bigger delta than the target-set stickiness — a mere tie-break flip never",
			"re-tasks. Only read when MissionCommitmentEnabled.")]
		public readonly int MissionBetterOppMarginPct = 50;

		[Desc("Mission commitment trigger 4 (combat-ineffective): numerator of the commit-time-strength fraction below",
			"which a gutted squad is released to re-task/regroup. With the denominator below, 1/2 = 'below half the units",
			"it committed with'. Only read when MissionCommitmentEnabled.")]
		public readonly int MissionIneffectiveNumerator = 1;

		[Desc("Mission commitment trigger 4: denominator of the commit-time-strength fraction (see numerator). Only read",
			"when MissionCommitmentEnabled.")]
		public readonly int MissionIneffectiveDenominator = 2;

		[Desc("Phase 1c — trigger-3 score QUANTIZATION band, as a percent of the larger of the two compared scores.",
			"The believed-field factors that scale an axis score are bucketed (BalanceOfPowerFactor 150/100/60,",
			"BelievedDangerFactor 100/60/20), so a single bucket crossing can multiply a raw score by up to 3× —",
			"more than any percent margin — and a RAW better-opportunity compare then ping-pongs abort/re-propose on",
			"a believed-field wobble at a bucket edge. When > 0, both scores are floored to a band of this percent of",
			"the top score before the MissionBetterOppMarginPct test, so a rival must be a full band clear to count as",
			"materially better (a bucket-aware threshold a single wobble cannot manufacture). 0 (default) = the raw",
			"pre-1c compare, byte-identical. Only read when MissionCommitmentEnabled. Pure MissionCommitmentMath.")]
		public readonly int MissionScoreQuantizeBandPct = 0;

		[Desc("Phase 1d — Aggressiveness slider (0 cautious … 50 neutral … 100 reckless), the first tunable-parameter",
			"knob (§2.7). Stood up on the offense module because that is where the first consumer lands; it migrates to",
			"the SquadBrain in a later phase. Threaded ONLY through pure PoiOffenseMath.ShiftByKnob so a sweep harness",
			"can vary it per match. 50 = neutral (no shift); with the slope pair below at 0 it is fully INERT, so the",
			"default is byte-identical regardless of value. Reserved for the Brain's posture/advance eagerness later.")]
		public readonly int Aggressiveness = 50;

		[Desc("Phase 1d — slope (range) the Aggressiveness knob spans when shifting MissionBetterOppMarginPct: effective",
			"margin = MissionBetterOppMarginPct + (Aggressiveness - 50) · this / 100. A higher Aggressiveness LOWERS the",
			"better-opportunity margin (more willing to abandon a mission for a clearly better attack). 0 (default) =",
			"INERT: the margin is unchanged for any Aggressiveness, so the slider scaffolding ships without behaviour.",
			"Only read when MissionCommitmentEnabled.")]
		public readonly int MissionBetterOppMarginSlopePct = 0;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase).
			ActorNameCase.NormalizeInPlace(ExcludeUnitTypes);
		}

		public override object Create(ActorInitializer init) { return new PoiOffensiveBotModule(init.Self, this); }
	}

	public class PoiOffensiveBotModule : ConditionalTrait<PoiOffensiveBotModuleInfo>, IBotTick
	{
		// A live attack axis: a target POI plus the units committed to it. Persists
		// across re-evals so units aren't reshuffled every scan (hysteresis).
		sealed class Axis
		{
			public uint TargetId;
			public CPos TargetCell;
			public WPos TargetPos;
			public long Score;
			public PoiAction Action;
			public string TargetName;
			public CPos OrderedCell;   // last target cell we AttackMoved to (for repath gating)
			public CPos? OrderedVia;   // last Stage-E lateral waypoint ordered (null = went direct)
			public bool HasOrdered;

			// Combat-quality force-preservation FSM state (levers 1+2). Engaged (default) reproduces the
			// legacy assault path exactly; Retreating falls back / is not reinforced. LosingStreak counts an
			// unbroken run of losing evals toward RetreatSustainEvals. OrderedRetreat records that the last
			// order issued was a fall-back (so the state flip back to Engaged forces an assault re-issue).
			public int LosingStreak;
			public RetreatDecision Retreat;
			public bool OrderedRetreat;

			// Phase 3 retreat-oscillation damper (populated only when RetreatDamperEnabled). ReadvanceHold counts
			// down the post-retreat DWELL (evals an axis holds after completing a retreat before it may re-advance
			// on the same target). NearRally records whether the axis centroid is within RetreatSafeDistanceCells
			// of the rally this eval (the "still massing in the rear" gate for the advance-strength floor).
			public int ReadvanceHold;
			public bool NearRally;

			public readonly List<Actor> Units = new();

			// MISSION COMMITMENT snapshot (populated only when MissionCommitmentEnabled). Committed = the
			// axis has been ordered at least once and holds a mission; the Commit* fields are the baseline
			// captured at that order, against which MissionCommitmentMath tests the abort triggers each eval.
			public bool Committed;
			public int CommitTick;
			public long CommitScore;
			public int CommitDanger;
			public int CommitStrength;

			// Phase 1: this axis targets a far-bank POI reachable only by water, so it must be crewed by
			// amphibious units (set from the reachability reshape). Default false ⇒ the legacy recruit path.
			public bool AmphibiousTyped;
		}

		readonly World world;
		readonly Player player;

		PoiMap poiMap;
		bool poiMapResolved;
		PoiGoalGuard goalGuard;
		bool goalGuardResolved;
		UnitRoleResolver resolver;
		bool resolverResolved;
		DangerFieldLayer dangerField;
		bool dangerFieldResolved;
		ControlField controlField;
		bool controlFieldResolved;
		BeliefStore beliefStore;
		bool beliefStoreResolved;
		CrossingMap crossingMap;
		bool crossingMapResolved;

		readonly List<Axis> axes = new();

		// Phase 1: targetId → the axis should be crewed by amphibious units (a water-only far-bank POI).
		// Rebuilt each reeval by the reachability reshape; empty (and unread) unless ReachabilityGatingEnabled.
		readonly Dictionary<uint, bool> amphibiousTargets = new();

		// Combat-quality force-preservation: rally cell (our own Supply Route) resolved once per re-eval when a
		// force-preservation lever is on — a losing axis falls back to it and counts as safe near it. Null when
		// no lever is active or we have no SR to fall back to.
		CPos? rallyCell;

		// Phase 2 free-pool forward staging: the muster point THIS eval (a safe standoff behind the believed
		// frontier, resolved from the SR down the control field's distance-to-frontier gradient). Null when
		// staging is off, no control field / SR, or the field is unpopulated (⇒ reserve idles at the SR, legacy).
		CPos? stagingAnchor;

		// The last ADOPTED staging anchor (Chebyshev hysteresis, so a 1-cell field wobble doesn't re-lay the
		// formation every eval), and the last staging cell each idle unit was AttackMoved to (re-issue dedup so a
		// unit already walking up keeps its order). Both empty/null unless ForwardStagingEnabled.
		CPos? lastStagingAnchor;
		readonly Dictionary<Actor, CPos> stagedCells = new();

		// Cached (build cost, armed) per believed-contact TypeName, resolved from world rules on first use, so
		// the believed-enemy force tally doesn't re-walk the actor rules every eval. Deterministic.
		readonly Dictionary<string, (int Cost, bool Armed)> contactFactCache = new();

		// Last cohesion mode we issued to each unit (dispersion doctrine). Cohesion is a
		// property of the unit, not the axis, so a re-recruited unit keeps its mode across
		// axes — we only re-issue SetCohesion when a unit's desired mode actually changes.
		readonly Dictionary<Actor, CohesionMode> lastCohesion = new();

		// Fires doctrine: last standoff-anchor CELL we AttackMoved each artillery piece to. Gates
		// re-issue so a piece holding in-band keeps firing uninterrupted (only re-ordered when it must
		// reposition or its anchor drifted past RepathThresholdCells). Empty unless FiresStandoff is on.
		readonly Dictionary<Actor, CPos> lastFiresAnchor = new();

		// Fires EV gate (item 19): rocket pieces we have forced to HoldFire because no worthy clump is in range,
		// plus the subset re-affirmed this eval. A piece that leaves the fires set while held is restored to
		// FireAtWill in the post-order reconciliation, so it can never strand in HoldFire. Empty unless FiresEvGate.
		readonly HashSet<Actor> firesHeldFire = new();
		readonly HashSet<Actor> firesHeldThisEval = new();

		// Continuous bombardment (Phase 1): each idle piece currently on a standing fire mission → the believed
		// static target (belief-contact ActorID) it is shelling, and the last standoff-anchor CELL it was
		// AttackMoved to (re-issue dedup, mirrors lastFiresAnchor). A tracked piece is committed to the shared
		// ledger under "bombard:<targetId>"; it is released + untracked when it runs dry / dies / the target
		// leaves the belief set. Both empty unless ContinuousBombardment is on ⇒ byte-identical when off.
		readonly Dictionary<Actor, uint> bombardAssigned = new();
		readonly Dictionary<Actor, CPos> lastBombardAnchor = new();

		// Fires EV gate: falloff cone weighting a salvo's projected clump over FiresEvClumpRadius (centre 100% →
		// edge 0%). A coarse, deterministic beaten-zone shape — the quick gate, not a per-rocket ballistic model.
		static readonly int[] FiresEvFalloff = { 100, 60, 25, 0 };

		// Fires doctrine: bounded Chebyshev radius (cells) for the nearest-passable clamp on a standoff anchor
		// that lands on impassable ground. Small — a passable cell almost always sits within a cell or two of
		// the standoff ring; if none does within this budget the raw ideal is used (pre-clamp behaviour).
		const int FiresAnchorClampCells = 4;

		int reevalCountdown;

		public PoiOffensiveBotModule(Actor self, PoiOffensiveBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
		}

		protected override void TraitEnabled(Actor self)
		{
			// Stagger so not every AI re-evaluates on the same frame.
			reevalCountdown = world.LocalRandom.Next(0, Math.Max(1, Info.ReevaluateInterval));
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (player.WinState != WinState.Undefined)
				return;

			if (--reevalCountdown > 0)
				return;

			reevalCountdown = Info.ReevaluateInterval;
			Reevaluate(bot);
		}

		void Reevaluate(IBot bot)
		{
			if (!poiMapResolved)
			{
				poiMap = world.WorldActor.TraitOrDefault<PoiMap>();
				poiMapResolved = true;
			}

			if (poiMap == null)
				return;

			if (!goalGuardResolved)
			{
				goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();
				goalGuardResolved = true;
			}

			if (!resolverResolved)
			{
				resolver = world.WorldActor.TraitOrDefault<UnitRoleResolver>();
				resolverResolved = true;
			}

			// The anti-ground danger field feeds the Stage-E flow-around routing, the Stage-F believed-danger
			// axis damp, AND the mission-commitment danger-spike trigger, so resolve it when any is enabled.
			if (!dangerFieldResolved)
			{
				dangerField = Info.DangerFieldRouting || Info.StrategicRepointEnabled || Info.MissionCommitmentEnabled
					|| Info.ForwardStagingEnabled
					? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;
				dangerFieldResolved = true;
			}

			// The control field feeds the Stage-F strategic repoint AND the echelon frontier standoff, so
			// resolve it when either is enabled.
			if (!controlFieldResolved)
			{
				controlField = Info.StrategicRepointEnabled || Info.MinFrontierDistanceCells > 0
					|| Info.ForwardStagingEnabled || Info.FrontlineProfileEnabled
					|| Info.WeakestPointBiasEnabled || Info.SectorPostureHoldEnabled
					? world.WorldActor.TraitOrDefault<ControlField>() : null;
				controlFieldResolved = true;

				// Phase-4/5: opt this @experimental player in to the frontline strength profile. Idempotent; only
				// reached when a profile CONSUMER flag is on (the Phase-4 sensor opt-in or a Phase-5 consumer that
				// reads it) ⇒ @stable / normal / human never opt in and the profile is never built for them
				// (byte-identical).
				if (Info.FrontlineProfileEnabled || Info.WeakestPointBiasEnabled || Info.SectorPostureHoldEnabled)
					controlField?.RequestFrontlineProfile(player);
			}

			// Phase-1 reachability model: resolve the CrossingMap only when the gate is on (its first query
			// triggers the lazy terrain build). Null otherwise ⇒ the reshape/typing below is skipped entirely.
			if (!crossingMapResolved)
			{
				crossingMap = Info.ReachabilityGatingEnabled
					? world.WorldActor.TraitOrDefault<CrossingMap>() : null;
				crossingMapResolved = true;
			}

			// Combat-quality force-preservation: the belief store feeds the believed-enemy force tally for both
			// levers; continuous bombardment (Phase 1) reads its IsStatic contacts as the fog-legal target set.
			// Resolve it when any is on. Inert (never resolved) otherwise ⇒ byte-identical.
			if (!beliefStoreResolved)
			{
				beliefStore = Info.RetreatWhenLosing || Info.NoReinforceLostFights || Info.ContinuousBombardment
					? world.WorldActor.TraitOrDefault<BeliefStore>() : null;
				beliefStoreResolved = true;
			}

			// Rally cell for the fall-back / safety test — our own Supply Route. Resolved once per eval only when
			// a lever is on (one FindOwnSupplyRoute scan); null otherwise, so no cost on the frozen path.
			// ForwardStaging also needs the SR as the descent start (the muster walk begins at the beachhead).
			rallyCell = Info.RetreatWhenLosing || Info.NoReinforceLostFights || Info.ForwardStagingEnabled
				|| Info.SectorPostureHoldEnabled
				? RallyCell() : null;

			// Phase 2: resolve the forward staging anchor for this eval (safe standoff behind the frontier, walked
			// from the SR down the control field's distance-to-frontier gradient, with anchor hysteresis). Null
			// when staging is off / no field / unpopulated ⇒ the reserve idles at the SR exactly as before. Shared
			// by the free-pool stager AND (as the preferred hold target) the Phase-3 damper, so it is resolved once
			// here before both consumers run.
			stagingAnchor = Info.ForwardStagingEnabled ? ResolveStagingAnchor() : null;

			var tick = world.WorldTick;

			// Early-econ behaviour 3: disperse in smaller packets while the match is young. When the gate is
			// off (default) these equal the Info values, so DesiredAxisCount / AllocateProportional / the
			// under-min retire below are byte-identical to the pre-change path for the frozen @stable twin and
			// every legacy profile. Pure gate on the synced sim tick — zero RNG.
			var early = EarlyGamePhase.IsEarly(tick, Info.EarlyGameSpread, Info.EarlyGameDurationTicks);
			var unitsPerAxis = early ? Info.EarlyUnitsPerAxis : Info.UnitsPerAxis;
			var minAxisSize = early ? Info.EarlyMinAxisSize : Info.MinAxisSize;

			// 1. Drop dead/lost units from live axes; sweep orphan offense commitments.
			PruneAxes();
			if (goalGuard != null)
				goalGuard.Ledger.Prune(tick, a => !a.IsDead && a.IsInWorld && a.Owner == player);

			// Bound the cohesion-tracking map to living units so it can't leak across a game.
			if (lastCohesion.Count > 0)
			{
				var stale = lastCohesion.Keys.Where(a => a.IsDead || !a.IsInWorld || a.Owner != player).ToList();
				foreach (var a in stale)
					lastCohesion.Remove(a);
			}

			// Same bound for the fires-anchor map (only populated when FiresStandoff is on).
			if (lastFiresAnchor.Count > 0)
			{
				var staleFires = lastFiresAnchor.Keys.Where(a => a.IsDead || !a.IsInWorld || a.Owner != player).ToList();
				foreach (var a in staleFires)
					lastFiresAnchor.Remove(a);
			}

			// Same bound for the bombardment anchor map (only populated when ContinuousBombardment is on). The
			// bombardAssigned ledger-commitment map is pruned inside BombardStaticPositions (it must RELEASE the
			// ledger claim, not just drop the key), so it is not swept here.
			if (lastBombardAnchor.Count > 0)
			{
				var staleBombard = lastBombardAnchor.Keys.Where(a => a.IsDead || !a.IsInWorld || a.Owner != player).ToList();
				foreach (var a in staleBombard)
					lastBombardAnchor.Remove(a);
			}

			// Fires EV gate: reset the per-eval "still held this eval" marker. The post-order reconciliation
			// uses it to restore FireAtWill on any rocket piece that left the fires set while holding fire.
			firesHeldThisEval.Clear();

			// 2. Score offensive targets from OUR SR (value x distance x threat).
			//    Stage-F strategic repoint: when on (and a control field exists), ask PoiMap for a
			//    threat-NEUTRAL base score — no omniscient InfluenceMap read — and re-shape it below
			//    from the BELIEVED control + danger fields. Off ⇒ the frozen omniscient path, so the
			//    @stable twin (flag unset) and every control profile stay byte-identical.
			// frontline-influence Phase 1.5: honest through-crossing distance. When reachability gating is on
			// and a CrossingMap exists, supply PoiMap a distance provider that replaces a far-bank POI's
			// crow-flies distance with its through-crossing detour (SR→crossing→POI), so central crossings
			// lose their artificial "as-if-adjacent" advantage. Null (flag off / no CrossingMap / no SR) ⇒
			// PoiMap keeps its exact Euclidean distance ⇒ byte-identical for @stable/normal/human.
			Func<CPos, int?> throughDist = null;
			if (Info.ReachabilityGatingEnabled && crossingMap != null)
			{
				var srForDist = poiMap.OwnSupplyRoute(player)?.Location;
				if (srForDist != null)
					throughDist = poiCell => crossingMap.ThroughCrossingDistanceOverride(srForDist.Value, poiCell);
			}

			var repoint = Info.StrategicRepointEnabled && controlField != null;
			var targets = repoint
				? poiMap.GetOffensiveTargets(player, suppressOmniscientThreat: true, throughDist)
				: poiMap.GetOffensiveTargets(player, throughCrossingDistance: throughDist);

			// 2a. Experimental SR-contestation: re-scale the enemy Supply Route Pressure axis so
			//     it can compete for an offensive axis. A no-op at multiplier 100 (guarded), so
			//     the frozen Stable/Normal controls keep their exact GetOffensiveTargets ranking.
			if (Info.SrPressureScoreMultiplier != 100)
				targets = RescaleSrPressure(targets);

			// 2b. Stage-F territorial repoint: re-shape the (threat-neutral) axis scores from the
			//     BELIEVED control field (balance-of-power: press cells we hold / the enemy holds
			//     weakly, damp lunging into believed strength) + the fog-legal anti-ground danger
			//     field (damp targets inside a believed weapon envelope). Replaces the omniscient
			//     threat read suppressed above. Inert/skipped unless the repoint is active.
			if (repoint)
				targets = RescaleByBelievedFields(targets, tick);

			// 2d. frontline-influence Phase 1: reachability-gate + amphibious-type the axis scores from the
			//     CrossingMap terrain model. Damps a far-bank POI a land force cannot reach and records which
			//     targets must be crewed by amphibious units (consumed in the recruit step). Inert/skipped —
			//     amphibiousTargets stays empty — unless the gate is on AND a CrossingMap exists, so every
			//     other profile is byte-identical.
			amphibiousTargets.Clear();
			if (Info.ReachabilityGatingEnabled && crossingMap != null)
				targets = RescaleByReachability(targets);

			// 2e. frontline-influence Phase 5: weakest-point attack bias. Boost axes whose target sits in the
			//     believed-thinnest enemy frontier sector (ControlField.WeakestEnemySector) so the push flows
			//     toward the enemy line's weak point. A BIAS — the same deterministic comparator re-sorts — so
			//     a bare enable (multiplier 100) or an un-built profile leaves the ranking byte-identical.
			//     Inert/skipped unless the flag is on AND a ControlField profile exists for this player.
			if (Info.WeakestPointBiasEnabled && controlField != null
				&& controlField.HasFrontlineProfile(player) && Info.WeakestPointBiasMultiplier != 100)
				targets = RescaleByWeakestSector(targets, tick);

			if (targets.Count == 0)
			{
				RetireAllAxes("no-targets");

				// FIX-1: no scoreable enemy POI means no fires set is built this eval, so every held rocket piece
				// is now a stray — reconcile BEFORE the early return, or a piece held on an unworthy clump would
				// strand in HoldFire (defenceless) for as long as the enemy has no visible POI (mop-up / fogged
				// POIs). firesHeldThisEval was just cleared above, so this restores FireAtWill on all held pieces.
				ReconcileFiresHoldFire(bot);

				Log.Write("debug", $"[exp-offense] reeval player={player.PlayerName} targets=0 axes=0 tick={tick}");
				return;
			}

			// 2c. MISSION COMMITMENT (experimental, default off): pull every axis that still HOLDS its
			//     mission out of the reshuffle universe BEFORE the free pool / allocation runs, so a
			//     committed squad is neither re-sized nor re-ordered this eval — its in-flight order
			//     stands. An axis is held unless MissionCommitmentMath fires an abort trigger (objective
			//     invalid, danger spike, materially better rival, or combat-ineffective). Held units stay
			//     ledger-committed (TTL refreshed) so BuildFreePool below excludes them, and held targets
			//     are dropped from the candidate list so no duplicate axis forms for them. When the flag is
			//     off heldAxes stays null and every line below is byte-identical to the pre-change path.
			var heldAxes = PartitionHeldAxes(ref targets, tick);

			// 3. Free pool = eligible combat units claimed by nobody (SquadManager no
			//    longer owns ground for experimental; capture/defense commitments are respected).
			var free = BuildFreePool();
			var totalOffensive = free.Count + axes.Sum(a => a.Units.Count);

			// 4. How many axes, and which targets (sticky top-k with a hysteresis slack). Held axes already
			//    hold their slots + targets, so the assignable budget is MaxAxes minus the held count.
			var maxAxes = Math.Max(0, Info.MaxAxes - (heldAxes?.Count ?? 0));
			var k = PoiOffenseMath.DesiredAxisCount(totalOffensive, targets.Count,
				unitsPerAxis, minAxisSize, maxAxes);

			var finalTargets = SelectStickyTargets(targets, k);

			// 5. Retire axes whose target didn't survive selection; free their units.
			var keepIds = new HashSet<uint>(finalTargets.Select(t => t.Actor.ActorID));
			for (var i = axes.Count - 1; i >= 0; i--)
			{
				if (!keepIds.Contains(axes[i].TargetId))
				{
					free.AddRange(ReleaseAxis(axes[i], "dropped"));
					axes.RemoveAt(i);
				}
			}

			// 6. Ensure an axis exists for each final target; refresh its scoring.
			foreach (var t in finalTargets)
			{
				var axis = axes.FirstOrDefault(a => a.TargetId == t.Actor.ActorID);
				if (axis == null)
				{
					axis = new Axis { TargetId = t.Actor.ActorID };
					axes.Add(axis);
				}

				axis.TargetCell = t.Location;
				axis.TargetPos = t.CenterPosition;
				axis.Score = t.Score;
				axis.Action = t.Action;
				axis.TargetName = t.Actor.Info.Name;

				// Phase 1: a far-bank water-only target is crewed by amphibious units. Empty map ⇒ always false
				// (byte-identical) when the reachability gate is off.
				axis.AmphibiousTyped = amphibiousTargets.TryGetValue(axis.TargetId, out var amphib) && amphib;
			}

			// 6b. Combat-quality: update each axis's LOSING/RETREAT state from the believed force ratio BEFORE
			//     allocation, so the no-reinforce lever can skip topping up a lost fight (step 8) and CommitAndOrder
			//     can issue a fall-back instead of an assault (step 9). Pure abort-trigger shape — no orders here.
			//     Inert unless a lever is on AND a BeliefStore exists; every axis stays Engaged otherwise (legacy).
			if ((Info.RetreatWhenLosing || Info.NoReinforceLostFights) && beliefStore != null)
				UpdateRetreatStates(tick);

			// 7. Proportional target sizes by score, min axis size enforced.
			var orderedAxes = axes.OrderByDescending(a => a.Score).ThenBy(a => a.TargetId).ToList();
			var sizes = PoiOffenseMath.AllocateProportional(
				orderedAxes.Select(a => a.Score).ToList(), totalOffensive, minAxisSize);

			// 8. Balance each axis to its size: shed surplus to the pool, then top up.
			for (var i = 0; i < orderedAxes.Count; i++)
			{
				var axis = orderedAxes[i];
				var want = sizes[i];

				if (axis.Units.Count > want)
				{
					var surplus = axis.Units
						.OrderByDescending(u => (u.CenterPosition - axis.TargetPos).LengthSquared)
						.Take(axis.Units.Count - want)
						.ToList();
					foreach (var u in surplus)
					{
						axis.Units.Remove(u);
						goalGuard?.Ledger.Release(u);
						free.Add(u);
						axis.HasOrdered = false; // set changed
					}
				}
			}

			foreach (var axis in orderedAxes)
			{
				var i = orderedAxes.IndexOf(axis);
				var want = sizes[i];
				var need = want - axis.Units.Count;
				if (need <= 0)
					continue;

				// Combat-quality lever 2: don't reinforce a lost fight — a losing axis is NOT topped up, so units
				// aren't fed piecemeal into a meat grinder. They stay free for other axes / a rally. Inert unless
				// the lever is on and the axis is in the Retreating state.
				if (Info.NoReinforceLostFights && axis.Retreat == RetreatDecision.Retreating)
					continue;

				// Phase 1: an amphibious-typed axis (far-bank water-only POI) recruits ONLY amphibious-capable
				// units, so we don't send land units that strand at the bank. Falls back to the full pool if
				// there aren't enough amphibious units, rather than leaving the axis empty. Inert (candidates ==
				// free) when the reachability gate is off, so the recruit set is byte-identical.
				var candidates = axis.AmphibiousTyped && crossingMap != null
					? free.Where(IsAmphibiousUnit).ToList()
					: free;
				if (candidates.Count == 0)
					candidates = free;

				var recruits = candidates
					.OrderBy(u => (u.CenterPosition - axis.TargetPos).LengthSquared)
					.ThenBy(u => u.ActorID)
					.Take(need)
					.ToList();

				foreach (var u in recruits)
				{
					free.Remove(u);
					axis.Units.Add(u);
					axis.HasOrdered = false; // set changed
				}
			}

			// 9. Issue orders + (re)commit. Retire any axis that ended up below min size.
			for (var i = axes.Count - 1; i >= 0; i--)
			{
				var axis = axes[i];

				// Keep a RETREATING axis intact even below min size so its fall-back order keeps issuing until it
				// reaches safety (retiring it would drop the units to the pool mid-withdrawal). Only applies when
				// lever 1 is on with a valid rally; otherwise the legacy under-min retire is byte-identical.
				var retreating = CombatRetreatMath.ShouldRetreat(Info.RetreatWhenLosing, axis.Retreat) && rallyCell.HasValue;
				if (axis.Units.Count < minAxisSize && !retreating)
				{
					ReleaseAxis(axis, "under-min");
					axes.RemoveAt(i);
					continue;
				}

				CommitAndOrder(bot, axis, tick);
			}

			// Mission commitment: fold the held (frozen) axes back into the live set. They kept their units,
			// target and in-flight order untouched this eval and already had their ledger TTL refreshed in
			// PartitionHeldAxes. No-op when the flag is off (heldAxes is null).
			if (heldAxes != null && heldAxes.Count > 0)
				axes.AddRange(heldAxes);

			// Phase 1 fires doctrine: give idle artillery a STANDING bombardment mission on believed-static enemy
			// positions BEFORE forward staging, so a piece that can shell a known defence line does so instead of
			// mustering forward empty-handed. Commits each bombarding piece to the shared ledger, so the StageFreePool
			// scan below (and next eval's axis free pool) excludes it. Skipped ⇒ byte-identical (no orders/commits).
			BombardStaticPositions(bot, tick);

			// Phase 2: walk the genuinely-idle reserve (uncommitted, un-axis'd — re-scanned so under-min releases
			// and shed surplus this eval are caught too) to the forward staging point, instead of leaving it idle
			// at the SR clogging the road to the front. Skipped ⇒ byte-identical (reserve keeps its empty activity).
			StageFreePool(bot, tick);

			ReconcileFiresHoldFire(bot);

			Log.Write("debug",
				$"[exp-offense] reeval player={player.PlayerName} pool={totalOffensive} free={free.Count} targets={targets.Count} axes={axes.Count} k={k} tick={tick}");
			foreach (var axis in axes)
				Log.Write("debug",
					$"[exp-offense] axis player={player.PlayerName} target={axis.TargetName}@{axis.TargetCell} action={axis.Action} score={axis.Score} units={axis.Units.Count} tick={tick}");
		}

		// Re-scale each enemy-SR Pressure target's score by SrPressureScoreMultiplier/100, then
		// re-sort by the SAME (score desc, nearer, lower id) order PoiMap.GetOffensiveTargets uses.
		// Only Pressure (enemy Supply Route) axes are touched — Attack/Secure income axes keep
		// their frozen scores, so the rest of the ranking is unchanged. Caller guards multiplier==100.
		List<ScoredPoi> RescaleSrPressure(List<ScoredPoi> targets)
		{
			var scaled = new List<ScoredPoi>(targets.Count);
			foreach (var p in targets)
			{
				if (p.Action == PoiAction.Pressure)
				{
					var newScore = p.Score * Info.SrPressureScoreMultiplier / 100;
					scaled.Add(new ScoredPoi(p.Actor, p.Kind, p.Action, p.Value,
						p.DistanceCells, p.EnemyInfluence, newScore));
				}
				else
					scaled.Add(p);
			}

			scaled.Sort((a, b) => PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
				b.Score, b.DistanceCells, b.Actor.ActorID));
			return scaled;
		}

		// STAGE F strategic repoint. Re-shape each (threat-neutral) offensive/expansion axis score from
		// the BELIEVED fields instead of the omniscient InfluenceMap threat that used to be baked in:
		//   * balance-of-power (terr-bias revival) — ControlField.ScoreAt at the target cell: press cells
		//     we believe we hold / the enemy holds weakly (boost), damp lunging into believed-enemy
		//     strength, leave contested fronts neutral. Reads the SAME GrayBand the field classifies by.
		//   * believed danger — DangerFieldLayer.GroundDanger at the target cell: damp targets sitting in
		//     a believed weapon envelope, the fog-legal stand-in for the old omniscient hostile-threat damp.
		// Both factors are pure (PoiOffenseMath) and draw ZERO random. Caller guards the switch + null
		// control field; re-sorts with the same deterministic comparator PoiMap uses.
		List<ScoredPoi> RescaleByBelievedFields(List<ScoredPoi> targets, int tick)
		{
			var grayBand = controlField.Info.GrayBand;

			// Read the control balance from a ring JUST OUTSIDE the target's own anchor footprint: every
			// enemy target is a site-anchor structure whose own cell (and a disc out to AnchorRadiusCells)
			// is floored ≈ −AnchorStrength, so the target-cell read is always deeply enemy. Sampling one
			// grid cell past that footprint reads the surrounding territory (encircled → boost). One closure
			// alloc per reeval (not per target); the direction set + math are alloc-free.
			var ringRadius = controlField.Info.AnchorRadiusCells + 1;
			Func<int, int, int> scoreAt = (sx, sy) => controlField.ScoreAt(player, sx, sy);

			int boosted = 0, damped = 0, neutral = 0;
			var scaled = new List<ScoredPoi>(targets.Count);
			foreach (var p in targets)
			{
				var (gx, gy) = controlField.MapCellToGridCell(p.Location);
				var controlScore = PoiOffenseMath.NeighborhoodControlScore(scoreAt, gx, gy, ringRadius);
				var bop = PoiOffenseMath.BalanceOfPowerFactor(controlScore, grayBand,
					Info.BopBoostMultiplier, Info.BopDampMultiplier);

				var groundDanger = dangerField != null ? dangerField.GroundDanger(player, p.Location) : 0;
				var dangerMul = PoiOffenseMath.BelievedDangerFactor(groundDanger,
					Info.BelievedDangerMildThreshold, Info.BelievedDangerHostileThreshold,
					Info.BelievedDangerSafeMultiplier, Info.BelievedDangerMildMultiplier,
					Info.BelievedDangerHostileMultiplier);

				var mul = bop * dangerMul / 100;
				if (mul == 100)
				{
					neutral++;
					scaled.Add(p);
					continue;
				}

				if (mul > 100)
					boosted++;
				else
					damped++;

				var newScore = p.Score * mul / 100;
				scaled.Add(new ScoredPoi(p.Actor, p.Kind, p.Action, p.Value,
					p.DistanceCells, p.EnemyInfluence, newScore));

				Log.Write("debug", $"[exp-terr] repoint player={player.PlayerName} target={p.Actor.Info.Name}@{p.Location} " +
					$"action={p.Action} nbhdControl={controlScore} bop={bop} groundDanger={groundDanger} danger={dangerMul} " +
					$"mul={mul} score={p.Score}->{newScore} tick={tick}");
			}

			var wasTop = string.Join(",", targets.Take(Info.MaxAxes).Select(t => t.Actor.ActorID));

			scaled.Sort((a, b) => PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
				b.Score, b.DistanceCells, b.Actor.ActorID));

			var nowTop = string.Join(",", scaled.Take(Info.MaxAxes).Select(t => t.Actor.ActorID));
			if (nowTop != wasTop)
				Log.Write("debug", $"[exp-terr] axis-shift player={player.PlayerName} nowTop={nowTop} wasTop={wasTop} tick={tick}");

			Log.Write("debug", $"[exp-terr] reeval player={player.PlayerName} boosted={boosted} damped={damped} neutral={neutral} tick={tick}");
			return scaled;
		}

		// frontline-influence Phase 1: re-shape each axis score by GROUND reachability from our SR (read from
		// the CrossingMap terrain model) and record which far-bank water-only targets must be crewed by
		// amphibious units. A POI a land force cannot reach is damped so it stops scoring as if adjacent; a
		// POI our amphibious units CAN reach keeps full value and is flagged for amphibious typing. Caller
		// guards ReachabilityGatingEnabled + a non-null CrossingMap, so this is never entered (and
		// amphibiousTargets stays empty) for any other profile ⇒ byte-identical. Re-sorts with the same
		// deterministic comparator PoiMap uses. Zero RNG.
		List<ScoredPoi> RescaleByReachability(List<ScoredPoi> targets)
		{
			var srCell = poiMap.OwnSupplyRoute(player)?.Location;
			if (srCell == null)
				return targets; // no SR anchor ⇒ nothing to measure reachability from; leave scores untouched.

			var hasAmphibiousPool = HasAmphibiousPool();

			int same = 0, intact = 0, repairable = 0, amphib = 0, unreach = 0, typed = 0;
			var scaled = new List<ScoredPoi>(targets.Count);
			foreach (var p in targets)
			{
				var reach = crossingMap.ClassifyGroundReach(srCell.Value, p.Location);
				var amphibReachable = crossingMap.AmphibiousReachable(srCell.Value, p.Location);

				// Phase 1.5 per-POI diagnostic (default OFF — see DebugReachLogging). p.DistanceCells already
				// carries the through-crossing distance PoiMap substituted (crow-flies for same-bank POIs). The
				// barrier line-walk + string interpolation run ONLY when the flag is on, so a normal match pays
				// nothing per POI here.
				if (Info.DebugReachLogging)
				{
					var crossesBarrier = crossingMap.CrossesGroundBarrier(srCell.Value, p.Location);
					Log.Write("debug", $"[exp-reach-dist] player={player.PlayerName} target={p.Actor.Info.Name}@{p.Location} " +
						$"dist={p.DistanceCells} crossesBarrier={crossesBarrier} reach={reach} score={p.Score} tick={world.WorldTick}");
				}

				switch (reach)
				{
					case GroundReach.Same: same++; break;
					case GroundReach.IntactCrossing: intact++; break;
					case GroundReach.RepairableCrossing: repairable++; break;
					case GroundReach.AmphibiousOnly: amphib++; break;
					default: unreach++; break;
				}

				if (PoiReachabilityMath.ShouldTypeAmphibious(reach, amphibReachable, hasAmphibiousPool))
				{
					amphibiousTargets[p.Actor.ActorID] = true;
					typed++;
				}

				var mul = PoiReachabilityMath.ReachabilityFactor(reach, amphibReachable, hasAmphibiousPool,
					Info.ReachabilityRepairableMultiplier, Info.ReachabilityAmphibiousMultiplier,
					Info.ReachabilityUnreachableMultiplier);

				if (mul == 100)
				{
					scaled.Add(p);
					continue;
				}

				var newScore = p.Score * mul / 100;
				scaled.Add(new ScoredPoi(p.Actor, p.Kind, p.Action, p.Value,
					p.DistanceCells, p.EnemyInfluence, newScore));

				if (Info.DebugReachLogging)
					Log.Write("debug", $"[exp-reach] player={player.PlayerName} target={p.Actor.Info.Name}@{p.Location} " +
						$"reach={reach} amphibReachable={amphibReachable} hasAmphib={hasAmphibiousPool} mul={mul} " +
						$"score={p.Score}->{newScore} tick={world.WorldTick}");
			}

			scaled.Sort((a, b) => PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
				b.Score, b.DistanceCells, b.Actor.ActorID));

			// One unconditional per-reeval summary line (cheap, continuity). Per-POI barrier counts are only
			// tallied under DebugReachLogging, so barrierCrossed is reported there, not here.
			Log.Write("debug", $"[exp-reach] reeval player={player.PlayerName} pois={targets.Count} same={same} " +
				$"intactCrossing={intact} repairable={repairable} amphibiousOnly={amphib} unreachable={unreach} " +
				$"amphibTyped={typed} crossingCells={crossingMap.CrossingCellCount} " +
				$"hasAmphibPool={hasAmphibiousPool} tick={world.WorldTick}");
			return scaled;
		}

		// frontline-influence Phase 5: weakest-point attack bias. Multiply each axis score by
		// FrontlineAllocationMath.WeakestSectorBiasFactor — a >100 boost for a target sitting in the believed-
		// weakest enemy frontier sector, 100 (neutral) everywhere else. The believed-weakest sector and the
		// target's sector are both deterministic ControlField reads (fog-legal — belief-side profile only). This
		// is a BIAS: the SAME PoiScoring comparator re-sorts, so an un-built profile (WeakestEnemySector == −1)
		// or a bare enable (multiplier 100, filtered by the caller) leaves the ranking byte-identical. Zero RNG.
		List<ScoredPoi> RescaleByWeakestSector(List<ScoredPoi> targets, int tick)
		{
			var weakest = controlField.WeakestEnemySector(player);
			if (weakest == FrontlineProfileMath.NoSector)
				return targets; // no believed front ⇒ nothing to bias toward; ranking untouched.

			var boosted = 0;
			var scaled = new List<ScoredPoi>(targets.Count);
			foreach (var p in targets)
			{
				var sector = SectorOfCell(p.Location);
				var mul = FrontlineAllocationMath.WeakestSectorBiasFactor(sector, weakest, Info.WeakestPointBiasMultiplier);
				if (mul == 100)
				{
					scaled.Add(p);
					continue;
				}

				boosted++;
				var newScore = p.Score * mul / 100;
				scaled.Add(new ScoredPoi(p.Actor, p.Kind, p.Action, p.Value, p.DistanceCells, p.EnemyInfluence, newScore));
			}

			scaled.Sort((a, b) => PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
				b.Score, b.DistanceCells, b.Actor.ActorID));

			Log.Write("debug", $"[exp-weakpoint] reeval player={player.PlayerName} weakestSector={weakest} " +
				$"boosted={boosted} mul={Info.WeakestPointBiasMultiplier} tick={tick}");
			return scaled;
		}

		// The frontier sector a MAP cell falls into (its X column bucketed into the control field's equal-width
		// vertical bands) — the same partition FrontlineProfileMath uses to build the profile, so the returned
		// sector index lines up with WeakestEnemySector / SectorProfile. Used for both a target cell (weakest-point
		// bias) and an axis's own centroid cell (posture hold). Deterministic, fog-legal (map geometry only).
		// Returns NoSector when no control field exists.
		int SectorOfCell(CPos cell)
		{
			if (controlField == null)
				return FrontlineProfileMath.NoSector;

			return FrontlineProfileMath.SectorOfMapCellX(cell.X, controlField.Info.CellSize,
				controlField.GridWidth, controlField.FrontlineSectorCount);
		}

		// Does the free pool contain at least one amphibious-capable combat unit? Cheap scan over eligible
		// units; only called on the reachability path (gate on). A unit is amphibious iff its Mobile
		// locomotor can cross water (CrossingMap.IsAmphibiousLocomotor).
		bool HasAmphibiousPool()
		{
			foreach (var a in world.Actors)
				if (IsEligibleCombatUnit(a) && IsAmphibiousUnit(a))
					return true;
			return false;
		}

		// True when the actor's Mobile locomotor is water-capable (amphibious). False for aircraft / units
		// with no Mobile. Guarded by a non-null crossingMap at the call sites.
		bool IsAmphibiousUnit(Actor a)
		{
			var loco = a.TraitOrDefault<Mobile>()?.Info.Locomotor;
			return loco != null && crossingMap.IsAmphibiousLocomotor(loco);
		}

		// MISSION COMMITMENT: remove every axis still HOLDING its mission from `axes` (returning them so the
		// caller re-adds them after the reshuffle), refresh their ledger claims, and strip their targets from
		// the candidate list so no duplicate axis forms. An axis is held unless MissionCommitmentMath fires an
		// abort trigger. Returns null (and touches nothing) when the flag is off / no ledger / no axes — that
		// path is byte-identical to the pre-change module. Deterministic: reverse index walk, zero RNG.
		List<Axis> PartitionHeldAxes(ref List<ScoredPoi> targets, int tick)
		{
			if (!Info.MissionCommitmentEnabled || goalGuard == null || axes.Count == 0)
				return null;

			List<Axis> held = null;
			HashSet<uint> heldIds = null;

			for (var i = axes.Count - 1; i >= 0; i--)
			{
				var axis = axes[i];

				// A brand-new axis that has not yet issued an order has no baseline to test — let it flow
				// through the normal assignment path (it snapshots when CommitAndOrder runs this eval).
				if (!axis.Committed)
					continue;

				// N3 lever composition: a LOSING (or already-retreating) axis must NOT be frozen by the
				// mission-commitment hold — release it into the live set so the retreat FSM (step 6b) can step it,
				// otherwise the force-ratio retreat would only fire on non-held axes (i.e. after the attrition it
				// exists to prevent). Same fog-legal force-ratio signal UpdateRetreatStates uses. Gated on
				// RetreatWhenLosing + a belief store, so byte-identical when the retreat lever is off (and when only
				// MissionCommitmentEnabled is on the release never fires ⇒ hold behaviour unchanged).
				if (Info.RetreatWhenLosing && beliefStore != null)
				{
					var own = OwnAxisStrength(axis);
					var enemy = BelievedEnemyStrength(AxisCentroidCell(axis));
					if (CombatRetreatMath.ShouldReleaseHeld(true, axis.Retreat, own, enemy, Info.RetreatForceRatioPct))
						continue; // release — do not hold a losing / retreating axis
				}

				var scored = targets.FirstOrDefault(t => t.Actor.ActorID == axis.TargetId);
				var objectiveValid = scored.Actor != null;
				var currentScore = objectiveValid ? scored.Score : 0;
				var currentDanger = dangerField != null ? dangerField.GroundDanger(player, axis.TargetCell) : 0;
				var currentStrength = axis.Units.Count;
				var bestAlt = BestAlternativeScore(targets, axis.TargetId);

				// Trigger-3 material-improvement margin, shifted by the Aggressiveness slider (1d) — inert at the
				// default slope 0 / knob 50 — and compared on believed-field-QUANTIZED scores (1c, band pct below).
				// Both are same-KIND by construction: every entry in `targets` is an offensive POI scored under the
				// one offense factor stack, so the FIX-7 "same-kind only" rule needs no extra filter here.
				var betterOppMargin = PoiOffenseMath.ShiftByKnob(
					Info.MissionBetterOppMarginPct, Info.Aggressiveness, Info.MissionBetterOppMarginSlopePct);

				var reassign = MissionCommitmentMath.ShouldReassign(
					objectiveValid,
					axis.CommitTick, tick, Info.MissionCommitmentWindowTicks,
					axis.CommitDanger, currentDanger, Info.MissionDangerSpikePct, Info.MissionDangerSpikeFloor,
					currentScore, bestAlt, betterOppMargin, Info.MissionScoreQuantizeBandPct,
					axis.CommitStrength, currentStrength,
					Info.MissionIneffectiveNumerator, Info.MissionIneffectiveDenominator);

				if (reassign)
					continue; // release to the normal reshuffle / re-task path

				// HELD: keep the mission. Refresh the ledger claim so the units stay ours and BuildFreePool
				// excludes them; keep any FiresEvGate-held pieces held (their axis skips this eval's fires
				// reconciliation, so mark them so ReconcileFiresHoldFire doesn't restore FireAtWill).
				var key = OffenseObjectiveKey(axis.TargetId);
				foreach (var u in axis.Units)
				{
					goalGuard.Ledger.Commit(u, key, tick, Info.AxisCommitmentTicks);
					if (firesHeldFire.Contains(u))
						firesHeldThisEval.Add(u);
				}

				(held ??= new List<Axis>()).Add(axis);
				(heldIds ??= new HashSet<uint>()).Add(axis.TargetId);
				axes.RemoveAt(i);

				Log.Write("debug",
					$"[exp-offense] hold player={player.PlayerName} target={axis.TargetName}@{axis.TargetCell} " +
					$"units={currentStrength} commitScore={axis.CommitScore} score={currentScore} " +
					$"commitDanger={axis.CommitDanger} danger={currentDanger} tick={tick}");
			}

			if (heldIds != null)
				targets = targets.Where(t => !heldIds.Contains(t.Actor.ActorID)).ToList();

			return held;
		}

		// Highest score among the scored targets that is NOT `excludeId` — the best rival objective a
		// committed axis could switch to. `targets` arrives score-desc, so the first non-excluded entry
		// is the max. 0 when no other target exists. Pure.
		static long BestAlternativeScore(List<ScoredPoi> targets, uint excludeId)
		{
			foreach (var t in targets)
				if (t.Actor.ActorID != excludeId)
					return t.Score;

			return 0;
		}

		// Sticky top-k selection with hysteresis: start from the score-ordered targets,
		// but if an existing axis's target is only marginally out of the top-k, keep it
		// instead of swapping for the newcomer (avoids axis thrash on score jitter).
		List<ScoredPoi> SelectStickyTargets(List<ScoredPoi> targets, int k)
		{
			if (k <= 0)
				return new List<ScoredPoi>();

			var top = targets.Take(k).ToList();
			if (axes.Count == 0 || top.Count < k)
				return top;

			var topIds = new HashSet<uint>(top.Select(t => t.Actor.ActorID));
			var cutoffScore = top[top.Count - 1].Score;

			foreach (var axis in axes)
			{
				if (topIds.Contains(axis.TargetId))
					continue;

				// The existing axis is out of the top-k. Find it in the full ranking.
				var existing = targets.FirstOrDefault(t => t.Actor.ActorID == axis.TargetId);
				if (existing.Actor == null)
					continue; // target gone — let it be dropped

				// Keep the existing axis unless the marginal newcomer clearly beats it.
				if (!PoiOffenseMath.ScoreBeatsByThreshold(cutoffScore, existing.Score, Info.ReassignScoreThresholdPct))
				{
					// Swap: drop the weakest newcomer, retain the sticky existing target.
					top[top.Count - 1] = existing;
					topIds = new HashSet<uint>(top.Select(t => t.Actor.ActorID));
					cutoffScore = top.Min(t => t.Score);
				}
			}

			return top;
		}

		List<Actor> BuildFreePool()
		{
			var tick = world.WorldTick;
			var claimedByAxis = new HashSet<Actor>(axes.SelectMany(a => a.Units));

			return world.Actors
				.Where(a => IsEligibleCombatUnit(a)
					&& !claimedByAxis.Contains(a)
					&& (goalGuard == null || !goalGuard.Ledger.IsCommitted(a, tick)))
				.ToList();
		}

		// Phase 2: resolve this eval's forward staging anchor — a safe standoff BEHIND the believed friendly
		// frontier. Walks from the SR grid cell DOWN the control field's distance-to-enemy-frontier gradient
		// toward the nearest front (steepest descent, staying out of believed danger envelopes), then applies
		// Chebyshev hysteresis so the anchor doesn't jitter on a 1-cell field wobble. Returns null when there is
		// no field / no SR, or the descent stays at the SR (flat/unpopulated field, or the front is already at the
		// SR) — in which case the reserve idles at the SR exactly as the legacy path would. Pure ForwardStagingMath.
		CPos? ResolveStagingAnchor()
		{
			if (controlField == null || !rallyCell.HasValue)
			{
				lastStagingAnchor = null;
				return null;
			}

			var (sgx, sgy) = controlField.MapCellToGridCell(rallyCell.Value);
			var (agx, agy) = ForwardStagingMath.StagingCell(sgx, sgy,
				Info.StagingStandoffCells, Info.StagingDangerSafeThreshold, Info.StagingMaxDescentSteps,
				(gx, gy) => controlField.FrontierDistanceAt(player, gx, gy),
				(gx, gy) => dangerField != null ? dangerField.GroundDanger(player, controlField.GridCellToMapCell(gx, gy)) : 0,
				(gx, gy) => gx >= 0 && gx < controlField.GridWidth && gy >= 0 && gy < controlField.GridHeight);

			var candidate = controlField.GridCellToMapCell(agx, agy);

			// Descent stayed at the SR ⇒ no forward gradient (field unpopulated, or the front is on top of us):
			// no staging this eval, reset the hysteresis memory so a later populated field re-adopts cleanly.
			if (candidate == rallyCell.Value)
			{
				lastStagingAnchor = null;
				return null;
			}

			// Hysteresis: keep the previously-adopted anchor unless the new one advanced past the threshold.
			if (lastStagingAnchor.HasValue
				&& !ForwardStagingMath.AnchorShifted(lastStagingAnchor.Value.X, lastStagingAnchor.Value.Y,
					candidate.X, candidate.Y, Info.StagingHysteresisCells))
				return lastStagingAnchor;

			lastStagingAnchor = candidate;
			return candidate;
		}

		// Phase 2: walk the genuinely-idle reserve to the forward staging anchor, fanned out over a deterministic
		// ring so it doesn't pile on one cell. The idle set is re-scanned via BuildFreePool (excludes axis-claimed
		// AND ledger-committed units, so retreating/held axes are never staged), catching units released under-min
		// or shed as surplus this eval. A staging move is (re)issued only when a unit's target cell changed (newly
		// idle, or the anchor advanced) so a unit already walking up keeps its order. Units are NOT ledger-committed
		// — staging is a soft muster, so a staged unit is fully re-eligible for an axis next eval. Skipped entirely
		// when ForwardStagingEnabled is off ⇒ the reserve keeps its empty activity (idles at the SR), byte-identical.
		void StageFreePool(IBot bot, int tick)
		{
			if (!Info.ForwardStagingEnabled)
				return;

			var idle = BuildFreePool();

			// Prune the staged-cell memory to units still idle + ours (so a re-recruited/dead unit drops out).
			if (stagedCells.Count > 0)
			{
				var live = new HashSet<Actor>(idle);
				List<Actor> stale = null;
				foreach (var a in stagedCells.Keys)
					if (a.IsDead || !a.IsInWorld || a.Owner != player || !live.Contains(a))
						(stale ??= new List<Actor>()).Add(a);

				if (stale != null)
					foreach (var a in stale)
						stagedCells.Remove(a);
			}

			if (!stagingAnchor.HasValue || idle.Count == 0)
				return;

			var anchor = stagingAnchor.Value;

			// Bound the fan-out so the widest ring radius (maxRings * StagingSpreadStepCells map cells) stays
			// STRICTLY inside the standoff (StagingStandoffCells coarse cells = *CellSize map cells) — so a spread
			// slot can never sit forward of the frontier the anchor descent already cleared of believed danger
			// (SpreadCell is not danger-guarded per cell; this is the invariant it documents). standoffMapCells-1
			// keeps radius < standoff even at the outermost ring.
			var standoffMapCells = Info.StagingStandoffCells * controlField.Info.CellSize;
			var maxRings = Math.Max(0, (standoffMapCells - 1) / Math.Max(1, Info.StagingSpreadStepCells));

			// Iterate ActorID-sorted (deterministic order), but slot each unit by a STABLE per-unit key
			// (StableSlot(ActorID)) rather than its list position — so a pool-composition change re-slots nobody
			// else (no order churn). Collisions (two ids sharing a slot) just share a cell.
			var ordered = idle.OrderBy(u => u.ActorID).ToList();
			var staged = 0;
			foreach (var u in ordered)
			{
				var slot = ForwardStagingMath.StableSlot(u.ActorID, maxRings);
				var (cx, cy) = ForwardStagingMath.SpreadCell(anchor.X, anchor.Y, slot, Info.StagingSpreadStepCells,
					(mx, my) => world.Map.Contains(new CPos(mx, my)));
				var target = new CPos(cx, cy);

				if (stagedCells.TryGetValue(u, out var prev) && prev == target)
					continue;

				bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, target), false, groupedActors: new[] { u }));
				stagedCells[u] = target;
				staged++;
			}

			if (staged > 0)
				Log.Write("debug",
					$"[exp-staging] player={player.PlayerName} anchor={anchor} idle={idle.Count} staged={staged} tick={tick}");
		}

		bool IsEligibleCombatUnit(Actor a)
		{
			if (a.Owner != player || a.IsDead || !a.IsInWorld)
				return false;
			if (!a.Info.HasTraitInfo<IPositionableInfo>() || !a.Info.HasTraitInfo<AttackBaseInfo>())
				return false;
			if (a.Info.HasTraitInfo<AircraftInfo>())
				return false;

			// An out-of-ammo unit is evacuating (RotateToEdge); recruiting it cancels the evac
			// and sends an empty unit at the enemy. Skip it until it resupplies. Default-off so
			// the frozen controls are untouched — only @experimental sets SkipOutOfAmmoUnits.
			if (Info.SkipOutOfAmmoUnits && IsOutOfAmmo(a))
				return false;

			// Role-model eligibility: MainBattle line units plus IndirectFire artillery (kept until a
			// dedicated fires executor exists, else artillery orphans — design §6). SHORAD/MANPADS,
			// capturers, logistics and scouts drop out by class. Cargo carriers (bradley/bmp2/m113) stay
			// owned by MountedTransportBotModule even though the IFVs classify MainBattle by override, so
			// this partial migration excludes any cargo-carrier by trait. See WORKSPACE/DISCOVERIES.md.
			if (Info.UseUnitRoles && resolver != null)
			{
				var role = resolver.GetRole(a);
				return (role == UnitRole.MainBattle || role == UnitRole.IndirectFire)
					&& !UnitRoleResolver.IsTroopCarrier(a.Info);
			}

			return !Info.ExcludeUnitTypes.Contains(a.Info.Name);
		}

		// "Out of ammo" = the unit has AmmoPool traits AND every pool is empty. Units with no
		// AmmoPool always return false; partial-ammo units (one pool empty, another full) too.
		// Copied verbatim from LayeredDefenceBotModule so both modules share the same predicate.
		static bool IsOutOfAmmo(Actor actor)
		{
			var pools = actor.TraitsImplementing<AmmoPool>().ToList();
			if (pools.Count == 0)
				return false;
			return pools.All(p => p.CurrentAmmoCount == 0);
		}

		// Remove units that died / changed owner / lost their axis commitment.
		void PruneAxes()
		{
			var tick = world.WorldTick;
			foreach (var axis in axes)
			{
				var key = OffenseObjectiveKey(axis.TargetId);
				axis.Units.RemoveAll(u =>
				{
					if (u.IsDead || !u.IsInWorld || u.Owner != player)
						return true;

					// A unit that emptied mid-axis must leave to evacuate — otherwise it stays
					// committed at the objective forever. Release its ledger claim inline (PruneAxes
					// only trims the list; ReleaseAxis isn't called here). Default-off with the guard.
					if (Info.SkipOutOfAmmoUnits && IsOutOfAmmo(u))
					{
						goalGuard?.Ledger.Release(u);
						return true;
					}

					// A committed-but-reclaimed unit (objective no longer ours) leaves.
					if (goalGuard != null
						&& goalGuard.Ledger.TryGetObjective(u, out var obj)
						&& obj != key
						&& obj != null
						&& obj.StartsWith("offense:", StringComparison.Ordinal))
						return true;

					return false;
				});
			}
		}

		void CommitAndOrder(IBot bot, Axis axis, int tick)
		{
			// (Re)commit every unit to this axis so the shared ledger keeps them ours. (Retreating units stay
			// committed too — they are ours, just withdrawing — so nothing else re-recruits them mid-retreat.)
			if (goalGuard != null)
			{
				var key = OffenseObjectiveKey(axis.TargetId);
				foreach (var u in axis.Units)
					goalGuard.Ledger.Commit(u, key, tick, Info.AxisCommitmentTicks);
			}

			// Combat-quality lever 1: a LOSING axis falls back toward friendly control instead of assaulting. A
			// single grouped AttackMove to the rally cell (attack-move ⇒ units still defend themselves while
			// withdrawing) replaces the assault order, and the fires/echelon/detour assault machinery is skipped
			// entirely. Gated on a valid rally target; inert unless RetreatWhenLosing is on and the axis is
			// Retreating, so every other profile / the frozen @stable twin takes the assault path below unchanged.
			// ORDER IS LOAD-BEARING: this runs BEFORE the mission-commitment snapshot below and RETURNS, so a
			// retreating axis is never marked Committed — otherwise PartitionHeldAxes would freeze it retreating.
			if (CombatRetreatMath.ShouldRetreat(Info.RetreatWhenLosing, axis.Retreat) && rallyCell.HasValue)
			{
				OrderRetreat(bot, axis, rallyCell.Value, tick);
				return;
			}

			// Phase 3 retreat-oscillation damper (experimental, default off; builds on RetreatWhenLosing). HOLD an
			// axis at the muster point instead of RE-advancing in two cases — NEITHER delays a genuine retreat (the
			// retreat check above already returned for a Retreating axis), so a truly-losing axis still withdraws:
			//   (a) post-retreat DWELL — an axis that JUST completed a retreat (ReadvanceHold > 0) waits before it
			//       re-advances on the same target, converting the small-axis advance/lose/retreat ping-pong into
			//       hold-then-push-as-a-group.
			//   (b) advance-STRENGTH floor — an axis still massing near the rally (NearRally) whose own force is
			//       below MinAdvanceStrength holds/merges rather than trickling 2-3 units forward into the enemy.
			// Held at the forward staging anchor when Phase-2 staging is on (off the SR road), else the rally cell.
			// Reuses OrderRetreat's gated grouped AttackMove; runs BEFORE the mission-commitment snapshot + RETURNS,
			// so a damped axis is never marked Committed (same discipline as the retreat above). Inert when off.
			if (Info.RetreatDamperEnabled && rallyCell.HasValue && DamperShouldHold(axis))
			{
				OrderRetreat(bot, axis, stagingAnchor ?? rallyCell.Value, tick);
				return;
			}

			// frontline-influence Phase 5 SECTOR POSTURE HOLD: where the believed profile reads this axis's target
			// sector as TOO STRONG (enemy force >= SectorPostureHoldRatioPct% of our own believed strength there),
			// hold/defend instead of pressing into believed strength. Reuses the SAME grouped fall-back order as the
			// retreat/damper (no competing writer), held at the forward staging anchor when Phase-2 staging is on
			// else the rally cell. Placed AFTER the genuine-retreat gate (which already RETURNED for a Retreating
			// axis) and the damper — so it can NEVER convert a truly-losing withdrawal into a hold. Runs BEFORE the
			// mission-commitment snapshot + RETURNS, so a held axis is never marked Committed (same discipline).
			// Inert unless the flag is on with a valid rally AND the profile reads the sector as overmatched.
			if (Info.SectorPostureHoldEnabled && rallyCell.HasValue && PostureShouldHold(axis))
			{
				OrderRetreat(bot, axis, stagingAnchor ?? rallyCell.Value, tick);
				return;
			}

			// Just left the retreat state (recovered / reached safety): the last order was a fall-back, so force
			// the assault order below to re-issue rather than assume the stale attack order still holds.
			if (axis.OrderedRetreat)
			{
				axis.OrderedRetreat = false;
				axis.HasOrdered = false;
			}

			// Mission commitment: snapshot the baseline this (re)tasking commits to. Any axis reaching
			// CommitAndOrder is being freshly assigned or re-tasked (a HELD axis is pulled out before here),
			// so its baseline resets to the current score / believed danger / squad size. Next eval
			// PartitionHeldAxes tests the abort triggers against this snapshot. Guarded ⇒ inert when off.
			if (Info.MissionCommitmentEnabled)
			{
				axis.Committed = true;
				axis.CommitTick = tick;
				axis.CommitScore = axis.Score;
				axis.CommitDanger = dangerField != null ? dangerField.GroundDanger(player, axis.TargetCell) : 0;
				axis.CommitStrength = axis.Units.Count;
			}

			// Fires doctrine (experimental, default off): peel IndirectFire artillery off the line group and
			// hold each at its own weapon standoff. When off (or no resolver) groupUnits IS axis.Units by
			// reference, so the whole block below is byte-identical to the pre-fires path.
			var groupUnits = axis.Units;
			if (Info.FiresStandoff && resolver != null)
			{
				List<Actor> fires = null;
				foreach (var u in axis.Units)
				{
					if (resolver.GetRole(u) != UnitRole.IndirectFire)
						continue;

					// A degenerate piece with no live weapon has no standoff to compute — leave it in the
					// line group rather than orphaning it with no order.
					if (MaxWeaponRange(u) <= 0)
						continue;

					fires ??= new List<Actor>();
					fires.Add(u);
				}

				if (fires != null)
				{
					// The SCREEN is every axis member that is NOT a fires piece — the MainBattle tanks/infantry
					// the echelon holds artillery behind. Computed before the standoff so OrderFiresStandoff can
					// anchor each piece relative to it (echelon) or fall back to the target ring (no screen).
					var firesSet = new HashSet<Actor>(fires);
					var screen = axis.Units.Where(u => !firesSet.Contains(u)).ToList();
					OrderFiresStandoff(bot, axis, fires, screen, tick);
					groupUnits = screen;

					// Pure-artillery axis: no line group to march. The standoff orders above stand alone.
					if (groupUnits.Count == 0)
						return;
				}
			}

			// Axis spacing geometry (pure Chebyshev, cheap for N<=8) — computed for every
			// axis so the clumpRadius telemetry gives a baseline for the frozen controls too.
			var cells = new List<(int X, int Y)>(groupUnits.Count);
			foreach (var u in groupUnits)
				cells.Add((u.Location.X, u.Location.Y));

			var centroid = PoiOffenseMath.CellCentroid(cells);
			var distToTarget = PoiOffenseMath.Chebyshev(centroid.X, centroid.Y, axis.TargetCell.X, axis.TargetCell.Y);
			var clumpRadius = PoiOffenseMath.MaxChebyshev(cells, centroid.X, centroid.Y);

			// Stage-E flow-around: when the axis centroid's straight approach to the objective crosses
			// ground-danger above the threshold (a defended strongpoint / choke), route it through a
			// lateral waypoint that lowers the worst-case exposure — attacks skirt kill zones instead of
			// grinding head-on. null ⇒ the beeline is clear (or routing off / no field), go direct.
			// Inert unless DangerFieldRouting is on AND a field exists, so every other profile is untouched.
			CPos? detourVia = null;
			if (dangerField != null)
			{
				var ground = GroundDangerSampler(dangerField);
				var passable = WaypointPassable(groupUnits[0]);
				detourVia = GroundDangerNav.DetourWaypoint(
					new CPos(centroid.X, centroid.Y), axis.TargetCell,
					Info.GroundDangerDetourCells, Info.GroundDangerDetourSteps,
					Info.GroundDangerSafeThreshold, ground, passable);
			}

			// Dispersion doctrine — spread to move, mass to assault. OFF for the frozen
			// Stable/Normal controls (CohesionSwitchEnabled=false): they keep the original
			// single-formation AttackMove untouched — no SetCohesion, no cohesion-forced repath.
			// When on, gate on the centroid's distance to the target: far ⇒ ApproachCohesion
			// (fan out crossing empty ground), near ⇒ AssaultCohesion (mass at the objective).
			var dispersion = Info.CohesionSwitchEnabled && groupUnits.Count > 0;
			var wantMode = distToTarget > Info.AssaultRadiusCells ? Info.ApproachCohesion : Info.AssaultCohesion;

			// A unit needs a fresh SetCohesion only when its desired mode actually changed —
			// avoids re-issuing the stance every re-eval for units already in the right mode.
			var cohesionChanged = false;
			if (dispersion)
			{
				foreach (var u in groupUnits)
				{
					if (!lastCohesion.TryGetValue(u, out var have) || have != wantMode)
					{
						cohesionChanged = true;
						break;
					}
				}
			}

			// Re-path when the unit set changed, the target moved enough, the desired cohesion changed
			// (e.g. the axis just crossed the assault radius) so the new formation takes effect
			// immediately, OR the Stage-E lateral waypoint shifted enough (the axis advanced past the
			// strongpoint, so the flow-around lane must be recomputed) — all bounded by RepathThreshold.
			var moved = !axis.HasOrdered
				|| cohesionChanged
				|| (axis.OrderedCell - axis.TargetCell).LengthSquared >= Info.RepathThresholdCells * Info.RepathThresholdCells
				|| ViaChanged(axis.OrderedVia, detourVia, Info.RepathThresholdCells);
			if (!moved)
				return;

			// Queue each needed SetCohesion BEFORE the grouped AttackMove. The bot order queue
			// drains FIFO (ModularBot), so SetCohesion resolves first and CohesionMoveModifier
			// reads the updated CohesionValue when it lays out the AttackMove formation.
			if (dispersion)
			{
				foreach (var u in groupUnits)
				{
					if (lastCohesion.TryGetValue(u, out var have) && have == wantMode)
						continue;

					bot.QueueOrder(new Order("SetCohesion", u, false) { ExtraData = (uint)wantMode });
					lastCohesion[u] = wantMode;
				}
			}

			var units = groupUnits.ToArray();

			// Stage-E: when a flow-around waypoint was chosen, attack-move to the lateral lane FIRST
			// (queued: false) then chain the objective (queued: true) so the axis skirts the strongpoint
			// and still presses on to the target. No waypoint ⇒ the single direct AttackMove, unchanged.
			if (detourVia.HasValue)
				bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, detourVia.Value), false, groupedActors: units));

			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, axis.TargetCell), detourVia.HasValue, groupedActors: units));
			axis.OrderedCell = axis.TargetCell;
			axis.OrderedVia = detourVia;
			axis.HasOrdered = true;

			var viaLog = detourVia.HasValue ? $" via={detourVia.Value}" : "";
			var cohesionLog = dispersion ? $" cohesion={wantMode}" : "";
			Log.Write("debug",
				$"[exp-offense] order player={player.PlayerName} target={axis.TargetName}@{axis.TargetCell} action={axis.Action} units={units.Length}{cohesionLog}{viaLog} clumpRadius={clumpRadius} distToTarget={distToTarget} tick={tick}");
			AIUtils.BotDebug("AI ({0}): exp-offense — axis {1}@{2} ({3} units, score={4})",
				player.ClientIndex, axis.TargetName, axis.TargetCell, units.Length, axis.Score);
		}

		// Fires doctrine: hold each IndirectFire piece at its own weapon standoff from the axis target.
		// A single AttackMove to the standoff anchor yields all three behaviours (advance to range, hold and
		// fire, back off when the target closes) from the shared, tested AttackMove -> AutoTarget path — the
		// ground twin of the Stage-0 heli standoff. Re-issue is gated so an in-band piece keeps firing
		// uninterrupted: only (re)order when it must reposition (out of band) or its anchor drifted past
		// RepathThresholdCells. Deterministic — pure integer geometry, no random draws.
		void OrderFiresStandoff(IBot bot, Axis axis, List<Actor> fires, List<Actor> screen, int tick)
		{
			var margin = Info.FiresStandoffMargin.Length;
			var hysteresis = Info.FiresStandoffHysteresis.Length;
			var floor = Info.FiresStandoffFloor.Length;
			var repathSq = Info.RepathThresholdCells * Info.RepathThresholdCells;

			// Defence-in-depth echelon (experimental, default off): when a live screen exists, anchor each
			// piece behind the screen LINE instead of at the target ring. The screen reference is precomputed
			// once — its centroid (order-independent sum) and its engagement range (order-independent max
			// weapon reach) — so the per-piece anchors are byte-identical without an ActorID sort. With the
			// echelon off, or no screen to hide behind, hasScreen stays false and every piece uses the exact
			// pre-echelon target-standoff path below (byte-identical).
			var hasScreen = false;
			var screenLine = WPos.Zero;
			var screenRange = 0;
			if (Info.EchelonPositioning && screen != null && screen.Count > 0)
			{
				long sx = 0, sy = 0;
				var maxScreenRange = 0;
				foreach (var s in screen)
				{
					var sp = s.CenterPosition;
					sx += sp.X;
					sy += sp.Y;
					var sr = MaxWeaponRange(s);
					if (sr > maxScreenRange)
						maxScreenRange = sr;
				}

				screenLine = new WPos((int)(sx / screen.Count), (int)(sy / screen.Count), 0);
				screenRange = maxScreenRange > 0 ? maxScreenRange : Info.EchelonScreenRangeFallback.Length;
				hasScreen = true;
			}

			foreach (var u in fires)
			{
				// Fires EV gate (item 19): decide the piece's fire stance BEFORE the standoff-positioning
				// continues below, so an in-band piece that holds position is still gated. Rocket pieces hold
				// fire on unworthy targets; tube pieces (and every piece when the gate is off) are untouched.
				if (Info.FiresEvGate && resolver != null && resolver.GetIndirectKind(u) == IndirectFireKind.Rocket)
					ApplyFiresStance(bot, u, RocketFireWorthy(u));

				var maxRange = MaxWeaponRange(u);
				if (maxRange <= 0)
					continue;

				var pos = u.CenterPosition;

				// Echelon when there is a screen to hide behind; otherwise the target-relative standoff (the
				// override: a solo / no-screen fires tasking goes where the mission needs it).
				WPos anchor;
				bool needs;
				if (hasScreen)
				{
					var depth = EchelonMath.EchelonDepth(maxRange, screenRange, Info.EchelonBuffer.Length, Info.EchelonMinDepth.Length);
					anchor = EchelonMath.EchelonAnchor(screenLine, axis.TargetPos, depth);

					// Frontier standoff (experimental, default off): if the echelon anchor still sits within
					// MinFrontierDistanceCells of the believed enemy frontier, walk it rearward along the anchor
					// axis until it clears — so the piece holds BEHIND the believed front, not on it. Inert until
					// a ControlField is populated for this player (FrontierDistanceAt reads the 'far' sentinel ⇒
					// zero steps ⇒ byte-identical to the pre-frontier anchor).
					if (controlField != null && Info.MinFrontierDistanceCells > 0)
						anchor = PushEchelonBehindFrontier(anchor, axis.TargetPos);

					needs = EchelonMath.NeedsReposition(anchor, pos, Info.EchelonTolerance.Length);
				}
				else
				{
					anchor = FiresStandoffMath.StandoffAnchor(axis.TargetPos, pos, maxRange, margin, floor);
					needs = FiresStandoffMath.NeedsReposition(axis.TargetPos, pos, maxRange, margin, hysteresis, floor);
				}

				// Clamp the anchor to a cell the piece can actually stand on (mirrors the group path's
				// WaypointPassable guard). An impassable anchor would degrade the AttackMove to some
				// engine-chosen reachable cell out-of-band, and the piece would then be re-ordered to the same
				// unreachable anchor every re-eval, cancelling in-flight shots. The nearest-passable fallback
				// keeps the destination reachable and near the standoff ring. Deterministic.
				var idealCell = world.Map.CellContaining(anchor);
				var anchorCell = FiresStandoffMath.NearestPassableCell(idealCell, FiresAnchorClampCells, WaypointPassable(u));

				var had = lastFiresAnchor.TryGetValue(u, out var prevCell);
				var anchorMoved = !had || (prevCell - anchorCell).LengthSquared >= repathSq;

				// Never re-issue the identical reachable destination (would restart the AttackMove and cancel a
				// shot); otherwise hold when in-band with an un-drifted anchor so AutoTarget keeps firing.
				if ((had && prevCell == anchorCell) || (!needs && !anchorMoved))
					continue;

				bot.QueueOrder(new Order("AttackMove", u, Target.FromCell(world, anchorCell), false));
				lastFiresAnchor[u] = anchorCell;

				Log.Write("debug",
					$"[exp-offense] fires player={player.PlayerName} unit={u.Info.Name}#{u.ActorID} anchor={anchorCell} maxRange={maxRange} needsReposition={needs} target={axis.TargetName}@{axis.TargetCell} tick={tick}");
			}
		}

		// Frontier standoff: walk an echelon anchor rearward (directly AWAY from the target, along the existing
		// anchor axis) in one-coarse-cell hops until the believed frontier distance at the anchor reaches
		// MinFrontierDistanceCells, bounded by a small step budget so it is never a free search. The frontier read
		// is the fog-legal ControlField distance-to-enemy-region; the step count decision is the pure, NUnit-pinned
		// FrontierStandoffMath.RearwardSteps. Deterministic integer geometry, zero random draws.
		WPos PushEchelonBehindFrontier(WPos anchor, WPos target)
		{
			// One coarse cell along the dominant axis of the away-from-target bearing (max-norm, so a diagonal
			// hop still crosses a full coarse cell — no undershoot).
			var step = FrontierStandoffMath.RearwardStep(anchor - target, WDist.FromCells(controlField.Info.CellSize).Length);
			if (step == WVec.Zero)
				return anchor; // degenerate (anchor on the target) — no rearward bearing to walk.

			var maxSteps = Info.MinFrontierDistanceCells + 2; // enough to lift a distance-0 anchor clear, bounded.
			var steps = FrontierStandoffMath.RearwardSteps(anchor, step, Info.MinFrontierDistanceCells, maxSteps,
				w =>
				{
					var (gx, gy) = controlField.MapCellToGridCell(world.Map.CellContaining(w));
					return controlField.FrontierDistanceAt(player, gx, gy);
				},
				w => world.Map.Contains(world.Map.CellContaining(w)));

			return anchor + new WVec(step.X * steps, step.Y * steps, 0);
		}

		// Fires EV gate: restore FireAtWill on every held rocket piece that was NOT re-affirmed as held this eval
		// (its axis retired, it was reclassified, it dropped out of range, or no scoreable POI exists so no fires
		// set was built at all) so a held piece can never strand defenceless in HoldFire. Called on BOTH the
		// normal post-order path and the targets.Count==0 early return. NOTE-2: a piece whose owner changed is
		// dropped from tracking but gets no restore order — we cannot issue orders for a unit we no longer own;
		// its new owner's logic governs its stance. Deterministic: content of the restored set is order-independent.
		void ReconcileFiresHoldFire(IBot bot)
		{
			if (firesHeldFire.Count == 0)
				return;

			var strays = firesHeldFire.Where(a => !firesHeldThisEval.Contains(a)).ToList();
			foreach (var a in strays)
			{
				firesHeldFire.Remove(a);
				if (!a.IsDead && a.IsInWorld && a.Owner == player)
					bot.QueueOrder(new Order("SetUnitStance", a, false) { ExtraData = (uint)UnitStance.FireAtWill });
			}
		}

		// Fires EV gate: force a rocket piece to HoldFire (unworthy) or restore FireAtWill (worthy), issuing a
		// SetUnitStance order only on a transition so an already-correct piece never chatters. Tracks the held
		// set + the per-eval "still held" marker for the post-order stranding reconciliation. Deterministic.
		void ApplyFiresStance(IBot bot, Actor u, bool worthy)
		{
			if (worthy)
			{
				if (firesHeldFire.Remove(u))
					bot.QueueOrder(new Order("SetUnitStance", u, false) { ExtraData = (uint)UnitStance.FireAtWill });

				return;
			}

			firesHeldThisEval.Add(u);
			if (firesHeldFire.Add(u))
				bot.QueueOrder(new Order("SetUnitStance", u, false) { ExtraData = (uint)UnitStance.HoldFire });
		}

		// Fires EV gate core (item 19): is a rocket salvo from this piece worth its ammo cost right now? Prices
		// one volley from the economy model (max weapon Burst rounds at the priciest ammo pool's per-batch
		// SupplyValue) and compares it to the best projected clump value among spotted enemies in weapon range
		// (each enemy tried as the aim point; splash-weighted enemy build value it would destroy). A piece with
		// no priced ammo or no live weapon is always worthy (no gate); no spotted target ⇒ not worthy (hold).
		// Deterministic: no random draws, order-independent sums, one bounded circle query. Reused pure helpers
		// keep the arithmetic NUnit-pinned (FiresEconMathTest).
		bool RocketFireWorthy(Actor u)
		{
			var burst = 0;
			foreach (var arm in u.TraitsImplementing<Armament>())
			{
				if (arm.IsTraitDisabled || arm.Weapon == null)
					continue;

				if (arm.Weapon.Burst > burst)
					burst = arm.Weapon.Burst;
			}

			var reloadCount = 1;
			var supplyValue = 0;
			foreach (var pool in u.TraitsImplementing<AmmoPool>())
			{
				if (pool.Info.SupplyValue > supplyValue)
				{
					supplyValue = pool.Info.SupplyValue;
					reloadCount = pool.Info.ReloadCount;
				}
			}

			var salvoCost = FiresEconMath.SalvoCost(burst, reloadCount, supplyValue);
			if (salvoCost <= 0)
				return true;

			var maxRange = MaxWeaponRange(u);
			if (maxRange <= 0)
				return true;

			var enemies = new List<FiresEconMath.ClumpTarget>();
			var positions = new List<WPos>();
			foreach (var a in world.FindActorsInCircle(u.CenterPosition, new WDist(maxRange)))
			{
				if (a == u || a.IsDead || !a.IsInWorld)
					continue;

				if (player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy || !a.CanBeViewedByPlayer(player))
					continue;

				var cost = a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
				if (cost <= 0)
					continue;

				var dmg = AutoTarget.EstimatePercentDamage(u, Target.FromActor(a));
				if (dmg <= 0)
					continue;

				positions.Add(a.CenterPosition);
				enemies.Add(new FiresEconMath.ClumpTarget(cost, dmg, 0));
			}

			if (enemies.Count == 0)
				return false;

			// Best aim = the enemy whose surrounding clump repays the most. Rebuild the clump with each enemy's
			// distance measured from that aim point (the ClumpTarget value/damage are aim-independent).
			var radius = Info.FiresEvClumpRadius.Length;
			long best = 0;
			for (var i = 0; i < positions.Count; i++)
			{
				var aim = positions[i];
				var clump = new List<FiresEconMath.ClumpTarget>(enemies.Count);
				for (var j = 0; j < enemies.Count; j++)
					clump.Add(new FiresEconMath.ClumpTarget(
						enemies[j].Value, enemies[j].DamagePercent, (positions[j] - aim).HorizontalLength));

				var val = FiresEconMath.ProjectedClumpValue(clump, radius, FiresEvFalloff);
				if (val > best)
					best = val;
			}

			return FiresEconMath.FireWorthy(best, salvoCost, Info.FiresEvMarginPercent);
		}

		// ===== Continuous bombardment (Phase 1, gap G1) =====

		// Give idle artillery a STANDING fire mission on believed-static enemy positions, independent of any
		// offensive axis. The target set is the fog-legal belief store (IsStatic contacts only — no world.Actors /
		// omniscient scan); worthiness reuses the FiresEconMath EV gate; positioning reuses FiresStandoffMath; the
		// piece is committed to the shared PoiGoalGuard ledger under "bombard:<targetId>" so no axis double-tasks
		// it. The assignment + re-target hysteresis + per-target cap decision is the pure ContinuousBombardMath.
		// OFF (ContinuousBombardment=false) ⇒ nothing tracked, immediate return, zero orders/commits ⇒ byte-identical.
		void BombardStaticPositions(IBot bot, int tick)
		{
			// First carry forward pieces already on a mission (they are ledger-committed, so BuildFreePool below
			// can't see them), releasing any that died / changed owner / ran dry / lost their weapon. When the
			// feature has never run, bombardAssigned is empty and this loop is a no-op.
			List<Actor> continuing = null;
			if (bombardAssigned.Count > 0)
			{
				List<Actor> drop = null;
				foreach (var u in bombardAssigned.Keys)
				{
					if (u.IsDead || !u.IsInWorld || u.Owner != player || IsOutOfAmmo(u) || MaxWeaponRange(u) <= 0)
						(drop ??= new List<Actor>()).Add(u);
					else
						(continuing ??= new List<Actor>()).Add(u);
				}

				if (drop != null)
					foreach (var u in drop)
						ReleaseBombard(u);
			}

			// Feature off, or a dependency (role resolver / belief store) is absent: release every remaining
			// mission so no piece strands committed, and take no new action. This is the whole cost when off.
			if (!Info.ContinuousBombardment || resolver == null || beliefStore == null)
			{
				if (continuing != null)
					foreach (var u in continuing)
						ReleaseBombard(u);
				return;
			}

			// 1. Fog-legal target set: believed STATIC enemy positions with a build value. Belief store ONLY — the
			//    contact's cell is its fog-correct last-seen cell, so no ground-truth/omniscient position is read.
			var statics = new List<(uint Id, CPos Cell, int Value)>();
			foreach (var c in beliefStore.Contacts(player))
			{
				if (!c.IsStatic)
					continue;

				var value = ContactFact(c.TypeName).Cost;
				if (value <= 0)
					continue;

				statics.Add((c.Key, c.Cell, value));
			}

			if (statics.Count == 0)
			{
				if (continuing != null)
					foreach (var u in continuing)
						ReleaseBombard(u);
				return;
			}

			// 2. Price each position's rocket CLUMP numerator: the splash-weighted value of the believed-static
			//    clump around it (FiresEconMath — the same beaten-zone kernel the reactive EV gate uses). Static
			//    structures carry no fog-legal HP, so the burst is assumed to threaten full value (damage 100) — a
			//    "worth firing at this position" heuristic, not a per-structure ballistic model. O(statics^2), and
			//    believed statics are few (structures). The tube numerator is the position's own value (Value).
			var clumpRadius = Info.FiresEvClumpRadius.Length;
			var mtargets = new List<ContinuousBombardMath.StaticTarget>(statics.Count);
			var targetCells = new Dictionary<uint, CPos>(statics.Count);
			for (var i = 0; i < statics.Count; i++)
			{
				var t = statics[i];
				var aim = world.Map.CenterOfCell(t.Cell);
				var clump = new List<FiresEconMath.ClumpTarget>(statics.Count);
				for (var j = 0; j < statics.Count; j++)
					clump.Add(new FiresEconMath.ClumpTarget(
						statics[j].Value, 100, (world.Map.CenterOfCell(statics[j].Cell) - aim).HorizontalLength));

				var clumpValue = FiresEconMath.ProjectedClumpValue(clump, clumpRadius, FiresEvFalloff);
				var clumpValueInt = clumpValue > int.MaxValue ? int.MaxValue : (int)clumpValue;
				mtargets.Add(new ContinuousBombardMath.StaticTarget(t.Id, t.Cell.X, t.Cell.Y, t.Value, clumpValueInt));
				targetCells[t.Id] = t.Cell;
			}

			// 3. Piece set: carried-in missions (their current target id) plus newly-idle artillery from the free
			//    pool (uncommitted, un-axis'd — role IndirectFire, with a live weapon and ammo). BuildFreePool
			//    filters IPositionable+AttackBase before any .Location read, so positionless PlayerActors can't
			//    NRE here (conventions.md world.Actors pattern). Fixed ActorID order ⇒ deterministic assignment.
			var pieceList = new List<ContinuousBombardMath.FiresPiece>();
			var pieceActors = new Dictionary<uint, Actor>();
			if (continuing != null)
				foreach (var u in continuing)
					AddBombardPiece(pieceList, pieceActors, u, bombardAssigned[u]);

			foreach (var a in BuildFreePool())
			{
				if (resolver.GetRole(a) != UnitRole.IndirectFire || MaxWeaponRange(a) <= 0 || IsOutOfAmmo(a))
					continue;

				AddBombardPiece(pieceList, pieceActors, a, 0);
			}

			if (pieceList.Count == 0)
				return;

			pieceList.Sort((x, y) => x.Id.CompareTo(y.Id));

			// 4. Pure decision: which piece shells which believed-static position (nearest worthwhile in reach,
			//    re-target hysteresis, per-target cap). Zero RNG, deterministic tie-breaks.
			var assignments = ContinuousBombardMath.SelectAssignments(
				pieceList, mtargets, Info.FiresEvMarginPercent, Info.BombardMaxPiecesPerTarget,
				Info.BombardRetargetHysteresisCells);

			// 5. Issue standoff orders + (re)commit. Reuses the exact FiresStandoffMath geometry + nearest-passable
			//    clamp + re-issue dedup as the reactive OrderFiresStandoff no-screen path, against the BELIEVED
			//    target cell centre (never a live-actor read, so a fogged position is still a legal aim point).
			var margin = Info.FiresStandoffMargin.Length;
			var hysteresis = Info.FiresStandoffHysteresis.Length;
			var floor = Info.FiresStandoffFloor.Length;
			var repathSq = Info.RepathThresholdCells * Info.RepathThresholdCells;
			var stillTasked = new HashSet<Actor>();

			foreach (var asn in assignments)
			{
				if (!asn.HasTarget)
					continue;

				var u = pieceActors[asn.PieceId];
				var targetCell = targetCells[asn.TargetId];
				var targetPos = world.Map.CenterOfCell(targetCell);
				var maxRange = MaxWeaponRange(u);
				if (maxRange <= 0)
					continue;

				var pos = u.CenterPosition;
				var anchor = FiresStandoffMath.StandoffAnchor(targetPos, pos, maxRange, margin, floor);
				var needs = FiresStandoffMath.NeedsReposition(targetPos, pos, maxRange, margin, hysteresis, floor);

				var idealCell = world.Map.CellContaining(anchor);
				var anchorCell = FiresStandoffMath.NearestPassableCell(idealCell, FiresAnchorClampCells, WaypointPassable(u));

				// Commit to the shared ledger BEFORE any early-out below, so a held-in-band piece is still claimed
				// (no axis / stager may poach it while it keeps firing). TTL refreshed each eval it holds a mission.
				goalGuard?.Ledger.Commit(u, BombardObjectiveKey(asn.TargetId), tick, Info.BombardCommitmentTicks);
				bombardAssigned[u] = asn.TargetId;
				stillTasked.Add(u);

				var had = lastBombardAnchor.TryGetValue(u, out var prevCell);
				var anchorMoved = !had || (prevCell - anchorCell).LengthSquared >= repathSq;

				// Never re-issue the identical reachable destination (restarting the AttackMove cancels a shot);
				// hold when in-band with an un-drifted anchor so AutoTarget keeps firing on the position.
				if ((had && prevCell == anchorCell) || (!needs && !anchorMoved))
					continue;

				bot.QueueOrder(new Order("AttackMove", u, Target.FromCell(world, anchorCell), false));
				lastBombardAnchor[u] = anchorCell;

				Log.Write("debug",
					$"[exp-offense] bombard player={player.PlayerName} unit={u.Info.Name}#{u.ActorID} anchor={anchorCell} maxRange={maxRange} target={asn.TargetId}@{targetCell} tick={tick}");
			}

			// 6. Reconcile: any carried-in mission NOT re-tasked this eval (nothing worthy in reach, or the cap/
			//    hysteresis left it with no candidate) is released so the piece can evac / rejoin the offense pool.
			if (bombardAssigned.Count > 0)
			{
				var strays = bombardAssigned.Keys.Where(u => !stillTasked.Contains(u)).ToList();
				foreach (var u in strays)
					ReleaseBombard(u);
			}
		}

		// Build a pure ContinuousBombardMath.FiresPiece from a live artillery actor + its current mission target
		// (0 = none). Range is the Chebyshev-cell in-reach gate (WDist / cell length); kind selects the EV
		// numerator; salvo cost prices its volley from the shared economy model. Deterministic — synced reads only.
		void AddBombardPiece(List<ContinuousBombardMath.FiresPiece> pieceList,
			Dictionary<uint, Actor> pieceActors, Actor u, uint currentTargetId)
		{
			var maxRangeCells = MaxWeaponRange(u) / WDist.FromCells(1).Length;
			var isRocket = resolver.GetIndirectKind(u) == IndirectFireKind.Rocket;
			pieceList.Add(new ContinuousBombardMath.FiresPiece(
				u.ActorID, u.Location.X, u.Location.Y, maxRangeCells, isRocket, SalvoCostOf(u), currentTargetId));
			pieceActors[u.ActorID] = u;
		}

		// Price one salvo from this piece: max weapon Burst rounds at the priciest ammo pool's per-batch
		// SupplyValue (FiresEconMath). 0 when the weapon has no priced ammo (⇒ FireWorthy treats it as free).
		// Same pricing RocketFireWorthy uses; kept a separate helper so the frozen reactive path is untouched.
		int SalvoCostOf(Actor u)
		{
			var burst = 0;
			foreach (var arm in u.TraitsImplementing<Armament>())
			{
				if (arm.IsTraitDisabled || arm.Weapon == null)
					continue;

				if (arm.Weapon.Burst > burst)
					burst = arm.Weapon.Burst;
			}

			var reloadCount = 1;
			var supplyValue = 0;
			foreach (var pool in u.TraitsImplementing<AmmoPool>())
			{
				if (pool.Info.SupplyValue > supplyValue)
				{
					supplyValue = pool.Info.SupplyValue;
					reloadCount = pool.Info.ReloadCount;
				}
			}

			return FiresEconMath.SalvoCost(burst, reloadCount, supplyValue);
		}

		// Release a piece from its standing bombardment: drop the shared-ledger claim (only when it is actually a
		// bombard claim — never stomp another module's) and clear both tracking maps. Idempotent.
		void ReleaseBombard(Actor u)
		{
			if (goalGuard != null
				&& goalGuard.Ledger.TryGetObjective(u, out var obj)
				&& obj != null
				&& obj.StartsWith("bombard:", StringComparison.Ordinal))
				goalGuard.Ledger.Release(u);

			bombardAssigned.Remove(u);
			lastBombardAnchor.Remove(u);
		}

		static string BombardObjectiveKey(uint targetId) => "bombard:" + targetId;

		// Largest enabled-armament max range (WDist length, with live range modifiers applied). 0 when the
		// piece has no live weapon. Deterministic — reads only synced trait state.
		static int MaxWeaponRange(Actor a)
		{
			var max = 0;
			foreach (var arm in a.TraitsImplementing<Armament>())
			{
				if (arm.IsTraitDisabled)
					continue;

				var r = arm.MaxRange().Length;
				if (r > max)
					max = r;
			}

			return max;
		}

		// Release an axis's units back to the free pool and return them.
		List<Actor> ReleaseAxis(Axis axis, string reason)
		{
			var freed = new List<Actor>(axis.Units);
			foreach (var u in axis.Units)
				goalGuard?.Ledger.Release(u);
			axis.Units.Clear();

			if (freed.Count > 0)
				Log.Write("debug",
					$"[exp-offense] retire player={player.PlayerName} target={axis.TargetName} freed={freed.Count} reason={reason} tick={world.WorldTick}");
			return freed;
		}

		void RetireAllAxes(string reason)
		{
			foreach (var axis in axes)
				ReleaseAxis(axis, reason);
			axes.Clear();
		}

		// A ground-danger sampler bound to this player's own anti-ground channel. Off-map cells read
		// Impassable so a detour waypoint never lands off the playable area. Fog-legal: the field is
		// stamped from the player's belief store; reads 0 in verified-safe ground.
		Func<CPos, int> GroundDangerSampler(DangerFieldLayer field)
		{
			var map = world.Map;
			return c => map.Contains(c) ? field.GroundDanger(player, c) : GroundDangerNav.Impassable;
		}

		// A terrain-passability predicate bound to a representative axis unit's locomotor: true when the
		// mover can actually stand on the cell (not on-map water/cliff, not off-map). Used to reject
		// detour WAYPOINTS that read "safe" only because unstamped impassable ground carries no danger.
		// Falls back to "all passable" if the representative has no Mobile (never rejects) — rare for a
		// combat axis (every member has IPositionable + AttackBase).
		Func<CPos, bool> WaypointPassable(Actor mover)
		{
			var loco = mover.TraitOrDefault<Mobile>()?.Locomotor;
			if (loco == null)
				return _ => true;

			return c => loco.MovementCostForCell(c) != PathGraph.MovementCostForUnreachableCell;
		}

		// True when the Stage-E flow-around waypoint changed enough to warrant re-issuing the axis order:
		// appeared, vanished, or shifted by >= threshold cells. Keeps the flow-around responsive as the
		// axis advances without spamming orders on jitter. Pure.
		static bool ViaChanged(CPos? previous, CPos? current, int thresholdCells)
		{
			if (previous.HasValue != current.HasValue)
				return true;
			if (!current.HasValue)
				return false;
			return (previous.Value - current.Value).LengthSquared >= thresholdCells * thresholdCells;
		}

		static string OffenseObjectiveKey(uint targetId) => "offense:" + targetId;

		// ===== Combat-quality force-preservation (levers 1+2) =====

		// The rally / safety anchor: our own Supply Route beachhead (the strongest friendly control). Null when
		// we have none — then a losing axis has nowhere to fall back, so lever 1 is inert (it still declines to
		// reinforce under lever 2). One FindOwnSupplyRoute scan per eval, only when a lever is on.
		CPos? RallyCell()
		{
			var sr = poiMap?.OwnSupplyRoute(player);
			return sr != null && !sr.IsDead && sr.IsInWorld ? sr.Location : (CPos?)null;
		}

		// Update every axis's losing/retreat FSM state from the believed local force ratio. Own force is the
		// axis's health-weighted build value; believed enemy force is the fog-legal BeliefStore tally near the
		// centroid — the SAME cost scale. Safety is proximity to the rally cell. Pure integer inputs into the
		// NUnit-pinned CombatRetreatMath.Step; zero RNG. A not-yet-staffed axis is held Engaged (it must be built,
		// not retreated). Only called when a lever is on and a BeliefStore exists.
		void UpdateRetreatStates(int tick)
		{
			foreach (var axis in axes)
			{
				if (axis.Units.Count == 0)
				{
					axis.Retreat = RetreatDecision.Engaged;
					axis.LosingStreak = 0;
					axis.ReadvanceHold = 0;
					axis.NearRally = false;
					continue;
				}

				var centroid = AxisCentroidCell(axis);
				var own = OwnAxisStrength(axis);
				var enemy = BelievedEnemyStrength(centroid);
				var safe = rallyCell.HasValue
					&& PoiOffenseMath.Chebyshev(centroid.X, centroid.Y, rallyCell.Value.X, rallyCell.Value.Y)
						<= Info.RetreatSafeDistanceCells;

				var prev = axis.Retreat;
				var (decision, streak) = CombatRetreatMath.Step(axis.Retreat, axis.LosingStreak,
					own, enemy, Info.RetreatForceRatioPct, Info.ReengageForceRatioPct, safe, Info.RetreatSustainEvals);
				axis.Retreat = decision;
				axis.LosingStreak = streak;

				// Phase 3 retreat-oscillation damper: track the post-retreat dwell + the "still massing in the
				// rear" flag. Both only read under RetreatDamperEnabled, so they never affect the base retreat
				// lever. StepReadvanceHold arms the dwell on a Retreating->Engaged transition (retreat completed)
				// and counts it down while Engaged; it is 0 whenever the axis is Retreating, so the damper can
				// NEVER delay a genuine withdrawal.
				if (Info.RetreatDamperEnabled)
				{
					axis.ReadvanceHold = RetreatDamperMath.StepReadvanceHold(
						axis.ReadvanceHold, prev, decision, Info.RetreatReadvanceDwellEvals);
					axis.NearRally = safe;
				}

				if (decision == RetreatDecision.Retreating)
					Log.Write("debug",
						$"[exp-retreat] state player={player.PlayerName} target={axis.TargetName}@{axis.TargetCell} " +
						$"own={own} enemy={enemy} streak={streak} safe={safe} tick={tick}");
			}
		}

		// Phase 3: should the retreat-oscillation damper HOLD this axis (at the muster point) instead of letting it
		// re-advance? Delegates to the pure RetreatDamperMath.ShouldHold, which carries a DEFENSIVE guard — a
		// Retreating axis is never held — so the "damper never delays a genuine withdrawal" property is structural
		// and no longer depends on the caller's retreat gate running first (NIT-3). (a) post-retreat dwell or
		// (b) a sub-strength axis still massing near the rally holds; an axis already forward is never yanked back
		// merely for being small. Zero RNG.
		bool DamperShouldHold(Axis axis)
			=> RetreatDamperMath.ShouldHold(axis.Retreat, axis.ReadvanceHold, axis.NearRally,
				OwnAxisStrength(axis), Info.MinAdvanceStrength);

		// Phase 5: should this axis HOLD because the sector it STANDS IN reads too strong? Reads the believed
		// per-sector profile (own vs enemy strength + front presence) and delegates the ratio test to the pure
		// FrontlineAllocationMath.SectorPostureHold. Evaluated at the axis's own CONTACT sector (its unit centroid),
		// NOT the deep TARGET sector: an offensive axis's target lies in the enemy rear where our believed own
		// strength is ~0, which made the ratio trivially "outnumbered" and froze every push at home. The centroid
		// sector counts the axis's own units, so sectorOwn reflects the committed force; the own-strength floor is
		// the backstop for a sector we don't actually occupy. Inert (false) until the profile is built for this
		// player; the caller only reaches here when SectorPostureHoldEnabled with a valid rally, and NEVER on a
		// Retreating axis (the retreat gate returned upstream), so a genuine withdrawal is never converted to a hold.
		bool PostureShouldHold(Axis axis)
		{
			if (controlField == null || !controlField.HasFrontlineProfile(player))
				return false;

			// No units ⇒ nothing to hold with (and no centroid); an empty axis is not a hold candidate.
			if (axis.Units.Count == 0)
				return false;

			var sector = SectorOfCell(AxisCentroidCell(axis));
			if (sector == FrontlineProfileMath.NoSector)
				return false;

			var prof = controlField.SectorProfile(player, sector);
			return FrontlineAllocationMath.SectorPostureHold(prof.OwnStrength, prof.EnemyStrength,
				prof.FrontierEdges, Info.SectorPostureHoldRatioPct, Info.SectorPostureHoldOwnFloor);
		}

		CPos AxisCentroidCell(Axis axis)
		{
			var cells = new List<(int X, int Y)>(axis.Units.Count);
			foreach (var u in axis.Units)
				cells.Add((u.Location.X, u.Location.Y));

			var c = PoiOffenseMath.CellCentroid(cells);
			return new CPos(c.X, c.Y);
		}

		// Own force = sum of health-weighted build value over the axis's units. Order-independent sum ⇒ no sort.
		int OwnAxisStrength(Axis axis)
		{
			var sum = 0;
			foreach (var u in axis.Units)
				sum += UnitStrength(u);
			return sum;
		}

		// Health-weighted build value: full cost scaled by the current HP fraction, so a wounded unit contributes
		// less to "our remaining force". Reads only synced trait state; zero RNG.
		static int UnitStrength(Actor u)
		{
			var cost = u.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
			if (cost <= 0)
				return 0;

			var health = u.TraitOrDefault<Health>();
			if (health == null || health.MaxHP <= 0)
				return cost;

			return (int)((long)cost * health.HP / health.MaxHP);
		}

		// Believed enemy force near a cell: sum over ARMED believed contacts within ForceRatioRadiusCells
		// (Chebyshev) of build cost x confidence. Fog-legal (belief store only). Addition is order-independent,
		// so iterating the belief dictionary needs no sort for determinism; zero RNG.
		int BelievedEnemyStrength(CPos centre)
		{
			if (beliefStore == null)
				return 0;

			var sum = 0;
			var r = Info.ForceRatioRadiusCells;
			foreach (var c in beliefStore.Contacts(player))
			{
				if (PoiOffenseMath.Chebyshev(c.Cell.X, c.Cell.Y, centre.X, centre.Y) > r)
					continue;

				var fact = ContactFact(c.TypeName);
				if (!fact.Armed || fact.Cost <= 0)
					continue;

				var confidence = c.Confidence < 0 ? 0 : (c.Confidence > 100 ? 100 : c.Confidence);
				sum += fact.Cost * confidence / 100;
			}

			return sum;
		}

		// (build cost, armed) for a believed-contact type name, cached from the actor rules. An armed contact
		// carries AttackBase; an unarmed one (supply truck, capturer) contributes no combat force to the ratio.
		(int Cost, bool Armed) ContactFact(string typeName)
		{
			if (typeName == null)
				return (0, false);

			if (contactFactCache.TryGetValue(typeName, out var fact))
				return fact;

			var cost = 0;
			var armed = false;
			if (world.Map.Rules.Actors.TryGetValue(typeName, out var info))
			{
				cost = info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
				armed = info.HasTraitInfo<AttackBaseInfo>();
			}

			fact = (cost, armed);
			contactFactCache[typeName] = fact;
			return fact;
		}

		// Issue the fall-back: a grouped AttackMove toward the rally cell for every unit on the axis. Re-issued
		// only when the axis just entered the retreat (or its unit set changed ⇒ HasOrdered cleared upstream), or
		// the rally cell drifted past the repath threshold — so a squad already withdrawing keeps its order
		// uninterrupted. Deterministic.
		void OrderRetreat(IBot bot, Axis axis, CPos rally, int tick)
		{
			var moved = !axis.HasOrdered
				|| !axis.OrderedRetreat
				|| (axis.OrderedCell - rally).LengthSquared >= Info.RepathThresholdCells * Info.RepathThresholdCells;
			if (!moved)
				return;

			var units = axis.Units.ToArray();
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, rally), false, groupedActors: units));
			axis.OrderedCell = rally;
			axis.OrderedVia = null;
			axis.OrderedRetreat = true;
			axis.HasOrdered = true;

			Log.Write("debug",
				$"[exp-retreat] fallback player={player.PlayerName} target={axis.TargetName} rally={rally} units={units.Length} tick={tick}");
			AIUtils.BotDebug("AI ({0}): exp-offense — axis {1} RETREATING to {2} ({3} units)",
				player.ClientIndex, axis.TargetName, rally, units.Length);
		}
	}

	// ============================================================
	// Pure offense math — engine-free, unit-tested (PoiOffenseTest). Ports to v3.
	// ============================================================
	public static class PoiOffenseMath
	{
		/// <summary>How many attack axes to open: ~one per UnitsPerAxis units, clamped to
		/// [1, maxAxes], never more than there are POIs, and never more than we can fund
		/// at minAxisSize. Returns 0 when there are no targets or too few units for one axis.</summary>
		public static int DesiredAxisCount(int totalUnits, int poiCount, int unitsPerAxis, int minAxisSize, int maxAxes)
		{
			if (poiCount <= 0 || totalUnits < Math.Max(1, minAxisSize))
				return 0;

			var byPool = Math.Max(1, totalUnits / Math.Max(1, unitsPerAxis));
			var k = Math.Min(byPool, Math.Max(1, maxAxes));
			k = Math.Min(k, poiCount);
			k = Math.Min(k, totalUnits / Math.Max(1, minAxisSize)); // fundability at min size
			return Math.Max(1, k);
		}

		/// <summary>Split totalUnits across axes whose scores are given (any order), each axis
		/// getting at least minAxisSize, the remainder distributed by score with a deterministic
		/// largest-remainder rule (ties by index). If the axes can't all be funded at min size,
		/// the lowest-index tail is dropped to zero. Sum of the result == totalUnits (when fundable).</summary>
		public static int[] AllocateProportional(IReadOnlyList<long> scores, int totalUnits, int minAxisSize)
		{
			var full = scores.Count;
			var result = new int[full];
			if (full == 0 || totalUnits <= 0)
				return result;

			var min = Math.Max(1, minAxisSize);

			// Fund as many leading axes as min size allows (scores arrive score-desc so
			// the tail we drop is the weakest).
			var n = full;
			while (n > 0 && n * min > totalUnits)
				n--;
			if (n == 0)
				return result;

			for (var i = 0; i < n; i++)
				result[i] = min;

			var leftover = totalUnits - n * min;
			if (leftover <= 0)
				return result;

			long sum = 0;
			for (var i = 0; i < n; i++)
				sum += Math.Max(1, scores[i]);
			if (sum <= 0)
				sum = n;

			var rem = new (long rem, int idx)[n];
			var assigned = 0;
			for (var i = 0; i < n; i++)
			{
				var exact = leftover * Math.Max(1, scores[i]);
				var floor = (int)(exact / sum);
				result[i] += floor;
				assigned += floor;
				rem[i] = (exact - (long)floor * sum, i);
			}

			var remaining = leftover - assigned;
			Array.Sort(rem, (a, b) =>
			{
				var c = b.rem.CompareTo(a.rem);
				return c != 0 ? c : a.idx.CompareTo(b.idx);
			});
			for (var r = 0; r < remaining && r < n; r++)
				result[rem[r].idx]++;

			return result;
		}

		/// <summary>Hysteresis test: does `candidate` beat `current` by strictly more than
		/// thresholdPct? Used to decide whether a fresh POI should displace a still-valid
		/// axis. Pure so the sticky-axis rule is unit-testable and v3-portable.</summary>
		public static bool ScoreBeatsByThreshold(long candidate, long current, int thresholdPct)
			=> candidate * 100 > current * (100L + Math.Max(0, thresholdPct));

		/// <summary>Chebyshev (chessboard) distance between two cells, in cells. The dispersion
		/// gate and clump telemetry both use Chebyshev — NOTE CVec.Length is Euclidean, so we
		/// compute this directly rather than reusing it.</summary>
		public static int Chebyshev(int ax, int ay, int bx, int by)
			=> Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

		// Eight fixed sampling directions (cardinals + diagonals) for the neighborhood control read.
		// Static readonly ⇒ zero allocation in the hot path; fixed order ⇒ deterministic aggregate.
		static readonly (int Dx, int Dy)[] NeighborhoodDirections =
		{
			(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
		};

		/// <summary>STAGE F: average BELIEVED control score of the ring of cells at grid distance `radius`
		/// around a target's grid cell (gx,gy), sampled via `scoreAt` (a ControlField.ScoreAt bound to the
		/// perspective player). The target's OWN cell is deliberately NOT sampled: every enemy Attack/
		/// Pressure target is a static structure (CaptureManager/SupplyProvider) that ControlField stamps
		/// as an enemy ANCHOR, flooring its own cell AND a disc out to AnchorRadiusCells to ≈ −AnchorStrength
		/// regardless of who actually surrounds it — so the target-cell read is ALWAYS deeply enemy and can
		/// never see an encirclement. Sampling a ring JUST OUTSIDE that anchor footprint (caller passes
		/// radius &gt; AnchorRadiusCells) reads the real surrounding balance instead: an enemy structure
		/// ringed by ours-painted ground reads positive (→ boost), one deep in enemy paint reads negative
		/// (→ damp). Off-grid samples read 0 (scoreAt's own out-of-grid return), biasing edge targets toward
		/// neutral — a safe direction. Pure integer average over a fixed direction set ⇒ deterministic,
		/// zero-alloc, zero RNG.</summary>
		public static int NeighborhoodControlScore(Func<int, int, int> scoreAt, int gx, int gy, int radius)
		{
			long sum = 0;
			foreach (var (dx, dy) in NeighborhoodDirections)
				sum += scoreAt(gx + dx * radius, gy + dy * radius);

			return (int)(sum / NeighborhoodDirections.Length);
		}

		/// <summary>STAGE F territorial balance-of-power axis multiplier (x100), read from the BELIEVED
		/// control field (the terr-bias revival — 4adf867c's per-POI InfluenceMap share was a near-pure
		/// damper; the control field is the substrate it needed). neighborhoodScore is the SURROUNDING
		/// control read (NeighborhoodControlScore) around the target — NOT the target's own cell, which a
		/// site-anchor structure floors to ≈ −AnchorStrength and would make every enemy target damp. Buckets:
		/// &gt; +grayBand ⇒ believed OURS around the target (it is encircled / the enemy's grip here is
		/// weak/broken ⇒ PRESS, boostMul); &lt; −grayBand ⇒ believed ENEMY (committing means lunging into
		/// believed strength ⇒ damp, dampMul); |score| ≤ grayBand ⇒ contested front ⇒ 100 (neutral). grayBand
		/// mirrors ControlFieldInfo.GrayBand so the tri-state matches the field's own classification exactly
		/// (ControlFieldMath.Classify). Pure ⇒ unit-tested, zero RNG.</summary>
		public static int BalanceOfPowerFactor(int neighborhoodScore, int grayBand, int boostMul, int dampMul)
		{
			if (neighborhoodScore > grayBand)
				return boostMul;
			if (neighborhoodScore < -grayBand)
				return dampMul;
			return 100;
		}

		/// <summary>STAGE F believed anti-ground danger axis multiplier (x100) — the fog-legal REPLACEMENT
		/// for the old omniscient InfluenceMap threat. groundDanger = DangerFieldLayer.GroundDanger at the
		/// target cell (0 in verified-safe ground; a low Stage-C territory baseline in believed-enemy rear;
		/// a dense kernel inside a believed weapon envelope). Buckets mirror PoiScoring.ThreatFactor:
		/// ≤ mildThreshold ⇒ safe (safeMul), ≤ hostileThreshold ⇒ mild (mildMul), else hostile (hostileMul).
		/// Thresholds are on the DANGER-FIELD (throughput-derived) scale, NOT the InfluenceMap scale.
		/// Pure ⇒ unit-tested, zero RNG.</summary>
		public static int BelievedDangerFactor(int groundDanger, int mildThreshold, int hostileThreshold,
			int safeMul, int mildMul, int hostileMul)
		{
			if (groundDanger <= mildThreshold)
				return safeMul;
			if (groundDanger <= hostileThreshold)
				return mildMul;
			return hostileMul;
		}

		/// <summary>Phase 1d — tunable-slider shift (§2.7 base ± slope). A knob in 0..100 (50 = neutral) shifts a
		/// base threshold/weight by <c>(knob - 50) · slopePct / 100</c>, integer-only. slopePct is the range the
		/// slider spans (itself tunable), so a slopePct of 0 makes the knob INERT — the base is returned unchanged
		/// for ANY knob value, which is the frozen default (byte-identical). knob = 50 is likewise always a no-op.
		/// This is the single pure seam every future slider (Aggressiveness, RiskTolerance, …) threads through so
		/// the whole decision stays NUnit-pinnable and a sweep harness can vary a knob per match. Deterministic,
		/// zero RNG.</summary>
		public static int ShiftByKnob(int baseValue, int knob, int slopePct)
		{
			return baseValue + (knob - 50) * slopePct / 100;
		}

		/// <summary>Integer (floor-division) centroid of a set of cell coordinates. Empty input
		/// returns (0,0). Pure so the dispersion gate math is unit-testable and v3-portable.</summary>
		public static (int X, int Y) CellCentroid(IReadOnlyList<(int X, int Y)> cells)
		{
			if (cells == null || cells.Count == 0)
				return (0, 0);

			long sx = 0, sy = 0;
			for (var i = 0; i < cells.Count; i++)
			{
				sx += cells[i].X;
				sy += cells[i].Y;
			}

			return ((int)(sx / cells.Count), (int)(sy / cells.Count));
		}

		/// <summary>Max Chebyshev distance from (cx,cy) to any cell — the "clump radius". Empty
		/// input returns 0. Pure so the spacing telemetry is unit-testable and v3-portable.</summary>
		public static int MaxChebyshev(IReadOnlyList<(int X, int Y)> cells, int cx, int cy)
		{
			var max = 0;
			if (cells == null)
				return max;

			for (var i = 0; i < cells.Count; i++)
			{
				var d = Chebyshev(cells[i].X, cells[i].Y, cx, cy);
				if (d > max)
					max = d;
			}

			return max;
		}
	}
}
