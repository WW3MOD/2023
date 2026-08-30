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
using OpenRA.Mods.Common.Graphics;
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

		[Desc("Base color of the shockwave ring (RGB only, alpha controlled separately). Set A to 0 to disable visual.")]
		public readonly Color ShockwaveColor = Color.FromArgb(255, 255, 255, 255);

		[Desc("Thickness of the shockwave ring band in WDist.")]
		public readonly WDist ShockwaveThickness = new WDist(1536);

		[Desc("Alpha at the outer (leading) edge of the shockwave ring, 0-100.")]
		public readonly int ShockwaveOuterAlpha = 8;

		[Desc("Alpha at the inner (trailing/dust) edge of the shockwave ring, 0-100.")]
		public readonly int ShockwaveInnerAlpha = 3;

		[Desc("Alpha of the shockwave ring where it stops, as percentage of initial alpha (0-100).",
			"That edge is ShockwaveVisualRadius when set, MaxRadius otherwise.")]
		public readonly int ShockwaveEndAlphaPercent = 0;

		[Desc("Ticks for the shockwave ring to fade in from fully transparent. Simulates fireball origin.")]
		public readonly int ShockwaveFadeInTicks = 25;

		[Desc("Radius at which the visible RING stops and its fade completes. Zero follows MaxRadius.",
			"MaxRadius alone cannot express a small ring on a wide blast, because it bounds the",
			"wavefront's travel and the wavefront has to REACH an actor to hurt it — so the damage",
			"reach is min(MaxRadius, (Falloff.Length - 1) * Spread) and cutting MaxRadius to shrink",
			"the visual silently shortens the lethal radius too whenever MaxRadius is the smaller term.",
			"Set this instead to rescale the ring while leaving damage exactly where it was.")]
		public readonly WDist ShockwaveVisualRadius = WDist.Zero;

		[Desc("Sides in the ring polygon. Lower is cheaper and more angular; 64 suits large radii.")]
		public readonly int ShockwaveSegments = 64;

		[Desc("How fast the band grows to its full ShockwaveThickness, as a percentage of expansion",
			"progress. 250 reaches full thickness two fifths of the way out; 100 only at the ring's edge.")]
		public readonly int ShockwaveThicknessRampPercent = 250;

		[Desc("Outer edge of the ring's bright core, as a percentage of band width from the inner edge.")]
		public readonly int ShockwavePeakOuterPercent = 75;

		[Desc("Inner edge of the ring's bright core, as a percentage of band width from the inner edge.")]
		public readonly int ShockwavePeakInnerPercent = 55;

		[Desc("How far the transparent feather overshoots the leading edge, as a percentage of band width.")]
		public readonly int ShockwaveOuterFeatherPercent = 15;

		WDist[] effectiveRange;

		/// <summary>Radius the ring is drawn out to, which need not be how far the wave travels.</summary>
		public WDist VisualRadius => ShockwaveVisualRadius.Length > 0 ? ShockwaveVisualRadius : MaxRadius;

		public ShockwaveRingShape RingShape => new ShockwaveRingShape(
			ShockwaveSegments, ShockwavePeakOuterPercent, ShockwavePeakInnerPercent, ShockwaveOuterFeatherPercent);

		/// <summary>
		/// Whether the wave carries anything worth scanning actors for. A ring that delivers no damage
		/// is purely decorative, and the per-tick FindActorsOnCircle sweep is then not merely wasted:
		/// a zero-damage InflictDamage still fires INotifyDamage and marks its victims as attacked.
		/// Checks every additive term InflictDamage reads, not just Damage.
		/// </summary>
		public bool DeliversDamage => Damage != 0 || DamagePercent != 0 || RandomDamageAddition != 0;

		void IRulesetLoaded<WeaponInfo>.RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (ShockwaveSegments < 3)
				throw new YamlException("ShockwaveSegments must be at least 3.");

			if (ShockwaveVisualRadius.Length < 0)
				throw new YamlException("ShockwaveVisualRadius cannot be negative.");

			// Past MaxRadius the wave has already ended, so the ring would be cut off mid-fade and
			// disappear at partial alpha — subtle enough as an artefact to be worth refusing at load.
			if (ShockwaveVisualRadius > MaxRadius)
				throw new YamlException("ShockwaveVisualRadius cannot exceed MaxRadius; the ring cannot outlive the wave.");

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
