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

	[Desc("The actor will automatically engage the enemy when it is in range.")]
	public class AutoTargetInfo : ConditionalTraitInfo, Requires<AttackBaseInfo>, IEditorActorOptions
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
		public readonly IEnumerable<AttackBase> ActiveAttackBases;

		readonly bool allowMovement;

		[Sync]
		int nextScanTime = 0;

		public UnitStance Stance => stance;

		public EngagementStance EngagementStanceValue => engagementStance;

		public CohesionMode CohesionValue => cohesion;

		public ResupplyBehavior ResupplyBehaviorValue => resupplyBehavior;

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

		UnitStance stance;
		EngagementStance engagementStance;
		CohesionMode cohesion;
		ResupplyBehavior resupplyBehavior;
		IOverrideAutoTarget[] overrideAutoTarget;
		INotifyStanceChanged[] notifyStanceChanged;
		INotifyEngagementStanceChanged[] notifyEngagementStanceChanged;
		IEnumerable<AutoTargetPriorityInfo> activeTargetPriorities;
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
			ActiveAttackBases = self.TraitsImplementing<AttackBase>().ToArray().Where(t => !t.IsTraitDisabled);

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

			// AutoTargetPriority and their Priorities are fixed - so we can safely cache them with ToArray.
			// IsTraitEnabled can change over time, and so must appear after the ToArray so it gets re-evaluated each time.
			activeTargetPriorities =
				self.TraitsImplementing<AutoTargetPriority>()
					.OrderByDescending(ati => ati.Info.Priority).ToArray()
					.Where(t => !t.IsTraitDisabled).Select(atp => atp.Info);

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
			// Scan at full range — ambush doesn't reduce scan radius
			var target = ScanForTarget(self, false, true);

			if (target.Type == TargetType.Invalid)
			{
				ambushPreAimTarget = Target.Invalid;
				ambushTriggered = false;
				return;
			}

			// Pre-aim: rotate turrets toward target WITHOUT firing
			ambushPreAimTarget = target;
			PreAimAtTarget(self, target);

			// Check if we've been spotted by the enemy — if so, open fire
			var targetOwner = target.Type == TargetType.Actor ? target.Actor.Owner : target.FrozenActor.Owner;
			var isSpotted = self.CanBeViewedByPlayer(targetOwner);

			if (isSpotted || ambushTriggered)
			{
				ambushTriggered = true;

				// Coordinate: trigger nearby allies in Ambush to also fire
				if (isSpotted)
					TriggerNearbyAmbushAllies(self, target);

				Attack(target, false);
			}
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
			}
		}

		/// <summary>Called externally when stance changes away from Ambush to reset state.</summary>
		void ResetAmbushState()
		{
			ambushPreAimTarget = Target.Invalid;
			ambushTriggered = false;
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

			return activeTargetPriorities.Any(ati =>
			{
				// Incompatible relationship
				if (!ati.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(owner)))
					return false;

				// Incompatible target types
				if (!ati.OnlyTargets.Except(targetTypes).Any() || !ati.ValidTargets.Overlaps(targetTypes) || ati.InvalidTargets.Overlaps(targetTypes))
					return false;

				return true;
			});
		}

		Target ChooseTarget(Actor self, AttackBase ab, PlayerRelationship attackStances, WDist scanRange, bool allowMove, bool allowTurn)
		{
			var chosenTarget = Target.Invalid;

			if (stance <= UnitStance.HoldFire)
				return chosenTarget;

			var activePriorities = activeTargetPriorities.ToList();
			if (activePriorities.Count == 0)
				return chosenTarget;

			var targetsInRange = self.World.FindActorsInCircle(self.CenterPosition, scanRange)
				.Select(Target.FromActor);

			if (allowMove || ab.Info.TargetFrozenActors)
				targetsInRange = targetsInRange
					.Concat(self.Owner.FrozenActorLayer.FrozenActorsInCircle(self.World, self.CenterPosition, scanRange)
					.Select(Target.FromFrozenActor));

			var chosenTargetPriority = 0;
			var chosenTargetRange = 0;
			var chosenTargetAverageDamagePercent = 0;
			var chosenTargetSuppression = 0;
			var chosenTargetValue = int.MaxValue;

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

				var validPriorities = activePriorities.Where(ati =>
				{
					// // Already have a higher priority target
					// if (ati.Priority < chosenTargetPriority)
					// 	return false;

					// Incompatible relationship
					if (!ati.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(owner)))
						return false;

					// Incompatible target types
					if (!ati.ValidTargets.Overlaps(targetTypes) || ati.InvalidTargets.Overlaps(targetTypes))
						return false;

					return true;
				}).ToList();

				if (validPriorities.Count == 0)
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
					if (self.TraitOrDefault<IndirectFire>() == null)
						if (BlocksProjectiles.AnyBlockingActorsBetween(self, target.CenterPosition, new WDist(1), out var blockedPos, true, true))
						{
							// Already checked in TargetInFiringArc, might be unneccesary/inefficient
							continue;
						}
				}

				if (target.Actor == null)
					continue;

				// Don't overkill — skip targets that already have enough incoming damage to destroy them
				if (Info.OverkillThreshold >= 0 && target.Actor.AverageDamagePercent >= Info.OverkillThreshold)
					continue;

				var targetRange = (target.CenterPosition - self.CenterPosition).Length;

				var priorityValue = 0;

				// Evaluate whether we want to target this actor
				foreach (var ati in validPriorities)
				{
					priorityValue = 0;

					if (target.Actor.GetEnabledTargetTypes().Any(t => t == "CriticalDamage"))
						priorityValue += 50000;

					var priorityCondition = target.Actor?.TraitsImplementing<ExternalCondition>()
						.FirstOrDefault(t => t.Info.Condition == ati.PriorityCondition)?.GrantedValue(target.Actor);

					// Shorter range has higher priority
					priorityValue += targetRange;

					// Deprioritize targets with significant incoming damage (soft penalty before hard skip)
					if (target.Actor.AverageDamagePercent > 50)
						priorityValue += targetRange * target.Actor.AverageDamagePercent / 50;

					// Optionally: Prioritize targets with the priorityCondition
					if (ati.ConditionalPriority > 0)
						priorityValue /= ati.ConditionalPriority * ((priorityCondition ?? 0) + 1);

					// Divide by the original Priority value, lower Priority is more prioritized
					priorityValue /= ati.Priority;

					// Reversed from original OpenRA, lower value is higher priority. If we have no chosen target this is the first and should be added directly.
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

			if (chosenTarget.Actor != null)
			{
				var arms = ab.ChooseArmamentsForTarget(chosenTarget, false);

				var percentDamage = 0;
				foreach (var arm in arms)
				{
					var damageWarheads = arm.Weapon.Warheads.OfType<Warheads.DamageWarhead>();
					foreach (var warhead in damageWarheads)
					{
						var vsArmor = (float)warhead.Penetration / chosenTarget.Actor.Trait<Armor>().Info.Thickness;
						if (vsArmor > 1)
							vsArmor = 1;

						var targetHealth = chosenTarget.Actor.TraitOrDefault<Health>()?.HP;

						percentDamage += (int)(vsArmor * warhead.Damage / targetHealth * 100);
					}
				}

				chosenTarget.Actor.MarkForDestruction(percentDamage);
			}

			return chosenTarget;
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
