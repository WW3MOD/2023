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

using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Renders an overlay when the actor is taking heavy damage.")]
	public class WithOverlayInfo : ConditionalTraitInfo, Requires<RenderSpritesInfo>
	{
		public readonly string Image = "smoke_m";

		[SequenceReference(nameof(Image))]
		public readonly string IdleSequence = "idle";

		[SequenceReference(nameof(Image))]
		public readonly string LoopSequence = "loop";

		[SequenceReference(nameof(Image))]
		public readonly string EndSequence = "end";

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Custom palette name.")]
		public readonly string Palette = null;

		[Desc("Custom palette is a player palette BaseName.")]
		public readonly bool IsPlayerPalette = false;

		[Desc("Position relative to body")]
		public readonly WVec Offset = WVec.Zero;

		public override object Create(ActorInitializer init) { return new WithOverlay(init, this); }
	}

	public class WithOverlay : ConditionalTrait<WithOverlayInfo>
	{
		readonly Animation anim;
		bool isActive;

		public WithOverlay(ActorInitializer init, WithOverlayInfo info)
			: base(info)
		{
			var rs = init.Self.Trait<RenderSprites>();

			anim = new Animation(init.Self.World, info.Image);
			rs.Add(new AnimationWithOffset(anim, () => Info.Offset, () => !isActive),
				info.Palette, info.IsPlayerPalette);
		}

		protected override void TraitEnabled(Actor _)
		{
			isActive = true;
		}

		protected override void TraitDisabled(Actor _)
		{
			isActive = false;
		}
	}
}
