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

	[Desc("Manages garrison port assignments, intelligent targeting, and reload swapping for garrisoned buildings.")]
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
		public Actor Occupant;
		public Target CurrentTarget;
		public int TargetLockTicks;
		public bool PlayerOverride;
		public int SwapCooldownRemaining;

		public PortState(GarrisonPortInfo port)
		{
			Port = port;
			CurrentTarget = Target.Invalid;
		}
	}

	public class GarrisonManager : INotifyCreated, INotifyPassengerEntered, INotifyPassengerExited,
		ITick, IResolveOrder
	{
		public readonly GarrisonManagerInfo Info;
		readonly Actor self;

		public PortState[] PortStates { get; private set; }

		// Reserve passengers (loaded but not assigned to a port)
		readonly List<Actor> reservePassengers = new List<Actor>();

		Cargo cargo;
		AutoTarget autoTarget;
		int tickOffset;

		// Force attack target set by player
		Target forceTarget = Target.Invalid;
		bool hasForceTarget;

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
			autoTarget = self.TraitOrDefault<AutoTarget>();
		}

		void INotifyPassengerEntered.OnPassengerEntered(Actor self, Actor passenger)
		{
			AssignPassengerToPort(passenger);
		}

		void INotifyPassengerExited.OnPassengerExited(Actor self, Actor passenger)
		{
			RemovePassengerFromPort(passenger);
		}

		void AssignPassengerToPort(Actor passenger)
		{
			// Check if passenger has a weapon — weaponless passengers go to reserve
			var armaments = passenger.TraitsImplementing<Armament>().ToArray();
			if (armaments.Length == 0)
			{
				reservePassengers.Add(passenger);
				return;
			}

			// Find the best empty port for this passenger
			var role = GetGarrisonRole(passenger);
			var bestPort = -1;
			var bestScore = int.MinValue;

			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].Occupant != null)
					continue;

				var score = PortStates[i].Port.Priority;

				// Bonus if port prefers this role
				if (PortStates[i].Port.PreferredRoles.Count > 0 && PortStates[i].Port.PreferredRoles.Contains(role))
					score += 100;

				if (score > bestScore)
				{
					bestScore = score;
					bestPort = i;
				}
			}

			if (bestPort >= 0)
			{
				PortStates[bestPort].Occupant = passenger;
				PortStates[bestPort].CurrentTarget = Target.Invalid;
				PortStates[bestPort].TargetLockTicks = 0;
				PortStates[bestPort].PlayerOverride = false;
			}
			else
			{
				// All ports full, go to reserve
				reservePassengers.Add(passenger);
			}
		}

		void RemovePassengerFromPort(Actor passenger)
		{
			// Check ports first
			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].Occupant == passenger)
				{
					PortStates[i].Occupant = null;
					PortStates[i].CurrentTarget = Target.Invalid;
					PortStates[i].TargetLockTicks = 0;
					PortStates[i].PlayerOverride = false;

					// Auto-promote from reserve
					PromoteFromReserve(i);
					return;
				}
			}

			// Check reserve
			reservePassengers.Remove(passenger);
		}

		void PromoteFromReserve(int portIndex)
		{
			if (reservePassengers.Count == 0)
				return;

			var portInfo = PortStates[portIndex].Port;
			Actor bestReserve = null;
			var bestScore = int.MinValue;

			foreach (var r in reservePassengers)
			{
				// Only promote passengers with weapons
				if (!r.TraitsImplementing<Armament>().Any())
					continue;

				var score = 0;
				var role = GetGarrisonRole(r);

				if (portInfo.PreferredRoles.Count > 0 && portInfo.PreferredRoles.Contains(role))
					score += 100;

				// Prefer passengers with ammo
				var ammo = r.TraitsImplementing<AmmoPool>().FirstOrDefault();
				if (ammo != null && ammo.CurrentAmmoCount > 0)
					score += 50;

				if (score > bestScore)
				{
					bestScore = score;
					bestReserve = r;
				}
			}

			if (bestReserve != null)
			{
				reservePassengers.Remove(bestReserve);
				PortStates[portIndex].Occupant = bestReserve;
				PortStates[portIndex].CurrentTarget = Target.Invalid;
				PortStates[portIndex].TargetLockTicks = 0;
				PortStates[portIndex].PlayerOverride = false;
			}
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

			// Decrement swap cooldowns
			for (var i = 0; i < PortStates.Length; i++)
				if (PortStates[i].SwapCooldownRemaining > 0)
					PortStates[i].SwapCooldownRemaining--;

			// Per-port targeting (staggered)
			for (var i = 0; i < PortStates.Length; i++)
			{
				var ps = PortStates[i];
				if (ps.Occupant == null || ps.Occupant.IsDead)
				{
					if (ps.Occupant != null)
					{
						ps.Occupant = null;
						ps.CurrentTarget = Target.Invalid;
						PromoteFromReserve(i);
					}

					continue;
				}

				// Stagger target scanning across ports
				if ((tickOffset + i) % Info.TargetScanInterval != 0)
				{
					// Still decrement lock ticks even on non-scan ticks
					if (ps.TargetLockTicks > 0)
						ps.TargetLockTicks--;

					continue;
				}

				UpdatePortTarget(i);
			}

			// Reload swapping
			if (Info.ReloadSwapping)
				TryReloadSwap();
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
			ps.CurrentTarget = ScanForBestTarget(portIndex);
			ps.TargetLockTicks = Info.TargetScanInterval;
		}

		Target ScanForBestTarget(int portIndex)
		{
			var ps = PortStates[portIndex];
			if (ps.Occupant == null)
				return Target.Invalid;

			// Get max range from occupant's armaments
			var maxRange = WDist.Zero;
			var armaments = ps.Occupant.TraitsImplementing<Armament>().ToArray();
			foreach (var a in armaments)
			{
				if (a.IsTraitDisabled)
					continue;

				var range = a.MaxRange();
				if (range > maxRange)
					maxRange = range;
			}

			if (maxRange == WDist.Zero)
				return Target.Invalid;

			var pos = self.CenterPosition;
			var candidates = self.World.FindActorsInCircle(pos, maxRange)
				.Where(a => a != self && !a.IsDead && a.IsInWorld
					&& self.Owner.RelationshipWith(a.Owner).HasRelationship(PlayerRelationship.Enemy));

			var bestScore = int.MinValue;
			Target bestTarget = Target.Invalid;

			foreach (var candidate in candidates)
			{
				var target = Target.FromActor(candidate);

				// Check if in port arc
				if (!IsTargetInPortArc(portIndex, target))
					continue;

				// Check if any armament can actually hit this target
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

				var score = ScoreTarget(portIndex, candidate, target);
				if (score > bestScore)
				{
					bestScore = score;
					bestTarget = target;
				}
			}

			return bestTarget;
		}

		int ScoreTarget(int portIndex, Actor candidate, in Target target)
		{
			var ps = PortStates[portIndex];
			var role = GetGarrisonRole(ps.Occupant);
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
					// AA soldiers prefer aircraft but can shoot ground
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
					if (i == portIndex || PortStates[i].Occupant == null)
						continue;

					var otherRole = GetGarrisonRole(PortStates[i].Occupant);
					if ((otherRole == "MachineGunner" || otherRole == "General") &&
						IsTargetInPortArc(i, target))
					{
						var otherAmmo = PortStates[i].Occupant.TraitsImplementing<AmmoPool>().FirstOrDefault();
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
			if (Info.AmmoConservation)
			{
				var ammo = ps.Occupant.TraitsImplementing<AmmoPool>().FirstOrDefault();
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

			// Get body orientation for rotating port yaw
			var bodyOrientation = self.TraitOrDefault<BodyOrientation>();
			var bodyYaw = self.TraitOrDefault<IFacing>()?.Facing ?? WAngle.Zero;
			var portYaw = bodyYaw + port.Yaw;

			var leftTurn = (portYaw - targetYaw).Angle;
			var rightTurn = (targetYaw - portYaw).Angle;
			return Math.Min(leftTurn, rightTurn) <= port.Cone.Angle;
		}

		void TryReloadSwap()
		{
			for (var i = 0; i < PortStates.Length; i++)
			{
				var ps = PortStates[i];
				if (ps.Occupant == null)
					continue;

				// Check if occupant is out of ammo
				var ammoPools = ps.Occupant.TraitsImplementing<AmmoPool>().ToArray();
				if (ammoPools.Length == 0)
					continue;

				var allEmpty = ammoPools.All(a => a.CurrentAmmoCount == 0);
				if (!allEmpty)
					continue;

				// Find a reserve passenger with ammo and compatible role
				var currentRole = GetGarrisonRole(ps.Occupant);
				Actor bestReserve = null;

				foreach (var r in reservePassengers)
				{
					if (r.IsDead || !r.TraitsImplementing<Armament>().Any())
						continue;

					var reserveAmmo = r.TraitsImplementing<AmmoPool>().FirstOrDefault();
					if (reserveAmmo != null && reserveAmmo.CurrentAmmoCount == 0)
						continue;

					var reserveRole = GetGarrisonRole(r);
					if (reserveRole == currentRole || reserveRole == "General")
					{
						bestReserve = r;
						break;
					}
				}

				if (bestReserve == null)
					continue;

				// Swap
				var oldOccupant = ps.Occupant;
				ps.Occupant = bestReserve;
				ps.CurrentTarget = Target.Invalid;
				ps.TargetLockTicks = 0;
				ps.SwapCooldownRemaining = Info.SwapCooldown;

				reservePassengers.Remove(bestReserve);
				reservePassengers.Add(oldOccupant);
			}
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
					if (PortStates[i].Occupant == null)
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

		// Used by AttackGarrisoned to get all armaments from occupied ports
		public IEnumerable<Armament> GetAllArmaments()
		{
			for (var i = 0; i < PortStates.Length; i++)
			{
				if (PortStates[i].Occupant == null)
					continue;

				foreach (var a in PortStates[i].Occupant.TraitsImplementing<Armament>())
					yield return a;
			}
		}

		public IEnumerable<Actor> ReservePassengers => reservePassengers;
		public int PortCount => PortStates.Length;

		public WVec GetPortWorldOffset(int portIndex, BodyOrientation coords)
		{
			var bodyOrientation = coords.QuantizeOrientation(self.Orientation);
			return coords.LocalToWorld(PortStates[portIndex].Port.Offset.Rotate(bodyOrientation));
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			switch (order.OrderString)
			{
				case "EjectGarrisonPassenger":
				{
					var passengerID = order.ExtraData;
					var passenger = self.World.GetActorById(passengerID);
					if (passenger == null)
						return;

					// Check if passenger is in this building
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

					// Remove passenger from current position
					var fromPort = -1;
					for (var i = 0; i < PortStates.Length; i++)
					{
						if (PortStates[i].Occupant == passenger)
						{
							fromPort = i;
							break;
						}
					}

					if (fromPort >= 0)
					{
						// Swap if target port is occupied
						if (PortStates[targetPortIndex].Occupant != null)
						{
							var otherOccupant = PortStates[targetPortIndex].Occupant;
							PortStates[fromPort].Occupant = otherOccupant;
							PortStates[fromPort].CurrentTarget = Target.Invalid;
							PortStates[fromPort].TargetLockTicks = 0;
						}
						else
						{
							PortStates[fromPort].Occupant = null;
							PortStates[fromPort].CurrentTarget = Target.Invalid;
						}
					}
					else
					{
						// From reserve
						reservePassengers.Remove(passenger);

						// If target port occupied, move current occupant to reserve
						if (PortStates[targetPortIndex].Occupant != null)
							reservePassengers.Add(PortStates[targetPortIndex].Occupant);
					}

					PortStates[targetPortIndex].Occupant = passenger;
					PortStates[targetPortIndex].CurrentTarget = Target.Invalid;
					PortStates[targetPortIndex].TargetLockTicks = 0;
					PortStates[targetPortIndex].PlayerOverride = false;

					break;
				}

				case "SwapGarrisonPorts":
				{
					var srcPortIdx = (int)(order.ExtraData >> 16);
					var dstPortIdx = (int)(order.ExtraData & 0xFFFF);

					if (srcPortIdx < 0 || srcPortIdx >= PortStates.Length ||
						dstPortIdx < 0 || dstPortIdx >= PortStates.Length)
						return;

					var temp = PortStates[srcPortIdx].Occupant;
					PortStates[srcPortIdx].Occupant = PortStates[dstPortIdx].Occupant;
					PortStates[dstPortIdx].Occupant = temp;

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
