#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Graphics;

namespace OpenRA.Traits
{
	// WW3MOD: the whole screen-shake model below replaces upstream's two-fixed-sine implementation.
	//
	// WHAT UPSTREAM DID, and why all three parts of it read as "annoying":
	//   offset    = multiplier * intensity * (sin(2*pi*t/4), cos(2*pi*t/5))
	//   intensity = min(10, 100 * 1024 * 1024 * SUM(e.Intensity / distanceSquared))
	//   1. Periods of exactly 4 and 5 TICKS. At Timestep 60 (16.67 ticks/s, NOT 25) that is a 4.2 Hz
	//      mechanical buzz with a pattern that repeats every 20 ticks. Nothing in nature does this.
	//   2. NO ENVELOPE. Amplitude is flat for the whole duration and then stops dead on the expiry
	//      tick. Real ground motion has a fast onset and a long decaying coda.
	//   3. Falloff is INVERSE SQUARE of camera distance, then hard-clamped to 10 px. Inverse square
	//      is so steep that the clamp saturates near the epicentre and the shake is near-zero a few
	//      cells out, so scrolling reads as an on/off switch rather than a fade.
	//
	// WHAT THIS DOES INSTEAD. Every number is a YAML lever, on `World: ScreenShaker:` for the global
	// model or on the ShakeScreen warhead / ShakeOnDeath trait per event. See ScreenShakerInfo.
	//
	// PITFALL: `Intensity` HAS A NEW MEANING. It is now the peak screen displacement in PIXELS at
	// the epicentre, not an inverse-square numerator. Old values do not carry over -- the tactical
	// nuke's `Intensity: 80` would be 80 px of camera throw. All ww3mod call sites were retuned in
	// the same commit; a new one should think in pixels (2 = a shell landing nearby, 8 = a building
	// collapsing next to you, 14-20 = a nuclear detonation on top of you).
	//
	// The maths lives in ScreenShakeModel, deliberately separated from the trait so it can be
	// sampled without a World or a WorldRenderer -- see ScreenShakeModelTest, which tabulates the
	// shipped profiles from this exact code rather than from a reimplementation of it.
	//
	// NOT EVERYTHING IS IN THE MODEL, and assuming it is has already cost a review. The trait keeps
	// two pieces of state the model cannot see: ShakeEffect.ExpiryTime, built in AddEffect, and the
	// sub-pixel residual the quantiser carries between ticks. ScreenShakeModelTest cannot reach
	// either; ScreenShakerTest drives a real ScreenShaker for them.
	[TraitLocation(SystemActors.World)]
	public class ScreenShakerInfo : TraitInfo
	{
		// ---- Global output limits ---------------------------------------------------------------
		[Desc("Ceiling on total screen displacement in pixels, summed over every live effect.")]
		public readonly float MaxAmplitude = 22f;

		[Desc("Approach MaxAmplitude through tanh instead of clipping at it. A hard clip flat-tops",
			"the waveform, which is exactly the mechanical character this model exists to avoid; the",
			"soft knee is transparent well below the ceiling and only compresses once effects stack.")]
		public readonly bool SoftClamp = true;

		[Desc("Per-axis clamp applied to each effect's own Multiplier.")]
		public readonly float2 MinMultiplier = new(-3, -3);

		[Desc("Per-axis clamp applied to each effect's own Multiplier.")]
		public readonly float2 MaxMultiplier = new(3, 3);

		[Desc("Vertical displacement as a fraction of horizontal. Rayleigh surface waves are",
			"elliptical rather than circular; 1.0 gives an isotropic wobble, lower values give the",
			"predominantly side-to-side motion a ground observer actually experiences.")]
		public readonly float VerticalFactor = 0.62f;

		// ---- Waveform ---------------------------------------------------------------------------
		[Desc("Frequency ratios of the components summed to make the waveform, relative to the",
			"event's fundamental. These MUST be mutually irrational or the sum acquires a repeat",
			"period and reads as a buzz again -- the defaults are sqrt(1,2,3,5).")]
		public readonly float[] FrequencyRatios = { 1f, 1.41421f, 1.73205f, 2.23607f };

		[Desc("Relative amplitude weight of each component, matched by index to FrequencyRatios.",
			"Falling with frequency approximates the 1/f spectrum of real ground motion. Do not make",
			"the rolloff much steeper than this: the fundamental then holds so much of the energy",
			"that the composite is effectively a single tone again, which is the failure mode the",
			"whole comb exists to avoid. At these values it holds about 53% and the fixture asserts",
			"a floor on what is left over.")]
		public readonly float[] FrequencyWeights = { 100f, 72f, 50f, 34f };

