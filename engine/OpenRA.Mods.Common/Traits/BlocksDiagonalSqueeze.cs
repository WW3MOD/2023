#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Tag trait for obstacles whose purpose is to stop vehicles. A full-cell mover cannot cut the diagonal corner ",
		"shared by two cells that each hold one of these, so a diagonal line of them is solid rather than porous. ",
		"Deliberately NOT put on terrain features — trees, hedges and rocks are natural cover and keep their diagonal ",
		"gaps. Read by Locomotor.CellBlocksCorner via CellFlag.HasDiagonalSqueezeBlocker.")]
	public class BlocksDiagonalSqueezeInfo : TraitInfo<BlocksDiagonalSqueeze> { }
	public class BlocksDiagonalSqueeze { }
}
