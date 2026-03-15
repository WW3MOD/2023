using System;
using System.Collections.Generic;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class Land : Activity
	{
		readonly Aircraft aircraft;
		readonly WVec offset;
		readonly WAngle? desiredFacing;
		readonly bool assignTargetOnFirstRun;
		readonly CPos[] clearCells;
		readonly WDist landRange;
		readonly Color? targetLineColor;

		Target target;
		WPos targetPosition;
		CPos landingCell;
		bool landingInitiated;
		bool finishedApproach;

		public Land(Actor self, WAngle? facing = null, Color? targetLineColor = null)
			: this(self, Target.Invalid, new WDist(-1), WVec.Zero, facing, targetLineColor: targetLineColor)
		{
			assignTargetOnFirstRun = true;
		}

		public Land(Actor self, in Target target, WAngle? facing = null, Color? targetLineColor = null)
			: this(self, target, new WDist(-1), WVec.Zero, facing, targetLineColor: targetLineColor) { }

		public Land(Actor self, in Target target, WDist landRange, WAngle? facing = null, Color? targetLineColor = null)
			: this(self, target, landRange, WVec.Zero, facing, targetLineColor: targetLineColor) { }

		public Land(Actor self, in Target target, in WVec offset, WAngle? facing = null, Color? targetLineColor = null)
			: this(self, target, WDist.Zero, offset, facing, targetLineColor: targetLineColor) { }

		public Land(Actor self, in Target target, WDist landRange, in WVec offset, WAngle? facing = null, CPos[] clearCells = null, Color? targetLineColor = null)
		{
			aircraft = self.Trait<Aircraft>();
			this.target = target;
			this.offset = offset;
			this.clearCells = clearCells ?? Array.Empty<CPos>();
			this.landRange = landRange.Length >= 0 ? landRange : aircraft.Info.LandRange;
			this.targetLineColor = targetLineColor;

			if (!facing.HasValue && aircraft.Info.TurnToLand)
				desiredFacing = aircraft.Info.InitialFacing;
			else
				desiredFacing = facing;
		}

		protected override void OnFirstRun(Actor self)
		{
			if (assignTargetOnFirstRun)
				target = Target.FromCell(self.World, self.Location);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || target.Type == TargetType.Invalid)
			{
				if (landingInitiated)
				{
					var shouldLand = aircraft.Info.IdleBehavior == IdleBehaviorType.Land;
					var continueLanding = shouldLand && self.CurrentActivity.IsCanceling && self.CurrentActivity.NextActivity == null;
					if (!continueLanding)
					{
						var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
						if (dat > aircraft.LandAltitude && dat < aircraft.Info.CruiseAltitude)
						{
							QueueChild(new TakeOff(self));
							return false;
						}

						aircraft.RemoveInfluence();
						return true;
					}
				}
				else
					return true;
			}

			var pos = aircraft.GetPosition();
			targetPosition = target.CenterPosition + offset;
			landingCell = self.World.Map.CellContaining(targetPosition);

			if (target.Type == TargetType.Terrain && !landingInitiated)
			{
				var newLocation = aircraft.FindLandingLocation(landingCell, landRange);
				if (!newLocation.HasValue)
				{
					QueueChild(aircraft.MoveTo(landingCell, 0));
					return true;
				}

				if (newLocation.Value != landingCell)
				{
					target = Target.FromCell(self.World, newLocation.Value);
					targetPosition = target.CenterPosition + offset;
					landingCell = self.World.Map.CellContaining(targetPosition);
				}
			}

			if (aircraft.Info.VTOL)
			{
				var delta = targetPosition - pos;
				var horizontalDistance = delta.HorizontalLength;
				var dat = self.World.Map.DistanceAboveTerrain(pos);
				var h = aircraft.Info.CruiseAltitude.Length - aircraft.LandAltitude.Length;
				var halfwayAltitude = new WDist(aircraft.Info.CruiseAltitude.Length - h / 2);

				// Phase 1: Approach at cruising altitude
				if (horizontalDistance > 512)
				{
					var desiredFacingMove = delta.HorizontalLengthSquared != 0 ? delta.Yaw : aircraft.Facing;
					Fly.FlyTick(self, aircraft, desiredFacingMove, aircraft.Info.CruiseAltitude);
					return false;
				}

				// Phase 2: Descent while moving horizontally
				if (dat.Length > halfwayAltitude.Length && horizontalDistance > 128)
				{
					var desiredFacingMove = delta.HorizontalLengthSquared != 0 ? delta.Yaw : aircraft.Facing;
					Fly.FlyTick(self, aircraft, desiredFacingMove, halfwayAltitude);
					return false;
				}

				// Phase 3: Vertical descent
				if (dat.Length > aircraft.LandAltitude.Length)
				{
					Fly.VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.LandAltitude);
					return false;
				}

				// Landed
				if (!landingInitiated)
				{
					if (aircraft.CanLand(new[] { landingCell }, target.Actor))
					{
						if (aircraft.Info.LandingSounds.Length > 0)
							Game.Sound.Play(SoundType.World, aircraft.Info.LandingSounds, self.World, aircraft.CenterPosition);
						foreach (var notify in self.TraitsImplementing<INotifyLanding>())
							notify.Landing(self);
						aircraft.AddInfluence(landingCell);
						aircraft.EnteringCell(self);
						landingInitiated = true;
					}
					else
					{
						QueueChild(new FlyIdle(self, 25));
						self.NotifyBlocker(new[] { landingCell });
						return false;
					}
				}

				return true;
			}
			else
			{
				if (!finishedApproach)
				{
					var altitude = aircraft.Info.CruiseAltitude.Length;
					var landDistance = altitude * 1024 / aircraft.Info.MaximumPitch.Tan();
					var rotation = WRot.None;
					if (desiredFacing.HasValue)
						rotation = WRot.FromYaw(desiredFacing.Value);

					var approachStart = targetPosition + new WVec(0, landDistance, altitude).Rotate(rotation);
					var speed = aircraft.MovementSpeed * 32 / 35;
					var turnRadius = Fly.CalculateTurnRadius(speed, aircraft.TurnSpeed);

					var angle = aircraft.Facing;
					var fwd = -new WVec(angle.Sin(), angle.Cos(), 0);
					var side = new WVec(-fwd.Y, fwd.X, fwd.Z);
					var approachDelta = self.CenterPosition - approachStart;
					var sideTowardBase = new[] { side, -side }.MinBy(a => WVec.Dot(a, approachDelta));

					var cp = self.CenterPosition + turnRadius * sideTowardBase / 1024;
					var posCenter = new WPos(cp.X, cp.Y, altitude);
					var approachCenter = approachStart + new WVec(0, turnRadius * Math.Sign(self.CenterPosition.Y - approachStart.Y), 0);
					var tangentDirection = approachCenter - posCenter;
					var tangentLength = tangentDirection.Length;
					var tangentOffset = WVec.Zero;
					if (tangentLength != 0)
						tangentOffset = new WVec(-tangentDirection.Y, tangentDirection.X, 0) * turnRadius / tangentLength;

					if (tangentOffset.X > 0)
						tangentOffset = -tangentOffset;

					var w1 = posCenter + tangentOffset;
					var w2 = approachCenter + tangentOffset;
					var w3 = approachStart;

					turnRadius = Fly.CalculateTurnRadius(aircraft.Info.Speed, aircraft.TurnSpeed);

					QueueChild(new Fly(self, Target.FromPos(w1), WDist.Zero, new WDist(turnRadius * 3)));
					QueueChild(new Fly(self, Target.FromPos(w2)));
					QueueChild(new Fly(self, Target.FromPos(w3), WDist.Zero, new WDist(turnRadius / 2)));
					finishedApproach = true;
					return false;
				}

				if (!landingInitiated)
				{
					var blockingCells = clearCells.Append(landingCell);
					if (!aircraft.CanLand(blockingCells, target.Actor))
					{
						QueueChild(new FlyIdle(self, 25));
						self.NotifyBlocker(blockingCells);
						finishedApproach = false;
						return false;
					}

					if (aircraft.Info.LandingSounds.Length > 0)
						Game.Sound.Play(SoundType.World, aircraft.Info.LandingSounds, self.World, aircraft.CenterPosition);

					foreach (var notify in self.TraitsImplementing<INotifyLanding>())
						notify.Landing(self);

					aircraft.AddInfluence(landingCell);
					aircraft.EnteringCell(self);
					landingInitiated = true;
				}

				var d = targetPosition - pos;
				var move = aircraft.FlyStep(aircraft.Facing);
				if (d.HorizontalLengthSquared < move.HorizontalLengthSquared)
				{
					var landingAltVec = new WVec(WDist.Zero, WDist.Zero, aircraft.LandAltitude);
					aircraft.SetPosition(self, targetPosition + landingAltVec);
					return true;
				}

				var landingAlt = self.World.Map.DistanceAboveTerrain(targetPosition) + aircraft.LandAltitude;
				Fly.FlyTick(self, aircraft, d.Yaw, landingAlt);
				return false;
			}
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (targetLineColor.HasValue)
				yield return new TargetLineNode(target, targetLineColor.Value);
		}
	}
}
