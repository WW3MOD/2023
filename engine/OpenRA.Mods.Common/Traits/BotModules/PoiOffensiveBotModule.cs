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

		readonly List<Axis> axes = new();

		// Last cohesion mode we issued to each unit (dispersion doctrine). Cohesion is a
		// property of the unit, not the axis, so a re-recruited unit keeps its mode across
		// axes — we only re-issue SetCohesion when a unit's desired mode actually changes.
		readonly Dictionary<Actor, CohesionMode> lastCohesion = new();

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

			if (!controlFieldResolved)
			{
				controlField = Info.StrategicRepointEnabled ? world.WorldActor.TraitOrDefault<ControlField>() : null;
				controlFieldResolved = true;
			}

			var tick = world.WorldTick;

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
				Log.Write("debug", $"[exp-offense] reeval player={player.PlayerName} targets=0 axes=0 tick={tick}");
				return;
			}

			// 3. Free pool = eligible combat units claimed by nobody (SquadManager no
			//    longer owns ground for experimental; capture/defense commitments are respected).
			var free = BuildFreePool();
			var totalOffensive = free.Count + axes.Sum(a => a.Units.Count);

			// 4. How many axes, and which targets (sticky top-k with a hysteresis slack).
			var k = PoiOffenseMath.DesiredAxisCount(totalOffensive, targets.Count,
				Info.UnitsPerAxis, Info.MinAxisSize, Info.MaxAxes);

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

			// 7. Proportional target sizes by score, min axis size enforced.
			var orderedAxes = axes.OrderByDescending(a => a.Score).ThenBy(a => a.TargetId).ToList();
			var sizes = PoiOffenseMath.AllocateProportional(
				orderedAxes.Select(a => a.Score).ToList(), totalOffensive, Info.MinAxisSize);

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
				if (axis.Units.Count < Info.MinAxisSize)
				{
					ReleaseAxis(axis, "under-min");
					axes.RemoveAt(i);
					continue;
				}

				CommitAndOrder(bot, axis, tick);
			}

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

			int boosted = 0, damped = 0, neutral = 0;
			var scaled = new List<ScoredPoi>(targets.Count);
			foreach (var p in targets)
			{
				var (gx, gy) = controlField.MapCellToGridCell(p.Location);
				var controlScore = controlField.ScoreAt(player, gx, gy);
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
					$"action={p.Action} control={controlScore} bop={bop} groundDanger={groundDanger} danger={dangerMul} " +
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
			// (Re)commit every unit to this axis so the shared ledger keeps them ours.
			if (goalGuard != null)
			{
				var key = OffenseObjectiveKey(axis.TargetId);
				foreach (var u in axis.Units)
					goalGuard.Ledger.Commit(u, key, tick, Info.AxisCommitmentTicks);
			}

			// Axis spacing geometry (pure Chebyshev, cheap for N<=8) — computed for every
			// axis so the clumpRadius telemetry gives a baseline for the frozen controls too.
			var cells = new List<(int X, int Y)>(axis.Units.Count);
			foreach (var u in axis.Units)
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
				var passable = WaypointPassable(axis.Units[0]);
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
			var dispersion = Info.CohesionSwitchEnabled && axis.Units.Count > 0;
			var wantMode = distToTarget > Info.AssaultRadiusCells ? Info.ApproachCohesion : Info.AssaultCohesion;

			// A unit needs a fresh SetCohesion only when its desired mode actually changed —
			// avoids re-issuing the stance every re-eval for units already in the right mode.
			var cohesionChanged = false;
			if (dispersion)
			{
				foreach (var u in axis.Units)
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
				foreach (var u in axis.Units)
				{
					if (lastCohesion.TryGetValue(u, out var have) && have == wantMode)
						continue;

					bot.QueueOrder(new Order("SetCohesion", u, false) { ExtraData = (uint)wantMode });
					lastCohesion[u] = wantMode;
				}
			}

			var units = axis.Units.ToArray();

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

		/// <summary>STAGE F territorial balance-of-power axis multiplier (x100), read from the BELIEVED
		/// control field (the terr-bias revival — 4adf867c's per-POI InfluenceMap share was a near-pure
		/// damper; the control field is the substrate it needed). controlScore = ControlField.ScoreAt at
		/// the target cell: &gt; +grayBand ⇒ believed OURS (we locally dominate the ground the target sits
		/// on — the enemy's control here is weak/broken ⇒ PRESS, boostMul); &lt; −grayBand ⇒ believed ENEMY
		/// (committing means lunging into believed strength ⇒ damp, dampMul); |score| ≤ grayBand ⇒ contested
		/// front ⇒ 100 (neutral). grayBand mirrors ControlFieldInfo.GrayBand so the tri-state matches the
		/// field's own classification exactly (ControlFieldMath.Classify). Pure ⇒ unit-tested, zero RNG.</summary>
		public static int BalanceOfPowerFactor(int controlScore, int grayBand, int boostMul, int dampMul)
		{
			if (controlScore > grayBand)
				return boostMul;
			if (controlScore < -grayBand)
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
