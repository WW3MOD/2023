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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor blocks bullets and missiles with 'Blockable' property.")]
	public class BlocksSightInfo : ConditionalTraitInfo, IBlocksSightInfo
	{
		public readonly WDist Height = WDist.Zero;

		[Desc("Percentage of sight looking through this actor should remove")]
		public readonly byte Density = 100;

		public WDist? HitShapeHeight;

		public override object Create(ActorInitializer init) { return new BlocksSight(this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (Height == WDist.Zero)
			{
				try
				{
					HitShapeInfo hitShape;

					if (ai.HasTraitInfo<HitShapeInfo>())
					{
						hitShape = ai.TraitInfos<HitShapeInfo>().First();

						if (hitShape != null)
							HitShapeHeight = hitShape.Type.VerticalTopOffset; // Set to fraction ?
					}
				}
				catch (System.Exception e)
				{
					throw new System.Exception("Test", e);
				}
			}
		}
	}

	public class BlocksSight : ConditionalTrait<BlocksSightInfo>, IBlocksSight
	{
		public BlocksSight(BlocksSightInfo info)
			: base(info) { }

		byte IBlocksSight.Density { get { return Info.Density; } }

		/* public static bool AnyBlockingActorAt(World world, WPos pos)
		{
			var dat = world.Map.DistanceAboveTerrain(pos);

			return world.ActorMap.GetActorsAt(world.Map.CellContaining(pos))
				.Any(a => a.TraitsImplementing<IBlocksSight>()
					.Where(t => t.BlockingHeight > dat)
					.Any(Exts.IsTraitEnabled));
		} */

		public static List<Actor> BlockingActorsBetween(Actor self, WPos end, WDist width)
		{
			return BlockingActorsBetween(self.World, self.CenterPosition, end, width);
		}

		public static List<Actor> BlockingActorsBetween(World world, WPos start, WPos end, WDist width)
		{
			var actors = world.FindActorsOnLine(start, end, width).ToList();

			return actors.Where(a => a.TraitsImplementing<IBlocksSight>().Count() > 0).ToList();
		}
	}
}
