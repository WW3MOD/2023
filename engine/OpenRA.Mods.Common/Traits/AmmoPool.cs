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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Actor has a limited amount of ammo, after using it all the actor must reload in some way.")]
	public class AmmoPoolInfo : TraitInfo
	{
		[Desc("Name of this ammo pool, used to link reload traits to this pool.")]
		public readonly string Name = "primary";

		[Desc("Name(s) of armament(s) that use this pool.")]
		public readonly string[] Armaments = { "primary", "secondary" };

		[Desc("Time in ticks to fully reload ammopool from empty.")]
		public readonly int FullReloadTicks = 0;

		[Desc("How many reloads should take place before unit is fully reloaded (based on reloading from empty).")]
		public readonly int FullReloadSteps = 0;

		[Desc("How much ammo does this pool contain when fully loaded.")]
		public readonly int Ammo = 1;

		[Desc("Initial ammo the actor is created with. Defaults to Ammo.")]
		public readonly int InitialAmmo = -1;

		[Desc("How much ammo is reloaded after a certain period.")]
		public readonly int ReloadCount = 1;

		[Desc("Time to reload per ReloadCount on airfield etc.")]
		public readonly int ReloadDelay = 50;

		[Desc("Sound to play for each reloaded ammo magazine.")]
		public readonly string RearmSound = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self for each ammo point in this pool.")]
		public readonly string AmmoCondition = null;

		public override object Create(ActorInitializer init) { return new AmmoPool(this); }
	}

	public class AmmoPool : INotifyCreated, INotifyAttack, INotifyBecomingIdle, ISync
	{
		public readonly AmmoPoolInfo Info;
		readonly Stack<int> tokens = new();
		IReloadAmmoModifier[] modifiers;

		[Sync]
		public int RemainingTicks;

		[Sync]
		public int CurrentAmmoCount { get; private set; }

		public bool HasAmmo => CurrentAmmoCount > 0;
		public bool HasHalfAmmo { get { return CurrentAmmoCount > Info.Ammo / 2; } }
		public bool HasFullAmmo => CurrentAmmoCount == Info.Ammo;

		public AmmoPool(AmmoPoolInfo info)
		{
			Info = info;
			CurrentAmmoCount = Info.InitialAmmo < Info.Ammo && Info.InitialAmmo >= 0 ? Info.InitialAmmo : Info.Ammo;
		}

		public bool GiveAmmo(Actor self, int count)
		{
			if (CurrentAmmoCount >= Info.Ammo || count < 0)
				return false;

			CurrentAmmoCount = (CurrentAmmoCount + count).Clamp(0, Info.Ammo);
			UpdateCondition(self);
			return true;
		}

		public bool TakeAmmo(Actor self, int count)
		{
			if (CurrentAmmoCount <= 0 || count < 0)
				return false;

			CurrentAmmoCount = (CurrentAmmoCount - count).Clamp(0, Info.Ammo);
			UpdateCondition(self);
			return true;
		}

		void INotifyCreated.Created(Actor self)
		{
			UpdateCondition(self);
			modifiers = self.TraitsImplementing<IReloadAmmoModifier>().ToArray();

			self.World.AddFrameEndTask(w =>
			{
				/* RemainingTicks = Util.ApplyPercentageModifiers(Info.ReloadDelay, modifiers.Select(m => m.GetReloadAmmoModifier())); */

				if (Info.FullReloadTicks > 0)
				{
					var reloadCount = Info.ReloadCount;
					if (Info.FullReloadSteps > 0)
					{
						double a = Info.Ammo / Info.FullReloadSteps;
						reloadCount = (int)System.Math.Ceiling(a);
					}

					RemainingTicks = Util.ApplyPercentageModifiers(Info.FullReloadTicks * reloadCount / Info.Ammo, modifiers.Select(m => m.GetReloadAmmoModifier()));
				}
				else
					RemainingTicks = Util.ApplyPercentageModifiers(Info.ReloadDelay, modifiers.Select(m => m.GetReloadAmmoModifier()));
			});
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			if (a != null && Info.Armaments.Contains(a.Info.Name))
			{
				TakeAmmo(self, a.Info.AmmoUsage);

				if (!HasAmmo && self.TraitOrDefault<IMove>() != null && !self.Info.HasTraitInfo<AircraftInfo>())
					AutoRearm(self);
			}
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			var ammoPools = self.TraitsImplementing<AmmoPool>();
			if (ammoPools.Any() && ammoPools.All(a => !a.HasAmmo) && !self.Info.HasTraitInfo<AircraftInfo>())
				AutoRearm(self);
		}

		public static void AutoRearm(Actor self)
		{
			var nearestResupplier = ChooseResupplier(self);

			if (nearestResupplier != null)
				self.QueueActivity(false, new Resupply(self, nearestResupplier, nearestResupplier.Trait<RearmsUnits>().Info.CloseEnough));
			else
			{
				var bases = self.World.ActorsHavingTrait<BaseBuilding>()
					.Where(a => a.Owner == self.Owner)
					.ToList();

				if (bases.Count > 0)
					self.QueueActivity(false, new Resupply(self, bases.First(), new WDist(0)));
			}
		}

		public static Actor ChooseResupplier(Actor self)
		{
			var rearmInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();

			if (rearmInfo == null)
				return null;

			var rearmActors = self.World.ActorsHavingTrait<RearmsUnits>()
				.Where(rearmActor => !rearmActor.IsDead
					&& rearmActor.Owner == self.Owner
					&& rearmInfo.RearmActors.Contains(rearmActor.Info.Name));

			return rearmActors.ClosestTo(self);
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		void UpdateCondition(Actor self)
		{
			if (string.IsNullOrEmpty(Info.AmmoCondition))
				return;

			while (CurrentAmmoCount > tokens.Count && tokens.Count < Info.Ammo)
				tokens.Push(self.GrantCondition(Info.AmmoCondition));

			while (CurrentAmmoCount < tokens.Count && tokens.Count > 0)
				self.RevokeCondition(tokens.Pop());
		}

		public void Reload(Actor self, int reloadDelay = 0, int reloadCount = 0)
		{
			if (reloadDelay == 0) reloadDelay = Info.ReloadDelay;
			if (reloadCount == 0) reloadCount = Info.ReloadCount;

			if (!HasFullAmmo && --RemainingTicks == 0)
			{
				if (Info.FullReloadSteps > 0)
				{
					double a = Info.Ammo / Info.FullReloadSteps;
					reloadCount = (int)System.Math.Ceiling(a);
				}

				if (Info.FullReloadTicks > 0)
					RemainingTicks = Util.ApplyPercentageModifiers(Info.FullReloadTicks * reloadCount / Info.Ammo, modifiers.Select(m => m.GetReloadAmmoModifier()));
				else
					RemainingTicks = Util.ApplyPercentageModifiers(reloadDelay, modifiers.Select(m => m.GetReloadAmmoModifier()));

				GiveAmmo(self, reloadCount);

				if (!string.IsNullOrEmpty(Info.RearmSound))
					Game.Sound.PlayToPlayer(SoundType.World, self.Owner, Info.RearmSound, self.CenterPosition);
			}
		}
	}
}
