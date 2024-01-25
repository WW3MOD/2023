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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	public enum RangeCircleMode { Maximum, Minimum }

	[Desc("Draw a circle indicating my weapon's range.")]
	class RenderRangeCircleInfo : ConditionalTraitInfo, IPlaceBuildingDecorationInfo, IRulesetLoaded, Requires<AttackBaseInfo>
	{
		[Desc("Which armament to draw circle for.")]
		public readonly string Armament = "primary";

		[Desc("I think this overlaps same type circles, try it out.")]
		public readonly string RangeCircleType = null;

		[Desc("Range to draw if no armaments are available.")]
		public readonly WDist FallbackRange = WDist.Zero;

		[Desc("Which circle to show. Valid values are `Maximum`, and `Minimum`.")]
		public readonly RangeCircleMode RangeCircleMode = RangeCircleMode.Maximum;

		[Desc("Alpha of the circle and scanner update line.")]
		public readonly int Alpha = 35;

		[Desc("Color of the circle.")]
		public Color Color = Color.FromArgb(60, Color.Red); // FF - no longer readonly, problem?

		[Desc("Range circle line width.")]
		public readonly float Width = 1;

		[Desc("Color of the border.")]
		public readonly Color BorderColor = Color.FromArgb(50, Color.Black);

		[Desc("Range circle border width.")]
		public readonly float BorderWidth = 0;

		// Computed range
		Lazy<WDist> range;

		public IEnumerable<IRenderable> RenderAnnotations(WorldRenderer wr, World w, ActorInfo ai, WPos centerPosition)
		{
			if (range == null || range.Value == WDist.Zero)
				return SpriteRenderable.None;

			var localRange = new RangeCircleAnnotationRenderable(
				centerPosition,
				range.Value,
				0,
				Color,
				Width,
				BorderColor,
				BorderWidth);

			var otherRanges = w.ActorsWithTrait<RenderRangeCircle>()
				.Where(a => a.Trait.Info.RangeCircleType == RangeCircleType)
				.SelectMany(a => a.Trait.RangeCircleRenderables());

			return otherRanges.Append(localRange);
		}

		public override object Create(ActorInitializer init)
		{
			Color = Color.FromArgb(Alpha, Color);

			return new RenderRangeCircle(init.Self, this);
		}

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			// ArmamentInfo.ModifiedRange is set by RulesetLoaded, and may not have been initialized yet.
			// Defer this lookup until we really need it to ensure we get the correct value.
			range = Exts.Lazy(() =>
			{
				var armaments = ai.TraitInfos<ArmamentInfo>().Where(a => a.EnabledByDefault);
				if (!armaments.Any())
					return FallbackRange;

				return armaments.Max(a => a.ModifiedRange);
			});
		}
	}

	class RenderRangeCircle : ConditionalTrait<RenderRangeCircleInfo>, INotifyCreated, IRenderAnnotationsWhenSelected
	{
		public readonly RenderRangeCircleInfo Info;
		readonly Actor self;
		public Armament armament;

		public RenderRangeCircle(Actor self, RenderRangeCircleInfo info)
			: base(info)
		{
			this.Info = info;
			this.self = self;
		}

		void INotifyCreated.Created(Actor self)
		{
			armament = self.TraitsImplementing<Armament>().FirstOrDefault(a => a.Info.Name == Info.Armament);
		}

		bool Visible
		{
			get
			{
				if (IsTraitDisabled)
					return false;

				return true;

				// var p = self.World.RenderPlayer;
				// return p == null || Info.ValidRelationships.HasStance(self.Owner.RelationshipWith(p)) || (p.Spectating && !p.NonCombatant);
			}
		}

		public IEnumerable<IRenderable> RangeCircleRenderables()
		{
			if (Visible)
			{
				if (!self.Owner.IsAlliedWith(self.World.RenderPlayer))
					yield break;

				var range = Info.RangeCircleMode == RangeCircleMode.Minimum ? armament.MinRange() : armament.MaxRange();
				if (range == WDist.Zero)
					yield break;

				yield return new RangeCircleAnnotationRenderable(
					self.CenterPosition,
					range,
					0,
					Info.Color,
					Info.Width,
					Info.BorderColor,
					Info.BorderWidth);
			}
		}

		IEnumerable<IRenderable> IRenderAnnotationsWhenSelected.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			return RangeCircleRenderables();
		}

		bool IRenderAnnotationsWhenSelected.SpatiallyPartitionable => false;
	}
}
