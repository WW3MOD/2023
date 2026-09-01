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
using OpenRA.Mods.Common.Warheads;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum UnitStance { HoldFire, Ambush, FireAtWill }

	public enum EngagementStance { HoldPosition, Defensive, Hunt }

	public enum CohesionMode { Tight, Loose, Spread }

	public enum ResupplyBehavior { Hold, Auto, Evacuate }

	[RequireExplicitImplementation]
	public interface IActivityNotifyStanceChanged : IActivityInterface
	{
		void StanceChanged(Actor self, AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance);
	}

	[RequireExplicitImplementation]
	public interface INotifyStanceChanged
	{
		void StanceChanged(Actor self, AutoTarget autoTarget, UnitStance oldStance, UnitStance newStance);
	}

	[RequireExplicitImplementation]
	public interface IActivityNotifyEngagementStanceChanged : IActivityInterface
	{
		void EngagementStanceChanged(Actor self, AutoTarget autoTarget, EngagementStance oldStance, EngagementStance newStance);
	}

	[RequireExplicitImplementation]
	public interface INotifyEngagementStanceChanged
	{
		void EngagementStanceChanged(Actor self, AutoTarget autoTarget, EngagementStance oldStance, EngagementStance newStance);
	}

	[Desc("The actor will automatically engage the enemy when it is in range.",
		"Also hosts stance state (fire/engagement/cohesion/resupply) — may be added to weaponless units (e.g. supply trucks) purely to expose the stance UI.")]
	public class AutoTargetInfo : ConditionalTraitInfo, IEditorActorOptions
	{
		[Desc("It will try to hunt down the enemy if engagement stance is set to Hunt.")]
		public readonly bool AllowMovement = true;

		[Desc("It will try to pivot to face the enemy if stance is not HoldFire.")]
		public readonly bool AllowTurning = true;

		[Desc("Scan for new targets when idle.")]
		public readonly bool ScanOnIdle = true;

		[Desc("Set to a value >1 to override weapons maximum range for this.")]
		public readonly int ScanRadius = -1;

		[Desc("Possible values are HoldFire, Ambush and FireAtWill.",
			"Used for computer-controlled players, both Lua-scripted and regular Skirmish AI alike.")]
		public readonly UnitStance InitialStanceAI = UnitStance.FireAtWill;

		[Desc("Possible values are HoldFire, Ambush and FireAtWill. Used for human players.")]
		public readonly UnitStance InitialStance = UnitStance.FireAtWill;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while in the HoldFire stance.")]
		public readonly string HoldFireCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while in the Ambush stance.")]
		public readonly string AmbushCondition = null;

		[Desc("Range in cells within which ambush units coordinate — when one is spotted, nearby allies in Ambush also engage.")]
		public readonly int AmbushCoordinationRadius = 10;

		[ConsumedConditionReference]
		[Desc("Widened-ambush gate (PIPELINE item 8). While this condition is granted AND the unit is in",
			"the Ambush fire-stance, attack-move / auto-move contact HALTS the march into an idle ambush",
			"(hold fire until spotted) instead of firing on contact — see AttackMoveActivity + AmbushTactics.",
			"Empty (default) = OFF and byte-identical to stock attack-move engage. Point it at a default-off",
			"token (e.g. enable-ambush-tactics) that a human opt-in / bot ledger commit / test map grants.",
			"NOTE (b8d2e601, 2026-08-02): @stable grants it too — LaneAmbushBotModule@stable (in ai.yaml)",
			"posts ambushers and grants this token to them, so @stable's ambushers DO take the gated path.",
			"The gate stays per-UNIT: a unit no ambush module posted never sees it, on any profile.")]
		public readonly string AmbushTacticsCondition = null;

		// ── Stage 3 (PIPELINE item 8): stationary literal-ambush state machine. ALL of the following are
		// read ONLY on the gated path (AmbushTacticsCondition granted AND stance == Ambush). They never
		// touch the ungated path, so their values are irrelevant to any unit without the gate — it
		// short-circuits before any of this is read. NOTE (b8d2e601, 2026-08-02): "ungated" no longer means
		// "@stable" — its LaneAmbush twin posts ambushers that DO read these. Defaults are tuned for a
		// sensible opt-in / test grant; the worthwhile weights/thresholds are meant to be tuned in autotest.
		[Desc("Stage-3 ambush: radius (cells) of the kill-zone actor-scan that feeds the worthwhile score.")]
		public readonly int AmbushKillZoneRadius = 8;

		[Desc("Stage-3 ambush: minimum ticks between kill-zone rescans (worthwhile score refresh cadence).",
			"Elapsed-based so it self-staggers by target-acquisition tick; matches the influence-stack 25-tick cadence.")]
		public readonly int AmbushScoreCadence = 25;

		[Desc("Stage-3 ambush: look-ahead (ticks) for the trigger-3 range-exit prediction on the best target.")]
		public readonly int AmbushExitPredictTicks = 20;

		[Desc("Stage-3 ambush: worthwhile-score floor for trigger 3 (best-strike degrading). Below it a",
			"departing target is not worth breaking concealment for.")]
		public readonly int AmbushMinSpringThreshold = 100;

		[Desc("Stage-3 ambush: worthwhile-score ceiling for trigger 4 (saturation). Score at/above this,",
			"sustained AmbushRequiredHighSamples samples, springs at peak density.")]
		public readonly int AmbushHighSpringThreshold = 400;

		[Desc("Stage-3 ambush: consecutive degrade samples required before trigger 3 fires (hysteresis).")]
		public readonly int AmbushRequiredDegradeSamples = 2;

		[Desc("Stage-3 ambush: consecutive high-score samples required before trigger 4 fires (hysteresis).")]
		public readonly int AmbushRequiredHighSamples = 2;

		[Desc("Stage-3 ambush: range-opening epsilon band (WDist) below which a sample is NOT a degrade —",
			"jitter/rounding near a stationary target must not read as a steady retreat.")]
		public readonly WDist AmbushDegradeEpsilon = new WDist(256);

		[Desc("Stage-3 ambush: overrun floor (cells) for trigger 5 when the weapon MinRange is 0. An",
			"engageable enemy at/inside max(weapon MinRange, this) is treated as overrunning the position.")]
		public readonly int AmbushOverrunFloor = 2;

		[Desc("Stage-3 ambush: weight on the per-contact threat term in the worthwhile score.")]
		public readonly int AmbushThreatWeight = 1;

		[Desc("Stage-3 ambush: weight on the per-contact economic-value term in the worthwhile score.")]
		public readonly int AmbushValueWeight = 1;

		[Desc("Stage-3 ambush: base threat credited to any armed contact (before HP/Cost terms).")]
		public readonly int AmbushThreatBase = 100;

		[Desc("Stage-3 ambush: divisor turning a contact's MaxHP into extra threat.")]
		public readonly int AmbushThreatHealthDivisor = 10;

		[Desc("Stage-3 ambush: divisor turning a contact's Cost into extra threat.")]
		public readonly int AmbushThreatCostDivisor = 50;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while in the FireAtWill stance.")]
		public readonly string FireAtWillCondition = null;

		[FieldLoader.Ignore]
		public readonly Dictionary<UnitStance, string> ConditionByStance = new Dictionary<UnitStance, string>();

		[Desc("Allow the player to change the unit stance.")]
		public readonly bool EnableStances = true;

		[Desc("Possible values are HoldPosition, Defensive and Hunt.",
			"Used for computer-controlled players, both Lua-scripted and regular Skirmish AI alike.")]
		public readonly EngagementStance InitialEngagementStanceAI = EngagementStance.Defensive;

		[Desc("Possible values are HoldPosition, Defensive and Hunt. Used for human players.")]
		public readonly EngagementStance InitialEngagementStance = EngagementStance.Defensive;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while in the HoldPosition engagement stance.")]
		public readonly string HoldPositionCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while in the Defensive engagement stance.")]
		public readonly string DefensiveCondition = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while in the Hunt engagement stance.")]
		public readonly string HuntCondition = null;

		[FieldLoader.Ignore]
		public readonly Dictionary<EngagementStance, string> ConditionByEngagementStance = new Dictionary<EngagementStance, string>();

		[Desc("Possible values are Tight, Loose and Spread.",
			"Used for computer-controlled players, both Lua-scripted and regular Skirmish AI alike.")]
		public readonly CohesionMode InitialCohesionAI = CohesionMode.Loose;

		[Desc("Possible values are Tight, Loose and Spread. Used for human players.")]
		public readonly CohesionMode InitialCohesion = CohesionMode.Loose;

		[Desc("Possible values are Hold, Seek and Rotate.",
			"Used for computer-controlled players, both Lua-scripted and regular Skirmish AI alike.")]
		public readonly ResupplyBehavior InitialResupplyBehaviorAI = ResupplyBehavior.Auto;

		[Desc("Possible values are Hold, Seek and Rotate. Used for human players.")]
		public readonly ResupplyBehavior InitialResupplyBehavior = ResupplyBehavior.Auto;

		[Desc("Ticks to wait until next AutoTarget: attempt.")]
		public readonly int MinimumScanTimeInterval = 3;

		[Desc("Ticks to wait until next AutoTarget: attempt.")]
		public readonly int MaximumScanTimeInterval = 8;

		[Desc("Ticks between re-evaluations of an ALREADY auto-picked target. Without this a unit shooting",
			"infantry keeps shooting it while an enemy tank sits in range unengaged, because the priority",
			"table is consulted once and then bypassed by two different routes: an idle unit's stale target",
			"is handed straight back by IOverrideAutoTarget, and a unit with a running attack activity never",
			"scans at all. This one interval governs both. A re-evaluation switches only to a STRICTLY higher",
			"AutoTargetPriority band, and only to something shootable from where the unit stands. Targets a",
			"player, Lua or a bot ordered are never re-evaluated.",
			"0 = disabled (behaviour is byte-identical to no re-evaluation).")]
		public readonly int PreemptScanInterval = 0;

		[Desc("Skip targets whose AverageDamagePercent exceeds this threshold.",
			"Prevents overkill — idle units won't fire at targets that already have enough incoming damage to destroy them.",
			"Set to -1 to disable overkill prevention.")]
		public readonly int OverkillThreshold = 100;

		[Desc("AverageDamagePercent above which the soft anti-overkill penalty kicks in.",
			"Below this threshold the priority bucket / range tiebreaker is used unchanged.")]
		public readonly int SoftOverkillThreshold = 50;

		[Desc("Divisor for the soft-overkill penalty formula:",
			"penalty = targetRange * AverageDamagePercent / SoftOverkillScale.",
			"Lower = stronger penalty (target appears further). Default 50 = penalty doubles range at 100% mark.")]
		public readonly int SoftOverkillScale = 50;

		[Desc("Divisor for the healthy-target preference:",
			"penalty = targetRange * (100 - target health %) / HealthPreferenceScale.",
			"A wounded enemy scores as though it stood further away, so a healthy one at equal range always",
			"wins. Linear in health, so it discriminates across the WHOLE band and not merely near-full vs",
			"near-dead: at the default 100 a 60% target reads as 1.4x its range and a 40% target as 1.6x.",
			"This is a PREFERENCE, not a filter — the penalty is bounded by one range-length and stays inside",
			"the range tiebreak, so a wounded unit that is the only thing in range is still engaged and",
			"finished off. Abandoning is BreakOffCondition's job, and that only reaches units already below",
			"the critical line. Lower = stronger preference. Set to 0 to disable (byte-identical to no term).")]
		public readonly int HealthPreferenceScale = 100;

		[Desc("If a target has this condition, autotarget treats it as already-finished and skips it.",
			"In-progress autotarget/opportunity attacks also break off when the current target acquires this condition.",
			"This is a preference, never a validity rule: an explicitly ordered attack (a player order, the Lua",
			"Actor.Attack binding, or any force-attack) still fires on these targets. See AttackBase.BreakOffApplies.",
			"Set to empty string to disable.")]
		public readonly string BreakOffCondition = "critical-damage";

		[ConsumedConditionReference]
		[Desc("AoE-aware cluster targeting (PIPELINE item 14): while this condition is granted on the unit,",
			"an area weapon prefers the target whose surrounding clump takes the most projected splash over",
			"simply the closest one. Empty (default) = OFF and byte-identical to the plain closest/priority score.",
			"Gate it to @experimental bot-owned pieces with enable-ai-experimental; a human opt-in token can be",
			"pointed here later. Inert on units whose weapon has no meaningful area warhead (see ClusterMinWarheadSpread).")]
		public readonly string ClusterTargetingCondition = null;

		[Desc("Cluster targeting: horizontal radius around a candidate within which enemy neighbours count",
			"toward its clump score. The splash-weight curve (ClusterMinWarheadSpread gate + falloff shape) is",
			"weapon-derived; this sets the scale. Only read while ClusterTargetingCondition is granted.")]
		public readonly WDist ClusterRadius = WDist.FromCells(3);

		[Desc("Cluster targeting: a unit only counts as an area weapon (and gets the cluster term) if some enabled",
			"weapon has a SpreadDamage warhead whose Spread is at least this. Excludes rifles/tank rounds even if",
			"the condition is granted.")]
		public readonly WDist ClusterMinWarheadSpread = new WDist(48);

		[Desc("Cluster targeting: WDist length of priority pull earned per 100 cluster-score points. Higher = a",
			"clump outweighs a larger range gap. Bounded by ClusterMaxBonus so it never crosses a priority bucket.")]
		public readonly int ClusterBonusScale = 96;

		[Desc("Cluster targeting: hard cap (WDist length) on the priority pull a clump can earn. MUST stay well",
			"below the priority bucket size so cluster preference never lets a low-priority clump beat a",
			"high-priority target — it only reorders WITHIN a priority class, like the soft-overkill penalty.")]
		public readonly WDist ClusterMaxBonus = WDist.FromCells(24);

		[Desc("Display order for the stance dropdown in the map editor")]
		public readonly int EditorStanceDisplayOrder = 1;
		public override object Create(ActorInitializer init) { return new AutoTarget(init, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			base.RulesetLoaded(rules, info);

			if (HoldFireCondition != null)
				ConditionByStance[UnitStance.HoldFire] = HoldFireCondition;

			if (AmbushCondition != null)
				ConditionByStance[UnitStance.Ambush] = AmbushCondition;

			if (FireAtWillCondition != null)
				ConditionByStance[UnitStance.FireAtWill] = FireAtWillCondition;

			if (HoldPositionCondition != null)
				ConditionByEngagementStance[EngagementStance.HoldPosition] = HoldPositionCondition;

			if (DefensiveCondition != null)
				ConditionByEngagementStance[EngagementStance.Defensive] = DefensiveCondition;

			if (HuntCondition != null)
				ConditionByEngagementStance[EngagementStance.Hunt] = HuntCondition;
		}

		IEnumerable<EditorActorOption> IEditorActorOptions.ActorOptions(ActorInfo ai, World world)
		{
			// Indexed by UnitStance
			var stances = new[] { "holdfire", "ambush", "fireatwill" };

			var labels = new Dictionary<string, string>()
			{
				{ "holdfire", "Hold Fire" },
				{ "ambush", "Ambush" },
				{ "fireatwill", "Fire at Will" },
			};

			yield return new EditorActorDropdown("Stance", EditorStanceDisplayOrder,
				_ => labels,
				(actor, _) =>
				{
					var init = actor.GetInitOrDefault<StanceInit>(this);
					var stance = init?.Value ?? InitialStance;
					return stances[(int)stance];
				},
				(actor, value) => actor.ReplaceInit(new StanceInit(this, (UnitStance)stances.IndexOf(value))));
		}
	}

	public class AutoTarget : ConditionalTrait<AutoTargetInfo>, INotifyIdle, INotifyDamage, ITick, IResolveOrder, ISync, INotifyOwnerChanged
	{
		// Populated in Created() rather than the constructor so it doesn't depend on AutoTarget being
		// constructed AFTER AttackBase. AutoTargetInfo no longer declares Requires<AttackBaseInfo>
		// (so weaponless units can host stance state), which means construction order is no longer
		// guaranteed — a constructor-time TraitsImplementing<AttackBase>() snapshot would be empty.
		public IEnumerable<AttackBase> ActiveAttackBases { get; private set; } = Array.Empty<AttackBase>();

		readonly bool allowMovement;

		[Sync]
		int nextScanTime = 0;

		/// <summary>Count of scans that reached ChooseTarget with NO trait holding a commitment —
		/// neither a live RequestedTarget nor a persistent OpportunityTarget. Deliberately NOT [Sync]
		/// and read by nothing in the simulation; it exists so an autotest can tell a mid-engagement
		/// handover apart from a re-acquisition after the engagement lapsed. Those two produce the
		/// SAME end state (the unit shoots the better target), which is why an outcome-only assertion
		/// cannot discriminate target preemption — see tools/autotest/scenarios/test-autotarget-preempt-air.</summary>
		public int UncommittedScanCount { get; private set; }

		/// <summary>WorldTick of the last scan that found something it could have shot and
		/// declined it anyway — overkill or break-off — leaving the unit with no target at all.
		/// Deliberately NOT [Sync] and read by nothing in the simulation: it exists so
		/// WithHoldingFireDecoration can tell the player why a unit is standing still next to a
		/// live enemy. -1 until the first such scan.</summary>
		public int LastHeldFireTick { get; private set; } = -1;

		public UnitStance Stance => stance;

		public EngagementStance EngagementStanceValue => engagementStance;

		public CohesionMode CohesionValue => cohesion;

		public ResupplyBehavior ResupplyBehaviorValue => resupplyBehavior;

		// These four gate real simulation branches — CohesionMoveModifier.ModifyGroupOrder reads
		// CohesionValue and Stance to choose a formation and to rewrite each actor's move target
		// (Tight even clears CohesionSlotMemory and passes the order through untouched), and
		// ResupplyBehaviorValue is read by AmmoPool, AutoSeekSupplies, SupplyProvider and
		// DropsSupplyCache. Until now none of them were in the sync hash, so two clients could
		// disagree about a unit's stance indefinitely and every sync report would still show
		// AutoTarget matching. A matching trait only ever cleared its HASHED fields.
		//
		// OpenRA's Sync hasher rejects enum types (Sync.cs:71 — only int/bool/registered structs),
		// so sync the int projection rather than the enum, matching StancePositioningExecutor.
		[Sync]
		int SyncStance => (int)stance;

		[Sync]
		int SyncEngagementStance => (int)engagementStance;

		[Sync]
		int SyncCohesion => (int)cohesion;

		[Sync]
		int SyncResupplyBehavior => (int)resupplyBehavior;

		/// <summary>True once this unit has SPRUNG its ambush (any Stage-3 trigger, a spot on the stock path,
		/// or a retaliation) — read-only view of the internal <c>ambushTriggered</c> latch. SPRUNG is terminal
		/// until the stance is reset away from Ambush (see <see cref="ResetAmbushState"/>), so a bot consumer
		/// (PIPELINE item 8 Stage 4) that posts lane ambushers polls this to release a fired unit back to normal
		/// tasking rather than leaving it latched forever. The latch evolves by pure integer/bool math over
		/// already-synced world state with zero RNG (see the field-group comment below), so it is deterministic
		/// across clients even though it is not itself [Sync] — a decision gated on it stays in lockstep.</summary>
		public bool AmbushSprung => ambushTriggered;

		[Sync]
		public Actor Aggressor;

		// NOT SYNCED: do not refer to this anywhere other than UI code
		public UnitStance PredictedStance;

		// NOT SYNCED: do not refer to this anywhere other than UI code
		public EngagementStance PredictedEngagementStance;

		// NOT SYNCED: do not refer to this anywhere other than UI code
		public CohesionMode PredictedCohesion;

		// NOT SYNCED: do not refer to this anywhere other than UI code
		public ResupplyBehavior PredictedResupplyBehavior;

		// Ambush system: track pre-aimed target and spotted state
		Target ambushPreAimTarget = Target.Invalid;
		bool ambushTriggered;

		// Stage 3 (PIPELINE item 8) — stationary literal-ambush tracking. NOT [Sync] (like ambushTriggered /
		// ambushPreAimTarget / PredictedStance above): these evolve by pure integer math over already-synced
		// world state (ranges, ActorIDs, WorldTick) with zero RNG, so they are deterministic across clients
		// without needing to be in the sync hash. They are read/written ONLY on the gated path, so they stay
		// at their defaults (all 0 / int.MinValue) forever on any ungated unit. NOTE (b8d2e601, 2026-08-02):
		// @stable's LaneAmbush-posted ambushers ARE gated, so these do evolve there.
		int ambushLastScore;
		int ambushLastScoreTick = int.MinValue;
		uint ambushBestTargetId;          // ActorID of the best target at the last sample (0 = none)
		int ambushBestPrevRange;
		int ambushBestRadialPerTick;      // signed: positive = opening the range
		int ambushDegradeSamples;         // consecutive "range opening" samples on the best target
		int ambushHighSamples;            // consecutive "score ≥ HighSpringThreshold" samples
		bool ambushBestPredictedExit;     // best target predicted to leave weapon range within K ticks
		bool ambushOverrun;               // an engageable enemy has breached the overrun threshold

		UnitStance stance;
		EngagementStance engagementStance;
		CohesionMode cohesion;
		ResupplyBehavior resupplyBehavior;
		IOverrideAutoTarget[] overrideAutoTarget;
		INotifyStanceChanged[] notifyStanceChanged;
		INotifyEngagementStanceChanged[] notifyEngagementStanceChanged;
		AutoTargetPriority[] allTargetPriorities;
		readonly List<AutoTargetPriorityInfo> reusableActivePriorities = new List<AutoTargetPriorityInfo>();
		readonly List<AutoTargetPriorityInfo> reusableValidPriorities = new List<AutoTargetPriorityInfo>();
		Turreted[] turretedTraits;
		int conditionToken = Actor.InvalidConditionToken;
		int engagementConditionToken = Actor.InvalidConditionToken;

		public void SetStance(Actor self, UnitStance value)
		{
			if (stance == value)
				return;

			var oldStance = stance;
			stance = value;
			ApplyStanceCondition(self);

			// Reset ambush tracking when leaving Ambush stance
			if (oldStance == UnitStance.Ambush && value != UnitStance.Ambush)
				ResetAmbushState();

			foreach (var nsc in notifyStanceChanged)
				nsc.StanceChanged(self, this, oldStance, stance);

			if (self.CurrentActivity != null)
				foreach (var a in self.CurrentActivity.ActivitiesImplementing<IActivityNotifyStanceChanged>())
					a.StanceChanged(self, this, oldStance, stance);
		}

		public void SetEngagementStance(Actor self, EngagementStance value)
		{
			if (engagementStance == value)
				return;

			var oldStance = engagementStance;
			engagementStance = value;
			ApplyEngagementStanceCondition(self);

			foreach (var nsc in notifyEngagementStanceChanged)
				nsc.EngagementStanceChanged(self, this, oldStance, engagementStance);

			if (self.CurrentActivity != null)
				foreach (var a in self.CurrentActivity.ActivitiesImplementing<IActivityNotifyEngagementStanceChanged>())
					a.EngagementStanceChanged(self, this, oldStance, engagementStance);
		}

		public void SetCohesion(Actor self, CohesionMode value)
		{
			if (cohesion == value)
				return;

			cohesion = value;
		}

		public void SetResupplyBehavior(Actor self, ResupplyBehavior value)
		{
			if (resupplyBehavior == value)
				return;

			resupplyBehavior = value;
		}

		void ApplyStanceCondition(Actor self)
		{
			if (conditionToken != Actor.InvalidConditionToken)
				conditionToken = self.RevokeCondition(conditionToken);

			if (Info.ConditionByStance.TryGetValue(stance, out var condition))
				conditionToken = self.GrantCondition(condition);
		}

		void ApplyEngagementStanceCondition(Actor self)
		{
			if (engagementConditionToken != Actor.InvalidConditionToken)
				engagementConditionToken = self.RevokeCondition(engagementConditionToken);

			if (Info.ConditionByEngagementStance.TryGetValue(engagementStance, out var condition))
				engagementConditionToken = self.GrantCondition(condition);
		}

		public AutoTarget(ActorInitializer init, AutoTargetInfo info)
			: base(info)
		{
			var self = init.Self;

			stance = init.GetValue<StanceInit, UnitStance>(self.Owner.IsBot || !self.Owner.Playable ? info.InitialStanceAI : info.InitialStance);
			engagementStance = init.GetValue<EngagementStanceInit, EngagementStance>(
				self.Owner.IsBot || !self.Owner.Playable ? info.InitialEngagementStanceAI : info.InitialEngagementStance);

			cohesion = self.Owner.IsBot || !self.Owner.Playable ? info.InitialCohesionAI : info.InitialCohesion;
			resupplyBehavior = self.Owner.IsBot || !self.Owner.Playable ? info.InitialResupplyBehaviorAI : info.InitialResupplyBehavior;

			PredictedStance = stance;
			PredictedEngagementStance = engagementStance;
			PredictedCohesion = cohesion;
			PredictedResupplyBehavior = resupplyBehavior;

			allowMovement = Info.AllowMovement && self.TraitOrDefault<IMove>() != null;
		}

		protected override void Created(Actor self)
		{
			// PITFALL: per-type stance defaults are NOT applied here. UnitDefaultsManager is backed by a
			// per-machine file, and this runs on every client for every actor — so reading it here made
			// each client apply ITS OWN preferences to everyone's units, diverging synced state with no
			// input from anybody. UnitDefaultsManager now applies them client-locally by issuing the
			// normal SetUnitStance/SetCohesion/... orders, which every client then resolves identically.

			// Snapshot AttackBase traits now that all traits have been constructed. The .Where filter
			// stays deferred so IsTraitDisabled is re-evaluated at each enumeration.
			var attackBaseSnapshot = self.TraitsImplementing<AttackBase>().ToArray();
			ActiveAttackBases = attackBaseSnapshot.Where(t => !t.IsTraitDisabled);

			// AutoTargetPriority and their Priorities are fixed - so we can safely cache them with ToArray.
			// IsTraitEnabled can change over time, so we filter at use time via GetActivePriorities().
			allTargetPriorities =
				self.TraitsImplementing<AutoTargetPriority>()
					.OrderByDescending(ati => ati.Info.Priority).ToArray();

			overrideAutoTarget = self.TraitsImplementing<IOverrideAutoTarget>().ToArray();
			notifyStanceChanged = self.TraitsImplementing<INotifyStanceChanged>().ToArray();
			notifyEngagementStanceChanged = self.TraitsImplementing<INotifyEngagementStanceChanged>().ToArray();
			turretedTraits = self.TraitsImplementing<Turreted>().ToArray();
			ApplyStanceCondition(self);
			ApplyEngagementStanceCondition(self);

			base.Created(self);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			PredictedStance = self.Owner.IsBot || !self.Owner.Playable ? Info.InitialStanceAI : Info.InitialStance;
			SetStance(self, PredictedStance);

			PredictedEngagementStance = self.Owner.IsBot || !self.Owner.Playable ? Info.InitialEngagementStanceAI : Info.InitialEngagementStance;
			SetEngagementStance(self, PredictedEngagementStance);

			PredictedCohesion = self.Owner.IsBot || !self.Owner.Playable ? Info.InitialCohesionAI : Info.InitialCohesion;
			SetCohesion(self, PredictedCohesion);

			PredictedResupplyBehavior = self.Owner.IsBot || !self.Owner.Playable ? Info.InitialResupplyBehaviorAI : Info.InitialResupplyBehavior;
			SetResupplyBehavior(self, PredictedResupplyBehavior);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "SetUnitStance" && Info.EnableStances)
				SetStance(self, (UnitStance)order.ExtraData);

			if (order.OrderString == "SetEngagementStance" && Info.EnableStances)
				SetEngagementStance(self, (EngagementStance)order.ExtraData);

			if (order.OrderString == "SetCohesion" && Info.EnableStances)
				SetCohesion(self, (CohesionMode)order.ExtraData);

			if (order.OrderString == "SetResupplyBehavior" && Info.EnableStances)
				SetResupplyBehavior(self, (ResupplyBehavior)order.ExtraData);

			// ExtraData: 1 grants the ambush-tactics gate, 0 revokes it. Issued by LaneAmbushBotModule
			// when it posts or releases an ambusher. It is an ORDER rather than the bot granting the
			// condition itself because bot ticks are suppressed during a saved-game restore
			// (ModularBot.cs:206), so a direct grant is not in the order stream, the restored world never
			// gates the unit, and AttackMoveActivity's halt-before-contact silently stops firing.
			if (order.OrderString == "SetAmbushGate")
				SetAmbushGate(self, order.ExtraData != 0);
		}

		// PITFALL: ambushGateToken is deliberately NOT [Sync]. A condition token is an allocation handle
		// whose value counts how many conditions the actor has ever been granted, so syncing one makes
		// handle identity a determinism requirement — exactly the Detectable desync fixed in e1bbf244.
		// The synced gameplay state is the condition itself, which the gate reads by count.
		int ambushGateToken = Actor.InvalidConditionToken;
		ExternalCondition ambushGateExternal;

		void SetAmbushGate(Actor self, bool grant)
		{
			var gate = Info.AmbushTacticsCondition;
			if (string.IsNullOrEmpty(gate))
				return;

			// Idempotent in both directions. The module re-offers this order on its own cadence, and a
			// duplicate grant would leak a second token that the matching single revoke could not clear.
			if (grant)
			{
				if (ambushGateToken != Actor.InvalidConditionToken)
					return;

				var ec = self.TraitsImplementing<ExternalCondition>()
					.FirstOrDefault(e => e.Info.Condition == gate && e.CanGrantCondition(this));

				if (ec == null)
					return;

				var token = ec.GrantCondition(self, this);
				if (token == Actor.InvalidConditionToken)
					return;

				ambushGateExternal = ec;
				ambushGateToken = token;
			}
			else
			{
				if (ambushGateToken == Actor.InvalidConditionToken)
					return;

				ambushGateExternal?.TryRevokeCondition(self, this, ambushGateToken);
				ambushGateExternal = null;
				ambushGateToken = Actor.InvalidConditionToken;
			}
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (IsTraitDisabled || !self.IsIdle || Stance < UnitStance.Ambush)
				return;

			// Don't retaliate against healers
			if (e.Damage.Value < 0)
				return;

			var attacker = e.Attacker;
			if (attacker.Disposed)
				return;

			// Don't change targets when there is a target overriding auto-targeting
			foreach (var oat in overrideAutoTarget)
				if (oat.TryGetAutoTargetOverride(self, out _, out _))
					return;

			if (!attacker.IsInWorld)
			{
				// If the aggressor is in a transport, then attack the transport instead
				var passenger = attacker.TraitOrDefault<Passenger>();
				if (passenger != null && passenger.Transport != null)
					attacker = passenger.Transport;
			}

			// Don't fire at an invisible enemy when we can't move to reveal it
			var allowMove = allowMovement && engagementStance >= EngagementStance.Hunt;
			if (!allowMove && !attacker.CanBeViewedByPlayer(self.Owner))
				return;

			// Not a lot we can do about things we can't hurt... although maybe we should automatically run away?
			var attackerAsTarget = Target.FromActor(attacker);
			if (!ActiveAttackBases.Any(a => a.HasAnyValidWeapons(attackerAsTarget)))
				return;

			// Don't retaliate against own units force-firing on us. It's usually not what the player wanted.
			if (attacker.AppearsFriendlyTo(self))
				return;

			Aggressor = attacker;

			// If in Ambush, trigger self and coordinate nearby allies
			if (Stance == UnitStance.Ambush)
			{
				ambushTriggered = true;
				TriggerNearbyAmbushAllies(self, Target.FromActor(attacker));
			}

			// Retaliation the trait decided on by itself, so genuinely AutoTarget-sourced.
			Attack(Target.FromActor(Aggressor), allowMove, AttackSource.AutoTarget);
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (IsTraitDisabled || !Info.ScanOnIdle || (Stance < UnitStance.Ambush))
				return;

			if (Stance == UnitStance.Ambush)
			{
				AmbushTickIdle(self);
				return;
			}

			// Hunt: actively chase targets. Balanced: allow moving to clear LOS only (handled in Attack activity).
			// Defensive/HoldPosition: no auto-move toward targets.
			var allowMove = allowMovement && engagementStance >= EngagementStance.Hunt;
			var allowTurn = Info.AllowTurning && Stance > UnitStance.HoldFire;
			ScanAndAttack(self, allowMove, allowTurn);
		}

		void AmbushTickIdle(Actor self)
		{
			// Stage 3 gate (PIPELINE item 8). Read FIRST, exactly like the Stage-2 halt: when the gate is
			// not granted (every un-opted-in unit — but since b8d2e601, 2026-08-02, NOT @stable's ambushers,
			// which LaneAmbushBotModule@stable posts and grants) NOTHING below touches the Stage-3 state, and
			// the else-branch is character-for-character the stock ambush idle behaviour.
			var stage3 = AmbushTacticsGranted(self);

			// Scan at full range — ambush doesn't reduce scan radius. PITFALL: ScanForTarget returns Invalid
			// BOTH when a scan ran and found nothing AND when the scan interval simply hasn't elapsed — and a
			// scan that does run re-arms nextScanTime, so whether THIS tick actually scanned must be captured
			// BEFORE the call.
			var scannedThisTick = nextScanTime <= 0;

			// fromProtectedOverride must be threaded here too, not discarded: this path re-issues the
			// scanned target below, and re-stamping a player/Lua/bot target as AutoTarget would make it
			// preemptable. The re-stamp is inert while the unit is in Ambush (PreemptionDue requires
			// >= FireAtWill) but nothing clears it — StanceChanged returns early on a stance INCREASE —
			// so raising the unit to Fire At Will would activate it retroactively.
			var target = ScanForTarget(self, false, true, false, out var fromProtectedOverride);
			var scanSource = fromProtectedOverride ? AttackSource.Default : AttackSource.AutoTarget;

			// Gated Stage-3 only: an off-interval Invalid means "no scan happened this tick", NOT "target
			// lost". Reuse the cached pre-aim target (if it is still alive and legally visible) so the cadence
			// sample counters survive between scans — resetting them on every off-scan idle tick made
			// AmbushRequiredHighSamples >= 2 unreachable, so a score-driven spring could never fire (found by
			// the convoy GREEN autotest, 260725). The ungated stock path below is untouched.
			if (stage3 && !scannedThisTick && target.Type == TargetType.Invalid
				&& ambushPreAimTarget.Type == TargetType.Actor
				&& ambushPreAimTarget.Actor.IsInWorld && !ambushPreAimTarget.Actor.IsDead
				&& ambushPreAimTarget.Actor.CanBeViewedByPlayer(self.Owner))
				target = ambushPreAimTarget;

			if (target.Type == TargetType.Invalid)
			{
				ambushPreAimTarget = Target.Invalid;
				if (stage3)
					// SPRUNG is terminal until stance reset (design §5.2), so DO NOT clear ambushTriggered
					// here — only clear the tracking counters. This gives OBS-2 its clean deterministic
					// outcome: a sprung unit that a bot re-issues away and that later re-idles stays sprung
					// instead of re-arming and latch-churning. Un-sprung (DORMANT) units simply drop their
					// stale counters.
					ResetStage3Tracking();
				else
					ambushTriggered = false;

				return;
			}

			// Pre-aim: rotate turrets toward target WITHOUT firing (identical on both paths)
			ambushPreAimTarget = target;
			PreAimAtTarget(self, target);

			// Check if we've been spotted by the enemy — trigger 1 (detection), evaluated fresh every scan.
			var targetOwner = target.Type == TargetType.Actor ? target.Actor.Owner : target.FrozenActor.Owner;
			var isSpotted = self.CanBeViewedByPlayer(targetOwner);

			if (!stage3)
			{
				// ── Ungated STOCK path — byte-identical to pre-Stage-3 behaviour. ──
				if (isSpotted || ambushTriggered)
				{
					ambushTriggered = true;

					// Coordinate: trigger nearby allies in Ambush to also fire
					if (isSpotted)
						TriggerNearbyAmbushAllies(self, target);

					Attack(target, false, scanSource);
				}

				return;
			}

			// ── Gated Stage-3 path — stationary literal-ambush state machine (design §5.2). ──
			// DORMANT/TRACKING is expressed by holding fire while pre-aiming (above); SPRUNG is ambushTriggered.
			// Triggers 3/4/5 refresh from the kill-zone scan at AmbushScoreCadence; detection (1) is fresh each
			// scan; damage (2) is handled synchronously in INotifyDamage.Damaged (sets ambushTriggered).
			var trigger = Stage3EvaluateSpring(self, target, isSpotted);

			if (trigger != AmbushSpringTrigger.None || ambushTriggered)
			{
				ambushTriggered = true;

				// Spring the whole group simultaneously on ANY fresh trigger (initiation-on-signal doctrine,
				// §4.6) — not just on being spotted as the stock path does.
				if (trigger != AmbushSpringTrigger.None)
					TriggerNearbyAmbushAllies(self, target);

				Attack(target, false, scanSource);
			}
		}

		/// <summary>Is the Stage-3 widened-ambush gate granted on this unit right now? Same cheap
		/// short-circuit as the Stage-2 halt: empty condition name or zero grant ⇒ false, so the whole
		/// Stage-3 state machine is dead on any un-opted-in unit. NOTE (b8d2e601, 2026-08-02): it is NOT dead
		/// for @stable — LaneAmbushBotModule@stable grants the gate to the ambushers it posts.</summary>
		bool AmbushTacticsGranted(Actor self)
		{
			var gate = Info.AmbushTacticsCondition;
			return !string.IsNullOrEmpty(gate) && self.GetConditionCount(gate) > 0;
		}

		/// <summary>Gated Stage-3 spring decision. Refreshes the worthwhile score + trigger inputs from a
		/// kill-zone actor-scan at AmbushScoreCadence (heavy work only ~once/cadence), then evaluates the
		/// pure trigger table. Between refreshes the stored trigger flags are reused, so the per-idle-tick
		/// cost off-cadence is just the pure integer compares in <see cref="AmbushTactics.EvaluateSpring"/>.</summary>
		AmbushSpringTrigger Stage3EvaluateSpring(Actor self, in Target target, bool isSpotted)
		{
			var tick = self.World.WorldTick;
			if (ambushLastScoreTick == int.MinValue || tick - ambushLastScoreTick >= Info.AmbushScoreCadence)
			{
				RecomputeAmbushScore(self, target, tick);
				ambushLastScoreTick = tick;
			}

			return AmbushTactics.EvaluateSpring(
				detected: isSpotted,
				damaged: false,
				bestTargetPredictedExit: ambushBestPredictedExit,
				score: ambushLastScore,
				minSpringThreshold: Info.AmbushMinSpringThreshold,
				consecutiveDegradeSamples: ambushDegradeSamples,
				requiredDegradeSamples: Info.AmbushRequiredDegradeSamples,
				consecutiveHighSamples: ambushHighSamples,
				requiredHighSamples: Info.AmbushRequiredHighSamples,
				overrun: ambushOverrun);
		}

		/// <summary>The one heavy Stage-3 operation (design §5.2): a fog-filtered kill-zone actor-scan that
		/// sums the worthwhile score, tracks the best engageable target's range trend (trigger 3) and the
		/// nearest engageable range (trigger 5), and advances the degrade / saturation hysteresis counters.
		/// Determinism: the FindActorsInCircle result is ordered by ActorID before ANY min/best pick, so the
		/// order-sensitive picks (nearest, best-target identity) are stable; the score sum is
		/// order-independent regardless. Zero RNG.</summary>
		void RecomputeAmbushScore(Actor self, in Target target, int tick)
		{
			var interval = ambushLastScoreTick == int.MinValue ? 0 : tick - ambushLastScoreTick;

			var killRadius = WDist.FromCells(Info.AmbushKillZoneRadius);
			var maxRange = 0;
			foreach (var ab in ActiveAttackBases)
			{
				var r = ab.GetMaximumRange().Length;
				if (r > maxRange)
					maxRange = r;
			}

			// Overrun stand-off: the largest weapon MinRange, floored so a MinRange-0 weapon still detects
			// an enemy about to walk on top of it.
			var minRange = 0;
			foreach (var ab in ActiveAttackBases)
			{
				var r = ab.GetMinimumRange().Length;
				if (r > minRange)
					minRange = r;
			}

			var overrunThreshold = Math.Max(minRange, WDist.FromCells(Info.AmbushOverrunFloor).Length);

			var score = 0;
			var nearestEngageableRange = int.MaxValue;

			var contacts = self.World.FindActorsInCircle(self.CenterPosition, killRadius)
				.Where(a => a != self && a.IsInWorld && !a.IsDead
					&& a.AppearsHostileTo(self)
					&& a.CanBeViewedByPlayer(self.Owner))   // fog filter — believe only what I can legally see
				.OrderBy(a => a.ActorID);                   // ActorID order ⇒ deterministic min/best tie-breaks

			foreach (var a in contacts)
			{
				var range = (a.CenterPosition - self.CenterPosition).Length;
				score += AmbushTactics.ContactScore(
					AmbushThreatValue(a), AmbushCellValue(a), Info.AmbushThreatWeight, Info.AmbushValueWeight);

				// Engageable = I hold a weapon that can hit it and it is within that weapon's reach.
				var engageable = range <= maxRange
					&& ActiveAttackBases.Any(ab => ab.HasAnyValidWeapons(Target.FromActor(a)));

				if (engageable && range < nearestEngageableRange)
					nearestEngageableRange = range;
			}

			ambushLastScore = score;

			ambushHighSamples = AmbushTactics.UpdateSustainCounter(
				ambushHighSamples, score >= Info.AmbushHighSpringThreshold);

			// Best target = the priority-correct pick AutoTarget already chose (ambushPreAimTarget == target).
			// Track its range trend across samples, keyed on ActorID so a target swap resets the trend.
			var bestId = target.Type == TargetType.Actor ? target.Actor.ActorID : 0u;
			var bestRange = (target.CenterPosition - self.CenterPosition).Length;

			if (bestId != 0 && bestId == ambushBestTargetId)
			{
				ambushBestRadialPerTick = AmbushTactics.RadialSpeedPerTick(ambushBestPrevRange, bestRange, interval);
				var degrade = AmbushTactics.IsDegradeSample(bestRange, ambushBestPrevRange, Info.AmbushDegradeEpsilon.Length);
				ambushDegradeSamples = AmbushTactics.UpdateSustainCounter(ambushDegradeSamples, degrade);
			}
			else
			{
				// First sample on this target (or the best target changed) — no trend yet.
				ambushBestRadialPerTick = 0;
				ambushDegradeSamples = 0;
			}

			ambushBestPredictedExit = bestId != 0
				&& AmbushTactics.PredictedToExitRange(bestRange, ambushBestRadialPerTick, Info.AmbushExitPredictTicks, maxRange);

			ambushOverrun = nearestEngageableRange != int.MaxValue
				&& AmbushTactics.IsOverrun(nearestEngageableRange, overrunThreshold);

			ambushBestTargetId = bestId;
			ambushBestPrevRange = bestRange;
		}

		/// <summary>Per-contact THREAT term for the worthwhile score. Only armed contacts (those carrying an
		/// AttackBase) project threat; an unarmed supply truck reads 0 here — its worth comes from the VALUE
		/// term instead, which is exactly why the split beats a value-blind danger field (design §3.2). Shape
		/// mirrors DangerKernelMath's durability weight (base + HP + Cost).</summary>
		int AmbushThreatValue(Actor a)
		{
			if (!a.Info.HasTraitInfo<AttackBaseInfo>())
				return 0;

			var hp = a.TraitOrDefault<Health>()?.MaxHP ?? 0;
			var cost = a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
			return Info.AmbushThreatBase
				+ (Info.AmbushThreatHealthDivisor > 0 ? hp / Info.AmbushThreatHealthDivisor : 0)
				+ (Info.AmbushThreatCostDivisor > 0 ? cost / Info.AmbushThreatCostDivisor : 0);
		}

		/// <summary>Per-contact VALUE term for the worthwhile score — the contact's economic Cost. Every
		/// enemy contributes, armed or not, so a lane of undefended reinforcements still scores worthwhile.</summary>
		static int AmbushCellValue(Actor a)
		{
			return a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
		}

		/// <summary>Clear the Stage-3 range/score tracking counters WITHOUT touching ambushTriggered (SPRUNG
		/// is terminal until stance reset). Called when a gated ambusher loses its target.</summary>
		void ResetStage3Tracking()
		{
			ambushLastScore = 0;
			ambushLastScoreTick = int.MinValue;
			ambushBestTargetId = 0;
			ambushBestPrevRange = 0;
			ambushBestRadialPerTick = 0;
			ambushDegradeSamples = 0;
			ambushHighSamples = 0;
			ambushBestPredictedExit = false;
			ambushOverrun = false;
		}

		void PreAimAtTarget(Actor self, in Target target)
		{
			// Rotate turrets toward target silently (no firing)
			if (turretedTraits != null)
				foreach (var turret in turretedTraits)
					turret.FaceTarget(self, target);

			// For non-turreted units (infantry), face the body toward the target
			if (turretedTraits == null || turretedTraits.Length == 0)
			{
				var facing = self.TraitOrDefault<IFacing>();
				if (facing != null)
				{
					var delta = target.CenterPosition - self.CenterPosition;
					var desiredFacing = delta.Yaw;
					facing.Facing = Util.TickFacing(facing.Facing, desiredFacing, facing.TurnSpeed);
				}
			}
		}

		void TriggerNearbyAmbushAllies(Actor self, in Target target)
		{
			var coordRadius = WDist.FromCells(Info.AmbushCoordinationRadius);
			var nearbyAllies = self.World.FindActorsInCircle(self.CenterPosition, coordRadius)
				.Where(a => a != self && a.Owner == self.Owner && a.IsInWorld && !a.IsDead);

			foreach (var ally in nearbyAllies)
			{
				var allyAutoTarget = ally.TraitOrDefault<AutoTarget>();
				if (allyAutoTarget != null && allyAutoTarget.Stance == UnitStance.Ambush && !allyAutoTarget.ambushTriggered)
					allyAutoTarget.ambushTriggered = true;

				// Also trigger garrisoned buildings in Ambush stance
				var gm = ally.TraitOrDefault<GarrisonManager>();
				if (gm != null)
					gm.TriggerAmbush();
			}
		}

		/// <summary>Called externally when stance changes away from Ambush to reset state. This is the ONLY
		/// path that clears the terminal SPRUNG latch (ambushTriggered) — see AmbushTickIdle's gated
		/// no-target branch, which deliberately leaves it set.</summary>
		void ResetAmbushState()
		{
			ambushPreAimTarget = Target.Invalid;
			ambushTriggered = false;
			ResetStage3Tracking();
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
				return;

			if (nextScanTime > 0)
				--nextScanTime;

			TickPreemption(self);
		}

		/// <summary><para>Target preemption: switch off a low-priority target when a strictly higher-priority one
		/// becomes attackable (the SHORAD that keeps shooting infantry while a helicopter hovers in range).</para>
		///
		/// <para>The ordinary priority scan is IDLE-ONLY — ChooseTarget is reachable only via ScanForTarget from
		/// INotifyIdle.TickIdle, and Actor.IsIdle is CurrentActivity == null, so an engaged unit never rescans.
		/// Even a forced rescan returns the incumbent, because AttackFollow.TryGetAutoTargetOverride hands
		/// RequestedTarget straight back ahead of the scan. See WORKSPACE/DISCOVERIES.md (2026-08-11).</para>
		///
		/// <para>Determinism: cadence is WorldTick + ActorID, zero RNG, and ChooseTarget is called DIRECTLY rather
		/// than through ScanForTarget — the latter re-arms nextScanTime off SharedRandom, which would shift the
		/// shared RNG stream (breaking byte-identity, see DOCS/reference/influence-stack.md) and starve the
		/// existing scanners (that starvation mode is documented at AttackMoveActivity.cs:110-115).</para></summary>
		void TickPreemption(Actor self)
		{
			// This and the ScanForTarget yield are two PHASES of one cycle, not two disjoint states: a
			// stationary engagement interleaves them every few ticks. AttackFollow.AttackActivity.Tick
			// returns false — keep running — for a target that stays in range with clear LOS (:418-428),
			// which is the window this pass covers; when that activity ends the unit is briefly idle with
			// its target promoted to a persistent override, which is the window the yield covers. Neither
			// path is redundant and neither alone is sufficient.
			if (self.IsIdle || !PreemptionDue(self))
				return;

			// Only preempt an engagement the unit acquired by ITSELF: a player order, a Lua order or a
			// bot's deliberate AttackTarget must survive untouched.
			//
			// PITFALL: this reads the TOP-LEVEL activity, deliberately NOT
			// self.CurrentActivity.ActivitiesImplementing<IAttackActivity>(). That walk descends into
			// ChildActivity and along NextActivity, so an autotarget attack nested under a player Move
			// (SmartMoveActivity.cs:117) would match — and preempting via queued:false calls
			// Actor.CancelActivity on the WHOLE current activity, destroying the move order.
			if (self.CurrentActivity is not IAttackActivity current
				|| !IsAutoAcquiredSource(current.Source)
				|| current.ForceAttack)
				return;

			if (!TryFindHigherBandTarget(self, current.Target, out var betterTarget))
				return;

			// Match TickIdle's movement policy rather than hard-coding false, which would silently
			// downgrade a Hunt-stance pursuer to a stationary attack the moment it preempted.
			var allowMove = allowMovement && engagementStance >= EngagementStance.Hunt;

			foreach (var ab in ActiveAttackBases)
				ab.AttackTarget(betterTarget, current.Source, false, allowMove);
		}

		/// <summary>Sources that represent an engagement the unit acquired BY ITSELF, and may therefore
		/// have replaced by a higher-priority target. AttackMove counts: the player/bot ordered a move,
		/// not that particular target, and it is the dominant bot engagement mode. Default (player, Lua,
		/// deliberate bot AttackTarget) never counts.</summary>
		public static bool IsAutoAcquiredSource(AttackSource source)
		{
			return source == AttackSource.AutoTarget || source == AttackSource.AttackMove;
		}

		/// <summary>Cadence gate shared by both re-evaluation paths. Tick-derived and staggered by
		/// ActorID — zero RNG, so it cannot perturb the shared random stream.</summary>
		bool PreemptionDue(Actor self)
		{
			var interval = Info.PreemptScanInterval;
			if (interval <= 0 || stance < UnitStance.FireAtWill)
				return false;

			return (self.World.WorldTick + (int)(self.ActorID % (uint)interval)) % interval == 0;
		}

		/// <summary>Re-runs the ordinary priority scan and reports a target whose band is STRICTLY higher
		/// than the incumbent's. Strictness IS the hysteresis: the range, cluster and soft-overkill terms
		/// are bounded by construction to stay inside one priority bucket, so they cannot move this
		/// comparison and two comparable targets can never trade places. No cooldown is needed.
		/// allowMove:false so only something shootable from where the unit stands can win — that is what
		/// stops a SHORAD abandoning a tank to chase an unreachable helicopter.</summary>
		bool TryFindHigherBandTarget(Actor self, in Target incumbent, out Target betterTarget)
		{
			betterTarget = Target.Invalid;

			var incumbentBand = GetTargetPriorityBand(self, incumbent);
			var allowTurn = Info.AllowTurning && stance > UnitStance.HoldFire;

			foreach (var ab in ActiveAttackBases)
			{
				var attackStances = ab.UnforcedAttackTargetStances();
				if (attackStances == PlayerRelationship.None)
					continue;

				var range = Info.ScanRadius > 0 ? WDist.FromCells(Info.ScanRadius) : ab.GetMaximumRange();
				var candidate = ChooseTarget(self, ab, attackStances, range, false, allowTurn, out var candidateBand);
				if (candidate.Type == TargetType.Invalid || candidateBand <= incumbentBand)
					continue;

				betterTarget = candidate;
				return true;
			}

			return false;
		}

		public Target ScanForTarget(Actor self, bool allowMove, bool allowTurn, bool ignoreScanInterval = false)
		{
			return ScanForTarget(self, allowMove, allowTurn, ignoreScanInterval, out _);
		}

		/// <summary><paramref name="fromProtectedOverride"/> is true when the returned target came from an
		/// override that refused to yield — i.e. it belongs to a player, Lua or bot order. Callers that
		/// re-issue the result must NOT re-stamp such a target as <see cref="AttackSource.AutoTarget"/>.</summary>
		public Target ScanForTarget(Actor self, bool allowMove, bool allowTurn, bool ignoreScanInterval, out bool fromProtectedOverride)
		{
			fromProtectedOverride = false;

			if ((ignoreScanInterval || nextScanTime <= 0) && ActiveAttackBases.Any())
			{
				foreach (var oat in overrideAutoTarget)
					if (oat.TryGetAutoTargetOverride(self, out var existingTarget, out var canYield))
					{
						// The override is consulted BEFORE the scan, so whatever it returns is the
						// target — ChooseTarget never runs and the AutoTargetPriority table is never
						// consulted. For an automatic engagement that is the whole bug: a unit stays on
						// a band-3 tank while a band-5 helicopter sits in range. Re-evaluate, but keep
						// the incumbent unless something is STRICTLY higher-banded.
						//
						// Deliberately NOT re-arming nextScanTime on this path (the re-arm below is
						// still reached only by the ordinary scan), so the SharedRandom draw pattern is
						// exactly what it was before this branch existed.
						if (canYield && PreemptionDue(self)
							&& TryFindHigherBandTarget(self, existingTarget, out var betterTarget))
							return betterTarget;

						fromProtectedOverride = !canYield;
						return existingTarget;
					}

				// Reaching here means every IOverrideAutoTarget declined: the unit holds no commitment
				// at all, so the free ChooseTarget below may pick anything. That state is the signature
				// of the engagement having LAPSED — AttackFollow.cs:176 wiping opportunityTargetIsPersistentTarget
				// is the route into it. Preemption never passes through here: it hands over while the
				// incumbent is still held (TickPreemption bypasses this method entirely, and the yield
				// at the top of the override loop returns before this line).
				++UncommittedScanCount;

				if (!ignoreScanInterval)
					nextScanTime = self.World.SharedRandom.Next(Info.MinimumScanTimeInterval, Info.MaximumScanTimeInterval);

				foreach (var ab in ActiveAttackBases)
				{
					// If we can't attack right now, there's no need to try and find a target.
					var attackStances = ab.UnforcedAttackTargetStances();
					if (attackStances != PlayerRelationship.None)
					{
						var range = Info.ScanRadius > 0 ? WDist.FromCells(Info.ScanRadius) : ab.GetMaximumRange();
						return ChooseTarget(self, ab, attackStances, range, allowMove, allowTurn);
					}
				}
			}

			return Target.Invalid;
		}

		public void ScanAndAttack(Actor self, bool allowMove, bool allowTurn)
		{
			var target = ScanForTarget(self, allowMove, allowTurn, false, out var fromProtectedOverride);
			if (target.Type == TargetType.Invalid)
				return;

			// PITFALL: re-issuing unconditionally as AutoTarget LAUNDERS provenance. A player's target
			// survives its attack activity ending (promoted to a persistent opportunity target), comes
			// back through the override with canYield false, and would then be re-stamped AutoTarget
			// here — making the player's own order preemptable on the next pass. A refusal to yield is
			// exactly the signal that this target is not autotarget's to re-stamp.
			Attack(target, allowMove, fromProtectedOverride ? AttackSource.Default : AttackSource.AutoTarget);
		}

		/// <summary><paramref name="source"/> is deliberately REQUIRED, not defaulted. A default of
		/// AutoTarget fails open — it silently re-stamps whatever it is handed as an automatic
		/// engagement — and that affordance is why the provenance-laundering bug had two separate
		/// instances (ScanAndAttack and AmbushTickIdle). FOUR call sites across three methods —
		/// retaliation, both AmbushTickIdle springs, and ScanAndAttack — and each states its source.</summary>
		void Attack(in Target target, bool allowMove, AttackSource source)
		{
			foreach (var ab in ActiveAttackBases)
				ab.AttackTarget(target, source, false, allowMove);
		}

		public bool HasValidTargetPriority(Actor self, Player owner, BitSet<TargetableType> targetTypes)
		{
			return GetTargetPriorityBand(self, owner, targetTypes) > NoTargetPriorityBand;
		}

		/// <summary>Returned when no enabled AutoTargetPriority matches — lower than any real Priority,
		/// so an unmatched incumbent always loses the preemption comparison.</summary>
		public const int NoTargetPriorityBand = int.MinValue;

		/// <summary>The highest AutoTargetPriority band this unit assigns to the given target, or
		/// <see cref="NoTargetPriorityBand"/> if none matches. Same relationship / ValidTargets / InvalidTargets
		/// matching HasValidTargetPriority has always done; it just reports the winning Priority instead of a bool.
		/// Deliberately reads the RAW ati.Priority, exactly like ChooseTarget's chosenTargetPriority — the
		/// ConditionalPriority suppression promote is excluded from BOTH sides of the preemption comparison,
		/// so a decaying suppression condition can never make two targets trade places across a band edge.</summary>
		public int GetTargetPriorityBand(Actor self, Player owner, BitSet<TargetableType> targetTypes)
		{
			if (owner == null || Stance <= UnitStance.HoldFire)
				return NoTargetPriorityBand;

			var relationship = self.Owner.RelationshipWith(owner);
			var best = NoTargetPriorityBand;
			foreach (var atp in allTargetPriorities)
			{
				if (atp.IsTraitDisabled)
					continue;

				var band = MatchTargetPriorityBand(atp.Info, relationship, targetTypes);
				if (band > best)
					best = band;
			}

			return best;
		}

		/// <summary><para>The band one AutoTargetPriority entry assigns to a target, or
		/// <see cref="NoTargetPriorityBand"/> if it does not match. Pure, so it can be pinned by unit
		/// test — see AutoTargetPriorityBandTest.</para>
		///
		/// <para>PITFALL: this deliberately does NOT consult OnlyTargets. Until this branch the matcher led
		/// with `!ati.OnlyTargets.Except(targetTypes).Any()`, but OnlyTargets defaults to an EMPTY
		/// BitSet and nothing in mods/ ever sets it — and empty.Except(x) is empty, so that term was
		/// `!false` for every candidate and skipped EVERY priority entry unconditionally. The predicate
		/// must also stay identical to ChooseTarget's own per-target filter, or an incumbent and a
		/// candidate get matched by different rules and any band comparison between them is
		/// meaningless.</para></summary>
		public static int MatchTargetPriorityBand(AutoTargetPriorityInfo ati, PlayerRelationship relationship,
			BitSet<TargetableType> targetTypes)
		{
			return MatchesTargetPriority(ati, relationship, targetTypes) ? ati.Priority : NoTargetPriorityBand;
		}

		/// <summary>THE single per-entry match predicate. Both sides of the preemption band comparison run
		/// through here — the incumbent via MatchTargetPriorityBand and the candidate via ChooseTarget —
		/// because they must agree by construction, not by two hand-maintained copies happening to agree.
		/// A duplicated copy of this test is exactly what shipped the OnlyTargets bug described above.</summary>
		public static bool MatchesTargetPriority(AutoTargetPriorityInfo ati, PlayerRelationship relationship,
			BitSet<TargetableType> targetTypes)
		{
			// Incompatible relationship
			if (!ati.ValidRelationships.HasRelationship(relationship))
				return false;

			// Incompatible target types
			if (!ati.ValidTargets.Overlaps(targetTypes) || ati.InvalidTargets.Overlaps(targetTypes))
				return false;

			return true;
		}

		/// <summary>Highest band a whole priority set assigns to a target. Test seam for the above.</summary>
		public static int ResolveTargetPriorityBand(IEnumerable<AutoTargetPriorityInfo> priorities,
			PlayerRelationship relationship, BitSet<TargetableType> targetTypes)
		{
			var best = NoTargetPriorityBand;
			foreach (var ati in priorities)
			{
				var band = MatchTargetPriorityBand(ati, relationship, targetTypes);
				if (band > best)
					best = band;
			}

			return best;
		}

		/// <summary>Band of a Target, resolving actor vs frozen-actor owner/type lookup.</summary>
		int GetTargetPriorityBand(Actor self, in Target target)
		{
			if (target.Type == TargetType.Actor)
				return GetTargetPriorityBand(self, target.Actor.Owner, target.Actor.GetEnabledTargetTypes());

			if (target.Type == TargetType.FrozenActor)
				return GetTargetPriorityBand(self, target.FrozenActor.Owner, target.FrozenActor.TargetTypes);

			return NoTargetPriorityBand;
		}

		Target ChooseTarget(Actor self, AttackBase ab, PlayerRelationship attackStances, WDist scanRange, bool allowMove, bool allowTurn)
		{
			return ChooseTarget(self, ab, attackStances, scanRange, allowMove, allowTurn, out _);
		}

		/// <summary><paramref name="chosenBand"/> reports the winning target's raw AutoTargetPriority
		/// (<see cref="NoTargetPriorityBand"/> when nothing was chosen), for target preemption.</summary>
		Target ChooseTarget(Actor self, AttackBase ab, PlayerRelationship attackStances, WDist scanRange, bool allowMove, bool allowTurn, out int chosenBand)
		{
			var chosenTarget = Target.Invalid;
			chosenBand = NoTargetPriorityBand;

			if (stance <= UnitStance.HoldFire)
				return chosenTarget;

			reusableActivePriorities.Clear();
			foreach (var atp in allTargetPriorities)
				if (!atp.IsTraitDisabled)
					reusableActivePriorities.Add(atp.Info);

			if (reusableActivePriorities.Count == 0)
				return chosenTarget;

			var targetsInRange = self.World.FindActorsInCircle(self.CenterPosition, scanRange)
				.Select(Target.FromActor);

			// Player.FrozenActorLayer is TraitOrDefault and can be null (e.g. for the Neutral
			// player or any player whose YAML omits the FrozenActorLayer trait). Network/Order.cs
			// and SupportPowerBotModule.cs explicitly null-guard the same access; auto-target
			// must do the same or it NREs every tick a weaponless trait host (e.g. AutoTarget on
			// a neutral TRUK from a captured supply truck) scans for targets.
			if ((allowMove || ab.Info.TargetFrozenActors) && self.Owner.FrozenActorLayer != null)
				targetsInRange = targetsInRange
					.Concat(self.Owner.FrozenActorLayer.FrozenActorsInCircle(self.World, self.CenterPosition, scanRange)
					.Select(Target.FromFrozenActor));

			// AoE-aware cluster targeting (PIPELINE item 14). Active only while ClusterTargetingCondition is
			// granted (default off ⇒ every branch below is skipped and scoring is byte-identical) AND the unit
			// actually wields an area weapon. When on, we snapshot the hostile-actor positions ONCE (a single
			// bounded circle over the set AutoTarget already scans — no map-wide sweep) so the per-candidate
			// clump score is a cheap sum. Deterministic: the sum is order-independent.
			int[] clusterFalloff = null;
			var clusterRadius = Info.ClusterRadius.Length;
			List<(Actor Actor, WPos Pos)> clusterField = null;
			var clusterActive = !string.IsNullOrEmpty(Info.ClusterTargetingCondition)
				&& self.GetConditionCount(Info.ClusterTargetingCondition) > 0
				&& TryGetClusterFalloff(self, out clusterFalloff);
			if (clusterActive)
			{
				clusterField = new List<(Actor, WPos)>();
				foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, scanRange))
				{
					if (a == self || !a.AppearsHostileTo(self) || !a.CanBeViewedByPlayer(self.Owner))
						continue;

					clusterField.Add((a, a.CenterPosition));
				}
			}

			var declinedShootableTarget = false;
			var chosenTargetRange = 0;
			var chosenTargetAverageDamagePercent = 0;
			var chosenTargetSuppression = 0;
			var chosenTargetValue = long.MaxValue;

			foreach (var target in targetsInRange)
			{
				BitSet<TargetableType> targetTypes;
				Player owner;
				if (target.Type == TargetType.Actor)
				{
					// PERF: Most units can only attack enemy units. If this is the case but the target is not an enemy, we
					// can bail early and avoid the more expensive targeting checks and armament selection. For groups of
					// allied units, this helps significantly reduce the cost of auto target scans. This is important as
					// these groups will continuously rescan their allies until an enemy finally comes into range.
					if (attackStances == PlayerRelationship.Enemy && !target.Actor.AppearsHostileTo(self))
						continue;

					// Check whether we can auto-target this actor
					targetTypes = target.Actor.GetEnabledTargetTypes();

					if (PreventsAutoTarget(self, target.Actor) || !target.Actor.CanBeViewedByPlayer(self.Owner))
						continue;

					owner = target.Actor.Owner;
				}
				else if (target.Type == TargetType.FrozenActor)
				{
					if (attackStances == PlayerRelationship.Enemy && self.Owner.RelationshipWith(target.FrozenActor.Owner) == PlayerRelationship.Ally)
						continue;

					targetTypes = target.FrozenActor.TargetTypes;
					owner = target.FrozenActor.Owner;
				}
				else
					continue;

				// Shared with the incumbent's band lookup — see MatchesTargetPriority. The relationship is
				// hoisted out of the loop: it is a pure function of two owners, neither of which changes
				// across iterations, so this is the same value computed once instead of once per entry.
				var targetRelationship = self.Owner.RelationshipWith(owner);

				reusableValidPriorities.Clear();
				foreach (var ati in reusableActivePriorities)
					if (MatchesTargetPriority(ati, targetRelationship, targetTypes))
						reusableValidPriorities.Add(ati);

				if (reusableValidPriorities.Count == 0)
					continue;

				// Make sure that we can actually fire on the actor
				var armaments = ab.ChooseArmamentsForTarget(target, false);
				if (!allowMove)
					armaments = armaments.Where(arm =>
						target.IsInRange(self.CenterPosition, arm.MaxRange()) &&
						!target.IsInRange(self.CenterPosition, arm.Weapon.MinRange));

				if (!armaments.Any())
					continue;

				if (!allowTurn && !ab.TargetInFiringArc(self, target, ab.Info.FacingTolerance))
					continue;

				if (target.Type != TargetType.Invalid)
				{
					// Per-unit LOS check using pre-cached ShadowLayer data.
					// Each unit must have clear enough LOS from its own cell.
					byte bestThreshold = 0;
					foreach (var arm in armaments)
						if (arm.Weapon.ClearSightThreshold > bestThreshold)
							bestThreshold = arm.Weapon.ClearSightThreshold;

					if (!FiringLOS.HasClearLOS(self, target, bestThreshold))
						continue;
				}

				if (target.Actor == null)
					continue;

				// Don't overkill — skip targets that already have MORE incoming damage than it takes
				// to destroy them.
				//
				// PITFALL: strictly greater, not >=. EstimatePercentDamage clamps ONE shooter's claim
				// at 100 (see the cap below) and OverkillThreshold defaults to 100, so under >= the
				// smallest possible unit of commitment was exactly sufficient to trip this skip: an AA
				// soldier's first missile marked a Halo at precisely 100 and blinded every other AA to
				// a full-health aircraft until the next 60-tick halving. The threshold could only be
				// tripped, never approached. Measured 2026-08-20 (run 260820_033930_p76804): four AA
				// against one helicopter took 173 ticks to all engage, against 7 for the same battery
				// with this skip disabled — one decay period per joiner.
				if (Info.OverkillThreshold >= 0 && target.Actor.AverageDamagePercent > Info.OverkillThreshold)
				{
					declinedShootableTarget = true;
					continue;
				}

				// Skip targets that are already "good as dead" — critical damage in WW3MOD means
				// the unit can't fight and will bleed out to 0. Force-attacks bypass this filter
				// because they go through AttackBase.AttackTarget without consulting ChooseTarget.
				if (!string.IsNullOrEmpty(Info.BreakOffCondition)
					&& target.Actor.GetConditionCount(Info.BreakOffCondition) > 0)
				{
					declinedShootableTarget = true;
					continue;
				}

				var targetRange = (target.CenterPosition - self.CenterPosition).Length;

				// Cluster pull (PIPELINE item 14): a distance-like BONUS subtracted from priorityValue so an
				// area weapon prefers the target ringed by the most enemies. Computed once per candidate (same
				// for every priority class), capped at ClusterMaxBonus so it stays WITHIN the range tiebreak and
				// never crosses a priority bucket — exactly like the soft-overkill penalty above it. 0 (no pull)
				// when cluster targeting is off, the candidate is a frozen actor, or it has no live neighbours.
				var clusterBonus = 0;
				if (clusterActive && target.Type == TargetType.Actor)
				{
					var aim = target.CenterPosition;
					var clusterScore = 0;
					for (var i = 0; i < clusterField.Count; i++)
					{
						// Exclude the aim unit itself; every OTHER hostile contributes its splash weight.
						if (clusterField[i].Actor == target.Actor)
							continue;

						clusterScore += FiresEconMath.ClusterWeight(
							(clusterField[i].Pos - aim).HorizontalLength, clusterRadius, clusterFalloff);
					}

					clusterBonus = FiresEconMath.ClusterPriorityBonus(
						clusterScore, Info.ClusterBonusScale, Info.ClusterMaxBonus.Length);
				}

				// Healthy-target preference: a wounded enemy scores as if it stood further away. Computed
				// ONCE per candidate (health and range are both per-candidate, not per-priority-class) and
				// read from the Actor's cached IHealth, so this costs no trait lookup on the scan path.
				var healthPenalty = FiresEconMath.HealthPreferencePenalty(
					targetRange, target.Actor.HealthPercent, Info.HealthPreferenceScale);

				// PITFALL: priority MUST be categorical — a tank should always shoot a tank
				// before a crewman, regardless of how close the crewman is. The pre-260511
				// formula was `range / Priority`, which made an Infantry-priority target at
				// ~40% the range of a Heavy-priority target win. That made tanks pivot to
				// freshly-ejected crew next to them and lose the duel against the surviving
				// enemy MBT. See balance autotest test-balance-tank-mass for the original
				// repro.
				//
				// New encoding (lower priorityValue = better):
				//   - Effective Priority bucket dominates: subtract Priority * BucketSize.
				//   - Within a bucket, range tiebreaks (closer wins).
				//   - SoftOverkill (incoming damage %) sits well within one bucket so it
				//     never crosses priority classes.
				//   - ConditionalPriority is now interpreted as "promote this priority by 1
				//     when the condition is granted" (e.g. suppressed infantry get a Sniper's
				//     elevated bucket). Only fires when both ConditionalPriority>0 AND the
				//     ExternalCondition is actually granted (>0) on the target.
				//   - The old CriticalDamage +50000 nudge was removed in 2026-05; critically
				//     damaged targets are now hard-skipped above via BreakOffCondition.
					//   - HealthPreference (the target's REMAINING HP) sits inside the range tiebreak
					//     too, so a healthy enemy outranks a wounded one at equal range. It is a
					//     preference and not a second break-off: bounded by one range-length, so the
					//     lone wounded target in range still wins its bucket and gets finished off.
				const long PriorityBucketSize = 1L << 24;  // 16 777 216 — far above any plausible map range
				long priorityValue;

				// Evaluate whether we want to target this actor
				foreach (var ati in reusableValidPriorities)
				{
					priorityValue = 0;

					var priorityCondition = target.Actor?.TraitsImplementing<ExternalCondition>()
						.FirstOrDefault(t => t.Info.Condition == ati.PriorityCondition)?.GrantedValue(target.Actor);

					// Shorter range has higher priority (within a bucket)
					priorityValue += targetRange;

					// Cluster pull: a clumped target scores as if it were nearer (bounded, bucket-safe).
					priorityValue -= clusterBonus;

					// Deprioritize targets with significant incoming damage (soft penalty before hard skip).
					// Knobs surface as SoftOverkillThreshold / SoftOverkillScale on AutoTargetInfo.
					if (Info.SoftOverkillScale > 0 && target.Actor.AverageDamagePercent > Info.SoftOverkillThreshold)
						priorityValue += targetRange * target.Actor.AverageDamagePercent / Info.SoftOverkillScale;

					// Prefer a healthy enemy over a wounded one. Distinct from the soft-overkill term above:
					// that reads AverageDamagePercent (damage OTHER shooters have already claimed, i.e. don't
					// pile on), this reads the target's actual remaining HP. Before this term a 30%-HP tank
					// and a full-health one at equal range scored identically.
					priorityValue += healthPenalty;

					// Categorical bucket: highest Priority always wins. ConditionalPriority
					// promotes the bucket by 1 when its named condition is actually granted.
					var effectivePriority = ati.Priority;
					if (ati.ConditionalPriority > 0 && (priorityCondition ?? 0) > 0)
						effectivePriority += 1;

					priorityValue -= (long)effectivePriority * PriorityBucketSize;

					// Lower value = higher priority. Skip if we already have a strictly better one.
					if (priorityValue >= chosenTargetValue && chosenTarget.Type != TargetType.Invalid)
						continue;

					chosenTarget = target;
					chosenTargetValue = priorityValue;
					chosenBand = ati.Priority;
					chosenTargetRange = targetRange;
					chosenTargetSuppression = priorityCondition ?? 0;
					chosenTargetAverageDamagePercent = target.Actor.AverageDamagePercent;
				}
			}

			// Marking is intentionally not done here — AttackBase.AttackTarget marks once
			// the order is actually issued, so force-attacks / Lua / AI direct attacks all
			// contribute to AverageDamagePercent. ChooseTarget used to mark inline, but that
			// double-counted (mark, then AttackTarget would re-evaluate) and missed every
			// non-autotarget code path. See MarkTargetForAttack below.

			if (declinedShootableTarget && chosenTarget.Type == TargetType.Invalid)
				LastHeldFireTick = self.World.WorldTick;

			return chosenTarget;
		}

		/// <summary>The falloff curve of this unit's widest-splash area weapon, used to weight cluster neighbours
		/// (PIPELINE item 14). Returns false — cluster targeting stays off — when no enabled weapon has a
		/// SpreadDamage warhead at or above ClusterMinWarheadSpread (rifles / tank rounds are not area weapons).
		/// The falloff SHAPE is weapon-derived; the search RADIUS is the tunable Info.ClusterRadius.</summary>
		bool TryGetClusterFalloff(Actor self, out int[] falloff)
		{
			falloff = null;
			var bestSpread = 0;
			var minSpread = Info.ClusterMinWarheadSpread.Length;
			foreach (var arm in self.TraitsImplementing<Armament>())
			{
				if (arm.IsTraitDisabled || arm.Weapon == null)
					continue;

				foreach (var wh in arm.Weapon.Warheads.OfType<SpreadDamageWarhead>())
				{
					var spread = wh.Spread.Length;
					if (spread > bestSpread && wh.Falloff != null && wh.Falloff.Length > 0)
					{
						bestSpread = spread;
						falloff = wh.Falloff;
					}
				}
			}

			return bestSpread >= minSpread && falloff != null;
		}

		/// <summary>Estimate of one full burst's damage as a % of the target's max HP,
		/// capped at 100 — one attacker can only claim one kill's worth, however far its
		/// burst overshoots. Uses the warhead's Versus table, penetration vs front-armor
		/// thickness, and the warhead's Damage value. Front armor only — directional is
		/// overkill for an intent estimate. Returns 0 if the target has no Health/Armor or
		/// no matching armament.</summary>
		public static int EstimatePercentDamage(Actor attacker, in Target target)
		{
			if (target.Actor == null)
				return 0;

			var ab = attacker.TraitOrDefault<AttackBase>();
			if (ab == null)
				return 0;

			var health = target.Actor.TraitOrDefault<Health>();
			if (health == null || health.MaxHP <= 0)
				return 0;

			var armor = target.Actor.TraitOrDefault<Armor>();
			var thickness = 0;
			string armorType = null;
			if (armor != null)
			{
				thickness = armor.Info.Thickness;
				armorType = armor.Info.Type;
				if (armor.Info.Distribution != null && armor.Info.Distribution.Length > 0)
					thickness = thickness * armor.Info.Distribution[0] / 100;
			}

			var totalDamage = 0;
			foreach (var arm in ab.ChooseArmamentsForTarget(target, false))
			{
				foreach (var warhead in arm.Weapon.Warheads.OfType<Warheads.DamageWarhead>())
				{
					var damage = warhead.Damage;
					if (damage <= 0)
						continue;

					// A warhead only lands on what its own ValidTargets/InvalidTargets admit —
					// DamageWarhead.DoImpact bails on exactly this check before applying anything.
					// Counting a warhead that cannot hit inflates the claim by damage that will
					// never arrive, which is the same defect as the missing cap below.
					if (!warhead.IsValidAgainst(target.Actor, attacker))
						continue;

					// Penetration vs thickness — capped at 1.0, same shape as the live
					// damage path in DamageWarhead.InflictDamage.
					if (thickness > 0)
					{
						var penetration = warhead.Penetration;
						if (penetration < thickness)
							damage = damage * penetration / thickness;
					}

					// Versus table: e.g. an AT round has Versus.Heavy=200, an HE round
					// targeting infantry has Versus.Heavy=20.
					if (warhead.Versus.Count > 0 && armorType != null
						&& warhead.Versus.TryGetValue(armorType, out var vs))
						damage = damage * vs / 100;

					totalDamage += damage;
				}
			}

			// PITFALL: cap PER SHOOTER, never on the shared accumulator. A MANPAD's 3000-damage
			// missile against a 600-HP Halo used to claim 500% — five kills for one missile —
			// so one AA committing took three 60-tick halvings to decay back under
			// OverkillThreshold and blinded every other AA to a healthy aircraft for ~172 ticks.
			// Capping the target's total instead would be wrong: ordinary firing does not
			// re-apply the tally (measured 2026-08-10, a lead shooter fired eight times at a
			// 200-tick BurstWait and its neighbours still joined on schedule), so the runaway
			// needs a feeder that does not arise naturally. What does arise naturally is a
			// single claim larger than any damage that can land.
			return Math.Min(totalDamage * 100 / health.MaxHP, 100);
		}

		/// <summary>
		/// <para>Register intent to attack — reserves a share of the target's AverageDamagePercent
		/// so other units' autotarget scans see this target as partially-committed.
		/// Called from every committed attack path: autotarget pick, force-attack,
		/// Lua Actor.Attack, AI direct AttackTarget, opportunity-fire pick.</para>
		///
		/// <para>The reservation is held by the ATTACKER (Actor.ClaimForAttack), not merged anonymously into the
		/// target's tally, so that it can be handed back the moment the shot resolves. Marking used to be a
		/// bare += with no owner and therefore no way back: every commitment pushed the tally up and only the
		/// 60-tick halving in Actor.Tick ever pulled it down, so a target under sustained attention read as
		/// permanently over-committed and ChooseTarget declined it.</para>
		/// </summary>
		public static void MarkTargetForAttack(Actor attacker, in Target target)
		{
			if (target.Actor == null || target.Actor.IsDead)
				return;

			var percentDamage = EstimatePercentDamage(attacker, target);
			if (percentDamage > 0)
				attacker.ClaimForAttack(target.Actor, percentDamage);
		}

		static bool PreventsAutoTarget(Actor attacker, Actor target)
		{
			foreach (var deat in target.TraitsImplementing<IDisableEnemyAutoTarget>())
				if (deat.DisableEnemyAutoTarget(target, attacker))
					return true;

			return false;
		}
	}

	public class StanceInit : ValueActorInit<UnitStance>, ISingleInstanceInit
	{
		public StanceInit(TraitInfo info, UnitStance value)
			: base(info, value) { }
	}

	public class EngagementStanceInit : ValueActorInit<EngagementStance>, ISingleInstanceInit
	{
		public EngagementStanceInit(TraitInfo info, EngagementStance value)
			: base(info, value) { }
	}
}
