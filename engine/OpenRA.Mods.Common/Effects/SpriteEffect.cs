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
using System.Collections.Generic;
using OpenRA.Effects;
using OpenRA.Graphics;

namespace OpenRA.Mods.Common.Effects
{
	public class SpriteEffect : IEffect, IEffectAboveFog
	{
		readonly World world;
		readonly string palette;
		readonly Animation anim;
		readonly Func<WPos> posFunc;
		readonly bool visibleThroughFog;

		// WW3MOD: when set, the sprite is drawn after the fog layers instead of before them, so it
		// keeps full brightness over explored-but-unobserved ground. See IEffectAboveFog.
		readonly bool renderAboveFog;
		readonly string sequence;
		WPos pos;
		int delay;
		readonly float scale;
		readonly int zOffset;
		bool initialized;

		// WW3MOD: how long the sprite takes to play, as a percentage of the sequence's natural length.
		// 100 is unchanged; 256 plays the same frames over 2.56x as many ticks. See Tick.
		readonly int durationScalePercent;

		// Milliseconds x percent banked from the last frame advance, so the fractional part of the
		// per-tick spend is carried rather than rounded away. See Tick.
		int animMsAccumulator;

		// What Animation.Tick() spends per call, independent of the mod's Timestep. Mirrored here
		// because the scaled path has to divide it; see Animation.cs and conventions.md
		// "Sequence Tick is milliseconds against a FIXED 40 ms clock".
		const int AnimationMillisecondsPerTick = 40;

		// Facing is last on these overloads partially for backwards compatibility with previous main ctor revision
		// and partially because most effects don't need it. The latter is also the reason for placement of 'delay'.
		public SpriteEffect(WPos pos, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false,
			int durationScalePercent = 100)
			: this(() => pos, () => WAngle.Zero, world, image, sequence, palette, visibleThroughFog, delay, scale, zOffset, renderAboveFog, durationScalePercent) { }

		public SpriteEffect(Actor actor, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false,
			int durationScalePercent = 100)
			: this(() => actor.CenterPosition, () => WAngle.Zero, world, image, sequence, palette, visibleThroughFog, delay, scale, zOffset, renderAboveFog, durationScalePercent) { }

		public SpriteEffect(WPos pos, WAngle facing, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false,
			int durationScalePercent = 100)
			: this(() => pos, () => facing, world, image, sequence, palette, visibleThroughFog, delay, scale, zOffset, renderAboveFog, durationScalePercent) { }

		public SpriteEffect(Func<WPos> posFunc, Func<WAngle> facingFunc, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false,
			int durationScalePercent = 100)
		{
			this.world = world;
			this.posFunc = posFunc;
			this.palette = palette;
			this.sequence = sequence;
			this.visibleThroughFog = visibleThroughFog;
			this.renderAboveFog = renderAboveFog;
			this.delay = delay;
			this.scale = scale;
			this.zOffset = zOffset;

			// Zero would divide by zero below and a negative would run the animation backwards through
			// the accumulator; neither is a meaning anyone wants from a percentage.
			this.durationScalePercent = Math.Max(1, durationScalePercent);
			pos = posFunc();
			anim = new Animation(world, image, facingFunc);
		}

		public void Tick(World world)
		{
			if (delay-- > 0)
				return;

			if (!initialized)
			{
				anim.PlayThen(sequence, () => world.AddFrameEndTask(w => w.Remove(this)));
				initialized = true;
			}
			else
			{
				// WW3MOD: DurationScalePercent stretches the sprite in TIME the way ScalePercent
				// stretches it in space. Animation.Tick(t) spends t milliseconds of the sequence's own
				// Tick/ChangeTick budget, so spending FEWER milliseconds per game tick makes the same
				// frames take proportionally LONGER to play -- hence the reciprocal rather than a
				// multiply. The spend is banked in a milliseconds x percent accumulator instead of
				// being divided per tick, so the fractional part is carried and the total elapsed
				// budget after N ticks is exactly floor(N * 4000 / percent) rather than N * round(...).
				//
				// At 100 this reduces to anim.Tick(40) on every tick with the accumulator always
				// landing back on zero -- the same call the parameterless anim.Tick() makes, whose
				// paused check is vacuous here because SpriteEffect never supplies a paused func.
				animMsAccumulator += AnimationMillisecondsPerTick * 100;
				var step = animMsAccumulator / durationScalePercent;
				animMsAccumulator -= step * durationScalePercent;
				anim.Tick(step);

				pos = posFunc();
			}
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			// WW3MOD: an above-fog effect renders from RenderAboveFog instead, never from both --
			// drawing it twice would double-composite its own alpha and read as a brightness jump.
			if (renderAboveFog)
				return SpriteRenderable.None;

			return RenderInner(wr);
		}

		IEnumerable<IRenderable> IEffectAboveFog.RenderAboveFog(WorldRenderer wr)
		{
			if (!renderAboveFog)
				return SpriteRenderable.None;

			return RenderInner(wr);
		}

		IEnumerable<IRenderable> RenderInner(WorldRenderer wr)
		{
			if (!initialized || (!visibleThroughFog && world.FogObscures(pos)))
				return SpriteRenderable.None;

			return anim.Render(pos, WVec.Zero, zOffset, wr.Palette(palette), scale);
		}
	}
}
