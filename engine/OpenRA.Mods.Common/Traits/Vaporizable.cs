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

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>How one weapon wants its victims to go. Passed in by the warhead so a yield can shape its own look.</summary>
	public readonly struct VaporizeParams
	{
		public readonly int Delay;
		public readonly int Duration;
		public readonly float Brightness;
		public readonly float WhiteoutFraction;

		public VaporizeParams(int delay, int duration, float brightness, float whiteoutFraction)
		{
			Delay = delay > 0 ? delay : 0;
			Duration = duration > 1 ? duration : 1;
			Brightness = brightness;
			WhiteoutFraction = whiteoutFraction < 0f ? 0f : whiteoutFraction > 1f ? 1f : whiteoutFraction;
		}
	}

	/// <summary>What Vaporizable wants drawn this frame.</summary>
	public readonly struct VaporizeFrame
	{
		/// <summary>Nothing to do - render the actor exactly as it would be without this trait.</summary>
		public readonly bool Unchanged;

		/// <summary>Multiply the sprite by this per channel. Above 1 lifts it towards white, keeping its shading.</summary>
		public readonly float Brightness;

		/// <summary>Replace the sprite with a flat silhouette at this alpha. Only meaningful when Silhouette.</summary>
		public readonly float Alpha;

		/// <summary>Past the whiteout point: the sprite is gone and only a fading shape is left.</summary>
		public readonly bool Silhouette;

		public VaporizeFrame(bool unchanged, float brightness, float alpha, bool silhouette)
		{
			Unchanged = unchanged;
			Brightness = brightness;
			Alpha = alpha;
			Silhouette = silhouette;
		}
	}

	// WHY A TRAIT ON THE VICTIM RATHER THAN AN EFFECT SPAWNED BY THE WARHEAD. Fading an arbitrary actor out
	// means modifying whatever renderables it happens to produce - infantry, tank, building, aircraft with a
	// shadow - and IRenderModifier is the only place that sees them. A warhead cannot: it runs in the
	// simulation tick with no WorldRenderer, and by the time anything could snapshot the actor's renderables
	// the actor would have to still exist. So the actor fades itself out and then kills itself.
	//
	// The trait sits on ^ExistsInWorld and is INERT until a VaporizeWarhead calls Begin. Idle cost is one
	// interface call and one bool test per actor per frame in Actor.Render, and no allocation - ModifyRender
	// returns the caller's own enumerable unwrapped.
	[Desc("Lets this actor be vaporised by a VaporizeWarhead: it flashes white, dissolves, and dies leaving",
		"no husk, no cook-off, no corpse and no ejected pilot. Inert until a warhead triggers it.",
		"Put it on a shared template - in WW3MOD that is ^ExistsInWorld, which reaches infantry, vehicles,",
		"aircraft, naval and every structure.")]
	public class VaporizableInfo : ConditionalTraitInfo
	{
		public override object Create(ActorInitializer init) { return new Vaporizable(this); }
	}

	public class Vaporizable : ConditionalTrait<VaporizableInfo>, IRenderModifier, ITick, IDamageModifier, ISuppressDeathRemains
	{
		VaporizeParams settings;
		Actor attacker;
		BitSet<DamageType> damageTypes;
		int age;
		bool active;

		public Vaporizable(VaporizableInfo info)
			: base(info) { }

		/// <summary>
		/// True from the instant Begin runs, NOT from the moment the actor finally dies. That is deliberate and
		/// it is what makes the effect robust: a nuclear detonation lands several warheads on the same tick, and
		/// the blast wave arrives later. Whichever of them actually kills this actor, it leaves nothing behind.
		/// </summary>
		bool ISuppressDeathRemains.SuppressDeathRemains => active;

		public bool IsVaporizing => active;

		// INVULNERABLE FOR THE LENGTH OF THE FADE, and without this the effect is usually invisible. The
		// warheads on one weapon all land on the same tick, so the nuke's own SpreadDamage would kill this
		// actor the instant after it was marked and there would be nothing left to dissolve. Holding damage off
		// for a few ticks costs nothing - the actor is already guaranteed to die when the fade ends, and
		// Health.Kill bypasses damage modifiers (ignoreModifiers: true, Health.cs:245) so this cannot make
		// anything immortal.
		//
		// It also gives the ordering rule its teeth in a legible way: declare the vaporize warhead BEFORE the
		// damage warheads and you get the dissolve, declare it after and the unit is already dead so you get
		// the removal without the visual. Never a husk either way.
		int IDamageModifier.GetDamageModifier(Actor attacker, Damage damage)
		{
			return active ? 0 : 100;
		}

		/// <summary>Starts the effect. Ignored if the actor is already vaporising, so overlapping warheads do not restart it.</summary>
		public void Begin(Actor self, in VaporizeParams p, Actor firedBy, in BitSet<DamageType> types)
		{
			// NOTE the absence of an IsDead check, which is load-bearing. Warheads on one weapon fire in
			// declaration order, so a damage warhead listed before the vaporize one will already have killed
			// this actor by the time we get here. It is too late to play the fade - the actor disposes at frame
			// end - but it is NOT too late to stop the husk, because SpawnActorOnDeath defers the actual spawn
			// to RemovedFromWorld, which has not run yet. Marking a corpse is therefore worth doing, and it is
			// what makes husk suppression independent of warhead order. The other remains - cook-off, corpse
			// animation, ejected pilot - fire during Killed and cannot be recovered this way; for those the
			// vaporize warhead genuinely must be declared first.
			if (active || IsTraitDisabled || self.Disposed || !self.IsInWorld)
				return;

			active = true;
			age = 0;
			settings = p;
			attacker = firedBy;
			damageTypes = types;
		}

		// Two phases, and the split is the point. Up to WhiteoutFraction the sprite is MULTIPLIED towards white,
		// so it keeps its own shading and reads as the thing itself getting hotter. After it, the sprite is
		// replaced by a flat silhouette whose alpha runs to zero - the shape survives a moment longer than the
		// detail, which is what makes it read as vaporising rather than as a fade-out.
		// VaporizeCurveTest.cs pins both phases and both degenerate WhiteoutFractions.

		/// <summary>The whole visual, as a pure function of age so it can be unit-tested without a world.</summary>
		public static VaporizeFrame Sample(int age, in VaporizeParams p)
		{
			if (age < p.Delay)
				return new VaporizeFrame(true, 1f, 1f, false);

			var t = (age - p.Delay) / (float)p.Duration;
			if (t < 0f)
				t = 0f;
			else if (t > 1f)
				t = 1f;

			var w = p.WhiteoutFraction;
			if (t < w)
			{
				// Heating. Quadratic so the first few ticks barely change and the lift happens late - a linear
				// ramp here reads as a dimmer being turned up rather than as something catching fire.
				var k = w <= 0f ? 1f : t / w;
				return new VaporizeFrame(false, 1f + (p.Brightness - 1f) * k * k, 1f, false);
			}

			// Dissolving. Alpha falls on 1-(1-x)^2, fast at first then trailing, so the silhouette lingers faintly.
			var x = w >= 1f ? 1f : (t - w) / (1f - w);
			var fade = 1f - x * (2f - x);
			return new VaporizeFrame(false, p.Brightness, fade < 0f ? 0f : fade, true);
		}

		void ITick.Tick(Actor self)
		{
			if (!active)
				return;

			age++;
			if (age < settings.Delay + settings.Duration)
				return;

			// Already dead - Begin was called on a corpse to suppress its husk (see the note there), or
			// something else finished it mid-fade. Nothing left to kill.
			if (self.IsDead)
				return;

			// Kill rather than Dispose, so every accounting trait still runs. The debris traits are already
			// suppressed by ISuppressDeathRemains above.
			//
			// PITFALL: Health.Kill routes through InflictDamage with ignoreModifiers TRUE (Health.cs:245), so
			// this bypasses DamageMultiplier entirely. Anything the mod has made damage-immune that way - every
			// tree carries DamageMultiplier: 0 - WILL die here if a warhead lists its target type.
			self.Kill(attacker, damageTypes);
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			if (!active)
				return r;

			var frame = Sample(age, settings);
			if (frame.Unchanged)
				return r;

			return ModifiedRender(r, frame);
		}

		static IEnumerable<IRenderable> ModifiedRender(IEnumerable<IRenderable> r, VaporizeFrame frame)
		{
			foreach (var a in r)
			{
				// Decorations are health bars, pips and selection boxes. They belong to the UI, not to the
				// object, and fading them looks like a rendering bug rather than like an effect.
				if (!a.IsDecoration && a is IModifyableRenderable ma)
				{
					if (frame.Silhouette)
					{
						// Replace path: ReplaceColor makes the shader substitute the tint colour outright
						// (`if (vTint.a < 0.0) c = vec4(vTint.rgb, -vTint.a)`), giving a flat shape at this alpha.
						// Brightness rather than plain white as the silhouette colour: the shader receives
						// Alpha * Tint as the rgb, so a tint of 1 would grey off as it faded, while an
						// over-unity one stays saturated white until it is nearly gone.
						// The world tint is deliberately NOT ignored - under a light event the silhouette is
						// multiplied by the flash's own colour, so it blows out white for free while the flash
						// is on and cools with it.
						var white = new float3(frame.Brightness, frame.Brightness, frame.Brightness);
						yield return ma
							.WithTint(white, ma.TintModifiers | TintModifiers.ReplaceColor)
							.WithAlpha(frame.Alpha);
					}
					else
					{
						// Multiply path: TintModifiers left alone, so the shader does `c *= vTint` and the
						// sprite keeps its own shading while lifting towards white.
						yield return ma.WithTint(frame.Brightness * ma.Tint, ma.TintModifiers);
					}
				}
				else
					yield return a;
			}
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}

		protected override void TraitDisabled(Actor self)
		{
			// Losing the trait mid-fade would leave the actor permanently half-white, because nothing else
			// would ever clear the modifier. Cancel instead.
			active = false;
		}
	}
}
