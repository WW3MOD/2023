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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Spawns remains of a husk actor with the correct facing.")]
	public class HuskInfo : TraitInfo, IPositionableInfo, IFacingInfo, IActorPreviewInitInfo, IRulesetLoaded
	{
		public readonly HashSet<string> AllowedTerrain = new();

		[Desc("Facing to use for actor previews (map editor, color picker, etc)")]
		public readonly WAngle PreviewFacing = new(384);

		[LocomotorReference]
		[Desc("Used to define crushes. Locomotor must be defined on the World actor.")]
		public readonly string Locomotor = null;

		IEnumerable<ActorInit> IActorPreviewInitInfo.ActorPreviewInits(ActorInfo ai, ActorPreviewType type)
		{
			yield return new FacingInit(PreviewFacing);
		}

		public override object Create(ActorInitializer init) { return new Husk(init, this); }

		public WAngle GetInitialFacing() { return new WAngle(512); }

		public IReadOnlyDictionary<CPos, SubCell> OccupiedCells(ActorInfo info, CPos location, SubCell subCell = SubCell.Any)
		{
			return new Dictionary<CPos, SubCell>() { { location, SubCell.FullCell } };
		}

		bool IOccupySpaceInfo.SharesCell => false;

		public bool CanEnterCell(World world, Actor self, CPos cell,
			SubCell subCell = SubCell.FullCell, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			// IPositionable*Info*.CanEnterCell is only ever used for things like exiting production facilities,
			// all places relevant for husks check IPositionable.CanEnterCell instead, so we can safely set this to true.
			return true;
		}

		public LocomotorInfo LocomotorInfo { get; private set; }
		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (string.IsNullOrEmpty(Locomotor))
				return;

			LocomotorInfo = rules.Actors[SystemActors.World].TraitInfos<LocomotorInfo>().FirstOrDefault(li => li.Name == Locomotor);
		}
	}

	public class Husk : IPositionable, IFacing, ISync, INotifyCreated, INotifyAddedToWorld, INotifyRemovedFromWorld,
		IDeathActorInitModifier, IEffectiveOwner
	{
		readonly Actor self;
		readonly HuskInfo info;
		readonly Player effectiveOwner;

		readonly int dragSpeed;
		readonly WPos finalPosition;

		INotifyCenterPositionChanged[] notifyCenterPositionChanged;

		[Sync]
		public CPos TopLeft { get; private set; }

		[Sync]
		public WPos CenterPosition { get; private set; }

		[Sync]
		public WAngle Facing
		{
			get => Orientation.Yaw;
			set => Orientation = Orientation.WithYaw(value);
		}

		public WRot Orientation { get; private set; }

		public WAngle TurnSpeed => WAngle.Zero;

		public Husk(ActorInitializer init, HuskInfo info)
		{
			this.info = info;
			self = init.Self;

			TopLeft = init.GetValue<LocationInit, CPos>();
			CenterPosition = init.GetValue<CenterPositionInit, WPos>(init.World.Map.CenterOfCell(TopLeft));
			Facing = init.GetValue<FacingInit, WAngle>(info.GetInitialFacing());

			dragSpeed = init.GetValue<HuskSpeedInit, int>(0);
			finalPosition = init.World.Map.CenterOfCell(TopLeft);

			// Point the wreck down the line it is about to be dragged along. Without this it keeps the facing it
			// died with, which on a corner is the tangent of the arc it was part-way through rather than the
			// straight line to the reserved cell -- so the wreck slides sideways. The husk sprite replaces the
			// living one on this same tick, so aligning here is hidden by a discontinuity that already happens.
			var travel = finalPosition - CenterPosition;
			var deathFacing = Facing;
			var settled = HuskSettleGeometry.SettleFacing(travel, deathFacing);

			if (dragSpeed > 0)
				Facing = settled;

			// `settled` is computed above the assignment so that reverting ONLY the assignment leaves a control
			// arm that still reports the angle it would have used -- the crab is a measured number in both arms.
			//
			// `applied` is what makes the two arms DISTINGUISHABLE, and without it a green run proves nothing:
			// every other field here is read before the assignment and so is byte-identical whether the fix is
			// live or reverted. applied == settled means the fix ran; applied == deathFacing means it did not.
			if (TestMode.IsActive)
				Log.Write("debug", $"[husk-settle] travel=({travel.X},{travel.Y}) dragSpeed={dragSpeed} " +
					$"deathFacing={deathFacing.Angle} settled={settled.Angle} " +
					$"crab={HuskSettleGeometry.CrabAngle(travel, deathFacing)} applied={Facing.Angle}");

			effectiveOwner = init.GetValue<EffectiveOwnerInit, Player>(info, self.Owner);
		}

		void INotifyCreated.Created(Actor self)
		{
			self.QueueActivity(new DragAndCrush(self, info.LocomotorInfo, dragSpeed, finalPosition));
			notifyCenterPositionChanged = self.TraitsImplementing<INotifyCenterPositionChanged>().ToArray();
		}

		public bool CanExistInCell(CPos cell)
		{
			if (!self.World.Map.Contains(cell))
				return false;

			if (!info.AllowedTerrain.Contains(self.World.Map.GetTerrainInfo(cell).Type))
				return false;

			return true;
		}

		public (CPos, SubCell)[] OccupiedCells() { return new[] { (TopLeft, SubCell.FullCell) }; }
		public bool IsLeavingCell(CPos location, SubCell subCell = SubCell.Any) { return false; }
		public SubCell GetValidSubCell(SubCell preferred = SubCell.Any) { return SubCell.FullCell; }
		public SubCell GetAvailableSubCell(CPos cell, SubCell preferredSubCell = SubCell.Any, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			if (!CanExistInCell(cell))
				return SubCell.Invalid;

			if (check == BlockedByActor.None)
				return SubCell.FullCell;

			return self.World.BlockingActorsAt(cell)
				.All(x => x == ignoreActor) ? SubCell.FullCell : SubCell.Invalid;
		}

		public bool CanEnterCell(CPos a, Actor ignoreActor = null, BlockedByActor check = BlockedByActor.All)
		{
			return GetAvailableSubCell(a, SubCell.Any, ignoreActor, check) != SubCell.Invalid;
		}

		public void SetPosition(Actor self, CPos cell, SubCell subCell = SubCell.Any) { SetPosition(self, self.World.Map.CenterOfCell(cell)); }

		public void SetCenterPosition(Actor self, WPos pos)
		{
			CenterPosition = pos;
			self.World.ScreenMap.AddOrUpdate(self);

			// This can be called from the constructor before notifyCenterPositionChanged is assigned.
			if (notifyCenterPositionChanged != null)
				foreach (var n in notifyCenterPositionChanged)
					n.CenterPositionChanged(self, 0, 0);
		}

		public void SetPosition(Actor self, WPos pos)
		{
			self.World.ActorMap.RemoveInfluence(self, this);
			CenterPosition = pos;
			TopLeft = self.World.Map.CellContaining(pos);
			self.World.ActorMap.AddInfluence(self, this);

			self.World.UpdateMaps(self, this);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			self.World.AddToMaps(self, this);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			self.World.RemoveFromMaps(self, this);
		}

		void IDeathActorInitModifier.ModifyDeathActorInit(Actor self, TypeDictionary init)
		{
			init.Add(new FacingInit(Facing));
		}

		// We return self.Owner if there's no effective owner
		bool IEffectiveOwner.Disguised => true;
		Player IEffectiveOwner.Owner => effectiveOwner;
	}

	public class HuskSpeedInit : ValueActorInit<int>, ISingleInstanceInit
	{
		public HuskSpeedInit(int value)
			: base(value) { }
	}

	public static class HuskSettleGeometry
	{
		// Which way a wreck should point while it finishes the move it died on. The husk's drag is a straight line,
		// so one facing covers the whole slide. Picking the nearer of travel/travel+180 keeps a unit that died
		// REVERSING pointing backwards instead of spinning it around (^WheeledVehicle sets CanMoveBackward), and
		// makes this a no-op on a straight leg, where Move has already aligned facing with travel.
		public static WAngle SettleFacing(WVec travel, WAngle deathFacing)
		{
			if (travel.HorizontalLengthSquared == 0)
				return deathFacing;

			var forward = travel.Yaw;
			var reverse = new WAngle(forward.Angle + 512);

			return AngleBetween(deathFacing, forward) <= AngleBetween(deathFacing, reverse) ? forward : reverse;
		}

		// How far off its direction of travel the wreck is pointing -- the sideways slide, as a number.
		public static int CrabAngle(WVec travel, WAngle deathFacing)
		{
			if (travel.HorizontalLengthSquared == 0)
				return 0;

			return AngleBetween(deathFacing, SettleFacing(travel, deathFacing));
		}

		static int AngleBetween(WAngle a, WAngle b)
		{
			var raw = Math.Abs(a.Angle - b.Angle);
			return raw > 512 ? 1024 - raw : raw;
		}
	}

	public class DragAndCrush : Activity
	{
		readonly LocomotorInfo info;

		public DragAndCrush(Actor self, LocomotorInfo info, int dragSpeed, WPos finalPosition)
		{
			this.info = info;

			var distance = (finalPosition - self.CenterPosition).Length;
			if (dragSpeed > 0 && distance > 0)
				self.QueueActivity(new Drag(self, self.CenterPosition, finalPosition, distance / dragSpeed));
		}

		protected override void OnFirstRun(Actor self)
		{
			if (self.IsAtGroundLevel())
				PassAction(self, self.CenterPosition, (notifyPassed) => notifyPassed.OnBeingPassed);
		}

		void PassAction(Actor self, WPos position, Func<INotifyBeingPassed, Action<Actor, Actor, BitSet<PassClass>>> action)
		{
			if (info == null || info.Crushes.IsEmpty)
				return;

			var passables = self.World.ActorMap.GetActorsAt(self.World.Map.CellContaining(position)).Where(a => a != self)
				.SelectMany(a => a.TraitsImplementing<IPassable>().Select(t => new TraitPair<IPassable>(a, t)));

			// Only pass actors that are on the ground level.
			foreach (var passable in passables)
				if (passable.Trait.PassableBy(passable.Actor, self, info.Crushes) && passable.Actor.IsAtGroundLevel())
					foreach (var notifyPassed in passable.Actor.TraitsImplementing<INotifyBeingPassed>())
						action(notifyPassed)(passable.Actor, self, info.Crushes);
		}
	}
}