		[Desc("Fundamental frequency in CYCLES PER TICK for an event of ReferenceIntensity.",
			"0.115 c/tick at Timestep 60 is 1.9 Hz.")]
		public readonly float ReferenceFrequency = 0.115f;

		[Desc("The Intensity at which ReferenceFrequency applies.")]
		public readonly float ReferenceIntensity = 6f;

		[Desc("Exponent k in f = ReferenceFrequency * (ReferenceIntensity / Intensity)^k.",
			"BIGGER EVENTS SHAKE SLOWER: corner frequency is inversely proportional to source",
			"dimension (Brune 1970), and Intensity stands in for source dimension here. k = 0.5",
			"rather than a literal 1.0 because the renderable band is only about 1-7 Hz at 16.67",
			"ticks/s; a steeper exponent pushes small events into the Nyquist cap, where every event",
			"would collapse back onto the same frequency and the lever would stop doing anything.")]
		public readonly float FrequencyIntensityExponent = 0.5f;

		[Desc("Floor on the fundamental, cycles per tick.")]
		public readonly float MinFrequency = 0.02f;

		[Desc("Ceiling on the HIGHEST component, cycles per tick. Nyquist is 0.5 at any tick rate;",
			"0.4 keeps the top component off the fold-back point. The fundamental is capped at",
			"MaxFrequency / max(FrequencyRatios) so the whole comb stays under it together -- capping",
			"per component instead would clamp several components of a small event to the SAME value",
			"and degenerate the sum back into a single sine.")]
		public readonly float MaxFrequency = 0.4f;

		[Desc("Per-event frequency detune, percent. Stops two detonations of the same weapon from",
			"producing an identical waveform. Deterministic hash, never an RNG.")]
		public readonly int FrequencyJitterPercent = 6;

		[Desc("Distance over which the fundamental halves, modelling the preferential absorption of",
			"high frequencies with range: f = f / (1 + r / this). A distant heavy blast should be a",
			"low roll even where a near one of the same size is a sharper shock. Zero disables.")]
		public readonly WDist HighFrequencyRolloffDistance = new(51200);

		// ---- Distance falloff ---------------------------------------------------------------------
		[Desc("Inside this radius there is no geometric falloff, and it is also the divisor that",
			"normalises the falloff curve. Prevents the 1/0 singularity upstream relied on its clamp",
			"to hide.")]
		public readonly WDist ReferenceDistance = new(4096);

		[Desc("Geometric spreading exponent: amplitude scales as (ReferenceDistance / r)^this.",
			"0.5 is the textbook value for surface waves spreading over a 2D wavefront, and is far",
			"gentler than the effective 2.0 upstream used -- which is what made shake feel like a",
			"switch that flipped as you scrolled rather than something that faded.")]
		public readonly float GeometricSpreadingExponent = 0.5f;

		[Desc("Anelastic attenuation: an additional exp(-r / this) on top of geometric spreading.",
			"This is what gives the shake a horizon instead of an abrupt cutoff.")]
		public readonly WDist AttenuationDistance = new(46080);

		// ---- Propagation ----------------------------------------------------------------------------
		[Desc("Ticks the ground wave takes to travel one cell. DERIVATION, all of it provisional on",
			"the 160 m/cell scale the mod is being calibrated to (Abrams TankRound Range 25c0 against",
			"a ~4 km real effective range):",
			"    Timestep 60 ms           ->  1 tick = 0.06 s",
			"    air blast, 340 m/s       ->  20.4 m/tick  ->  160 / 20.4 = 7.84 ticks/cell",
			"      (which is why the nuke's own BlastWave WaveSpeed is 7 -- same physics, same answer)",
			"    Rayleigh wave, 3.0 km/s  ->  180 m/tick   ->  160 / 180  = 0.89 ticks/cell",
			"So the ground arrives about 8.7x ahead of the pressure wave, and 0.9 is that number.",
			"THIS IS THE LEVER THAT MAKES A DISTANT BLAST FEEL DISTANT: the shake lands first and the",
			"shockwave follows seconds later, instead of everything happening on the same tick.")]
		public readonly float PropagationTicksPerCell = 0.9f;

