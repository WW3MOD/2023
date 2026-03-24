#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class FirePort
	{
		public WVec Offset;
		public WAngle Yaw;
		public WAngle Cone;
	}

	[Desc("Cargo can fire their weapons out of fire ports.")]
	public class AttackGarrisonedInfo : AttackFollowInfo, IRulesetLoaded, Requires<CargoInfo>
	{
		[Desc("Fire port offsets in local coordinates. Used as fallback when no GarrisonManager is present.")]
		public readonly WVec[] PortOffsets = null;

		[Desc("Fire port yaw angles. Used as fallback when no GarrisonManager is present.")]
		public readonly WAngle[] PortYaws = null;

		[Desc("Fire port yaw cone angle. Used as fallback when no GarrisonManager is present.")]
		public readonly WAngle[] PortCones = null;

		public FirePort[] Ports { get; private set; }

		[PaletteReference]
		public readonly string MuzzlePalette = "effect";

		public readonly bool FlashOnAttack = true;

		public override object Create(ActorInitializer init) { return new AttackGarrisoned(init.Self, this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			// If GarrisonManager is present, PortOffsets/Yaws/Cones are optional
			var hasGarrisonManager = ai.HasTraitInfo<GarrisonManagerInfo>();

			if (!hasGarrisonManager)
			{
				if (PortOffsets == null || PortOffsets.Length == 0)
					throw new YamlException("PortOffsets must have at least one entry when no GarrisonManager is present.");

				if (PortYaws == null || PortYaws.Length != PortOffsets.Length)
					throw new YamlException("PortYaws must define an angle for each port.");

				if (PortCones == null || PortCones.Length != PortOffsets.Length)
					throw new YamlException("PortCones must define an angle for each port.");
			}

			if (PortOffsets != null && PortOffsets.Length > 0)
			{
				Ports = new FirePort[PortOffsets.Length];
				for (var i = 0; i < PortOffsets.Length; i++)
				{
					Ports[i] = new FirePort
					{
						Offset = PortOffsets[i],
						Yaw = PortYaws[i],
						Cone = PortCones[i],
					};
				}
			}
			else
			{
				Ports = Array.Empty<FirePort>();
			}

			base.RulesetLoaded(rules, ai);
		}
	}

	public class AttackGarrisoned : AttackFollow, INotifyPassengerEntered, INotifyPassengerExited, IRender
	{
		public new readonly AttackGarrisonedInfo Info;
		INotifyAttack[] notifyAttacks;
		readonly Lazy<BodyOrientation> coords;
		readonly List<AnimationWithOffset> muzzles;

		// Legacy mode (no GarrisonManager): flat armament list
		readonly List<Armament> legacyArmaments;
		readonly Dictionary<Actor, IFacing> paxFacing;
		readonly Dictionary<Actor, IPositionable> paxPos;
		readonly Dictionary<Actor, RenderSprites> paxRender;

		// New mode: GarrisonManager handles targeting
		GarrisonManager garrisonManager;
		bool useGarrisonManager;

		public AttackGarrisoned(Actor self, AttackGarrisonedInfo info)
			: base(self, info)
		{
			Info = info;
			coords = Exts.Lazy(() => self.Trait<BodyOrientation>());
			muzzles = new List<AnimationWithOffset>();
			legacyArmaments = new List<Armament>();
			paxFacing = new Dictionary<Actor, IFacing>();
			paxPos = new Dictionary<Actor, IPositionable>();
			paxRender = new Dictionary<Actor, RenderSprites>();
		}

		protected override void Created(Actor self)
		{
			garrisonManager = self.TraitOrDefault<GarrisonManager>();
			useGarrisonManager = garrisonManager != null;
			base.Created(self);
		}

		protected override Func<IEnumerable<Armament>> InitializeGetArmaments(Actor self)
		{
			return () =>
			{
				if (useGarrisonManager)
					return garrisonManager.GetAllArmaments();

				return legacyArmaments;
			};
		}

		void INotifyPassengerEntered.OnPassengerEntered(Actor self, Actor passenger)
		{
			// In GarrisonManager mode, pax dictionaries are managed when soldiers deploy/recall
			// For legacy mode, track them on enter
			if (!useGarrisonManager)
			{
				paxFacing[passenger] = passenger.Trait<IFacing>();
				paxPos[passenger] = passenger.Trait<IPositionable>();
				paxRender[passenger] = passenger.Trait<RenderSprites>();

				legacyArmaments.AddRange(
					passenger.TraitsImplementing<Armament>()
					.Where(a => Info.Armaments.Contains(a.Info.Name)));
			}
		}

		void INotifyPassengerExited.OnPassengerExited(Actor self, Actor passenger)
		{
			if (!useGarrisonManager)
			{
				paxFacing.Remove(passenger);
				paxPos.Remove(passenger);
				paxRender.Remove(passenger);
				legacyArmaments.RemoveAll(a => a.Actor == passenger);
			}
		}

		// Ensure pax dictionaries have entries for a deployed soldier
		void EnsurePaxTracking(Actor soldier)
		{
			if (!paxFacing.ContainsKey(soldier))
				paxFacing[soldier] = soldier.Trait<IFacing>();
			if (!paxPos.ContainsKey(soldier))
				paxPos[soldier] = soldier.Trait<IPositionable>();
			if (!paxRender.ContainsKey(soldier))
				paxRender[soldier] = soldier.Trait<RenderSprites>();
		}

		FirePort SelectFirePort(Actor self, WAngle targetYaw)
		{
			// Legacy mode only
			if (Info.Ports == null || Info.Ports.Length == 0)
				return null;

			var bodyYaw = facing != null ? facing.Facing : WAngle.Zero;
			var indices = Enumerable.Range(0, Info.Ports.Length).Shuffle(self.World.SharedRandom);
			foreach (var i in indices)
			{
				var yaw = bodyYaw + Info.Ports[i].Yaw;
				var leftTurn = (yaw - targetYaw).Angle;
				var rightTurn = (targetYaw - yaw).Angle;
				if (Math.Min(leftTurn, rightTurn) <= Info.Ports[i].Cone.Angle)
					return Info.Ports[i];
			}

			return null;
		}

		WVec PortOffset(Actor self, FirePort p)
		{
			var bodyOrientation = coords.Value.QuantizeOrientation(self.Orientation);
			return coords.Value.LocalToWorld(p.Offset.Rotate(bodyOrientation));
		}

		WVec GarrisonPortOffset(int portIndex)
		{
			return garrisonManager.GetPortWorldOffset(portIndex, coords.Value);
		}

		protected override void Tick(Actor self)
		{
			// Always call base.Tick for AttackFollow target management and AttackBase aiming notifications.
			// In GarrisonManager mode, DoAttack is a no-op so base.Tick's DoAttack calls are harmless.
			base.Tick(self);

			if (useGarrisonManager)
			{
				// Forward force-attack target to GarrisonManager
				if (RequestedTarget.IsValidFor(self))
					garrisonManager.SetForceTarget(RequestedTarget);
				else
					garrisonManager.ClearForceTarget();

				// Per-port firing (independent of AttackFollow's single-target system)
				DoGarrisonedAttack(self);

				// Override IsAiming based on whether any port has an active target
				for (var i = 0; i < garrisonManager.PortStates.Length; i++)
				{
					var ps = garrisonManager.PortStates[i];
					if (ps.DeployedSoldier != null && ps.CurrentTarget.IsValidFor(self))
					{
						IsAiming = true;
						break;
					}
				}
			}

			// Tick muzzle animations
			foreach (var m in muzzles.ToArray())
				m.Animation.Tick();
		}

		void DoGarrisonedAttack(Actor self)
		{
			if (!self.IsInWorld || IsTraitDisabled || IsTraitPaused)
				return;

			var pos = self.CenterPosition;

			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var ps = garrisonManager.PortStates[i];
				if (ps.DeployedSoldier == null || ps.DeployedSoldier.IsDead)
					continue;

				if (!ps.CurrentTarget.IsValidFor(self))
					continue;

				var target = ps.CurrentTarget;
				var targetedPosition = GetTargetPosition(pos, target);
				var targetYaw = (targetedPosition - pos).Yaw;

				// Check if target is in port's firing arc
				if (!garrisonManager.IsTargetInPortArc(i, target))
					continue;

				var portOffset = GarrisonPortOffset(i);

				// Ensure we're tracking this deployed soldier
				EnsurePaxTracking(ps.DeployedSoldier);

				if (!paxFacing.ContainsKey(ps.DeployedSoldier) || !paxPos.ContainsKey(ps.DeployedSoldier))
					continue;

				paxFacing[ps.DeployedSoldier].Facing = targetYaw;
				paxPos[ps.DeployedSoldier].SetCenterPosition(ps.DeployedSoldier, pos + portOffset);

				// Fire each of the occupant's armaments
				foreach (var a in ps.DeployedSoldier.TraitsImplementing<Armament>())
				{
					if (a.IsTraitDisabled)
						continue;

					// Check if weapon is valid against this target
					if (!a.Weapon.IsValidAgainst(target, self.World, self))
						continue;

					// Check range
					if (!target.IsInRange(pos, a.MaxRange()))
						continue;

					var barrel = a.CheckFire(a.Actor, facing, target);
					if (barrel == null)
						continue;

					if (a.Info.MuzzleSequence != null && paxRender.ContainsKey(ps.DeployedSoldier))
					{
						var muzzleAnim = new Animation(self.World, paxRender[ps.DeployedSoldier].GetImage(ps.DeployedSoldier), () => targetYaw);
						var sequence = a.Info.MuzzleSequence;
						var muzzleFlash = new AnimationWithOffset(muzzleAnim,
							() => portOffset,
							() => false,
							p => RenderUtils.ZOffsetFromCenter(self, p, 1024));

						muzzles.Add(muzzleFlash);
						muzzleAnim.PlayThen(sequence, () => muzzles.Remove(muzzleFlash));
					}

					if (Info.FlashOnAttack)
						self.World.AddFrameEndTask(w =>
						{
							w.Add(new Effects.FlashTarget(self, Color.Orange, 0.1f));
						});

					foreach (var npa in self.TraitsImplementing<INotifyAttack>())
						npa.Attacking(self, target, a, barrel);
				}
			}
		}

		public override void DoAttack(Actor self, in Target target, bool isManualTarget = false)
		{
			if (useGarrisonManager)
			{
				// In GarrisonManager mode, DoAttack from AttackFollow is a no-op.
				// Firing is handled by DoGarrisonedAttack called from Tick.
				return;
			}

			// Legacy mode: original behavior
			if (!CanAttack(self, target))
				return;

			var pos = self.CenterPosition;
			var targetedPosition = GetTargetPosition(pos, target);
			var targetYaw = (targetedPosition - pos).Yaw;

			foreach (var a in Armaments)
			{
				if (a.IsTraitDisabled)
					continue;

				var port = SelectFirePort(self, targetYaw);
				if (port == null)
					return;

				paxFacing[a.Actor].Facing = targetYaw;
				paxPos[a.Actor].SetCenterPosition(a.Actor, pos + PortOffset(self, port));

				if (!a.CheckFire(a.Actor, facing, target))
					continue;

				if (a.Info.MuzzleSequence != null)
				{
					// Muzzle facing is fixed once the firing starts
					var muzzleAnim = new Animation(self.World, paxRender[a.Actor].GetImage(a.Actor), () => targetYaw);
					var sequence = a.Info.MuzzleSequence;
					var muzzleFlash = new AnimationWithOffset(muzzleAnim,
						() => PortOffset(self, port),
						() => false,
						p => RenderUtils.ZOffsetFromCenter(self, p, 1024));

					muzzles.Add(muzzleFlash);
					muzzleAnim.PlayThen(sequence, () => muzzles.Remove(muzzleFlash));
				}

				if (Info.FlashOnAttack)
					self.World.AddFrameEndTask(w =>
					{
						w.Add(new Effects.FlashTarget(self, Color.Orange, 0.1f));
					});

				foreach (var npa in self.TraitsImplementing<INotifyAttack>())
					npa.Attacking(self, target, a, barrel);
			}
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			var pal = wr.Palette(Info.MuzzlePalette);

			// Display muzzle flashes
			foreach (var m in muzzles)
				foreach (var r in m.Render(self, pal))
					yield return r;
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			// Muzzle flashes don't contribute to actor bounds
			yield break;
		}
	}
}
