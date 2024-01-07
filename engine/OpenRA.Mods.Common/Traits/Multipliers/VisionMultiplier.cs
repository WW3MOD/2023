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
	[Desc("Modifies the shroud range revealed by this actor.")]
	public class VisionModifierInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[Desc("Percentage modifier to apply.")]
		public readonly int Modifier = 100;

		public override object Create(ActorInitializer init) { return new VisionModifier(this); }
	}

	public class VisionModifier : ConditionalTrait<VisionModifierInfo>, IVisionModifier
	{
		public VisionModifier(VisionModifierInfo info)
			: base(info) { }

		int IVisionModifier.GetVisionModifier()
		{
			return IsTraitDisabled ? 100 : Info.Modifier;
		}
	}
}
