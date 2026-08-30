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
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	public abstract class DamageWarhead : Warhead
	{
		[Desc("How much armor this warhead can penetrate.")]
		public readonly int Penetration = 1;

		[Desc("The percent of damage to deal when firing at max range (e.g. kinetic weapons).")]
		public readonly int DamageAtMaxRange = 100;

		[Desc("How much (raw) damage to deal.")]
		public readonly int Damage = 0;

		[Desc("How much damage to deal in percent.")]
		public readonly int DamagePercent = 0;

		[Desc("Percent of damage to deal randomly for each unit, from this value to 100.")]
		public readonly int RandomDamagePercentFrom = 100;

		[Desc("Random extra damage for each victim (Total = Damage + Random(0, RandomDamage).")]
		public readonly int RandomDamageAddition = 0;

		[Desc("Random extra damage for each victim (Total = Damage + Random(0, RandomDamage).")]
		public readonly int RandomDamageSubtraction = 0;

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

		// Interpolates 100% at the muzzle down to DamageAtMaxRange% at the weapon's maximum range.
		// NOTE maxRange is the *warhead's own weapon* Range, which is zero for a weapon that is never
		// fired from an armament (an Explodes/SpawnedExplodes payload). Zero maxRange makes ofMax
		// non-finite, so the result is not a usable percentage — see BallisticPenetrationTest.
		public static int RangeDamageFactor(int range, int maxRange, int damageAtMaxRange)
		{
			var ofMax = (float)range / maxRange;
			var damage = ((1 - ofMax) * 100) + (ofMax * damageAtMaxRange);

			return (int)damage;
		}

		// Armour subtracts from damage only when it out-thicknesses the warhead: a warhead that
		// penetrates does full damage, one that does not keeps the fraction it got through.
		// Penetration defaults to 1, so a warhead that omits it delivers damage/thickness — which
		// against a 280mm tank is 0.4% of the number written in the YAML.
		public static int ApplyPenetration(int damage, int penetration, int thickness)
		{
			if (thickness <= 0 || penetration >= thickness)
				return damage;

			return damage * penetration / thickness;
		}

		protected virtual int RangeDamageMultiplier(Actor victim, Actor firedBy, WarheadArgs args)
		{
			var range = (args.Source - args.ImpactPosition).Value.HorizontalLength;
			return RangeDamageFactor(range, args.Weapon.Range.Length, DamageAtMaxRange);
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

			if (RandomDamageAddition != 0)
				damage += firedBy.World.SharedRandom.Next(0, RandomDamageAddition);

			if (RandomDamageSubtraction != 0)
				damage -= firedBy.World.SharedRandom.Next(0, RandomDamageSubtraction);

			if (RandomDamagePercentFrom != 100)
			{
				damage *= firedBy.World.SharedRandom.Next(RandomDamagePercentFrom, 100);
				damage /= 100;
			}

			var thickness = victim.Trait<Armor>().Info.Thickness;
			var damageBeforeArmour = damage;
			var effectiveThickness = 0;
			if (thickness != 0)
			{
				var armorPercent = ArmorDirectionPercent(victim, shape, args);

				// Kept in a local rather than inlined into the call: this is the number
				// ApplyPenetration actually compares against, and HitCheck below needs it. Feeding
				// it raw Thickness instead would report every top-attack weapon as broken -- the
				// ATGM's Penetration 100 clears an Abrams ROOF of 70 and is correctly sized, while
				// against the frontal 700 it looks seven times under-sized.
				effectiveThickness = thickness * armorPercent / 100;
				damage = ApplyPenetration(damage, Penetration, effectiveThickness);
			}

			// Anomaly detection, not tracing -- silent unless armour turned a lethal shot into a
			// non-lethal one. See HitCheck for the predicate and the measurements behind it.
			//
			// COST, because this line sits on every warhead application in the game: the gate is one
			// int comparison against a local plus a static call that early-returns after at most two
			// more. No allocation, no trait lookup, no dictionary probe. An unarmoured victim never
			// even reaches the call -- effectiveThickness is 0 and short-circuits it -- which is the
			// overwhelming majority of hits in an infantry fight. Hoisting effectiveThickness into a
			// local added no arithmetic; the same expression was previously written inline as the
			// argument to ApplyPenetration.
			//
			// Everything past the gate is rare by construction, and that is where the two lookups
			// live: Health for max HP, and DebugVisualizations for the on-screen banner. The second
			// is deliberately inside `if (loud)` rather than beside it -- the advisory band DOES fire
			// in a real match, and it must not pay for a trait lookup feeding a banner it will never
			// draw.
			if (effectiveThickness > 0 && HitCheck.LostMostOfItsDamage(damageBeforeArmour, damage))
			{
				var victimMaxHp = victim.TraitOrDefault<Health>()?.MaxHP ?? 0;
				var loud = HitCheck.IsUnderPerforming(damageBeforeArmour, damage, effectiveThickness, victimMaxHp);
				if (loud || HitCheck.IsUnderPerformingAgainstThinArmour(damageBeforeArmour, damage, effectiveThickness, victimMaxHp))
				{
					// Deduped inside Report, and the set is probed before any string is built.
					HitCheck.Report(firedBy.Info.Name, victim.Info.Name, GetType(), Damage, Penetration,
						damageBeforeArmour, damage, effectiveThickness, victimMaxHp);

					// On-screen half of the same signal, so a developer watching a match sees the
					// anomaly without tailing a file. Loud channel only -- the advisory band is
					// numerous enough to clutter the screen and is not what anyone is hunting.
					// NOT deduped, unlike the log: a repeat occurrence is worth seeing each time.
					if (loud)
					{
						var debugVis = firedBy.World.WorldActor.TraitOrDefault<DebugVisualizations>();
						if (debugVis != null && debugVis.DamageNumbers)
						{
							var text = $"ARMOUR {damageBeforeArmour}->{damage}";
							firedBy.World.AddFrameEndTask(w => w.Add(
								new FloatingText(victim.CenterPosition, Color.OrangeRed, text, 60)));
						}
					}
				}
			}

			if (DamagePercent != 0)
				damage += victim.TraitOrDefault<Health>().Info.HP * DamagePercent / 100;

			var modifiedDamage = Util.ApplyPercentageModifiers(damage, args.DamageModifiers.Append(DamageVersus(victim, shape, args)));

			if (GunTrace.Enabled)
				GunTrace.Write($"    InflictDamage victim={victim.Info.Name} rawDamage={Damage} afterThickness={damage} thickness={victim.Trait<Armor>().Info.Thickness} pen={Penetration} modifiers=[{string.Join(",", args.DamageModifiers)}] versus={DamageVersus(victim, shape, args)} FINAL={modifiedDamage} hpBefore={victim.TraitOrDefault<Health>()?.HP}");

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
