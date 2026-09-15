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

	public class GarrisonProtection : INotifyDamage, INotifyCreated, IDamageModifier
	{
		readonly GarrisonProtectionInfo info;
		readonly Actor self;

		GarrisonManager garrisonManager;
		IHealth health;

		/// <summary>The damage the attacker AIMED at the building, before any IDamageModifier ran.
		/// Written by the observer below and consumed by Damaged on the same hit. See the rubble note
		/// there for why it has to exist at all.</summary>
		int aimedDamage;

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

		/// <summary>THE CLAMP RULE, as one expression so a fixture can state it. The damage a hit really
		/// delivers to the men in the shelter is the post-modifier value while the building still has
		/// hit points to lose, and the value the attacker AIMED once it does not -- which is the only
		/// way the curve can stay monotone across the clamp. A genuine zero stays zero: Health skips
		/// the modifier pass entirely unless damage.Value > 0, so a heal or a zero-damage warhead
		/// leaves `aimed` at zero and this returns zero rather than inventing a hit.</summary>
		public static int EffectiveIncomingDamage(int postModifierDamage, int aimedDamage)
		{
			return postModifierDamage > 0 ? postModifierDamage : aimedDamage;
		}

		/// <summary>OBSERVER ONLY -- always returns 100 and modifies nothing.
		/// <para>WHY A DAMAGE MODIFIER IS THE PLACE TO READ THIS. GarrisonManager.Indestructible clamps
		/// the building at 1 HP by returning a modifier of 0 for every hit once it is there
		/// (GarrisonManager.cs:1451-1468). Health applies the modifiers and only then notifies
		/// INotifyDamage (Health.cs:177-215), so at the clamp Damaged used to see a damage of ZERO and
		/// return without forwarding anything -- and a rubbled garrison became immune again, having
		/// been at its most exposed one hit point earlier. That is the non-monotone step this fixes:
		/// pass-through now stays at the curve's maximum at the clamp instead of dropping to nothing.
		/// IDamageModifier.GetDamageModifier is the one hook that sees the damage the attacker aimed,
		/// before the clamp erases it.</para>
		/// <para>Safe to stash across the two calls: Health calls every modifier exactly once and then
		/// notifies, in that order, on the same hit; Damaged clears the field unconditionally so a hit
		/// that was NOT clamped cannot leave a value behind for the next one; and the only other
		/// callers of GetDamageModifier in the engine (Demolishable, BridgeHut) pass a null damage,
		/// which the guard below ignores. The occupant damage is still inflicted from Damaged, not from
		/// here -- killing an actor while Health is mid-way through enumerating its modifiers is not a
		/// thing to start doing.</para></summary>
		int IDamageModifier.GetDamageModifier(Actor attacker, Damage damage)
		{
			if (damage != null && damage.Value > 0)
				aimedDamage = damage.Value;

			return 100;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			// Consume-and-clear FIRST and unconditionally, ahead of every early return below, so no
			// path can leave a stale aim behind to be spent on a later hit.
			var aimed = aimedDamage;
			aimedDamage = 0;

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

			// e.Damage is POST-modifier, so at the rubble clamp it is zero however hard the building
			// was hit. Fall back to what the attacker aimed: the shot happened, the building simply
			// had no hit points left to lose, and the men inside are the only thing left to absorb it.
			// A genuine zero (a heal, a zero-damage warhead) leaves `aimed` at zero too -- Health
			// skips the modifiers entirely unless damage.Value > 0 -- so this cannot invent damage.
			var incomingDamage = EffectiveIncomingDamage(e.Damage.Value, aimed);

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