		[Desc("DEFAULT ceiling on the propagation delay, ticks, for events that do not set their own.",
			"It also decides how long past its Duration an effect is kept alive waiting for far-away",
			"cameras, and THOSE TWO ROLES ARE WHY THIS IS NOT THE LEVER TO RAISE. An event whose",
			"wavefront is slower than 300 ticks wide -- AtomicHighYield's air blast needs 645, and a",
			"50 Mt front runs 1682 -- saturates here and lands at a flat 18 s however far away the",
			"camera is. Raising this global number to cover the worst case would keep EVERY shake",
			"event in the mod alive for the same extra span, so the ceiling is a per-event property:",
			"set ShakeParams.MaxPropagationDelay on the one stage that needs it and everything else",
			"keeps this.")]
		public readonly int MaxPropagationDelay = 300;

		public override object Create(ActorInitializer init) { return new ScreenShaker(this); }
	}

	/// <summary>Per-event shake parameters, shared by ShakeScreenWarhead and ShakeOnDeath.</summary>
	public struct ShakeParams
	{
		/// <summary>Total lifetime in ticks, measured from the moment the wave ARRIVES at the camera.</summary>
		public int Duration;

		/// <summary>Peak screen displacement in pixels at the epicentre. See the PITFALL above.</summary>
		public int Intensity;

		/// <summary>Per-axis scale on this effect's own displacement.</summary>
		public float2 Multiplier;

		/// <summary>Ticks to ramp from silence to full amplitude.</summary>
		public int AttackTicks;

		/// <summary>Ticks for the amplitude to halve. 0 derives Duration / 4.</summary>
		public int DecayHalfLife;

		/// <summary>Ticks of linear taper at the end, so the effect never stops dead. 0 derives one.</summary>
		public int ReleaseTicks;

		/// <summary>Percent scale on the frequency this event's Intensity would otherwise imply.</summary>
		public int FrequencyScale;

		/// <summary>Ticks per cell for THIS event. 0 inherits ScreenShakerInfo.PropagationTicksPerCell.</summary>
		public float PropagationTicksPerCell;

		/// <summary>
		/// <para>Ceiling on THIS event's arrival delay, ticks. 0 inherits
		/// ScreenShakerInfo.MaxPropagationDelay.</para>
		///
		/// <para>Pair it with <see cref="PropagationTicksPerCell"/>: a stage that tracks a slow front
		/// needs a ceiling wide enough to cover the front's whole travel, and the global default is
		/// sized for the 0.9 t/cell ground wave. It is also what the effect's ExpiryTime is built
		/// from, so raising it here lengthens only this event's lifetime.</para>
		/// </summary>
		public int MaxPropagationDelay;

		/// <summary>Attenuation e-folding distance for THIS event. 0 inherits the global one.</summary>
		public WDist AttenuationDistance;

		public static ShakeParams Default(int duration, int intensity)
		{
			return new ShakeParams
			{
				Duration = duration,
				Intensity = intensity,
				Multiplier = new float2(1, 1),
				AttackTicks = 2,
				DecayHalfLife = 0,
				ReleaseTicks = 0,
				FrequencyScale = 100,
				PropagationTicksPerCell = 0f,
				MaxPropagationDelay = 0,
				AttenuationDistance = WDist.Zero
			};
		}
	}

	public struct ShakeEffect
	{
		public int SpawnTime;
		public int ExpiryTime;
		public WPos Position;
		public ShakeParams Params;

		/// <summary>
		/// <para>Deterministic per-effect, per-component phase source.</para>
		///
		/// <para>PITFALL: do NOT reach for World.SharedRandom here. Screen shake is client-local view
		/// state, but the shared RNG is not -- drawing from it advances a stream every client must
		/// agree on, so seeding a purely cosmetic waveform from it would desync the match. This is a
		/// pure function of the effect's own position and spawn tick: no state is read, none is
		/// written, and two clients that somehow disagreed about the answer would still only
		/// disagree about where their own camera is.</para>
		/// </summary>
		public uint Hash(int component, int axis)
		{
			unchecked
			{
				var h = 2166136261u;
				h = (h ^ (uint)Position.X) * 16777619u;
				h = (h ^ (uint)Position.Y) * 16777619u;
				h = (h ^ (uint)Position.Z) * 16777619u;
				h = (h ^ (uint)SpawnTime) * 16777619u;
				h = (h ^ (uint)component) * 16777619u;
				h = (h ^ (uint)axis) * 16777619u;

				h ^= h >> 15;
				h *= 2246822519u;
				h ^= h >> 13;
				h *= 3266489917u;
				h ^= h >> 16;
				return h;
			}
		}
	}

