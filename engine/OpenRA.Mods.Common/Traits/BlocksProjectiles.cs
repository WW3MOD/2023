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

using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor blocks bullets and missiles with 'Blockable' property.")]
	public class BlocksProjectilesInfo : ConditionalTraitInfo, IBlocksProjectilesInfo
	{
		public readonly WDist Height = WDist.Zero;

		[Desc("Always bypass this many regardless of BypassChance.")]
		public readonly int MinBypass = 0;

		[Desc("Never bypass more actors than this.")]
		public readonly int MaxBypass = 0;

		[Desc("Chance of bypassing each blocking actor after the MinBypass is reached.")]
		public readonly int BypassChance = 100;

		[Desc("Determines what projectiles to block based on their allegiance to the wall owner.")]
		public readonly PlayerRelationship ExplodesOn = PlayerRelationship.Enemy;

		[Desc("Obstacles closer than this to the projectile source are automatically bypassed.",
			"Simulates the operator aiming around nearby obstacles. Default 4 cells.")]
		public static readonly int ProximityBypassRange = 4096;

		[Desc("Obstacles between ProximityBypassRange and this distance get a boosted bypass chance.",
			"Creates a smooth transition zone. Default 7 cells.")]
		public static readonly int ProximityFalloffRange = 7168;

		public WDist? HitShapeHeight;

		public override object Create(ActorInitializer init) { return new BlocksProjectiles(this); }
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

	public class BlocksProjectiles : ConditionalTrait<BlocksProjectilesInfo>, IBlocksProjectiles
	{
		// readonly BlocksProjectilesInfo info;
		public BlocksProjectiles(BlocksProjectilesInfo info)
			: base(info)
		{
			// this.info = info;
		}

		// The result of the expression is always 'true' since a value of type 'WDist' is never equal to 'null' of type 'WDist?'
		// WDist IBlocksProjectiles.BlockingHeight => Info.HitShapeHeight.Value;
		WDist IBlocksProjectiles.BlockingHeight => Info.HitShapeHeight != null ? Info.HitShapeHeight.Value : Info.Height;
		int IBlocksProjectiles.MaxBypass { get { return Info.MaxBypass; } }
		int IBlocksProjectiles.BypassChance { get { return Info.BypassChance; } }

		PlayerRelationship IBlocksProjectiles.ExplodesOn { get { return Info.ExplodesOn; } }

		public static bool AnyBlockingActorAt(World world, WPos pos)
		{
			var dat = world.Map.DistanceAboveTerrain(pos);

			return world.ActorMap.GetActorsAt(world.Map.CellContaining(pos))
				.Any(a => a.TraitsImplementing<IBlocksProjectiles>()
					.Where(t => t.BlockingHeight > dat)
					.Any(Exts.IsTraitEnabled));
		}

		public static bool AnyBlockingActorsBetween(Actor self, WPos end, WDist width, out WPos hit, bool checkRelationships = false, bool checkBypassChance = false)
		{
			return AnyBlockingActorsBetween(self.World, self.Owner, self.CenterPosition, end, width, out hit, self, checkRelationships, checkBypassChance, self.CenterPosition);
		}

		public static bool AnyBlockingActorsBetween(World world, Player owner, WPos start, WPos end, WDist width, out WPos hit, Actor self = null, bool checkRelationships = false, bool checkBypassChance = false, WPos? sourceOrigin = null)
		{
			var actors = world.FindBlockingActorsOnLine(start, end, width);
			var length = (end - start).Length;
			var totalBypassed = 0;

			foreach (var a in actors)
			{
				if (a == self)
					continue;

				var blockers = a.TraitsImplementing<IBlocksProjectiles>()
					.Where(Exts.IsTraitEnabled).Where(t => !checkRelationships || t.ExplodesOn.HasRelationship(a.Owner.RelationshipWith(owner)))
					.ToList();

				if (blockers.Count == 0)
					continue;

				var hitPos = WorldExtensions.MinimumPointLineProjection(start, end, a.CenterPosition);
				var dat = world.Map.DistanceAboveTerrain(hitPos);

				var isBlocking = blockers.Find(t => t.BlockingHeight > dat);

				// Proximity-based bypass: obstacles close to the source are easy to aim around.
				// An operator can line up a shot to avoid trees right next to them.
				if (isBlocking != null && sourceOrigin.HasValue)
				{
					var distFromSource = (hitPos - sourceOrigin.Value).HorizontalLength;

					// Within inner zone: always bypass (operator easily avoids nearby obstacles)
					if (distFromSource < BlocksProjectilesInfo.ProximityBypassRange)
					{
						totalBypassed += 1;
						continue;
					}

					// Transition zone: boosted bypass chance (linear falloff)
					if (distFromSource < BlocksProjectilesInfo.ProximityFalloffRange)
					{
						var falloffRange = BlocksProjectilesInfo.ProximityFalloffRange - BlocksProjectilesInfo.ProximityBypassRange;
						var distIntoFalloff = distFromSource - BlocksProjectilesInfo.ProximityBypassRange;

						// 100% bypass at inner edge, scales down to base BypassChance at outer edge
						var boostedChance = 100 - (100 - isBlocking.BypassChance) * distIntoFalloff / falloffRange;
						if (boostedChance >= 100 || boostedChance > world.SharedRandom.Next(100))
						{
							totalBypassed += 1;
							continue;
						}
					}
				}

				if ((isBlocking != null && isBlocking.MaxBypass > 0 && totalBypassed < isBlocking.MaxBypass)
					&& (!checkBypassChance || isBlocking.BypassChance == 100 || isBlocking.BypassChance > world.SharedRandom.Next(100)))
				{
					totalBypassed += 1;
					continue;
				}

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
