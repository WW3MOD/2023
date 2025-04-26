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

		/// <summary>
		/// Determines if any actors with IBlocksProjectiles trait block a projectile's path from the actor's position to a target position.
		/// This is a convenience overload that uses the actor's world, owner, and center position as defaults.
		/// </summary>
		/// <param name="self">The actor firing the projectile.</param>
		/// <param name="end">The target position of the projectile.</param>
		/// <param name="width">The width of the projectile's path (e.g., 1c0 for one cell).</param>
		/// <param name="hit">Output parameter for the position where the projectile is blocked, if any.</param>
		/// <param name="checkRelationships">If true, only blocks if the blocker's owner has the required relationship (e.g., Enemy).</param>
		/// <param name="checkBypassChance">If true, applies bypass chance logic to potentially ignore blockers.</param>
		/// <returns>True if a blocking actor is found and not bypassed, false otherwise.</returns>
		public static bool AnyBlockingActorsBetween(Actor self, WPos end, WDist width, out WPos hit, bool checkRelationships = false, bool checkBypassChance = false)
		{
			// Delegate to the main method, passing the actor's world, owner, and center position
			return AnyBlockingActorsBetween(self.World, self.Owner, self.CenterPosition, end, width, out hit, self, checkRelationships, checkBypassChance);
		}

		// Improvement: Consider caching the self.World and self.Owner references if this method is called frequently in a tight loop to reduce property access overhead.
		// Improvement: Add a check for invalid parameters (e.g., null self or zero width) to prevent edge-case errors and improve robustness.

		/// <summary>
		/// Checks for actors with IBlocksProjectiles trait that block a projectile's path between two positions.
		/// Handles bypass logic based on distance, chance, and maximum bypass limits, used during projectile flight.
		/// </summary>
		/// <param name="world">The game world containing actors and map data.</param>
		/// <param name="owner">The player owning the firing actor.</param>
		/// <param name="start">The starting position of the projectile (e.g., shooter's center).</param>
		/// <param name="end">The target position of the projectile.</param>
		/// <param name="width">The width of the projectile's path to check for blockers.</param>
		/// <param name="hit">Output parameter for the position where the projectile is blocked, if any.</param>
		/// <param name="self">The firing actor, excluded from blocking checks.</param>
		/// <param name="checkRelationships">If true, only blocks if the blocker's owner has the required relationship.</param>
		/// <param name="checkBypassChance">If true, applies bypass chance logic to potentially ignore blockers.</param>
		/// <param name="bypassModifier">Multiplies the effective bypass chance (default 100).</param>
		/// <param name="originalSource">The original source position for distance calculations.</param>
		/// <param name="targetDist">The total distance to the target for bypass chance interpolation.</param>
		/// <param name="fullBypassDistance">Distance within which blockers are always bypassed.</param>
		/// <returns>True if a blocking actor stops the projectile, false if no blockers or all are bypassed.</returns>
		public static bool AnyBlockingActorsBetween(World world, Player owner, WPos start, WPos end, WDist width, out WPos hit, Actor self = null, bool checkRelationships = false, bool checkBypassChance = false, int bypassModifier = 100, WPos originalSource = default, int targetDist = 0, int fullBypassDistance = 0)
		{
			// Find all actors along the projectile's path within the specified width
			var actors = world.FindBlockingActorsOnLine(start, end, width);

			// Calculate the total length of the projectile's path for distance comparisons
			var length = (end - start).Length;

			// Track the number of blockers bypassed
			var totalBypassed = 0;

			// Use a shared random number generator for bypass chance decisions
			var random = world.SharedRandom;

			// Flag to indicate if any blockers were found (for logging purposes)
			var blockerFound = false;

			// Iterate through each actor in the path
			foreach (var a in actors)
			{
				// Skip the firing actor itself to avoid self-blocking
				if (a == self) continue;

				// Get all IBlocksProjectiles traits on the actor that are enabled and match relationship criteria
				var blockers = a.TraitsImplementing<IBlocksProjectiles>()
					.Where(Exts.IsTraitEnabled)
					.Where(t => !checkRelationships || t.ExplodesOn.HasRelationship(a.Owner.RelationshipWith(owner)))
					.ToList();

				// If no valid blockers are found on this actor, skip to the next
				if (blockers.Count == 0) continue;

				// Calculate the closest point on the projectile's path to the actor's center
				var hitPos = WorldExtensions.MinimumPointLineProjection(start, end, a.CenterPosition);

				// Get the terrain height at the hit position to compare with blocker height
				var dat = world.Map.DistanceAboveTerrain(hitPos);

				// Find the first blocker whose height exceeds the terrain distance
				var isBlocking = blockers.Find(t => t.BlockingHeight > dat);

				// If a blocking trait is found, process it
				if (isBlocking != null)
				{
					// Mark that a blocker was found for logging
					blockerFound = true;

					// Initialize the effective bypass chance
					var effectiveBypassChance = 0;

					// Calculate bypass chance if enabled
					if (checkBypassChance)
					{
						// Calculate the distance from the original source to the blocker
						var d = (hitPos - originalSource).Length;

						// Determine bypass chance based on distance
						if (d <= fullBypassDistance)
						{
							// Within full bypass distance, always bypass
							effectiveBypassChance = 100;
						}
						else if (d >= targetDist)
						{
							// Beyond target distance, use the blocker's base bypass chance
							effectiveBypassChance = isBlocking.BypassChance;
						}
						else
						{
							// Interpolate bypass chance between full bypass and base chance
							var delta = (isBlocking.BypassChance - 100) * (d - fullBypassDistance);
							var denom = targetDist - fullBypassDistance;
							effectiveBypassChance = 100 + delta / denom;
						}

						// Apply bypass modifier and clamp the value between 0 and 100
						effectiveBypassChance = Math.Max(0, Math.Min(100, effectiveBypassChance * bypassModifier / 100));

						// Log the blocker's details for debugging
						Console.WriteLine($"Blocker at {hitPos}: Distance={d}, EffectiveBypassChance={effectiveBypassChance}, MaxBypass={isBlocking.MaxBypass}, TotalBypassed={totalBypassed}");
					}

					// Check if the blocker can be bypassed
					if (isBlocking.MaxBypass > 0 && totalBypassed < isBlocking.MaxBypass && (!checkBypassChance || effectiveBypassChance >= 100 || effectiveBypassChance > random.Next(100)))
					{
						// Increment the bypass count and log the bypass
						totalBypassed++;
						Console.WriteLine($"Bypassed blocker at {hitPos}, TotalBypassed={totalBypassed}");
						continue;
					}

					// If the blocker is before the target and not bypassed, stop the projectile
					if ((hitPos - start).Length < length && blockers.Any(t => t.BlockingHeight > dat))
					{
						// Set the hit position and log the stop
						hit = hitPos;
						Console.WriteLine($"Blocked at {hitPos}: Projectile stopped");
						return true;
					}
				}
			}

			// No blockers stop the projectile; set hit to zero
			hit = WPos.Zero;

			// Log if no blockers were found
			if (!blockerFound)
				Console.WriteLine("No blockers found in projectile path");

			// Return false as no blockers prevent the projectile
			return false;
		}

		// Improvement: Optimize performance by early-exiting the loop once MaxBypass is reached or a non-bypassable blocker is found, reducing unnecessary iterations.
		// Improvement: Consider adding a spatial index or pre-filtering actors to reduce the number of actors checked, especially on large maps with many trees.
		// Improvement: Add support for partial blocking (e.g., reducing projectile damage or speed) to enhance realism, allowing for more complex tactical scenarios.
		// Improvement: Refactor the bypass chance calculation into a separate method for better readability and reusability across other projectile-related logic.
		// Improvement: Introduce logging levels (e.g., debug, info) to control verbosity, making it easier to toggle detailed logs in production builds.
		// Improvement: Add unit tests to verify edge cases, such as zero-width projectiles, negative distances, or invalid actor states, to ensure robustness.
		// Improvement: Consider adding a parameter to limit the number of blockers checked (e.g., maxBlockersToCheck) to prevent performance issues with dense obstacle fields.
		// Improvement: Enhance the bypass chance interpolation with a non-linear curve (e.g., exponential decay) for more realistic projectile behavior near the shooter.
		// Improvement: Add support for dynamic blocker properties (e.g., temporary buffs that increase MaxBypass) to allow for mod-specific gameplay mechanics like terrain effects.
		// ...
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
