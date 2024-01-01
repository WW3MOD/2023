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

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.MiniMap
{
	public class MiniMapColorFromTerrainInfo : TraitInfo
	{
		[FieldLoader.Require]
		public readonly string Terrain;

		public Color GetColorFromTerrain(World world)
		{
			var terrainInfo = world.Map.Rules.TerrainInfo;
			return terrainInfo.TerrainTypes[terrainInfo.GetTerrainIndex(Terrain)].Color;
		}

		public override object Create(ActorInitializer init) { return new MiniMapColorFromTerrain(init.Self, this); }
	}

	public class MiniMapColorFromTerrain : IMiniMapColorModifier
	{
		readonly Color c;

		public MiniMapColorFromTerrain(Actor self, MiniMapColorFromTerrainInfo info)
		{
			c = info.GetColorFromTerrain(self.World);
		}

		public Color MiniMapColorOverride(Actor self, Color color) { return c; }
	}
}
