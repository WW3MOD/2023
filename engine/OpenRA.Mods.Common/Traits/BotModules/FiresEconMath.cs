#region Copyright & License Information
/*
 * WW3MOD fires economics (PIPELINE items 14 + 19) — pure scoring/gating math.
 *
 * Two doctrine halves, one clump kernel:
 *   (14) AoE-AWARE CLUSTER TARGETING — an area weapon should prefer the target whose SURROUNDING clump
 *        takes the most projected splash, not simply the closest. ClusterWeight is the splash fraction
 *        (0..100) a burst lands at a given distance; ClusterScore sums it over a candidate's neighbours;
 *        ClusterPriorityBonus turns that into a bounded, bucket-safe pull for AutoTarget's score.
 *   (19) AMMO EXPECTED-VALUE GATE — a fire mission must project more $ damage than the salvo's ammo costs,
 *        or it is money wasted. SalvoCost prices one volley from the economy model (Burst rounds at the
 *        pool's per-batch SupplyValue); ProjectedClumpValue is the splash-weighted enemy value destroyed;
 *        FireWorthy compares them. The quick gate the user asked for; a richer EV model can drop in behind
 *        the same three functions later.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer WDist math, long intermediates
 * so large maps / big clumps never overflow. Sums are order-independent (commutative), so a caller feeding
 * an unordered FindActorsInCircle result gets an identical score on every client. NUnit-pinned in
 * FiresEconMathTest. Engine-free (only WPos), so it is v3-portable like FiresStandoffMath.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class FiresEconMath
	{
		/// <summary>Projected splash fraction (0..100) a burst lands at horizontal distance <paramref name="dist"/>,
		/// for a weapon whose splash reaches <paramref name="radius"/> and decays along <paramref name="falloff"/>
		/// (percent at evenly spaced steps from 0 at the centre to radius at the last step). Linear interpolation
		/// between steps; 0 beyond the radius. Pure integer math.</summary>
		public static int ClusterWeight(int dist, int radius, int[] falloff)
		{
			if (falloff == null || falloff.Length == 0 || radius <= 0)
				return 0;

			if (dist <= 0)
				return falloff[0];

			if (dist >= radius)
				return falloff[falloff.Length - 1];

			// Map dist in [0, radius) onto the falloff steps. steps = number of intervals between samples.
			var steps = falloff.Length - 1;
			if (steps <= 0)
				return falloff[0];

			var scaled = dist * steps;          // in units of radius/steps
			var idx = scaled / radius;          // step index, guaranteed 0..steps-1 because dist < radius
			var lo = falloff[idx];
			var hi = falloff[idx + 1];
			var within = scaled - idx * radius; // 0..radius, position between the two samples
			return lo + (hi - lo) * within / radius;
		}

		/// <summary>Sum of <see cref="ClusterWeight"/> over every neighbour of <paramref name="aim"/> — the total
		/// projected splash a burst aimed at <paramref name="aim"/> lands on the clump. Callers pre-exclude the
		/// aim unit itself. Order-independent (a plain sum), so an unordered neighbour list is deterministic.</summary>
		public static int ClusterScore(WPos aim, IReadOnlyList<WPos> neighbours, int radius, int[] falloff)
		{
			if (neighbours == null)
				return 0;

			var score = 0;
			for (var i = 0; i < neighbours.Count; i++)
				score += ClusterWeight((neighbours[i] - aim).HorizontalLength, radius, falloff);

			return score;
		}

		/// <summary>The AutoTarget priority pull (a WDist length subtracted from a candidate's priorityValue) that
		/// a clump of the given <paramref name="clusterScore"/> earns: <paramref name="scalePerPoint"/> length per
		/// cluster point, capped at <paramref name="cap"/> so it can NEVER cross a priority bucket (the bonus lives
		/// inside the range tiebreak, exactly like the soft-overkill penalty). Non-positive inputs earn nothing, so
		/// a lone target (score 0) is byte-identical to the pre-cluster score.</summary>
		public static int ClusterPriorityBonus(int clusterScore, int scalePerPoint, int cap)
		{
			if (clusterScore <= 0 || scalePerPoint <= 0 || cap <= 0)
				return 0;

			var bonus = (long)clusterScore * scalePerPoint / 100;
			return bonus > cap ? cap : (int)bonus;
		}

		/// <summary>
		/// <para>The AutoTarget priority penalty (a WDist length ADDED to a candidate's priorityValue) a
		/// wounded target earns, so a healthy enemy is preferred over a damaged one: a target at
		/// <paramref name="healthPercent"/> of its max HP scores as though it stood
		/// <c>targetRange * (100 - healthPercent) / scale</c> further away. Linear in health, so it
		/// discriminates across the WHOLE legal band rather than only separating near-full from near-dead —
		/// at scale 100 a 60% target reads as 1.4x its range and a 40% target as 1.6x.</para>
		///
		/// <para>This is a PREFERENCE, never a filter: the penalty is bounded by <c>targetRange * 100 / scale</c>,
		/// which stays far inside the range tiebreak and can never cross a priority bucket, so a wounded
		/// unit that is the only thing in range is still chosen and finished off. Deprioritising is not
		/// abandoning — the separate <see cref="AutoTargetInfo.BreakOffCondition"/> skip owns that, and it
		/// only reaches units already below the critical line.</para>
		///
		/// <para>0 (byte-identical to no term) when disabled, at zero range, or against a full-health target.
		/// Health is clamped to 0..100 so an over-heal can never invert the sign into a bonus.</para>
		/// </summary>
		public static int HealthPreferencePenalty(int targetRange, int healthPercent, int scale)
		{
			if (scale <= 0 || targetRange <= 0)
				return 0;

			var hp = healthPercent < 0 ? 0 : (healthPercent > 100 ? 100 : healthPercent);
			var penalty = (long)targetRange * (100 - hp) / scale;
			return penalty > int.MaxValue ? int.MaxValue : (int)penalty;
		}

		/// <summary>Money one fire mission spends: <paramref name="burst"/> rounds priced at the ammo pool's
		/// per-batch <paramref name="supplyValue"/> (a batch is <paramref name="reloadCount"/> rounds), rounded UP
		/// to whole batches because a partial batch is still billed in full — mirrors the economy model's rearm/
		/// evac batch math (DOCS/reference/economy.md). 0 when the weapon has no priced ammo.</summary>
		public static int SalvoCost(int burst, int reloadCount, int supplyValue)
		{
			if (burst <= 0 || supplyValue <= 0)
				return 0;

			var batch = reloadCount > 0 ? reloadCount : 1;
			var batches = (burst + batch - 1) / batch;
			return batches * supplyValue;
		}

		/// <summary>One enemy in a projected clump: its build <see cref="Value"/>, the <see cref="DamagePercent"/>
		/// of its max HP the burst would remove, and its <see cref="Dist"/> from the aim point.</summary>
		public readonly struct ClumpTarget
		{
			public readonly int Value;
			public readonly int DamagePercent;
			public readonly int Dist;

			public ClumpTarget(int value, int damagePercent, int dist)
			{
				Value = value;
				DamagePercent = damagePercent;
				Dist = dist;
			}
		}

		/// <summary>Projected $ damage a salvo aimed with the given <paramref name="radius"/>/<paramref name="falloff"/>
		/// lands on a clump: each enemy's build value times the HP fraction the burst removes (capped at 100 — you
		/// cannot destroy more value than the unit holds) times the splash weight at its distance from the aim point.
		/// The EV numerator for <see cref="FireWorthy"/>. long accumulator — a big clump of costly units never
		/// overflows. Order-independent.</summary>
		public static long ProjectedClumpValue(IReadOnlyList<ClumpTarget> clump, int radius, int[] falloff)
		{
			if (clump == null)
				return 0;

			long total = 0;
			for (var i = 0; i < clump.Count; i++)
			{
				var t = clump[i];
				if (t.Value <= 0 || t.DamagePercent <= 0)
					continue;

				var dmg = t.DamagePercent > 100 ? 100 : t.DamagePercent;
				var weight = ClusterWeight(t.Dist, radius, falloff); // 0..100
				if (weight <= 0)
					continue;

				total += (long)t.Value * dmg * weight / 10000; // /100 for dmg%, /100 for weight%
			}

			return total;
		}

		/// <summary>Fire-worthiness: the projected damage value must beat the salvo's ammo cost by
		/// <paramref name="marginPercent"/> (100 = plain "cost &lt; value"; &gt;100 demands a surplus). A free / unpriced
		/// weapon (salvoCost 0) is always worthy. This is the standing north-star gate — a real EV model replaces
		/// the value estimate without touching this comparison.</summary>
		public static bool FireWorthy(long projectedValue, int salvoCost, int marginPercent)
		{
			if (salvoCost <= 0)
				return true;

			return projectedValue * 100 >= (long)salvoCost * marginPercent;
		}
	}
}
