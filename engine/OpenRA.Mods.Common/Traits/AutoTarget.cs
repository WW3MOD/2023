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
			"token (e.g. enable-ambush-tactics) that a human opt-in / @experimental bot / test map grants;",
			"@stable and control bots never grant it, so they stay byte-identical.")]
		public readonly string AmbushTacticsCondition = null;

		// ── Stage 3 (PIPELINE item 8): stationary literal-ambush state machine. ALL of the following are
		// read ONLY on the gated path (AmbushTacticsCondition granted AND stance == Ambush). They never
		// touch the ungated path, so their values are irrelevant to @stable / control bots — those cohorts
		// short-circuit before any of this is read and stay byte-identical. Defaults are tuned for a
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

		[Desc("If a target has this condition, autotarget treats it as already-finished and skips it.",
			"In-progress autotarget/opportunity attacks also break off when the current target acquires this condition.",
			"Only force-attacks (Ctrl+click, Lua Actor.Attack(..., forceAttack=true), AI direct AttackTarget with forceAttack=true) still fire on these targets.",
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

		public UnitStance Stance => stance;

		public EngagementStance EngagementStanceValue => engagementStance;

		public CohesionMode CohesionValue => cohesion;

		public ResupplyBehavior ResupplyBehaviorValue => resupplyBehavior;

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
		// at their defaults (all 0 / int.MinValue) forever for @stable / control bots.
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
			// Apply per-type defaults from UnitDefaultsManager (player-set overrides that persist across games)
			if (self.Owner.Playable && !self.Owner.IsBot)
			{
				var mgr = self.World.WorldActor.TraitOrDefault<UnitDefaultsManager>();
				var defaults = mgr?.GetDefaults(self.Info.Name);
				if (defaults != null)
				{
					if (defaults.FireStance.HasValue)
					{
						stance = defaults.FireStance.Value;
						PredictedStance = stance;
					}

					if (defaults.Engagement.HasValue)
					{
						engagementStance = defaults.Engagement.Value;
						PredictedEngagementStance = engagementStance;
					}

					if (defaults.Cohesion.HasValue)
					{
						cohesion = defaults.Cohesion.Value;
						PredictedCohesion = cohesion;
					}

					if (defaults.Resupply.HasValue)
					{
						resupplyBehavior = defaults.Resupply.Value;
						PredictedResupplyBehavior = resupplyBehavior;
					}
				}
			}

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
				if (oat.TryGetAutoTargetOverride(self, out _))
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

			Attack(Target.FromActor(Aggressor), allowMove);
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
			// not granted (the default for @stable / control bots and every un-opted-in unit) NOTHING below
			// touches the Stage-3 state, and the else-branch is character-for-character the stock ambush idle
			// behaviour — that is the byte-identity guarantee.
			var stage3 = AmbushTacticsGranted(self);

			// Scan at full range — ambush doesn't reduce scan radius. PITFALL: ScanForTarget returns Invalid
			// BOTH when a scan ran and found nothing AND when the scan interval simply hasn't elapsed — and a
			// scan that does run re-arms nextScanTime, so whether THIS tick actually scanned must be captured
			// BEFORE the call.
			var scannedThisTick = nextScanTime <= 0;
			var target = ScanForTarget(self, false, true);

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

					Attack(target, false);
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

				Attack(target, false);
			}
		}

		/// <summary>Is the Stage-3 widened-ambush gate granted on this unit right now? Same cheap
		/// short-circuit as the Stage-2 halt: empty condition name or zero grant ⇒ false, so the whole
		/// Stage-3 state machine is dead for @stable / control bots and any un-opted-in unit.</summary>
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
		}

		public Target ScanForTarget(Actor self, bool allowMove, bool allowTurn, bool ignoreScanInterval = false)
		{
			if ((ignoreScanInterval || nextScanTime <= 0) && ActiveAttackBases.Any())
			{
				foreach (var oat in overrideAutoTarget)
					if (oat.TryGetAutoTargetOverride(self, out var existingTarget))
						return existingTarget;

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
			var target = ScanForTarget(self, allowMove, allowTurn);
			if (target.Type != TargetType.Invalid)
				Attack(target, allowMove);
		}

		void Attack(in Target target, bool allowMove)
		{
			foreach (var ab in ActiveAttackBases)
				ab.AttackTarget(target, AttackSource.AutoTarget, false, allowMove);
		}

		public bool HasValidTargetPriority(Actor self, Player owner, BitSet<TargetableType> targetTypes)
		{
			if (owner == null || Stance <= UnitStance.HoldFire)
				return false;

			foreach (var atp in allTargetPriorities)
			{
				if (atp.IsTraitDisabled)
					continue;

				var ati = atp.Info;

				// Incompatible relationship
				if (!ati.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(owner)))
					continue;

				// Incompatible target types
				if (!ati.OnlyTargets.Except(targetTypes).Any() || !ati.ValidTargets.Overlaps(targetTypes) || ati.InvalidTargets.Overlaps(targetTypes))
					continue;

				return true;
			}

			return false;
		}

		Target ChooseTarget(Actor self, AttackBase ab, PlayerRelationship attackStances, WDist scanRange, bool allowMove, bool allowTurn)
		{
			var chosenTarget = Target.Invalid;

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

			var chosenTargetPriority = 0;
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

				reusableValidPriorities.Clear();
				foreach (var ati in reusableActivePriorities)
				{
					// Incompatible relationship
					if (!ati.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(owner)))
						continue;

					// Incompatible target types
					if (!ati.ValidTargets.Overlaps(targetTypes) || ati.InvalidTargets.Overlaps(targetTypes))
						continue;

					reusableValidPriorities.Add(ati);
				}

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

				// Don't overkill — skip targets that already have enough incoming damage to destroy them
				if (Info.OverkillThreshold >= 0 && target.Actor.AverageDamagePercent >= Info.OverkillThreshold)
					continue;

				// Skip targets that are already "good as dead" — critical damage in WW3MOD means
				// the unit can't fight and will bleed out to 0. Force-attacks bypass this filter
				// because they go through AttackBase.AttackTarget without consulting ChooseTarget.
				if (!string.IsNullOrEmpty(Info.BreakOffCondition)
					&& target.Actor.GetConditionCount(Info.BreakOffCondition) > 0)
					continue;

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
					chosenTargetPriority = ati.Priority;
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

		/// <summary>Estimate of one full burst's damage as a % of the target's max HP.
		/// Uses the warhead's Versus table, penetration vs front-armor thickness, and
		/// the warhead's Damage value. Front armor only — directional is overkill for
		/// an intent estimate. Returns 0 if the target has no Health/Armor or no
		/// matching armament.</summary>
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

			return totalDamage * 100 / health.MaxHP;
		}

		/// <summary>Register intent to attack — bumps the target's AverageDamagePercent
		/// so other units' autotarget scans see this target as partially-committed.
		/// Called from every committed attack path: autotarget pick, force-attack,
		/// Lua Actor.Attack, AI direct AttackTarget, opportunity-fire pick.</summary>
		public static void MarkTargetForAttack(Actor attacker, in Target target)
		{
			if (target.Actor == null || target.Actor.IsDead)
				return;

			var percentDamage = EstimatePercentDamage(attacker, target);
			if (percentDamage > 0)
				target.Actor.MarkForDestruction(percentDamage);
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
