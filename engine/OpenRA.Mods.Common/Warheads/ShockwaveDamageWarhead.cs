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
	[Desc("Expanding shockwave that damages actors as the wavefront passes them.",
		"Creates a ShockwaveEffect that ticks independently in the world.")]
	public class ShockwaveDamageWarhead : DamageWarhead, IRulesetLoaded<WeaponInfo>
	{
		[Desc("Delay in ticks before the shockwave starts expanding.")]
		public readonly int StartDelay = 0;

		[Desc("Ticks per cell of wave travel. Higher = slower wave. 7 ≈ speed of sound at 100m/cell.")]
		public readonly int WaveSpeed = 7;

		[Desc("Maximum radius the shockwave expands to.")]
		public readonly WDist MaxRadius = WDist.FromCells(25);

		[Desc("Range between falloff steps, used to compute effective ranges from Falloff array.")]
		public readonly WDist Spread = WDist.FromCells(1);

		[Desc("Damage percentage at each range step from the center.")]
		public readonly int[] Falloff = { 100, 50, 25, 12, 6, 3, 1 };

		[Desc("Explicit ranges at which each Falloff step is defined. Overrides Spread.")]
		public readonly WDist[] Range = null;

		[Desc("Controls the way damage is calculated. Possible values are 'HitShape', 'ClosestTargetablePosition' and 'CenterPosition'.")]
		public readonly DamageCalculationType DamageCalculationType = DamageCalculationType.HitShape;

		[Desc("Color of the shockwave ring visual. Set alpha to 0 to disable.")]
		public readonly Color ShockwaveColor = Color.FromArgb(180, 255, 255, 255);

		[Desc("Width of the shockwave ring line in pixels.")]
		public readonly float ShockwaveWidth = 3f;

		[Desc("Width of the shockwave ring border in pixels. 0 = no border.")]
		public readonly float ShockwaveBorderWidth = 1f;

		[Desc("Color of the shockwave ring border.")]
		public readonly Color ShockwaveBorderColor = Color.FromArgb(100, 200, 200, 200);

		[Desc("Alpha of the shockwave ring at MaxRadius, as fraction of initial alpha (0-100).")]
		public readonly int ShockwaveEndAlphaPercent = 15;

		WDist[] effectiveRange;

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
		}

		protected override void DoImpact(WPos pos, Actor firedBy, WarheadArgs args)
		{
			var debugVis = firedBy.World.WorldActor.TraitOrDefault<DebugVisualizations>();
			if (debugVis != null && debugVis.CombatGeometry)
				firedBy.World.WorldActor.Trait<WarheadDebugOverlay>().AddImpact(pos, effectiveRange, DebugOverlayColor);

			firedBy.World.AddFrameEndTask(w => w.Add(
				new ShockwaveEffect(w, this, pos, firedBy, args)));
		}

		/// <summary>Apply blast damage to a single actor. Called by ShockwaveEffect as the wavefront passes.</summary>
		public void ApplyBlastDamage(Actor victim, Actor firedBy, WPos center, WarheadArgs args)
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

			// Impact comes radially from the blast center
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
