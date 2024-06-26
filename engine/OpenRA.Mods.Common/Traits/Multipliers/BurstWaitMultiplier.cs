#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Modifies the reload time of weapons fired by this actor.")]
	public class BurstWaitMultiplierInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[Desc("Percentage modifier to apply.")]
		public readonly int Modifier = 100;

		public override object Create(ActorInitializer init) { return new BurstWaitMultiplier(this); }
	}

	public class BurstWaitMultiplier : ConditionalTrait<BurstWaitMultiplierInfo>, IBurstWaitModifier
	{
		public BurstWaitMultiplier(BurstWaitMultiplierInfo info)
			: base(info) { }

		int IBurstWaitModifier.GetBurstWaitModifier() { return IsTraitDisabled ? 100 : Info.Modifier; }
	}
}
