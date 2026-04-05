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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class GarrisonPortInfo
	{
		[Desc("Display name for this port.")]
		public readonly string Name = "port";

		[Desc("World-space offset from building center.")]
		public readonly WVec Offset = WVec.Zero;

		[Desc("Direction the port faces.")]
		public readonly WAngle Yaw = WAngle.Zero;

		[Desc("Firing arc half-angle each side of Yaw.")]
		public readonly WAngle Cone = new WAngle(512);

		[Desc("Higher priority ports are preferred for auto-assignment.")]
		public readonly int Priority = 0;

		[Desc("Preferred GarrisonRoles for this port (hint, not hard constraint).")]
		public readonly HashSet<string> PreferredRoles = new HashSet<string>();
	}

	[Desc("Manages garrison port assignments with shelter/port deployment model. " +
		"Soldiers enter shelter (Cargo) and deploy to ports (in-world) when targets appear.")]
	public class GarrisonManagerInfo : TraitInfo, Requires<CargoInfo>
	{
		[FieldLoader.LoadUsing(nameof(LoadPorts))]
		[Desc("Named fire ports on this building.")]
		public readonly GarrisonPortInfo[] Ports = Array.Empty<GarrisonPortInfo>();

		[Desc("Cooldown ticks after a reload swap before the swapped-out unit can be swapped back in.")]
		public readonly int SwapCooldown = 20;

		[Desc("Ticks between target re-evaluation per port.")]
		public readonly int TargetScanInterval = 8;

		[Desc("If true, AT soldiers prefer vehicles over infantry when MG soldiers can handle infantry.")]
		public readonly bool AmmoConservation = true;

		[Desc("If true, reloading port occupants are auto-swapped with reserve passengers that have ammo.")]
		public readonly bool ReloadSwapping = true;

		[Desc("Condition name to grant to soldiers deployed at ports.")]
		public readonly string GarrisonedCondition = "garrisoned-at-port";

		[Desc("Ticks a deployed soldier must be idle (no valid target) before being recalled to shelter. 0 disables.")]
		public readonly int IdleRecallTicks = 125;

		[Desc("If true, building cannot be destroyed — HP is clamped to 1 minimum. " +
			"At 1 HP the building shows its damaged sprite and provides minimal cover.")]
		public readonly bool Indestructible = true;

		[Desc("If true, building changes owner to the garrisoning player on entry " +
			"and reverts to neutral when all soldiers leave.")]
		public readonly bool DynamicOwnership = true;

		[Desc("Condition name to read suppression level from deployed soldiers.")]
		public readonly string SuppressionCondition = "suppressed";

		[Desc("Suppression level at which a port soldier starts ducking (reduced fire). 0 disables.")]
		public readonly int SuppressionDuckThreshold = 30;

		[Desc("Suppression level at which a port soldier is forced to recall to shelter. 0 disables.")]
		public readonly int SuppressionRecallThreshold = 60;

		[Desc("Ticks a port stays locked after a suppression recall, preventing immediate redeployment.")]
		public readonly int SuppressionLockoutTicks = 50;

		static object LoadPorts(MiniYaml yaml)
		{
			var ports = new List<GarrisonPortInfo>();
			var dict = yaml.ToDictionary();
			if (dict.ContainsKey("Ports"))
			{
				foreach (var node in dict["Ports"].Nodes)
				{
					var port = new GarrisonPortInfo();
					FieldLoader.Load(port, node.Value);
					ports.Add(port);
				}
			}

			return ports.ToArray();
		}

		public override object Create(ActorInitializer init) { return new GarrisonManager(init.Self, this); }
	}

	public class PortState
	{
		public readonly GarrisonPortInfo Port;

		// Deployed soldier: in-world at port position (sprite hidden, pips visible, targetable)
		public Actor DeployedSoldier;

		// Cached armaments of deployed soldier (set on deploy, cleared on recall)
		public Armament[] CachedArmaments;

		// Condition token for the garrisoned-at-port condition on the deployed soldier
		public int ConditionToken = Actor.InvalidConditionToken;

		public Target CurrentTarget;
		public int TargetLockTicks;
		public bool PlayerOverride;
		public int SwapCooldownRemaining;
		public int IdleTicks;

		// Suppression lockout: ticks remaining before this port accepts redeployment after suppression recall
		public int SuppressionLockoutRemaining;

		// True if soldier is currently "ducking" due to medium suppression (still deployed but fire-impaired)
		public bool IsDucking;

		public PortState(GarrisonPortInfo port)
		{
			Port = port;
			CurrentTarget = Target.Invalid;
		}
	}

	public class GarrisonManager : INotifyCreated, INotifyPassengerEntered, INotifyPassengerExited,
		ITick, IResolveOrder, INotifyKilled, INotifyDamage, IDamageModifier
	{
		public readonly GarrisonManagerInfo Info;
		readonly Actor self;

		public PortState[] PortStates { get; private set; }

		// Shelter passengers: soldiers inside Cargo waiting to deploy
		readonly List<Actor> shelterPassengers = new List<Actor>();

		Cargo cargo;
		Health health;
		AutoTarget autoTarget;
		BodyOrientation cachedBodyOrientation;
		int tickOffset;

		// Suppress flag: prevents OnPassengerEntered/Exited from running during internal transitions
		bool suppressNotifications;

		// Force attack target set by player
		Target forceTarget = Target.Invalid;
		bool hasForceTarget;

		// Ambush state: tracks whether garrison has been triggered out of ambush stance
		bool ambushTriggered;

		// Dynamic ownership: track neutral player for revert-on-empty
		Player neutralPlayer;

		public GarrisonManager(Actor self, GarrisonManagerInfo info)
		{
			this.self = self;
			Info = info;

			PortStates = new PortState[info.Ports.Length];
			for (var i = 0; i < info.Ports.Length; i++)
				PortStates[i] = new PortState(info.Ports[i]);
		}

		void INotifyCreated.Created(Actor self)
		{
			cargo = self.Trait<Cargo>();
			health = self.TraitOrDefault<Health>();
			autoTarget = self.TraitOrDefault<AutoTarget>();
			cachedBodyOrientation = self.Trait<BodyOrientation>();

			if (Info.DynamicOwnership)
				neutralPlayer = self.World.Players.FirstOrDefault(p => p.InternalName == "Neutral");
		}

		void INotifyPassengerEntered.OnPassengerEntered(Actor self, Actor passenger)
		{
			if (suppressNotifications)
				return;

			// New passenger enters → goes to shelter
			shelterPassengers.Add(passenger);

			// Dynamic ownership: claim building for the entering soldier's owner
			if (Info.DynamicOwnership && neutralPlayer != null)
			{
				var passengerOwner = passenger.Owner;
				if (self.Owner == neutralPlayer || self.Owner.InternalName == "Neutral")
					self.ChangeOwner(passengerOwner);
			}
		}

		void INotifyPassengerExited.OnPassengerExited(Actor self, Actor passenger)
		{
			if (suppressNotifications)
				return;

			// Player-initiated eject: check if soldier was deployed at a port
			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].DeployedSoldier == passenger)
				{
					// Soldier was at port but is being ejected via Cargo
					// Revoke condition so they become normal infantry
					RevokePortCondition(i);
					PortStates[i].DeployedSoldier = null;
					PortStates[i].CurrentTarget = Target.Invalid;
					PortStates[i].TargetLockTicks = 0;
					PortStates[i].PlayerOverride = false;
					CheckOwnershipAfterExit();
					return;
				}
			}

			// Was in shelter
			shelterPassengers.Remove(passenger);
			CheckOwnershipAfterExit();
		}

		/// <summary>
		/// After a soldier exits or dies, check if we need to revert ownership to neutral
		/// or transfer to another allied player still inside.
		/// </summary>
		void CheckOwnershipAfterExit()
		{
			if (!Info.DynamicOwnership || neutralPlayer == null)
				return;

			// Collect all living soldiers (port + shelter)
			var remainingOwners = new HashSet<Player>();
			foreach (var ps in PortStates)
			{
				if (ps.DeployedSoldier != null && !ps.DeployedSoldier.IsDead)
					remainingOwners.Add(ps.DeployedSoldier.Owner);
			}

			foreach (var s in shelterPassengers)
			{
				if (!s.IsDead)
					remainingOwners.Add(s.Owner);
			}

			if (remainingOwners.Count == 0)
			{
				// No soldiers left → revert to neutral
				if (self.Owner != neutralPlayer)
					self.ChangeOwner(neutralPlayer);
			}
			else if (!remainingOwners.Contains(self.Owner))
			{
				// Current owner has no soldiers left, but an ally does → transfer
				self.ChangeOwner(remainingOwners.First());
			}
		}

		// Deploy a shelter soldier to a port (shelter → port)
		void DeployToPort(int portIndex, Actor soldier)
		{
			// Remove from shelter list
			shelterPassengers.Remove(soldier);

			// Remove from Cargo (suppress notifications so we don't trigger OnPassengerExited)
			suppressNotifications = true;
			cargo.Unload(self, soldier);
			suppressNotifications = false;

			// Add to world at port position
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead || soldier.IsDead)
					return;

				// Grant garrisoned condition BEFORE adding to world to prevent
				// one-frame visual glitches (parachute, wrong animation, etc)
				PortStates[portIndex].ConditionToken = soldier.GrantCondition(Info.GarrisonedCondition);

				// Set port info on occupant trait for directional targetability
				var occupant = soldier.TraitOrDefault<GarrisonPortOccupant>();
				occupant?.SetPort(self, portIndex);

				// Position at port: use building cell for pathfinding, port offset for visual
				var coords = cachedBodyOrientation;
				var portOffset = GetPortWorldOffset(portIndex, coords);

				// Clamp Z to terrain level to prevent the engine thinking soldier is airborne
				var terrainZ = self.World.Map.CenterOfCell(self.Location).Z;
				var portWorldPos = self.CenterPosition + portOffset;
				portWorldPos = new WPos(portWorldPos.X, portWorldPos.Y, terrainZ);

				var positionable = soldier.Trait<IPositionable>();
				positionable.SetPosition(soldier, self.Location);
				positionable.SetCenterPosition(soldier, portWorldPos);

				w.Add(soldier);
			});

			// Assign to port
			PortStates[portIndex].DeployedSoldier = soldier;
			PortStates[portIndex].CachedArmaments = soldier.TraitsImplementing<Armament>().ToArray();
			PortStates[portIndex].CurrentTarget = Target.Invalid;
			PortStates[portIndex].TargetLockTicks = 0;
			PortStates[portIndex].PlayerOverride = false;
			PortStates[portIndex].IdleTicks = 0;
		}

		// Recall a deployed soldier from port to shelter (port → shelter)
		void RecallToShelter(int portIndex)
		{
			var soldier = PortStates[portIndex].DeployedSoldier;
			if (soldier == null || soldier.IsDead)
				return;

			// Clear port assignment
			var soldierRef = soldier;
			PortStates[portIndex].DeployedSoldier = null;
			PortStates[portIndex].CurrentTarget = Target.Invalid;
			PortStates[portIndex].TargetLockTicks = 0;
			PortStates[portIndex].PlayerOverride = false;

			// Revoke garrisoned condition
			RevokePortCondition(portIndex);

			// Remove from world and add back to Cargo
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead || soldierRef.IsDead)
					return;

				w.Remove(soldierRef);

				// Add back to Cargo (suppress notifications)
				suppressNotifications = true;
				cargo.Load(self, soldierRef);
				suppressNotifications = false;

				// Add to shelter list
				shelterPassengers.Add(soldierRef);
			});
		}

		void RevokePortCondition(int portIndex)
		{
			var token = PortStates[portIndex].ConditionToken;
			var soldier = PortStates[portIndex].DeployedSoldier;
			if (soldier != null && !soldier.IsDead)
			{
				// Clear port info for directional targetability
				var occupant = soldier.TraitOrDefault<GarrisonPortOccupant>();
				occupant?.ClearPort();

				if (token != Actor.InvalidConditionToken && soldier.TokenValid(token))
				{
					soldier.RevokeCondition(token);
					PortStates[portIndex].ConditionToken = Actor.InvalidConditionToken;
				}
			}
		}

		// Find the best shelter soldier to deploy at a given port for a given target
		Actor FindBestShelterSoldier(int portIndex, in Target target)
		{
			Actor bestSoldier = null;
			var bestScore = int.MinValue;

			foreach (var soldier in shelterPassengers)
			{
				if (soldier.IsDead)
					continue;

				// Must have weapons
				var armaments = soldier.TraitsImplementing<Armament>().ToArray();
				if (armaments.Length == 0)
					continue;

				// Must have ammo
				var ammo = soldier.TraitsImplementing<AmmoPool>().FirstOrDefault();
				if (ammo != null && ammo.CurrentAmmoCount == 0)
					continue;

				// Check if weapon is valid against this target
				var canHit = false;
				foreach (var a in armaments)
				{
					if (!a.IsTraitDisabled && a.Weapon.IsValidAgainst(target, self.World, self))
					{
						canHit = true;
						break;
					}
				}

				if (!canHit)
					continue;

				var score = ScoreSoldierForDeployment(soldier, portIndex, target);
				if (score > bestScore)
				{
					bestScore = score;
					bestSoldier = soldier;
				}
			}

			return bestSoldier;
		}

		int ScoreSoldierForDeployment(Actor soldier, int portIndex, in Target target)
		{
			var score = 1000;
			var role = GetGarrisonRole(soldier);
			var portInfo = PortStates[portIndex].Port;

			// Role preference for this port
			if (portInfo.PreferredRoles.Count > 0 && portInfo.PreferredRoles.Contains(role))
				score += 100;

			// Weapon effectiveness vs target type
			if (target.Type == TargetType.Actor)
			{
				var candidate = target.Actor;
				var targetTypes = candidate.GetEnabledTargetTypes();
				var isInfantry = targetTypes.Overlaps(new BitSet<TargetableType>("Infantry"));
				var isVehicle = targetTypes.Overlaps(new BitSet<TargetableType>("Vehicle"))
					|| targetTypes.Overlaps(new BitSet<TargetableType>("Heavy"));

				switch (role)
				{
					case "AntiTank":
						if (isVehicle) score += 500;
						if (isInfantry) score -= 300;
						break;
					case "MachineGunner":
						if (isInfantry) score += 400;
						if (isVehicle) score -= 200;
						break;
					case "Sniper":
						if (isInfantry) score += 300;
						break;
				}
			}

			// Prefer soldiers with more ammo
			var ammo = soldier.TraitsImplementing<AmmoPool>().FirstOrDefault();
			if (ammo != null)
				score += ammo.CurrentAmmoCount * 20;

			return score;
		}

		public static string GetGarrisonRole(Actor passenger)
		{
			var pi = passenger.Info.TraitInfoOrDefault<PassengerInfo>();
			if (pi != null && !string.IsNullOrEmpty(pi.GarrisonRole))
				return pi.GarrisonRole;

			return "General";
		}

		void ITick.Tick(Actor self)
		{
			tickOffset++;

			// Check building's fire discipline stance
			var buildingStance = autoTarget?.Stance ?? UnitStance.FireAtWill;

			// Decrement swap cooldowns and suppression lockouts
			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].SwapCooldownRemaining > 0)
					PortStates[i].SwapCooldownRemaining--;
				if (PortStates[i].SuppressionLockoutRemaining > 0)
					PortStates[i].SuppressionLockoutRemaining--;
			}

			// Per-port management: deploy/recall/target
			for (var i = 0; i < PortStates.Length; i++)
			{
				var ps = PortStates[i];

				// Handle dead deployed soldiers
				if (ps.DeployedSoldier != null && ps.DeployedSoldier.IsDead)
				{
					ps.DeployedSoldier = null;
					ps.ConditionToken = Actor.InvalidConditionToken;
					ps.CurrentTarget = Target.Invalid;
					ps.TargetLockTicks = 0;
					ps.PlayerOverride = false;

					// Try to promote a shelter soldier to this port
					PromoteFromShelter(i);

					// Check if ownership needs to change after soldier death
					CheckOwnershipAfterExit();
					continue;
				}

				// Update deployed soldier position each tick (in case building moves/rotates)
				if (ps.DeployedSoldier != null)
				{
					var coords = cachedBodyOrientation;
					var portOffset = GetPortWorldOffset(i, coords);
					var terrainZ = self.World.Map.CenterOfCell(self.Location).Z;
					var portWorldPos = self.CenterPosition + portOffset;
					portWorldPos = new WPos(portWorldPos.X, portWorldPos.Y, terrainZ);
					var positionable = ps.DeployedSoldier.Trait<IPositionable>();
					positionable.SetCenterPosition(ps.DeployedSoldier, portWorldPos);
				}

				// Check suppression level of deployed soldier
				if (ps.DeployedSoldier != null && Info.SuppressionRecallThreshold > 0)
				{
					var suppressionLevel = ps.DeployedSoldier.GetConditionCount(Info.SuppressionCondition);

					if (suppressionLevel >= Info.SuppressionRecallThreshold)
					{
						// Pinned: force recall and lock the port
						ps.SuppressionLockoutRemaining = Info.SuppressionLockoutTicks;
						ps.IsDucking = false;
						RecallToShelter(i);
						continue;
					}

					ps.IsDucking = Info.SuppressionDuckThreshold > 0 && suppressionLevel >= Info.SuppressionDuckThreshold;
				}
				else if (ps.DeployedSoldier != null)
				{
					ps.IsDucking = false;
				}

				// Stagger target scanning across ports
				if ((tickOffset + i) % Info.TargetScanInterval != 0)
				{
					if (ps.TargetLockTicks > 0)
						ps.TargetLockTicks--;
					continue;
				}

				// Port has a deployed soldier — update its target
				if (ps.DeployedSoldier != null)
				{
					UpdatePortTarget(i);

					if (ps.CurrentTarget.IsValidFor(self))
					{
						// Active target — reset idle timer
						ps.IdleTicks = 0;
					}
					else
					{
						// No valid target — increment idle timer and recall if threshold reached
						ps.IdleTicks += Info.TargetScanInterval;
						if (Info.IdleRecallTicks > 0 && ps.IdleTicks >= Info.IdleRecallTicks)
						{
							RecallToShelter(i);
							continue;
						}

						// Reload swap: if also out of ammo, swap with shelter soldier that has ammo
						if (Info.ReloadSwapping)
						{
							var hasAmmo = false;
							var hasAmmoPools = false;
							foreach (var ap in ps.DeployedSoldier.TraitsImplementing<AmmoPool>())
							{
								hasAmmoPools = true;
								if (ap.CurrentAmmoCount > 0)
								{
									hasAmmo = true;
									break;
								}
							}

							if (hasAmmoPools && !hasAmmo)
							{
								var replacement = FindBestShelterSoldier(i, Target.Invalid);
								if (replacement != null)
									RecallToShelter(i);
							}
						}
					}
				}
				else
				{
					// Port is empty — skip if locked out by suppression
					if (ps.SuppressionLockoutRemaining > 0)
						continue;

					// Respect fire discipline stance before auto-deploying
					if (buildingStance == UnitStance.HoldFire)
						continue;

					if (buildingStance == UnitStance.Ambush && !ambushTriggered)
						continue;

					// Check if there's a target that warrants deployment
					var target = ScanForTarget(i);
					if (target.IsValidFor(self))
					{
						var soldier = FindBestShelterSoldier(i, target);
						if (soldier != null)
						{
							DeployToPort(i, soldier);
							ps.CurrentTarget = target;
							ps.TargetLockTicks = Info.TargetScanInterval * 2;
						}
					}
				}
			}

			// Ambush reset: when all ports have been recalled and ambush was triggered,
			// reset so garrison returns to hidden standby state
			if (buildingStance == UnitStance.Ambush && ambushTriggered)
			{
				var anyDeployed = false;
				for (var i = 0; i < PortStates.Length; i++)
				{
					if (PortStates[i].DeployedSoldier != null)
					{
						anyDeployed = true;
						break;
					}
				}

				if (!anyDeployed)
					ambushTriggered = false;
			}
		}

		// Promote the best shelter soldier to fill an empty port
		void PromoteFromShelter(int portIndex)
		{
			// Respect fire discipline stance
			var buildingStance = autoTarget?.Stance ?? UnitStance.FireAtWill;
			if (buildingStance == UnitStance.HoldFire)
				return;

			if (buildingStance == UnitStance.Ambush && !ambushTriggered)
				return;

			// Scan for a target first — only deploy if there's something to shoot at
			var target = ScanForTarget(portIndex);
			if (!target.IsValidFor(self))
				return;

			var soldier = FindBestShelterSoldier(portIndex, target);
			if (soldier != null)
			{
				DeployToPort(portIndex, soldier);
				PortStates[portIndex].CurrentTarget = target;
				PortStates[portIndex].TargetLockTicks = Info.TargetScanInterval * 2;
			}
		}

		void UpdatePortTarget(int portIndex)
		{
			var ps = PortStates[portIndex];

			// Player override persists until target dies or goes out of range
			if (ps.PlayerOverride)
			{
				if (ps.CurrentTarget.IsValidFor(self))
					return;

				ps.PlayerOverride = false;
			}

			// Force attack from AttackGarrisoned
			if (hasForceTarget && forceTarget.IsValidFor(self))
			{
				if (IsTargetInPortArc(portIndex, forceTarget))
				{
					ps.CurrentTarget = forceTarget;
					ps.TargetLockTicks = Info.TargetScanInterval;
					ps.PlayerOverride = true;
					return;
				}
			}

			// If target lock is active and target still valid, keep it
			if (ps.TargetLockTicks > 0 && ps.CurrentTarget.IsValidFor(self))
				return;

			// Scan for best target
			ps.CurrentTarget = ScanForTarget(portIndex);
			ps.TargetLockTicks = Info.TargetScanInterval;
		}

		// Scan for best target visible from a port. Uses deployed soldier's armaments if present,
		// otherwise checks what shelter soldiers could handle.
		Target ScanForTarget(int portIndex)
		{
			// Determine max range from the deployed soldier or from shelter soldiers
			var maxRange = WDist.Zero;
			Armament[] armaments = null;

			var ps = PortStates[portIndex];
			if (ps.DeployedSoldier != null && !ps.DeployedSoldier.IsDead)
			{
				armaments = ps.CachedArmaments;
				foreach (var a in armaments)
				{
					if (a.IsTraitDisabled)
						continue;
					var range = a.MaxRange();
					if (range > maxRange)
						maxRange = range;
				}
			}
			else
			{
				// For empty ports, estimate range from shelter soldiers
				foreach (var soldier in shelterPassengers)
				{
					if (soldier.IsDead)
						continue;

					foreach (var a in soldier.TraitsImplementing<Armament>())
					{
						if (a.IsTraitDisabled)
							continue;
						var range = a.MaxRange();
						if (range > maxRange)
							maxRange = range;
					}
				}
			}

			if (maxRange == WDist.Zero)
				return Target.Invalid;

			var pos = self.CenterPosition;
			var candidates = self.World.FindActorsInCircle(pos, maxRange)
				.Where(a => a != self && !a.IsDead && a.IsInWorld
					&& !IsDeployedSoldierOf(a)
					&& self.Owner.RelationshipWith(a.Owner).HasRelationship(PlayerRelationship.Enemy));

			var bestScore = int.MinValue;
			Target bestTarget = Target.Invalid;

			foreach (var candidate in candidates)
			{
				var target = Target.FromActor(candidate);

				// Check if in port arc
				if (!IsTargetInPortArc(portIndex, target))
					continue;

				// If we have a deployed soldier, check weapon validity
				if (armaments != null)
				{
					var canHit = false;
					foreach (var a in armaments)
					{
						if (!a.IsTraitDisabled && a.Weapon.IsValidAgainst(target, self.World, self))
						{
							canHit = true;
							break;
						}
					}

					if (!canHit)
						continue;
				}

				var score = ScoreTarget(portIndex, candidate, target);
				if (score > bestScore)
				{
					bestScore = score;
					bestTarget = target;
				}
			}

			return bestTarget;
		}

		// Check if an actor is one of our deployed soldiers (to avoid targeting them)
		bool IsDeployedSoldierOf(Actor a)
		{
			for (var i = 0; i < PortStates.Length; i++)
				if (PortStates[i].DeployedSoldier == a)
					return true;
			return false;
		}

		int ScoreTarget(int portIndex, Actor candidate, in Target target)
		{
			var ps = PortStates[portIndex];
			var occupant = ps.DeployedSoldier;
			var role = occupant != null ? GetGarrisonRole(occupant) : "General";
			var score = 1000;

			// Weapon effectiveness based on role vs target type
			var targetTypes = candidate.GetEnabledTargetTypes();
			var isInfantry = targetTypes.Overlaps(new BitSet<TargetableType>("Infantry"));
			var isVehicle = targetTypes.Overlaps(new BitSet<TargetableType>("Vehicle"))
				|| targetTypes.Overlaps(new BitSet<TargetableType>("Heavy"));

			switch (role)
			{
				case "AntiTank":
					if (isVehicle) score += 500;
					if (isInfantry) score -= 300;
					break;
				case "MachineGunner":
					if (isInfantry) score += 400;
					if (isVehicle) score -= 200;
					break;
				case "AntiAir":
					break;
				case "Sniper":
					if (isInfantry) score += 300;
					break;
			}

			// Ammo conservation: AT soldiers avoid infantry when MG soldiers can handle it
			if (Info.AmmoConservation && role == "AntiTank" && isInfantry)
			{
				var hasMGInPort = false;
				for (var i = 0; i < PortStates.Length; i++)
				{
					if (i == portIndex || PortStates[i].DeployedSoldier == null)
						continue;

					var otherRole = GetGarrisonRole(PortStates[i].DeployedSoldier);
					if ((otherRole == "MachineGunner" || otherRole == "General") &&
						IsTargetInPortArc(i, target))
					{
						var otherAmmo = PortStates[i].DeployedSoldier.TraitsImplementing<AmmoPool>().FirstOrDefault();
						if (otherAmmo == null || otherAmmo.CurrentAmmoCount > 0)
						{
							hasMGInPort = true;
							break;
						}
					}
				}

				if (hasMGInPort)
					score -= 500;
			}

			// Low ammo penalty
			if (Info.AmmoConservation && occupant != null)
			{
				var ammo = occupant.TraitsImplementing<AmmoPool>().FirstOrDefault();
				if (ammo != null && ammo.CurrentAmmoCount <= 1)
					score -= 200;
			}

			// Overkill prevention: reduce score if other ports already targeting this
			var portsTargeting = 0;
			for (var i = 0; i < PortStates.Length; i++)
			{
				if (i == portIndex)
					continue;

				if (PortStates[i].CurrentTarget.Type == TargetType.Actor &&
					target.Type == TargetType.Actor &&
					PortStates[i].CurrentTarget.Actor == target.Actor)
					portsTargeting++;
			}

			if (portsTargeting >= 1)
				score -= 400 * portsTargeting;

			// Range preference (closer = better)
			var distance = (candidate.CenterPosition - self.CenterPosition).Length;
			score -= distance / 100;

			// Threat priority: target attacking this building
			var attackBases = candidate.TraitsImplementing<AttackBase>();
			foreach (var ab in attackBases)
			{
				if (ab.IsAiming)
				{
					score += 300;
					break;
				}
			}

			// Finish off wounded targets
			var health = candidate.TraitOrDefault<Health>();
			if (health != null && health.DamageState >= DamageState.Heavy)
				score += 200;

			return score;
		}

		public bool IsTargetInPortArc(int portIndex, in Target target)
		{
			if (!target.IsValidFor(self))
				return false;

			var port = PortStates[portIndex].Port;
			var pos = self.CenterPosition;
			var targetPos = target.CenterPosition;
			var delta = targetPos - pos;

			if (delta.HorizontalLengthSquared == 0)
				return true;

			var targetYaw = delta.Yaw;

			var bodyYaw = self.TraitOrDefault<IFacing>()?.Facing ?? WAngle.Zero;
			var portYaw = bodyYaw + port.Yaw;

			var leftTurn = (portYaw - targetYaw).Angle;
			var rightTurn = (targetYaw - portYaw).Angle;
			return Math.Min(leftTurn, rightTurn) <= port.Cone.Angle;
		}

		// Called by AttackGarrisoned when player issues force-attack
		public void SetForceTarget(in Target target)
		{
			forceTarget = target;
			hasForceTarget = target.IsValidFor(self);

			if (hasForceTarget)
			{
				for (var i = 0; i < PortStates.Length; i++)
				{
					if (PortStates[i].DeployedSoldier == null)
					{
						// Try to deploy a shelter soldier for this target
						if (IsTargetInPortArc(i, target))
						{
							var soldier = FindBestShelterSoldier(i, target);
							if (soldier != null)
								DeployToPort(i, soldier);
						}
					}

					if (PortStates[i].DeployedSoldier == null)
						continue;

					if (IsTargetInPortArc(i, target))
					{
						PortStates[i].CurrentTarget = target;
						PortStates[i].PlayerOverride = true;
						PortStates[i].TargetLockTicks = 0;
					}
				}
			}
		}

		public void ClearForceTarget()
		{
			forceTarget = Target.Invalid;
			hasForceTarget = false;

			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].PlayerOverride)
				{
					PortStates[i].PlayerOverride = false;
					PortStates[i].TargetLockTicks = 0;
				}
			}
		}

		// Used by AttackGarrisoned to get all armaments from deployed port soldiers
		public IEnumerable<Armament> GetAllArmaments()
		{
			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].DeployedSoldier == null || PortStates[i].DeployedSoldier.IsDead)
					continue;

				foreach (var a in PortStates[i].DeployedSoldier.TraitsImplementing<Armament>())
					yield return a;
			}
		}

		// Returns armaments from both deployed AND shelter soldiers.
		// Used by AttackGarrisoned for cursor/validity checks so force-attack works when soldiers are in shelter.
		public IEnumerable<Armament> GetAllPotentialArmaments()
		{
			foreach (var a in GetAllArmaments())
				yield return a;

			foreach (var soldier in shelterPassengers)
			{
				if (soldier.IsDead)
					continue;

				foreach (var a in soldier.TraitsImplementing<Armament>())
					yield return a;
			}
		}

		// Called by AutoTarget.TriggerNearbyAmbushAllies to coordinate garrison buildings with ambush units
		public void TriggerAmbush()
		{
			var buildingStance = autoTarget?.Stance ?? UnitStance.FireAtWill;
			if (buildingStance != UnitStance.Ambush || ambushTriggered)
				return;

			ambushTriggered = true;
			TriggerAmbushDeploy();
		}

		// Force-deploy shelter soldiers to all empty ports that have valid targets
		void TriggerAmbushDeploy()
		{
			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].DeployedSoldier != null)
					continue;

				var target = ScanForTarget(i);
				if (!target.IsValidFor(self))
					continue;

				var soldier = FindBestShelterSoldier(i, target);
				if (soldier != null)
				{
					DeployToPort(i, soldier);
					PortStates[i].CurrentTarget = target;
					PortStates[i].TargetLockTicks = Info.TargetScanInterval * 2;
				}
			}
		}

		public IEnumerable<Actor> ShelterPassengers => shelterPassengers;
		public int PortCount => PortStates.Length;

		public WVec GetPortWorldOffset(int portIndex, BodyOrientation coords)
		{
			var bodyOrientation = coords.QuantizeOrientation(self.Orientation);
			return coords.LocalToWorld(PortStates[portIndex].Port.Offset.Rotate(bodyOrientation));
		}

		// Building destroyed: deployed soldiers become free infantry, shelter soldiers ejected by Cargo
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			for (var i = 0; i < PortStates.Length; i++)
			{
				var soldier = PortStates[i].DeployedSoldier;
				if (soldier == null || soldier.IsDead)
					continue;

				// Revoke garrisoned condition — sprite appears, movement enabled
				RevokePortCondition(i);
				PortStates[i].DeployedSoldier = null;

				// They're already in world at port position, so they can scatter
				// Apply some damage from building destruction
				if (!soldier.IsDead)
				{
					var damage = e.Damage.Value;
					if (damage > 0)
					{
						var soldierHealth = soldier.TraitOrDefault<Health>();
						if (soldierHealth != null)
						{
							var damageToDeal = soldierHealth.MaxHP * damage / self.Trait<Health>().MaxHP;
							damageToDeal += self.World.SharedRandom.Next(soldierHealth.MaxHP / 5);
							soldier.InflictDamage(e.Attacker, new Damage(damageToDeal));
						}
					}
				}
			}

			// Shelter soldiers are in Cargo and will be handled by Cargo.EjectOnDeath
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (e.Damage.Value <= 0)
				return;

			var buildingStance = autoTarget?.Stance ?? UnitStance.FireAtWill;
			if (buildingStance == UnitStance.Ambush && !ambushTriggered)
			{
				ambushTriggered = true;
				TriggerAmbushDeploy();
			}
		}

		int IDamageModifier.GetDamageModifier(Actor attacker, Damage damage)
		{
			if (!Info.Indestructible || health == null || health.IsDead || damage.Value <= 0)
				return 100;

			// Already at minimum HP — block all damage
			if (health.HP <= 1)
				return 0;

			// If this damage would kill us, reduce it so we stay at 1 HP
			if (damage.Value >= health.HP)
			{
				var maxAllowedDamage = health.HP - 1;
				if (maxAllowedDamage <= 0)
					return 0;

				return maxAllowedDamage * 100 / damage.Value;
			}

			return 100;
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			switch (order.OrderString)
			{
				case "Unload":
				{
					// When building is ordered to unload, eject ALL soldiers (port + shelter)
					// Port soldiers: revoke condition, they're already in world
					for (var i = 0; i < PortStates.Length; i++)
					{
						var soldier = PortStates[i].DeployedSoldier;
						if (soldier == null || soldier.IsDead)
							continue;

						RevokePortCondition(i);
						PortStates[i].DeployedSoldier = null;
						PortStates[i].CurrentTarget = Target.Invalid;
						PortStates[i].TargetLockTicks = 0;
						PortStates[i].PlayerOverride = false;

						// Soldier is already in world at port position — just needs to scatter
					}

					// Shelter soldiers will be ejected by Cargo's normal Unload handling
					break;
				}

				case "EjectGarrisonPassenger":
				{
					var passengerID = order.ExtraData;
					var passenger = self.World.GetActorById(passengerID);
					if (passenger == null)
						return;

					// Check if passenger is deployed at a port
					for (var i = 0; i < PortStates.Length; i++)
					{
						if (PortStates[i].DeployedSoldier == passenger)
						{
							// Revoke condition and clear port
							RevokePortCondition(i);
							PortStates[i].DeployedSoldier = null;
							PortStates[i].CachedArmaments = null;
							PortStates[i].CurrentTarget = Target.Invalid;
							PortStates[i].TargetLockTicks = 0;
							PortStates[i].PlayerOverride = false;

							// Soldier is already in world, just needs to become normal infantry
							// (condition revoked above enables sprite/movement/attack)
							return;
						}
					}

					// Check if passenger is in shelter (Cargo)
					if (!cargo.Passengers.Contains(passenger))
						return;

					cargo.Unload(self, passenger);

					// Position the ejected passenger
					self.World.AddFrameEndTask(w =>
					{
						w.Add(passenger);
						var positionable = passenger.Trait<IPositionable>();
						positionable.SetPosition(passenger, self.Location);
					});

					break;
				}

				case "AssignGarrisonPort":
				{
					var passengerID = order.ExtraData;
					var portName = order.TargetString;
					var passenger = self.World.GetActorById(passengerID);
					if (passenger == null)
						return;

					// Find the target port
					var targetPortIndex = -1;
					for (var i = 0; i < PortStates.Length; i++)
					{
						if (PortStates[i].Port.Name == portName)
						{
							targetPortIndex = i;
							break;
						}
					}

					if (targetPortIndex < 0)
						return;

					// Find where the passenger currently is
					var fromPort = -1;
					for (var i = 0; i < PortStates.Length; i++)
					{
						if (PortStates[i].DeployedSoldier == passenger)
						{
							fromPort = i;
							break;
						}
					}

					if (fromPort >= 0)
					{
						// Swap deployed soldiers between ports
						if (PortStates[targetPortIndex].DeployedSoldier != null)
						{
							var otherSoldier = PortStates[targetPortIndex].DeployedSoldier;
							var otherToken = PortStates[targetPortIndex].ConditionToken;
							var otherArmaments = PortStates[targetPortIndex].CachedArmaments;
							PortStates[fromPort].DeployedSoldier = otherSoldier;
							PortStates[fromPort].CachedArmaments = otherArmaments;
							PortStates[fromPort].ConditionToken = otherToken;
							PortStates[fromPort].CurrentTarget = Target.Invalid;
							PortStates[fromPort].TargetLockTicks = 0;
						}
						else
						{
							PortStates[fromPort].DeployedSoldier = null;
							PortStates[fromPort].CachedArmaments = null;
							PortStates[fromPort].ConditionToken = Actor.InvalidConditionToken;
							PortStates[fromPort].CurrentTarget = Target.Invalid;
						}
					}
					else
					{
						// From shelter — deploy to port
						if (shelterPassengers.Contains(passenger))
						{
							// If target port occupied, recall current occupant
							if (PortStates[targetPortIndex].DeployedSoldier != null)
								RecallToShelter(targetPortIndex);

							DeployToPort(targetPortIndex, passenger);
							return;
						}
					}

					if (fromPort >= 0)
					{
						PortStates[targetPortIndex].DeployedSoldier = passenger;
						PortStates[targetPortIndex].CachedArmaments = passenger.TraitsImplementing<Armament>().ToArray();
						PortStates[targetPortIndex].ConditionToken = PortStates[fromPort >= 0 ? fromPort : targetPortIndex].ConditionToken;
						PortStates[targetPortIndex].CurrentTarget = Target.Invalid;
						PortStates[targetPortIndex].TargetLockTicks = 0;
						PortStates[targetPortIndex].PlayerOverride = false;
					}

					break;
				}

				case "SwapGarrisonPorts":
				{
					var srcPortIdx = (int)(order.ExtraData >> 16);
					var dstPortIdx = (int)(order.ExtraData & 0xFFFF);

					if (srcPortIdx < 0 || srcPortIdx >= PortStates.Length ||
						dstPortIdx < 0 || dstPortIdx >= PortStates.Length)
						return;

					var tempSoldier = PortStates[srcPortIdx].DeployedSoldier;
					var tempToken = PortStates[srcPortIdx].ConditionToken;
					PortStates[srcPortIdx].DeployedSoldier = PortStates[dstPortIdx].DeployedSoldier;
					PortStates[srcPortIdx].ConditionToken = PortStates[dstPortIdx].ConditionToken;
					PortStates[dstPortIdx].DeployedSoldier = tempSoldier;
					PortStates[dstPortIdx].ConditionToken = tempToken;

					// Reset targeting for both
					PortStates[srcPortIdx].CurrentTarget = Target.Invalid;
					PortStates[srcPortIdx].TargetLockTicks = 0;
					PortStates[dstPortIdx].CurrentTarget = Target.Invalid;
					PortStates[dstPortIdx].TargetLockTicks = 0;

					break;
				}

				case "SetGarrisonPortTarget":
				{
					var portIdx = (int)order.ExtraData;
					if (portIdx < 0 || portIdx >= PortStates.Length)
						return;

					if (order.Target.IsValidFor(self))
					{
						PortStates[portIdx].CurrentTarget = order.Target;
						PortStates[portIdx].PlayerOverride = true;
						PortStates[portIdx].TargetLockTicks = 0;
					}

					break;
				}

				case "ClearGarrisonPortTarget":
				{
					var portIdx = (int)order.ExtraData;
					if (portIdx < 0 || portIdx >= PortStates.Length)
						return;

					PortStates[portIdx].PlayerOverride = false;
					PortStates[portIdx].TargetLockTicks = 0;

					break;
				}
			}
		}
	}
}
