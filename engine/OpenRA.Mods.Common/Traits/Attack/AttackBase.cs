using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum AttackSource { Default, AutoTarget, AttackMove }

	public abstract class AttackBaseInfo : PausableConditionalTraitInfo
	{
		[Desc("Armament names")]
		public readonly string[] Armaments = { "primary", "secondary", "tertiary", "quaternary", "repair", "clearmines" };

		[CursorReference]
		[Desc("Cursor to display when hovering over a valid target.")]
		public readonly string Cursor = null;

		[CursorReference]
		[Desc("Cursor to display when hovering over a valid target that is outside of range.")]
		public readonly string OutsideRangeCursor = null;

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.Crimson;

		[Desc("Does the attack type require the attacker to enter the target's cell?")]
		public readonly bool AttackRequiresEnteringCell = false;

		[Desc("Allow firing into the fog to target frozen actors without requiring force-fire.")]
		public readonly bool TargetFrozenActors = false;

		[Desc("Force-fire mode ignores actors and targets the ground instead.")]
		public readonly bool ForceFireIgnoresActors = false;

		[Desc("Force-fire mode is required to enable targeting against targets outside of range.")]
		public readonly bool OutsideRangeRequiresForceFire = false;

		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Tolerance for attack angle. Range [0, 512], 512 covers 360 degrees.")]
		public readonly WAngle FacingTolerance = new WAngle(512);

		[Desc("Health percentage below which to retarget (e.g., 50 for 50%).")]
		public readonly int CriticalHealthThreshold = 50;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);

			if (FacingTolerance.Angle > 512)
				throw new YamlException("Facing tolerance must be in range of [0, 512], 512 covers 360 degrees.");
		}

		public abstract override object Create(ActorInitializer init);
	}

	public abstract class AttackBase : PausableConditionalTrait<AttackBaseInfo>, ITick, IIssueOrder, IResolveOrder, IOrderVoice, ISync
	{
		readonly string attackOrderName = "Attack";
		readonly string forceAttackOrderName = "ForceAttack";

		[Sync]
		public bool IsAiming { get; set; }

		public IEnumerable<Armament> Armaments => getArmaments();

		protected IFacing facing;
		protected IPositionable positionable;
		protected INotifyAiming[] notifyAiming;
		protected Func<IEnumerable<Armament>> getArmaments;

		readonly Actor self;

		bool wasAiming;

		public AttackBase(Actor self, AttackBaseInfo info)
			: base(info)
		{
			this.self = self;
		}

		protected override void Created(Actor self)
		{
			facing = self.TraitOrDefault<IFacing>();
			positionable = self.TraitOrDefault<IPositionable>();
			notifyAiming = self.TraitsImplementing<INotifyAiming>().ToArray();

			getArmaments = InitializeGetArmaments(self);

			base.Created(self);
		}

		void ITick.Tick(Actor self)
		{
			Tick(self);
		}

		protected virtual void Tick(Actor self)
		{
			if (!wasAiming && IsAiming)
				foreach (var n in notifyAiming)
					n.StartedAiming(self, this);
			else if (wasAiming && !IsAiming)
				foreach (var n in notifyAiming)
					n.StoppedAiming(self, this);

			wasAiming = IsAiming;
		}

		protected virtual Func<IEnumerable<Armament>> InitializeGetArmaments(Actor self)
		{
			var armaments = self.TraitsImplementing<Armament>()
				.Where(a => Info.Armaments.Contains(a.Info.Name)).ToArray();

			return () => armaments;
		}

		public bool TargetInFiringArc(Actor self, in Target target, WAngle facingTolerance)
		{
			if (facing == null)
				return true;

			var pos = self.CenterPosition;
			var targetedPosition = GetTargetPosition(pos, target);
			var delta = targetedPosition - pos;

			if (delta.HorizontalLengthSquared == 0)
				return true;

			if (target.Type == TargetType.Invalid || (self.TraitOrDefault<IndirectFire>() == null &&
				BlocksProjectiles.AnyBlockingActorsBetween(self, target.CenterPosition, new WDist(1), out _)))
				return false;

			return Util.FacingWithinTolerance(facing.Facing, delta.Yaw, facingTolerance);
		}

		protected virtual bool CanAttack(Actor self, in Target target)
		{
			if (!self.IsInWorld || IsTraitDisabled || IsTraitPaused)
				return false;

			if (!target.IsValidFor(self))
				return false;

			if (!HasAnyValidWeapons(target, reloadingIsInvalid: true))
				return false;

			// PERF: Mobile implements IPositionable, so we can use 'as' to save a trait look-up here.
			if (positionable is Mobile mobile && !mobile.CanInteractWithGroundLayer(self))
				return false;

			return true;
		}

		// Evaluate and potentially switch targets
		protected virtual Target EvaluateTarget(in Target currentTarget, bool forceAttack)
		{
			// If the current target is invalid or not critically damaged, keep it
			if (!currentTarget.IsValidFor(self) || !IsTargetCriticallyDamaged(currentTarget))
				return currentTarget;

			// Scan for a new target
			var newTarget = ScanForNewTarget(currentTarget, forceAttack);
			/* 	if (newTarget.IsValidFor(self))
					Console.WriteLine($"Unit {self} retargeted from {currentTarget.Actor} to {newTarget.Actor}"); */
			return newTarget.IsValidFor(self) ? newTarget : currentTarget;
		}

		// Check if the target is below the critical health threshold
		protected bool IsTargetCriticallyDamaged(in Target target)
		{
			if (!target.IsValidFor(self))
				return false;

			Actor targetActor = null;
			targetActor = target.Type == TargetType.Actor ? target.Actor : target.Type == TargetType.FrozenActor ? target.FrozenActor.Actor : null;

			if (targetActor == null || targetActor.IsDead)
				return false;

			var health = targetActor.TraitOrDefault<Health>();
			if (health == null)
				return false;

			return health.HP < (health.MaxHP * Info.CriticalHealthThreshold / 100);
		}

		// Scan for a new target within range, prioritizing healthy targets
		protected Target ScanForNewTarget(in Target currentTarget, bool forceAttack)
		{
			var maxRange = GetMaximumRange();
			var currentTargetActor = currentTarget.Type == TargetType.Actor ? currentTarget.Actor : currentTarget.Type == TargetType.FrozenActor ? currentTarget.FrozenActor.Actor : null;
			var candidates = self.World.FindActorsInCircle(self.CenterPosition, maxRange)
				.Where(a => a != self && !a.IsDead && a != currentTargetActor && CanTargetActor(a, forceAttack));

			// Prioritize healthy targets (not critically damaged)
			var validTargets = candidates
				.Where(a =>
				{
					var health = a.TraitOrDefault<Health>();
					return health != null && health.HP >= (health.MaxHP * Info.CriticalHealthThreshold / 100);
				})
				.ToList();

			// If no healthy targets, return invalid target to stick with current target
			if (!validTargets.Any())
				return Target.Invalid;

			// Choose the closest valid target
			return Target.FromActor(validTargets.OrderBy(a => (a.CenterPosition - self.CenterPosition).LengthSquared).First());
		}

		// Helper method to check if an actor is a valid target
		protected bool CanTargetActor(Actor targetActor, bool forceAttack)
		{
			var target = Target.FromActor(targetActor);
			if (!target.IsValidFor(self))
				return false;

			// Check if we have valid weapons for this target
			if (!HasAnyValidWeapons(target))
				return false;

			// Check relationship and force-attack rules
			var armaments = ChooseArmamentsForTarget(target, forceAttack);
			return armaments.Any();
		}

		public virtual void DoAttack(Actor self, in Target target)
		{
			if (!CanAttack(self, target))
				return;

			// Evaluate the target to see if we should switch to a new one
			var evaluatedTarget = EvaluateTarget(target, false);

			foreach (var a in Armaments)
			{
				if (a.Info.AllowIndirectFire) // TODO FF, Unimplemented
					a.CheckFire(self, facing, evaluatedTarget);
			}
		}

		// Modified to avoid CS1628 by copying the target to a local variable
		public IEnumerable<Armament> ChooseArmamentsForTarget(in Target t, bool forceAttack)
		{
			// Copy the target to a local variable to avoid capturing 'in' parameter in lambda
			var target = t;

			// If force-fire is not used, and the target requires force-firing or the target is
			// terrain or invalid, no armaments can be used
			if (!forceAttack && (target.Type == TargetType.Terrain || target.Type == TargetType.Invalid || target.RequiresForceFire))
				return Enumerable.Empty<Armament>();

			// Get target's owner; in case of terrain or invalid target there will be no problems
			// with owner == null since forceFire will have to be true in this part of the method
			// (short-circuiting in the logical expression below)
			Player owner = null;
			if (target.Type == TargetType.FrozenActor)
				owner = target.FrozenActor.Owner;
			else if (target.Type == TargetType.Actor)
				owner = target.Actor.Owner;

			// FF TODO Check ammo?
			return Armaments.Where(a =>
				!a.IsTraitDisabled
				&& (owner == null || (forceAttack ? a.Info.ForceTargetRelationships : a.Info.TargetRelationships).HasRelationship(self.Owner.RelationshipWith(owner)))
				&& a.Weapon.IsValidAgainst(target, self.World, self));
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get
			{
				if (IsTraitDisabled)
					yield break;

				if (!Armaments.Any())
					yield break;

				yield return new AttackOrderTargeter(this, 6);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			if (order is AttackOrderTargeter)
				return new Order(order.OrderID, self, target, queued);

			return null;
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			var forceAttack = order.OrderString == forceAttackOrderName;
			if (forceAttack || order.OrderString == attackOrderName)
			{
				if (!order.Target.IsValidFor(self))
					return;

				AttackTarget(order.Target, AttackSource.Default, order.Queued, true, forceAttack, Info.TargetLineColor);
				self.ShowTargetLines();
			}
			else if (order.OrderString == "Stop")
				OnStopOrder(self);
		}

		// Some 3rd-party mods rely on this being public
		public virtual void OnStopOrder(Actor self)
		{
			// We don't want Stop orders from traits other than Mobile or Aircraft to cancel Resupply activity.
			// Resupply is always either the main activity or a child of ReturnToBase.
			// TODO: This should generally only cancel activities queued by this trait.
			if (self.CurrentActivity == null || self.CurrentActivity is Resupply || self.CurrentActivity is ReturnToBase)
				return;

			self.CancelActivity();
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			return order.OrderString == attackOrderName || order.OrderString == forceAttackOrderName ? Info.Voice : null;
		}

		public abstract Activity GetAttackActivity(Actor self, AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor = null);

		public bool HasAnyValidWeapons(in Target t, bool checkForCenterTargetingWeapons = false, bool reloadingIsInvalid = false)
		{
			if (IsTraitDisabled)
				return false;

			if (Info.AttackRequiresEnteringCell && (positionable == null || !positionable.CanEnterCell(t.Actor.Location, null, BlockedByActor.None)))
				return false;

			// PERF: Avoid LINQ.
			foreach (var armament in Armaments)
			{
				var checkIsValid = checkForCenterTargetingWeapons ? armament.Weapon.TargetActorCenter : !armament.IsTraitPaused;
				var reloadingStateIsValid = !reloadingIsInvalid || !armament.IsReloading;
				if (checkIsValid && reloadingStateIsValid && !armament.IsTraitDisabled && armament.Weapon.IsValidAgainst(t, self.World, self))
					return true;
			}

			return false;
		}

		public virtual WPos GetTargetPosition(WPos pos, in Target target)
		{
			return HasAnyValidWeapons(target, true) ? target.CenterPosition : target.Positions.PositionClosestTo(pos);
		}

		public virtual WPos GetCurrentTarget(WPos pos, in Target target)
		{
			return HasAnyValidWeapons(target, true) ? target.CenterPosition : target.Positions.PositionClosestTo(pos);
		}

		public WDist GetMinimumRange()
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			// PERF: Avoid LINQ.
			var min = WDist.MaxValue;
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (armament.IsTraitPaused)
					continue;

				var range = armament.Weapon.MinRange;
				if (min > range)
					min = range;
			}

			return min != WDist.MaxValue ? min : WDist.Zero;
		}

		public WDist GetMaximumRange()
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			// PERF: Avoid LINQ.
			var max = WDist.Zero;
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (armament.IsTraitPaused)
					continue;

				var range = armament.MaxRange();
				if (max < range)
					max = range;
			}

			return max;
		}

		public WDist GetMinimumRangeVersusTarget(in Target target)
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			// PERF: Avoid LINQ.
			var min = WDist.MaxValue;
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (armament.IsTraitPaused)
					continue;

				if (!armament.Weapon.IsValidAgainst(target, self.World, self))
					continue;

				var range = armament.Weapon.MinRange;
				if (min > range)
					min = range;
			}

			return min != WDist.MaxValue ? min : WDist.Zero;
		}

		public WDist GetMaximumRangeVersusTarget(in Target target)
		{
			if (IsTraitDisabled)
				return WDist.Zero;

			var max = WDist.Zero;

			// We want actors to use only weapons with ammo for this, except when ALL weapons are out of ammo,
			// then we use the paused, valid weapon with highest range.
			var maxFallback = WDist.Zero;

			// PERF: Avoid LINQ.
			foreach (var armament in Armaments)
			{
				if (armament.IsTraitDisabled)
					continue;

				if (!armament.Weapon.IsValidAgainst(target, self.World, self))
					continue;

				var range = armament.MaxRange();
				if (maxFallback < range)
					maxFallback = range;

				if (armament.IsTraitPaused)
					continue;

				if (max < range)
					max = range;
			}

			return max != WDist.Zero ? max : maxFallback;
		}

		public void AttackTarget(in Target target, AttackSource source, bool queued, bool allowMove, bool forceAttack = false, Color? targetLineColor = null)
		{
			if (IsTraitDisabled)
				return;

			if (!target.IsValidFor(self))
				return;

			var activity = GetAttackActivity(self, source, target, allowMove, forceAttack, targetLineColor);
			self.QueueActivity(queued, activity);
			OnResolveAttackOrder(self, activity, target, queued, forceAttack);
		}

		public virtual void OnResolveAttackOrder(Actor self, Activity activity, in Target target, bool queued, bool forceAttack) { }

		public bool IsReachableTarget(in Target target, bool allowMove)
		{
			return HasAnyValidWeapons(target)
				&& (target.IsInRange(self.CenterPosition, GetMaximumRangeVersusTarget(target)) || (allowMove && self.Info.HasTraitInfo<IMoveInfo>()));
		}

		public PlayerRelationship UnforcedAttackTargetStances()
		{
			// PERF: Avoid LINQ.
			var stances = PlayerRelationship.None;
			foreach (var armament in Armaments)
				if (!armament.IsTraitDisabled)
					stances |= armament.Info.TargetRelationships;

			return stances;
		}

		class AttackOrderTargeter : IOrderTargeter
		{
			readonly AttackBase ab;

			public AttackOrderTargeter(AttackBase ab, int priority)
			{
				this.ab = ab;
				OrderID = ab.attackOrderName;
				OrderPriority = priority;
			}

			public string OrderID { get; private set; }
			public int OrderPriority { get; }
			public bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers) { return true; }

			bool CanTargetActor(Actor self, in Target target, List<Actor> othersAtTarget, CPos xy, TargetModifiers modifiers, ref string cursor)
			{
				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				if (modifiers.HasModifier(TargetModifiers.ForceMove))
					return false;

				if (ab.Info.ForceFireIgnoresActors && modifiers.HasModifier(TargetModifiers.ForceAttack))
					return false;

				// Disguised actors are revealed by the attack cursor
				if (target.Type == TargetType.Actor && target.Actor.EffectiveOwner != null &&
					target.Actor.EffectiveOwner.Disguised && self.Owner.RelationshipWith(target.Actor.Owner) == PlayerRelationship.Enemy)
					modifiers |= TargetModifiers.ForceAttack;

				var forceAttack = modifiers.HasModifier(TargetModifiers.ForceAttack);
				var armaments = ab.ChooseArmamentsForTarget(target, forceAttack);

				if (!armaments.Any())
					return false;

				armaments = armaments.OrderByDescending(x => x.MaxRange());
				var a = armaments.FirstOrDefault(x => !x.IsTraitPaused) ?? armaments.First();

				if (armaments.All(armament => armament.AmmoPool != null && !armament.AmmoPool.HasAmmo))
					return false;

				var outOfRange = !target.IsInRange(self.CenterPosition, a.MaxRange()) ||
					(!forceAttack && target.Type == TargetType.FrozenActor && !ab.Info.TargetFrozenActors);

				if (outOfRange && ab.Info.OutsideRangeRequiresForceFire && !modifiers.HasModifier(TargetModifiers.ForceAttack))
					return false;

				cursor = outOfRange ? ab.Info.OutsideRangeCursor ?? a.Info.OutsideRangeCursor : ab.Info.Cursor ?? a.Info.Cursor;

				if (!forceAttack)
					return true;

				OrderID = ab.forceAttackOrderName;
				return true;
			}

			bool CanTargetLocation(Actor self, CPos location, List<Actor> othersAtTarget, CPos xy, TargetModifiers modifiers, ref string cursor)
			{
				if (!self.World.Map.Contains(location))
					return false;

				IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

				if (modifiers.HasModifier(TargetModifiers.ForceMove) || !modifiers.HasModifier(TargetModifiers.ForceAttack))
					return false;

				var target = Target.FromCell(self.World, location);
				var armaments = ab.ChooseArmamentsForTarget(target, true);
				if (!armaments.Any())
					return false;

				armaments = armaments.OrderByDescending(x => x.MaxRange());
				var a = armaments.FirstOrDefault(x => !x.IsTraitPaused) ?? armaments.First();

				cursor = !target.IsInRange(self.CenterPosition, a.MaxRange())
					? ab.Info.OutsideRangeCursor ?? a.Info.OutsideRangeCursor
					: ab.Info.Cursor ?? a.Info.Cursor;

				OrderID = ab.forceAttackOrderName;
				return true;
			}

			public bool CanTarget(Actor self, in Target target, List<Actor> othersAtTarget, CPos xy, TargetModifiers modifiers, ref string cursor)
			{
				switch (target.Type)
				{
					case TargetType.Actor:
					case TargetType.FrozenActor:
						return CanTargetActor(self, target, othersAtTarget, xy, modifiers, ref cursor);
					case TargetType.Terrain:
						return CanTargetLocation(self, self.World.Map.CellContaining(target.CenterPosition), othersAtTarget, xy, modifiers, ref cursor);
					default:
						return false;
				}
			}

			public bool IsQueued { get; protected set; }
		}
	}
}
