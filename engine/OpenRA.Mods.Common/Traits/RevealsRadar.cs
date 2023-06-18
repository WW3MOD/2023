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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class RevealsRadarInfo : AffectsRadarInfo
	{
		[Desc("Relationships the watching player needs to see the shroud removed.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		public override object Create(ActorInitializer init) { return new RevealsRadar(init.Self, this); }
	}

	public class RevealsRadar : AffectsRadar
	{
		readonly RevealsRadarInfo info;
		readonly Shroud.SourceType type;
		IEnumerable<int> rangeModifiers;

		public RevealsRadar(Actor self, RevealsRadarInfo info)
			: base(self, info)
		{
			this.info = info;
			type = Shroud.SourceType.Radar;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			rangeModifiers = self.TraitsImplementing<IRevealsRadarModifier>().ToArray().Select(x => x.GetRevealsRadarModifier());
		}

		protected override void AddCellsToPlayerRadar(Actor self, Player p, PPos[] uv)
		{
			if (!info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(p)))
				return;

			p.Shroud.AddSource(this, type, uv);
		}

		protected override void RemoveCellsFromPlayerRadar(Actor self, Player p) { p.Shroud.RemoveSource(this); }

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
