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

		[Desc("Alpha of the flash overlay (0.0 to 1.0). Ignored when Brightness is set.")]
		public readonly float Alpha = 0.3f;

		[Desc("If greater than zero, MULTIPLY the sprite by Color scaled to this factor instead of",
			"replacing it with a flat silhouette. 1.0 is no change; above 1.0 brightens toward white,",
			"the highest channel saturating first. The sprite keeps its shading and its alpha, so the",
			"actor stays fully readable underneath the flash.",
			"WHY THIS EXISTS: the default path is FlashTarget's Color+Alpha constructor, which sets",
			"TintModifiers.ReplaceColor — every pixel becomes one flat colour at that alpha, so the actor",
			"is not tinted but REPLACED by a half-transparent silhouette. At a saturated colour that reads",
			"as a flash; at a light one it reads as the unit fading out or dying, which for a heal is the",
			"opposite of the intended message. Defaults to 0 so existing users are unchanged.")]
		public readonly float Brightness = 0f;

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

		float3 Tint => info.Brightness * (new float3(info.Color.R, info.Color.G, info.Color.B) / 255f);

		void ITick.Tick(Actor self)
		{
			if (cooldownRemaining > 0)
				cooldownRemaining--;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (!HealEvent.IsHealing(e) || cooldownRemaining > 0)
				return;

			cooldownRemaining = info.Cooldown;

			// Two different FlashTarget constructors, not two colours: the tint one leaves TintModifiers
			// at None so the shader multiplies the sprite (combined.frag `c *= vTint`), while the
			// Color+Alpha one sets ReplaceColor and the shader substitutes the colour outright.
			self.World.AddFrameEndTask(w => w.Add(info.Brightness > 0f
				? new FlashTarget(self, Tint, info.Count, info.Interval)
				: new FlashTarget(self, info.Color, info.Alpha, info.Count, info.Interval)));
		}
	}
}
