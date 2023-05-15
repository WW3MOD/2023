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

using System;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	[Desc("Apply damage to the targeted actor.")]
	public class TargetDamageWarhead : DamageWarhead
	{
		[Desc("Damage will be applied to actors in this area. A value of zero means only targeted actor will be damaged.")]
		public readonly WDist Spread = new WDist(1);

		/* protected override void InflictDamage(Actor victim, Actor firedBy, HitShape shape, WarheadArgs args) {} */

		protected override void DoImpact(WPos pos, Actor firedBy, WarheadArgs args)
		{
			if (Spread == WDist.Zero)
				return;

			var debugVis = firedBy.World.WorldActor.TraitOrDefault<DebugVisualizations>();
			if (debugVis != null && debugVis.CombatGeometry)
				firedBy.World.WorldActor.Trait<WarheadDebugOverlay>().AddImpact(pos, new[] { WDist.Zero, Spread }, DebugOverlayColor);

			foreach (var victim in firedBy.World.FindActorsOnCircle(pos, Spread))
			{
				if (!IsValidAgainst(victim, firedBy))
					continue;

				HitShape closestActiveShape = null;
				var closestDistance = int.MaxValue;

				// PERF: Avoid using TraitsImplementing<HitShape> that needs to find the actor in the trait dictionary.
				foreach (var targetPos in victim.EnabledTargetablePositions)
				{
					if (targetPos is HitShape hitshape)
					{
						var distance = hitshape.DistanceFromEdge(victim, pos).Length;
						if (distance < closestDistance)
						{
							closestDistance = distance;
							closestActiveShape = hitshape;
						}
					}
				}

				// Cannot be damaged without an active HitShape.
				if (closestActiveShape == null)
					continue;

				// Cannot be damaged if HitShape is outside Spread.
				if (closestDistance > Spread.Length)
					continue;

				var damage = closestActiveShape.PercentFromEdge(victim, args.ImpactPosition);

				// var adjustedModifiers = args.DamageModifiers.Append(damage); // what if there are multiple victims? Testing solution below
				var adjustedModifiers = Array.Empty<int>();
				adjustedModifiers.Append(args.DamageModifiers).Append(damage);

				var updatedWarheadArgs = new WarheadArgs(args)
				{
					DamageModifiers = adjustedModifiers.ToArray(),
					ImpactOrientation = args.ImpactOrientation,
				};

				InflictDamage(victim, firedBy, closestActiveShape, updatedWarheadArgs);
			}
		}
	}
}
