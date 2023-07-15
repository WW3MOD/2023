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
	public class RevealsShroudInfo : AffectsShroudInfo
	{
		[Desc("How much visibility to grant for this layer.")]
		public readonly int Visibility = 10;

		[Desc("Relationships the watching player needs to see the shroud removed.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally;

		public override object Create(ActorInitializer init) { return new RevealsShroud(this); }
	}

	public class RevealsShroud : AffectsShroud
	{
		readonly RevealsShroudInfo info;
		IEnumerable<int> rangeModifiers;

		public RevealsShroud(RevealsShroudInfo info)
			: base(info)
		{
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			rangeModifiers = self.TraitsImplementing<IRevealsShroudModifier>().ToArray().Select(x => x.GetRevealsShroudModifier());
		}

		protected override void AddCellsToPlayerShroud(Actor self, Player p, PPos[] uv)
		{
			if (!info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(p)))
				return;

			p.Shroud.AddSource(this, info.Visibility, uv);
		}

		protected override void RemoveCellsFromPlayerShroud(Actor self, Player p) { p.Shroud.RemoveSource(this); }

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