	/// <summary>
	/// The shake maths, with no dependency on World, WorldRenderer or Viewport. Everything here is a
	/// pure function of an effect, a tick and a distance, which is what lets the unit tests tabulate
	/// the shipped profiles from the code that actually runs instead of from a copy of it.
	/// </summary>
	public class ScreenShakeModel
	{
		const float TwoPi = (float)(2 * Math.PI);

		public readonly ScreenShakerInfo Info;

		readonly float weightSum;
		readonly float maxRatio;
		readonly int components;

		public ScreenShakeModel(ScreenShakerInfo info)
		{
			Info = info;

			weightSum = 0f;
			foreach (var w in info.FrequencyWeights)
				weightSum += Math.Abs(w);

			if (weightSum <= 0f)
				weightSum = 1f;

			maxRatio = 1f;
			foreach (var r in info.FrequencyRatios)
				maxRatio = Math.Max(maxRatio, Math.Abs(r));

			components = Math.Min(info.FrequencyRatios.Length, info.FrequencyWeights.Length);
		}

		/// <summary>
		/// <para>Ceiling on this event's arrival delay, ticks: its own if it set one, else the global
		/// default.</para>
		///
		/// <para>The one number two callers must agree on. <see cref="ArrivalDelay"/> stops the wave
		/// arriving later than this, and ScreenShaker.AddEffect keeps the effect alive exactly this
		/// long past its Duration; if the second were smaller than the first, an effect would be
		/// culled before its own wave reached the furthest camera still entitled to it.</para>
		/// </summary>
		public int MaxDelay(in ShakeParams p)
		{
			return p.MaxPropagationDelay > 0 ? p.MaxPropagationDelay : Info.MaxPropagationDelay;
		}

		/// <summary>Ticks before this event's wave, <paramref name="distance"/> away, reaches the camera.</summary>
		public int ArrivalDelay(in ShakeParams p, int distance)
		{
			var speed = p.PropagationTicksPerCell > 0f ? p.PropagationTicksPerCell : Info.PropagationTicksPerCell;
			var delay = (int)(speed * distance / 1024f);
			var cap = MaxDelay(p);
			return delay > cap ? cap : delay;
		}

		/// <summary>Amplitude envelope in [0,1] at <paramref name="t"/> ticks after arrival.</summary>
		public static float Envelope(in ShakeParams p, int t)
		{
			if (t < 0 || t >= p.Duration)
				return 0f;

			var halfLife = p.DecayHalfLife > 0 ? p.DecayHalfLife : Math.Max(1, p.Duration / 4);
			var release = p.ReleaseTicks > 0 ? p.ReleaseTicks : Math.Max(1, Math.Min(20, p.Duration / 3));
			var attack = Math.Max(1, p.AttackTicks);

			var rise = Math.Min(1f, (t + 1f) / attack);
			var decay = (float)Math.Pow(0.5, (double)t / halfLife);
			var tail = Math.Min(1f, (p.Duration - t) / (float)release);

			return rise * decay * tail;
		}

		/// <summary>Distance attenuation in [0,1]: geometric spreading times anelastic absorption.</summary>
		public float Falloff(in ShakeParams p, int distance)
		{
			var refDist = Math.Max(1, Info.ReferenceDistance.Length);
			var geometric = distance <= refDist
				? 1f
				: (float)Math.Pow((double)refDist / distance, Info.GeometricSpreadingExponent);

			var attenDist = p.AttenuationDistance.Length > 0
				? p.AttenuationDistance.Length
				: Info.AttenuationDistance.Length;

			var atten = 1f;
			if (attenDist > 0 && distance > refDist)
				atten = (float)Math.Exp(-(double)(distance - refDist) / attenDist);

			return geometric * atten;
		}

