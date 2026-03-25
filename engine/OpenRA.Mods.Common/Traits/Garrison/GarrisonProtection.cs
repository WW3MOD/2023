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

		[Desc("Percentage of damage absorbed at critical damage state (0-100).")]
		public readonly int CriticalProtection = 30;

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
			health = self.Trait<IHealth>();
		}

		/// <summary>
		/// Returns the current shelter protection percentage (0-100), interpolated between
		/// BaseProtection (at full HP) and CriticalProtection (at 0 HP).
		/// </summary>
		public int GetCurrentProtection()
		{
			if (health == null || health.IsDead)
				return 0;

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

			// Interpolate protection based on building HP percentage
			var hpPct = (float)health.HP / health.MaxHP;
			var protection = (int)(info.CriticalProtection + (info.BaseProtection - info.CriticalProtection) * hpPct);
			protection = protection.Clamp(0, 100);

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
