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
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Renders the actor at reduced alpha when a condition is active. " +
		"Useful for garrisoned soldiers visible 'through' buildings.")]
	public class WithAlphaConditionInfo : ConditionalTraitInfo
	{
		[Desc("Alpha value (0.0 fully transparent - 1.0 fully opaque) when the condition is active.")]
		public readonly float Alpha = 0.4f;

		public override object Create(ActorInitializer init) { return new WithAlphaCondition(this); }
	}

	public class WithAlphaCondition : ConditionalTrait<WithAlphaConditionInfo>, IRenderModifier
	{
		public WithAlphaCondition(WithAlphaConditionInfo info)
			: base(info) { }

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			if (IsTraitDisabled)
				return r;

			return ModifiedRender(r);
		}

		IEnumerable<IRenderable> ModifiedRender(IEnumerable<IRenderable> r)
		{
			foreach (var a in r)
			{
				if (!a.IsDecoration && a is IModifyableRenderable ma)
					yield return ma.WithAlpha(Info.Alpha);
				else
					yield return a;
			}
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}
	}
}
