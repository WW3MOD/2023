#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
	public class AmmoPoolInfo : TraitInfo, IProvideTooltipDescription
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

		[Desc("Should actor automatically move to rearm when out of ammo.")]
		public readonly bool AutoRearm = true;

		[ConsumedConditionReference]
		[Desc("Should actor automatically move to rearm when out of ammo.")]
		public readonly string AutoRearmCondition = null;

		[Desc("Supply cost per ammo unit when rearmed by a SupplyProvider.")]
		public readonly int SupplyValue = 1;

		[Desc("Credit value per ammo unit. Missing ammo reduces sell/rotation value.")]
		public readonly int CreditValue = 0;

		[Desc("Sound to play for each reloaded ammo magazine.")]
		public readonly string RearmSound = null;

		[GrantedConditionReference]
		[Desc("The condition to grant to self for each ammo point in this pool.")]
		public readonly string AmmoCondition = null;

		public override object Create(ActorInitializer init) { return new AmmoPool(this); }

		string IProvideTooltipDescription.ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority)
		{
			priority = 100;

			if (Ammo <= 0 || SupplyValue <= 0)
				return null;

			// Walk the actor's armaments and pick out the ones that draw from this pool.
			// Multiple armaments can share one pool (e.g. dual-barrel burst weapons), so
			// list all the weapon names, joined with '+'. Falls back to the pool name if
			// no armaments link here (defensive — should not happen in well-formed YAML).
			var armaments = ai.TraitInfos<ArmamentInfo>()
				.Where(arm => Armaments.Contains(arm.Name))
				.ToArray();

			string label;
			if (armaments.Length == 0)
				label = FormatWeaponLabel(Name);
			else
				label = string.Join(" + ", armaments
					.Select(arm => FormatWeaponLabel(arm.Weapon))
					.Distinct());

			var totalCost = Ammo * SupplyValue;
			return $"{label}\n  Ammo: {Ammo} × {SupplyValue} supply = {totalCost}";
		}

		static string FormatWeaponLabel(string raw)
		{
			if (string.IsNullOrEmpty(raw))
				return "Weapon";

			// Strip leading hat (template marker) and convert PascalCase / dashed names
			// into a human-friendly label without changing the YAML key itself.
			var trimmed = raw.TrimStart('^').Replace('-', ' ').Replace('_', ' ');
			return trimmed;
		}
	}

	public class AmmoPool : INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync
	{
		public readonly AmmoPoolInfo Info;
		readonly Stack<int> tokens = new Stack<int>();
		IReloadAmmoModifier[] modifiers;

		/// <summary>
		/// Set when unit is out of ammo and ResupplyBehavior is Hold.
		/// Supply trucks with Hunt stance should seek out these units.
		/// </summary>
		public bool NeedsResupply { get; private set; }

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
			if (CurrentAmmoCount > 0)
				NeedsResupply = false;

			UpdateCondition(self);
			return true;
		}

		public bool TakeAmmo(Actor self, int count)
		{
			if (CurrentAmmoCount <= 0 || count < 0)
				return false;

			CurrentAmmoCount = (CurrentAmmoCount - count).Clamp(0, Info.Ammo);
			UpdateCondition(self);

			/* if (CurrentAmmoCount == 0)
			{
				AutoRearmIfAllEmpty(self);
			} */

			return true;
		}

		public void AutoRearmIfAllEmpty(Actor self)
		{
			var ammoPools = self.TraitsImplementing<AmmoPool>();
			if (!ammoPools.Any() || !ammoPools.All(a => !a.HasAmmo) || self.Info.HasTraitInfo<AircraftInfo>())
				return;

			// Check resupply behavior stance
			var autoTarget = self.TraitOrDefault<AutoTarget>();
			var behavior = autoTarget?.ResupplyBehaviorValue ?? ResupplyBehavior.Auto;

			switch (behavior)
			{
				case ResupplyBehavior.Auto:
					// Clear flag and seek resupply
					foreach (var ap in ammoPools)
						ap.NeedsResupply = false;

					AutoRearm(self);
					break;

				case ResupplyBehavior.Hold:
					// Stay put, flag for supply truck pickup
					foreach (var ap in ammoPools)
						ap.NeedsResupply = true;

					break;

				case ResupplyBehavior.Evacuate:
					// Leave the battlefield via Supply Route
					foreach (var ap in ammoPools)
						ap.NeedsResupply = false;

					var amount = self.GetSellValue();
					self.QueueActivity(false, new RotateToEdge(self, true, amount));
					self.ShowTargetLines();
					break;
			}
		}

		public void AutoRearmIfAnyNotFull(Actor self)
		{
			var ammoPools = self.TraitsImplementing<AmmoPool>();
			if (ammoPools.Any() && ammoPools.Any(a => !a.HasFullAmmo) && !self.Info.HasTraitInfo<AircraftInfo>())
				AutoRearm(self);
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
					AutoRearmIfAllEmpty(self);
			}
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			AutoRearmIfAllEmpty(self);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Resupply")
			{
				if (self.World.IsGameOver)
					return;

				var ammoPools = self.TraitsImplementing<AmmoPool>();
				if (ammoPools != null)
				{
					foreach (var ammoPool in ammoPools)
					{
						// OpenRA.Mods.Common.Traits.AmmoPool.AutoRearm(self);
						// Desyncs, orders needs to be synced, some kind of handshake involved.
						ammoPool.AutoRearmIfAnyNotFull(self);
					}
				}
			}
		}

		public static void AutoRearm(Actor self)
		{
			var nearestResupplier = ChooseResupplier(self);

			if (nearestResupplier != null)
			{
				// CargoSupply host (TRUK, etc.) — passive rearm model.
				// Use SeekCargoSupply so the unit re-picks if its target runs out of supply
				// mid-route, and shows a target line for the resupply move.
				var cargoSupply = nearestResupplier.TraitOrDefault<CargoSupply>();
				if (cargoSupply != null)
				{
					if (self.TraitOrDefault<IMove>() != null)
						self.QueueActivity(false, new SeekCargoSupply(self, nearestResupplier));

					return;
				}

				// RearmsUnits host (logisticscenter, etc.) — existing dock/rearm behavior
				var cargo = nearestResupplier.TraitOrDefault<Cargo>();
				if (cargo != null && self.Info.HasTraitInfo<PassengerInfo>())
				{
					var passenger = self.TraitOrDefault<Passenger>();
					if (passenger != null && cargo.HasSpace(self.Info.TraitInfo<PassengerInfo>().Weight))
					{
						self.QueueActivity(false, new RideTransport(self, Target.FromActor(nearestResupplier), null));
						return;
					}
				}

				self.QueueActivity(false, new Resupply(self, nearestResupplier, nearestResupplier.Trait<RearmsUnits>().Info.CloseEnough));
			}
			else
			{
				// No resupplier found — flag for supply truck pickup instead of evacuating.
				// Evacuation only happens when ResupplyBehavior is explicitly set to Evacuate.
				var ammoPools = self.TraitsImplementing<AmmoPool>();
				foreach (var ap in ammoPools)
					ap.NeedsResupply = true;
			}
		}

		public static Actor ChooseResupplier(Actor self)
		{
			var rearmInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();

			if (rearmInfo == null)
				return null;

			// Traditional RearmsUnits hosts (logisticscenter, etc.)
			var rearmsUnitsActors = self.World.ActorsHavingTrait<RearmsUnits>()
				.Where(a => !a.IsDead
					&& a.Owner == self.Owner
					&& rearmInfo.RearmActors.Contains(a.Info.Name));

			// CargoSupply hosts (TRUK, etc.) with supply remaining
			var cargoSupplyActors = self.World.ActorsHavingTrait<CargoSupply>()
				.Where(a => !a.IsDead
					&& a.Owner == self.Owner
					&& rearmInfo.RearmActors.Contains(a.Info.Name)
					&& a.Trait<CargoSupply>().EffectiveSupply > 0);

			return rearmsUnitsActors.Concat(cargoSupplyActors)
				.ClosestToIgnoringPath(self);
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
