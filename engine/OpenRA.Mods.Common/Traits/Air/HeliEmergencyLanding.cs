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
using OpenRA.GameRules;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Helicopters with this trait will autorotate on heavy damage and crash on critical damage.",
		"Heavy damage: controlled descent, player can steer, lands safely on suitable terrain.",
		"Critical damage: uncontrolled crash with optional spinning, always destroyed on impact.")]
	public class HeliEmergencyLandingInfo : TraitInfo, Requires<AircraftInfo>, Requires<IHealthInfo>, IRulesetLoaded
	{
		[Desc("Rate of altitude loss per tick during controlled autorotation descent.")]
		public readonly WDist AutorotationDescentRate = new WDist(20);

		[Desc("Rate of altitude loss per tick during uncontrolled crash.")]
		public readonly WDist CrashDescentRate = new WDist(50);

		[Desc("Forward speed during autorotation as percentage of normal Aircraft.Speed.")]
		public readonly int AutorotationSpeedPercent = 60;

		[Desc("Forward speed acceleration per tick during autorotation (WDist units).",
			"Helicopter gradually ramps up to AutorotationSpeedPercent instead of instant speed.")]
		public readonly int AutorotationAcceleration = 3;

		[Desc("Altitude above terrain at which autorotation flare begins (descent rate reduction before touchdown).",
			"Set to 0 to disable flare.")]
		public readonly WDist FlareAltitude = new WDist(512);

		[Desc("Descent rate percentage at ground level during flare. Linearly interpolated from 100% at FlareAltitude to this at ground.",
			"Lower = gentler touchdown.")]
		public readonly int FlareDescentPercent = 30;

		[Desc("Forward speed percentage at ground level during flare. Linearly interpolated from 100% at FlareAltitude to this at ground.",
			"Lower = slower approach to landing.")]
		public readonly int FlareSpeedPercent = 60;

		[Desc("Whether the helicopter spins during uncontrolled crash (tail rotor loss simulation).",
			"Set to false for dual-rotor aircraft like Chinook.")]
		public readonly bool SpinsOnCrash = true;

		[Desc("Maximum spin rate in angle units per tick during crash.")]
		public readonly int MaxSpinRate = 80;

		[Desc("Spin acceleration in angle units per tick squared.")]
		public readonly int SpinAcceleration = 4;

		[GrantedConditionReference]
		[Desc("Condition granted during controlled autorotation descent.")]
		public readonly string AutorotationCondition = "autorotation";

		[GrantedConditionReference]
		[Desc("Condition granted during uncontrolled crash descent.")]
		public readonly string CrashLandingCondition = "crash-landing";

		[GrantedConditionReference]
		[Desc("Condition granted when helicopter is disabled on the ground after a safe autorotation landing.")]
		public readonly string DisabledCondition = "crash-disabled";

		[GrantedConditionReference]
		[Desc("Condition granted to suppress crew ejection during mid-air destruction.",
			"Use this to gate EjectOnDeath with !suppress-eject.")]
		public readonly string SuppressEjectCondition = "suppress-eject";

		[GrantedConditionReference]
		[Desc("Condition granted when rotors have wound down after safe landing.")]
		public readonly string RotorStoppedCondition = "rotor-stopped";

		[Desc("Ticks after safe landing before rotors fully stop spinning.")]
		public readonly int RotorWindDownTicks = 60;

		[Desc("Terrain types where autorotation can land safely without destroying the helicopter.",
			"If empty, falls back to Aircraft.LandableTerrainTypes.")]
		public readonly HashSet<string> SuitableLandingTerrains = new HashSet<string>();

		[Desc("Whether to eject Cargo passengers on safe autorotation landing.")]
		public readonly bool EjectPassengersOnSafeLanding = true;

		[Desc("Whether to eject Cargo passengers on crash landing (terrain permitting).")]
		public readonly bool EjectPassengersOnCrash = true;

		[WeaponReference]
		[Desc("Weapon to fire on crash impact. Use for explosion effects.")]
		public readonly string CrashExplosion = "UnitExplode";

		[Desc("Damage state at which autorotation begins.")]
		public readonly DamageState AutorotationDamageState = DamageState.Heavy;

		[Desc("Damage state at which uncontrolled crash begins.")]
		public readonly DamageState CrashDamageState = DamageState.Critical;

		[Desc("Whether to transfer ownership to Neutral player after safe landing (abandoned airframe).")]
		public readonly bool TransferToNeutralOnSafeLanding = true;

		public WeaponInfo CrashExplosionWeapon { get; private set; }

		public override object Create(ActorInitializer init) { return new HeliEmergencyLanding(init.Self, this); }

		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (string.IsNullOrEmpty(CrashExplosion))
				return;

			var weaponToLower = CrashExplosion.ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out var weapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{weaponToLower}'");

			CrashExplosionWeapon = weapon;
		}
	}

	public class HeliEmergencyLanding : INotifyDamageStateChanged, INotifyCreated, INotifyKilled, IResolveOrder
	{
		public enum EmergencyState { None, Autorotation, Crashing }

		readonly HeliEmergencyLandingInfo info;
		readonly Aircraft aircraft;
		readonly IHealth health;

		public EmergencyState State { get; private set; } = EmergencyState.None;
		public bool IsDisabledOnGround => disabledToken != Actor.InvalidConditionToken;

		/// <summary>
		/// Desired facing set by player right-click during autorotation.
		/// HeliAutorotate reads this to steer the helicopter.
		/// </summary>
		public WAngle? DesiredAutorotationFacing { get; set; }

		int autorotationToken = Actor.InvalidConditionToken;
		int crashLandingToken = Actor.InvalidConditionToken;
		int disabledToken = Actor.InvalidConditionToken;
		int suppressEjectToken = Actor.InvalidConditionToken;
		int rotorStoppedToken = Actor.InvalidConditionToken;

		Cargo cargo;
		VehicleCrew vehicleCrew;
		HashSet<string> suitableTerrains;

		public HeliEmergencyLanding(Actor self, HeliEmergencyLandingInfo info)
		{
			this.info = info;
			aircraft = self.Trait<Aircraft>();
			health = self.Trait<IHealth>();
		}

		void INotifyCreated.Created(Actor self)
		{
			cargo = self.TraitOrDefault<Cargo>();
			vehicleCrew = self.TraitOrDefault<VehicleCrew>();
			suitableTerrains = info.SuitableLandingTerrains.Count > 0
				? info.SuitableLandingTerrains
				: aircraft.Info.LandableTerrainTypes;
		}

		void INotifyDamageStateChanged.DamageStateChanged(Actor self, AttackInfo e)
		{
			if (self.IsDead)
				return;

			// Escalate: autorotation → crash if damage worsens to critical
			if (State == EmergencyState.Autorotation && e.DamageState >= info.CrashDamageState)
			{
				TransitionToCrash(self);
				return;
			}

			// Start autorotation on heavy damage (only if airborne)
			if (State == EmergencyState.None && e.DamageState >= info.AutorotationDamageState
				&& e.DamageState < info.CrashDamageState && !self.IsAtGroundLevel())
			{
				StartAutorotation(self);
				return;
			}

			// Start crash on critical damage (only if airborne)
			if (State == EmergencyState.None && e.DamageState >= info.CrashDamageState && !self.IsAtGroundLevel())
			{
				StartCrash(self);
				return;
			}

			// If repaired out of heavy damage while autorotating, cancel (shouldn't happen per design, but safety)
			if (State == EmergencyState.Autorotation && e.DamageState < info.AutorotationDamageState)
			{
				CancelAutorotation(self);
				return;
			}

			// If repaired out of heavy damage while disabled on ground, allow takeoff
			CheckDisabledRecovery(self);
		}

		void StartAutorotation(Actor self)
		{
			State = EmergencyState.Autorotation;

			if (autorotationToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.AutorotationCondition))
				autorotationToken = self.GrantCondition(info.AutorotationCondition);

			// Suppress crew ejection during descent — they stay in the helicopter
			if (suppressEjectToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.SuppressEjectCondition))
				suppressEjectToken = self.GrantCondition(info.SuppressEjectCondition);

			// Cancel current activities and start autorotation
			self.CancelActivity();

			var speed = aircraft.Info.Speed * info.AutorotationSpeedPercent / 100;
			self.QueueActivity(false, new HeliAutorotate(self, this, info, aircraft, speed));
		}

		void TransitionToCrash(Actor self)
		{
			// Revoke autorotation condition
			if (autorotationToken != Actor.InvalidConditionToken)
				autorotationToken = self.RevokeCondition(autorotationToken);

			State = EmergencyState.Crashing;

			if (crashLandingToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.CrashLandingCondition))
				crashLandingToken = self.GrantCondition(info.CrashLandingCondition);

			// Cancel current activity and start crash
			self.CancelActivity();
			self.QueueActivity(false, new HeliCrashLand(self, this, info, aircraft));
		}

		void StartCrash(Actor self)
		{
			State = EmergencyState.Crashing;

			if (crashLandingToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.CrashLandingCondition))
				crashLandingToken = self.GrantCondition(info.CrashLandingCondition);

			// Suppress crew ejection during descent
			if (suppressEjectToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.SuppressEjectCondition))
				suppressEjectToken = self.GrantCondition(info.SuppressEjectCondition);

			self.CancelActivity();
			self.QueueActivity(false, new HeliCrashLand(self, this, info, aircraft));
		}

		void CancelAutorotation(Actor self)
		{
			State = EmergencyState.None;

			if (autorotationToken != Actor.InvalidConditionToken)
				autorotationToken = self.RevokeCondition(autorotationToken);

			if (suppressEjectToken != Actor.InvalidConditionToken)
				suppressEjectToken = self.RevokeCondition(suppressEjectToken);

			self.CancelActivity();
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			// If killed during descent (mid-air), the suppress-eject condition is already granted,
			// which will prevent EjectOnDeath from firing. Crew dies with the helicopter.
			// If killed on the ground after safe landing, suppress-eject was revoked, so normal behavior.
		}

		public bool IsSuitableTerrain(Actor self)
		{
			var cell = self.World.Map.CellContaining(self.CenterPosition);
			if (!self.World.Map.Contains(cell))
				return false;

			var terrainType = self.World.Map.GetTerrainInfo(cell).Type;
			return suitableTerrains.Contains(terrainType);
		}

		public void OnSafeLanding(Actor self)
		{
			// Revoke flight conditions
			if (autorotationToken != Actor.InvalidConditionToken)
				autorotationToken = self.RevokeCondition(autorotationToken);

			// Revoke suppress-eject — crew is safe now
			if (suppressEjectToken != Actor.InvalidConditionToken)
				suppressEjectToken = self.RevokeCondition(suppressEjectToken);

			State = EmergencyState.None;

			// Grant disabled condition — helicopter sits on ground, repairable
			if (disabledToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.DisabledCondition))
				disabledToken = self.GrantCondition(info.DisabledCondition);

			// Zero velocity
			aircraft.CurrentVelocity = WVec.Zero;

			// Eject crew via VehicleCrew (spawns pilot/gunner/copilot infantry with original owner)
			if (vehicleCrew != null)
			{
				vehicleCrew.EjectAllCrew();
				vehicleCrew.AllowForeignCrew = true;
			}

			// Eject passengers
			if (info.EjectPassengersOnSafeLanding && cargo != null && cargo.PassengerCount > 0)
				EjectAllPassengers(self);

			// Transfer to neutral — abandoned airframe, capturable by any pilot
			if (info.TransferToNeutralOnSafeLanding)
			{
				var neutral = self.World.Players.FirstOrDefault(p => p.InternalName == "Neutral");
				if (neutral != null)
					self.ChangeOwner(neutral);
			}
		}

		public void OnCrashImpact(Actor self)
		{
			// Fire explosion
			if (info.CrashExplosionWeapon != null)
				info.CrashExplosionWeapon.Impact(Target.FromPos(self.CenterPosition), self);

			// Critical crash = total loss. suppress-eject stays active so EjectOnDeath
			// and VehicleCrew cannot fire. Cargo passengers die inside. No survivors.
			self.Kill(self);
		}

		public void OnUnsafeLanding(Actor self)
		{
			// Landing on unsuitable terrain — helicopter destroyed, crew dead
			// suppress-eject stays active, so EjectOnDeath won't fire
			if (info.CrashExplosionWeapon != null)
				info.CrashExplosionWeapon.Impact(Target.FromPos(self.CenterPosition), self);

			self.Kill(self);
		}

		void EjectAllPassengers(Actor self)
		{
			var cell = self.World.Map.CellContaining(self.CenterPosition);
			if (!self.World.Map.Contains(cell))
				return;

			var passengers = cargo.Passengers.ToList();
			foreach (var passenger in passengers)
			{
				var positionable = passenger.TraitOrDefault<IPositionable>();
				if (positionable != null && positionable.CanEnterCell(cell, null, BlockedByActor.None))
				{
					cargo.Unload(self, passenger);
					self.World.AddFrameEndTask(w =>
					{
						positionable.SetPosition(passenger, cell);
						w.Add(passenger);
						// Nudge the ejected passenger so they scatter from the crash site
						passenger.QueueActivity(false, new Nudge(passenger));
					});
				}
			}
		}

		public void OnRotorsStopped(Actor self)
		{
			if (rotorStoppedToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.RotorStoppedCondition))
				rotorStoppedToken = self.GrantCondition(info.RotorStoppedCondition);
		}

		// Called by DamageStateChanged when helicopter is repaired out of disabled state
		public void CheckDisabledRecovery(Actor self)
		{
			if (disabledToken != Actor.InvalidConditionToken && health.DamageState < info.AutorotationDamageState)
			{
				disabledToken = self.RevokeCondition(disabledToken);

				if (rotorStoppedToken != Actor.InvalidConditionToken)
					rotorStoppedToken = self.RevokeCondition(rotorStoppedToken);

				// Helicopter recovered — no longer capturable by foreign crew
				if (vehicleCrew != null)
					vehicleCrew.AllowForeignCrew = false;
			}
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			// During autorotation, intercept Move orders and convert them to steering input
			if (State != EmergencyState.Autorotation)
				return;

			if (order.OrderString != "Move")
				return;

			if (order.Target.Type == TargetType.Invalid)
				return;

			// Calculate desired facing from current position to click target
			var delta = order.Target.CenterPosition - self.CenterPosition;
			if (delta.HorizontalLengthSquared != 0)
				DesiredAutorotationFacing = delta.Yaw;
		}
	}
}
