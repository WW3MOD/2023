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

using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Scales damage pass-through to shelter occupants (inside Cargo) based on building health state. " +
		"Port soldiers (deployed in-world) are NOT affected — they have their own DamageMultiplier via condition.")]
	public class GarrisonProtectionInfo : TraitInfo, Requires<GarrisonManagerInfo>, Requires<CargoInfo>
	{
		[Desc("Percentage of damage absorbed by the building at full HP (0-100). Remainder passes to a random shelter occupant.")]
		public readonly int BaseProtection = 80;

		[Desc("The interpolation's INTERCEPT AT ZERO HP (0-100) — NOT the value at DamageState.Critical. ",
			"Protection is CriticalProtection + (BaseProtection - CriticalProtection) * hpPct, so this is ",
			"only approached as HP tends to zero: at the 25% HP where DamageState.Critical actually begins, ",
			"protection is still a quarter of the way from here up to BaseProtection. Lower it to make a ",
			"damaged building degrade across the whole health bar instead of at the rubble clamp.")]
		public readonly int CriticalProtection = 30;

		[Desc("Percentage of damage absorbed when building is at minimum HP (rubble state, ",
			"clamped to 1HP by GarrisonManager.Indestructible). This replaces the interpolation outright ",
			"at 1 HP, so the distance between it and CriticalProtection is a cliff crossed in a single hit ",
			"point — keep the two close unless that cliff is wanted. Both default to 30, i.e. no cliff at ",
			"defaults. Default 30 means soldiers take 70% of incoming damage.")]
		public readonly int RubbleProtection = 30;

		[Desc("Minimum damage per hit to pass through to occupants. Hits below this deal zero to occupants.")]
		public readonly int MinPassThrough = 5;

		public override object Create(ActorInitializer init) { return new GarrisonProtection(init.Self, this); }
	}

	public class GarrisonProtection : INotifyDamage, INotifyCreated
	{
		readonly GarrisonProtectionInfo info;
		readonly Actor self;

		GarrisonManager garrisonManager;
		IHealth health;

		public GarrisonProtection(Actor self, GarrisonProtectionInfo info)
		{
			this.self = self;
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			garrisonManager = self.Trait<GarrisonManager>();

			// TraitOrDefault, NOT Trait: this trait is inherited by actors that remove Health while
			// keeping the garrison stack (V19.Husk, civilian.yaml:444-450 -- a wreck that still
			// carries Cargo/GarrisonManager/GarrisonProtection). Trait<IHealth>() throws
			// InvalidOperationException from TraitDictionary.Get on such an actor, which kills it at
			// construction. Nothing in the YAML can catch that: GarrisonProtectionInfo declares
			// Requires<GarrisonManagerInfo> and Requires<CargoInfo> but NOT Requires<HealthInfo>, so
			// no lint has anything to flag. The two health == null guards below were written for
			// exactly this case and were unreachable dead code while this line threw first.
			health = self.TraitOrDefault<IHealth>();
		}

		/// <summary>
		/// Returns the current shelter protection percentage (0-100). Uses RubbleProtection
		/// when the building is clamped to 1HP (Indestructible rubble), otherwise interpolates
		/// between BaseProtection (at full HP) and CriticalProtection (at 0 HP).
		/// </summary>
		public int GetCurrentProtection()
		{
			if (health == null || health.IsDead)
				return 0;

			if (health.HP <= 1)
				return info.RubbleProtection.Clamp(0, 100);

			var hpPct = (float)health.HP / health.MaxHP;
			var protection = (int)(info.CriticalProtection + (info.BaseProtection - info.CriticalProtection) * hpPct);
			return protection.Clamp(0, 100);
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (garrisonManager == null || health == null || health.IsDead)
				return;

			// Only pass damage to shelter soldiers (those inside Cargo, not deployed at ports)
			var shelterSoldiers = garrisonManager.ShelterPassengers
				.Where(s => s != null && !s.IsDead)
				.ToArray();

			if (shelterSoldiers.Length == 0)
				return;

			// One source of truth for the tier maths. This used to be a verbatim second copy of
			// GetCurrentProtection's body, with the public one never called by the private one --
			// two implementations of the same curve, free to drift, where the panel readout comes
			// from one and the damage that actually lands comes from the other. The health == null
			// and health.IsDead cases GetCurrentProtection folds to 0 are already returned above,
			// so the value is identical on every path that reaches here.
			var protection = GetCurrentProtection();

			var incomingDamage = e.Damage.Value;
			if (incomingDamage <= 0)
				return;

			var passThrough = incomingDamage * (100 - protection) / 100;
			if (passThrough < info.MinPassThrough)
				return;

			// Pick a random shelter soldier deterministically
			var targetIndex = self.World.SharedRandom.Next(shelterSoldiers.Length);
			var targetSoldier = shelterSoldiers[targetIndex];

			targetSoldier.InflictDamage(e.Attacker, new Damage(passThrough, e.Damage.DamageTypes));
		}
	}
}