		/// <summary>Fundamental frequency in cycles per tick for this event, seen from this distance.</summary>
		public float Fundamental(in ShakeParams p, int distance, uint jitterHash)
		{
			var i = Math.Max(1f, Math.Abs(p.Intensity));
			var f = Info.ReferenceFrequency * (float)Math.Pow(
				Info.ReferenceIntensity / i, Info.FrequencyIntensityExponent);

			f *= p.FrequencyScale / 100f;

			if (Info.HighFrequencyRolloffDistance.Length > 0)
				f /= 1f + ((float)distance / Info.HighFrequencyRolloffDistance.Length);

			if (Info.FrequencyJitterPercent != 0)
				f *= 1f + (Info.FrequencyJitterPercent / 100f * ((2f * Unit(jitterHash)) - 1f));

			// Cap the FUNDAMENTAL by the top ratio so the whole comb clears Nyquist together.
			return f.Clamp(Info.MinFrequency, Info.MaxFrequency / maxRatio);
		}

		/// <summary>Peak amplitude in pixels this effect can reach at this distance and time.</summary>
		public float Amplitude(in ShakeEffect e, int t, int distance)
		{
			return Math.Abs(e.Params.Intensity) * Envelope(e.Params, t) * Falloff(e.Params, distance);
		}

		/// <summary>
		/// One effect's contribution in screen pixels, at absolute tick <paramref name="ticks"/> and
		/// world distance <paramref name="distance"/> from the camera.
		/// </summary>
		public float2 Sample(in ShakeEffect e, int ticks, int distance)
		{
			var t = ticks - e.SpawnTime - ArrivalDelay(e.Params, distance);
			if (t < 0 || t >= e.Params.Duration)
				return float2.Zero;

			var amp = Amplitude(e, t, distance);
			if (amp <= 0.001f)
				return float2.Zero;

			var f0 = Fundamental(e.Params, distance, e.Hash(31, 2));

			// Weighted sum of mutually-irrational sinusoids with per-event, per-axis phases.
			var x = 0f;
			var y = 0f;
			for (var k = 0; k < components; k++)
			{
				var w = Info.FrequencyWeights[k];
				var f = f0 * Info.FrequencyRatios[k];
				x += w * (float)Math.Sin((TwoPi * f * t) + (TwoPi * Unit(e.Hash(k, 0))));
				y += w * (float)Math.Sin((TwoPi * f * t) + (TwoPi * Unit(e.Hash(k, 1))));
			}

			x = amp * x / weightSum;
			y = amp * y / weightSum * Info.VerticalFactor;

			var m = e.Params.Multiplier.Constrain(Info.MinMultiplier, Info.MaxMultiplier);
			return new float2(x * m.X, y * m.Y);
		}

		/// <summary>Applies the stacking ceiling to a summed displacement.</summary>
		public float2 ClampTotal(float2 sum)
		{
			if (Info.MaxAmplitude <= 0f)
				return sum;

			if (Info.SoftClamp)
				return new float2(
					Info.MaxAmplitude * (float)Math.Tanh(sum.X / Info.MaxAmplitude),
					Info.MaxAmplitude * (float)Math.Tanh(sum.Y / Info.MaxAmplitude));

			return sum.Constrain(
				new float2(-Info.MaxAmplitude, -Info.MaxAmplitude),
				new float2(Info.MaxAmplitude, Info.MaxAmplitude));
		}

		static float Unit(uint h) { return h / 4294967296f; }
	}

	public class ScreenShaker : ITick, IWorldLoaded
	{
		readonly ScreenShakerInfo info;
		readonly ScreenShakeModel model;
		readonly List<ShakeEffect> shakeEffects = new();

		WorldRenderer worldRenderer;
		int ticks = 0;

		// The offset we have pushed into Viewport.CenterLocation and have not yet taken back.
		//
		// PITFALL: Viewport.Scroll is CUMULATIVE (`CenterLocation += delta`, Viewport.cs), so an
		// arbitrary waveform applied tick-by-tick random-walks the camera away from wherever the
		// player left it. Upstream got away with raw sines only because periods of 4 and 5 ticks sum
		// to zero over their own period -- that, and not the feel, is why those two numbers were
		// chosen, and it is a constraint no broadband waveform can satisfy. So rather than emit a
		// delta per tick we track the ABSOLUTE offset and scroll by the difference; when the last
		// effect dies the target is zero and the camera lands back on the exact pixel it started on.
		int2 appliedOffset;

