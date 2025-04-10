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
            FlyTick(self, aircraft, desiredFacing, desiredAltitude, aircraft.FlyStep(aircraft.Info.Speed, desiredFacing), idleTurn);
        }

        public static bool VerticalTakeOffOrLandTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, bool idleTurn = false)
        {
            var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
            var move = WVec.Zero;

            var turnSpeed = idleTurn ? aircraft.IdleTurnSpeed ?? aircraft.TurnSpeed : aircraft.TurnSpeed;
            aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing, turnSpeed);

            if (dat != desiredAltitude)
            {
                var maxDelta = aircraft.Info.AltitudeVelocity.Length;
                var deltaZ = (desiredAltitude.Length - dat.Length).Clamp(-maxDelta, maxDelta);
                move += new WVec(0, 0, deltaZ);
            }
            else
                return false;

            aircraft.SetPosition(self, self.CenterPosition + move);
            return true;
        }

        public override bool Tick(Actor self)
        {
            if (aircraft.ForceLanding)
                Cancel(self);

            var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
            var isLanded = dat <= aircraft.LandAltitude;

			// Decelerate on each tick
			var d = 2;
			aircraft.CurrentMomentum = new WVec(
				aircraft.CurrentMomentum.X > 0 ? Math.Max(aircraft.CurrentMomentum.X - d, 0) : Math.Min(aircraft.CurrentMomentum.X + d, 0),
				aircraft.CurrentMomentum.Y > 0 ? Math.Max(aircraft.CurrentMomentum.Y - d, 0) : Math.Min(aircraft.CurrentMomentum.Y + d, 0),
				aircraft.CurrentMomentum.Z > 0 ? Math.Max(aircraft.CurrentMomentum.Z - d, 0) : Math.Min(aircraft.CurrentMomentum.Z + d, 0)
			);

            if (isLanded && aircraft.IsTraitPaused)
                return false;

            if (IsCanceling)
            {
                var landWhenIdle = aircraft.Info.IdleBehavior == IdleBehaviorType.Land;
                var skipHeightAdjustment = landWhenIdle && self.CurrentActivity.IsCanceling && self.CurrentActivity.NextActivity == null;
                if (aircraft.Info.CanHover && !skipHeightAdjustment && dat != aircraft.Info.CruiseAltitude)
                {
                    if (isLanded)
                        QueueChild(new TakeOff(self));
                    else
                        VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
                    return false;
                }

				// If we are still moving, we need to decelerate before fully canceling
                if (self.CurrentActivity.NextActivity == null && aircraft.CurrentMomentum.LengthSquared > 64)
				{
					FlyTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);

					return false;
            	}

                return true;
            }
            else if (isLanded)
            {
                QueueChild(new TakeOff(self));
                return false;
            }

            target = target.Recalculate(self.Owner, out var targetIsHiddenActor);
            if (!targetIsHiddenActor && target.Type == TargetType.Actor)
                lastVisibleTarget = Target.FromTargetPositions(target);

            useLastVisibleTarget = targetIsHiddenActor || !target.IsValidFor(self);

            if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self))
                return true;

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

            if (insideMaxRange && !insideMinRange && isFinalWaypoint)
            {
                // Decelerate to stop
                aircraft.AdjustMomentum(WVec.Zero);
                if (aircraft.CurrentMomentum.LengthSquared < 64)
                    return true;
                FlyTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
                return false;
            }
            else if (insideMinRange)
            {
                var isSlider = aircraft.Info.CanSlide;
                var speed = aircraft.MovementSpeed;
                var move = isSlider ? aircraft.FlyStep(speed, deltaHorizontal.Yaw) : aircraft.FlyStep(speed, aircraft.Facing);
                if (isSlider)
                    FlyTick(self, aircraft, deltaHorizontal.Yaw, aircraft.Info.CruiseAltitude, -move);
                else
                    FlyTick(self, aircraft, deltaHorizontal.Yaw + new WAngle(512), aircraft.Info.CruiseAltitude, move);
                return false;
            }

            // Normal movement
            var desiredFacing = deltaHorizontal.LengthSquared != 0 ? deltaHorizontal.Yaw : aircraft.Facing;

            var targetSpeed = aircraft.MovementSpeed;
            var turnSpeed = aircraft.TurnSpeed;

            // Calculate stopping distance
            var stoppingDistance = aircraft.CalculateStoppingDistance();

            // Check if close enough
            if (deltaLengthSquared <= (isFinalWaypoint ? StopThreshold * StopThreshold : WaypointThreshold * WaypointThreshold))
            {
                if (isFinalWaypoint)
                {
                    var targetPosAtCruise = new WPos(checkTarget.CenterPosition.X, checkTarget.CenterPosition.Y, pos.Z);
                    aircraft.SetPosition(self, targetPosAtCruise);
                    aircraft.CurrentMomentum = WVec.Zero;
                    aircraft.CurrentSpeed = 0;
                    return true;
                }
                else
                {
                    return true; // Proceed to next waypoint
                }
            }

            // Calculate turn radius
            var turnRadius = CalculateTurnRadius(aircraft.CurrentSpeed, turnSpeed);

            // Check for early turn
            bool shouldTurn = false;
            if (!isFinalWaypoint && NextActivity is Fly nextFly)
            {
                var nextTarget = nextFly.target;
                var nextDelta = nextTarget.CenterPosition - checkTarget.CenterPosition;
                var nextDeltaHorizontal = new WVec(nextDelta.X, nextDelta.Y, 0);
                var nextYaw = nextDeltaHorizontal.Yaw;
                var nextDistance = nextDeltaHorizontal.Length;

                var angleDiff = Math.Abs((nextYaw - desiredFacing).Angle % 1024);
                if (angleDiff > 512) angleDiff = 1024 - angleDiff;

                var turnTicks = (angleDiff + turnSpeed.Angle - 1) / turnSpeed.Angle;
                var turnDistance = aircraft.CurrentSpeed * turnTicks;

                shouldTurn = deltaLength <= turnDistance;

                if (shouldTurn)
                {
                    desiredFacing = nextYaw;
                    // Return true to clear current waypoint
                    return true;
                }

                // Speed reduction for tight turns (helicopters only)
                if (aircraft.Info.CanSlide && nextDistance <= 2 * turnRadius)
                {
                    var speedReductionFactor = Math.Max(0.5f, (float)nextDistance / (2 * turnRadius));
                    targetSpeed = (int)(aircraft.MovementSpeed * speedReductionFactor);
                    targetSpeed = Math.Max(targetSpeed, aircraft.Info.AccelerationRate * 2);
                }
            }

            // For final waypoint, adjust speed if within stopping distance
            if (isFinalWaypoint && deltaLength <= stoppingDistance)
            {
                targetSpeed = (int)(aircraft.MovementSpeed * (float)deltaLength / stoppingDistance);
                targetSpeed = Math.Max(targetSpeed, aircraft.Info.AccelerationRate);
            }

            // Prevent orbiting for non-sliders
            if (!aircraft.Info.CanSlide)
            {
                var turnCenterFacing = aircraft.Facing + new WAngle(Util.GetTurnDirection(aircraft.Facing, desiredFacing) * 256);
                var turnCenterDir = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(turnCenterFacing)) * turnRadius / 1024;
                var turnCenter = pos + turnCenterDir;
                if ((checkTarget.CenterPosition - turnCenter).HorizontalLengthSquared < turnRadius * turnRadius)
                    desiredFacing = aircraft.Facing;
            }

            // Set desired move
            var desiredMove = aircraft.FlyStep(targetSpeed, desiredFacing);

            // Call FlyTick
            FlyTick(self, aircraft, desiredFacing, aircraft.Info.CruiseAltitude, desiredMove);

            return false;
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
