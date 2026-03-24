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
using OpenRA.Effects;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Effects
{
	public class ShockwaveEffect : IEffect, IEffectAnnotation
	{
		readonly World world;
		readonly ShockwaveDamageWarhead warhead;
		readonly WPos center;
		readonly Actor firedBy;
		readonly WarheadArgs args;
		readonly HashSet<uint> hitActors = new HashSet<uint>();

		int delay;
		int ticks;
		bool finished;

		// WDist units expanded per tick = 1 cell (1024) / WaveSpeed
		readonly int expansionPerTick;

		public ShockwaveEffect(World world, ShockwaveDamageWarhead warhead, WPos center, Actor firedBy, WarheadArgs args)
		{
			this.world = world;
			this.warhead = warhead;
			this.center = center;
			this.firedBy = firedBy;
			this.args = args;
			this.delay = warhead.StartDelay;
			this.expansionPerTick = 1024 / warhead.WaveSpeed;
		}

		public void Tick(World world)
		{
			if (finished)
				return;

			if (delay-- > 0)
				return;

			var previousRadius = new WDist(ticks * expansionPerTick);
			ticks++;
			var currentRadius = new WDist(ticks * expansionPerTick);

			if (currentRadius > warhead.MaxRadius)
			{
				finished = true;
				world.AddFrameEndTask(w => w.Remove(this));
				return;
			}

			// Find all actors within the current radius and damage those
			// that haven't been hit yet (i.e., the wavefront just passed them)
			foreach (var victim in world.FindActorsOnCircle(center, currentRadius))
			{
				if (hitActors.Contains(victim.ActorID))
					continue;

				if (victim.IsDead || !victim.IsInWorld)
					continue;

				// Use horizontal distance for blast wave propagation (it travels along the ground)
				var offset = victim.CenterPosition - center;
				var horizontalDist = offset.HorizontalLength;

				// Skip actors beyond current wavefront
				if (horizontalDist > currentRadius.Length)
					continue;

				// Mark as hit (even if inside previous radius — they were missed, damage them now)
				hitActors.Add(victim.ActorID);

				warhead.ApplyBlastDamage(victim, firedBy, center, args);
			}
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			return SpriteRenderable.None;
		}

		public IEnumerable<IRenderable> RenderAnnotation(WorldRenderer wr)
		{
			if (finished || delay > 0 || warhead.ShockwaveColor.A == 0)
				yield break;

			var currentRadius = new WDist(ticks * expansionPerTick);
			if (currentRadius.Length <= 0)
				yield break;

			// Fade alpha as the ring expands
			var progress = (float)currentRadius.Length / warhead.MaxRadius.Length;
			var startAlpha = warhead.ShockwaveColor.A;
			var endAlpha = startAlpha * warhead.ShockwaveEndAlphaPercent / 100;
			var currentAlpha = (int)(startAlpha + (endAlpha - startAlpha) * progress);

			var color = Color.FromArgb(currentAlpha,
				warhead.ShockwaveColor.R,
				warhead.ShockwaveColor.G,
				warhead.ShockwaveColor.B);

			var borderColor = Color.FromArgb(
				currentAlpha * warhead.ShockwaveBorderColor.A / 255,
				warhead.ShockwaveBorderColor.R,
				warhead.ShockwaveBorderColor.G,
				warhead.ShockwaveBorderColor.B);

			// Use ground-level center for the ring (project blast center down to ground)
			var groundCenter = new WPos(center.X, center.Y, world.Map.CenterOfCell(world.Map.CellContaining(center)).Z);

			yield return new RangeCircleAnnotationRenderable(
				groundCenter,
				currentRadius,
				0,
				color,
				warhead.ShockwaveWidth,
				borderColor,
				warhead.ShockwaveBorderWidth);
		}
	}
}
