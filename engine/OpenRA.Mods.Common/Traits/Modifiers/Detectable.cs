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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("The actor visibility/radar signature/detectability.")]
	public class DetectableInfo : PausableConditionalTraitInfo, IDefaultVisibilityInfo
	{
		[Desc("What level of vision is required to detect this actor")]
		public readonly int Vision = 2;

		[Desc("0 = not detectable by radar, 1 = is detectable by radar. (Using int because possible future implementation of stealth features)")]
		public readonly int Radar = 0;

		[ConsumedConditionReference]
		[Desc("Conditions to activate a third custom sequence")]
		public readonly BooleanExpression RadarDetectableCondition = null;

		public readonly string RadarDetectableGrantsCondition = "radar-detectable";
		public readonly string VisionDetectableConditionPrefix = "visibility-";

		[Desc("Players with these relationships can always see the actor.")]
		public readonly PlayerRelationship AlwaysVisibleRelationships = PlayerRelationship.Ally;

		[Desc("Possible values are CenterPosition (reveal when the center is visible) and ",
			"Footprint (reveal when any footprint cell is visible).")]
		public readonly DetectablePosition Position = DetectablePosition.Footprint;

		public override object Create(ActorInitializer init) => new Detectable(init, this);
	}

	public class Detectable : PausableConditionalTrait<DetectableInfo>, IDefaultVisibility, IRenderModifier
	{
		protected readonly DetectableInfo DetectableInfo;
		IEnumerable<int> detectableModifiers;
		public int PreviousVisibility { get; set; }
		public int CurrentVisibility { get; set; }
		public Detectable(ActorInitializer _, DetectableInfo info)
			: base(info)
			{
				DetectableInfo = info;
			}

		protected override void Created(Actor self)
		{
			base.Created(self);

			detectableModifiers = self.TraitsImplementing<IDetectableAddativeModifier>().ToArray().Select(x => x.GetDetectableVisionAddativeModifier());
		}

		protected virtual bool IsVisibleInner(Actor self, Player byPlayer)
		{
			var pos = self.CenterPosition;
			if (DetectableInfo.Position == DetectablePosition.Ground)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			var detectable = Util.ApplyAddativeModifiers(DetectableInfo.Vision, detectableModifiers);

			if (detectable <= 0)
				detectable = 1;
			else if (detectable > MapLayers.VisionLayers - 1)
				detectable = MapLayers.VisionLayers - 1;

			CurrentVisibility = detectable;

			if (PreviousVisibility != CurrentVisibility)
			{
				DetectableVisionChanged(self);
				PreviousVisibility = CurrentVisibility;
			}

			if (DetectableInfo.Position == DetectablePosition.Footprint)
			{
				return byPlayer.MapLayers.AnyVisible(self.OccupiesSpace.OccupiedCells(), detectable) || (RadarDetectionActive() && byPlayer.MapLayers.AnyVisibleOnRader(self.OccupiesSpace.OccupiedCells()));
			}

			return byPlayer.MapLayers.IsVisible(pos, detectable) || (RadarDetectionActive() && byPlayer.MapLayers.RadarCover(pos));
		}

		bool RadarDetectionActive()
		{
			return DetectableInfo.Radar != 0 && IsRadarDetectable;
		}

		public bool IsVisible(Actor self, Player byPlayer)
		{
			if (byPlayer == null)
				return true;

			var relationship = self.Owner.RelationshipWith(byPlayer);
			return DetectableInfo.AlwaysVisibleRelationships.HasRelationship(relationship) || IsVisibleInner(self, byPlayer);
		}

		public override IEnumerable<VariableObserver> GetVariableObservers()
		{
			foreach (var observer in base.GetVariableObservers())
				yield return observer;

			if (DetectableInfo.RadarDetectableCondition != null)
				yield return new VariableObserver(RadarConditionsChanged, DetectableInfo.RadarDetectableCondition.Variables);
		}

		[Sync]
		int visionDetectableConditionToken = Actor.InvalidConditionToken;

		protected void DetectableVisionChanged(Actor self)
		{
			if (visionDetectableConditionToken != Actor.InvalidConditionToken)
				visionDetectableConditionToken = self.RevokeCondition(visionDetectableConditionToken);

			visionDetectableConditionToken = self.GrantCondition(DetectableInfo.VisionDetectableConditionPrefix + CurrentVisibility);
		}

		[Sync]
		public bool IsRadarDetectable { get; private set; }
		int radarDetectableConditionToken = Actor.InvalidConditionToken;

		void RadarConditionsChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			if (IsRadarDetectable != DetectableInfo.RadarDetectableCondition.Evaluate(conditions))
			{
				if (IsRadarDetectable)
					RadarDetectableTraitDisabled(self);
				else
					RadarDetectableTraitEnabled(self);
			}
		}

		protected void RadarDetectableTraitEnabled(Actor self)
		{
			IsRadarDetectable = true;

			if (radarDetectableConditionToken == Actor.InvalidConditionToken)
				radarDetectableConditionToken = self.GrantCondition(DetectableInfo.RadarDetectableGrantsCondition);
		}

		protected void RadarDetectableTraitDisabled(Actor self)
		{
			IsRadarDetectable = false;

			if (radarDetectableConditionToken != Actor.InvalidConditionToken)
				radarDetectableConditionToken = self.RevokeCondition(radarDetectableConditionToken);
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			// TODO Modify to GPS dot when barely visible?
			return IsVisible(self, self.World.RenderPlayer) ? r : SpriteRenderable.None;
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}
	}
}
