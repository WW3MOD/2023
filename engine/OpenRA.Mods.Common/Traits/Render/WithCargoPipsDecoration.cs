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
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	public class WithCargoPipsDecorationInfo : WithDecorationBaseInfo, Requires<CargoInfo>
	{
		[Desc("Number of pips to display. Defaults to Cargo.MaxWeight.")]
		public readonly int PipCount = -1;

		[Desc("Number of pips to display per row.")]
		public readonly int PerRow = 8;

		[Desc("If non-zero, override the spacing between adjacent pips.")]
		public readonly int2 PipStride = int2.Zero;

		[Desc("Image that defines the pip sequences.")]
		public readonly string Image = "class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for empty pips.")]
		public readonly string EmptySequence = "empty_class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for full pips that aren't defined in CustomPipSequences.")]
		public readonly string FullSequence = "unknown_class";

		[SequenceReference(nameof(Image), dictionaryReference: LintDictionaryReference.Values)]
		[Desc("Pip sequence to use for specific passenger actors.")]
		public readonly Dictionary<string, string> CustomPipSequences = new();

		[PaletteReference]
		public readonly string Palette = "chrome";

		public override object Create(ActorInitializer init) { return new WithCargoPipsDecoration(init.Self, this); }
	}

	public class WithCargoPipsDecoration : WithDecorationBase<WithCargoPipsDecorationInfo>
	{
		readonly Cargo cargo;
		readonly Animation pips;
		readonly int pipCount;

		readonly Actor self;

		int PipCount { get => self.Trait<Cargo>().PassengerCount; }

		public WithCargoPipsDecoration(Actor self, WithCargoPipsDecorationInfo info)
			: base(self, info)
		{
			this.self = self;
			cargo = self.Trait<Cargo>();
			pipCount = info.PipCount > 0 ? info.PipCount : cargo.Info.MaxWeight;
			pips = new Animation(self.World, info.Image);
		}

		string GetPipSequence(int i)
		{
			var n = i * cargo.Info.MaxWeight / pipCount;

			foreach (var c in cargo.Passengers)
			{
				var pi = c.Info.TraitInfo<PassengerInfo>();
				if (n < pi.Weight)
				{
					if (pi.CustomPipType != null)
						return pi.CustomPipType;

					return Info.FullSequence;
				}

				n -= pi.Weight;
			}

			return Info.EmptySequence;
		}

		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			var selected = self.World.Selection.Contains(self);
			var scale = selected ? 1f : 0.5f;
			var alpha = selected ? 0.2f : 0.05f;

			var palette = wr.Palette(Info.Palette);
			var pipImageSize = pips.Image.Size;
			var pipSize = new int2((int)(pipImageSize.X * scale), (int)(pipImageSize.Y * scale));

			var pipStrideX = new int2(pipSize.X, 0);
			var pipStrideY = new int2(0, pipSize.Y);

			var currentRow = 1;
			var currentRowCount = (currentRow * Info.PerRow) > PipCount ? (PipCount % Info.PerRow) : Info.PerRow;

			screenPos -= pipSize / 2;
			var startPos = screenPos;

			screenPos -= (currentRowCount - 1) * pipStrideX / 2;

			pips.PlayRepeating(Info.EmptySequence);

			for (var i = 0; i < PipCount; i++)
			{
				pips.PlayRepeating(GetPipSequence(i));
				yield return new UISpriteRenderable(
					pips.Image, self.CenterPosition, screenPos, 0, palette, scale, alpha);

				if (i + 1 >= currentRow * Info.PerRow)
				{
					screenPos = startPos - (pipStrideY * currentRow); // Vertical increment for each row

					currentRow++;
					currentRowCount = (currentRow * Info.PerRow) > PipCount ? (PipCount % Info.PerRow) : Info.PerRow;

					screenPos -= (currentRowCount - 1) * pipStrideX / 2; // Horizontal center alignment
				}
				else
				{
					screenPos += pipStrideX;
				}
			}
		}
	}
}
