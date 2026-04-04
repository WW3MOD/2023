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
using OpenRA.GameRules;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// ShadowLayer-based line-of-sight check for targeting.
	/// Uses pre-cached shadow data for O(1) per-unit LOS lookups.
	/// Replaces the expensive AnyBlockingActorsBetween check for targeting decisions.
	/// Projectile-in-flight collision still uses BlocksProjectiles separately.
	/// </summary>
	public static class FiringLOS
	{
		/// <summary>
		/// Check if a unit has clear enough line of sight to fire at a target position.
		/// Uses the pre-computed ShadowLayer for fast O(1) lookups.
		/// </summary>
		/// <param name="self">The attacking actor.</param>
		/// <param name="targetPos">The target's world position.</param>
		/// <param name="threshold">Maximum shadow value that allows firing (from weapon's ClearSightThreshold).</param>
		/// <returns>True if the unit can fire at the target.</returns>
		public static bool HasClearLOS(Actor self, WPos targetPos, byte threshold)
		{
			// IndirectFire units always have clear LOS (artillery, mortars)
			var indirectFire = self.TraitOrDefault<IndirectFire>();
			if (indirectFire != null && !indirectFire.IsTraitDisabled)
				return true;

			var map = self.World.Map;

			// No shadow data available — allow targeting
			if (map.ShadowLayer == null)
				return true;

			var fromCell = map.CellContaining(self.CenterPosition);
			var toCell = map.CellContaining(targetPos);

			// Same cell — always clear
			if (fromCell == toCell)
				return true;

			var fromMPos = fromCell.ToMPos(map);
			var toMPos = toCell.ToMPos(map);

			// Distance check — ShadowLayer is computed for 2-32 cell range
			var dx = fromMPos.U - toMPos.U;
			var dy = fromMPos.V - toMPos.V;
			var distSq = dx * dx + dy * dy;

			// Within 2 cells — no shadow data, assume clear (can see anything nearby)
			if (distSq < 4) // 2*2
				return true;

			// Beyond 32 cells — no shadow data, fall back to BlocksProjectiles check
			if (distSq > 1024) // 32*32
				return !BlocksProjectiles.AnyBlockingActorsBetween(self, targetPos, new WDist(1), out _);

			// Bounds check — ensure cells are within map
			var shadowFromCell = map.ShadowLayer[fromMPos];
			if (shadowFromCell == null)
				return true;

			if (!shadowFromCell.Contains(toMPos))
				return true;

			// Look up pre-computed shadow value
			var (groundShadow, airborneShadow) = shadowFromCell[toMPos];

			// Aircraft use airborne shadow channel (accounts for altitude, much lower values)
			var isAirborne = self.TraitsImplementing<IAirborneVisibility>()
				.Any(t => t.IsAirborne);

			var shadow = isAirborne ? airborneShadow : groundShadow;

			return shadow <= threshold;
		}

		/// <summary>
		/// Get the best (lowest) ClearSightThreshold from a unit's active armaments that can fire at the target.
		/// </summary>
		public static byte GetBestThreshold(Actor self, in Target target)
		{
			byte best = 0;
			foreach (var ab in self.TraitsImplementing<AttackBase>())
			{
				if (ab.IsTraitDisabled || ab.IsTraitPaused)
					continue;

				foreach (var arm in ab.Armaments)
				{
					if (arm.IsTraitDisabled || arm.IsTraitPaused)
						continue;

					if (!arm.Weapon.IsValidAgainst(target, self.World, self))
						continue;

					if (arm.Weapon.ClearSightThreshold > best)
						best = arm.Weapon.ClearSightThreshold;
				}
			}

			return best;
		}
	}
}
