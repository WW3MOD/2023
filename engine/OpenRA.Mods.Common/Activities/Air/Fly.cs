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

			// The target may become hidden between the initial order request and the first tick (e.g. if queued)
			// Moving to any position (even if quite stale) is still better than immediately giving up
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

		// PITFALL: step-based movement. CanSlide aircraft move via CurrentVelocity in Aircraft.Tick — calling FlyTick on them double-moves unless CurrentVelocity is zeroed first.
		public static void FlyTick(Actor self, Aircraft aircraft, WAngle desiredFacing, WDist desiredAltitude, in WVec moveOverride, bool idleTurn = false)
		{
			var dat = self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition);
			var move = aircraft.Info.CanSlide ? aircraft.FlyStep(desiredFacing) : aircraft.FlyStep(aircraft.Facing);
			if (moveOverride != WVec.Zero)
				move = moveOverride;

			var oldFacing = aircraft.Facing;
			var turnSpeed = aircraft.GetTurnSpeed(idleTurn);
			aircraft.Facing = Util.TickFacing(aircraft.Facing, desiredFacing, turnSpeed);

			var roll = idleTurn ? aircraft.Info.IdleRoll ?? aircraft.Info.Roll : aircraft.Info.Roll;
			if (roll != WAngle.Zero)
			{
				var desiredRoll = aircraft.Facing == desiredFacing ? WAngle.Zero :
					new WAngle(roll.Angle * Util.GetTurnDirection(aircraft.Facing, oldFacing));

				aircraft.Roll = Util.TickFacing(aircraft.Roll, desiredRoll, aircraft.Info.RollSpeed);
			}

			if (aircraft.Info.Pitch != WAngle.Zero)
				aircraft.Pitch = Util.TickFacing(aircraft.Pitch, aircraft.Info.Pitch, aircraft.Info.PitchSpeed);

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
			FlyTick(self, aircraft, desiredFacing, desiredAltitude, WVec.Zero, idleTurn);
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

			aircraft.SetPosition(self, aircraft.CenterPosition + move);
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
			var isSlider = aircraft.Info.CanSlide;

			if (isSlider)
			{
				// CanSlide (helicopter) path: velocity-based movement with precise arrival.
				// All horizontal movement happens in Aircraft.Tick via CurrentVelocity.
				var speed = aircraft.CurrentVelocity.HorizontalLength;
				var distToTarget = delta.HorizontalLength;

				// Precise arrival: when slow enough and close enough, snap to exact target and stop
				if (speed <= aircraft.Info.MaxAcceleration && distToTarget <= aircraft.Info.MaxAcceleration * 3)
				{
					var targetPos = checkTarget.CenterPosition;
					aircraft.SetPosition(self, new WPos(targetPos.X, targetPos.Y, aircraft.CenterPosition.Z));
					aircraft.CurrentVelocity = WVec.Zero;

					// Don't climb to CruiseAltitude if we're about to land — go straight to landing
					if (dat != aircraft.Info.CruiseAltitude && !(NextActivity is Land))
					{
						VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
						return false;
					}

					return true;
				}

				// Inside the target annulus (weapon range), stop here
				var insideMaxRange = maxRange.Length > 0 && checkTarget.IsInRange(pos, maxRange);
				var insideMinRange = minRange.Length > 0 && checkTarget.IsInRange(pos, minRange);
				if (insideMaxRange && !insideMinRange)
				{
					aircraft.CurrentVelocity = WVec.Zero;
					return true;
				}

				var desiredFacing = delta.HorizontalLengthSquared != 0 ? delta.Yaw : aircraft.Facing;

				// Helicopters always decelerate toward their target — even when activities are
				// queued after (like Land). This prevents the jarring instant-stop snap that occurs
				// when isFinalWaypoint is false and the helicopter flies at full speed until overshoot.
				var acceleration = aircraft.CalculateAccelerationToWaypoint(checkTarget.CenterPosition, true);
				aircraft.RequestedAcceleration = new WVec(acceleration.X, acceleration.Y, 0);

				// Inside the minimum range, reverse
				if (insideMinRange)
				{
					aircraft.RequestedAcceleration = new WVec(-acceleration.X, -acceleration.Y, 0);
					return false;
				}

				// HACK: Consider ourselves blocked if we have moved by less than 64 WDist in the last five ticks
				if (positionBuffer.Count >= 5 && (positionBuffer.Last() - positionBuffer[0]).LengthSquared < 4096 &&
					delta.HorizontalLengthSquared <= nearEnough.LengthSquared)
				{
					aircraft.CurrentVelocity = WVec.Zero;
					return true;
				}

				// Predict next frame's movement (current velocity + acceleration we just set)
				// to detect overshoot before it happens
				var predictedVel = new WVec(
					aircraft.CurrentVelocity.X + acceleration.X,
					aircraft.CurrentVelocity.Y + acceleration.Y,
					0);
				var predictedSpeed = predictedVel.Length;
				if (predictedSpeed > aircraft.Info.Speed)
					predictedVel = predictedVel * aircraft.Info.Speed / predictedSpeed;

				if (delta.HorizontalLengthSquared < predictedVel.HorizontalLengthSquared)
				{
					// Would overshoot — only snap if speed is low enough for an invisible correction
					if (speed <= aircraft.Info.MaxAcceleration * 2)
					{
						var targetPos = checkTarget.CenterPosition;
						aircraft.SetPosition(self, new WPos(targetPos.X, targetPos.Y, aircraft.CenterPosition.Z));
						aircraft.CurrentVelocity = WVec.Zero;
						aircraft.RequestedAcceleration = WVec.Zero;

						if (dat != aircraft.Info.CruiseAltitude && !(NextActivity is Land))
						{
							VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
							return false;
						}

						return true;
					}

					// At high speed: emergency brake instead of snapping (visually jarring)
					var brakeDir = aircraft.CurrentVelocity.HorizontalLength > 0
						? aircraft.CurrentVelocity * (-aircraft.Info.MaxAcceleration) / aircraft.CurrentVelocity.HorizontalLength
						: WVec.Zero;
					aircraft.RequestedAcceleration = new WVec(brakeDir.X, brakeDir.Y, 0);
				}

				positionBuffer.Add(self.CenterPosition);
				if (positionBuffer.Count > 5)
					positionBuffer.RemoveAt(0);

				// Gradually climb/descend toward CruiseAltitude while flying
				// This allows helicopters to start flying before reaching full altitude (after halfway takeoff)
				if (dat != aircraft.Info.CruiseAltitude)
				{
					var maxAltDelta = aircraft.Info.AltitudeVelocity.Length;
					var altDiff = aircraft.Info.CruiseAltitude.Length - dat.Length;
					var deltaZ = altDiff > 0 ? System.Math.Min(altDiff, maxAltDelta) : System.Math.Max(altDiff, -maxAltDelta);
					aircraft.SetPosition(self, aircraft.CenterPosition + new WVec(0, 0, deltaZ));
				}

				// CanSlide movement is applied in Aircraft.Tick via CurrentVelocity
				return false;
			}
			else
			{
				// Fixed-wing path: step-based movement (unchanged from original logic)
				var stopPos = aircraft.CalculateStopPosition();
				var stopDelta = checkTarget.CenterPosition - stopPos;

				if (stopDelta.HorizontalLengthSquared < 512 * 512)
					return true;

				var insideMaxRange = maxRange.Length > 0 && checkTarget.IsInRange(pos, maxRange);
				var insideMinRange = minRange.Length > 0 && checkTarget.IsInRange(pos, minRange);
				if (insideMaxRange && !insideMinRange)
					return true;

				var desiredFacing = delta.HorizontalLengthSquared != 0 ? delta.Yaw : aircraft.Facing;
				var isFinalWaypoint = NextActivity == null;
				var acceleration = aircraft.CalculateAccelerationToWaypoint(checkTarget.CenterPosition, isFinalWaypoint);
				aircraft.RequestedAcceleration = new WVec(acceleration.X, acceleration.Y, 0);

				var move = aircraft.FlyStep(aircraft.Facing);

				if (insideMinRange)
				{
					FlyTick(self, aircraft, desiredFacing + new WAngle(512), aircraft.Info.CruiseAltitude, move);
					return false;
				}

				// HACK: Consider ourselves blocked if we have moved by less than 64 WDist in the last five ticks
				if (positionBuffer.Count >= 5 && (positionBuffer.Last() - positionBuffer[0]).LengthSquared < 4096 &&
					delta.HorizontalLengthSquared <= nearEnough.LengthSquared)
					return true;

				// The next move would overshoot
				if (delta.HorizontalLengthSquared < move.HorizontalLengthSquared)
				{
					// For VTOL landing to succeed, it must reach the exact target position
					if (aircraft.Info.VTOL)
					{
						if (delta.HorizontalLengthSquared != 0)
						{
							var deltaMove = new WVec(delta.X, delta.Y, 0);
							FlyTick(self, aircraft, desiredFacing, dat, deltaMove);
						}

						if (dat != aircraft.Info.CruiseAltitude)
						{
							Fly.VerticalTakeOffOrLandTick(self, aircraft, aircraft.Facing, aircraft.Info.CruiseAltitude);
							return false;
						}
					}

					return true;
				}

				// Using the turn rate, compute a hypothetical circle traced by a continuous turn.
				// If it contains the destination point, it's unreachable without more complex maneuvering.
				var turnRadius = CalculateTurnRadius(aircraft.MovementSpeed, aircraft.TurnSpeed);

				// The current facing is a tangent of the minimal turn circle.
				// Make a perpendicular vector, and use it to locate the turn's center.
				var turnCenterFacing = aircraft.Facing + new WAngle(Util.GetTurnDirection(aircraft.Facing, desiredFacing) * 256);

				var turnCenterDir = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(turnCenterFacing));
				turnCenterDir *= turnRadius;
				turnCenterDir /= 1024;

				// Compare with the target point, and keep flying away if it's inside the circle.
				var turnCenter = aircraft.CenterPosition + turnCenterDir;
				if ((checkTarget.CenterPosition - turnCenter).HorizontalLengthSquared < turnRadius * turnRadius)
					desiredFacing = aircraft.Facing;

				positionBuffer.Add(self.CenterPosition);
				if (positionBuffer.Count > 5)
					positionBuffer.RemoveAt(0);

				FlyTick(self, aircraft, desiredFacing, aircraft.Info.CruiseAltitude);
				return false;
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
			// turnSpeed -> divide into 256 to get the number of ticks per complete rotation
			// speed -> multiply to get distance travelled per rotation (circumference)
			// 180 -> divide by 2*pi to get the turn radius: 180==1024/(2*pi), with some extra leeway
			return turnSpeed.Angle > 0 ? 180 * speed / turnSpeed.Angle : 0;
		}
	}
}
