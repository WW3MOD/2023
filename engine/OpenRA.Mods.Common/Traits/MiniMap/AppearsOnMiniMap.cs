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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.MiniMap
{
	public class AppearsOnMiniMapInfo : ConditionalTraitInfo
	{
		public readonly bool UseLocation = false;

		[Desc("Player relationships who can view this actor on radar.")]
		public readonly PlayerRelationship ValidRelationships = PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		public override object Create(ActorInitializer init) { return new AppearsOnMiniMap(this); }
	}

	public class AppearsOnMiniMap : ConditionalTrait<AppearsOnMiniMapInfo>, IMiniMapSignature
	{
		IMiniMapColorModifier modifier;

		public AppearsOnMiniMap(AppearsOnMiniMapInfo info)
			: base(info) { }

		protected override void Created(Actor self)
		{
			base.Created(self);
			modifier = self.TraitsImplementing<IMiniMapColorModifier>().FirstOrDefault();
		}

		public void PopulateMiniMapSignatureCells(Actor self, List<(CPos Cell, Color Color)> destinationBuffer)
		{
			var viewer = self.World.RenderPlayer ?? self.World.LocalPlayer;
			if (IsTraitDisabled || (viewer != null && !Info.ValidRelationships.HasRelationship(self.Owner.RelationshipWith(viewer))))
				return;

			var color = Game.Settings.Game.UsePlayerStanceColors ? self.Owner.PlayerRelationshipColor(self) : self.Owner.Color;
			if (modifier != null)
				color = modifier.MiniMapColorOverride(self, color);

			if (Info.UseLocation)
			{
				destinationBuffer.Add((self.Location, color));
				return;
			}

			foreach (var cell in self.OccupiesSpace.OccupiedCells())
				destinationBuffer.Add((cell.Cell, color));
		}
	}
}
