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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Adds the hard-coded shroud palette to the game")]
	class ShroudPaletteInfo : TraitInfo
	{
		[PaletteDefinition]
		[Desc("Internal palette name")]
		public readonly string Shroud = "shroud";

		[PaletteDefinition]
		[Desc("Internal palette name")]
		public readonly string Fog = "fog";

		[PaletteDefinition]
		[Desc("Internal palette name")]
		public readonly string Haze = "haze";

		public readonly int HazeSteps = 10;

		public override object Create(ActorInitializer init) { return new ShroudPalette(this); }
	}

	class ShroudPalette : ILoadsPalettes, IProvidesAssetBrowserPalettes
	{
		readonly ShroudPaletteInfo info;

		public ShroudPalette(ShroudPaletteInfo info) { this.info = info; }

		public void LoadPalettes(WorldRenderer wr)
		{
			wr.AddPalette(info.Shroud, new ImmutablePalette(Enumerable.Range(0, Palette.Size).Select(i => (uint)ShroudColors[i % 8].ToArgb())));
			wr.AddPalette(info.Fog, new ImmutablePalette(Enumerable.Range(0, Palette.Size).Select(i => (uint)FogColors[i % 8].ToArgb())));

			for (int hazeIndex = 0; hazeIndex < info.HazeSteps; hazeIndex++)
			{
				Color[] HazeColors = new[]
				{
					Color.FromArgb(0, 0, 0, 0),
					Color.Green, Color.Blue, Color.Yellow,
					Color.FromArgb(20 + hazeIndex * 10, 0, 0, 0),
					Color.FromArgb(96, 0, 0, 0),
					Color.FromArgb(64, 0, 0, 0),
					Color.FromArgb(32, 0, 0, 0)
				};

				wr.AddPalette(info.Haze + hazeIndex, new ImmutablePalette(Enumerable.Range(0, Palette.Size).Select(i => (uint)HazeColors[i % 8].ToArgb())));
			}

		}

		static readonly Color[] ShroudColors = new[]
		{
			Color.FromArgb(0, 0, 0, 0),
			Color.Green, Color.Blue, Color.Yellow,
			Color.FromArgb(255, 0, 0, 0),
			Color.FromArgb(96, 0, 0, 0),
			Color.FromArgb(64, 0, 0, 0),
			Color.FromArgb(32, 0, 0, 0)
		};

		static readonly Color[] FogColors = new[]
		{
			Color.FromArgb(0, 0, 0, 0),
			Color.Green, Color.Blue, Color.Yellow,
			Color.FromArgb(150, 0, 0, 0),
			Color.FromArgb(96, 0, 0, 0),
			Color.FromArgb(64, 0, 0, 0),
			Color.FromArgb(32, 0, 0, 0)
		};

		public IEnumerable<string> PaletteNames {
			get
			{
				yield return info.Shroud;
				yield return info.Fog;

				for (int hazeIndex = 0; hazeIndex < info.HazeSteps; hazeIndex++)
				{
					yield return info.Haze + hazeIndex;
				}
			}
		}
	}
}
