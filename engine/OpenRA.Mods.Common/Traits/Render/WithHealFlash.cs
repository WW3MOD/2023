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

using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Flashes the actor with a color overlay when receiving healing.")]
	public class WithHealFlashInfo : TraitInfo
	{
		[Desc("Color of the flash overlay.")]
		public readonly Color Color = Color.White;

		[Desc("Alpha of the flash overlay (0.0 to 1.0).")]
		public readonly float Alpha = 0.3f;

		[Desc("Number of flash pulses per heal event.")]
		public readonly int Count = 2;

		[Desc("Ticks between flash pulses.")]
		public readonly int Interval = 2;

		[Desc("Minimum ticks between flash triggers to prevent spam from multiple healers.")]
		public readonly int Cooldown = 25;

		public override object Create(ActorInitializer init) { return new WithHealFlash(this); }
	}

	public class WithHealFlash : INotifyDamage, ITick
	{
		readonly WithHealFlashInfo info;
		int cooldownRemaining;

		public WithHealFlash(WithHealFlashInfo info)
		{
			this.info = info;
		}

		void ITick.Tick(Actor self)
		{
			if (cooldownRemaining > 0)
				cooldownRemaining--;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (e.Damage.Value >= 0 || cooldownRemaining > 0)
				return;

			cooldownRemaining = info.Cooldown;
			self.World.AddFrameEndTask(w => w.Add(
				new FlashTarget(self, info.Color, info.Alpha, info.Count, info.Interval)));
		}
	}
}
