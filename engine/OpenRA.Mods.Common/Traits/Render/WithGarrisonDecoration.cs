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
	[Desc("Renders garrison pips below the building showing all soldiers (shelter + deployed at ports). " +
		"Also renders empty port indicators when the building is selected.")]
	public class WithGarrisonDecorationInfo : WithDecorationBaseInfo, Requires<GarrisonManagerInfo>, Requires<CargoInfo>
	{
		[Desc("Image that defines the pip/icon sequences.")]
		public readonly string Image = "class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for empty pips (no soldier in slot).")]
		public readonly string EmptySequence = "empty_class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for soldiers without a CustomPipType.")]
		public readonly string FullSequence = "unknown_class";

		[PaletteReference]
		public readonly string Palette = "chrome";

		[Desc("Number of pips per row.")]
		public readonly int PerRow = 8;

		[Desc("Image for empty port indicators when selected.")]
		public readonly string PortImage = "class";

		[SequenceReference(nameof(PortImage))]
		[Desc("Sequence used for empty port indicators when selected.")]
		public readonly string PortEmptySequence = "empty_class";

		[Desc("Scale of empty port icons when building is selected.")]
		public readonly float PortScale = 1.5f;

		[Desc("Additional vertical offset (in world units) above the port position for port icons.")]
		public readonly int PortIconAltitudeOffset = 512;

		public override object Create(ActorInitializer init) { return new WithGarrisonDecoration(init.Self, this); }
	}

	public class WithGarrisonDecoration : WithDecorationBase<WithGarrisonDecorationInfo>, IRender
	{
		readonly Animation pips;
		readonly Cargo cargo;

		GarrisonManager garrisonManager;

		public WithGarrisonDecoration(Actor self, WithGarrisonDecorationInfo info)
			: base(self, info)
		{
			pips = new Animation(self.World, info.Image);
			cargo = self.Trait<Cargo>();
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
			garrisonManager = self.Trait<GarrisonManager>();
		}

		int TotalSoldierCount()
		{
			if (garrisonManager == null)
				return 0;

			var count = 0;
			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
				if (garrisonManager.PortStates[i].DeployedSoldier != null && !garrisonManager.PortStates[i].DeployedSoldier.IsDead)
					count++;
			count += garrisonManager.ShelterPassengers.Count(s => s != null && !s.IsDead);
			return count;
		}

		// Collect all soldiers: deployed at ports + shelter (in cargo)
		IEnumerable<Actor> AllSoldiers()
		{
			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var soldier = garrisonManager.PortStates[i].DeployedSoldier;
				if (soldier != null && !soldier.IsDead)
					yield return soldier;
			}

			foreach (var soldier in garrisonManager.ShelterPassengers)
				if (soldier != null && !soldier.IsDead)
					yield return soldier;
		}

		string GetPipSequence(int index)
		{
			var i = 0;
			foreach (var soldier in AllSoldiers())
			{
				if (i == index)
				{
					var pi = soldier.Info.TraitInfoOrDefault<PassengerInfo>();
					if (pi?.CustomPipType != null)
						return pi.CustomPipType;
					return Info.FullSequence;
				}

				i++;
			}

			return Info.EmptySequence;
		}

		// Pips decoration (below actor, screen-space, via WithDecorationBase/IDecoration)
		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			if (garrisonManager == null)
				yield break;

			var totalCount = TotalSoldierCount();
			if (totalCount == 0)
				yield break;

			var maxSlots = cargo.Info.MaxWeight;
			var pipCount = maxSlots > 0 ? maxSlots : totalCount;

			var selected = self.World.Selection.Contains(self);
			var scale = selected ? 1f : 0.7f;
			var alpha = selected ? 0.8f : 0.35f;

			var palette = wr.Palette(Info.Palette);
			pips.PlayRepeating(Info.EmptySequence);
			var pipImageSize = pips.Image.Size;
			var pipSize = new int2((int)(pipImageSize.X * scale), (int)(pipImageSize.Y * scale));
			var pipStrideX = new int2(pipSize.X, 0);
			var pipStrideY = new int2(0, pipSize.Y);

			var currentRow = 1;
			var currentRowCount = (currentRow * Info.PerRow) > pipCount ? (pipCount % Info.PerRow) : Info.PerRow;

			screenPos -= pipSize / 2;
			var startPos = screenPos;
			screenPos -= (currentRowCount - 1) * pipStrideX / 2;

			for (var i = 0; i < pipCount; i++)
			{
				pips.PlayRepeating(i < totalCount ? GetPipSequence(i) : Info.EmptySequence);
				yield return new UISpriteRenderable(
					pips.Image, self.CenterPosition, screenPos, 0, palette, scale, alpha);

				if (i + 1 >= currentRow * Info.PerRow)
				{
					screenPos = startPos - (pipStrideY * currentRow);
					currentRow++;
					currentRowCount = (currentRow * Info.PerRow) > pipCount ? (pipCount % Info.PerRow) : Info.PerRow;
					screenPos -= (currentRowCount - 1) * pipStrideX / 2;
				}
				else
				{
					screenPos += pipStrideX;
				}
			}
		}

		// Empty port indicators (world-space, via IRender)
		// Currently disabled — deployed soldiers are visible at 40% alpha, which is sufficient.
		// Port indicator rendering can be re-enabled here when better sprites/icons are available.
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (garrisonManager == null)
				yield break;

			// Only show port indicators when building is selected
			if (!self.World.Selection.Contains(self))
				yield break;

			var portPalette = wr.Palette(Info.Palette);
			var coords = self.Trait<BodyOrientation>();

			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var ps = garrisonManager.PortStates[i];

				// Only show empty port indicators
				if (ps.DeployedSoldier != null)
					continue;

				var portWorldOffset = garrisonManager.GetPortWorldOffset(i, coords);
				var iconOffset = portWorldOffset + new WVec(0, 0, Info.PortIconAltitudeOffset);
				var iconWorldPos = self.CenterPosition + iconOffset;
				var zOffset = RenderUtils.ZOffsetFromCenter(self, iconWorldPos, 1024);

				var emptyAnim = new Animation(self.World, Info.PortImage);
				emptyAnim.PlayRepeating(Info.PortEmptySequence);
				foreach (var r in emptyAnim.Render(iconWorldPos, WVec.Zero, zOffset, portPalette))
					yield return r;
			}
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			yield break;
		}
	}
}
