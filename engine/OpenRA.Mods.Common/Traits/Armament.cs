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
using OpenRA.GameRules;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class Barrel
	{
		public int Index;
		public WVec Offset;
		public WAngle Yaw;
	}

	public readonly struct BurstStep
	{
		public readonly int Burst;
		public readonly int Wait;
		public readonly bool Completed;
		public readonly int StaleTick;

		public BurstStep(int burst, int wait, bool completed, int staleTick)
		{
			Burst = burst;
			Wait = wait;
			Completed = completed;
			StaleTick = staleTick;
		}
	}

	// Burst-counter arithmetic, split out of Armament so the interrupted-burst rules can be
	// driven without an Actor or World (see BurstSequenceTest).
	public static class BurstSequence
	{
		// No partial burst is outstanding, so there is nothing that could go stale.
		public const int NoPendingBurst = -1;

		public static int InterShotDelay(int weaponBurst, int remaining, int[] burstDelays)
		{
			return burstDelays.Length == 1 ? burstDelays[0] : burstDelays[weaponBurst - (remaining + 1)];
		}

		// The reset clock starts when a shot fails to arrive on schedule and then runs for a full
		// between-bursts wait, rather than measuring the raw gap since the last shot. Measuring the
		// raw gap would call a healthy burst stale on any weapon whose inter-shot delay is not
		// shorter than its BurstWait (Mandible 14/10, MandibleHeavy 20/15), and would let a unit
		// that repeatedly interrupts itself out-shoot one that lets its bursts run to the end.
		public static int StaleTick(int worldTick, int interShotDelay, int burstWait)
		{
			return worldTick + interShotDelay + burstWait;
		}

		public static bool IsStale(int staleTick, int worldTick)
		{
			return staleTick != NoPendingBurst && worldTick >= staleTick;
		}

		// One shot's effect on the counter. On completion the caller restores the full burst via
		// ResetBurst; Burst is left below 1 here so that path stays the only one that knows the
		// modifier-adjusted starting value.
		public static BurstStep Advance(int burst, int weaponBurst, int[] burstDelays, int burstWait, int worldTick)
		{
			if (--burst < 1)
				return new BurstStep(burst, burstWait, true, NoPendingBurst);

			var delay = InterShotDelay(weaponBurst, burst, burstDelays);
			return new BurstStep(burst, delay, false, StaleTick(worldTick, delay, burstWait));
		}
	}

	[Desc("Allows you to attach weapons to the unit (use @IdentifierSuffix for > 1)")]
	public class ArmamentInfo : PausableConditionalTraitInfo, Requires<AttackBaseInfo>
	{
		public readonly string Name = "primary";

		[WeaponReference]
		[FieldLoader.Require]
		[Desc("Has to be defined in weapons.yaml as well.")]
		public readonly string Weapon = null;

		[Desc("Which turret (if present) should this armament be assigned to.")]
		public readonly string Turret = "primary";

		[Desc("Cannot be 0 for Bullet Projectiles as it is used to calculate how much to lead target by checking position change (speed) between this many ticks. Lower numbers tested and does not work properly.")]
		public readonly int FireDelay = 3;

		[Desc("How long time unit needs after acquiring the target (turret facing) to aim, before being able to fire")]
		public readonly int AimingDelay = 15;

		[Desc("How much a moving target cause added inaccuracy. (TargetSpeed * MovementInaccuracy * distanceToTarget / MaxRange)")]
		public readonly int MovementInaccuracy = 30;

		[Desc("Muzzle position relative to turret or body, (forward, right, up) triples.",
			"If weapon Burst = 1, it cycles through all listed offsets, otherwise the offset corresponding to current burst is used.")]
		public readonly WVec[] LocalOffset = Array.Empty<WVec>();

		[Desc("Muzzle yaw relative to turret or body.")]
		public readonly WAngle[] LocalYaw = Array.Empty<WAngle>();

		[Desc("Move the turret backwards when firing.")]
		public readonly WDist Recoil = WDist.Zero;

		[Desc("Recoil recovery per-frame")]
		public readonly WDist RecoilRecovery = new WDist(9);

		[SequenceReference]
		[Desc("Muzzle flash sequence to render")]
		public readonly string MuzzleSequence = null;

		[PaletteReference]
		[Desc("Palette to render Muzzle flash sequence in")]
		public readonly string MuzzlePalette = "effect";

		[GrantedConditionReference]
		[Desc("Condition to grant while reloading.")]
		public readonly string ReloadingCondition = null;

		[GrantedConditionReference]
		public string WeaponCondition => "weapon-" + Name;

		[Desc("If unit has IndirectFire trait this can be disabled for specific armaments.")]
		public readonly bool AllowIndirectFire = true; // TODO FF, Unimplemented

		[Desc("Hide this armament from AutoTarget — only force-fire / explicit player attack orders use it. " +
			"Use for deploy/strike weapons (drone targeter, missile launcher, artillery strike) whose " +
			"ValidTargets accidentally match enemy actors and would auto-trigger.")]
		public readonly bool RequiresForceFire = false;

		[Desc("Don't let this armament cancel a player Move via SmartMove's self-defense logic. " +
			"The armament can still fire when the unit is stationary — it just won't pause an in-progress move. " +
			"Use for low-priority self-defense weapons (e.g. drone jammer) that shouldn't override player intent.")]
		public readonly bool NoSelfDefenseInterrupt = false;

		public WeaponInfo WeaponInfo { get; private set; }
		public WDist ModifiedRange { get; private set; }

		public readonly PlayerRelationship TargetRelationships = PlayerRelationship.Enemy;
		public readonly PlayerRelationship ForceTargetRelationships = PlayerRelationship.Enemy | PlayerRelationship.Neutral | PlayerRelationship.Ally;

		// TODO: instead of having multiple Armaments and unique AttackBase,
		// an actor should be able to have multiple AttackBases with
		// a single corresponding Armament each
		[CursorReference]
		[Desc("Cursor to display when hovering over a valid target.")]
		public readonly string Cursor = "attack";

		// TODO: same as above
		[CursorReference]
		[Desc("Cursor to display when hovering over a valid target that is outside of range.")]
		public readonly string OutsideRangeCursor = "attackoutsiderange";

		[Desc("Ammo the weapon consumes per shot.")]
		public readonly int AmmoUsage = 1;

		public override object Create(ActorInitializer init) { return new Armament(init.Self, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var weaponToLower = Weapon.ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out var weaponInfo))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{weaponToLower}'");

			WeaponInfo = weaponInfo;
			ModifiedRange = new WDist(Util.ApplyPercentageModifiers(
				WeaponInfo.Range.Length,
				ai.TraitInfos<IRangeModifierInfo>().Select(m => m.GetRangeModifierDefault())));

			if (WeaponInfo.Burst > 1 && WeaponInfo.BurstDelays.Length > 1 && (WeaponInfo.BurstDelays.Length != WeaponInfo.Burst - 1))
				throw new YamlException($"Weapon '{weaponToLower}' has an invalid number of BurstDelays, must be single entry or Burst - 1.");

			if (WeaponInfo.BurstWait == 0)
				throw new YamlException($"Weapons must define BurstWait: '{weaponToLower}'");

			base.RulesetLoaded(rules, ai);
		}
	}

	public class Armament : PausableConditionalTrait<ArmamentInfo>, ITick, INotifyAiming
	{
		public readonly WeaponInfo Weapon;
		public readonly Barrel[] Barrels;

		readonly Actor self;
		Turreted turret;
		BodyOrientation coords;
		INotifyBurstComplete[] notifyBurstComplete;
		INotifyMagazineComplete[] notifyMagazineComplete;
		INotifyAttack[] notifyAttacks;

		int conditionToken = Actor.InvalidConditionToken;

		IEnumerable<int> rangeModifiers;
		IEnumerable<int> reloadModifiers;
		IEnumerable<int> burstWaitModifiers;
		IEnumerable<int> burstModifiers;
		IEnumerable<int> damageModifiers;
		IEnumerable<int> inaccuracyModifiers;

		// int ticksSinceLastShot; // FF ??
		int currentBarrel;
		readonly int barrelCount;

		readonly List<(int Ticks, int Burst, Action<int> Func)> delayedActions = new List<(int, int, Action<int>)>();

		public WDist Recoil;
		public int Magazine { get; protected set; }
		public int AimingDelay { get; protected set; }
		public int ReloadDelay { get; protected set; }
		public int Burst { get; protected set; }
		public int BurstWait { get; protected set; }
		public int FireDelay { get; protected set; }
		public bool IsBurstWait { get; protected set; }
		public AmmoPool AmmoPool
		{
			get
			{
				var matchingAmmopool = self.TraitsImplementing<AmmoPool>().FirstOrDefault(ammopool =>
					ammopool.Info.Armaments.Any(armament => armament == Info.Name));
				return matchingAmmopool;
			}
		}

		public List<WPos> AimInitialTargetPosition { get; protected set; }
		public int AimInitialTicksBefore { get; protected set; }
		public Target? Target { get; protected set; }
		Target? oldTarget = null;

		// Diagnostic only, and read ONLY inside a GunTrace.Enabled branch: last set of CanFire
		// blocking reasons, so the trace is edge-triggered instead of one line per tick. Kept
		// updated unconditionally so enabling the trace mid-game cannot miss the first transition.
		int lastBlockMask = -1;
		int lastFiredTick = -1;

		// Tick at which an unfinished burst counts as interrupted and is restarted from full.
		// NoPendingBurst whenever the counter is already sitting at a fresh burst.
		int burstStaleTick = BurstSequence.NoPendingBurst;

		// Modifier-adjusted number of shots a fresh burst is worth. Suppression and damage-tier
		// BurstMultipliers drive this down, so it is not interchangeable with Weapon.Burst —
		// which stays the raw value the BurstDelays list is indexed against.
		int FullBurst => Util.ApplyPercentageModifiers(Weapon.Burst, burstModifiers);

		// LockAimPerBurst: lead-corrected impact point captured by the first shot's
		// delayed action and reused by every subsequent shot in the same burst.
		// Cleared at the start of each new burst, on target change, and when aiming stops.
		WPos? lockedAimCenter;

		public Armament(Actor self, ArmamentInfo info)
			: base(info)
		{
			this.self = self;
			AimInitialTargetPosition = new List<WPos>();

			Weapon = info.WeaponInfo;
			Burst = Weapon.Burst;

			var barrels = new List<Barrel>();
			for (var i = 0; i < info.LocalOffset.Length; i++)
			{
				barrels.Add(new Barrel
				{
					Index = i,
					Offset = info.LocalOffset[i],
					Yaw = info.LocalYaw.Length > i ? info.LocalYaw[i] : WAngle.Zero
				});
			}

			if (barrels.Count == 0)
				barrels.Add(new Barrel { Offset = WVec.Zero, Yaw = WAngle.Zero });

			barrelCount = barrels.Count;

			Barrels = barrels.ToArray();
		}

		void INotifyAiming.StartedAiming(Actor self, AttackBase attack) { }

		void INotifyAiming.StoppedAiming(Actor self, AttackBase attack)
		{
			// Game.Debug("StoppedAiming -- {0}", self.Info.Name);
			AimInitialTargetPosition.Clear();
			lockedAimCenter = null;
		}

		// void INotifyNewTarget.Acquired(Actor self)
		// {
		// 	// Game.Debug("Acquired -- {0}", self.Info.Name);
		// }
		public virtual WDist MaxRange()
		{
			return new WDist(Util.ApplyPercentageModifiers(Weapon.Range.Length, rangeModifiers));
		}

		public virtual WDist MinRange()
		{
			return new WDist(Weapon.MinRange.Length);
		}

		protected override void Created(Actor self)
		{
			Magazine = Weapon.Magazine;
			Burst = Weapon.Burst;

			turret = self.TraitsImplementing<Turreted>().FirstOrDefault(t => t.Name == Info.Turret);
			coords = self.Trait<BodyOrientation>();
			notifyBurstComplete = self.TraitsImplementing<INotifyBurstComplete>().ToArray();
			notifyMagazineComplete = self.TraitsImplementing<INotifyMagazineComplete>().ToArray();
			notifyAttacks = self.TraitsImplementing<INotifyAttack>().ToArray();

			rangeModifiers = self.TraitsImplementing<IRangeModifier>().ToArray().Select(m => m.GetRangeModifier());
			burstWaitModifiers = self.TraitsImplementing<IBurstWaitModifier>().ToArray().Select(m => m.GetBurstWaitModifier());
			burstModifiers = self.TraitsImplementing<IBurstModifier>().ToArray().Select(m => m.GetBurstModifier());
			reloadModifiers = self.TraitsImplementing<IReloadModifier>().ToArray().Select(m => m.GetReloadModifier());
			damageModifiers = self.TraitsImplementing<IFirepowerModifier>().ToArray().Select(m => m.GetFirepowerModifier());
			inaccuracyModifiers = self.TraitsImplementing<IInaccuracyModifier>().ToArray().Select(m => m.GetInaccuracyModifier());

			self.GrantCondition("weapon-" + Info.Name);

			base.Created(self);
		}

		void UpdateReloadingCondition(Actor self)
		{
			if (string.IsNullOrEmpty(Info.ReloadingCondition))
				return;

			var isReloading = !IsTraitDisabled && IsReloading;

			if (isReloading && conditionToken == Actor.InvalidConditionToken)
				conditionToken = self.GrantCondition(Info.ReloadingCondition);
			else if (!isReloading && conditionToken != Actor.InvalidConditionToken)
				conditionToken = self.RevokeCondition(conditionToken);
		}

		protected virtual void Tick(Actor self)
		{
			// We need to disable conditions if IsTraitDisabled is true, so we have to update conditions before the return below.
			UpdateReloadingCondition(self);

			if (IsTraitDisabled)
			{
				delayedActions.Clear(); // Seems necessary to stop immediatelly, but will cause ammo to be drawn so needs updating for that
				return;
			}

			if (AimingDelay > 0)
				--AimingDelay;

			if (ReloadDelay > 0)
				--ReloadDelay;

			if (BurstWait > 0)
				--BurstWait;

			Recoil = new WDist(Math.Max(0, Recoil.Length - Info.RecoilRecovery.Length));

			for (var i = 0; i < delayedActions.Count; i++)
			{
				var x = delayedActions[i];
				if (--x.Ticks <= 0)
					x.Func(x.Burst);
				delayedActions[i] = x;
			}

			delayedActions.RemoveAll(a => a.Ticks <= 0);
		}

		void ITick.Tick(Actor self)
		{
			// Split into a protected method to allow subclassing
			Tick(self);
		}

		protected void ScheduleDelayedAction(int t, int b, Action<int> a)
		{
			if (t > 0)
				delayedActions.Add((t, b, a));
			else
				a(b);
		}

		protected virtual bool CanFire(Actor self, in Target target)
		{
			if (IsReloading || IsWaitingBurst || IsAiming || IsTraitPaused)
				return false;

			if (turret != null && !turret.HasAchievedDesiredFacing)
				return false;

			if ((!target.IsInRange(self.CenterPosition, MaxRange()))
				|| (Weapon.MinRange != WDist.Zero && target.IsInRange(self.CenterPosition, Weapon.MinRange)))
				return false;

			if (!Weapon.IsValidAgainst(target, self.World, self))
				return false;

			return true;
		}

		// Note: facing is only used by the legacy positioning code
		// The world coordinate model uses Actor.Orientation
		public virtual Barrel CheckFire(Actor self, IFacing facing, in Target target)
		{
			// PITFALL: this treats "not Equals to last tick's target" as a fresh ACQUISITION and
			// restarts the aim countdown. Target's operator== compares a Terrain target's
			// terrainPositions array BY REFERENCE (Target.cs:233), and Target.FromTargetPositions
			// allocates a new one per call (Target.cs:86) — so any caller that rebuilds a
			// positional target every tick resets AimingDelay forever and can never fire. That is
			// not hypothetical: it is what made every AttackType: Strafe airframe fire zero shots
			// until 2026-09-02 (see FlyAttack.cs StrafeAttackRun.aimPoint). If you hand this method
			// a target, hand it the SAME value while the engagement lasts.
			var retargeted = !target.Equals(oldTarget);
			if (retargeted)
			{
				oldTarget = target;
				AimingDelay = Info.AimingDelay;
				delayedActions.Clear();
				AimInitialTargetPosition.Clear();
				lockedAimCenter = null;
			}

			if (!CanFire(self, target))
			{
				// Env-gated (WW3_GUNTRACE=1), and edge-triggered so a permanently blocked armament
				// writes one line per change of reason rather than one per tick. This exists
				// because the aim-reset defect above was diagnosed by reading code and arithmetic;
				// the trace makes the same claim directly observable.
				if (GunTrace.Enabled)
				{
					var blockMask =
						(IsReloading ? 1 : 0)
						| (IsWaitingBurst ? 2 : 0)
						| (IsAiming ? 4 : 0)
						| (IsTraitPaused ? 8 : 0)
						| (turret != null && !turret.HasAchievedDesiredFacing ? 16 : 0)
						| (!target.IsInRange(self.CenterPosition, MaxRange()) ? 32 : 0)
						| (Weapon.MinRange != WDist.Zero && target.IsInRange(self.CenterPosition, Weapon.MinRange) ? 64 : 0)
						| (!Weapon.IsValidAgainst(target, self.World, self) ? 128 : 0);

					if (blockMask != lastBlockMask)
					{
						lastBlockMask = blockMask;
						GunTrace.Write(
							$"CheckFire BLOCKED shooter={self.Info.Name} armament={Info.Name} weapon={Info.Weapon}"
							+ $" targetType={target.Type} retargetedThisTick={retargeted}"
							+ $" aimingDelay={AimingDelay}/{Info.AimingDelay} mask={blockMask}"
							+ $" [reloading={IsReloading} waitingBurst={IsWaitingBurst} aiming={IsAiming}"
							+ $" paused={IsTraitPaused} outOfMaxRange={!target.IsInRange(self.CenterPosition, MaxRange())}"
							+ $" insideMinRange={Weapon.MinRange != WDist.Zero && target.IsInRange(self.CenterPosition, Weapon.MinRange)}"
							+ $" invalidTarget={!Weapon.IsValidAgainst(target, self.World, self)}]");
					}
				}

				return null;
			}

			lastBlockMask = 0;

			// Per-weapon LOS gate. The unit-level gate in AttackBase / AutoTarget uses the
			// most permissive threshold across all armaments (so units don't refuse to fire
			// at all just because one weapon is strict). Refine here per weapon so a strict
			// weapon (e.g. WGM through trees) declines while a looser one on the same unit
			// (e.g. 25mm chaingun) can still fire.
			if (!FiringLOS.HasClearLOS(self, target, Weapon.ClearSightThreshold))
				return null;

			// An interrupted burst starts again from full rather than delivering whatever was left
			// of it. UpdateBurst was called here previously, which decremented the counter instead
			// of restoring it, so a burst broken off partway came back one or two shots long.
			if (BurstSequence.IsStale(burstStaleTick, self.World.WorldTick))
				ResetBurst(self);

			// If Weapon.Burst == 1, cycle through all LocalOffsets, otherwise use the offset corresponding to current Burst
			currentBarrel %= barrelCount;
			var barrel = Weapon.Burst == 1 ? Barrels[currentBarrel] : Barrels[Burst % Barrels.Length];
			currentBarrel++;

			FireBarrel(self, facing, target, barrel);

			UpdateMagazine(self, target);
			UpdateBurst(self, target);

			return barrel;
		}

		protected virtual void FireBarrel(Actor self, IFacing facing, in Target target, Barrel barrel)
		{
			Target = target;
			var previousLastFiredTick = lastFiredTick;
			lastFiredTick = self.World.WorldTick;

			if (GunTrace.Enabled)
			{
				// Indexed: every entry is a FirepowerMultiplier, so the position in
				// declaration order is the only thing that names which YAML instance
				// (@Rank_1..@Rank_4, @CrashDisabled, @NoGunner, @EmergencyDescent) is
				// responsible for a zero.
				var mods = string.Join(", ", self.TraitsImplementing<IFirepowerModifier>()
					.Select((m, i) => $"{i}:{m.GetType().Name}={m.GetFirepowerModifier()}"));
				GunTrace.Write($"FireBarrel shooter={self.Info.Name} armament={Info.Name} weapon={Info.Weapon} shooterPos={self.CenterPosition} targetType={target.Type} firepowerModifiers=[{mods}]");
			}

			if (target.Type != TargetType.Invalid)
			{
				AimInitialTargetPosition.Add(target.CenterPosition);
			}

			if (Weapon.LockAimPerBurst)
			{
				// Detect the first shot of a fresh burst so the upcoming delayed action recomputes the locked impact point.
				// Burst counts down per shot and is reset to the modifier-adjusted starting value when the previous burst completes,
				// so equality with that starting value (and the no-prior-fire / long-idle cases) marks a new burst.
				var idleLongerThanBurstWait = previousLastFiredTick != -1 && lastFiredTick - previousLastFiredTick > Weapon.BurstWait;
				if (Burst == FullBurst || previousLastFiredTick == -1 || idleLongerThanBurstWait)
					lockedAimCenter = null;
			}

			foreach (var na in notifyAttacks)
				na.PreparingAttack(self, target, this, barrel);

			WPos MuzzlePosition() => self.CenterPosition + MuzzleOffset(self, barrel);
			WAngle MuzzleFacing() => MuzzleOrientation(self, barrel).Yaw;
			var muzzleOrientation = WRot.FromYaw(MuzzleFacing());

			var passiveTarget = Weapon.TargetActorCenter ? target.CenterPosition : target.Positions.ClosestToIgnoringPath(MuzzlePosition());
			var initialOffset = Weapon.FirstBurstTargetOffset;
			var targetingVector = WVec.Zero;

			if (initialOffset != WVec.Zero)
			{
				// We want this to match Armament.LocalOffset, so we need to convert it to forward, right, up
				initialOffset = new WVec(initialOffset.Y, -initialOffset.X, initialOffset.Z);

				targetingVector += initialOffset.Rotate(muzzleOrientation);
				passiveTarget += initialOffset.Rotate(muzzleOrientation);
			}

			var followingOffset = Weapon.FollowingBurstTargetOffset;
			if (followingOffset != WVec.Zero)
			{
				// We want this to match Armament.LocalOffset, so we need to convert it to forward, right, up
				followingOffset = new WVec(followingOffset.Y, -followingOffset.X, followingOffset.Z);

				targetingVector += ((Weapon.Burst - Burst) * followingOffset).Rotate(muzzleOrientation);
				passiveTarget += ((Weapon.Burst - Burst) * followingOffset).Rotate(muzzleOrientation);
			}

			var args = new ProjectileArgs
			{
				Weapon = Weapon,
				Facing = MuzzleFacing(),
				CurrentMuzzleFacing = MuzzleFacing,
				DamageModifiers = damageModifiers.ToArray(),
				InaccuracyModifiers = inaccuracyModifiers.ToArray(),
				RangeModifiers = rangeModifiers.ToArray(),
				Source = MuzzlePosition(),
				CurrentSource = MuzzlePosition,
				SourceActor = self,
				PassiveTarget = passiveTarget,
				TargetingVector = targetingVector,
				GuidedTarget = target
			};

			// Lambdas can't use 'in' variables, so capture a copy for later
			var delayedTarget = target;
			ScheduleDelayedAction(Info.FireDelay, Burst, (burst) =>
			{
				// THE SHOT HAS RESOLVED — hand back this shooter's overkill claim
				// (Actor.ReleaseAttackClaim / OverkillClaim). The claim was a PREDICTION registered when the
				// unit committed, so that other units scanning in the same window saw the target as spoken
				// for; the trigger has now been pulled and the prediction is settled. Held past this point it
				// double-counts against damage that is now either inbound or never coming, and since nothing
				// else ever gave it back, a target under sustained attention read as permanently
				// over-committed and AutoTarget.ChooseTarget declined it for everyone.
				//
				// ABOVE the re-validation return below, deliberately: a shot aborted because the target died
				// in the FireDelay gap is the case where holding the reservation is least defensible. Above
				// the projectile spawn for the same reason — a shot that misses, expires short or spawns
				// nothing must release too, and a projectile carries no back-reference to the claim.
				// In Armament rather than in a trait because this is the one choke point every armament in
				// the game fires through, including the garrison paths with their own notify loops.
				self.ReleaseAttackClaim();

				// Re-validate the captured target. With FireDelay > 0 the target can
				// die / leave the world / drop targetable status during the gap
				// between aim-and-pull and the actual barrel-fire. Without this check
				// a wire-guided ATGM would launch a tracker missile aimed at a stale
				// position; the missile would home on last-known and either fuel-out
				// or re-target via the operator system. Cheaper and more honest to
				// just abort the shot — the operator wouldn't pull the trigger if
				// the lock broke between aim and squeeze. Ammo is already accounted
				// for, this is just suppressing the projectile spawn.
				//
				// Skip when the target was already invalidated INSIDE FireBarrel
				// (e.g. the tree-clip path below sets GuidedTarget=Invalid by design
				// after capture). delayedTarget is the captured original — if the
				// caller passed an invalid target (force-fire on terrain), Type is
				// Invalid here and we treat that as "no actor to validate".
				if (delayedTarget.Type == TargetType.Actor && !delayedTarget.IsValidFor(self))
					return;

				// Lead/aim in front of moving target
				if (args.Weapon.Projectile != null)
				{
					// If projectile is bullet (not missile), lead (aim in front of) target
					if (Weapon.Projectile is BulletInfo bullet && Target.Value.Type != TargetType.Invalid)
					{
						if (Weapon.LockAimPerBurst && lockedAimCenter.HasValue)
						{
							// Subsequent shot in a burst — reuse the locked impact point so the volley doesn't snake with target turns.
							// The per-shot FirstBurst/FollowingBurstTargetOffset spread is already in args.TargetingVector.
							if (AimInitialTargetPosition.Count > 0)
								AimInitialTargetPosition.RemoveAt(0);
							args.PassiveTarget = lockedAimCenter.Value + args.TargetingVector;
						}
						else
						{
							var initialPosition = AimInitialTargetPosition.FirstOrDefault();

							if (!initialPosition.Equals(default))
							{
								var targetPosition = Target.Value.CenterPosition;
								var leadTarget = WVec.CalculateLeadTarget(self.CenterPosition, initialPosition, targetPosition, Info.FireDelay, bullet.Speed.First().Length);
								var distanceToTarget = WPos.PositionDiff(targetPosition, self.CenterPosition).HorizontalLength;

								if (AimInitialTargetPosition.Count > 0)
									AimInitialTargetPosition.RemoveAt(0);

								var aimCenter = targetPosition + leadTarget;

								// Add inaccuracy for moving targets — applied once when the lock is established;
								// reused locked shots get the same wobble.
								var targetMobile = delayedTarget.Actor?.TraitOrDefault<Mobile>();
								if (targetMobile != null)
								{
									var maxInaccuracy = (int)((float)bullet.Inaccuracy.Length * Info.MovementInaccuracy / 100 * targetMobile.CurrentSpeed / targetMobile.Info.Speed * distanceToTarget / args.Weapon.Range.Length);

									// movementInaccuracy goes infront of or behind actors direction
									var wVec = new WVec(0, self.World.SharedRandom.Next(-maxInaccuracy, maxInaccuracy), 0).Rotate(WRot.FromYaw(leadTarget.Yaw));
									aimCenter += wVec;
								}

								if (Weapon.LockAimPerBurst)
									lockedAimCenter = aimCenter;

								args.PassiveTarget = aimCenter + args.TargetingVector;
							}
						}
					}

					// Density-based miss roll for foliage-sensitive weapons (wire-guided ATGMs, helicopter
					// missiles). At fire time we know the same shadow density the LOS gate already used.
					// On miss, redirect the projectile to a tree on the line so it visibly clips foliage
					// instead of magically hitting through the canopy.
					if (args.Weapon.MissChancePerDensity > 0 && delayedTarget.Type != TargetType.Invalid)
					{
						var density = FiringLOS.GetGroundShadowDensity(self, delayedTarget);
						var excess = Math.Max(0, density - args.Weapon.FreeLineDensity);
						if (excess > 0)
						{
							var missPct = Math.Min(95, excess * args.Weapon.MissChancePerDensity);
							if (missPct > 0 && self.World.SharedRandom.Next(100) < missPct)
							{
								var muzzlePos = MuzzlePosition();
								var lineEnd = delayedTarget.CenterPosition;
								const int LineWidth = 512;
								var candidates = self.World.FindActorsOnLine(muzzlePos, lineEnd, new WDist(LineWidth))
									.Where(a => a != self && !a.Disposed && a.IsInWorld)
									.Where(a =>
									{
										var targetable = a.TraitsImplementing<ITargetable>()
											.FirstOrDefault(Exts.IsTraitEnabled);
										return targetable != null && targetable.TargetTypes.Contains("Trees");
									})
									.ToList();

								if (candidates.Count > 0)
								{
									// Weighted-random selection: trees closer to the firing line are more likely
									// to be the one clipped. Avoids the "the first tree always gets hit" bias of
									// a closest-to-muzzle pick and the unphysical "missile swerves to a far-off
									// tree" of a uniform-random pick.
									var weights = new int[candidates.Count];
									var totalWeight = 0;
									for (var i = 0; i < candidates.Count; i++)
									{
										var hitPos = WorldExtensions.MinimumPointLineProjection(muzzlePos, lineEnd, candidates[i].CenterPosition);
										var perpDist = (candidates[i].CenterPosition - hitPos).Length;
										weights[i] = Math.Max(1, LineWidth - perpDist);
										totalWeight += weights[i];
									}

									var roll = self.World.SharedRandom.Next(totalWeight);
									Actor treeOnLine = null;
									var cumulative = 0;
									for (var i = 0; i < candidates.Count; i++)
									{
										cumulative += weights[i];
										if (roll < cumulative)
										{
											treeOnLine = candidates[i];
											break;
										}
									}

									if (treeOnLine != null)
									{
										args.PassiveTarget = treeOnLine.CenterPosition;
										args.GuidedTarget = OpenRA.Traits.Target.Invalid;
									}
								}
							}
						}
					}

					var projectile = args.Weapon.Projectile.Create(args);
					if (projectile != null)
						self.World.Add(projectile);

					if (args.Weapon.Report != null && args.Weapon.Report.Length > 0)
						Game.Sound.Play(SoundType.World, args.Weapon.Report, self.World, self.CenterPosition);

					if (burst == args.Weapon.Burst && args.Weapon.StartBurstReport != null && args.Weapon.StartBurstReport.Length > 0)
						Game.Sound.Play(SoundType.World, args.Weapon.StartBurstReport, self.World, self.CenterPosition);

					foreach (var na in notifyAttacks)
						na.Attacking(self, delayedTarget, this, barrel);

					Recoil = Info.Recoil;
				}
			});
		}

		protected virtual void UpdateMagazine(Actor self, in Target target)
		{
			if (Weapon.ReloadDelay > 0)
			{
				if (--Magazine < 1)
				{
					ReloadDelay = Util.ApplyPercentageModifiers(Weapon.ReloadDelay, reloadModifiers);

					Magazine = Weapon.Magazine;

					foreach (var nbc in notifyMagazineComplete)
						nbc.FiredMagazine(self, target, this);
				}
			}
		}

		protected virtual void UpdateBurst(Actor self, in Target target)
		{
			if (Weapon.BurstWait > 0)
			{
				var step = BurstSequence.Advance(Burst, Weapon.Burst, Weapon.BurstDelays,
					Util.ApplyPercentageModifiers(Weapon.BurstWait, burstWaitModifiers), self.World.WorldTick);

				Burst = step.Burst;
				burstStaleTick = step.StaleTick;
				SetBurstWait(step.Wait, step.Completed);

				if (step.Completed)
				{
					ResetBurst(self);

					if (Weapon.AfterFireSound != null && Weapon.AfterFireSound.Any())
						ScheduleDelayedAction(Weapon.AfterFireSoundDelay, Burst, (burst) => Game.Sound.Play(SoundType.World, Weapon.AfterFireSound, self.World, self.CenterPosition));

					foreach (var nbc in notifyBurstComplete)
						nbc.FiredBurst(self, target, this);
				}
			}
		}

		protected virtual void ResetBurst(Actor self)
		{
			Burst = FullBurst;
			burstStaleTick = BurstSequence.NoPendingBurst;
		}

		void SetBurstWait(int delay, bool isBurstWait = false)
		{
			BurstWait = delay;
			IsBurstWait = isBurstWait;
		}

		public virtual bool IsAiming { get { return AimingDelay > 0; } }
		public virtual bool IsWaitingBurst { get { return BurstWait > 0; } }
		public virtual bool IsReloading => ReloadDelay > 0;

		public WVec MuzzleOffset(Actor self, Barrel b)
		{
			return CalculateMuzzleOffset(self, b);
		}

		protected virtual WVec CalculateMuzzleOffset(Actor self, Barrel b)
		{
			// Weapon offset in turret coordinates
			var localOffset = b.Offset + new WVec(-Recoil, WDist.Zero, WDist.Zero);

			// Turret coordinates to body coordinates
			var bodyOrientation = coords.QuantizeOrientation(self.Orientation);
			if (turret != null)
				localOffset = localOffset.Rotate(turret.WorldOrientation) + turret.Offset.Rotate(bodyOrientation);
			else
				localOffset = localOffset.Rotate(bodyOrientation);

			// Body coordinates to world coordinates
			return coords.LocalToWorld(localOffset);
		}

		public WRot MuzzleOrientation(Actor self, Barrel b)
		{
			return CalculateMuzzleOrientation(self, b);
		}

		protected virtual WRot CalculateMuzzleOrientation(Actor self, Barrel b)
		{
			return WRot.FromYaw(b.Yaw).Rotate(turret?.WorldOrientation ?? self.Orientation);
		}

		public Actor Actor => self;
	}
}
