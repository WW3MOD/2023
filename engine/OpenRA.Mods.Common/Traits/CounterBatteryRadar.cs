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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Provides counter-battery radar coverage that detects enemy artillery when they fire.")]
	public class CounterBatteryRadarInfo : AffectsMapLayerInfo
	{
		[Desc("Relationships the watching player needs to utilize the counter-battery radar coverage.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		public override object Create(ActorInitializer init) { return new CounterBatteryRadar(this); }
	}

	public class CounterBatteryRadar : AffectsMapLayer
	{
		readonly CounterBatteryRadarInfo info;
		IEnumerable<int> rangeModifiers;
		public override MapLayers.Type Type => MapLayers.Type.CounterBatteryRadar;

		public CounterBatteryRadar(CounterBatteryRadarInfo info)
			: base(info)
		{
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			rangeModifiers = self.TraitsImplementing<IRadarModifier>().ToArray().Select(x => x.GetRadarModifier());
		}

		protected override void AddCellsToPlayerMapLayer(Actor self, Player p, IReadOnlyList<PPos> uv)
		{
			if (!info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(p)))
				return;

			p.MapLayers.AddSource(this, 1, uv);
		}

		protected override void RemoveCellsFromPlayerMapLayer(Actor self, Player p) { p.MapLayers.RemoveSource(this); }

		public override WDist MinRange
		{
			get
			{
				if (CachedTraitDisabled)
					return WDist.Zero;

				var range = Util.ApplyPercentageModifiers(Info.MinRange.Length, rangeModifiers);
				return new WDist(range);
			}
		}

		public override WDist Range
		{
			get
			{
				if (CachedTraitDisabled)
					return WDist.Zero;

				var range = Util.ApplyPercentageModifiers(Info.Range.Length, rangeModifiers);
				return new WDist(range);
			}
		}
	}
}
