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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Caches the shadows cast from actors with the BlocksSight trait.")]
	public class CastShadowsInfo : TraitInfo<CastShadows> { }

	public class CastShadows : IWorldLoaded
	{
		public void WorldLoaded(World world, WorldRenderer wr)
		{

		}
	}
}
