using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
    public enum IdleBehaviorType
    {
        None,
        Land,
        ReturnToBase,
        LeaveMap,
        LeaveMapAtClosestEdge
    }

    public class AircraftInfo : PausableConditionalTraitInfo, IPositionableInfo, IFacingInfo, IMoveInfo, ICruiseAltitudeInfo,
        IActorPreviewInitInfo, IEditorActorOptions
    {
        [Desc("Behavior when aircraft becomes idle. Options are Land, ReturnToBase, LeaveMap, and None.")]
        public readonly IdleBehaviorType IdleBehavior = IdleBehaviorType.None;

        public readonly WDist CruiseAltitude = new WDist(1280);

        [Desc("Whether the aircraft can be repulsed.")]
        public readonly bool Repulsable = true;

        [Desc("The distance it tries to maintain from other aircraft if repulsable.")]
        public readonly WDist IdleSeparation = new WDist(3072);

        [Desc("The distance it must maintain from other aircraft if repulsable.")]
        public readonly WDist MinSeparation = new WDist(256);

        [Desc("The speed at which the aircraft is repulsed from other aircraft. Specify -1 for max movement speed.")]
        public readonly int RepulsionSpeed = 140;

        public readonly WAngle InitialFacing = WAngle.Zero;

        [Desc("Speed at which the actor turns.")]
        public readonly WAngle TurnSpeed = new WAngle(512);

        [Desc("Turn speed to apply when aircraft flies in circles while idle. Defaults to TurnSpeed if undefined.")]
        public readonly WAngle? IdleTurnSpeed = null;

        [Desc("Acceleration rate when speeding up (in units per tick).")]
        public readonly int AccelerationRate = 5;

        [Desc("Deceleration rate when slowing down (in units per tick).")]
        public readonly int DecelerationRate = 7;

        [Desc("Maximum flight speed when cruising.")]
        public readonly int Speed = 150;

        [Desc("If non-negative, force the aircraft to move in circles at this speed when idle (a speed of 0 means don't move).")]
        public readonly int IdleSpeed = -1;

        [Desc("Body pitch when flying forwards. Only relevant for voxel aircraft.")]
        public readonly WAngle Pitch = WAngle.Zero;

        [Desc("Pitch steps to apply each tick when starting/stopping.")]
        public readonly WAngle PitchSpeed = WAngle.Zero;

        [Desc("Body roll when turning. Only relevant for voxel aircraft.")]
        public readonly WAngle Roll = WAngle.Zero;

        [Desc("Body roll to apply when aircraft flies in circles while idle. Defaults to Roll if undefined.")]
        public readonly WAngle? IdleRoll = null;

        [Desc("Roll steps to apply each tick when turning.")]
        public readonly WAngle RollSpeed = WAngle.Zero;

        [Desc("Minimum altitude where this aircraft is considered airborne.")]
        public readonly int MinAirborneAltitude = 1;

        public readonly HashSet<string> LandableTerrainTypes = new HashSet<string>();

        [Desc("Can the actor be ordered to move in to shroud?")]
        public readonly bool MoveIntoShroud = true;

        [Desc("e.g. crate, wall, infantry")]
        public readonly BitSet<PassClass> Crushes = default;

        [Desc("Types of damage that are caused while crushing. Leave empty for no damage types.")]
        public readonly BitSet<DamageType> CrushDamageTypes = default;

        [VoiceReference]
        public readonly string Voice = "Action";

        [Desc("Color to use for the target line for regular move orders.")]
        public readonly Color TargetLineColor = Color.Green;

        [GrantedConditionReference]
        [Desc("The condition to grant to self while airborne.")]
        public readonly string AirborneCondition = null;

        [GrantedConditionReference]
        [Desc("The condition to grant to self while at cruise altitude.")]
        public readonly string CruisingCondition = null;

        [Desc("Can the actor hover in place mid-air? If not, then the actor will have to remain in motion.")]
        public readonly bool CanHover = false;

        [Desc("Can the actor immediately change direction without turning first?")]
        public readonly bool CanSlide = false;

		[Desc("Speed threshold below which helicopters can slide (in units/tick). Above this, they turn like airplanes.")]
        public readonly int SlideSpeedThreshold = 30;

        [Desc("Maximum angle helicopters can slide relative to facing (in degrees).")]
        public readonly int MaxSlideAngle = 45;

        [Desc("Does the actor land and take off vertically?")]
        public readonly bool VTOL = false;

        [Desc("Does this VTOL actor need to turn before landing (on terrain)?")]
        public readonly bool TurnToLand = false;

        [Desc("Does this actor automatically take off after resupplying?")]
        public readonly bool TakeOffOnResupply = false;

        [Desc("Does this actor automatically take off after creation?")]
        public readonly bool TakeOffOnCreation = true;

        [Desc("Can this actor be given an explicit land order using the force-move modifier?")]
        public readonly bool CanForceLand = true;

        [Desc("Altitude at which the aircraft considers itself landed.")]
        public readonly WDist LandAltitude = WDist.Zero;

        [Desc("Range to search for an alternative landing location if the ordered cell is blocked.")]
        public readonly WDist LandRange = WDist.FromCells(5);

        [Desc("How fast this actor ascends or descends during horizontal movement.")]
        public readonly WAngle MaximumPitch = WAngle.FromDegrees(10);

        [Desc("How fast this actor ascends or descends when moving vertically only.")]
        public readonly WDist AltitudeVelocity = new WDist(43);

        [Desc("Sounds to play when the actor is taking off.")]
        public readonly string[] TakeoffSounds = Array.Empty<string>();

        [Desc("Sounds to play when the actor is landing.")]
        public readonly string[] LandingSounds = Array.Empty<string>();

        [Desc("The distance of the resupply base that the aircraft will wait for its turn.")]
        public readonly WDist WaitDistanceFromResupplyBase = new WDist(3072);

        [Desc("The number of ticks that a airplane will wait to make a new search for an available airport.")]
        public readonly int NumberOfTicksToVerifyAvailableAirport = 150;

        [Desc("Facing to use for actor previews (map editor, color picker, etc)")]
        public readonly WAngle PreviewFacing = new WAngle(384);

        [Desc("Display order for the facing slider in the map editor")]
        public readonly int EditorFacingDisplayOrder = 3;

        [ConsumedConditionReference]
        [Desc("Boolean expression defining the condition under which the regular (non-force) move cursor is disabled.")]
        public readonly BooleanExpression RequireForceMoveCondition = null;

        [CursorReference]
        [Desc("Cursor to display when a move order can be issued at target location.")]
        public readonly string Cursor = "move";

        [CursorReference]
        [Desc("Cursor to display when a move order cannot be issued at target location.")]
        public readonly string BlockedCursor = "move-blocked";

        [CursorReference]
        [Desc("Cursor to display when able to land at target building.")]
        public readonly string EnterCursor = "enter";

        [CursorReference]
        [Desc("Cursor to display when unable to land at target building.")]
        public readonly string EnterBlockedCursor = "enter-blocked";

        public WAngle GetInitialFacing() { return InitialFacing; }
        public WDist GetCruiseAltitude() { return CruiseAltitude; }
        public Color GetTargetLineColor() { return TargetLineColor; }

        public override object Create(ActorInitializer init) { return new Aircraft(init, this); }

        IEnumerable<ActorInit> IActorPreviewInitInfo.ActorPreviewInits(ActorInfo ai, ActorPreviewType type)
        {
            yield return new FacingInit(PreviewFacing);
        }

        public IReadOnlyDictionary<CPos, SubCell> OccupiedCells(ActorInfo info, CPos location, SubCell subCell = SubCell.Any) { return new Dictionary<CPos, SubCell>(); }

        bool IOccupySpaceInfo.SharesCell => false;

        public bool CanEnterCell(World world, Actor self, CPos cell, SubCell subCell = SubCell.FullCell, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
        {
            if (!world.Map.Contains(cell))
                return false;

            var type = world.Map.GetTerrainInfo(cell).Type;
            if (!LandableTerrainTypes.Contains(type))
                return false;

            if (check == BlockedByActor.None)
                return true;

            return !world.ActorMap.GetActorsAt(cell).Any(x => x != ignoreActor);
        }

        IEnumerable<EditorActorOption> IEditorActorOptions.ActorOptions(ActorInfo ai, World world)
        {
            yield return new EditorActorSlider("Facing", EditorFacingDisplayOrder, 0, 1023, 8,
                actor =>
                {
                    var init = actor.GetInitOrDefault<FacingInit>(this);
                    return (init != null ? init.Value : InitialFacing).Angle;
                },
                (actor, value) => actor.ReplaceInit(new FacingInit(new WAngle((int)value))));
        }
    }

    public class Aircraft : PausableConditionalTrait<AircraftInfo>, ITick, ISync, IFacing, IPositionable, IMove,
        INotifyAddedToWorld, INotifyRemovedFromWorld, INotifyActorDisposing, INotifyBecomingIdle, ICreationActivity,
        IActorPreviewInitModifier, IDeathActorInitModifier, IIssueDeployOrder, IIssueOrder, IResolveOrder, IOrderVoice
    {
        readonly Actor self;

        Repairable repairable;
        Rearmable rearmable;
        IAircraftCenterPositionOffset[] positionOffsets;
        IDisposable reservation;
        IEnumerable<int> speedModifiers;
        INotifyMoving[] notifyMoving;
        INotifyCenterPositionChanged[] notifyCenterPositionChanged;
        IOverrideAircraftLanding overrideAircraftLanding;

        WRot orientation;

        [Sync]
        public WAngle Facing
        {
            get => orientation.Yaw;
            set => orientation = orientation.WithYaw(value);
        }

        public WAngle Pitch
        {
            get => orientation.Pitch;
            set => orientation = orientation.WithPitch(value);
        }

        public WAngle Roll
        {
            get => orientation.Roll;
            set => orientation = orientation.WithRoll(value);
        }

        public WRot Orientation => orientation;

        [Sync]
        public WPos CenterPosition { get; private set; }

        public CPos TopLeft => self.World.Map.CellContaining(CenterPosition);
        public WAngle TurnSpeed => IsTraitDisabled || IsTraitPaused ? WAngle.Zero : Info.TurnSpeed;
        public WAngle? IdleTurnSpeed => IsTraitDisabled || IsTraitPaused ? null : Info.IdleTurnSpeed;

        public WAngle GetTurnSpeed(bool isIdleTurn)
        {
            if ((isIdleTurn && IdleMovementSpeed == 0) || MovementSpeed == 0)
                return WAngle.Zero;

            var turnSpeed = isIdleTurn ? IdleTurnSpeed ?? TurnSpeed : TurnSpeed;
            return new WAngle(Util.ApplyPercentageModifiers(turnSpeed.Angle, speedModifiers).Clamp(1, 1024));
        }

        public Actor ReservedActor { get; private set; }
        public bool MayYieldReservation { get; private set; }
        public bool ForceLanding { get; private set; }

        (CPos, SubCell)[] landingCells = Array.Empty<(CPos, SubCell)>();
        bool requireForceMove;

        readonly int creationActivityDelay;

        bool notify = true;

        public static WPos GroundPosition(Actor self)
        {
            return self.CenterPosition - new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(self.CenterPosition));
        }

        public bool AtLandAltitude => self.World.Map.DistanceAboveTerrain(GetPosition()) == LandAltitude;

        bool airborne;
        bool cruising;
        int airborneToken = Actor.InvalidConditionToken;
        int cruisingToken = Actor.InvalidConditionToken;

        [Sync]
        public WVec CurrentMomentum { get; set; } = WVec.Zero; // Current velocity vector (made public for Fly.cs)
        [Sync]
        public int CurrentSpeed; // Current speed magnitude (made public for Fly.cs, PascalCase)

        MovementType movementTypes;
        WPos cachedPosition;
        WAngle cachedFacing;

        public Aircraft(ActorInitializer init, AircraftInfo info)
            : base(info)
        {
            self = init.Self;

            var locationInit = init.GetOrDefault<LocationInit>();
            if (locationInit != null)
                SetPosition(self, locationInit.Value);

            var centerPositionInit = init.GetOrDefault<CenterPositionInit>();
            if (centerPositionInit != null)
                SetPosition(self, centerPositionInit.Value);

            Facing = init.GetValue<FacingInit, WAngle>(Info.InitialFacing);
            CurrentSpeed = 0; // Start at 0 speed, will accelerate on takeoff
        }

        public void AdjustMomentum(WVec desiredMove)
        {
            var targetSpeed = desiredMove.Length;
            var maxSpeed = Info.Speed;

            if (targetSpeed > 0)
            {
                var desiredDir = desiredMove * 1024 / targetSpeed;
                var currentDir = CurrentMomentum.Length > 0 ? CurrentMomentum * 1024 / CurrentMomentum.Length : WVec.Zero;

                var newDir = currentDir == WVec.Zero ? desiredDir :
                    (currentDir + (desiredDir - currentDir) * Info.AccelerationRate / 32);
                var magnitude = newDir.Length;
                if (magnitude > 1024)
                    newDir = newDir * 1024 / magnitude;

                // Gradually accelerate or decelerate to target speed
                if (CurrentSpeed < targetSpeed)
                    CurrentSpeed = Math.Min(CurrentSpeed + Info.AccelerationRate, Math.Min(targetSpeed, maxSpeed));
                else if (CurrentSpeed > targetSpeed)
                    CurrentSpeed = Math.Max(CurrentSpeed - Info.DecelerationRate, targetSpeed);

                CurrentMomentum = newDir * CurrentSpeed / 1024;
            }
            else
            {
                CurrentSpeed = Math.Max(CurrentSpeed - Info.DecelerationRate, 0);
                CurrentMomentum = CurrentMomentum.Length > 0 ?
                    CurrentMomentum * CurrentSpeed / CurrentMomentum.Length : WVec.Zero;
            }
        }

        public int CalculateTurnRadius()
        {
            return Info.TurnSpeed.Angle > 0 ? 180 * CurrentSpeed / Info.TurnSpeed.Angle : 0;
        }

        public int CalculateStoppingDistance()
        {
            return CurrentSpeed * CurrentSpeed / (2 * Math.Max(1, Info.DecelerationRate));
        }

        public WVec FlyStep(int speed, WAngle facing)
        {
            return new WVec(0, -speed, 0).Rotate(WRot.FromYaw(facing));
        }

        public WDist LandAltitude
        {
            get
            {
                var alt = Info.LandAltitude;
                foreach (var offset in positionOffsets)
                    alt -= new WDist(offset.PositionOffset.Z);

                return alt;
            }
        }

        public WPos GetPosition()
        {
            var pos = self.CenterPosition;
            foreach (var offset in positionOffsets)
                pos += offset.PositionOffset;

            return pos;
        }

        public override IEnumerable<VariableObserver> GetVariableObservers()
        {
            foreach (var observer in base.GetVariableObservers())
                yield return observer;

            if (Info.RequireForceMoveCondition != null)
                yield return new VariableObserver(RequireForceMoveConditionChanged, Info.RequireForceMoveCondition.Variables);
        }

        void RequireForceMoveConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
        {
            requireForceMove = Info.RequireForceMoveCondition.Evaluate(conditions);
        }

        protected override void Created(Actor self)
        {
            repairable = self.TraitOrDefault<Repairable>();
            rearmable = self.TraitOrDefault<Rearmable>();
            speedModifiers = self.TraitsImplementing<ISpeedModifier>().ToArray().Select(sm => sm.GetSpeedModifier());
            cachedPosition = self.CenterPosition;
            notifyMoving = self.TraitsImplementing<INotifyMoving>().ToArray();
            positionOffsets = self.TraitsImplementing<IAircraftCenterPositionOffset>().ToArray();
            overrideAircraftLanding = self.TraitOrDefault<IOverrideAircraftLanding>();
            notifyCenterPositionChanged = self.TraitsImplementing<INotifyCenterPositionChanged>().ToArray();
            base.Created(self);
        }

        void INotifyAddedToWorld.AddedToWorld(Actor self)
        {
            AddedToWorld(self);
        }

        protected virtual void AddedToWorld(Actor self)
        {
            self.World.AddToMaps(self, this);

            var altitude = self.World.Map.DistanceAboveTerrain(CenterPosition);
            if (altitude.Length >= Info.MinAirborneAltitude)
                OnAirborneAltitudeReached();
            if (altitude == Info.CruiseAltitude)
                OnCruisingAltitudeReached();
        }

        void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
        {
            RemovedFromWorld(self);
        }

        protected virtual void RemovedFromWorld(Actor self)
        {
            UnReserve();
            self.World.RemoveFromMaps(self, this);

            OnCruisingAltitudeLeft();
            OnAirborneAltitudeLeft();
        }

        void ITick.Tick(Actor self)
        {
            Tick(self);
        }

        protected virtual void Tick(Actor self)
        {
            // Handle landing/takeoff logic when paused
            if (!ForceLanding && IsTraitPaused && airborne && CanLand(self.Location)
                && !((self.CurrentActivity is Land) || self.CurrentActivity is Turn))
            {
                self.QueueActivity(false, new Land(self));
                ForceLanding = true;
            }

            if (ForceLanding && !IsTraitPaused && !cruising && !(self.CurrentActivity is TakeOff))
            {
                ForceLanding = false;
                if (Info.IdleBehavior != IdleBehaviorType.Land)
                    self.QueueActivity(false, new TakeOff(self));
            }

            var oldCachedFacing = cachedFacing;
            cachedFacing = Facing;

            var oldCachedPosition = cachedPosition;
            cachedPosition = self.CenterPosition;

            var newMovementTypes = MovementType.None;
            if (oldCachedFacing != Facing)
                newMovementTypes |= MovementType.Turn;

            if ((oldCachedPosition - cachedPosition).HorizontalLengthSquared != 0)
                newMovementTypes |= MovementType.Horizontal;

            if ((oldCachedPosition - cachedPosition).VerticalLengthSquared != 0)
                newMovementTypes |= MovementType.Vertical;

            CurrentMovementTypes = newMovementTypes;

            if (!CurrentMovementTypes.HasMovementType(MovementType.Horizontal))
            {
                if (Info.Roll != WAngle.Zero && Roll != WAngle.Zero)
                    Roll = Util.TickFacing(Roll, WAngle.Zero, Info.RollSpeed);

                if (Info.Pitch != WAngle.Zero && Pitch != WAngle.Zero)
                    Pitch = Util.TickFacing(Pitch, WAngle.Zero, Info.PitchSpeed);

                // Decelerate when not moving horizontally (outside Fly activity)
                if (CurrentSpeed > 0)
                {
                    CurrentSpeed = Math.Max(0, CurrentSpeed - Info.DecelerationRate);
                    CurrentMomentum = CurrentMomentum.Length > 0 ?
                        CurrentMomentum * CurrentSpeed / CurrentMomentum.Length : WVec.Zero;
                }
            }

            Repulse();
        }

        public void Repulse()
        {
            var repulsionForce = GetRepulsionForce();
            if (repulsionForce == WVec.Zero)
                return;

            var speed = Info.RepulsionSpeed != -1 ? Info.RepulsionSpeed : MovementSpeed;
            var move = FlyStep(speed, repulsionForce.Yaw);
            AdjustMomentum(move);
            notify = false;
            SetPosition(self, CenterPosition + CurrentMomentum);
            notify = true;
        }

        public virtual WVec GetRepulsionForce()
        {
            if (!Info.Repulsable || !cruising)
                return WVec.Zero;

            if (reservation != null)
            {
                var distanceFromReservationActor = (ReservedActor.CenterPosition - self.CenterPosition).HorizontalLength;
                if (distanceFromReservationActor < Info.WaitDistanceFromResupplyBase.Length)
                    return WVec.Zero;
            }

            var repulsionForce = WVec.Zero;
            foreach (var actor in self.World.FindActorsInCircle(self.CenterPosition, Info.IdleSeparation))
            {
                if (actor.IsDead)
                    continue;

                var ai = actor.Info.TraitInfoOrDefault<AircraftInfo>();
                if (ai == null || !ai.Repulsable || ai.CruiseAltitude != Info.CruiseAltitude)
                    continue;

                repulsionForce += GetRepulsionForce(actor);
            }

            if (!self.World.Map.Contains(self.Location))
            {
                var center = WPos.Lerp(self.World.Map.ProjectedTopLeft, self.World.Map.ProjectedBottomRight, 1, 2);
                repulsionForce += new WVec(0, 1024, 0).Rotate(WRot.FromYaw((self.CenterPosition - center).Yaw));
            }

            if (Info.CanSlide)
                return repulsionForce;

            var currentDir = FlyStep(Facing);
            var length = currentDir.HorizontalLength * repulsionForce.HorizontalLength;
            if (length == 0)
                return WVec.Zero;

            var dot = WVec.Dot(currentDir, repulsionForce) / length;
            return dot >= 0 ? repulsionForce : WVec.Zero;
        }

        public WVec GetRepulsionForce(Actor other)
        {
            if (self == other || other.CenterPosition.Z < self.CenterPosition.Z)
                return WVec.Zero;

            var d = self.CenterPosition - other.CenterPosition;
            var distSq = d.HorizontalLengthSquared;

            if (distSq > Info.IdleSeparation.LengthSquared)
                return WVec.Zero;

            if (distSq < 1)
            {
                var yaw = self.World.SharedRandom.Next(0, 1023);
                var rot = new WRot(WAngle.Zero, WAngle.Zero, new WAngle(yaw));
                return new WVec(1024, 0, 0).Rotate(rot);
            }

            if (distSq < Info.MinSeparation.LengthSquared)
                return (d * 1024 * 8) / (int)distSq;

            return d * 1024 * 8 / (int)distSq;
        }

        public Actor GetActorBelow()
        {
            if (self.World.Map.DistanceAboveTerrain(CenterPosition) != LandAltitude)
                return null;

            return self.World.ActorMap.GetActorsAt(self.Location)
                .FirstOrDefault(a => a.Info.HasTraitInfo<ReservableInfo>());
        }

        public void MakeReservation(Actor target)
        {
            UnReserve();
            var reservable = target.TraitOrDefault<Reservable>();
            if (reservable != null)
            {
                reservation = reservable.Reserve(target, self, this);
                ReservedActor = target;
            }
        }

        public void AllowYieldingReservation()
        {
            if (reservation == null)
                return;

            MayYieldReservation = true;
        }

        public void UnReserve()
        {
            if (reservation == null)
                return;

            reservation.Dispose();
            reservation = null;
            ReservedActor = null;
            MayYieldReservation = false;
        }

        bool AircraftCanEnter(Actor a, TargetModifiers modifiers)
        {
            if (requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove))
                return false;

            return AircraftCanEnter(a);
        }

        bool AircraftCanEnter(Actor a)
        {
            if (self.AppearsHostileTo(a))
                return false;

            var canRearmAtActor = rearmable != null && rearmable.Info.RearmActors.Contains(a.Info.Name);
            var canRepairAtActor = repairable != null && repairable.Info.RepairActors.Contains(a.Info.Name);

            return canRearmAtActor || canRepairAtActor;
        }

        bool AircraftCanResupplyAt(Actor a, bool allowedToForceEnter = false)
        {
            if (self.AppearsHostileTo(a))
                return false;

            var canRearmAtActor = rearmable != null && rearmable.Info.RearmActors.Contains(a.Info.Name);
            var canRepairAtActor = repairable != null && repairable.Info.RepairActors.Contains(a.Info.Name);

            var allowedToEnterRearmer = canRearmAtActor && (allowedToForceEnter || rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo));
            var allowedToEnterRepairer = canRepairAtActor && (allowedToForceEnter || self.GetDamageState() != DamageState.Undamaged);

            return allowedToEnterRearmer || allowedToEnterRepairer;
        }

        public int MovementSpeed => !IsTraitDisabled && !IsTraitPaused ? Util.ApplyPercentageModifiers(Info.Speed, speedModifiers) : 0;
        public int IdleMovementSpeed => Info.IdleSpeed < 0 ? MovementSpeed :
            !IsTraitDisabled && !IsTraitPaused ? Util.ApplyPercentageModifiers(Info.IdleSpeed, speedModifiers) : 0;

        public (CPos Cell, SubCell SubCell)[] OccupiedCells()
        {
            return landingCells;
        }

        public WVec FlyStep(WAngle facing)
        {
            return FlyStep(CurrentSpeed, facing);
        }

        public CPos? FindLandingLocation(CPos targetCell, WDist maxSearchDistance)
        {
            if (CanLand(targetCell, blockedByMobile: false))
                return targetCell;

            var cellRange = (maxSearchDistance.Length + 1023) / 1024;
            var centerPosition = self.World.Map.CenterOfCell(targetCell);
            foreach (var c in self.World.Map.FindTilesInCircle(targetCell, cellRange))
            {
                if (!CanLand(c, blockedByMobile: false))
                    continue;

                var delta = self.World.Map.CenterOfCell(c) - centerPosition;
                if (delta.LengthSquared < maxSearchDistance.LengthSquared)
                    return c;
            }

            return null;
        }

        public bool CanLand(IEnumerable<CPos> cells, Actor dockingActor = null, bool blockedByMobile = true)
        {
            foreach (var c in cells)
                if (!CanLand(c, dockingActor, blockedByMobile))
                    return false;

            return true;
        }

        public bool CanLand(CPos cell, Actor dockingActor = null, bool blockedByMobile = true)
        {
            if (!self.World.Map.Contains(cell))
                return false;

            foreach (var otherActor in self.World.ActorMap.GetActorsAt(cell))
                if (IsBlockedBy(self, otherActor, dockingActor, blockedByMobile))
                    return false;

            if (dockingActor != null)
                return true;

            var landableTerrain = overrideAircraftLanding != null ? overrideAircraftLanding.LandableTerrainTypes : Info.LandableTerrainTypes;
            return landableTerrain.Contains(self.World.Map.GetTerrainInfo(cell).Type);
        }

        bool IsBlockedBy(Actor self, Actor otherActor, Actor ignoreActor, bool blockedByMobile = true)
        {
            if (otherActor == self || otherActor == ignoreActor)
                return false;

            if (!blockedByMobile && self.Owner.RelationshipWith(otherActor.Owner) == PlayerRelationship.Ally &&
                otherActor.TraitOrDefault<Mobile>() != null && otherActor.CurrentActivity == null)
                return false;

            if (self.World.RulesContainTemporaryBlocker)
            {
                var temporaryBlocker = otherActor.TraitOrDefault<ITemporaryBlocker>();
                if (temporaryBlocker != null && temporaryBlocker.CanRemoveBlockage(otherActor, self))
                    return false;
            }

            if (Info.Crushes.IsEmpty)
                return true;

            var passables = otherActor.TraitsImplementing<IPassable>();
            foreach (var passable in passables)
                if (passable.PassableBy(otherActor, self, Info.Crushes))
                    return false;

            return true;
        }

        public bool CanRearmAt(Actor host)
        {
            return rearmable != null && rearmable.Info.RearmActors.Contains(host.Info.Name) && rearmable.RearmableAmmoPools.Any(p => !p.HasFullAmmo);
        }

        public bool CanRepairAt(Actor host)
        {
            return repairable != null && repairable.Info.RepairActors.Contains(host.Info.Name) && self.GetDamageState() != DamageState.Undamaged;
        }

        public void ModifyDeathActorInit(Actor self, TypeDictionary init)
        {
            init.Add(new FacingInit(Facing));
        }

        void INotifyBecomingIdle.OnBecomingIdle(Actor self)
        {
            OnBecomingIdle(self);
        }

        protected virtual void OnBecomingIdle(Actor self)
        {
            if (Info.IdleBehavior == IdleBehaviorType.LeaveMap)
            {
                self.QueueActivity(new FlyOffMap(self));
                self.QueueActivity(new RemoveSelf());
            }
            else if (Info.IdleBehavior == IdleBehaviorType.LeaveMapAtClosestEdge)
            {
                var edgeCell = self.World.Map.ChooseClosestEdgeCell(self.Location);
                self.QueueActivity(new FlyOffMap(self, Target.FromCell(self.World, edgeCell)));
                self.QueueActivity(new RemoveSelf());
            }
            else if (Info.IdleBehavior == IdleBehaviorType.ReturnToBase && GetActorBelow() == null)
                self.QueueActivity(new ReturnToBase(self, null, !Info.TakeOffOnResupply));
            else
            {
                var dat = self.World.Map.DistanceAboveTerrain(CenterPosition);
                if (dat == LandAltitude)
                {
                    if (!CanLand(self.Location) && ReservedActor == null)
                        self.QueueActivity(new TakeOff(self));
                    return;
                }

                if (Info.IdleBehavior != IdleBehaviorType.Land && dat != Info.CruiseAltitude)
                    self.QueueActivity(new TakeOff(self));
                else if (Info.IdleBehavior == IdleBehaviorType.Land && Info.LandableTerrainTypes.Count > 0)
                    self.QueueActivity(new Land(self));
                else
                    self.QueueActivity(new FlyIdle(self));
            }
        }

        #region Implement IPositionable

        public bool CanExistInCell(CPos cell) { return true; }
        public bool IsLeavingCell(CPos location, SubCell subCell = SubCell.Any) { return false; }
        public bool CanEnterCell(CPos cell, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All) { return true; }
        public SubCell GetValidSubCell(SubCell preferred) { return SubCell.Invalid; }
        public SubCell GetAvailableSubCell(CPos a, SubCell preferredSubCell = SubCell.Any, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
        {
            return SubCell.Invalid;
        }

        public void SetCenterPosition(Actor self, WPos pos) { SetPosition(self, pos); }

        public void SetPosition(Actor self, CPos cell, SubCell subCell = SubCell.Any)
        {
            SetPosition(self, self.World.Map.CenterOfCell(cell) + new WVec(0, 0, CenterPosition.Z));
        }

        public void SetPosition(Actor self, WPos pos)
        {
            CenterPosition = pos;

            if (!self.IsInWorld)
                return;

            var altitude = self.World.Map.DistanceAboveTerrain(CenterPosition);

            if (HasInfluence() && altitude.Length <= Info.MinAirborneAltitude)
            {
                var currentPos = new[] { (TopLeft, SubCell.FullCell) };
                if (!landingCells.SequenceEqual(currentPos))
                {
                    self.World.ActorMap.RemoveInfluence(self, this);
                    landingCells = currentPos;
                    self.World.ActorMap.AddInfluence(self, this);
                }
            }

            self.World.UpdateMaps(self, this);

            var isAirborne = altitude.Length >= Info.MinAirborneAltitude;
            if (isAirborne && !airborne)
                OnAirborneAltitudeReached();
            else if (!isAirborne && airborne)
                OnAirborneAltitudeLeft();

            var isCruising = altitude == Info.CruiseAltitude;
            if (isCruising && !cruising)
                OnCruisingAltitudeReached();
            else if (!isCruising && cruising)
                OnCruisingAltitudeLeft();

            if (notify && notifyCenterPositionChanged != null)
                foreach (var n in notifyCenterPositionChanged)
                    n.CenterPositionChanged(self, 0, 0);

            FinishedMoving(self);
        }

        public void FinishedMoving(Actor self)
        {
            if (!self.IsAtGroundLevel())
                return;

            PassAction(self, (notifyCrushed) => notifyCrushed.OnBeingPassed);
        }

        public void EnteringCell(Actor self)
        {
            PassAction(self, (notifyCrushed) => notifyCrushed.WarnPass);
        }

        void PassAction(Actor self, Func<INotifyBeingPassed, Action<Actor, Actor, BitSet<PassClass>>> action)
        {
            var passables = self.World.ActorMap.GetActorsAt(TopLeft).Where(a => a != self)
                .SelectMany(a => a.TraitsImplementing<IPassable>().Select(t => new TraitPair<IPassable>(a, t)));

            foreach (var passable in passables)
                if (passable.Trait.PassableBy(passable.Actor, self, Info.Crushes) && passable.Actor.IsAtGroundLevel())
                    foreach (var notifyCrushed in passable.Actor.TraitsImplementing<INotifyBeingPassed>())
                        action(notifyCrushed)(passable.Actor, self, Info.Crushes);
        }

        public void AddInfluence((CPos, SubCell)[] landingCells)
        {
            if (HasInfluence())
                self.World.ActorMap.RemoveInfluence(self, this);

            this.landingCells = landingCells;
            if (self.IsInWorld)
                self.World.ActorMap.AddInfluence(self, this);
        }

        public void AddInfluence(CPos landingCell)
        {
            AddInfluence(new[] { (landingCell, SubCell.FullCell) });
        }

        public void RemoveInfluence()
        {
            if (self.IsInWorld)
                self.World.ActorMap.RemoveInfluence(self, this);

            landingCells = Array.Empty<(CPos, SubCell)>();
        }

        public bool HasInfluence()
        {
            return landingCells.Length > 0;
        }

        #endregion

        #region Implement IMove

        public Activity MoveTo(CPos cell, int nearEnough = 0, Actor ignoreActor = null,
            bool evaluateNearestMovableCell = false, Color? targetLineColor = null)
        {
            return new Fly(self, Target.FromCell(self.World, cell), WDist.FromCells(nearEnough), targetLineColor: targetLineColor);
        }

        public Activity MoveWithinRange(in Target target, WDist range,
            WPos? initialTargetPosition = null, Color? targetLineColor = null)
        {
            return new Fly(self, target, WDist.Zero, range, initialTargetPosition, targetLineColor);
        }

        public Activity MoveWithinRange(in Target target, WDist minRange, WDist maxRange,
            WPos? initialTargetPosition = null, Color? targetLineColor = null)
        {
            return new Fly(self, target, minRange, maxRange, initialTargetPosition, targetLineColor);
        }

        public Activity MoveFollow(Actor self, in Target target, WDist minRange, WDist maxRange,
            WPos? initialTargetPosition = null, Color? targetLineColor = null)
        {
            return new FlyFollow(self, target, minRange, maxRange, initialTargetPosition, targetLineColor);
        }

        public Activity ReturnToCell(Actor self) { return null; }

        public Activity MoveToTarget(Actor self, in Target target,
            WPos? initialTargetPosition = null, Color? targetLineColor = null)
        {
            return new Fly(self, target, initialTargetPosition, targetLineColor);
        }

        public Activity MoveIntoTarget(Actor self, in Target target)
        {
            return new Land(self, target);
        }

        public Activity LocalMove(Actor self, WPos fromPos, WPos toPos)
        {
            var activities = new CallFunc(() => SetCenterPosition(self, fromPos));
            activities.Queue(new Fly(self, Target.FromPos(toPos)));
            return activities;
        }

        public int EstimatedMoveDuration(Actor self, WPos fromPos, WPos toPos)
        {
            var speed = MovementSpeed;
            return speed > 0 ? (toPos - fromPos).Length / speed : 0;
        }

        public CPos NearestMoveableCell(CPos cell) { return cell; }

        public MovementType CurrentMovementTypes
        {
            get => movementTypes;
            set
            {
                var oldValue = movementTypes;
                movementTypes = value;
                if (value != oldValue)
                    foreach (var n in notifyMoving)
                        n.MovementTypeChanged(self, value);
            }
        }

        public bool CanEnterTargetNow(Actor self, in Target target)
        {
			var targetActor = target;
			if (target.Positions.Any(p => self.World.ActorMap.GetActorsAt(self.World.Map.CellContaining(p)).Any(a => a != self && a != targetActor.Actor)))
                return false;

			MakeReservation(target.Actor);
			return true;
        }

        #endregion

        #region Implement order interfaces

        public IEnumerable<IOrderTargeter> Orders
        {
            get
            {
                yield return new EnterAlliedActorTargeter<BuildingInfo>(
                    "ForceEnter",
                    6,
                    Info.EnterCursor,
                    Info.EnterBlockedCursor,
                    (target, modifiers) => Info.CanForceLand && modifiers.HasModifier(TargetModifiers.ForceMove) && AircraftCanEnter(target),
                    target => Reservable.IsAvailableFor(target, self) && AircraftCanResupplyAt(target, true));

                yield return new EnterAlliedActorTargeter<BuildingInfo>(
                    "Enter",
                    5,
                    Info.EnterCursor,
                    Info.EnterBlockedCursor,
                    AircraftCanEnter,
                    target => Reservable.IsAvailableFor(target, self) && AircraftCanResupplyAt(target, !Info.TakeOffOnResupply));

                yield return new AircraftMoveOrderTargeter(this);
            }
        }

        public Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
        {
            if (!IsTraitDisabled &&
                (order.OrderID == "Enter" || order.OrderID == "Move" || order.OrderID == "Land" || order.OrderID == "ForceEnter"))
                return new Order(order.OrderID, self, target, queued);

            return null;
        }

        Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
        {
            if (IsTraitDisabled || rearmable == null || rearmable.Info.RearmActors.Count == 0)
                return null;

            return new Order("ReturnToBase", self, queued);
        }

        bool IIssueDeployOrder.CanIssueDeployOrder(Actor self, bool queued) { return rearmable != null && rearmable.Info.RearmActors.Count > 0; }

        public string VoicePhraseForOrder(Actor self, Order order)
        {
            if (IsTraitDisabled)
                return null;

            switch (order.OrderString)
            {
                case "Land":
                case "Move":
                    if (!Info.MoveIntoShroud && order.Target.Type != TargetType.Invalid)
                    {
                        var cell = self.World.Map.CellContaining(order.Target.CenterPosition);
                        if (!self.Owner.MapLayers.IsExplored(cell))
                            return null;
                    }

                    return Info.Voice;
                case "Enter":
                case "ForceEnter":
                case "Stop":
                case "Scatter":
                    return Info.Voice;
                case "ReturnToBase":
                    return rearmable != null && rearmable.Info.RearmActors.Count > 0 ? Info.Voice : null;
                default: return null;
            }
        }

        public void ResolveOrder(Actor self, Order order)
        {
            if (IsTraitDisabled)
                return;

            var orderString = order.OrderString;
            if (orderString == "Move")
            {
                var cell = self.World.Map.Clamp(self.World.Map.CellContaining(order.Target.CenterPosition));
                if (!Info.MoveIntoShroud && !self.Owner.MapLayers.IsExplored(cell))
                    return;

                if (!order.Queued)
                    UnReserve();

                var target = Target.FromCell(self.World, cell);
                self.QueueActivity(order.Queued, new Fly(self, target, WDist.FromCells(8), targetLineColor: Info.TargetLineColor));
                self.ShowTargetLines();
            }
            else if (orderString == "Land")
            {
                var cell = self.World.Map.Clamp(self.World.Map.CellContaining(order.Target.CenterPosition));
                if (!Info.MoveIntoShroud && !self.Owner.MapLayers.IsExplored(cell))
                    return;

                if (!order.Queued)
                    UnReserve();

                var target = Target.FromCell(self.World, cell);
                self.QueueActivity(order.Queued, new Land(self, target, targetLineColor: Info.TargetLineColor));
                self.ShowTargetLines();
            }
            else if (orderString == "Enter" || orderString == "ForceEnter" || orderString == "Repair")
            {
                if (order.Target.Type != TargetType.Actor)
                    return;

                var targetActor = order.Target.Actor;
                var isForceEnter = orderString == "ForceEnter";
                var canResupplyAt = AircraftCanResupplyAt(targetActor, isForceEnter || !Info.TakeOffOnResupply);

                if (!canResupplyAt || !Reservable.IsAvailableFor(targetActor, self))
                    return;

                if (!order.Queued)
                    UnReserve();

                var forceLand = isForceEnter || !Info.TakeOffOnResupply;
                self.QueueActivity(order.Queued, new ReturnToBase(self, targetActor, forceLand));
                self.ShowTargetLines();
            }
            else if (orderString == "Stop")
            {
                if (self.CurrentActivity is Resupply ||
                    (self.CurrentActivity is ReturnToBase && GetActorBelow() != null))
                    return;

                self.CancelActivity();
                UnReserve();
            }
            else if (orderString == "ReturnToBase")
            {
                if (rearmable == null || rearmable.Info.RearmActors.Count == 0 || self.CurrentActivity is ReturnToBase || GetActorBelow() != null)
                    return;

                if (!order.Queued)
                    UnReserve();

                self.QueueActivity(order.Queued, new ReturnToBase(self, null, !Info.TakeOffOnResupply));
                self.ShowTargetLines();
            }
            else if (orderString == "Scatter")
                Nudge(self);
        }

        #endregion

        void Nudge(Actor self)
        {
            if (IsTraitDisabled || IsTraitPaused || requireForceMove || !self.World.Map.Contains(self.Location))
                return;

            var offset = new WVec(0, -self.World.SharedRandom.Next(512, 2048), 0)
                .Rotate(WRot.FromFacing(self.World.SharedRandom.Next(256)));
            var target = Target.FromPos(self.CenterPosition + offset);

            self.QueueActivity(false, new Fly(self, target));
            self.ShowTargetLines();
            UnReserve();
        }

        #region Airborne conditions

        void OnAirborneAltitudeReached()
        {
            if (airborne)
                return;

            airborne = true;
            if (airborneToken == Actor.InvalidConditionToken)
                airborneToken = self.GrantCondition(Info.AirborneCondition);
        }

        void OnAirborneAltitudeLeft()
        {
            if (!airborne)
                return;

            airborne = false;
            if (airborneToken != Actor.InvalidConditionToken)
                airborneToken = self.RevokeCondition(airborneToken);
        }

        #endregion

        #region Cruising conditions

        void OnCruisingAltitudeReached()
        {
            if (cruising)
                return;

            cruising = true;
            if (cruisingToken == Actor.InvalidConditionToken)
                cruisingToken = self.GrantCondition(Info.CruisingCondition);
        }

        void OnCruisingAltitudeLeft()
        {
            if (!cruising)
                return;

            cruising = false;
            if (cruisingToken != Actor.InvalidConditionToken)
                cruisingToken = self.RevokeCondition(cruisingToken);
        }

        #endregion

        void INotifyActorDisposing.Disposing(Actor self)
        {
            UnReserve();
        }

        void IActorPreviewInitModifier.ModifyActorPreviewInit(Actor self, TypeDictionary inits)
        {
            if (!inits.Contains<DynamicFacingInit>() && !inits.Contains<FacingInit>())
                inits.Add(new DynamicFacingInit(() => Facing));
        }

        Activity ICreationActivity.GetCreationActivity()
        {
            return new AssociateWithAirfieldActivity(self, creationActivityDelay);
        }

        public class AssociateWithAirfieldActivity : Activity
        {
            readonly Aircraft aircraft;
            readonly int delay;

            public AssociateWithAirfieldActivity(Actor self, int delay = 0)
            {
                aircraft = self.Trait<Aircraft>();
                IsInterruptible = false;
                this.delay = delay;
            }

            protected override void OnFirstRun(Actor self)
            {
                var host = aircraft.GetActorBelow();
                if (host != null)
                    aircraft.MakeReservation(host);

                if (delay > 0)
                    QueueChild(new Wait(delay));
            }

            public override bool Tick(Actor self)
            {
                if (!aircraft.Info.TakeOffOnCreation)
                {
                    aircraft.AllowYieldingReservation();
                    return true;
                }

                if (self.World.Map.DistanceAboveTerrain(aircraft.CenterPosition).Length <= aircraft.LandAltitude.Length)
                    QueueChild(new TakeOff(self));

                aircraft.UnReserve();
                return true;
            }
        }

        public class AircraftMoveOrderTargeter : IOrderTargeter
        {
            readonly Aircraft aircraft;

            public string OrderID { get; protected set; }
            public int OrderPriority => 4;
            public bool IsQueued { get; protected set; }

            public AircraftMoveOrderTargeter(Aircraft aircraft)
            {
                this.aircraft = aircraft;
                OrderID = "Move";
            }

            public bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers)
            {
                if (target.Type == TargetType.Actor && (target.Actor.Owner != self.Owner || self.World.Selection.Contains(target.Actor)))
                    return true;

                return modifiers.HasModifier(TargetModifiers.ForceMove);
            }

            public virtual bool CanTarget(Actor self, in Target target, ref TargetModifiers modifiers, ref string cursor)
            {
                if (target.Type != TargetType.Terrain || (aircraft.requireForceMove && !modifiers.HasModifier(TargetModifiers.ForceMove)))
                    return false;

                var location = self.World.Map.CellContaining(target.CenterPosition);

                if (modifiers.HasModifier(TargetModifiers.ForceMove) && aircraft.Info.CanForceLand)
                {
                    var buildingAtLocation = self.World.ActorMap.GetActorsAt(location)
                        .Any(a => a.TraitOrDefault<Building>() != null && a.TraitOrDefault<Selectable>() != null);

                    if (!buildingAtLocation || aircraft.CanLand(location, blockedByMobile: false))
                        OrderID = "Land";
                }

                IsQueued = modifiers.HasModifier(TargetModifiers.ForceQueue);

                var explored = self.Owner.MapLayers.IsExplored(location);
                cursor = !aircraft.IsTraitPaused && (explored || aircraft.Info.MoveIntoShroud) && self.World.Map.Contains(location) ?
                    aircraft.Info.Cursor : aircraft.Info.BlockedCursor;

                return true;
            }
        }
    }
}
