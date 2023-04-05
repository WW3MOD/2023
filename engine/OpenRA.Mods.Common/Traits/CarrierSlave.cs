﻿#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Can be slaved to a spawner.")]
	public class CarrierSlaveInfo : BaseSpawnerSlaveInfo
	{
		[Desc("Move this close to the spawner, before entering it.")]
		public readonly WDist LandingDistance = new WDist(5 * 1024);

		[Desc("Move this close to the spawner, before entering it.")]
		public readonly int MaxDistance = 0;

		[Desc("Move this close to the spawner, before entering it.")]
		public readonly int MaxDistanceCheckTicks = 0;

		[Desc("How long can the slave be out before returning to master.")]
		public readonly int ReturnAfter = 0;

		public readonly Color BarColor = Color.White;

		[Desc("When the slave becomes idle, it returns to the carrier.")]
		public readonly bool SlaveReturnOnIdle = true;

		public override object Create(ActorInitializer init) { return new CarrierSlave(init, this); }
	}

	public class CarrierSlave : BaseSpawnerSlave, ITick, INotifyIdle, ISelectionBar
	{
		readonly Actor self;
		public readonly CarrierSlaveInfo Info;
		public int ReturnTimeRemaining;
		int rejectOrdersToken = Actor.InvalidConditionToken;
		int maxDistanceCheckTicks;

		CarrierMaster spawnerMaster;

		public CarrierSlave(ActorInitializer init, CarrierSlaveInfo info)
			: base(init, info)
		{
			Info = info;
			self = init.Self;
			ReturnTimeRemaining = Info.ReturnAfter;
			/* ammoPools = init.Self.TraitsImplementing<AmmoPool>().ToArray(); */
		}

		void ITick.Tick(Actor self)
		{
			ReturnAfterTime(self);
			ReturnWithinDistance(self);
		}

		public void EnterSpawner(Actor self, bool rejectOrders = false)
		{
			// Hopefully, self will be disposed shortly afterwards by SpawnerSlaveDisposal policy.
			if (Master == null || Master.IsDead)
				return;

			// Proceed with enter, if already at it.
			if (self.CurrentActivity is EnterCarrierMaster)
				return;

			if (rejectOrders)
				rejectOrdersToken = self.GrantCondition("reject-orders");

			// Cancel whatever else self was doing and return.
			self.QueueActivity(false, new EnterCarrierMaster(self, Master, spawnerMaster));
		}

		public override void LinkMaster(Actor self, Actor master, BaseSpawnerMaster spawnerMaster)
		{
			base.LinkMaster(self, master, spawnerMaster);
			this.spawnerMaster = spawnerMaster as CarrierMaster;
		}

		/* bool NeedToReload(Actor _)
		{
			// The unit may not have ammo but will have unlimited ammunitions.
			if (ammoPools.Length == 0)
				return false;

			return ammoPools.All(x => !x.HasAmmo);
		} */

		void ReturnAfterTime(Actor self)
		{
			if (Info.ReturnAfter == 0 || !self.IsInWorld || --ReturnTimeRemaining > 0)
				return;

			EnterSpawner(self, true);
		}

		public void RevokeRejectOrdersToken(Actor self)
		{
			self.RevokeCondition(rejectOrdersToken);
		}

		void ReturnWithinDistance(Actor self)
		{
			if (Info.MaxDistance == 0 || --maxDistanceCheckTicks > 0)
				return;

			maxDistanceCheckTicks = Info.MaxDistanceCheckTicks;

			var diffVector = self.Location - Master.Location;

			if (new WDist((diffVector.Length + 1) * 1024) < new WDist(Info.MaxDistance * 1024))
				return;

			var cell = Master.Location + diffVector;

			var mv = self.Trait<IMove>();
			self.QueueActivity(false, mv.MoveTo(cell, 0));
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			if (Info.SlaveReturnOnIdle)
				EnterSpawner(self);
		}

		public override void Stop(Actor self)
		{
			base.Stop(self);
			EnterSpawner(self);
		}

		float ISelectionBar.GetValue()
		{
			// Only people we like should see our production status.
			if (!self.Owner.IsAlliedWith(self.World.RenderPlayer))
				return 0;

			if (ReturnTimeRemaining < 0)
				return 0;

			return (float)((float)ReturnTimeRemaining / (float)Info.ReturnAfter);
		}

		bool ISelectionBar.DisplayWhenEmpty => false;

		Color ISelectionBar.GetColor() { return Info.BarColor; }
	}
}
