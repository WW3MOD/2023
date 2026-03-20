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
	[Desc("Scales damage pass-through to garrisoned occupants based on building health state.")]
	public class GarrisonProtectionInfo : TraitInfo, Requires<GarrisonManagerInfo>, Requires<CargoInfo>
	{
		[Desc("Percentage of damage absorbed by the building at full HP (0-100). Remainder passes to a random occupant.")]
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

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (garrisonManager == null || health == null || health.IsDead)
				return;

			// Find occupied ports
			var occupiedPorts = garrisonManager.PortStates
				.Where(ps => ps.Occupant != null && !ps.Occupant.IsDead)
				.ToArray();

			if (occupiedPorts.Length == 0)
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

			// Pick a random occupied port deterministically
			var targetIndex = self.World.SharedRandom.Next(occupiedPorts.Length);
			var targetOccupant = occupiedPorts[targetIndex].Occupant;

			targetOccupant.InflictDamage(e.Attacker, new Damage(passThrough, e.Damage.DamageTypes));
		}
	}
}
