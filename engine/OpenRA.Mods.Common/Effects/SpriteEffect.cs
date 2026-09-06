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

		// Facing is last on these overloads partially for backwards compatibility with previous main ctor revision
		// and partially because most effects don't need it. The latter is also the reason for placement of 'delay'.
		public SpriteEffect(WPos pos, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false)
			: this(() => pos, () => WAngle.Zero, world, image, sequence, palette, visibleThroughFog, delay, scale, zOffset, renderAboveFog) { }

		public SpriteEffect(Actor actor, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false)
			: this(() => actor.CenterPosition, () => WAngle.Zero, world, image, sequence, palette, visibleThroughFog, delay, scale, zOffset, renderAboveFog) { }

		public SpriteEffect(WPos pos, WAngle facing, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false)
			: this(() => pos, () => facing, world, image, sequence, palette, visibleThroughFog, delay, scale, zOffset, renderAboveFog) { }

		public SpriteEffect(Func<WPos> posFunc, Func<WAngle> facingFunc, World world, string image, string sequence, string palette,
			bool visibleThroughFog = false, int delay = 0, float scale = 1f, int zOffset = 0, bool renderAboveFog = false)
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
				anim.Tick();
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
