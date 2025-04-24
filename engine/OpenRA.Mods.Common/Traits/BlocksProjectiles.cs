using System;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor blocks bullets and missiles with 'Blockable' property.")]
	public class BlocksProjectilesInfo : ConditionalTraitInfo, IBlocksProjectilesInfo
	{
		public readonly WDist Height = WDist.Zero;
		public readonly int MinBypass = 0;
		public readonly int MaxBypass = 0;
		public readonly int BypassChance = 100;
		public readonly PlayerRelationship ExplodesOn = PlayerRelationship.Enemy;
		public WDist? HitShapeHeight;

		public override object Create(ActorInitializer init) { return new BlocksProjectiles(this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (Height == WDist.Zero && ai.HasTraitInfo<HitShapeInfo>())
			{
				var hitShape = ai.TraitInfos<HitShapeInfo>().First();
				if (hitShape != null)
					HitShapeHeight = hitShape.Type.VerticalTopOffset;
			}
		}
	}

	public class BlocksProjectiles : ConditionalTrait<BlocksProjectilesInfo>, IBlocksProjectiles
	{
		public BlocksProjectiles(BlocksProjectilesInfo info) : base(info) { }

		WDist IBlocksProjectiles.BlockingHeight => Info.HitShapeHeight ?? Info.Height;
		int IBlocksProjectiles.MaxBypass => Info.MaxBypass;
		int IBlocksProjectiles.BypassChance => Info.BypassChance;
		PlayerRelationship IBlocksProjectiles.ExplodesOn => Info.ExplodesOn;

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
			return AnyBlockingActorsBetween(self.World, self.Owner, self.CenterPosition, end, width, out hit, self, checkRelationships, checkBypassChance);
		}

		public static bool AnyBlockingActorsBetween(World world, Player owner, WPos start, WPos end, WDist width, out WPos hit, Actor self = null, bool checkRelationships = false, bool checkBypassChance = false, int bypassModifier = 100, WPos originalSource = default, int targetDist = 0, int fullBypassDistance = 0)
		{
			var actors = world.FindBlockingActorsOnLine(start, end, width);
			var length = (end - start).Length;
			var totalBypassed = 0;
			var random = world.SharedRandom;
			var blockerFound = false;

			foreach (var a in actors)
			{
				if (a == self) continue;

				var blockers = a.TraitsImplementing<IBlocksProjectiles>()
					.Where(Exts.IsTraitEnabled)
					.Where(t => !checkRelationships || t.ExplodesOn.HasRelationship(a.Owner.RelationshipWith(owner)))
					.ToList();

				if (blockers.Count == 0) continue;

				var hitPos = WorldExtensions.MinimumPointLineProjection(start, end, a.CenterPosition);
				var dat = world.Map.DistanceAboveTerrain(hitPos);
				var isBlocking = blockers.Find(t => t.BlockingHeight > dat);

				if (isBlocking != null)
				{
					blockerFound = true;
					var effectiveBypassChance = 0;
					if (checkBypassChance)
					{
						var d = (hitPos - originalSource).Length;
						if (d <= fullBypassDistance)
							effectiveBypassChance = 100;
						else if (d >= targetDist)
							effectiveBypassChance = isBlocking.BypassChance;
						else
						{
							var delta = (isBlocking.BypassChance - 100) * (d - fullBypassDistance);
							var denom = targetDist - fullBypassDistance;
							effectiveBypassChance = 100 + delta / denom;
						}

						effectiveBypassChance = Math.Max(0, Math.Min(100, effectiveBypassChance * bypassModifier / 100));
						Console.WriteLine($"Blocker at {hitPos}: Distance={d}, EffectiveBypassChance={effectiveBypassChance}, MaxBypass={isBlocking.MaxBypass}, TotalBypassed={totalBypassed}");
					}

					if (isBlocking.MaxBypass > 0 && totalBypassed < isBlocking.MaxBypass && (!checkBypassChance || effectiveBypassChance >= 100 || effectiveBypassChance > random.Next(100)))
					{
						totalBypassed++;
						Console.WriteLine($"Bypassed blocker at {hitPos}, TotalBypassed={totalBypassed}");
						continue;
					}

					if ((hitPos - start).Length < length && blockers.Any(t => t.BlockingHeight > dat))
					{
						hit = hitPos;
						Console.WriteLine($"Blocked at {hitPos}: Projectile stopped");
						return true;
					}
				}
			}

			hit = WPos.Zero;
			if (!blockerFound)
				Console.WriteLine("No blockers found in projectile path");
			return false;
		}

		public static int GetCumulativeBypassProbability(World world, Player owner, WPos start, WPos end, WDist width, Actor self = null, bool checkRelationships = false)
		{
			var actors = world.FindBlockingActorsOnLine(start, end, width);
			var cumulativeBypass = 100;
			var blockerFound = false;

			foreach (var a in actors)
			{
				if (a == self) continue;

				var blockers = a.TraitsImplementing<IBlocksProjectiles>()
					.Where(Exts.IsTraitEnabled)
					.Where(t => !checkRelationships || t.ExplodesOn.HasRelationship(a.Owner.RelationshipWith(owner)))
					.ToList();

				if (blockers.Count == 0) continue;

				var hitPos = WorldExtensions.MinimumPointLineProjection(start, end, a.CenterPosition);
				var dat = world.Map.DistanceAboveTerrain(hitPos);
				var isBlocking = blockers.Find(t => t.BlockingHeight > dat);

				if (isBlocking != null)
				{
					blockerFound = true;
					var bypassChance = isBlocking.BypassChance;
					cumulativeBypass = (cumulativeBypass * bypassChance) / 100;
					Console.WriteLine($"Blocker at {hitPos}: BypassChance={bypassChance}, CumulativeBypass={cumulativeBypass}");
					if (cumulativeBypass == 0) break;
				}
			}

			if (!blockerFound)
				Console.WriteLine("No blockers affecting cumulative bypass");
			return cumulativeBypass;
		}
	}
}
