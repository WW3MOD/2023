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

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Modifies the required vision to see this actor.")]
	public class VisibilityModifierInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[Desc("Modifier to apply.")]
		public readonly int Modifier = 0;

		public override object Create(ActorInitializer init) { return new VisibilityModifier(this); }
	}

	public class VisibilityModifier : ConditionalTrait<VisibilityModifierInfo>, IVisibilityModifier
	{
		public VisibilityModifier(VisibilityModifierInfo info)
			: base(info) { }

		int IVisibilityModifier.GetVisibilityModifier()
		{
			return IsTraitDisabled ? 0 : Info.Modifier;
		}
	}
}
