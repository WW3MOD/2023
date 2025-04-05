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
        readonly List<WPos> positionBuffer = new List<WPos>();
        const int StopThreshold = 128; // Small distance to snap to target (unnoticeable to player)

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

            // Adjust momentum towards desired move
            aircraft.AdjustMomentum(desiredMove);

            // Update facing
            var oldFacing = aircraft.Facing;
            var turnSpeed = aircraft.GetTurnSpeed(idleTurn);
            aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing, turnSpeed);

            // Update roll
            var roll = idleTurn ? aircraft.Info.IdleRoll ?? aircraft.Info.Roll : aircraft.Info.Roll;
            if (roll != WAngle.Zero)
            {
                var desiredRoll = aircraft.Facing == desiredFacing ? WAngle.Zero :
                    new WAngle(roll.Angle * Util.GetTurnDirection(aircraft.Facing, oldFacing));
                aircraft.Roll = Util.TickFacing(aircraft.Roll, desiredRoll, aircraft.Info.RollSpeed);
            }

            // Update pitch
            if (aircraft.Info.Pitch != WAngle.Zero)
                aircraft.Pitch = Util.TickFacing(aircraft.Pitch, aircraft.Info.Pitch, aircraft.Info.PitchSpeed);

            // Adjust vertical movement
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
            FlyTick(self, aircraft, desiredFacing, desiredAltitude, aircraft.FlyStep(aircraft.MovementSpeed, desiredFacing), idleTurn);
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
            var deltaHorizontal = new WVec(delta.X, delta.Y, 0); // Ignore Z for stopping distance
            var deltaLengthSquared = deltaHorizontal.LengthSquared;
            var deltaLength = (int)Math.Sqrt(deltaLengthSquared);

            var insideMaxRange = maxRange.Length > 0 && checkTarget.IsInRange(pos, maxRange);
            var insideMinRange = minRange.Length > 0 && checkTarget.IsInRange(pos, minRange);
            if (insideMaxRange && !insideMinRange)
            {
                aircraft.AdjustMomentum(WVec.Zero);
                if (aircraft.CurrentMomentum.LengthSquared < 64)
                    return true;
                FlyTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
                return false;
            }

            var desiredFacing = deltaLengthSquared != 0 ? deltaHorizontal.Yaw : aircraft.Facing;
            var stoppingDistance = aircraft.CalculateStoppingDistance();

            // Snap to target if within threshold
            if (deltaLengthSquared <= StopThreshold * StopThreshold)
            {
                var targetPosAtCruise = new WPos(checkTarget.CenterPosition.X, checkTarget.CenterPosition.Y, pos.Z); // Keep Z at cruise altitude
                aircraft.SetPosition(self, targetPosAtCruise);
                aircraft.CurrentMomentum = WVec.Zero;
                aircraft.CurrentSpeed = 0;
                return true;
            }

            // Calculate desired move based on distance and stopping requirements
            WVec desiredMove;
            if (deltaLength <= stoppingDistance + nearEnough.Length)
            {
                // Decelerate: Scale speed based on remaining distance
                var targetSpeed = Math.Max(0, deltaLength - nearEnough.Length) * aircraft.Info.DecelerationRate;
                targetSpeed = Math.Min(targetSpeed, aircraft.CurrentSpeed); // Don't exceed current speed
                desiredMove = deltaLengthSquared > 0 ? deltaHorizontal * targetSpeed / deltaLength : WVec.Zero;
            }
            else
            {
                // Accelerate or maintain max speed
                desiredMove = aircraft.FlyStep(aircraft.MovementSpeed, desiredFacing);
            }

            // Apply turn radius only when far from target
            var turnRadius = CalculateTurnRadius(aircraft.MovementSpeed, aircraft.TurnSpeed);
            var turnCenterFacing = aircraft.Facing + new WAngle(Util.GetTurnDirection(aircraft.Facing, desiredFacing) * 256);
            var turnCenterDir = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(turnCenterFacing)) * turnRadius / 1024;
            var turnCenter = aircraft.CenterPosition + turnCenterDir;

            if (deltaLengthSquared > (stoppingDistance + turnRadius) * (stoppingDistance + turnRadius) &&
                (checkTarget.CenterPosition - turnCenter).HorizontalLengthSquared < turnRadius * turnRadius)
                desiredFacing = aircraft.Facing;

            positionBuffer.Add(self.CenterPosition);
            if (positionBuffer.Count > 5)
                positionBuffer.RemoveAt(0);

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
