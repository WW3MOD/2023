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
	public class VisibilityAddativeModifierInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[Desc("Modifier to apply.")]
		public readonly int Modifier = 0;

		public override object Create(ActorInitializer init) { return new VisibilityAddativeModifier(this); }
	}

	public class VisibilityAddativeModifier : ConditionalTrait<VisibilityAddativeModifierInfo>, IVisibilityAddativeModifier
	{
		public VisibilityAddativeModifier(VisibilityAddativeModifierInfo info)
			: base(info) { }

		int IVisibilityAddativeModifier.GetVisibilityAddativeModifier()
		{
			return IsTraitDisabled ? 0 : Info.Modifier;
		}
	}
}
