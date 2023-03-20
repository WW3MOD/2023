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
		[Desc("How much (raw) damage to deal.")]
		public readonly int Damage = 0;

		[Desc("How much damage to deal in percent.")]
		public readonly int DamagePercent = 0;

		[Desc("Random extra damage for each victim (Total = Damage + Random(0, RandomDamage)")]
		public readonly int RandomDamage = 0;

		[Desc("Apply the damage for this many ticks after initial")]
		public readonly int Duration = 0;

		[Desc("Apply the Damage over time slower by waiting this many between each hit")]
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
			// If no Versus values are defined, DamageVersus would return 100 anyway, so we might as well do that early.
			if (Versus.Count == 0)
				return 100;

			var armor = victim.TraitsImplementing<Armor>()
				.Where(a => !a.IsTraitDisabled && a.Info.Type != null && Versus.ContainsKey(a.Info.Type) &&
					(shape.Info.ArmorTypes.IsEmpty || shape.Info.ArmorTypes.Contains(a.Info.Type)))
				.Select(a => Versus[a.Info.Type]);

			return Util.ApplyPercentageModifiers(100, armor);
		}

		protected virtual void InflictDamage(Actor victim, Actor firedBy, HitShape shape, WarheadArgs args)
		{
			var damage = Damage;
			if (RandomDamage != 0)
				damage += firedBy.World.SharedRandom.Next(0, RandomDamage);

			if (DamagePercent != 0)
				damage += victim.TraitOrDefault<Health>().Info.HP * DamagePercent / 100;

			var modifiedDamage = Util.ApplyPercentageModifiers(damage, args.DamageModifiers.Append(DamageVersus(victim, shape, args)));

			if (Duration > 0)
			{
				// Game.Debug("Inflict Durational Damage {0}", Duration);
				victim.InflictDamage(firedBy, new Actor.DamageOverTime(Duration, Modulus, new Damage(modifiedDamage, DamageTypes)));
			}
			else
				victim.InflictDamage(firedBy, new Damage(modifiedDamage, DamageTypes));
		}

		protected abstract void DoImpact(WPos pos, Actor firedBy, WarheadArgs args);
	}
}
