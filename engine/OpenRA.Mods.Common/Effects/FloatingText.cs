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
using OpenRA.Effects;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Effects
{
	public class FloatingText : IEffect, IEffectAnnotation
	{
		/// <summary>Default rise per tick, in world units. Public so a caller that lengthens `duration` can scale
		/// this down by the same factor and leave the total drift unchanged.</summary>
		public const int DefaultRiseRate = 86;

		readonly SpriteFont font;
		readonly string text;
		readonly Color color;
		readonly WVec velocity;
		readonly bool ignoreVisibility;
		int remaining;
		WPos pos;

		public FloatingText(WPos pos, Color color, string text, int duration)
			: this(pos, color, text, duration, DefaultRiseRate, false) { }

		/// <param name="riseRate">World units risen per tick. Lower means the same drift takes longer.</param>
		/// <param name="ignoreVisibility">Draw even where the render player currently has no vision. Correct only
		/// for text reporting something the player already knows they did: the evacuation refund is one, because the
		/// actor holding the fog open at that spot is disposed in the same frame the text is added, so the fog would
		/// otherwise close over the tick reporting its own refund.</param>
		public FloatingText(WPos pos, Color color, string text, int duration, int riseRate, bool ignoreVisibility)
		{
			font = Game.Renderer.Fonts["TinyBold"];
			this.pos = pos;
			this.color = color;
			this.text = text;
			this.ignoreVisibility = ignoreVisibility;
			velocity = new WVec(0, 0, riseRate);
			remaining = duration;
		}

		void IEffect.Tick(World world)
		{
			if (--remaining <= 0)
				world.AddFrameEndTask(w => w.Remove(this));

			pos += velocity;
		}

		IEnumerable<IRenderable> IEffect.Render(WorldRenderer wr) { return SpriteRenderable.None; }

		IEnumerable<IRenderable> IEffectAnnotation.RenderAnnotation(WorldRenderer wr)
		{
			if (!ignoreVisibility && (wr.World.FogObscures(pos) || wr.World.ShroudObscures(pos)))
				yield break;

			yield return new TextAnnotationRenderable(font, pos, 0, color, text);
		}

		public static string FormatCashTick(int cashAmount)
		{
			return $"{(cashAmount < 0 ? "-" : "+")}${Math.Abs(cashAmount)}";
		}
	}
}
