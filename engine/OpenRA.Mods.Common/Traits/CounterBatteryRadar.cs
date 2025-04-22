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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class CounterBatteryRadarInfo : AffectsMapLayerInfo
	{
		[Desc("Strength of this layer")]
		public readonly int Strength = 1;

		[Desc("Relationships the watching player needs to utilize the coverage.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		public override object Create(ActorInitializer init) { return new CounterBatteryRadar(this); }
	}

	public class CounterBatteryRadar : AffectsMapLayer
	{
		readonly CounterBatteryRadarInfo info;
		public override MapLayers.Type Type => MapLayers.Type.CounterBatteryRadar;

		public CounterBatteryRadar(CounterBatteryRadarInfo info)
			: base(info)
		{
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
		}

		protected override void AddCellsToPlayerMapLayer(Actor self, Player p, PPos[] uv)
		{
			if (!info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(p)))
				return;

			p.MapLayers.AddSource(this, info.Strength, uv);
		}

		protected override void RemoveCellsFromPlayerMapLayer(Actor self, Player p) { p.MapLayers.RemoveSource(this); }

		public override WDist Range
		{
			get
			{
				if (CachedTraitDisabled)
					return WDist.Zero;

				return Info.Range;
			}
		}
	}
}