		// The fraction of a world-pixel the last quantisation had to throw away, carried into the
		// next one.
		//
		// PITFALL: Viewport.CenterLocation is an INTEGER in world-px, and the renderer multiplies it
		// by Zoom to reach the screen -- so one step of the only quantity we can move is Zoom SCREEN
		// pixels, and at MaxZoom (4, Viewport.cs) the camera physically cannot move less than four
		// of them. Rounding each tick independently and discarding the remainder therefore does two
		// separate kinds of damage at high zoom:
		//   * a 3 px waveform becomes round(0.75 * sin) -- a three-state square wave that sits at
		//     +-4 screen px or at nothing, which is a staircase where the model computed a sine;
		//   * anything whose peak is under half a world-px (2 px on screen at zoom 4 -- an ordinary
		//     shell, or the tail of any event once its envelope has decayed) rounds to zero on EVERY
		//     tick and vanishes outright.
		// Carrying the remainder makes the quantiser a first-order error-feedback loop: the applied
		// value is still a whole world-px, but the accumulated difference between what was asked for
		// and what was applied never exceeds half a pixel, so the camera's running mean traces the
		// float waveform instead of a staircase of it. Amplitude and frequency survive at any zoom.
		//
		// It carries no state across events: GetTargetOffset clears it the moment the last effect
		// dies, which is what keeps "the camera lands back on the exact pixel it started on" exact.
		float2 residual;

		public ScreenShaker(ScreenShakerInfo info)
		{
			this.info = info;
			model = new ScreenShakeModel(info);
		}

		/// <summary>Effects still alive this tick. A view for the fixture; the structs are copies.</summary>
		public IReadOnlyList<ShakeEffect> Effects => shakeEffects;

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr) { worldRenderer = wr; }

		void ITick.Tick(Actor self)
		{
			if (shakeEffects.Count > 0 || appliedOffset != int2.Zero)
			{
				var target = GetTargetOffset();
				if (target != appliedOffset)
				{
					worldRenderer.Viewport.ScrollPx(target - appliedOffset);
					appliedOffset = target;
				}

				shakeEffects.RemoveAll(t => t.ExpiryTime <= ticks);
			}

			ticks++;
		}

		public void AddEffect(int time, WPos position, int intensity)
		{
			AddEffect(position, ShakeParams.Default(time, intensity));
		}

		public void AddEffect(int time, WPos position, int intensity, float2 multiplier)
		{
			var p = ShakeParams.Default(time, intensity);
			p.Multiplier = multiplier;
			AddEffect(position, p);
		}

		public void AddEffect(WPos position, ShakeParams p)
		{
			if (p.Duration <= 0 || p.Intensity == 0)
				return;

			shakeEffects.Add(new ShakeEffect
			{
				SpawnTime = ticks,
				ExpiryTime = ticks + p.Duration + model.MaxDelay(p),
				Position = position,
				Params = p
			});
		}

		/// <summary>
		/// Converts a displacement in SCREEN pixels into the whole-world-pixel offset that
		/// Viewport.CenterLocation can actually hold, carrying the discarded fraction into the next
		/// call. See the note on <see cref="residual"/> for why the fraction cannot be dropped.
		/// Public because it is stateful and the fixture drives the shipping instance of it.
		/// </summary>
		public int2 Quantize(float2 screenPx, float zoom)
		{
			if (zoom <= 0f)
				zoom = 1f;

			var wantedX = (screenPx.X / zoom) + residual.X;
			var wantedY = (screenPx.Y / zoom) + residual.Y;

			var applied = new int2((int)Math.Round(wantedX), (int)Math.Round(wantedY));
			residual = new float2(wantedX - applied.X, wantedY - applied.Y);

			return applied;
		}

		/// <summary>Absolute offset, in Viewport.CenterLocation units, wanted this tick.</summary>
		int2 GetTargetOffset()
		{
			if (shakeEffects.Count == 0)
			{
				residual = float2.Zero;
				return int2.Zero;
			}

			// Measure distance from where the camera WOULD be if we were not shaking it. Reading the
			// live centre instead feeds our own displacement back into the falloff every tick.
			var cp = worldRenderer.ProjectedPosition(worldRenderer.Viewport.CenterLocation - appliedOffset);

			var sum = float2.Zero;
			foreach (var e in shakeEffects)
				sum += model.Sample(e, ticks, (e.Position - cp).Length);

			sum = model.ClampTotal(sum);

			// `sum` is in screen pixels. Viewport.CenterLocation is in world-px, which the renderer
			// multiplies by Zoom to reach the screen -- so divide it out, and the shake keeps the
			// same apparent size however far the player is zoomed in.
			return Quantize(sum, worldRenderer.Viewport.Zoom);
		}
	}
}
