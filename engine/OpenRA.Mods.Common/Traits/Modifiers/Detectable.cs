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

		[Desc("0 = not detectable by counter-battery radar, 1 = is detectable. Only the MSAR's CounterBatteryRadar trait provides this coverage.")]
		public readonly int CounterBatteryRadar = 0;

		[ConsumedConditionReference]
		[Desc("Condition that activates counter-battery radar detectability (e.g. 'firing')")]
		public readonly BooleanExpression CounterBatteryRadarDetectableCondition = null;

		public readonly string CounterBatteryRadarDetectableGrantsCondition = "counter-battery-radar-detectable";
		public readonly string VisionDetectableConditionPrefix = "visibility-";

		// DetectableVisionChanged grants "<prefix><CurrentVisibility>", a name built at runtime. Declare the
		// whole set so the condition lint can see it: without this, every consumer (e.g.
		// ^DetectableRangeCircles) reads as "consumes conditions that are not granted".
		// This is deliberately a SUPERSET of what Detectable.ClampConcealment can now produce -- it still
		// declares the top level, which the ceiling put out of reach. Narrowing it would strand the tier-10
		// ring as a consumer of an undeclared condition, and that ring must survive a revert of the ceiling.
		[GrantedConditionReference]
		public IEnumerable<string> VisionDetectableConditions =>
			Enumerable.Range(1, MapLayers.VisionLayers - 1).Select(i => VisionDetectableConditionPrefix + i);

		[Desc("Players with these relationships can always see the actor.")]
		public readonly PlayerRelationship AlwaysVisibleRelationships = PlayerRelationship.Ally;

		[Desc("Possible values are CenterPosition (reveal when the center is visible) and ",
			"Footprint (reveal when any footprint cell is visible).")]
		public readonly DetectablePosition Position = DetectablePosition.Footprint;

		public override object Create(ActorInitializer init) => new Detectable(init, this);
	}

	public class Detectable : PausableConditionalTrait<DetectableInfo>, IDefaultVisibility, IRenderModifier, ITick
	{
		protected readonly DetectableInfo DetectableInfo;
		IEnumerable<int> detectableModifiers;
		public int PreviousVisibility { get; set; }

		[Sync]
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

		/// <summary>
		/// Clamps a composed concealment level into the range detection can actually resolve.
		/// </summary>
		/// <remarks>
		/// The ceiling is VisionLayers - 2 — one BELOW the top vision band — and that gap is the whole point.
		/// Concealment and observer strength share a single 1..VisionLayers-1 ladder, so while concealment
		/// could reach the top, the strongest observer in the game could at best match it and a unit at the
		/// ceiling was undetectable at every range by everything.
		///
		/// State the guarantee exactly, because the obvious phrasing is wrong and this comment carried it.
		/// The gap guarantees that an observer whose STAMPED strength reaches the ceiling detects. On a bare
		/// sightline that is ^StandardVision's strength-9 band, i.e. inside 7 cells. It does NOT mean "inside
		/// 7 cells" in general: MapLayers.AddSource subtracts the sightline's forest shadow from the observer
		/// BEFORE stamping it (MapLayers.cs:371-374), Map.ForestGroundShadow returns 2 for crossed density
		/// 11-20, and one authored tree cell is density 10 — so an observer crossing about two dense cells
		/// stamps 8 and cannot detect a ceiling-concealment target at ANY range. That is not a corner case:
		/// the term that carries a unit to the ceiling is object-proximity, i.e. being surrounded by the very
		/// density that casts the shadow.
		///
		/// What IS unconditional is the adjacent case, which is what the ruling was about. Shadow entries
		/// exist only for viewer/target pairs 2-32 cells apart (Map.RecomputeShadowFrom's annulus) and the
		/// walk skips the from and to cells (Map.cs:1150-1155), so an observer standing next to the target
		/// crosses nothing, stamps full strength, and detects. "Invisible while an enemy stands on top of it"
		/// is closed. "Invisible in forest at range" is NOT — closing that needs the observer floor raised in
		/// AddSource, which also moves fog rendering, radar and the AI belief layer.
		///
		/// The ceiling holds whether reveal is strict or not, so it does not depend on IsDetected staying
		/// non-strict. The floor of 1 is pre-existing and stays: 0 is shroud's level and must not be a
		/// concealment value.
		/// </remarks>
		public static int ClampConcealment(int concealment)
		{
			if (concealment < 1)
				return 1;

			var ceiling = MapLayers.VisionLayers - 2;
			return concealment > ceiling ? ceiling : concealment;
		}

		void ITick.Tick(Actor self)
		{
			var detectable = ClampConcealment(Util.ApplyAddativeModifiers(DetectableInfo.Vision, detectableModifiers));

			CurrentVisibility = detectable;

			if (PreviousVisibility != CurrentVisibility)
			{
				DetectableVisionChanged(self);
				PreviousVisibility = CurrentVisibility;
			}
		}

		protected virtual bool IsVisibleInner(Actor self, Player byPlayer)
		{
			var pos = self.CenterPosition;
			if (DetectableInfo.Position == DetectablePosition.Ground)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			var detectable = ClampConcealment(Util.ApplyAddativeModifiers(DetectableInfo.Vision, detectableModifiers));

			if (DetectableInfo.Position == DetectablePosition.Footprint)
				return byPlayer.MapLayers.AnyDetectable(self.OccupiesSpace.OccupiedCells(), detectable)
					|| IsRadarDetectedBy(self, byPlayer);

			return byPlayer.MapLayers.IsDetectable(pos, detectable)
				|| IsRadarDetectedBy(self, byPlayer);
		}

		/// <summary>
		/// Whether <paramref name="byPlayer"/> holds this actor on radar or counter-battery radar,
		/// independently of any line of sight.
		/// </summary>
		/// <remarks>
		/// PITFALL: radar is a SEPARATE map layer from vision. It increments MapLayers.radarCount and
		/// contributes nothing to ResolvedVisibility, so a radar-only contact sits on a cell that every
		/// band-based query — IsVisible, IsDetectable, World.FogObscures(pos) — still reports as fogged.
		/// A caller asking "does this player legitimately know the actor is here" must therefore ask this
		/// as well, or it will veto real radar knowledge. See MouseTargetVisibility.
		/// </remarks>
		public bool IsRadarDetectedBy(Actor self, Player byPlayer)
		{
			if (byPlayer == null)
				return false;

			if (DetectableInfo.Position == DetectablePosition.Footprint)
			{
				var cells = self.OccupiesSpace.OccupiedCells();
				return (RadarDetectionActive() && byPlayer.MapLayers.AnyVisibleOnRader(cells))
					|| (CounterBatteryRadarDetectionActive() && byPlayer.MapLayers.AnyVisibleOnCounterBatteryRadar(cells));
			}

			var pos = self.CenterPosition;
			if (DetectableInfo.Position == DetectablePosition.Ground)
				pos -= new WVec(WDist.Zero, WDist.Zero, self.World.Map.DistanceAboveTerrain(pos));

			return (RadarDetectionActive() && byPlayer.MapLayers.RadarCover(pos))
				|| (CounterBatteryRadarDetectionActive() && byPlayer.MapLayers.CounterBatteryRadarCover(pos));
		}

		bool RadarDetectionActive()
		{
			return DetectableInfo.Radar != 0 && IsRadarDetectable;
		}

		bool CounterBatteryRadarDetectionActive()
		{
			return DetectableInfo.CounterBatteryRadar != 0 && IsCounterBatteryRadarDetectable;
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

			if (DetectableInfo.CounterBatteryRadarDetectableCondition != null)
				yield return new VariableObserver(CounterBatteryRadarConditionsChanged, DetectableInfo.CounterBatteryRadarDetectableCondition.Variables);
		}

		// PITFALL: never [Sync] a condition token — its value is an allocation handle counting how many
		// conditions the actor has been granted, so a grant-count skew desyncs clients whose gameplay state
		// agrees. The gameplay state here is the visibility level, synced on CurrentVisibility above.
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

		[Sync]
		public bool IsCounterBatteryRadarDetectable { get; private set; }
		int counterBatteryRadarDetectableConditionToken = Actor.InvalidConditionToken;

		void CounterBatteryRadarConditionsChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			if (IsCounterBatteryRadarDetectable != DetectableInfo.CounterBatteryRadarDetectableCondition.Evaluate(conditions))
			{
				if (IsCounterBatteryRadarDetectable)
					CounterBatteryRadarDetectableTraitDisabled(self);
				else
					CounterBatteryRadarDetectableTraitEnabled(self);
			}
		}

		protected void CounterBatteryRadarDetectableTraitEnabled(Actor self)
		{
			IsCounterBatteryRadarDetectable = true;

			if (counterBatteryRadarDetectableConditionToken == Actor.InvalidConditionToken)
				counterBatteryRadarDetectableConditionToken = self.GrantCondition(DetectableInfo.CounterBatteryRadarDetectableGrantsCondition);
		}

		protected void CounterBatteryRadarDetectableTraitDisabled(Actor self)
		{
			IsCounterBatteryRadarDetectable = false;

			if (counterBatteryRadarDetectableConditionToken != Actor.InvalidConditionToken)
				counterBatteryRadarDetectableConditionToken = self.RevokeCondition(counterBatteryRadarDetectableConditionToken);
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			// TODO Modify to GPS dot when barely visible?
			if (IsVisible(self, self.World.RenderPlayer))
				return r;

			// Cosmetic reveal: render non-visible actors as semi-transparent ghosts
			var devMode = self.World.LocalPlayer?.PlayerActor.TraitOrDefault<DeveloperMode>();
			if (devMode != null && devMode.CosmeticReveal)
				return ApplyCosmeticRevealAlpha(r);

			return SpriteRenderable.None;
		}

		static IEnumerable<IRenderable> ApplyCosmeticRevealAlpha(IEnumerable<IRenderable> renderables)
		{
			foreach (var renderable in renderables)
			{
				if (renderable is IModifyableRenderable mr)
					yield return mr.WithAlpha(mr.Alpha * 0.5f);
				else
					yield return renderable;
			}
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}
	}
}
