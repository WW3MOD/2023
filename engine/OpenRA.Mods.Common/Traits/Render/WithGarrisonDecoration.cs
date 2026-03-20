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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Renders unit class icons at garrison port positions and ammo pips beneath them.")]
	public class WithGarrisonDecorationInfo : TraitInfo, Requires<GarrisonManagerInfo>, Requires<RenderSpritesInfo>
	{
		[Desc("Image that defines the pip/icon sequences.")]
		public readonly string Image = "class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for empty port indicators.")]
		public readonly string EmptySequence = "empty_class";

		[PaletteReference]
		public readonly string Palette = "chrome";

		[Desc("Image for ammo pips.")]
		public readonly string AmmoImage = "pips";

		[SequenceReference(nameof(AmmoImage))]
		[Desc("Sequence for full ammo pips.")]
		public readonly string AmmoFullSequence = "pip-green";

		[SequenceReference(nameof(AmmoImage))]
		[Desc("Sequence for empty ammo pips.")]
		public readonly string AmmoEmptySequence = "pip-empty";

		[Desc("Screen-space vertical offset from port world position for the icon.")]
		public readonly int IconVerticalOffset = -20;

		[Desc("Maximum ammo pips to show per occupant.")]
		public readonly int MaxAmmoPips = 5;

		[Desc("Only show when building is selected.")]
		public readonly bool RequiresSelection = false;

		public override object Create(ActorInitializer init) { return new WithGarrisonDecoration(init.Self, this); }
	}

	public class WithGarrisonDecoration : IRender, INotifyCreated
	{
		readonly WithGarrisonDecorationInfo info;
		readonly Animation iconAnim;
		readonly Animation ammoAnim;

		GarrisonManager garrisonManager;

		public WithGarrisonDecoration(Actor self, WithGarrisonDecorationInfo info)
		{
			this.info = info;
			iconAnim = new Animation(self.World, info.Image);
			ammoAnim = new Animation(self.World, info.AmmoImage);
		}

		void INotifyCreated.Created(Actor self)
		{
			garrisonManager = self.Trait<GarrisonManager>();
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (garrisonManager == null)
				yield break;

			if (info.RequiresSelection && !self.World.Selection.Contains(self))
				yield break;

			var selected = self.World.Selection.Contains(self);
			var scale = selected ? 1f : 0.5f;
			var alpha = selected ? 0.3f : 0.1f;
			var palette = wr.Palette(info.Palette);
			var coords = self.Trait<BodyOrientation>();

			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var ps = garrisonManager.PortStates[i];
				var portWorldOffset = garrisonManager.GetPortWorldOffset(i, coords);
				var portWorldPos = self.CenterPosition + portWorldOffset;
				var portScreenPos = wr.Viewport.WorldToViewPx(wr.ScreenPosition(portWorldPos));

				// Offset icon above the port position
				portScreenPos += new int2(0, info.IconVerticalOffset);

				if (ps.Occupant != null && !ps.Occupant.IsDead)
				{
					// Draw unit class icon
					var pi = ps.Occupant.Info.TraitInfoOrDefault<PassengerInfo>();
					var iconSequence = pi?.CustomPipType ?? info.EmptySequence;

					iconAnim.PlayRepeating(iconSequence);
					yield return new UISpriteRenderable(
						iconAnim.Image, portWorldPos, portScreenPos, 0, palette, scale, alpha);

					// Draw ammo pips if selected
					if (selected)
					{
						var ammoPool = ps.Occupant.TraitsImplementing<AmmoPool>().FirstOrDefault();
						if (ammoPool != null)
						{
							var pipCount = System.Math.Min(ammoPool.Info.Ammo, info.MaxAmmoPips);
							var pipScreenPos = portScreenPos + new int2(-pipCount * 3, 8);

							for (var p = 0; p < pipCount; p++)
							{
								var isFull = p < ammoPool.CurrentAmmoCount;
								var seq = isFull ? info.AmmoFullSequence : info.AmmoEmptySequence;

								ammoAnim.PlayRepeating(seq);
								yield return new UISpriteRenderable(
									ammoAnim.Image, portWorldPos, pipScreenPos + new int2(p * 6, 0),
									0, palette, 0.5f, alpha);
							}
						}
					}
				}
				else
				{
					// Draw empty port indicator
					iconAnim.PlayRepeating(info.EmptySequence);
					yield return new UISpriteRenderable(
						iconAnim.Image, portWorldPos, portScreenPos, 0, palette, scale * 0.7f, alpha * 0.5f);
				}
			}
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			yield break;
		}
	}
}
