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
		const int StopThreshold = 128;
		const int WaypointThreshold = 512;
		readonly Aircraft aircraft;
		readonly WDist maxRange;
		readonly WDist minRange;
		readonly Color? targetLineColor;
		readonly WDist nearEnough;
		Target target;
		Target lastVisibleTarget;
		bool useLastVisibleTarget;
		bool justTakenOff;
		WPos? lastTargetPosition; // Store the last known target position to detect changes

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
			lastTargetPosition = null; // Initialize last target position
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

			// Reset justTakenOff flag if we're not canceling
			if (!IsCanceling)
				justTakenOff = false;

			// Validate and update target
			if (ValidateAndUpdateTarget(self))
				return true; // Exit

			// Compute position, delta, and range checks
			var (checkTarget, pos, delta, deltaHorizontal, deltaLengthSquared, deltaLength, isFinalWaypoint, insideMaxRange, insideMinRange) = ComputeTargetDeltaAndRanges(self);

			// Check if the target has changed
			var currentTargetPosition = checkTarget.CenterPosition;
			var targetChanged = lastTargetPosition.HasValue && lastTargetPosition != currentTargetPosition;
			lastTargetPosition = currentTargetPosition;

			// Check if we should start turning to the next waypoint (and clear the current waypoint)
			var desiredFacing = deltaHorizontal.LengthSquared != 0 ? deltaHorizontal.Yaw : aircraft.Facing;
			var turnInfo = CheckEarlyTurn(self, deltaHorizontal, deltaLength, desiredFacing, isFinalWaypoint);
			desiredFacing = turnInfo.desiredFacing;
			if (turnInfo.shouldStartTurning && !isFinalWaypoint)
			{
				return true; // Proceed to next waypoint immediately
			}

			// Step 1: Check if we should apply braking
			var momentumAdjustment = WVec.Zero;
			var shouldBrake = ShouldApplyBraking(self, deltaLength, isFinalWaypoint, deltaHorizontal, desiredFacing);
			if (shouldBrake)
			{
				momentumAdjustment += ComputeBrakingForce();
				aircraft.CurrentMomentum += momentumAdjustment;
				aircraft.CurrentSpeed = aircraft.CurrentMomentum.HorizontalLength;

				// Update facing
				UpdateFacing(desiredFacing, targetChanged);

				// Move the aircraft (no vertical momentum adjustment needed here)
				ApplyMomentumAndMove(self, aircraft, aircraft.Info.CruiseAltitude, WVec.Zero);

				// If stopped and at cruise altitude, check if we should exit
				if (aircraft.CurrentMomentum.LengthSquared < 64 && dat == aircraft.Info.CruiseAltitude)
				{
					if (IsCanceling && self.CurrentActivity.NextActivity != null)
					{
						return true; // Proceed to next activity
					}
					else if (IsCanceling)
					{
						return true; // Exit
					}
					else if (isFinalWaypoint)
					{
						var finalZ = aircraft.Info.CruiseAltitude.Length;
						var targetPosAtCruise = new WPos(checkTarget.CenterPosition.X, checkTarget.CenterPosition.Y, finalZ);
						aircraft.SetPosition(self, targetPosAtCruise);
						aircraft.CurrentMomentum = WVec.Zero;
						aircraft.CurrentSpeed = 0;
						return true; // Exit
					}
				}

				return false; // Tick
			}

			// If too close, reverse or turn away
			if (insideMinRange)
			{
				HandleTooClose(self, checkTarget, pos);
				return false; // Tick
			}

			// Check if close enough to the waypoint (for final waypoint or if we missed the early turn)
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

			// Step 2: Compute movement forces (no braking here)
			var totalMomentumAdjustment = WVec.Zero;

			// Compute vertical momentum adjustment separately
			var verticalMomentum = ComputeAltitudeAdjustment(self, dat, delta, isFinalWaypoint);

			// Compute horizontal momentum adjustment
			totalMomentumAdjustment += ComputeHorizontalMovementForces(self, deltaHorizontal, desiredFacing, deltaLength, isFinalWaypoint, targetChanged);

			// Step 3: Apply the momentum adjustment
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
			UpdateFacing(desiredFacing, targetChanged);

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

		bool ShouldApplyBraking(Actor self, long deltaLength, bool isFinalWaypoint, WVec deltaHorizontal, WAngle desiredFacing)
		{
			// Condition 1: Canceling (but only brake if there's no next Fly activity)
			if (IsCanceling && !(NextActivity is Fly))
				return true;

			// Condition 2: Close to the final waypoint
			var stoppingDistance = (long)aircraft.CalculateStoppingDistance();
			if (isFinalWaypoint && deltaLength <= stoppingDistance)
				return true;

			// Condition 3: Approaching a turn where we need to slow down
			if (!isFinalWaypoint && NextActivity is Fly nextFly)
			{
				var nextTarget = nextFly.target;
				var nextDelta = nextTarget.CenterPosition - (useLastVisibleTarget ? lastVisibleTarget : target).CenterPosition;
				var nextDeltaHorizontal = new WVec(nextDelta.X, nextDelta.Y, 0);
				var nextYaw = nextDeltaHorizontal.Yaw;

				var earlyTurnAngleDiff = Math.Abs((nextYaw.Angle - desiredFacing.Angle) % 1024);
				if (earlyTurnAngleDiff > 512) earlyTurnAngleDiff = 1024 - earlyTurnAngleDiff;

				var turnSpeed = aircraft.TurnSpeed;
				var turnTicks = (earlyTurnAngleDiff + turnSpeed.Angle - 1) / turnSpeed.Angle;
				var turnDistance = (long)aircraft.CurrentSpeed * turnTicks;

				if (deltaLength <= turnDistance)
				{
					// Calculate the distance to the next waypoint
					var distanceToNextWaypoint = (long)nextDeltaHorizontal.Length;

					// Calculate the target speed to make the turn without overshooting
					var targetSpeed = CalculateTargetTurnSpeed(earlyTurnAngleDiff, turnSpeed, distanceToNextWaypoint);

					// Brake if the current speed is too high to make the turn
					if (aircraft.CurrentSpeed > targetSpeed)
						return true;
				}
			}

			return false;
		}

		int CalculateTargetTurnSpeed(int turnAngleDiff, WAngle turnSpeed, long distanceToNextWaypoint)
		{
			// Scale the target speed based on the turn angle (sharper turns require slower speeds)
			var angleFactor = turnAngleDiff / 1024.0; // Normalize to [0, 0.5]
			var minSpeedFactor = 0.2; // Minimum speed as a fraction of MaxSpeed (e.g., 20%)
			var speedReduction = minSpeedFactor + (1 - minSpeedFactor) * (1 - angleFactor); // Reduce speed more for sharper turns

			// Base target speed on the turn radius required to make the turn
			var targetTurnRadius = Math.Max(distanceToNextWaypoint / 2, 512); // Ensure a minimum turn radius
			var targetSpeed = turnSpeed.Angle > 0 ? (int)(180 * targetTurnRadius / turnSpeed.Angle) : aircraft.Info.MaxSpeed;

			// Apply the angle-based speed reduction
			targetSpeed = (int)(targetSpeed * speedReduction);

			// Ensure the target speed doesn’t exceed MaxSpeed or fall below a minimum
			var minSpeed = (int)(aircraft.Info.MaxSpeed * minSpeedFactor);
			return Math.Max(minSpeed, Math.Min(targetSpeed, aircraft.Info.MaxSpeed));
		}

		(WAngle desiredFacing, bool shouldStartTurning) CheckEarlyTurn(Actor self, WVec deltaHorizontal, long deltaLength, WAngle desiredFacing, bool isFinalWaypoint)
		{
			if (isFinalWaypoint || !(NextActivity is Fly nextFly))
				return (desiredFacing, false);

			var nextTarget = nextFly.target;
			var nextDelta = nextTarget.CenterPosition - (useLastVisibleTarget ? lastVisibleTarget : target).CenterPosition;
			var nextDeltaHorizontal = new WVec(nextDelta.X, nextDelta.Y, 0);
			var nextYaw = nextDeltaHorizontal.Yaw;

			var earlyTurnAngleDiff = Math.Abs((nextYaw.Angle - desiredFacing.Angle) % 1024);
			if (earlyTurnAngleDiff > 512) earlyTurnAngleDiff = 1024 - earlyTurnAngleDiff;

			var turnSpeed = aircraft.TurnSpeed;
			var turnTicks = (earlyTurnAngleDiff + turnSpeed.Angle - 1) / turnSpeed.Angle;
			var turnDistance = (long)aircraft.CurrentSpeed * turnTicks;

			if (deltaLength <= turnDistance)
				return (nextYaw, true);

			return (desiredFacing, false);
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

		WVec ComputeHorizontalMovementForces(Actor self, WVec deltaHorizontal, WAngle desiredFacing, long deltaLength, bool isFinalWaypoint, bool targetChanged)
		{
			var momentumAdjustment = WVec.Zero;
			var currentYaw = aircraft.CurrentMomentum.LengthSquared > 0 ? aircraft.CurrentMomentum.Yaw : desiredFacing;
			var angleDiff = WAngle.AngleDiff(desiredFacing, currentYaw);

			// If the target has changed, adjust the momentum to align with the new desired facing
			if (targetChanged && aircraft.CurrentMomentum.LengthSquared > 0)
			{
				var currentSpeed = aircraft.CurrentSpeed;
				var newMomentumDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(desiredFacing));
				aircraft.CurrentMomentum = newMomentumDirection * currentSpeed / 1024;
				aircraft.CurrentSpeed = currentSpeed; // Preserve the speed
				currentYaw = desiredFacing; // Update currentYaw to match the new momentum
				angleDiff = WAngle.Zero; // Reset angle difference since we've aligned the momentum
			}

			// Always apply forward thrust to ensure we reach MaxSpeed
			var thrustForce = aircraft.Info.ThrustForce;
			var thrustDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(desiredFacing));
			thrustDirection = thrustDirection * thrustForce / 1024;
			momentumAdjustment += thrustDirection;

			// Apply turning force if the angle difference is significant or if the target has changed
			// 14 in 1024-based system is approximately 5 degrees (14/1024 * 360 ≈ 5)
			if (angleDiff.Angle >= 14 || targetChanged)
			{
				var isSlider = aircraft.Info.CanSlide;
				var shouldSlide = isSlider && aircraft.CurrentSpeed < aircraft.Info.MaxSlidingSpeed;

				if (shouldSlide)
				{
					// Already applied thrust above
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

			return momentumAdjustment;
		}

		void UpdateFacing(WAngle desiredFacing, bool targetChanged)
		{
			if (aircraft.CurrentMomentum.LengthSquared == 0)
			{
				aircraft.Facing = desiredFacing;
				aircraft.CurrentRotationalVelocity = WAngle.Zero;
				return;
			}

			// If the target has changed, immediately set the facing to the desired facing
			if (targetChanged)
			{
				aircraft.Facing = desiredFacing;
				aircraft.CurrentRotationalVelocity = WAngle.Zero;
			}
			else
			{
				// Otherwise, set the facing to the current momentum's yaw
				var targetFacing = aircraft.CurrentMomentum.Yaw;
				aircraft.Facing = targetFacing;
				aircraft.CurrentRotationalVelocity = WAngle.Zero; // Reset rotational velocity since we're directly setting the facing
			}
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
