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
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	public abstract class DamageWarhead : Warhead
	{
		[Desc("How much armor this warhead can penetrate.")]
		public readonly int Penetration = 1;

		[Desc("Warhead always does full damage regardless of range (e.g. missiles).")]
		public readonly bool IgnoreRangeFalloff = false;

		[Desc("The percent of damage to deal when firing at max range (e.g. kinetic weapons).")]
		public readonly int DamageAtMaxRange = 50;

		[Desc("How far out in % will this projectile to maximum damage before starting to falloff to DamageAtMaxRange.")]
		public readonly int MaxDamageRangePercent = 50; // Unimplemented

		[Desc("How much (raw) damage to deal.")]
		public readonly int Damage = 0;

		[Desc("How much damage to deal in percent.")]
		public readonly int DamagePercent = 0;

		[Desc("Random extra damage for each victim (Total = Damage + Random(0, RandomDamage).")]
		public readonly int RandomDamage = 0;

		[Desc("Apply the damage for this many ticks after initial.")]
		public readonly int Duration = 0;

		[Desc("Apply the Damage over time slower by waiting this many ticks between each hit.")]
		public readonly int Modulus = 0;

		[Desc("Types of damage that this warhead causes. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> DamageTypes = default;

		[Desc("Damage percentage versus each armor type.")]
		public readonly Dictionary<string, int> Versus = new Dictionary<string, int>();

		public override bool IsValidAgainst(Actor victim, Actor firedBy)
		{
			// Cannot be damaged without a Health trait
			if (!victim.Info.HasTraitInfo<IHealthInfo>())
				return false;

			return base.IsValidAgainst(victim, firedBy);
		}

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var firedBy = args.SourceActor;

			// Used by traits or warheads that damage a single actor, rather than a position
			if (target.Type == TargetType.Actor)
			{
				var victim = target.Actor;

				if (!IsValidAgainst(victim, firedBy))
					return;

				// PERF: Avoid using TraitsImplementing<HitShape> that needs to find the actor in the trait dictionary.
				var closestActiveShape = (HitShape)victim.EnabledTargetablePositions.MinByOrDefault(t =>
				{
					if (t is HitShape h)
						return h.DistanceFromEdge(victim, victim.CenterPosition);
					else
						return WDist.MaxValue;
				});

				// Cannot be damaged without an active HitShape
				if (closestActiveShape == null)
					return;

				InflictDamage(victim, firedBy, closestActiveShape, args);
			}
			else if (target.Type != TargetType.Invalid)
				DoImpact(target.CenterPosition, firedBy, args);
		}

		protected virtual int DamageVersus(Actor victim, HitShape shape, WarheadArgs args)
		{
			var damage = 100;

			// If no Versus values are defined, DamageVersus can be ignored.
			if (Versus.Count == 0)
				return damage;

			var armorVs = victim.TraitsImplementing<Armor>()
				.Where(a => !a.IsTraitDisabled && a.Info.Type != null && Versus.ContainsKey(a.Info.Type)
					&& (shape.Info.ArmorTypes.IsEmpty || shape.Info.ArmorTypes.Contains(a.Info.Type)));

			return Util.ApplyPercentageModifiers(damage, armorVs.Select(a => Versus[a.Info.Type]));
		}

		protected virtual int RangeDamageMultiplier(Actor victim, Actor firedBy, WarheadArgs args)
		{
			var range = (args.Source - args.ImpactPosition).Value.HorizontalLength;
			var maxRange = args.Weapon.Range.Length;
			var ofMax = (float)range / maxRange;
			var damage = ((1 - ofMax) * 100) + (ofMax * DamageAtMaxRange);

			return (int)damage;
		}

		protected virtual int ArmorDirectionPercent(Actor victim, HitShape shape, WarheadArgs args)
		{
			var armorPercent = 100;

			var distribution = victim.TraitsImplementing<Armor>()
				.First(a => !a.IsTraitDisabled).Info.Distribution;

			// Directional damage, e.g. higher damage from the rear
			if (distribution.Length == 5)
			{
				if (args.Weapon.TopAttack)
				{
					return distribution[3];
				}
				else if (args.Weapon.BottomAttack)
				{
					return distribution[4];
				}
				else
				{
					var victimYaw = victim.Orientation.Yaw;
					var projectileYaw = args.ImpactOrientation.Yaw;

					var alignment = victimYaw - projectileYaw;

					var frontAlignment = (alignment + new WAngle(512)).Angle;
					var rearAlignment = alignment.Angle;
					var leftAlignment = (alignment - new WAngle(256)).Angle;
					var rightAlignment = (alignment + new WAngle(256)).Angle;

					float frontModifier = 0;
					float rearModifier = 0;
					float leftModifier = 0;
					float rightModifier = 0;

					if (frontAlignment < 256)
					{
						frontModifier = (float)(256 - frontAlignment) / 256f;
					}
					else if (frontAlignment > 768)
					{
						frontModifier = (float)(frontAlignment - 768) / 256f;
					}
					else
					{
						if (rearAlignment < 512)
							rearModifier = (float)(256 - rearAlignment) / 256f;
						else
							rearModifier = (float)(rearAlignment - 768) / 256f;
					}

					if (leftAlignment < 256)
					{
						leftModifier = (float)(256 - leftAlignment) / 256f;
					}
					else if (leftAlignment > 768)
					{
						leftModifier = (float)(leftAlignment - 768) / 256f;
					}
					else
					{
						if (rightAlignment < 256)
							rightModifier = (float)(256 - rightAlignment) / 256f;
						else if (rightAlignment > 256)
							rightModifier = (float)(rightAlignment - 768) / 256f;
					}

					var frontDamage = frontModifier * 100f * (distribution[0] / 100f);
					var leftDamage = leftModifier * 100f * (distribution[1] / 100f);
					var rightDamage = rightModifier * 100f * (distribution[1] / 100f);
					var rearDamage = rearModifier * 100f * (distribution[2] / 100f);

					return (int)(frontDamage + leftDamage + rightDamage + rearDamage);
				}
			}

			return armorPercent;
		}

		protected virtual void InflictDamage(Actor victim, Actor firedBy, HitShape shape, WarheadArgs args)
		{
			var damage = Damage;
			if (RandomDamage != 0)
				damage += firedBy.World.SharedRandom.Next(0, RandomDamage);

			if (!Info.IgnoreRangeFalloff)
				damage = damage * RangeDamageMultiplier(victim, firedBy, args) / 100;

			var thickness = victim.Trait<Armor>().Info.Thickness;
			if (thickness != 0)
			{
				var armorPercent = ArmorDirectionPercent(victim, shape, args);
				thickness = thickness * armorPercent / 100;

				var penetration = Penetration;

				var diff = penetration - thickness;

				if (diff < 0)
				{
					// Can't penetrate - Reduce damage by how much it penetrated
					damage = damage * penetration / thickness;
				} // TODO: damage more when penetrating? Or less if not?
			}

			if (DamagePercent != 0)
				damage += victim.TraitOrDefault<Health>().Info.HP * DamagePercent / 100;

			var modifiedDamage = Util.ApplyPercentageModifiers(damage, args.DamageModifiers.Append(DamageVersus(victim, shape, args)));

			if (Duration > 0)
			{
				victim.InflictDamage(firedBy, new Actor.DamageOverTime(Duration, Modulus, new Damage(modifiedDamage, DamageTypes)));
			}
			else
				victim.InflictDamage(firedBy, new Damage(modifiedDamage, DamageTypes));
		}

		protected abstract void DoImpact(WPos pos, Actor firedBy, WarheadArgs args);
	}
}
