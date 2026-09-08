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

		[Desc("Ticks per cell of wave travel once the front has decayed to the speed of sound.",
			"Higher = slower wave. 7 is ~380 m/s at this mod's 160 m/cell blast scale, i.e. sound.")]
		public readonly int WaveSpeed = 7;

		[Desc("Radius the wavefront ALREADY OCCUPIES on the tick it is created, instead of starting at",
			"a point. A nuclear detonation has no travelling wave inside its own fireball: the shock is",
			"attached to the fireball surface while that surface is still expanding supersonically, and",
			"only detaches (`breakaway`) once it falls toward Mach 1. Everything inside is already gone",
			"before there is a wave to speak of, so the front is born at the breakaway radius.",
			"Actors inside it are damaged on the first tick, at the innermost Falloff step.",
			"The default 0 is a point source, i.e. exactly the pre-2026-09-06 behaviour.")]
		public readonly WDist StartRadius = WDist.Zero;

		[Desc("Wave speed at StartRadius, as a percentage of the speed WaveSpeed implies — i.e. the",
			"Mach number at breakaway times 100. 551 is a 20 kt airburst, 405 a 6 Mt one (they differ",
			"because breakaway happens at a LOWER overpressure for a larger weapon). The default 100",
			"is sonic from the first tick, which is the historical behaviour and makes",
			"SpeedDecayPercent irrelevant.")]
		public readonly int InitialSpeedPercent = 100;

		[Desc("Radius at which the front has finished decaying and is travelling at exactly WaveSpeed.",
			"THIS IS THE FIELD THAT SHAPES THE WAVE, and the one to set. A nuclear shock has two",
			"phases, not one: a supersonic sweep from the fireball surface out to roughly TWICE the",
			"fireball radius, then the long sonic remainder. Set this to 2 * StartRadius and the",
			"excess speed above Mach 1 falls LINEARLY IN RADIUS from InitialSpeedPercent at",
			"StartRadius to zero here — linear in radius rather than in time, so the transition",
			"happens at a PLACE you can point at on the map instead of at a tick you have to derive.",
			"Zero (the default) leaves the front on the older SpeedDecayPercent geometric-in-time",
			"decay, which is retained only so the default path stays bit-identical.")]
		public readonly WDist TransitionRadius = WDist.Zero;

		[Desc("SUPERSEDED by TransitionRadius, and ignored whenever that is set. Fraction of the",
			"CURRENT excess speed above Mach 1 that survives each tick, as a percentage. Decaying in",
			"TIME rather than in radius is why this needed a hand-derived constant per weapon and",
			"still landed the transition at 3.6x the fireball radius on one weapon and 5x on another",
			"instead of at the 2x both were supposed to share. Kept so the 100 default — no decay",
			"because there is no excess to decay — remains exactly the pre-2026-09-06 behaviour.",
			"Integer, and applied by integer division, so the wavefront position is exactly",
			"reproducible — this is a damage-bearing quantity and must not go through a float.")]
		public readonly int SpeedDecayPercent = 100;

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
			"That edge is ShockwaveVisualRadius when set, MaxRadius otherwise. The default 0 means every",
			"ring fades out completely instead of being cut off while still solid; anything above 0 leaves",
			"a visible band at full radius that vanishes between one frame and the next.")]
		public readonly int ShockwaveEndAlphaPercent = 0;

		[Desc("Shape of the fade from full alpha down to ShockwaveEndAlphaPercent, as a percentage",
			"exponent on expansion progress: alpha follows 1 - progress^(percent/100).",
			"100 is a straight line, spending alpha evenly over the ring's travel. The default 200",
			"instead holds the ring near full brightness while it is still growing and then drops it",
			"away over the last fifth, which is what reads as a wave dissipating rather than a ring",
			"being switched off — 1 - p^2 factors to (1 + p) times the linear ramp, so it is brighter",
			"everywhere in between and still lands on exactly the same endpoints.",
			"Below 100 the fade is hardest at the START, which is where a small ring is still inside",
			"its own fireball sprite; that is how a ring gets tuned into invisibility.")]
		public readonly int ShockwaveFadeOutExponentPercent = 200;

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

		/// <summary>
		/// Ring alpha as a fraction of its starting alpha, <paramref name="progress"/> of the way out to
		/// the edge the ring is drawn to. Lives on the warhead rather than inline in ShockwaveEffect so
		/// that the curve the game renders and the curve ShockwaveTuningTest pins are the same code.
		/// </summary>
		public float FadeOutAt(float progress)
		{
			var endAlphaFrac = ShockwaveEndAlphaPercent / 100f;
			var falloff = 1f - (float)System.Math.Pow(progress, ShockwaveFadeOutExponentPercent / 100f);
			return endAlphaFrac + (1f - endAlphaFrac) * falloff;
		}

		/// <summary>
		/// <para>Excess speed above Mach 1, in permille of the sonic speed, for a front that has reached
		/// <paramref name="currentRadius"/>. Falls linearly from its birth value at StartRadius to zero at
		/// TransitionRadius, which is the whole two-phase model: supersonic sweep, then sonic remainder.</para>
		///
		/// <para>WHY LINEAR IN RADIUS. The mod's blast law is R proportional to P^-0.589, so overpressure —
		/// and through Rankine-Hugoniot the excess Mach number with it — falls as roughly R^-1.7. Across
		/// the one octave from StartRadius to twice it, the straight line between the endpoints is the
		/// CHORD of that curve: the two agree to within a percent at the midpoint (0.500 against 0.494)
		/// and are never more than about 0.12 apart anywhere in between. What the chord buys over the
		/// power law is that it lands on zero at a finite radius instead of trailing a permanent
		/// supersonic tail out to the map edge, so "where does it stop being fast" has an answer.</para>
		///
		/// <para>Integer throughout, long only to keep the product from overflowing on an absurd YAML. This
		/// decides which tick an actor takes blast damage on, so it is simulation state: no floats.</para>
		/// </summary>
		public int ExcessPermilleAt(int currentRadius)
		{
			var span = TransitionRadius.Length - StartRadius.Length;
			var remaining = TransitionRadius.Length - currentRadius;
			if (remaining <= 0)
				return 0;

			return (int)((long)(InitialSpeedPercent - 100) * 10 * remaining / span);
		}

		void IRulesetLoaded<WeaponInfo>.RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (ShockwaveSegments < 3)
				throw new YamlException("ShockwaveSegments must be at least 3.");

			if (StartRadius.Length < 0)
				throw new YamlException("StartRadius cannot be negative.");

			// A front born outside its own MaxRadius finishes on the tick it starts and delivers one
			// silent full-radius hit, which looks exactly like the warhead not being wired up at all.
			if (StartRadius >= MaxRadius)
				throw new YamlException("StartRadius must be less than MaxRadius; the wave would end before it moved.");

			// Below 100 the front would start SLOWER than sound and then accelerate as the excess
			// decays toward zero, which is backwards and is the easy typo here.
			if (InitialSpeedPercent < 100)
				throw new YamlException("InitialSpeedPercent cannot be below 100; a shock front never starts subsonic.");

			if (SpeedDecayPercent < 0 || SpeedDecayPercent > 100)
				throw new YamlException("SpeedDecayPercent must be between 0 and 100; it is the fraction of the excess speed kept per tick.");

			if (TransitionRadius.Length < 0)
				throw new YamlException("TransitionRadius cannot be negative.");

			if (TransitionRadius.Length > 0)
			{
				// The two decay laws would silently fight, and the loser would be whichever the reader
				// did not expect. Refuse the ambiguity rather than document which one wins.
				if (SpeedDecayPercent != 100)
					throw new YamlException("TransitionRadius and SpeedDecayPercent are two different decay laws; set one. TransitionRadius is the one that anchors the transition to a radius.");

				// Equal would divide by zero; below would make the front decelerate before it exists.
				if (TransitionRadius <= StartRadius)
					throw new YamlException("TransitionRadius must be greater than StartRadius; the front has to have somewhere to decay across.");

				if (TransitionRadius > MaxRadius)
					throw new YamlException("TransitionRadius cannot exceed MaxRadius; the wave would end while still supersonic.");
			}

			// Zero would make Pow return 1 at every radius, which collapses the fade to a flat
			// ShockwaveEndAlphaPercent and hides the mistake as "the ring just never fades".
			if (ShockwaveFadeOutExponentPercent <= 0)
				throw new YamlException("ShockwaveFadeOutExponentPercent must be positive.");

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
