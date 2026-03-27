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
using OpenRA.GameRules;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	[Desc("Sustained thermal radiation that pulses damage over a duration.",
		"Units close to the center are cooked rapidly; distant units take progressively less.",
		"Creates a ThermalRadiationEffect that ticks independently in the world.")]
	public class ThermalRadiationWarhead : DamageWarhead, IRulesetLoaded<WeaponInfo>
	{
		[Desc("Total duration of thermal radiation in ticks.")]
		public readonly int RadiationDuration = 50;

		[Desc("Ticks between each damage pulse. Lower = more pulses = smoother visible health drain.")]
		public readonly int DamageInterval = 3;

		[Desc("Range between falloff steps, used to compute effective ranges from Falloff array.")]
		public readonly WDist Spread = WDist.FromCells(1);

		[Desc("Damage percentage at each range step from the center. Should follow inverse-square-ish curve.")]
		public readonly int[] Falloff = { 100, 50, 25, 12, 6, 3, 1 };

		[Desc("Explicit ranges at which each Falloff step is defined. Overrides Spread.")]
		public readonly WDist[] Range = null;

		[Desc("Controls the way damage distance is calculated. Possible values are 'HitShape', 'ClosestTargetablePosition' and 'CenterPosition'.")]
		public readonly DamageCalculationType DamageCalculationType = DamageCalculationType.HitShape;

		WDist[] effectiveRange;

		/// <summary>Maximum range of the thermal radiation effect.</summary>
		public WDist MaxRange { get; private set; }

		void IRulesetLoaded<WeaponInfo>.RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (Range != null)
			{
				if (Range.Length != 1 && Range.Length != Falloff.Length)
					throw new YamlException("Number of range values must be 1 or equal to the number of Falloff values.");

				for (var i = 0; i < Range.Length - 1; i++)
					if (Range[i] > Range[i + 1])
						throw new YamlException("Range values must be specified in an increasing order.");

				effectiveRange = Range;
			}
			else
				effectiveRange = Exts.MakeArray(Falloff.Length, i => i * Spread);

			MaxRange = effectiveRange[effectiveRange.Length - 1];
		}

		protected override void DoImpact(WPos pos, Actor firedBy, WarheadArgs args)
		{
			firedBy.World.AddFrameEndTask(w => w.Add(
				new ThermalRadiationEffect(w, this, pos, firedBy, args)));
		}

		/// <summary>Apply thermal damage to a single actor. Called by ThermalRadiationEffect each pulse.</summary>
		public void ApplyThermalDamage(Actor victim, Actor firedBy, WPos center, WarheadArgs args)
		{
			if (!IsValidAgainst(victim, firedBy))
				return;

			HitShape closestActiveShape = null;
			var closestDistance = int.MaxValue;

			foreach (var targetPos in victim.EnabledTargetablePositions)
			{
				if (targetPos is HitShape h)
				{
					var distance = h.DistanceFromEdge(victim, center).Length;
					if (distance < closestDistance)
					{
						closestDistance = distance;
						closestActiveShape = h;
					}
				}
			}

			if (closestActiveShape == null)
				return;

			var falloffDistance = 0;
			switch (DamageCalculationType)
			{
				case DamageCalculationType.HitShape:
					falloffDistance = closestDistance;
					break;
				case DamageCalculationType.ClosestTargetablePosition:
					falloffDistance = victim.GetTargetablePositions().Select(x => (x - center).Length).Min();
					break;
				case DamageCalculationType.CenterPosition:
					falloffDistance = (victim.CenterPosition - center).Length;
					break;
			}

			if (falloffDistance > effectiveRange[effectiveRange.Length - 1].Length)
				return;

			var localModifiers = args.DamageModifiers.Append(GetDamageFalloff(falloffDistance));

			// Thermal radiation comes radially from the fireball center
			var towardsTargetYaw = (victim.CenterPosition - center).Yaw;
			var impactAngle = Util.GetVerticalAngle(center, victim.CenterPosition);
			var impactOrientation = new WRot(WAngle.Zero, impactAngle, towardsTargetYaw);

			var updatedWarheadArgs = new WarheadArgs(args)
			{
				DamageModifiers = localModifiers.ToArray(),
				ImpactOrientation = impactOrientation,
			};

			InflictDamage(victim, firedBy, closestActiveShape, updatedWarheadArgs);
		}

		int GetDamageFalloff(int distance)
		{
			var inner = effectiveRange[0].Length;
			for (var i = 1; i < effectiveRange.Length; i++)
			{
				var outer = effectiveRange[i].Length;
				if (outer > distance)
					return int2.Lerp(Falloff[i - 1], Falloff[i], distance - inner, outer - inner);

				inner = outer;
			}

			return 0;
		}
	}
}
