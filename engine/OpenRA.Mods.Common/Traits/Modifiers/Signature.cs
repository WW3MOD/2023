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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("The actor visibility/radar signature/detectability.")]
	public class SignatureInfo : PausableConditionalTraitInfo, IDefaultVisibilityInfo
	{
		[Desc("")]
		public readonly int Vision = 2;

		[Desc("")]
		public readonly int Radar = 0;

		[Desc("Players with these relationships can always see the actor.")]
		public readonly PlayerRelationship AlwaysVisibleRelationships = PlayerRelationship.Ally;

		[Desc("Possible values are CenterPosition (reveal when the center is visible) and ",
			"Footprint (reveal when any footprint cell is visible).")]
		public readonly SignaturePosition Position = SignaturePosition.Footprint;

		public override object Create(ActorInitializer init) => new Signature(init, this);
	}

	public class Signature : PausableConditionalTrait<SignatureInfo>, IDefaultVisibility, IRenderModifier
	{
		protected readonly SignatureInfo SignatureInfo;
		IEnumerable<int> visibilityModifiers;
		public Signature(ActorInitializer _, SignatureInfo info)
			: base(info)
			{
				SignatureInfo = info;
			}

		protected override void Created(Actor self)
		{
			base.Created(self);

			visibilityModifiers = self.TraitsImplementing<IVisibilityAddativeModifier>().ToArray().Select(x => x.GetVisibilityAddativeModifier());
		}

		protected virtual bool IsVisibleInner(Actor self, Player byPlayer)
		{
			var pos = self.CenterPosition;
			if (SignatureInfo.Position == SignaturePosition.Ground)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			var vision = Util.ApplyAddativeModifiers(SignatureInfo.Vision, visibilityModifiers);

			if (vision > MapLayers.VisionLayers - 1)
				vision = MapLayers.VisionLayers - 1;

			if (SignatureInfo.Position == SignaturePosition.Footprint)
			{
				return byPlayer.MapLayers.AnyVisible(self.OccupiesSpace.OccupiedCells(), vision) || (SignatureInfo.Radar != 0 && byPlayer.MapLayers.RadarCover(pos));
			}

			return byPlayer.MapLayers.IsVisible(pos, vision) || (SignatureInfo.Radar != 0 && byPlayer.MapLayers.RadarCover(pos));
		}

		public bool IsVisible(Actor self, Player byPlayer)
		{
			if (byPlayer == null)
				return true;

			var relationship = self.Owner.RelationshipWith(byPlayer);
			return SignatureInfo.AlwaysVisibleRelationships.HasRelationship(relationship) || IsVisibleInner(self, byPlayer);
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			return IsVisible(self, self.World.RenderPlayer) ? r : SpriteRenderable.None;
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}
	}
}