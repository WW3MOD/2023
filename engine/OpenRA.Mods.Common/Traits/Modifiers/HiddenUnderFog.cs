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

namespace OpenRA.Mods.Common.Traits
{
	[Desc("The actor stays invisible under fog of war.")]
	public class HiddenUnderFogInfo : HiddenUnderShroudInfo
	{
		public readonly int VisualVisibility = 2;
		public readonly int RadarVisibility = 0;
		public override object Create(ActorInitializer init) { return new HiddenUnderFog(init, this); }
	}

	public class HiddenUnderFog : HiddenUnderShroud
	{
		readonly HiddenUnderFogInfo info;

		public HiddenUnderFog(ActorInitializer init, HiddenUnderFogInfo info)
			: base(init, info)
			{
				this.info = info;
			}

		protected override bool IsVisibleInner(Actor self, Player byPlayer)
		{
			// If fog is disabled visibility is determined by shroud
			if (!byPlayer.MapLayer.FogEnabled)
				return base.IsVisibleInner(self, byPlayer);

			if (Info.Type == VisibilityType.Footprint)
				return byPlayer.MapLayer.AnyVisible(self.OccupiesSpace.OccupiedCells(), info.VisualVisibility);

			var pos = self.CenterPosition;
			if (Info.Type == VisibilityType.GroundPosition)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			return byPlayer.MapLayer.IsVisible(pos, info.VisualVisibility);
		}
	}
}
