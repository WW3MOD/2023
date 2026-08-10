#region Copyright & License Information
/*
 * WW3MOD PoiGarrisonBotModule — experimental AI, POI-strategy Phase 4 (hold captured money).
 *
 * Closes the capture loop: once the experimental AI OWNS a money POI (oil derrick, expansion
 * post, reactor) it must HOLD it, not leave it for a ninja-recapture. This module
 * reads PoiMap's DEFENSIVE ranking (own money POIs scored value x distance x
 * DEFENCE-urgency, where enemy pressure RAISES the score) and parks a SMALL
 * garrison on each — 1-3 units, scaled by the POI's value and bumped when it's
 * under assault. Deliberately small so it never starves the offense (Phase 3):
 * a handful of held income structures tie up at most a dozen units; the rest of
 * the ground pool stays with PoiOffensiveBotModule.
 *
 * SCORE-FLOATING, NO PRIORITY LADDER (design guidance): there is no hardcoded
 * "defense first" or "offense first". Both this module and the offense module pull
 * from the SAME free pool and stake their claims through the ONE shared
 * PoiGoalGuard ledger. Garrison commits "defend:<id>"; offense commits
 * "offense:<id>"; capture commits "capture:<id>". A unit committed to anyone is
 * invisible to the others, so whichever module claims a unit owns it until the
 * commitment expires — the competition is emergent through the ledger + the small
 * per-POI garrison cap, not a special-case ordering.
 *
 * PIPELINE (scoring -> sizing -> execution), mirroring PoiOffensiveBotModule:
 *   1. PoiMap.GetDefendTargets(player) — own money POIs, best (most urgent) first.
 *   2. PoiGarrisonMath.GarrisonSize per POI (value ramp + threat bump, clamped),
 *      then AllocateGarrisons funds them in priority order from the free pool.
 *   3. Reconcile live garrisons (sticky), recruit uncommitted units nearest each
 *      POI, commit them in the shared ledger, AttackMove them onto the POI cell.
 *
 * DESIGN INTENT (v3-portable): all sizing/allocation MATH lives in the pure
 * PoiGarrisonMath class (unit-tested in PoiGarrisonTest) so it ports verbatim into
 * a future v3 brain; only the assignment plumbing (this IBotTick module) is
 * engine-specific. Constants are Info fields so behaviour is YAML-tunable.
 *
 * Gated enable-ai-experimental in ai.yaml — Normal / Rush / Turtle never instantiate it.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("WW3MOD experimental AI: parks a small garrison (1-3 units, scaled by value + threat) on each",
		"money POI the AI OWNS so captured income is held, not ninja-recaptured. Reads PoiMap's",
		"defensive ranking; claims units through the shared PoiGoalGuard ledger so it never",
		"fights the offense/capture modules over a unit. Deliberately small to not starve",
		"offense. Gate enable-ai-experimental.")]
	public class PoiGarrisonBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between garrison re-evaluations. Slow cadence + the per-unit commitment TTL",
			"give hysteresis so garrisons don't re-path every scan.")]
		public readonly int ReevaluateInterval = 100;

		[Desc("Cells of POI value per garrison unit: a POI's base garrison is value / this, clamped",
			"to [MinGarrison, MaxGarrison]. With 50 (OILB=50, FCOM=100, BIO=150) that's 1 / 2 / 3.")]
		public readonly int ValuePerGarrisonUnit = 50;

		[Desc("Minimum garrison on any held money POI (a token hold even for the cheapest derrick).")]
		public readonly int MinGarrison = 1;

		[Desc("Hard cap on a single POI's garrison, threat bump included. Keeps garrisons small so",
			"the offense pool isn't starved.")]
		public readonly int MaxGarrison = 3;

		[Desc("Extra garrison units added when a held POI is under HOSTILE (assaulted) enemy",
			"influence, on top of the value ramp — still clamped to MaxGarrison.")]
		public readonly int ThreatGarrisonBonus = 1;

		[Desc("Hard cap on how many POIs are garrisoned concurrently (highest defend-score first).",
			"Bounds the total units this module ties up = MaxGarrisons x MaxGarrison.")]
		public readonly int MaxGarrisons = 4;

		[Desc("Commitment lifetime (ticks) for a unit assigned to a garrison. While committed the",
			"unit holds its POI and is invisible to capture/offense. Refreshed each re-eval so it",
			"must exceed ReevaluateInterval.")]
		public readonly int GarrisonCommitmentTicks = 250;

		[Desc("Re-issue a garrison AttackMove only if the target cell moved by at least this many",
			"cells (or the unit set changed). POIs are stationary, so this is mostly set-change.")]
		public readonly int RepathThresholdCells = 3;

		[Desc("Actor types NEVER pulled into a garrison (capturers, supply trucks, IFV carriers —",
			"owned by CaptureCoordinator / SupplyFollower / MountedTransport). Aircraft are",
			"excluded automatically by trait. Mirror PoiOffensiveBotModule's ExcludeUnitTypes.")]
		public readonly HashSet<string> ExcludeUnitTypes = new HashSet<string>();

		[Desc("EXPERIMENTAL: derive free-pool eligibility from UnitRoleResolver (role is MainBattle or",
			"IndirectFire) instead of the ExcludeUnitTypes name list. Same filter as",
			"PoiOffensiveBotModule — SHORAD/MANPADS/capturers/logistics/scouts drop out by class; cargo",
			"carriers stay owned by MountedTransportBotModule. Default false = frozen list behaviour, so",
			"the @stable twin stays byte-identical.")]
		public readonly bool UseUnitRoles = false;

		[Desc("Withhold a unit from a garrison while ANY of its ammo pools sits below this per-mille of capacity.",
			"Garrisoning a POI is a WALK to it, so a starving platoon is marched off its ground to hold something",
			"it has no ammunition to hold. Matches SupplyFollowerBotModule.HuntStarvingThresholdPerMille. Units",
			"already IN a garrison are left alone — they are standing on the objective, which is what a dry unit",
			"should be doing. 0 = OFF, the shipped default, so the @stable twin (which omits this field)",
			"garrisons regardless of ammo state. No longer byte-identical: StarvingRecruitGate additionally",
			"withholds a unit that is mid-resupply, unconditionally and on both profiles — see the gate.")]
		public readonly int StarvingRecruitThresholdPerMille = 0;

		[Desc("Influence stack (garrison migration): score held-POI defend urgency off the BELIEVED anti-ground",
			"danger field (DangerFieldLayer) instead of the OMNISCIENT InfluenceMap threat grid PoiMap bakes into",
			"the defend score. When on, GetDefendTargets is asked for a threat-NEUTRAL (calm) base score (no",
			"omniscient read) and this module re-applies a fog-legal believed-danger RAISE — the MIRROR of the",
			"capture damp: believed danger at a POI we hold RAISES its defend score and garrison size (something",
			"is pressing it). Completes the @experimental fog migration for garrison ordering. OFF by default so",
			"legacy/normal and the frozen @stable twin stay byte-identical; only PoiGarrisonBotModule@experimental",
			"turns it on. Inert (falls back to the omniscient path) if no DangerFieldLayer exists.")]
		public readonly bool DefendRepointEnabled = false;

		[Desc("Garrison migration: believed anti-ground danger (DangerFieldLayer.GroundDanger) at/below which a",
			"held POI counts as CALM. IN DANGER UNITS (100 = one reference contact at point-blank), NOT raw field",
			"units and NOT the InfluenceMap scale; sits above the Stage-C territory baseline so ambient 'deep",
			"enemy ground' danger doesn't raise every POI.")]
		public readonly int BelievedDangerMildUnits = 30;

		[Desc("Garrison migration: believed anti-ground danger above which a held POI is ASSAULTED (inside a dense",
			"believed weapon envelope) — the level at which the garrison-size threat bump fires. Boundary between",
			"the probed and assaulted buckets. IN DANGER UNITS: 100 = a full reference contact's worth of envelope",
			"over the POI, which is the honest meaning of 'this position is under assault'.")]
		public readonly int BelievedDangerHostileUnits = 100;

		[Desc("Garrison migration: defend-ordering multiplier (x100) at CALM believed danger. Default 100 = inert.")]
		public readonly int BelievedDangerCalmMultiplier = 100;

		[Desc("Garrison migration: defend-ordering multiplier (x100) at PROBED believed danger. >100 RAISES a",
			"probed POI above a calm one so contested income is garrisoned first. Default 100 = inert.")]
		public readonly int BelievedDangerProbedMultiplier = 100;

		[Desc("Garrison migration: defend-ordering multiplier (x100) at ASSAULTED believed danger (dense believed",
			"weapon envelope) — highest urgency, this POI is actively being taken. >100 RAISES most. Default 100 = inert.")]
		public readonly int BelievedDangerAssaultedMultiplier = 100;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			// Case-harden actor-name config (see ActorNameCase).
			ActorNameCase.NormalizeInPlace(ExcludeUnitTypes);
		}

		public override object Create(ActorInitializer init) { return new PoiGarrisonBotModule(init.Self, this); }
	}

	public class PoiGarrisonBotModule : ConditionalTrait<PoiGarrisonBotModuleInfo>, IBotTick
	{
		// A live garrison: a held POI plus the units committed to holding it. Persists
		// across re-evals so defenders aren't reshuffled every scan (hysteresis).
		sealed class Garrison
		{
			public uint PoiId;
			public CPos PoiCell;
			public WPos PoiPos;
			public long Score;
			public int Value;
			public string PoiName;
			public CPos OrderedCell;   // last cell we AttackMoved to (for repath gating)
			public bool HasOrdered;
			public readonly List<Actor> Units = new();
		}

		readonly World world;
		readonly Player player;

		PoiMap poiMap;
		bool poiMapResolved;

		// Influence stack (garrison migration): the believed anti-ground danger field, resolved ONLY when
		// DefendRepointEnabled so the off/@stable path never touches it. When present it replaces the
		// omniscient InfluenceMap threat baked into the defend score/size with a fog-legal RAISE.
		DangerFieldLayer dangerField;
		bool dangerFieldResolved;

		PoiGoalGuard goalGuard;
		bool goalGuardResolved;
		UnitRoleResolver resolver;
		bool resolverResolved;

		readonly List<Garrison> garrisons = new();

		// The ammo term (StarvingRecruitThresholdPerMille); see StarvingRecruitGate.
		readonly StarvingRecruitGate ammoGate = new("garrison");
		int reevalCountdown;

		public PoiGarrisonBotModule(Actor self, PoiGarrisonBotModuleInfo info)
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

			if (!dangerFieldResolved)
			{
				dangerField = Info.DefendRepointEnabled
					? world.WorldActor.TraitOrDefault<DangerFieldLayer>() : null;
				dangerFieldResolved = true;
			}

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

			var tick = world.WorldTick;

			// 1. Drop dead/lost units from live garrisons; sweep orphan defend commitments.
			PruneGarrisons();
			if (goalGuard != null)
				goalGuard.Ledger.Prune(tick, a => !a.IsDead && a.IsInWorld && a.Owner == player);

			// 2. Score our held money POIs (value x distance x defence-urgency). Cap to
			//    MaxGarrisons highest-urgency POIs. Garrison migration: when DefendRepointEnabled (and a
			//    DangerFieldLayer exists) ask PoiMap for a threat-NEUTRAL (calm) base score — no omniscient
			//    read — and re-apply a fog-legal believed-danger RAISE. Off ⇒ the frozen omniscient path, so
			//    the @stable twin (flag unset) stays byte-identical.
			var repoint = Info.DefendRepointEnabled && dangerField != null;
			var targets = repoint
				? RescaleDefendByBelievedDanger(poiMap.GetDefendTargets(player, suppressOmniscientThreat: true))
				: poiMap.GetDefendTargets(player);
			if (targets.Count > Info.MaxGarrisons)
				targets = targets.Take(Info.MaxGarrisons).ToList();

			if (targets.Count == 0)
			{
				RetireAll("no-held-pois");
				Log.Write("debug", $"[exp-garrison] reeval player={player.PlayerName} held=0 garrisons=0 tick={tick}");
				return;
			}

			// 3. Retire garrisons whose POI is no longer held (captured back / gone).
			var keepIds = new HashSet<uint>(targets.Select(t => t.Actor.ActorID));
			var free = new List<Actor>();
			for (var i = garrisons.Count - 1; i >= 0; i--)
			{
				if (!keepIds.Contains(garrisons[i].PoiId))
				{
					free.AddRange(ReleaseGarrison(garrisons[i], "poi-lost"));
					garrisons.RemoveAt(i);
				}
			}

			// 4. Ensure a garrison exists for each target; refresh its scoring.
			foreach (var t in targets)
			{
				var g = garrisons.FirstOrDefault(x => x.PoiId == t.Actor.ActorID);
				if (g == null)
				{
					g = new Garrison { PoiId = t.Actor.ActorID };
					garrisons.Add(g);
				}

				g.PoiCell = t.Location;
				g.PoiPos = t.CenterPosition;
				g.Score = t.Score;
				g.Value = t.Value;
				g.PoiName = t.Actor.Info.Name;
			}

			// 5. Desired size per POI (value ramp + threat bump), then fund them in
			//    priority order from what the pool can spare.
			var ordered = garrisons.OrderByDescending(g => g.Score).ThenBy(g => g.PoiId).ToList();
			free.AddRange(BuildFreePool());
			var pool = free.Count + ordered.Sum(g => g.Units.Count);

			// Sizing threat bump fires when the POI's threat exceeds this threshold. On the omniscient path that
			// is the InfluenceMap ThreatMildThreshold; under the fog migration TargetThreat carries BELIEVED
			// ground danger (danger-field scale), so the bump must key on the ASSAULTED threshold instead —
			// only a dense believed weapon envelope, not the ambient Stage-C baseline, reinforces the hold.
			// Still the correct pattern — the threshold SOURCE switches with the scale TargetThreat carries —
			// and now the believed branch also converts its danger units to raw field units, so the two arms
			// are genuinely in the same units as the value they are compared against rather than merely being
			// different constants.
			var sizeThreatThreshold = repoint
				? dangerField.GroundDangerUnitsToField(Info.BelievedDangerHostileUnits)
				: poiMap.Info.ThreatMildThreshold;
			var desired = ordered
				.Select(g => PoiGarrisonMath.GarrisonSize(g.Value,
					TargetThreat(targets, g.PoiId), sizeThreatThreshold,
					Info.ValuePerGarrisonUnit, Info.MinGarrison, Info.MaxGarrison, Info.ThreatGarrisonBonus))
				.ToList();
			var sizes = PoiGarrisonMath.AllocateGarrisons(desired, pool);

			// 6. Balance each garrison to its granted size: shed surplus, then top up.
			for (var i = 0; i < ordered.Count; i++)
			{
				var g = ordered[i];
				var want = sizes[i];

				if (g.Units.Count > want)
				{
					var surplus = g.Units
						.OrderByDescending(u => (u.CenterPosition - g.PoiPos).LengthSquared)
						.Take(g.Units.Count - want)
						.ToList();
					foreach (var u in surplus)
					{
						g.Units.Remove(u);
						goalGuard?.Ledger.Release(u);
						free.Add(u);
						g.HasOrdered = false; // set changed
					}
				}
			}

			for (var i = 0; i < ordered.Count; i++)
			{
				var g = ordered[i];
				var need = sizes[i] - g.Units.Count;
				if (need <= 0)
					continue;

				var recruits = free
					.OrderBy(u => (u.CenterPosition - g.PoiPos).LengthSquared)
					.ThenBy(u => u.ActorID)
					.Take(need)
					.ToList();

				foreach (var u in recruits)
				{
					free.Remove(u);
					g.Units.Add(u);
					g.HasOrdered = false; // set changed
				}
			}

			// 7. Issue orders + (re)commit. Retire any garrison that ended up empty.
			for (var i = garrisons.Count - 1; i >= 0; i--)
			{
				var g = garrisons[i];
				if (g.Units.Count == 0)
				{
					garrisons.RemoveAt(i);
					continue;
				}

				CommitAndOrder(bot, g, tick);
			}

			Log.Write("debug",
				$"[exp-garrison] reeval player={player.PlayerName} held={targets.Count} pool={pool} free={free.Count} garrisons={garrisons.Count} tick={tick}");
			foreach (var g in garrisons)
				Log.Write("debug",
					$"[exp-garrison] garrison player={player.PlayerName} poi={g.PoiName}@{g.PoiCell} value={g.Value} score={g.Score} units={g.Units.Count} tick={tick}");
		}

		static int TargetThreat(List<ScoredPoi> targets, uint poiId)
		{
			foreach (var t in targets)
				if (t.Actor.ActorID == poiId)
					return t.EnemyInfluence;
			return 0;
		}

		// Re-score the (calm-neutral) defend targets by the BELIEVED anti-ground danger field: believed
		// danger RAISES a held POI's defend score (calm < probed < assaulted), the MIRROR of the capture
		// damp and the fog-legal replacement for the omniscient InfluenceMap threat PoiMap used to bake in.
		// The EnemyInfluence field is repurposed to carry the sampled ground danger so TargetThreat (garrison
		// sizing) reads the believed danger too. Pure factor (PoiScoring.BelievedDefendFactor) draws ZERO
		// random; re-sorts with the SAME comparator PoiMap uses.
		List<ScoredPoi> RescaleDefendByBelievedDanger(List<ScoredPoi> targets)
		{
			var scaled = new List<ScoredPoi>(targets.Count);
			foreach (var p in targets)
			{
				var groundDanger = dangerField.GroundDanger(player, p.Location);
				var mul = PoiScoring.BelievedDefendFactor(groundDanger,
					dangerField.GroundDangerUnitsToField(Info.BelievedDangerMildUnits),
					dangerField.GroundDangerUnitsToField(Info.BelievedDangerHostileUnits),
					Info.BelievedDangerCalmMultiplier, Info.BelievedDangerProbedMultiplier,
					Info.BelievedDangerAssaultedMultiplier);

				var newScore = p.Score * mul / 100;
				scaled.Add(new ScoredPoi(p.Actor, p.Kind, p.Action, p.Value,
					p.DistanceCells, groundDanger, newScore));
			}

			scaled.Sort((a, b) => PoiScoring.CompareForOrder(a.Score, a.DistanceCells, a.Actor.ActorID,
				b.Score, b.DistanceCells, b.Actor.ActorID));
			return scaled;
		}

		List<Actor> BuildFreePool()
		{
			var tick = world.WorldTick;
			var claimed = new HashSet<Actor>(garrisons.SelectMany(g => g.Units));

			return world.Actors
				// Claimed/committed FIRST: a unit already holding a POI is not being recruited, and the ammo
				// gate inside IsEligibleCombatUnit logs a withhold on every candidate it refuses.
				.Where(a => !claimed.Contains(a)
					&& (goalGuard == null || !goalGuard.Ledger.IsCommitted(a, tick))
					&& IsEligibleCombatUnit(a))
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

			// A man too low on ammo to defend the POI should not be marched to it. Inert at 0.
			if (ammoGate.Withhold(a, Info.StarvingRecruitThresholdPerMille))
				return false;

			// Role-model eligibility: MainBattle line units plus IndirectFire artillery (design §6).
			// SHORAD/MANPADS/capturers/logistics/scouts drop out by class; cargo carriers stay owned by
			// MountedTransportBotModule (excluded by trait). See WORKSPACE/DISCOVERIES.md (2026-07-22).
			if (Info.UseUnitRoles && resolver != null)
			{
				var role = resolver.GetRole(a);
				return (role == UnitRole.MainBattle || role == UnitRole.IndirectFire)
					&& !UnitRoleResolver.IsTroopCarrier(a.Info);
			}

			return !Info.ExcludeUnitTypes.Contains(a.Info.Name);
		}

		// Remove units that died / changed owner / lost their garrison commitment.
		void PruneGarrisons()
		{
			foreach (var g in garrisons)
			{
				var key = DefendObjectiveKey(g.PoiId);
				g.Units.RemoveAll(u =>
				{
					if (u.IsDead || !u.IsInWorld || u.Owner != player)
						return true;

					// A committed-but-reclaimed unit (objective no longer ours) leaves.
					if (goalGuard != null
						&& goalGuard.Ledger.TryGetObjective(u, out var obj)
						&& obj != key
						&& obj != null
						&& obj.StartsWith("defend:", StringComparison.Ordinal))
						return true;

					return false;
				});
			}
		}

		void CommitAndOrder(IBot bot, Garrison g, int tick)
		{
			// (Re)commit every unit to this POI so the shared ledger keeps them ours.
			if (goalGuard != null)
			{
				var key = DefendObjectiveKey(g.PoiId);
				foreach (var u in g.Units)
					goalGuard.Ledger.Commit(u, key, tick, Info.GarrisonCommitmentTicks);
			}

			// Only issue a fresh AttackMove when the unit set changed or the POI moved
			// (POIs are stationary, so this is essentially set-change only).
			var moved = !g.HasOrdered
				|| (g.OrderedCell - g.PoiCell).LengthSquared >= Info.RepathThresholdCells * Info.RepathThresholdCells;
			if (!moved)
				return;

			var units = g.Units.ToArray();
			if (!bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, g.PoiCell), false, groupedActors: units)))
				return;

			g.OrderedCell = g.PoiCell;
			g.HasOrdered = true;

			Log.Write("debug",
				$"[exp-garrison] order player={player.PlayerName} poi={g.PoiName}@{g.PoiCell} units={units.Length} tick={tick}");
			AIUtils.BotDebug("AI ({0}): exp-garrison — hold {1}@{2} ({3} units, score={4})",
				player.ClientIndex, g.PoiName, g.PoiCell, units.Length, g.Score);
		}

		// Release a garrison's units back to the free pool and return them.
		List<Actor> ReleaseGarrison(Garrison g, string reason)
		{
			var freed = new List<Actor>(g.Units);
			foreach (var u in g.Units)
				goalGuard?.Ledger.Release(u);
			g.Units.Clear();

			if (freed.Count > 0)
				Log.Write("debug",
					$"[exp-garrison] retire player={player.PlayerName} poi={g.PoiName} freed={freed.Count} reason={reason} tick={world.WorldTick}");
			return freed;
		}

		void RetireAll(string reason)
		{
			foreach (var g in garrisons)
				ReleaseGarrison(g, reason);
			garrisons.Clear();
		}

		static string DefendObjectiveKey(uint poiId) => "defend:" + poiId;
	}

	// ============================================================
	// Pure garrison math — engine-free, unit-tested (PoiGarrisonTest). Ports to v3.
	// ============================================================
	public static class PoiGarrisonMath
	{
		/// <summary>Desired garrison for a held money POI: a VALUE ramp (value / valuePerUnit,
		/// clamped to [minGarrison, maxGarrison]) plus a THREAT bump when the POI is under
		/// hostile (assaulted) enemy influence — the whole thing clamped to maxGarrison so a
		/// garrison stays small (1-3) and never starves the offense. With valuePerUnit=50 and
		/// [1,3]: OILB($50)->1, FCOM($100)->2, BIO($150)->3; under assault each +bonus, capped.</summary>
		public static int GarrisonSize(int value, int enemyInfluence, int mildThreshold,
			int valuePerUnit, int minGarrison, int maxGarrison, int threatBonus)
		{
			var min = Math.Max(0, minGarrison);
			var max = Math.Max(min, maxGarrison);

			var perUnit = Math.Max(1, valuePerUnit);
			var baseSize = Math.Clamp(value / perUnit, min, max);

			// Under hostile (assaulted) influence, reinforce the hold — still clamped.
			if (enemyInfluence > mildThreshold)
				baseSize = Math.Min(max, baseSize + Math.Max(0, threatBonus));

			return baseSize;
		}

		/// <summary>Fund garrisons from a shared pool in PRIORITY order (sizes given
		/// score-desc): each POI gets its full desired size until the pool runs dry, then the
		/// next gets whatever remains (possibly 0). The pool passed in is the army's SPARE
		/// capacity, so highest-urgency held POIs are garrisoned first and the tail is dropped
		/// rather than dribbling every POI thin — offense keeps the rest. Sum &lt;= poolSize.</summary>
		public static int[] AllocateGarrisons(IReadOnlyList<int> desiredSizes, int poolSize)
		{
			var n = desiredSizes.Count;
			var result = new int[n];
			if (n == 0 || poolSize <= 0)
				return result;

			var remaining = poolSize;
			for (var i = 0; i < n && remaining > 0; i++)
			{
				var grant = Math.Min(Math.Max(0, desiredSizes[i]), remaining);
				result[i] = grant;
				remaining -= grant;
			}

			return result;
		}
	}
}
