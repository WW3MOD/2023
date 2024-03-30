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
	[Desc("Renders an animation when actor is added to world.")]
	public class WithAddedAnimationInfo : TraitInfo, Requires<RenderSpritesInfo>
	{
		[FieldLoader.Require]
		public readonly string Image = "";

		[FieldLoader.Require]
		[SequenceReference(nameof(Image))]
		public readonly string Sequence = "";

		[SequenceReference(nameof(Image))]
		public readonly string MidSequence = "";

		[SequenceReference(nameof(Image))]
		public readonly string EndSequence = "";

		[Desc("How much to offset the animation relative actor.")]
		public readonly WVec Offset = WVec.Zero;

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Custom palette name.")]
		public readonly string Palette = null;

		[Desc("Custom palette is a player palette BaseName.")]
		public readonly bool IsPlayerPalette = false;

		public override object Create(ActorInitializer init) { return new WithAddedAnimation(init.Self, this); }
	}

	public class WithAddedAnimation : INotifyAddedToWorld
	{
		readonly WithAddedAnimationInfo info;
		readonly Animation anim;

		bool hasEnded = false;

		public WithAddedAnimation(Actor self, WithAddedAnimationInfo info)
		{
			this.info = info;

			var rs = self.Trait<RenderSprites>();

			anim = new Animation(self.World, info.Image)
			{
				IsDecoration = true
			};

			rs.Add(new AnimationWithOffset(anim, () => info.Offset, () => hasEnded),
				info.Palette, info.IsPlayerPalette);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			anim.PlayThen(info.Sequence, () => { hasEnded = true; });

			// if (info.MidSequence != "" && info.EndSequence != "")
			// 		anim.PlayThen(info.Sequence,
			// 			() => anim.PlayThen(info.MidSequence,
			// 				() => anim.PlayThen(info.EndSequence, null)));
			// else if (info.EndSequence != "")
			// 	anim.PlayThen(info.Sequence,
			// 		() => anim.PlayThen(info.EndSequence, null));
			// else
			// 	anim.PlayThen(info.Sequence, null);
		}
	}
}
