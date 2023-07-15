#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	[Desc("The actor is visible under fog with radar.")]
	public class VisibleWithRadarInfo : HiddenUnderShroudInfo
	{
		public override object Create(ActorInitializer init) { return new VisibleWithRadar(init, this); }
	}

	public class VisibleWithRadar : HiddenUnderShroud
	{
		bool traitEnabled = false;

		public VisibleWithRadar(ActorInitializer init, VisibleWithRadarInfo info)
			: base(init, info) { }

		protected override bool IsVisibleInner(Actor self, Player byPlayer)
		{
			// If fog is disabled visibility is determined by shroud
			/* if (!byPlayer.MapLayers.FogEnabled)
				return base.IsVisibleInner(self, byPlayer);
			if (Info.Type == VisibilityType.Footprint)
				return byPlayer.MapLayers.AnyVisible(self.OccupiesSpace.OccupiedCells()); */

			var pos = self.CenterPosition;

			if (Info.Type == VisibilityType.GroundPosition)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			if (traitEnabled && byPlayer.MapLayers.RadarCover(pos))
				return true;

			return byPlayer.MapLayers.IsVisible(pos, 1); // TODO
		}

		protected override void TraitEnabled(Actor self)
		{
			traitEnabled = true;
		}

		protected override void TraitDisabled(Actor self)
		{
			traitEnabled = false;
		}

		protected override void TraitResumed(Actor self)
		{
			traitEnabled = true;
		}

		protected override void TraitPaused(Actor self)
		{
			traitEnabled = false;
		}
	}
}
