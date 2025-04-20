#region Copyright & License Information
/*
 * Copyright 2007-2025 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Plays a sequential animation (start, loop, end) when a specified condition is active.")]
	public class WithSequentialAnimationInfo : ConditionalTraitInfo, Requires<RenderSpritesInfo>, Requires<WithSpriteBodyInfo>
	{
		[Desc("The image to use for the animation.")]
		public readonly string Image = "smoke_m";

		[SequenceReference(nameof(Image))]
		[Desc("The sequence to play once when the condition is first granted.")]
		public readonly string StartSequence = "start";

		[SequenceReference(nameof(Image))]
		[Desc("The sequence to loop while the condition is active.")]
		public readonly string LoopSequence = "loop";

		[SequenceReference(nameof(Image))]
		[Desc("The sequence to play once when the condition is removed or the actor dies.")]
		public readonly string EndSequence = "end";

		[Desc("The condition to monitor for triggering the animation.")]
		public readonly string Condition = "critical-damage";

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Custom palette name for the animation.")]
		public readonly string Palette = null;

		[Desc("Custom palette is a player palette BaseName.")]
		public readonly bool IsPlayerPalette = false;

		[Desc("Offset of the animation relative to the actor's center.")]
		public readonly WVec Offset = WVec.Zero;

		public override object Create(ActorInitializer init) { return new WithSequentialAnimation(init.Self, this); }
	}

	public class WithSequentialAnimation : ConditionalTrait<WithSequentialAnimationInfo>, INotifyKilled
	{
		readonly WithSequentialAnimationInfo info;
		readonly Animation anim;
		readonly RenderSprites rs;
		bool isAnimating;
		bool conditionActive;

		public WithSequentialAnimation(Actor self, WithSequentialAnimationInfo info)
			: base(info)
		{
			this.info = info;
			rs = self.Trait<RenderSprites>();
			anim = new Animation(self.World, info.Image);
			rs.Add(new AnimationWithOffset(anim, () => info.Offset, () => !isAnimating || IsTraitDisabled), info.Palette, info.IsPlayerPalette);

			// Check initial condition state
			if (!IsTraitDisabled)
				StartAnimation(self);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (conditionActive)
				StopAnimation(self);
		}

		void StartAnimation(Actor self)
		{
			if (isAnimating)
				return;

			conditionActive = true;
			isAnimating = true;
			anim.PlayThen(info.StartSequence, () =>
			{
				if (conditionActive && !IsTraitDisabled)
					anim.PlayRepeating(info.LoopSequence);
			});
		}

		void StopAnimation(Actor self)
		{
			conditionActive = false;
			if (isAnimating)
			{
				anim.PlayThen(info.EndSequence, () => isAnimating = false);
			}
		}

		protected override void TraitEnabled(Actor self)
		{
			// Re-check condition when trait is enabled
			StartAnimation(self);
		}

		protected override void TraitDisabled(Actor self)
		{
			StopAnimation(self);
		}
	}
}
