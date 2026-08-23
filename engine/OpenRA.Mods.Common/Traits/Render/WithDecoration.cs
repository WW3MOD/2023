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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Displays a custom UI overlay relative to the actor's mouseover bounds.")]
	public class WithDecorationInfo : WithDecorationBaseInfo
	{
		[Desc("Image used for this decoration. Defaults to the actor's type.")]
		public readonly string Image = null;

		[FieldLoader.Require]
		[SequenceReference(nameof(Image), allowNullImage: true)]
		[Desc("Sequence used for this decoration (can be animated).")]
		public readonly string Sequence = null;

		[PaletteReference(nameof(IsPlayerPalette))]
		[Desc("Palette to render the sprite in. Reference the world actor's PaletteFrom* traits.")]
		public readonly string Palette = "chrome";

		[Desc("Custom palette is a player palette BaseName")]
		public readonly bool IsPlayerPalette = false;

		public override object Create(ActorInitializer init) { return new WithDecoration(init.Self, this); }
	}

	public class WithDecoration : WithDecorationBase<WithDecorationInfo>, ITick
	{
		protected Animation anim;
		readonly string image;
		readonly BlinkPhase blinkPhase = new();
		IHealth health;

		public WithDecoration(Actor self, WithDecorationInfo info)
			: base(self, info)
		{
			image = info.Image ?? self.Info.Name;
			anim = new Animation(self.World, image, () => self.World.Paused);

			// PITFALL: an animated decoration must not advance its frame off the sim tick. Animation.Tick()
			// credits a fixed 40ms per world tick (Animation.cs, "25 fps == 40 ms"), but a world tick is
			// Timestep ms of REAL time — 120ms at the slowest game speed and 30ms at the fastest — so a
			// ticked sequence runs 4x faster at "insane" than at "strategical", and 1.5x slower than its
			// author intended at the default 60ms. Fetching the frame from wall-clock instead makes a
			// sequence's Tick: value mean real milliseconds at every game speed, matching the sibling
			// BlinkPattern mechanism in WithDecorationBase. Render-only: the fetched index feeds nothing
			// but this decoration's own sprite, so it stays out of the synced trait state. Do NOT "fix"
			// this in Animation itself — RenderSprites is ITick and PlayThen callbacks gate Transform,
			// Sellable and the dock sequences, so changing Animation's advance rate would desync.
			// Length-1 sequences (every other decoration in the mod) resolve to frame 0 as before.
			anim.PlayFetchIndex(info.Sequence, FetchFrame);
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			// Resolved here rather than in the constructor: the actor's traits are not all built yet
			// while this one is being constructed. FetchFrame tolerates a null health (it just means
			// no ramp), which is also what a decoration on a healthless actor gets.
			health = self.TraitOrDefault<IHealth>();
		}

		// Runs once per world tick from ITick — NOT once per rendered frame — so carrying phase state
		// across calls is safe here in a way it would not be inside ShouldRender on the render path.
		int FetchFrame()
		{
			var sequence = anim.CurrentSequence;
			if (sequence.Length <= 1)
				return 0;

			var interval = sequence.Tick;
			if (sequence.HealthRampTick > 0 && health != null && health.MaxHP > 0)
				interval = DecorationBlink.IntervalForHealth(
					sequence.Tick, sequence.HealthRampTick, sequence.HealthRampStart,
					health.HP * 100 / health.MaxHP);

			return blinkPhase.Advance(Game.RunTime, interval, sequence.Length);
		}

		protected virtual PaletteReference GetPalette(Actor self, WorldRenderer wr)
		{
			return wr.Palette(Info.IsPlayerPalette ? Info.Palette + self.Owner.InternalName : Info.Palette);
		}

		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			if (anim == null)
				return Enumerable.Empty<IRenderable>();

			return new IRenderable[]
			{
				new UISpriteRenderable(anim.Image, self.CenterPosition, screenPos - (0.5f * anim.Image.Size.XY).ToInt2(), 0, GetPalette(self, wr))
			};
		}

		void ITick.Tick(Actor self) { anim.Tick(); }
	}
}
