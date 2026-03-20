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
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Renders garrison port occupant icons at port world positions around the building.")]
	public class WithGarrisonDecorationInfo : TraitInfo, Requires<GarrisonManagerInfo>, Requires<RenderSpritesInfo>
	{
		[Desc("Image that defines the pip/icon sequences.")]
		public readonly string Image = "class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for empty port indicators.")]
		public readonly string EmptySequence = "empty_class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for occupied ports without a CustomPipType.")]
		public readonly string FullSequence = "unknown_class";

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

		[Desc("Maximum ammo pips to show per occupant.")]
		public readonly int MaxAmmoPips = 5;

		[Desc("Only show when building is selected.")]
		public readonly bool RequiresSelection = false;

		[Desc("Scale of icons when selected.")]
		public readonly float SelectedScale = 2.25f;

		[Desc("Scale of icons when not selected.")]
		public readonly float UnselectedScale = 1.5f;

		[Desc("Additional vertical offset (in world units) above the port position for the icon.")]
		public readonly int IconAltitudeOffset = 512;

		[Desc("Width of health bars in pixels.")]
		public readonly int HealthBarWidth = 24;

		[Desc("Height of health bars in pixels.")]
		public readonly int HealthBarHeight = 3;

		public override object Create(ActorInitializer init) { return new WithGarrisonDecoration(init.Self, this); }
	}

	public class WithGarrisonDecoration : IRender, IRenderAnnotationsWhenSelected, INotifyCreated
	{
		readonly WithGarrisonDecorationInfo info;
		readonly RenderSprites renderSprites;

		GarrisonManager garrisonManager;

		// Cached screen positions per port for click detection
		readonly Dictionary<int, int2> portScreenPositions = new Dictionary<int, int2>();
		readonly Dictionary<int, int> portIconSizes = new Dictionary<int, int>();

		// Selected port index (-1 = none)
		public int SelectedPortIndex = -1;

		public WithGarrisonDecoration(Actor self, WithGarrisonDecorationInfo info)
		{
			this.info = info;
			renderSprites = self.Trait<RenderSprites>();
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
			var scale = selected ? info.SelectedScale : info.UnselectedScale;

			var palette = wr.Palette(info.Palette);
			var ammoPalette = wr.Palette(info.Palette);
			var coords = self.Trait<BodyOrientation>();

			portScreenPositions.Clear();
			portIconSizes.Clear();

			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var ps = garrisonManager.PortStates[i];
				var portWorldOffset = garrisonManager.GetPortWorldOffset(i, coords);

				// Raise the icon above the port position
				var iconOffset = portWorldOffset + new WVec(0, 0, info.IconAltitudeOffset);
				var iconWorldPos = self.CenterPosition + iconOffset;
				var zOffset = RenderUtils.ZOffsetFromCenter(self, iconWorldPos, 1024);

				// Cache screen position for click detection
				var screenPos = wr.Viewport.WorldToViewPx(wr.ScreenPosition(iconWorldPos));
				portScreenPositions[i] = screenPos;
				portIconSizes[i] = (int)(16 * scale);

				var isSelectedPort = i == SelectedPortIndex;
				var portScale = isSelectedPort ? scale * 1.2f : scale;

				if (ps.Occupant != null && !ps.Occupant.IsDead)
				{
					// Draw unit class icon at port position
					var pi = ps.Occupant.Info.TraitInfoOrDefault<PassengerInfo>();
					var iconSequence = pi?.CustomPipType ?? info.FullSequence;

					var iconAnim = new Animation(self.World, info.Image);
					iconAnim.PlayRepeating(iconSequence);
					foreach (var r in iconAnim.Render(iconWorldPos, WVec.Zero, zOffset, palette, portScale))
						yield return r;

					// Draw ammo pips below the icon if selected
					if (selected)
					{
						var ammoPool = ps.Occupant.TraitsImplementing<AmmoPool>().FirstOrDefault();
						if (ammoPool != null)
						{
							var pipCount = System.Math.Min(ammoPool.Info.Ammo, info.MaxAmmoPips);
							for (var p = 0; p < pipCount; p++)
							{
								var isFull = p < ammoPool.CurrentAmmoCount;
								var seq = isFull ? info.AmmoFullSequence : info.AmmoEmptySequence;

								// Offset each pip horizontally, and below the icon
								var pipOffset = iconOffset + new WVec(0, 0, -200) + new WVec((p - pipCount / 2) * 80, 0, 0);
								var pipWorldPos = self.CenterPosition + pipOffset;

								var ammoAnim = new Animation(self.World, info.AmmoImage);
								ammoAnim.PlayRepeating(seq);
								foreach (var r in ammoAnim.Render(pipWorldPos, WVec.Zero, zOffset - 1, ammoPalette, scale * 0.5f))
									yield return r;
							}
						}
					}
				}
				else if (selected)
				{
					// Draw empty port indicator when selected
					var emptyAnim = new Animation(self.World, info.Image);
					emptyAnim.PlayRepeating(info.EmptySequence);
					foreach (var r in emptyAnim.Render(iconWorldPos, WVec.Zero, zOffset, palette, scale * 0.7f))
						yield return r;
				}
			}
		}

		IEnumerable<IRenderable> IRenderAnnotationsWhenSelected.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (garrisonManager == null)
				yield break;

			var coords = self.Trait<BodyOrientation>();
			var cr = Game.Renderer.RgbaColorRenderer;

			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var ps = garrisonManager.PortStates[i];
				if (ps.Occupant == null || ps.Occupant.IsDead)
					continue;

				var occupantHealth = ps.Occupant.TraitOrDefault<IHealth>();
				if (occupantHealth == null || occupantHealth.IsDead)
					continue;

				// Get screen position for this port
				var portWorldOffset = garrisonManager.GetPortWorldOffset(i, coords);
				var iconOffset = portWorldOffset + new WVec(0, 0, info.IconAltitudeOffset);
				var iconWorldPos = self.CenterPosition + iconOffset;

				// Health bar positioned below icon
				var healthBarPos = iconOffset + new WVec(0, 0, -info.IconAltitudeOffset / 3);
				var healthWorldPos = self.CenterPosition + healthBarPos;
				var screenCenter = wr.Viewport.WorldToViewPx(wr.ScreenPosition(healthWorldPos));

				var barWidth = info.HealthBarWidth;
				var barHeight = info.HealthBarHeight;
				var barLeft = screenCenter.X - barWidth / 2;
				var barTop = screenCenter.Y;

				// Background
				var bgStart = new float2(barLeft, barTop);
				var bgEnd = new float2(barLeft + barWidth, barTop);
				var bgColor = Color.FromArgb(160, 0, 0, 0);
				for (var row = 0; row < barHeight; row++)
				{
					var offset = new float2(0, row);
					cr.DrawLine(bgStart + offset, bgEnd + offset, 1, bgColor);
				}

				// Health fill
				var hpPct = (float)occupantHealth.HP / occupantHealth.MaxHP;
				var fillWidth = (int)(barWidth * hpPct);
				if (fillWidth > 0)
				{
					Color barColor;
					if (hpPct > 0.65f)
						barColor = Color.LimeGreen;
					else if (hpPct > 0.35f)
						barColor = Color.Yellow;
					else
						barColor = Color.Red;

					var fillStart = new float2(barLeft, barTop);
					var fillEnd = new float2(barLeft + fillWidth, barTop);
					for (var row = 0; row < barHeight; row++)
					{
						var offset = new float2(0, row);
						cr.DrawLine(fillStart + offset, fillEnd + offset, 1, barColor);
					}
				}

				// Selection highlight around selected port
				if (i == SelectedPortIndex)
				{
					var highlightColor = Color.FromArgb(200, 0, 255, 0);
					var iconScreenCenter = wr.Viewport.WorldToViewPx(wr.ScreenPosition(iconWorldPos));
					var halfSize = (int)(10 * info.SelectedScale);
					var tl = new float2(iconScreenCenter.X - halfSize, iconScreenCenter.Y - halfSize);
					var tr = new float2(iconScreenCenter.X + halfSize, iconScreenCenter.Y - halfSize);
					var bl = new float2(iconScreenCenter.X - halfSize, iconScreenCenter.Y + halfSize);
					var br = new float2(iconScreenCenter.X + halfSize, iconScreenCenter.Y + halfSize);
					cr.DrawLine(tl, tr, 1, highlightColor);
					cr.DrawLine(tr, br, 1, highlightColor);
					cr.DrawLine(br, bl, 1, highlightColor);
					cr.DrawLine(bl, tl, 1, highlightColor);
				}
			}

			yield break;
		}

		bool IRenderAnnotationsWhenSelected.SpatiallyPartitionable => false;

		public int? GetPortAtScreenPosition(int2 screenPos)
		{
			foreach (var kvp in portScreenPositions)
			{
				var center = kvp.Value;
				var halfSize = portIconSizes.ContainsKey(kvp.Key) ? portIconSizes[kvp.Key] : 16;
				if (System.Math.Abs(screenPos.X - center.X) <= halfSize &&
					System.Math.Abs(screenPos.Y - center.Y) <= halfSize)
					return kvp.Key;
			}

			return null;
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			yield break;
		}
	}
}
