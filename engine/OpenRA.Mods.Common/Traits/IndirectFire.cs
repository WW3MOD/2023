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
	[Desc("This actor shoots at targets outside it's own visual range.")]
	public class IndirectFireInfo : ConditionalTraitInfo
	{
		public override object Create(ActorInitializer init) { return new IndirectFire(init.Self, this); }
	}

	public class IndirectFire : ConditionalTrait<IndirectFireInfo>
	{
		public IndirectFire(Actor self, IndirectFireInfo info)
			: base(info) { }
	}
}
