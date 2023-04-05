﻿#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor can spawn actors.")]
	public class CarrierMasterInfo : BaseSpawnerMasterInfo
	{
		[Desc("Spawn is a missile that dies and not return.")]
		public readonly bool SpawnIsMissile = false;

		[Desc("Ammo pool to drain from if slave is destroyed.")]
		public readonly string AmmoPool = "primary";

		[Desc("Spawn rearm delay, in ticks")]
		public readonly int RearmTicks = 150;

		[GrantedConditionReference]
		[Desc("The condition to grant to self right after launching a spawned unit. (Used by V3 to make immobile.)")]
		public readonly string LaunchingCondition = null;

		[Desc("After this many ticks, we remove the condition.")]
		public readonly int LaunchingTicks = 15;

		[Desc("Max distance slaves can travel from master before being recalled.")]
		public readonly WDist MaxSlaveDistance = WDist.FromCells(18);

		[Desc("Ticks between performing range checks for slave maximum distance.")]
		public readonly int MaxSlaveDistanceCheckInterval = 50;

		[Desc("Instantly repair spawners when they return?")]
		public readonly bool InstantRepair = true;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while spawned units are loaded.",
			"Condition can stack with multiple spawns.")]
		public readonly string LoadedCondition = null;

		[Desc("Conditions to grant when specified actors are contained inside the transport.",
			"A dictionary of [actor id]: [condition].")]
		public readonly Dictionary<string, string> SpawnContainConditions = new Dictionary<string, string>();

		[GrantedConditionReference]
		public IEnumerable<string> LinterSpawnContainConditions { get { return SpawnContainConditions.Values; } }

		public override object Create(ActorInitializer init) { return new CarrierMaster(init, this); }
	}

	public class CarrierMaster : BaseSpawnerMaster, ITick, IResolveOrder, INotifyAttack, INotifyMoving
	{
		class CarrierSlaveEntry : BaseSpawnerSlaveEntry
		{
			public int RearmTicks = 0;
			public new CarrierSlave SpawnerSlave;
		}

		readonly Dictionary<string, Stack<int>> spawnContainTokens = new Dictionary<string, Stack<int>>();
		readonly Stack<int> loadedTokens = new Stack<int>();
		public readonly CarrierMasterInfo CarrierMasterInfo;

		AmmoPool ammoPool;

		int respawnTicks = 0;

		int launchCondition = Actor.InvalidConditionToken;
		int launchConditionTicks;

		Target currentTarget;
		int maxDistanceCheckTicks;

		public CarrierMaster(ActorInitializer init, CarrierMasterInfo info)
			: base(init, info)
		{
			CarrierMasterInfo = info;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
			ammoPool = self.TraitsImplementing<AmmoPool>().Single(ap => ap.Info.Name == CarrierMasterInfo.AmmoPool);

			// Spawn initial load.
			var burst = Info.InitialActorCount == -1 ? Info.Actors.Length : Info.InitialActorCount;
			for (var i = 0; i < burst; i++)
				Replenish(self, SlaveEntries);
		}

		public override BaseSpawnerSlaveEntry[] CreateSlaveEntries(BaseSpawnerMasterInfo info)
		{
			var slaveEntries = new CarrierSlaveEntry[info.Actors.Length]; // For this class to use

			for (var i = 0; i < slaveEntries.Length; i++)
				slaveEntries[i] = new CarrierSlaveEntry();

			return slaveEntries; // For the base class to use
		}

		public override void InitializeSlaveEntry(Actor slave, BaseSpawnerSlaveEntry entry)
		{
			base.InitializeSlaveEntry(slave, entry);

			var carrierSlaveEntry = entry as CarrierSlaveEntry;
			carrierSlaveEntry.RearmTicks = 0;
			carrierSlaveEntry.IsLaunched = false;
			carrierSlaveEntry.SpawnerSlave = slave.Trait<CarrierSlave>();
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Stop")
				Recall();
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		// The rate of fire of the dummy weapon determines the launch cycle as each shot
		// invokes Attacking()
		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			if (target.Type != TargetType.Terrain)
				return;

			currentTarget = target;

			if (IsTraitDisabled || IsTraitPaused || !Info.ArmamentNames.Contains(a.Info.Name))
				return;

			// Issue retarget order for already launched ones
			foreach (var slave in SlaveEntries)
				if (slave.IsLaunched && slave.IsValid)
					slave.SpawnerSlave.Attack(slave.Actor, target);

			var carrierSlaveEntry = GetLaunchable();
			if (carrierSlaveEntry == null)
				return;

			carrierSlaveEntry.IsLaunched = true; // mark as launched

			if (CarrierMasterInfo.LaunchingCondition != null)
			{
				if (launchCondition == Actor.InvalidConditionToken)
					launchCondition = self.GrantCondition(CarrierMasterInfo.LaunchingCondition);

				launchConditionTicks = CarrierMasterInfo.LaunchingTicks;
			}

			SpawnIntoWorld(self, carrierSlaveEntry.Actor, self.CenterPosition);

			if (spawnContainTokens.TryGetValue(a.Info.Name, out var spawnContainToken) && spawnContainToken.Count > 0)
				self.RevokeCondition(spawnContainToken.Pop());

			if (loadedTokens.Count > 0 && CarrierMasterInfo.LoadedCondition != null)
				self.RevokeCondition(loadedTokens.Pop());

			// Lambdas can't use 'in' variables, so capture a copy for later
			var delayedTarget = target;

			// Queue attack order, too.
			self.World.AddFrameEndTask(w =>
			{
				var slave = carrierSlaveEntry.SpawnerSlave;

				if (slave.ForceReturnToken != Actor.InvalidConditionToken)
					slave.RevokeRejectOrdersToken();

				slave.ReturnTimeRemaining = slave.Info.ReturnAfter;

				// The actor might had been trying to do something before entering the carrier.
				// Cancel whatever it was trying to do.
				slave.Stop(carrierSlaveEntry.Actor);

				if (carrierSlaveEntry.Actor.TraitOrDefault<Armament>() != null)
					slave.Attack(carrierSlaveEntry.Actor, delayedTarget);
				else
				{
					if (delayedTarget.Type != TargetType.Invalid)
					{
						var mv = carrierSlaveEntry.Actor.Trait<IMove>();
						var cell = self.World.Map.CellContaining(delayedTarget.CenterPosition);

						carrierSlaveEntry.Actor.QueueActivity(false, mv.MoveTo(cell, 0));
					}
				}
			});
		}

		void Recall()
		{
			// TODO: Bug? Runs periodically before first launch then stops
			// Game.Debug("Recall");

			// Tell launched slaves to come back and enter me.
			foreach (var slaveEntry in SlaveEntries)
				if (slaveEntry.IsLaunched && slaveEntry.IsValid)
				{
					var carrierSlaveEntry = slaveEntry as CarrierSlaveEntry;
					carrierSlaveEntry.SpawnerSlave.EnterSpawner(slaveEntry.Actor);
				}
		}

		public override void OnSlaveKilled(Actor self, Actor slave)
		{
			// Set clock so that regen happens.
			if (respawnTicks <= 0) // Don't interrupt an already running timer!
				respawnTicks = Info.RespawnTicks;

			ammoPool.TakeAmmo(self, 1);
		}

		CarrierSlaveEntry GetLaunchable()
		{
			foreach (var slaveEntry in SlaveEntries)
			{
				var carrierSlaveEntry = slaveEntry as CarrierSlaveEntry;
				if (carrierSlaveEntry.RearmTicks <= 0 && !slaveEntry.IsLaunched && slaveEntry.IsValid)
					return carrierSlaveEntry;
			}

			return null;
		}

		public void PickupSlave(Actor self, Actor a)
		{
			CarrierSlaveEntry slaveEntry = null;
			foreach (var carrierSlaveEntry in SlaveEntries)
				if (carrierSlaveEntry.Actor == a)
				{
					slaveEntry = carrierSlaveEntry as CarrierSlaveEntry;
					break;
				}

			if (slaveEntry == null)
				throw new InvalidOperationException("An actor that isn't my slave entered me?");

			slaveEntry.IsLaunched = false;

			// setup rearm
			slaveEntry.RearmTicks = Util.ApplyPercentageModifiers(CarrierMasterInfo.RearmTicks, reloadModifiers.Select(rm => rm.GetReloadModifier()));

			if (CarrierMasterInfo.SpawnContainConditions.TryGetValue(a.Info.Name, out var spawnContainCondition))
				spawnContainTokens.GetOrAdd(a.Info.Name).Push(self.GrantCondition(spawnContainCondition));

			if (CarrierMasterInfo.LoadedCondition != null)
				loadedTokens.Push(self.GrantCondition(CarrierMasterInfo.LoadedCondition));
		}

		public override void Replenish(Actor self, BaseSpawnerSlaveEntry entry)
		{
			base.Replenish(self, entry);

			if (CarrierMasterInfo.SpawnContainConditions.TryGetValue(entry.Actor.Info.Name, out var spawnContainCondition))
				spawnContainTokens.GetOrAdd(entry.Actor.Info.Name).Push(self.GrantCondition(spawnContainCondition));

			if (CarrierMasterInfo.LoadedCondition != null)
				loadedTokens.Push(self.GrantCondition(CarrierMasterInfo.LoadedCondition));
		}

		void ITick.Tick(Actor self)
		{
			if (launchCondition != Actor.InvalidConditionToken && --launchConditionTicks < 0)
				launchCondition = self.RevokeCondition(launchCondition);

			if (respawnTicks > 0)
			{
				respawnTicks--;

				// Time to respawn someting.
				if (respawnTicks <= 0)
				{
					Replenish(self, SlaveEntries);

					// If there's something left to spawn, restart the timer.
					if (SelectEntryToSpawn(SlaveEntries) != null)
						respawnTicks = Util.ApplyPercentageModifiers(Info.RespawnTicks, reloadModifiers.Select(rm => rm.GetReloadModifier()));
				}
			}

			// Rearm
			foreach (var slaveEntry in SlaveEntries)
			{
				var carrierSlaveEntry = slaveEntry as CarrierSlaveEntry;
				if (carrierSlaveEntry.RearmTicks > 0)
					carrierSlaveEntry.RearmTicks--;
			}

			// range check
			// RangeCheck(self);
		}

		/* protected void RangeCheck(Actor self)
		{
			if (--maxDistanceCheckTicks > 0)
				return;

			maxDistanceCheckTicks = CarrierMasterInfo.MaxSlaveDistanceCheckInterval;
			var pos = self.CenterPosition;
			var inRange = currentTarget.IsInRange(pos, CarrierMasterInfo.MaxSlaveDistance);

			if (!inRange)
				Recall();
		} */

		public void MovementTypeChanged(Actor self, MovementType type)
		{
			if (type != MovementType.None)
				Recall();
		}

		protected override void TraitPaused(Actor self)
		{
			Recall();
		}
	}
}
