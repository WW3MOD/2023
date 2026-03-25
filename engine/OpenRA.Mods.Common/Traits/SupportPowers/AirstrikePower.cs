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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class AirstrikePowerInfo : SupportPowerInfo
	{
		[ActorReference(typeof(AircraftInfo))]
		public readonly string UnitType = "badr.bomber";
		public readonly int SquadSize = 1;
		public readonly WVec SquadOffset = new WVec(-1536, 1536, 0);

		public readonly WDist Cordon = new WDist(5120);

		[ActorReference]
		[Desc("Actor to spawn when the aircraft start attacking")]
		public readonly string CameraActor = null;

		[Desc("Amount of time to keep the camera alive after the aircraft have finished attacking")]
		public readonly int CameraRemoveDelay = 25;

		[Desc("Weapon range offset to apply during the beacon clock calculation")]
		public readonly WDist BeaconDistanceOffset = WDist.FromCells(6);

		public override object Create(ActorInitializer init) { return new AirstrikePower(init.Self, this); }
	}

	public class AirstrikePower : SupportPower
	{
		readonly AirstrikePowerInfo info;

		public AirstrikePower(Actor self, AirstrikePowerInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);
			SendAirstrike(self, order.Target.CenterPosition);
		}

		public Actor[] SendAirstrike(Actor self, WPos target)
		{
			var aircraft = new List<Actor>();
			var map = self.World.Map;

			var actorInfo = map.Rules.Actors[info.UnitType.ToLowerInvariant()];
			var aircraftInfo = actorInfo.TraitInfo<AircraftInfo>();
			var altitude = aircraftInfo.CruiseAltitude.Length;

			// Spawn from the closest map edge to the player's base
			var spawnCell = map.ChooseClosestEdgeCell(self.Owner.HomeLocation);
			var spawnPos = map.CenterOfCell(spawnCell) + new WVec(0, 0, altitude);

			// Target position at cruise altitude
			var targetWithAlt = target + new WVec(0, 0, altitude);

			// Face from spawn toward target
			var delta = targetWithAlt - spawnPos;
			var spawnFacing = delta.HorizontalLengthSquared != 0 ? delta.Yaw : WAngle.Zero;
			var attackRotation = WRot.FromYaw(spawnFacing);

			// Distance from spawn to target — used for exit calculation
			var distanceToTarget = delta.HorizontalLength;

			// Create the actors immediately so they can be returned
			for (var i = -info.SquadSize / 2; i <= info.SquadSize / 2; i++)
			{
				// Even-sized squads skip the lead plane
				if (i == 0 && (info.SquadSize & 1) == 0)
					continue;

				// Includes the 90 degree rotation between body and world coordinates
				var so = info.SquadOffset;
				var spawnOffset = new WVec(i * so.Y, -Math.Abs(i) * so.X, 0).Rotate(attackRotation);

				var a = self.World.CreateActor(false, info.UnitType, new TypeDictionary
				{
					new CenterPositionInit(spawnPos + spawnOffset),
					new OwnerInit(self.Owner),
					new FacingInit(spawnFacing),
				});

				aircraft.Add(a);
			}

			self.World.AddFrameEndTask(w =>
			{
				PlayLaunchSounds();

				// Spawn camera at target
				Actor camera = null;
				if (info.CameraActor != null)
				{
					camera = w.CreateActor(info.CameraActor, new TypeDictionary
					{
						new LocationInit(map.CellContaining(target)),
						new OwnerInit(self.Owner),
					});

					camera.QueueActivity(new Wait(info.CameraRemoveDelay));
					camera.QueueActivity(new RemoveSelf());
				}

				Actor distanceTestActor = null;
				foreach (var a in aircraft)
				{
					w.Add(a);

					// Single-pass strafe run: fly to target, then return to spawn edge.
					// (OpportunityFire handles shooting during the pass), then exit map.
					// Player can still select and redirect — queued activities cancel normally.
					a.QueueActivity(new Fly(a, Target.FromPos(targetWithAlt)));

					// Turn around and fly back to the spawn edge (where the plane entered)
					a.QueueActivity(new Fly(a, Target.FromPos(spawnPos)));

					// Fly forward past the map edge to ensure clean exit — guarantees the aircraft exits past the far map edge.
					a.QueueActivity(new FlyForward(a, info.Cordon));
					a.QueueActivity(new RemoveSelf());

					distanceTestActor = a;
				}

				if (Info.DisplayBeacon && distanceTestActor != null)
				{
					var distance = distanceToTarget;

					var beacon = new Beacon(
						self.Owner,
						target,
						Info.BeaconPaletteIsPlayerPalette,
						Info.BeaconPalette,
						Info.BeaconImage,
						Info.BeaconPoster,
						Info.BeaconPosterPalette,
						Info.BeaconSequence,
						Info.ArrowSequence,
						Info.CircleSequence,
						Info.ClockSequence,
						() => distanceTestActor.IsDead || distanceTestActor.Disposed
						? 1f
						: 1 - ((distanceTestActor.CenterPosition - targetWithAlt).HorizontalLength - info.BeaconDistanceOffset.Length) * 1f / distance,
						Info.BeaconDelay);

					w.Add(beacon);
				}
			});

			return aircraft.ToArray();
		}
	}
}
