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
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Renders empty port indicators when the building is selected. " +
		"Deployed soldiers have their own in-world pips/decorations.")]
	public class WithGarrisonDecorationInfo : TraitInfo, Requires<GarrisonManagerInfo>, Requires<RenderSpritesInfo>
	{
		[Desc("Image that defines the pip/icon sequences.")]
		public readonly string Image = "class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for empty port indicators.")]
		public readonly string EmptySequence = "empty_class";

		[PaletteReference]
		public readonly string Palette = "chrome";

		[Desc("Scale of empty port icons.")]
		public readonly float Scale = 1.5f;

		[Desc("Additional vertical offset (in world units) above the port position for the icon.")]
		public readonly int IconAltitudeOffset = 512;

		public override object Create(ActorInitializer init) { return new WithGarrisonDecoration(init.Self, this); }
	}

	public class WithGarrisonDecoration : IRender, INotifyCreated
	{
		readonly WithGarrisonDecorationInfo info;

		GarrisonManager garrisonManager;

		public WithGarrisonDecoration(Actor self, WithGarrisonDecorationInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			garrisonManager = self.Trait<GarrisonManager>();
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (garrisonManager == null)
				yield break;

			// Only show when building is selected
			if (!self.World.Selection.Contains(self))
				yield break;

			var palette = wr.Palette(info.Palette);
			var coords = self.Trait<BodyOrientation>();

			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var ps = garrisonManager.PortStates[i];

				// Only show empty port indicators (deployed soldiers have their own pips)
				if (ps.DeployedSoldier != null)
					continue;

				var portWorldOffset = garrisonManager.GetPortWorldOffset(i, coords);
				var iconOffset = portWorldOffset + new WVec(0, 0, info.IconAltitudeOffset);
				var iconWorldPos = self.CenterPosition + iconOffset;
				var zOffset = RenderUtils.ZOffsetFromCenter(self, iconWorldPos, 1024);

				var emptyAnim = new Animation(self.World, info.Image);
				emptyAnim.PlayRepeating(info.EmptySequence);
				foreach (var r in emptyAnim.Render(iconWorldPos, WVec.Zero, zOffset, palette, info.Scale * 0.7f))
					yield return r;
			}
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			yield break;
		}
	}
}
