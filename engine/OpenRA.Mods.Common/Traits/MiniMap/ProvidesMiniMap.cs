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

namespace OpenRA.Mods.Common.Traits.MiniMap
{
	[Desc("This actor enables the radar minimap.")]
	public class ProvidesMiniMapInfo : ConditionalTraitInfo
	{
		public override object Create(ActorInitializer init) { return new ProvidesMiniMap(this); }
	}

	public class ProvidesMiniMap : ConditionalTrait<ProvidesMiniMapInfo>
	{
		public ProvidesMiniMap(ProvidesMiniMapInfo info)
			: base(info) { }
	}
}
