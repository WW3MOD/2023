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

using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor blocks bullets and missiles with 'Blockable' property.")]
	public class BlocksSightInfo : ConditionalTraitInfo, IBlocksSightInfo
	{
		public readonly WDist Height = WDist.Zero;

		public readonly int Bypass = 0;

		[Desc("Determines what projectiles to block based on their allegiance to the wall owner.")]
		public readonly PlayerRelationship ExplodesOn = PlayerRelationship.Enemy;

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
			: base(info)
			{ }

		// The result of the expression is always 'true' since a value of type 'WDist' is never equal to 'null' of type 'WDist?'
		// WDist IBlocksSight.BlockingHeight => Info.HitShapeHeight.Value;
		WDist IBlocksSight.BlockingHeight => Info.HitShapeHeight != null ? Info.HitShapeHeight.Value : Info.Height;
		int IBlocksSight.Bypass { get { return Info.Bypass; } }

		PlayerRelationship IBlocksSight.ExplodesOn { get { return Info.ExplodesOn; } }

		public static bool AnyBlockingActorAt(World world, WPos pos)
		{
			var dat = world.Map.DistanceAboveTerrain(pos);

			return world.ActorMap.GetActorsAt(world.Map.CellContaining(pos))
				.Any(a => a.TraitsImplementing<IBlocksSight>()
					.Where(t => t.BlockingHeight > dat)
					.Any(Exts.IsTraitEnabled));
		}

		public static bool AnyBlockingActorsBetween(Actor self, WPos end, WDist width, out WPos hit)
		{
			return AnyBlockingActorsBetween(self.World, self.CenterPosition, end, width, out hit);
		}

		public static bool AnyBlockingActorsBetween(World world, WPos start, WPos end, WDist width, out WPos hit)
		{
			// var actors = world.FindBlockingActorsOnLine(start, end, width);
			var actors = world.FindActorsOnLine(start, end, width);
			var length = (end - start).Length;

			foreach (var a in actors)
			{
				var blockers = a.TraitsImplementing<IBlocksSight>()
					.ToList();

				if (blockers.Count == 0)
					continue;

				var hitPos = WorldExtensions.MinimumPointLineProjection(start, end, a.CenterPosition);
				var dat = world.Map.DistanceAboveTerrain(hitPos);

				var isBlocking = blockers.Find(t => t.BlockingHeight > dat);

				if ((hitPos - start).Length < length && blockers.Any(t => t.BlockingHeight > dat))
				{
					hit = hitPos;
					return true;
				}
			}

			hit = WPos.Zero;
			return false;
		}
	}
}
