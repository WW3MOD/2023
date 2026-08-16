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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class CreatesShroudInfo : AffectsMapLayerInfo
	{
		[Desc("Relationship the watching player needs to see the generated shroud.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		public override object Create(ActorInitializer init) { return new CreatesShroud(this); }
	}

	public class CreatesShroud : AffectsMapLayer
	{
		// AffectsMapLayer.Type is `virtual => throw new NotImplementedException()`, and MapLayers reads
		// it on the first AddCellsToPlayerMapLayer call. Radar, CounterBatteryRadar and Vision each
		// override it; CreatesShroud never did, so ANY actor carrying this trait hard-crashed on
		// entering the world. It appears in no shipped YAML, which is the only reason that was never hit.
		//
		// PassiveVisibility is the honest value, but be clear about what it buys: MapLayers.AddSource
		// branches on Vision / Radar / CounterBatteryRadar only, and the generated-shroud counter is
		// commented out (MapLayers.cs:125), so shroud GENERATION is not implemented in this rewrite at
		// all. This override makes the trait SAFE, not FUNCTIONAL - adding CreatesShroud to an actor
		// will no longer crash, and will also not produce any shroud. Wiring that up is a feature, not
		// a bug fix. Vision would be actively wrong: it would make a shroud generator REVEAL the map.
		public override MapLayers.Type Type => MapLayers.Type.PassiveVisibility;

		readonly CreatesShroudInfo info;
		IEnumerable<int> rangeModifiers;

		public CreatesShroud(CreatesShroudInfo info)
			: base(info)
		{
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			rangeModifiers = self.TraitsImplementing<ICreatesShroudModifier>().ToArray().Select(x => x.GetCreatesShroudModifier());
		}

		protected override void AddCellsToPlayerMapLayer(Actor self, Player p, IReadOnlyList<PPos> uv)
		{
			if (!info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(p)))
				return;

			p.MapLayers.AddSource(this, 1, uv);
		}

		protected override void RemoveCellsFromPlayerMapLayer(Actor self, Player p)
		{
			if (!info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(p)))
				return;

			p.MapLayers.RemoveSource(this);
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
