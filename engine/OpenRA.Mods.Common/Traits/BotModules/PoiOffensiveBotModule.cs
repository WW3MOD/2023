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

			public readonly List<Actor> Units = new();
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

		readonly List<Axis> axes = new();

		// Combat-quality force-preservation: rally cell (our own Supply Route) resolved once per re-eval when a
		// force-preservation lever is on — a losing axis falls back to it and counts as safe near it. Null when
		// no lever is active or we have no SR to fall back to.
		CPos? rallyCell;

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

			// The anti-ground danger field feeds BOTH the Stage-E flow-around routing and the Stage-F
			// believed-danger axis damp, so resolve it when either is enabled.
			if (!dangerFieldResolved)
			{
				dangerField = Info.DangerFieldRouting || Info.StrategicRepointEnabled
					? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;
				dangerFieldResolved = true;
			}

			// The control field feeds the Stage-F strategic repoint AND the echelon frontier standoff, so
			// resolve it when either is enabled.
			if (!controlFieldResolved)
			{
				controlField = Info.StrategicRepointEnabled || Info.MinFrontierDistanceCells > 0
					? world.WorldActor.TraitOrDefault<ControlField>() : null;
				controlFieldResolved = true;
			}

			// Combat-quality force-preservation: the belief store feeds the believed-enemy force tally for both
			// levers, so resolve it when either is on. Inert (never resolved) otherwise ⇒ byte-identical.
			if (!beliefStoreResolved)
			{
				beliefStore = Info.RetreatWhenLosing || Info.NoReinforceLostFights
					? world.WorldActor.TraitOrDefault<BeliefStore>() : null;
				beliefStoreResolved = true;
			}

			// Rally cell for the fall-back / safety test — our own Supply Route. Resolved once per eval only when
			// a lever is on (one FindOwnSupplyRoute scan); null otherwise, so no cost on the frozen path.
			rallyCell = Info.RetreatWhenLosing || Info.NoReinforceLostFights ? RallyCell() : null;

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

			// Fires EV gate: reset the per-eval "still held this eval" marker. The post-order reconciliation
			// uses it to restore FireAtWill on any rocket piece that left the fires set while holding fire.
			firesHeldThisEval.Clear();

			// 2. Score offensive targets from OUR SR (value x distance x threat).
			//    Stage-F strategic repoint: when on (and a control field exists), ask PoiMap for a
			//    threat-NEUTRAL base score — no omniscient InfluenceMap read — and re-shape it below
			//    from the BELIEVED control + danger fields. Off ⇒ the frozen omniscient path, so the
			//    @stable twin (flag unset) and every control profile stay byte-identical.
			var repoint = Info.StrategicRepointEnabled && controlField != null;
			var targets = repoint
				? poiMap.GetOffensiveTargets(player, suppressOmniscientThreat: true)
				: poiMap.GetOffensiveTargets(player);

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

			// 3. Free pool = eligible combat units claimed by nobody (SquadManager no
			//    longer owns ground for experimental; capture/defense commitments are respected).
			var free = BuildFreePool();
			var totalOffensive = free.Count + axes.Sum(a => a.Units.Count);

			// 4. How many axes, and which targets (sticky top-k with a hysteresis slack).
			var k = PoiOffenseMath.DesiredAxisCount(totalOffensive, targets.Count,
				unitsPerAxis, minAxisSize, Info.MaxAxes);

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

				var recruits = free
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
			if (CombatRetreatMath.ShouldRetreat(Info.RetreatWhenLosing, axis.Retreat) && rallyCell.HasValue)
			{
				OrderRetreat(bot, axis, rallyCell.Value, tick);
				return;
			}

			// Just left the retreat state (recovered / reached safety): the last order was a fall-back, so force
			// the assault order below to re-issue rather than assume the stale attack order still holds.
			if (axis.OrderedRetreat)
			{
				axis.OrderedRetreat = false;
				axis.HasOrdered = false;
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
					continue;
				}

				var centroid = AxisCentroidCell(axis);
				var own = OwnAxisStrength(axis);
				var enemy = BelievedEnemyStrength(centroid);
				var safe = rallyCell.HasValue
					&& PoiOffenseMath.Chebyshev(centroid.X, centroid.Y, rallyCell.Value.X, rallyCell.Value.Y)
						<= Info.RetreatSafeDistanceCells;

				var (decision, streak) = CombatRetreatMath.Step(axis.Retreat, axis.LosingStreak,
					own, enemy, Info.RetreatForceRatioPct, Info.ReengageForceRatioPct, safe, Info.RetreatSustainEvals);
				axis.Retreat = decision;
				axis.LosingStreak = streak;

				if (decision == RetreatDecision.Retreating)
					Log.Write("debug",
						$"[exp-retreat] state player={player.PlayerName} target={axis.TargetName}@{axis.TargetCell} " +
						$"own={own} enemy={enemy} streak={streak} safe={safe} tick={tick}");
			}
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
