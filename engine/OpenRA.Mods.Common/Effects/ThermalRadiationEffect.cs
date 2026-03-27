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
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Mods.Common.Effects
{
	/// <summary>
	/// Sustained thermal radiation effect that pulses damage at regular intervals.
	/// Units close to the center are cooked rapidly by repeated pulses;
	/// distant units receive progressively less damage per pulse.
	/// The result is that close units visibly melt away while far units barely notice.
	/// </summary>
	public class ThermalRadiationEffect : IEffect
	{
		readonly World world;
		readonly ThermalRadiationWarhead warhead;
		readonly WPos center;
		readonly Actor firedBy;
		readonly WarheadArgs args;

		int ticksRemaining;
		int intervalCounter;
		bool finished;

		public ThermalRadiationEffect(World world, ThermalRadiationWarhead warhead, WPos center, Actor firedBy, WarheadArgs args)
		{
			this.world = world;
			this.warhead = warhead;
			this.center = center;
			this.firedBy = firedBy;
			this.args = args;
			this.ticksRemaining = warhead.RadiationDuration;
			this.intervalCounter = 0;
		}

		public void Tick(World world)
		{
			if (finished)
				return;

			ticksRemaining--;
			if (ticksRemaining <= 0)
			{
				finished = true;
				world.AddFrameEndTask(w => w.Remove(this));
				return;
			}

			intervalCounter++;
			if (intervalCounter < warhead.DamageInterval)
				return;

			intervalCounter = 0;

			// Pulse damage to all actors within thermal range
			foreach (var victim in world.FindActorsOnCircle(center, warhead.MaxRange))
			{
				if (victim.IsDead || !victim.IsInWorld)
					continue;

				warhead.ApplyThermalDamage(victim, firedBy, center, args);
			}
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			return SpriteRenderable.None;
		}
	}
}
