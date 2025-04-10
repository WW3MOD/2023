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

			// Roll and pitch logic for voxels
			// var roll = idleTurn ? aircraft.Info.IdleRoll ?? aircraft.Info.Roll : aircraft.Info.Roll;
			// if (roll != WAngle.Zero)
			// {
			//     var desiredRoll = aircraft.Facing == desiredFacing ? WAngle.Zero :
			//         new WAngle(roll.Angle * Util.GetTurnDirection(aircraft.Facing, oldFacing));
			//     aircraft.Roll = Util.TickFacing(aircraft.Roll, desiredRoll, aircraft.Info.RollSpeed);
			// }

			// if (aircraft.Info.Pitch != WAngle.Zero)
			//     aircraft.Pitch = Util.TickFacing(aircraft.Pitch, aircraft.Info.Pitch, aircraft.Info.PitchSpeed);

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

			aircraft.SetPosition(self, self.CenterPosition + move);
			return true;
		}

		public override bool Tick(Actor self)
		{
			// If forced to land, cancel the activity
			if (aircraft.ForceLanding)
				Cancel(self);

			// Check altitude and landing status
			var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
			var isLanded = dat <= aircraft.LandAltitude;

			// If landed and paused, do nothing
			if (isLanded && aircraft.IsTraitPaused)
				return false; // Tick

			// Handle canceling: decelerate and return to cruise altitude
			if (IsCanceling)
			{
				var landWhenIdle = aircraft.Info.IdleBehavior == IdleBehaviorType.Land;
				var skipHeightAdjustment = landWhenIdle && self.CurrentActivity.IsCanceling && self.CurrentActivity.NextActivity == null;

				// If can hover and not at cruise altitude, adjust height
				if (aircraft.Info.MinSpeed == 0 && !skipHeightAdjustment && dat != aircraft.Info.CruiseAltitude)
				{
					if (isLanded)
						QueueChild(new TakeOff(self));
					else
						VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
					return false; // Tick
				}

				// Apply brakes to decelerate
				if (aircraft.CurrentMomentum.LengthSquared > 0)
				{
					// Compute braking force opposite the direction of movement
					var brakeDirection = aircraft.CurrentMomentum.LengthSquared > 0 ? -aircraft.CurrentMomentum : WVec.Zero;
					var brakeMagnitude = brakeDirection.Length;
					if (brakeMagnitude > 0)
					{
						brakeDirection = brakeDirection * aircraft.Info.BreakingForce / brakeMagnitude;
						aircraft.CurrentMomentum += brakeDirection;
					}
				}

				// If stopped and at cruise altitude, exit the activity
				if (self.CurrentActivity.NextActivity == null && aircraft.CurrentMomentum.LengthSquared < 64 && dat == aircraft.Info.CruiseAltitude)
					return true; // Exit

				// Continue decelerating
				FlyTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude, WVec.Zero);
				return false; // Tick
			}
			else if (isLanded)
			{
				// If landed, queue takeoff
				QueueChild(new TakeOff(self));
				return false; // Tick
			}

			// Recalculate target and validate
			target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
			if (!targetIsHiddenActor && target.Type == TargetType.Actor)
				lastVisibleTarget = Target.FromTargetPositions(target);

			useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

			// No valid target, exit
			if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
				return true; // Exit

			// Compute position and delta to target
			var checkTarget = useLastVisibleTarget ? lastVisibleTarget : target;
			var pos = aircraft.GetPosition();
			var delta = checkTarget.CenterPosition - pos;
			var deltaHorizontal = new WVec(delta.X, delta.Y, 0);
			var deltaLengthSquared = deltaHorizontal.LengthSquared;
			var deltaLength = (int)Math.Sqrt(deltaLengthSquared);
			var isFinalWaypoint = NextActivity == null;

			// Range checks
			var insideMaxRange = maxRange.Length > 0 && checkTarget.IsInRange(pos, maxRange);
			var insideMinRange = minRange.Length > 0 && checkTarget.IsInRange(pos, minRange);

			// If within target range and at final waypoint, decelerate to stop
			if (insideMaxRange && !insideMinRange && isFinalWaypoint)
			{
				// Apply brakes to stop
				if (aircraft.CurrentMomentum.LengthSquared > 0)
				{
					var brakeDirection = aircraft.CurrentMomentum.LengthSquared > 0 ? -aircraft.CurrentMomentum : WVec.Zero;
					var brakeMagnitude = brakeDirection.Length;
					if (brakeMagnitude > 0)
					{
						brakeDirection = brakeDirection * aircraft.Info.BreakingForce / brakeMagnitude;
						aircraft.CurrentMomentum += brakeDirection;
					}
				}

				// If stopped, exit
				if (aircraft.CurrentMomentum.LengthSquared < 64)
					return true; // Exit

				FlyTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude, WVec.Zero);
				return false; // Tick
			}
			// If too close, reverse or turn away
			else if (insideMinRange)
			{
				var isSlider = aircraft.Info.CanSlide;
				var speed = aircraft.Info.MaxSpeed;
				var move = isSlider ? aircraft.FlyStep(speed, deltaHorizontal.Yaw) : aircraft.FlyStep(speed, aircraft.Facing);
				if (isSlider)
					FlyTick(self, aircraft, deltaHorizontal.Yaw, aircraft.Info.CruiseAltitude, -move);
				else
					FlyTick(self, aircraft, deltaHorizontal.Yaw + new WAngle(512), aircraft.Info.CruiseAltitude, move);
				return false; // Tick
			}

			// Check if close enough to the waypoint
			if (deltaLengthSquared <= (isFinalWaypoint ? StopThreshold * StopThreshold : WaypointThreshold * WaypointThreshold))
			{
				if (isFinalWaypoint)
				{
					var targetPosAtCruise = new WPos(checkTarget.CenterPosition.X, checkTarget.CenterPosition.Y, pos.Z);
					aircraft.SetPosition(self, targetPosAtCruise);
					aircraft.CurrentMomentum = WVec.Zero;
					aircraft.CurrentSpeed = 0;
					return true; // Exit
				}
				else
				{
					return true; // Proceed to next waypoint
				}
			}

			// Check for early turn to the next waypoint
			var desiredFacing = deltaHorizontal.LengthSquared != 0 ? deltaHorizontal.Yaw : aircraft.Facing;
			bool shouldTurn = false;
			if (!isFinalWaypoint && NextActivity is Fly nextFly)
			{
				var nextTarget = nextFly.target;
				var nextDelta = nextTarget.CenterPosition - checkTarget.CenterPosition;
				var nextDeltaHorizontal = new WVec(nextDelta.X, nextDelta.Y, 0);
				var nextYaw = nextDeltaHorizontal.Yaw;
				var nextDistance = nextDeltaHorizontal.Length;

				var earlyTurnAngleDiff = Math.Abs((nextYaw - desiredFacing).Angle % 1024);
				if (earlyTurnAngleDiff > 512) earlyTurnAngleDiff = 1024 - earlyTurnAngleDiff;

				var turnSpeed = aircraft.TurnSpeed;
				var turnTicks = (earlyTurnAngleDiff + turnSpeed.Angle - 1) / turnSpeed.Angle;
				var turnDistance = aircraft.CurrentSpeed * turnTicks;

				shouldTurn = deltaLength <= turnDistance;

				if (shouldTurn)
				{
					desiredFacing = nextYaw;
				}
			}

			// Climb or descend to match target altitude
			WVec momentumAdjustment = WVec.Zero;
			if (dat != aircraft.Info.CruiseAltitude || delta.Z != 0)
			{
				var targetAltitude = isFinalWaypoint ? checkTarget.CenterPosition.Z : aircraft.Info.CruiseAltitude.Length;
				var deltaZ = targetAltitude - pos.Z;
				if (deltaZ != 0)
				{
					var liftForce = aircraft.Info.LiftForce;
					var liftAdjustment = deltaZ > 0 ? Math.Min(deltaZ, liftForce) : Math.Max(deltaZ, -liftForce);
					momentumAdjustment += new WVec(0, 0, liftAdjustment);
				}
			}

			// Compute angle difference to determine if we need to turn
			// Use CurrentMomentum.Yaw instead of Facing to determine movement direction
			var currentYaw = aircraft.CurrentMomentum.LengthSquared > 0 ? aircraft.CurrentMomentum.Yaw : desiredFacing;
			var angleDiff = WAngle.FromDegrees(Math.Abs((desiredFacing - currentYaw).Angle) % 360);
			var stoppingDistance = aircraft.CalculateStoppingDistance();

			// Decelerate if at final waypoint and within stopping distance
			if (isFinalWaypoint && deltaLength <= stoppingDistance)
			{
				if (aircraft.CurrentMomentum.LengthSquared > 0)
				{
					var brakeDirection = aircraft.CurrentMomentum.LengthSquared > 0 ? -aircraft.CurrentMomentum : WVec.Zero;
					var brakeMagnitude = brakeDirection.Length;
					if (brakeMagnitude > 0)
					{
						brakeDirection = brakeDirection * aircraft.Info.BreakingForce / brakeMagnitude;
						momentumAdjustment += brakeDirection;
					}
				}
			}
			else
			{
				// Determine if we should accelerate or turn
				if (angleDiff.Angle < 14) // Moving toward the target (within ~5 degrees)
				{
					// Apply thrust in the direction of the target
					var thrustForce = aircraft.Info.ThrustForce;
					var thrustDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(desiredFacing)); // Use desiredFacing (target direction)
					thrustDirection = thrustDirection * thrustForce / 1024;
					momentumAdjustment += thrustDirection;

					// Cancel sideways momentum to prevent orbiting
					var sidewaysMomentumDampeningFactor = 0.5f; // Hardcoded: Suggest adding SidewaysMomentumDampeningFactor to AircraftInfo (e.g., 0.5)
					var forwardDirection = thrustDirection.LengthSquared > 0 ? thrustDirection : new WVec(0, -1024, 0).Rotate(WRot.FromYaw(desiredFacing));
					var forwardMagnitude = forwardDirection.Length;
					if (forwardMagnitude > 0)
					{
						// Project momentum onto forward direction: (momentum · forward) / |forward|^2 * forward
						var dotProduct = aircraft.CurrentMomentum.X * forwardDirection.X + aircraft.CurrentMomentum.Y * forwardDirection.Y;
						var forwardComponent = forwardDirection * dotProduct / (forwardMagnitude * forwardMagnitude);
						var sidewaysMomentum = aircraft.CurrentMomentum - forwardComponent;
						// Scale sidewaysMomentum by the dampening factor manually
						var scaledSidewaysMomentum = new WVec(
							(int)(sidewaysMomentum.X * sidewaysMomentumDampeningFactor),
							(int)(sidewaysMomentum.Y * sidewaysMomentumDampeningFactor),
							sidewaysMomentum.Z
						);
						momentumAdjustment -= scaledSidewaysMomentum;
					}
				}
				else
				{
					// Determine if we should slide (helicopters at low speed)
					var isSlider = aircraft.Info.CanSlide;
					var shouldSlide = isSlider && aircraft.CurrentSpeed < aircraft.Info.MaxSlidingSpeed;

					if (shouldSlide)
					{
						// Apply thrust toward the target to maintain movement
						var thrustForce = aircraft.Info.ThrustForce;
						var thrustDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(desiredFacing)); // Use desiredFacing (target direction)
						thrustDirection = thrustDirection * thrustForce / 1024;
						momentumAdjustment += thrustDirection;
					}
					else
					{
						// Apply lift force sideways to turn (airplane-like turning)
						var liftForce = aircraft.Info.LiftForce;
						var turnDirection = Util.GetTurnDirection(currentYaw, desiredFacing);
						var liftDirection = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(currentYaw + new WAngle(turnDirection * 256))); // Use currentYaw (momentum direction)
						liftDirection = liftDirection * liftForce / 1024;

						// Limit sideways acceleration
						var maxTurnAcceleration = 20; // Hardcoded: Suggest adding MaxTurnAcceleration to AircraftInfo (e.g., 20 units/tick²)
						var currentSidewaysAcceleration = liftDirection.Length;
						if (currentSidewaysAcceleration > maxTurnAcceleration)
							liftDirection = liftDirection * maxTurnAcceleration / currentSidewaysAcceleration;

						momentumAdjustment += liftDirection;
					}
				}
			}

			// Apply momentum adjustment
			aircraft.CurrentMomentum += momentumAdjustment;

			// Update speed based on momentum
			aircraft.CurrentSpeed = aircraft.CurrentMomentum.HorizontalLength; // Update CurrentSpeed based on horizontal momentum

			// Update Facing at the end (cosmetic)
			if (aircraft.CurrentMomentum.LengthSquared > 0)
			{
				var targetFacing = aircraft.CurrentMomentum.Yaw; // Face the direction of travel
				var facingAngleDiff = (targetFacing - aircraft.Facing).Angle;
				if (facingAngleDiff != 0)
				{
					// Determine the direction of rotation
					var rotationDirection = facingAngleDiff > 0 ? 1 : -1;
					if (facingAngleDiff > 512) rotationDirection = -1;
					else if (facingAngleDiff < -512) rotationDirection = 1;

					// Apply rotational force (using TurnSpeed as the acceleration)
					var rotationalForce = aircraft.TurnSpeed.Angle; // Use TurnSpeed as the rotational force (e.g., 512 units/tick²)
					var maxRotationalVelocity = aircraft.Info.MaxRotationalVelocity.Angle; // Use the new MaxRotationalVelocity property

					// Adjust CurrentRotationalVelocity
					var currentRotationalVelocity = aircraft.CurrentRotationalVelocity.Angle;
					currentRotationalVelocity += rotationDirection * rotationalForce;

					// Clamp rotational velocity
					if (currentRotationalVelocity > maxRotationalVelocity)
						currentRotationalVelocity = maxRotationalVelocity;
					else if (currentRotationalVelocity < -maxRotationalVelocity)
						currentRotationalVelocity = -maxRotationalVelocity;

					// If we're close to the target facing, slow down rotational velocity
					if (Math.Abs(facingAngleDiff) < Math.Abs(currentRotationalVelocity))
					{
						currentRotationalVelocity = facingAngleDiff; // Snap to target to prevent overshooting
					}

					// Update the CurrentRotationalVelocity field
					aircraft.CurrentRotationalVelocity = new WAngle(currentRotationalVelocity);

					// Apply rotational velocity to Facing
					var newFacingAngle = (aircraft.Facing.Angle + currentRotationalVelocity) % 1024;
					if (newFacingAngle < 0) newFacingAngle += 1024;
					aircraft.Facing = new WAngle(newFacingAngle);
				}
				else
				{
					// If aligned, stop rotating
					aircraft.CurrentRotationalVelocity = WAngle.Zero;
				}
			}

			// Call FlyTick with no additional move (momentum is handled above)
			FlyTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude, WVec.Zero);

			return false; // Tick
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
