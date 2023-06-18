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
	public class RearmsUnitsInfo : PausableConditionalTraitInfo
	{
		[Desc("How close is close enough for rearming.")]
		public readonly WDist CloseEnough = WDist.Zero;

		public override object Create(ActorInitializer init) { return new RearmsUnits(this); }
	}

	public class RearmsUnits : PausableConditionalTrait<RearmsUnitsInfo>
	{
		public RearmsUnits(RearmsUnitsInfo info)
			: base(info) { }
	}
}
