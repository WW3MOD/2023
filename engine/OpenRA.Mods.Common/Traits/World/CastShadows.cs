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
			// TODO: If fog enabled...

			// var map = world.Map;

			// var ShadowLayers = new CellLayer<CellLayer<byte>>(map);

			// var counter1 = 0;
			// var counter2 = 0;

			// foreach (var uv in map.AllCells.MapCoords)
			// {
			// 	counter1++;

			// 	var shadowLayer = new CellLayer<byte>(map);

			// 	foreach (var tile in map.FindTilesInAnnulus(uv.ToCPos(map), 24, 25, false)) // TODO 25/25 works? Test later
			// 	{
			// 		counter2++;

			// 		MPos mpos = tile.ToMPos(map);

			// 		if (BlocksSight.AnyBlockingActorsBetween(world, uv.ToWPos(map), mpos.ToWPos(map), new WDist(100), out WPos hit, null, false))
			// 		{
			// 			shadowLayer[mpos] = (byte)1;
			// 		}
			// 		else
			// 		{
			// 			shadowLayer[mpos] = (byte)2;
			// 		}

			// 	}

			// 	ShadowLayers[uv] = shadowLayer;
			// }

			// map.ShadowLayers = ShadowLayers;
		}
	}
}
