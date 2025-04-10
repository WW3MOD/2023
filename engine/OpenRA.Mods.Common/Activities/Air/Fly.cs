using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class Fly : Activity
	{
		readonly Aircraft aircraft;
		readonly WDist maxRange;
		readonly WDist minRange;
		readonly Color? targetLineColor;
		readonly WDist nearEnough;

		Target target;
		Target lastVisibleTarget;
		bool useLastVisibleTarget;
		const int StopThreshold = 128;
		const int WaypointThreshold = 512;
		bool justTakenOff;

		public Fly(Actor self, in Target t, WDist nearEnough, WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: this(self, t, initialTargetPosition, targetLineColor)
		{
			this.nearEnough = nearEnough;
		}

		public Fly(Actor self, in Target t, WPos? initialTargetPosition = null, Color? targetLineColor = null)
		{
			aircraft = self.Trait<Aircraft>();
			target = t;
			this.targetLineColor = targetLineColor;

			if ((target.Type == TargetType.Actor && target.Actor.CanBeViewedByPlayer(self.Owner))
				|| target.Type == TargetType.FrozenActor || target.Type == TargetType.Terrain)
				lastVisibleTarget = Target.FromPos(target.CenterPosition);
			else if (initialTargetPosition.HasValue)
				lastVisibleTarget = Target.FromPos(initialTargetPosition.Value);

			justTakenOff = false;
		}

		public Fly(Actor self, in Target t, WDist minRange, WDist maxRange,
			WPos? initialTargetPosition = null, Color? targetLineColor = null)
			: this(self, t, initialTargetPosition, targetLineColor)
		{
			this.maxRange = maxRange;
			this.minRange = minRange;
		}

		public static void FlyTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, in WVec desiredMove, bool idleTurn = false)
		{
			var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);

			aircraft.AdjustMomentum(desiredMove);

			var oldFacing = aircraft.Facing;
			var turnSpeed = idleTurn ? aircraft.IdleTurnSpeed ?? aircraft.TurnSpeed : aircraft.TurnSpeed;
			aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing, turnSpeed);

			var move = aircraft.CurrentMomentum;
			if (dat != desiredAltitude || move.Z != 0)
			{
				var maxDelta = move.HorizontalLength * aircraft.Info.MaximumPitch.Tan() / 1024;
				var moveZ = move.Z != 0 ? move.Z : (desiredAltitude.Length - dat.Length);
				var deltaZ = moveZ.Clamp(-maxDelta, maxDelta);
				move = new WVec(move.X, move.Y, deltaZ);
			}

			aircraft.SetPosition(self, aircraft.CenterPosition + move);
		}

		public static void FlyTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, bool idleTurn = false)
		{
			FlyTick(self, aircraft, desiredFacing, desiredAltitude, aircraft.FlyStep(aircraft.Info.MaxSpeed, desiredFacing), idleTurn);
		}

		public static void ApplyMomentumAndMove(Actor self, Aircraft aircraft, WDist desiredAltitude, WVec verticalMomentum)
		{
			var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
			var move = aircraft.CurrentMomentum;

			// Apply horizontal momentum (X, Y) with pitch constraints
			if (dat != desiredAltitude || move.Z != 0)
			{
				var maxDelta = move.HorizontalLength * aircraft.Info.MaximumPitch.Tan() / 1024;
				var moveZ = move.Z != 0 ? move.Z : (desiredAltitude.Length - dat.Length);
				var deltaZ = moveZ.Clamp(-maxDelta, maxDelta);
				move = new WVec(move.X, move.Y, deltaZ);
			}

			// Apply vertical momentum directly, bypassing pitch constraints for helicopters
			if (aircraft.Info.MinSpeed == 0) // Helicopters can move vertically without pitch constraints
			{
				move = new WVec(move.X, move.Y, verticalMomentum.Z);
			}

			aircraft.SetPosition(self, aircraft.CenterPosition + move);
		}

		public static bool VerticalTakeOffOrLandTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, bool idleTurn = false)
		{
			var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
			var move = WVec.Zero;

			var turnSpeed = idleTurn ? aircraft.IdleTurnSpeed ?? aircraft.TurnSpeed : aircraft.TurnSpeed;
			aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing, turnSpeed);

			if (dat != desiredAltitude)
			{
				var maxDelta = aircraft.Info.LiftForce;
				var deltaZ = (desiredAltitude.Length - dat.Length).Clamp(-maxDelta, maxDelta);
				move += new WVec(0, 0, deltaZ);
			}
			else
				return false; // Tick

			aircraft.SetPosition(self, aircraft.CenterPosition + move);
			return true;
		}

		public override bool Tick(Actor self)
		{
			// If forced to land, cancel the activity
			if (aircraft.ForceLanding)
			{
				Cancel(self);
				return false; // Tick
			}

			// Check altitude and landing status
			var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
			var isLanded = dat <= aircraft.LandAltitude;

			// If landed and paused, do nothing
			if (isLanded && aircraft.IsTraitPaused)
				return false; // Tick

			// If landed, queue takeoff
			if (isLanded)
			{
				QueueChild(new TakeOff(self));
				justTakenOff = false;
				return false; // Tick
			}

			// Mark that the helicopter has just taken off
			if (!isLanded && !justTakenOff)
			{
				justTakenOff = true;
			}

			// Handle canceling: decelerate and return to cruise altitude
			if (IsCanceling)
			{
				var landWhenIdle = aircraft.Info.IdleBehavior == IdleBehaviorType.Land;
				var skipHeightAdjustment = landWhenIdle && self.CurrentActivity.IsCanceling && self.CurrentActivity.NextActivity == null;

				// If can hover and not at cruise altitude, adjust height
				if (aircraft.Info.MinSpeed == 0 && !skipHeightAdjustment && dat != aircraft.Info.CruiseAltitude)
				{
					VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
					return false; // Tick
				}

				// If we just took off, ignore canceling and proceed to the next waypoint if there is one
				if (justTakenOff && NextActivity != null)
				{
					justTakenOff = false;
					return true; // Proceed to next activity
				}

				// Initialize total momentum adjustment
				var momentumAdjustment = WVec.Zero;

				// Apply braking force to decelerate
				momentumAdjustment += ComputeBrakingForce();

				// Apply the total momentum adjustment
				aircraft.CurrentMomentum += momentumAdjustment;
				aircraft.CurrentSpeed = aircraft.CurrentMomentum.HorizontalLength;

				// Clamp the speed to MaxSpeed
				if (aircraft.CurrentSpeed > aircraft.Info.MaxSpeed)
				{
					var currentDirection = aircraft.CurrentMomentum.LengthSquared > 0 ? aircraft.CurrentMomentum : WVec.Zero;
					var magnitude = currentDirection.Length;
					if (magnitude > 0)
					{
						aircraft.CurrentMomentum = currentDirection * aircraft.Info.MaxSpeed / magnitude;
						aircraft.CurrentSpeed = aircraft.Info.MaxSpeed;
					}
				}

				// Update facing
				UpdateFacing();

				// Move the aircraft (no vertical momentum adjustment needed here)
				ApplyMomentumAndMove(self, aircraft, aircraft.Info.CruiseAltitude, WVec.Zero);

				// If stopped and at cruise altitude, check if we should exit or proceed
				if (aircraft.CurrentMomentum.LengthSquared < 64 && dat == aircraft.Info.CruiseAltitude)
				{
					if (self.CurrentActivity.NextActivity != null)
					{
						return true; // Proceed to next activity
					}

					return true; // Exit
				}

				return false; // Tick
			}

			// Reset justTakenOff flag if we're not canceling
			justTakenOff = false;

			// Validate and update target
			if (ValidateAndUpdateTarget(self))
				return true; // Exit

			// Compute position, delta, and range checks
			var (checkTarget, pos, delta, deltaHorizontal, deltaLengthSquared, deltaLength, isFinalWaypoint, insideMaxRange, insideMinRange) = ComputeTargetDeltaAndRanges(self);

			// If within target range and at final waypoint, decelerate to stop
			if (insideMaxRange && !insideMinRange && isFinalWaypoint)
			{
				// Initialize total momentum adjustment
				var momentumAdjustment = WVec.Zero;

				// Apply braking force to stop
				momentumAdjustment += ComputeBrakingForce();

				// Apply the total momentum adjustment
				aircraft.CurrentMomentum += momentumAdjustment;
				aircraft.CurrentSpeed = aircraft.CurrentMomentum.HorizontalLength;

				// Clamp the speed to MaxSpeed
				if (aircraft.CurrentSpeed > aircraft.Info.MaxSpeed)
				{
					var currentDirection = aircraft.CurrentMomentum.LengthSquared > 0 ? aircraft.CurrentMomentum : WVec.Zero;
					var magnitude = currentDirection.Length;
					if (magnitude > 0)
					{
						aircraft.CurrentMomentum = currentDirection * aircraft.Info.MaxSpeed / magnitude;
						aircraft.CurrentSpeed = aircraft.Info.MaxSpeed;
					}
				}

				// Update facing
				UpdateFacing();

				// Move the aircraft (no vertical momentum adjustment needed here)
				ApplyMomentumAndMove(self, aircraft, aircraft.Info.CruiseAltitude, WVec.Zero);

				// If stopped, exit
				if (aircraft.CurrentMomentum.LengthSquared < 64)
					return true; // Exit

				return false; // Tick
			}

			// If too close, reverse or turn away
			if (insideMinRange)
			{
				HandleTooClose(self, checkTarget, pos);
				return false; // Tick
			}

			// Check if close enough to the waypoint
			if (deltaLengthSquared <= (isFinalWaypoint ? (long)StopThreshold * StopThreshold : (long)WaypointThreshold * WaypointThreshold))
			{
				if (isFinalWaypoint)
				{
					var finalZ = aircraft.Info.CruiseAltitude.Length;
					var targetPosAtCruise = new WPos(checkTarget.CenterPosition.X, checkTarget.CenterPosition.Y, finalZ);
					aircraft.SetPosition(self, targetPosAtCruise);
					aircraft.CurrentMomentum = WVec.Zero;
					aircraft.CurrentSpeed = 0;
					return true; // Exit
				}
				return true; // Proceed to next waypoint
			}

			// Initialize total momentum adjustment
			var totalMomentumAdjustment = WVec.Zero;

			// Step 1: Compute vertical momentum adjustment separately
			var verticalMomentum = ComputeAltitudeAdjustment(self, dat, delta, isFinalWaypoint);

			// Step 2: Compute horizontal momentum adjustment
			var desiredFacing = deltaHorizontal.LengthSquared != 0 ? deltaHorizontal.Yaw : aircraft.Facing;
			desiredFacing = CheckEarlyTurn(self, deltaHorizontal, deltaLength, desiredFacing, isFinalWaypoint);
			totalMomentumAdjustment += ComputeHorizontalMovementForces(self, deltaHorizontal, desiredFacing, deltaLength, isFinalWaypoint);

			// Step 3: Apply the horizontal momentum adjustment
			aircraft.CurrentMomentum += totalMomentumAdjustment;
			aircraft.CurrentSpeed = aircraft.CurrentMomentum.HorizontalLength;

			// Step 4: Clamp the speed to MaxSpeed
			if (aircraft.CurrentSpeed > aircraft.Info.MaxSpeed)
			{
				var currentDirection = aircraft.CurrentMomentum.LengthSquared > 0 ? aircraft.CurrentMomentum : WVec.Zero;
				var magnitude = currentDirection.Length;
				if (magnitude > 0)
				{
					aircraft.CurrentMomentum = currentDirection * aircraft.Info.MaxSpeed / magnitude;
					aircraft.CurrentSpeed = aircraft.Info.MaxSpeed;
				}
			}

			// Step 5: Update facing
			UpdateFacing();

			// Step 6: Move the aircraft, applying the vertical momentum separately
			ApplyMomentumAndMove(self, aircraft, aircraft.Info.CruiseAltitude, verticalMomentum);

			return false; // Tick
		}

		WVec ComputeBrakingForce()
		{
			if (aircraft.CurrentMomentum.LengthSquared == 0)
				return WVec.Zero;

			var brakeDirection = aircraft.CurrentMomentum.LengthSquared > 0 ? -aircraft.CurrentMomentum : WVec.Zero;
			var brakeMagnitude = brakeDirection.Length;
			if (brakeMagnitude > 0)
			{
				brakeDirection = brakeDirection * aircraft.Info.BreakingForce / brakeMagnitude;
				return brakeDirection;
			}

			return WVec.Zero;
		}

		void HandleTooClose(Actor self, Target checkTarget, WPos pos)
		{
			var isSlider = aircraft.Info.CanSlide;
			var speed = aircraft.Info.MaxSpeed;
			var deltaHorizontal = new WVec(checkTarget.CenterPosition.X - pos.X, checkTarget.CenterPosition.Y - pos.Y, 0);
			var move = isSlider ? aircraft.FlyStep(speed, deltaHorizontal.Yaw) : aircraft.FlyStep(speed, aircraft.Facing);
			if (isSlider)
				FlyTick(self, aircraft, deltaHorizontal.Yaw, aircraft.Info.CruiseAltitude, -move);
			else
				FlyTick(self, aircraft, deltaHorizontal.Yaw + new WAngle(512), aircraft.Info.CruiseAltitude, move);
		}

		bool ValidateAndUpdateTarget(Actor self)
		{
			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			return useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self);
		}

		(Target checkTarget, WPos pos, WVec delta, WVec deltaHorizontal, long deltaLengthSquared, long deltaLength, bool isFinalWaypoint, bool insideMaxRange, bool insideMinRange)
			ComputeTargetDeltaAndRanges(Actor self)
		{
			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;
			var pos = aircraft.GetPosition();
			var delta = checkTarget.CenterPosition - pos;
			var deltaHorizontal = new WVec(delta.X, delta.Y, 0);
			var deltaLengthSquared = (long)deltaHorizontal.LengthSquared;
			var deltaLength = (long)Math.Sqrt(deltaLengthSquared);
			var isFinalWaypoint = NextActivity == null;

			var insideMaxRange = maxRange.Length > 0 && checkTarget.IsInRange(pos, maxRange);
			var insideMinRange = minRange.Length > 0 && checkTarget.IsInRange(pos, minRange);

			return (checkTarget, pos, delta, deltaHorizontal, deltaLengthSquared, deltaLength, isFinalWaypoint, insideMaxRange, insideMinRange);
		}

		WAngle CheckEarlyTurn(Actor self, WVec deltaHorizontal, long deltaLength, WAngle desiredFacing, bool isFinalWaypoint)
		{
			if (isFinalWaypoint || !(NextActivity is Fly nextFly))
				return desiredFacing;

			var nextTarget = nextFly.target;
			var nextDelta = nextTarget.CenterPosition - (useLastVisibleTarget ? lastVisibleTarget : target).CenterPosition;
			var nextDeltaHorizontal = new WVec(nextDelta.X, nextDelta.Y, 0);
			var nextYaw = nextDeltaHorizontal.Yaw;

			var earlyTurnAngleDiff = Math.Abs((nextYaw - desiredFacing).Angle % 1024);
			if (earlyTurnAngleDiff > 512) earlyTurnAngleDiff = 1024 - earlyTurnAngleDiff;

			var turnSpeed = aircraft.TurnSpeed;
			var turnTicks = (earlyTurnAngleDiff + turnSpeed.Angle - 1) / turnSpeed.Angle;
			var turnDistance = (long)aircraft.CurrentSpeed * turnTicks;

			if (deltaLength <= turnDistance)
				return nextYaw;

			return desiredFacing;
		}

		WVec ComputeAltitudeAdjustment(Actor self, WDist dat, WVec delta, bool isFinalWaypoint)
		{
			var momentumAdjustment = WVec.Zero;
			// Compare the current altitude above terrain with the desired CruiseAltitude
			var currentAltitude = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
			var desiredAltitude = aircraft.Info.CruiseAltitude;

			if (currentAltitude != desiredAltitude)
			{
				var deltaAltitude = desiredAltitude.Length - currentAltitude.Length;
				var liftForce = aircraft.Info.LiftForce;
				var liftAdjustment = deltaAltitude > 0 ? Math.Min(deltaAltitude, liftForce) : Math.Max(deltaAltitude, -liftForce);
				momentumAdjustment += new WVec(0, 0, liftAdjustment);
			}

			return momentumAdjustment;
		}

		WVec ComputeHorizontalMovementForces(Actor self, WVec deltaHorizontal, WAngle desiredFacing, long deltaLength, bool isFinalWaypoint)
		{
			var momentumAdjustment = WVec.Zero;
			var currentYaw = aircraft.CurrentMomentum.LengthSquared > 0 ? aircraft.CurrentMomentum.Yaw : desiredFacing;
			var angleDiff = WAngle.FromDegrees(Math.Abs((desiredFacing - currentYaw).Angle) % 360);
			var stoppingDistance = (long)aircraft.CalculateStoppingDistance();

			if (isFinalWaypoint && deltaLength <= stoppingDistance)
			{
				momentumAdjustment += ComputeBrakingForce();
			}
			else
			{
				if (angleDiff.Angle < 14)
				{
					var thrustForce = aircraft.Info.ThrustForce;
					var thrustDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(desiredFacing));
					thrustDirection = thrustDirection * thrustForce / 1024;
					momentumAdjustment += thrustDirection;

					// Only apply sideways dampening if the speed is significantly above MaxSpeed (which shouldn't happen after speed clamping)
					// Removed excessive dampening to allow the helicopter to reach MaxSpeed
				}
				else
				{
					var isSlider = aircraft.Info.CanSlide;
					var shouldSlide = isSlider && aircraft.CurrentSpeed < aircraft.Info.MaxSlidingSpeed;

					if (shouldSlide)
					{
						var thrustForce = aircraft.Info.ThrustForce;
						var thrustDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(desiredFacing));
						thrustDirection = thrustDirection * thrustForce / 1024;
						momentumAdjustment += thrustDirection;
					}
					else
					{
						var liftForce = aircraft.Info.LiftForce;
						var turnDirection = Util.GetTurnDirection(currentYaw, desiredFacing);
						var liftDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(currentYaw + new WAngle(turnDirection * 256)));
						liftDirection = liftDirection * liftForce / 1024;

						var maxTurnAcceleration = 20;
						var currentSidewaysAcceleration = liftDirection.Length;
						if (currentSidewaysAcceleration > maxTurnAcceleration)
							liftDirection = liftDirection * maxTurnAcceleration / currentSidewaysAcceleration;

						momentumAdjustment += liftDirection;
					}
				}
			}

			return momentumAdjustment;
		}

		void UpdateFacing()
		{
			if (aircraft.CurrentMomentum.LengthSquared == 0)
				return;

			// Directly set the facing to the current momentum's yaw
			var targetFacing = aircraft.CurrentMomentum.Yaw;
			aircraft.Facing = targetFacing;
			aircraft.CurrentRotationalVelocity = WAngle.Zero; // Reset rotational velocity since we're directly setting the facingvar rawTargetFacing = aircraft.CurrentMomentum.Yaw;

			// var rawTargetFacing = aircraft.CurrentMomentum.Yaw;
			// var facingDiff = (rawTargetFacing - smoothedTargetFacing).Angle;
			// if (facingDiff > 512) facingDiff -= 1024;
			// else if (facingDiff < -512) facingDiff += 1024;

			// var maxFacingChange = aircraft.TurnSpeed.Angle / 2;
			// var facingChange = facingDiff.Clamp(-maxFacingChange, maxFacingChange);
			// smoothedTargetFacing = new WAngle((smoothedTargetFacing.Angle + facingChange) % 1024);
			// if (smoothedTargetFacing.Angle < 0) smoothedTargetFacing = new WAngle(smoothedTargetFacing.Angle + 1024);

			// var facingAngleDiff = (smoothedTargetFacing - aircraft.Facing).Angle;
			// if (facingAngleDiff == 0)
			// {
			// 	aircraft.CurrentRotationalVelocity = WAngle.Zero;
			// 	return;
			// }

			// var rotationDirection = facingAngleDiff > 0 ? 1 : -1;
			// if (facingAngleDiff > 512) rotationDirection = -1;
			// else if (facingAngleDiff < -512) rotationDirection = 1;

			// var rotationalForce = aircraft.TurnSpeed.Angle;
			// var maxRotationalVelocity = aircraft.Info.MaxRotationalVelocity.Angle;

			// var currentRotationalVelocity = aircraft.CurrentRotationalVelocity.Angle;
			// currentRotationalVelocity += rotationDirection * rotationalForce;

			// if (currentRotationalVelocity > maxRotationalVelocity)
			// 	currentRotationalVelocity = maxRotationalVelocity;
			// else if (currentRotationalVelocity < -maxRotationalVelocity)
			// 	currentRotationalVelocity = -maxRotationalVelocity;

			// if (Math.Abs(facingAngleDiff) < Math.Abs(currentRotationalVelocity))
			// 	currentRotationalVelocity = facingAngleDiff;

			// aircraft.CurrentRotationalVelocity = new WAngle(currentRotationalVelocity);

			// var newFacingAngle = (aircraft.Facing.Angle + currentRotationalVelocity) % 1024;
			// if (newFacingAngle < 0) newFacingAngle += 1024;
			// aircraft.Facing = new WAngle(newFacingAngle);
		}

		public override IEnumerable<Target> GetTargets(Actor self)
		{
			yield return target;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (targetLineColor.HasValue)
				yield return new TargetLineNode(useLastVisibleTarget ? lastVisibleTarget : target, targetLineColor.Value);
		}

		public static int CalculateTurnRadius(int speed, WAngle turnSpeed)
		{
			return turnSpeed.Angle > 0 ? 180 * speed / turnSpeed.Angle : 0;
		}
	}
}
