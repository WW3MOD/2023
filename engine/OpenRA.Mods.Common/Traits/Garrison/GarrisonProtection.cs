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

			// TraitOrDefault, NOT Trait, and it stays that way even though the actor it was written for
			// is gone. V19.Husk used to keep Cargo/GarrisonManager/GarrisonProtection while removing
			// Health; 5dfc6c09 took the garrison stack off the wreck, so there is no actor in the mod
			// today that reaches this line without an IHealth. What has NOT changed is the reason the
			// hazard existed: GarrisonProtectionInfo declares Requires<GarrisonManagerInfo> and
			// Requires<CargoInfo> but NOT Requires<HealthInfo>, so nothing in the YAML or the lint
			// stops someone re-creating it, and Trait<IHealth>() would throw InvalidOperationException
			// out of TraitDictionary.Get and kill the actor at construction. The two health == null
			// guards below are the same insurance and are likewise unreachable today.
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

			return ProtectionAt(health.HP, health.MaxHP, info.BaseProtection, info.CriticalProtection, info.RubbleProtection);
		}

		/// <summary>The curve itself, with no actor behind it, so a fixture can walk it from full health
		/// to the rubble clamp and assert it never turns back up. Arithmetic is byte-for-byte what
		/// GetCurrentProtection did before the extraction -- the float and its truncation included --
		/// because this is a testability seam and not a retune.
		/// <para>MONOTONICITY IS A YAML PROPERTY, NOT A CODE ONE: the value at the clamp is
		/// <paramref name="rubbleProtection"/> outright, while the value just above it tends to
		/// <paramref name="criticalProtection"/>, so the curve only descends all the way if
		/// rubble &lt;= critical. Both are authored per actor; GarrisonRubbleProtectionTest checks
		/// every actor in the mod that declares the trait.</para></summary>
		public static int ProtectionAt(int hp, int maxHp, int baseProtection, int criticalProtection, int rubbleProtection)
		{
			if (hp <= 1)
				return rubbleProtection.Clamp(0, 100);

			var hpPct = (float)hp / maxHp;
			var protection = (int)(criticalProtection + (baseProtection - criticalProtection) * hpPct);
			return protection.Clamp(0, 100);
		}

		/// <summary>Damage forwarded to one shelter occupant for a hit of <paramref name="incomingDamage"/>
		/// against a building at <paramref name="protection"/>. Zero when the share falls under
		/// <paramref name="minPassThrough"/>, which is a floor on the HIT and not on the curve.</summary>
		public static int PassThroughFor(int incomingDamage, int protection, int minPassThrough)
		{
			if (incomingDamage <= 0)
				return 0;

			var passThrough = incomingDamage * (100 - protection) / 100;
			return passThrough < minPassThrough ? 0 : passThrough;
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

			// e.Damage is what the attacker AIMED, and at the rubble floor that is exactly the number
			// wanted: Health's floor limits the HP, not the reported damage (IDamageFloor), so a hit
			// on a building that has nothing left to lose still arrives here at full size and the men
			// absorb all of it. That is the monotone end of the curve rather than a special case.
			//
			// THIS USED TO NEED A STASH. Indestructible was an IDamageModifier returning 0 at the
			// clamp, Health applies modifiers before notifying, so this read ZERO and forwarded
			// nothing -- a rubbled garrison was immune. The first fix recorded the pre-modifier value
			// through an IDamageModifier observer of its own and substituted it whenever this read
			// zero, which was wrong in a way the fixture found: a zero also arose when the old
			// percentage TRUNCATED, at any HP, and the substitution then forwarded a full share while
			// the building had absorbed nothing. Run 260915_184945 killed four men that way at 2/20 HP.
			// Moving the clamp to a floor removed both the zero and the need to guess about it.
			var incomingDamage = e.Damage.Value;

			var passThrough = PassThroughFor(incomingDamage, protection, info.MinPassThrough);
			if (passThrough <= 0)
				return;

			// Pick a random shelter soldier deterministically
			var targetIndex = self.World.SharedRandom.Next(shelterSoldiers.Length);
			var targetSoldier = shelterSoldiers[targetIndex];

			targetSoldier.InflictDamage(e.Attacker, new Damage(passThrough, e.Damage.DamageTypes));
		}
	}
}
