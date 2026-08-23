#region Copyright & License Information
/*
 * WW3MOD fires economics (PIPELINE items 14 + 19) — pure scoring/gating test.
 *
 * Pins the arithmetic AutoTarget's cluster term (item 14) and PoiOffensive's rocket EV gate (item 19)
 * turn into a target choice / hold-fire decision, so neither can silently regress:
 *   (1) CLUSTER WEIGHT   — the splash falloff kernel: centre = falloff[0], edge = falloff[last], linear between,
 *                          0 beyond the radius, and degenerate-input guards.
 *   (2) CLUSTER SCORE    — a plain (order-independent) sum of weights over a candidate's neighbours.
 *   (3) PRIORITY BONUS   — scaled + hard-capped so a clump can never cross an AutoTarget priority bucket.
 *   (4) SALVO COST       — Burst rounds priced in whole batches (the real Grad/Paladin/M270 numbers).
 *   (5) PROJECTED VALUE  — splash-weighted, damage-capped enemy value destroyed.
 *   (6) FIRE-WORTHY      — cost-vs-value with the margin, and the free-weapon short circuit.
 *   (7) DETERMINISM      — reordered neighbours give the identical score.
 * Pure math over synthetic inputs; no world mounted.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FiresEconMathTest
	{
		const int Cell = 1024;

		static WPos Pos(int xCells, int yCells) => new(xCells * Cell, yCells * Cell, 0);

		// A symmetric cone: 100% at the centre, 0% at the radius, linear between.
		static readonly int[] Cone = { 100, 0 };

		// ---- (1) ClusterWeight ---------------------------------------------------------------------------

		[Test]
		public void ClusterWeight_CentreEdgeAndBeyond()
		{
			var falloff = new[] { 100, 40, 0 };
			var radius = 4 * Cell;

			Assert.That(FiresEconMath.ClusterWeight(0, radius, falloff), Is.EqualTo(100), "centre = falloff[0]");
			Assert.That(FiresEconMath.ClusterWeight(radius, radius, falloff), Is.EqualTo(0), "edge = falloff[last]");
			Assert.That(FiresEconMath.ClusterWeight(radius + 1, radius, falloff), Is.EqualTo(0), "beyond radius = 0");

			// Midpoint of the FIRST interval (0..2c maps to falloff[0]..falloff[1]): halfway = (100+40)/2 = 70.
			Assert.That(FiresEconMath.ClusterWeight(Cell, radius, falloff), Is.EqualTo(70), "linear interp, first interval");

			// Midpoint of the SECOND interval (2c..4c maps 40..0): at 3c halfway = 20.
			Assert.That(FiresEconMath.ClusterWeight(3 * Cell, radius, falloff), Is.EqualTo(20), "linear interp, second interval");
		}

		[Test]
		public void ClusterWeight_DegenerateInputs()
		{
			Assert.That(FiresEconMath.ClusterWeight(Cell, 0, Cone), Is.EqualTo(0), "zero radius earns nothing");
			Assert.That(FiresEconMath.ClusterWeight(Cell, 4 * Cell, null), Is.EqualTo(0), "null falloff earns nothing");
			Assert.That(FiresEconMath.ClusterWeight(Cell, 4 * Cell, new int[0]), Is.EqualTo(0), "empty falloff earns nothing");
			Assert.That(FiresEconMath.ClusterWeight(0, 4 * Cell, new[] { 100 }), Is.EqualTo(100), "single-sample falloff = its value");
		}

		// ---- (2) ClusterScore + (7) determinism ----------------------------------------------------------

		[Test]
		public void ClusterScore_SumsNeighbours_AndIsOrderIndependent()
		{
			var radius = 4 * Cell;
			var aim = Pos(0, 0);

			// Three neighbours at 0c (100), 2c (50), 4c (0).
			var a = new List<WPos> { Pos(0, 0), Pos(2, 0), Pos(4, 0) };
			var b = new List<WPos> { Pos(4, 0), Pos(0, 0), Pos(2, 0) }; // same set, shuffled

			var scoreA = FiresEconMath.ClusterScore(aim, a, radius, Cone);
			var scoreB = FiresEconMath.ClusterScore(aim, b, radius, Cone);

			Assert.That(scoreA, Is.EqualTo(150), "100 + 50 + 0");
			Assert.That(scoreB, Is.EqualTo(scoreA), "sum is order-independent (determinism)");
			Assert.That(FiresEconMath.ClusterScore(aim, null, radius, Cone), Is.EqualTo(0), "null list = 0");
		}

		// ---- (3) ClusterPriorityBonus -------------------------------------------------------------------

		[Test]
		public void ClusterPriorityBonus_ScalesAndCaps()
		{
			// scale 96 per 100 points: score 150 -> 144 uncapped.
			Assert.That(FiresEconMath.ClusterPriorityBonus(150, 96, 24 * Cell), Is.EqualTo(144), "150 * 96 / 100");

			// A big clump caps at ClusterMaxBonus so it never crosses a priority bucket.
			Assert.That(FiresEconMath.ClusterPriorityBonus(1_000_000, 96, 24 * Cell), Is.EqualTo(24 * Cell), "capped");

			Assert.That(FiresEconMath.ClusterPriorityBonus(0, 96, 24 * Cell), Is.EqualTo(0), "a lone target earns nothing");
			Assert.That(FiresEconMath.ClusterPriorityBonus(150, 0, 24 * Cell), Is.EqualTo(0), "zero scale = off");
		}

		// ---- (4) SalvoCost --------------------------------------------------------------------------------

		[Test]
		public void SalvoCost_PricesWholeBatches()
		{
			// Real WW3MOD arty pools (economy.md batch math): batches = ceil(Burst / ReloadCount), cost = batches * SupplyValue.
			Assert.That(FiresEconMath.SalvoCost(40, 5, 85), Is.EqualTo(680), "Grad: 8 batches * 85");
			Assert.That(FiresEconMath.SalvoCost(24, 3, 120), Is.EqualTo(960), "TOS: 8 batches * 120");
			Assert.That(FiresEconMath.SalvoCost(12, 1, 70), Is.EqualTo(840), "M270: 12 batches * 70");
			Assert.That(FiresEconMath.SalvoCost(3, 5, 60), Is.EqualTo(60), "Paladin: partial batch billed as 1 * 60");
			Assert.That(FiresEconMath.SalvoCost(1, 5, 60), Is.EqualTo(60), "Giatsint: 1 round still 1 batch * 60");

			Assert.That(FiresEconMath.SalvoCost(0, 5, 60), Is.EqualTo(0), "no burst = no cost");
			Assert.That(FiresEconMath.SalvoCost(10, 5, 0), Is.EqualTo(0), "unpriced ammo = no cost");
		}

		// ---- (5) ProjectedClumpValue --------------------------------------------------------------------

		[Test]
		public void ProjectedClumpValue_WeightsDamageAndValue()
		{
			var radius = 4 * Cell;

			// One unit at the centre: full value * full damage * full weight.
			var single = new List<FiresEconMath.ClumpTarget> { new(100, 100, 0) };
			Assert.That(FiresEconMath.ProjectedClumpValue(single, radius, Cone), Is.EqualTo(100), "cost 100 * 100% dmg * 100% weight");

			// Damage caps at 100 (can't destroy more value than the unit holds).
			var over = new List<FiresEconMath.ClumpTarget> { new(100, 250, 0) };
			Assert.That(FiresEconMath.ProjectedClumpValue(over, radius, Cone), Is.EqualTo(100), "damage% capped at 100");

			// Half damage OR half splash weight both halve the contribution.
			var halfDmg = new List<FiresEconMath.ClumpTarget> { new(100, 50, 0) };
			Assert.That(FiresEconMath.ProjectedClumpValue(halfDmg, radius, Cone), Is.EqualTo(50), "50% damage");
			var halfWeight = new List<FiresEconMath.ClumpTarget> { new(100, 100, 2 * Cell) }; // Cone at 2c = 50%
			Assert.That(FiresEconMath.ProjectedClumpValue(halfWeight, radius, Cone), Is.EqualTo(50), "50% splash weight");

			// A clump sums; a unit at the radius edge contributes nothing.
			var clump = new List<FiresEconMath.ClumpTarget> { new(100, 100, 0), new(200, 100, 2 * Cell), new(500, 100, 4 * Cell) };
			Assert.That(FiresEconMath.ProjectedClumpValue(clump, radius, Cone), Is.EqualTo(100 + 100), "100 + 200*50% + 500*0%");
		}

		// ---- (6) FireWorthy -------------------------------------------------------------------------------

		[Test]
		public void FireWorthy_ComparesValueToCostWithMargin()
		{
			// A lone cheap target ($100) never repays a Grad salvo ($680) — hold fire.
			Assert.That(FiresEconMath.FireWorthy(100, 680, 100), Is.False, "lone cheap target: not worthy");

			// A worthy clump ($700 projected) beats the salvo at margin 100 (cost < value).
			Assert.That(FiresEconMath.FireWorthy(700, 680, 100), Is.True, "worthy clump: fire");

			// Exactly equal passes at margin 100 (>=).
			Assert.That(FiresEconMath.FireWorthy(680, 680, 100), Is.True, "value == cost passes at margin 100");

			// A stricter margin (150) demands a 1.5x surplus.
			Assert.That(FiresEconMath.FireWorthy(700, 680, 150), Is.False, "margin 150 not met at 700");
			Assert.That(FiresEconMath.FireWorthy(1100, 680, 150), Is.True, "margin 150 met at 1100");

			// A free / unpriced weapon is always worthy (no gate).
			Assert.That(FiresEconMath.FireWorthy(0, 0, 100), Is.True, "zero salvo cost = always worthy");
		}

		// ---- (8) HealthPreferencePenalty ------------------------------------------------------------------

		[Test]
		public void HealthPreferencePenalty_IsProportionalAcrossTheWholeBand()
		{
			const int Scale = 100;

			// An undamaged target is never penalised, so a field with no wounded in it scores exactly as
			// it did before this term existed.
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 100, Scale), Is.EqualTo(0),
				"a full-health target must carry no penalty at all");

			// The two values the ruling singled out. This is the band that matters: above the critical
			// fence (25%) so they are still legal targets, and — once the infantry fire/move gates move
			// to 50% — 40% is a disarmed, stationary unit that a healthy one should outrank.
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 60, Scale), Is.EqualTo(409),
				"60% HP at 1 cell must score as ~1.4 cells (1024 * 40 / 100)");
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 40, Scale), Is.EqualTo(614),
				"40% HP at 1 cell must score as ~1.6 cells (1024 * 60 / 100)");

			// The point of the whole exercise: mid-band must DISCRIMINATE, not collapse to one step. A
			// term that only separated near-full from near-dead could still satisfy the two asserts
			// above by bucketing; this pins that 60% and 40% are genuinely different scores.
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 40, Scale),
				Is.GreaterThan(FiresEconMath.HealthPreferencePenalty(Cell, 60, Scale)),
				"a 40% target must be deprioritised strictly more than a 60% one");

			// Strictly monotonic the whole way down the legal band, one sample per damage state.
			var prev = -1;
			foreach (var hp in new[] { 100, 75, 60, 50, 40, 26 })
			{
				var penalty = FiresEconMath.HealthPreferencePenalty(Cell, hp, Scale);
				Assert.That(penalty, Is.GreaterThan(prev),
					$"penalty must rise monotonically as health falls (broke at {hp}% HP)");
				prev = penalty;
			}

			// Bounded: even a 0% HP target (reachable only where the critical-damage grant was removed,
			// e.g. civilians) can never earn more than one range-length of penalty at Scale 100, which
			// keeps this WELL inside the range tiebreak and nowhere near the priority bucket.
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 0, Scale), Is.EqualTo(Cell),
				"worst case at Scale 100 is exactly one extra range-length");
		}

		[Test]
		public void HealthPreferencePenalty_GuardsDegenerateInput()
		{
			// Disabled by scale — the off switch must be byte-identical to no term at all.
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 30, 0), Is.EqualTo(0), "scale 0 = off");
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 30, -1), Is.EqualTo(0), "negative scale = off");

			// A target on top of the shooter has no range to inflate, so no penalty either.
			Assert.That(FiresEconMath.HealthPreferencePenalty(0, 30, 100), Is.EqualTo(0), "zero range = no penalty");

			// Health outside 0..100 is clamped rather than allowed to flip the sign of the term — an
			// over-heal (>100) must not turn the penalty into a BONUS that outranks a full-health unit.
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, 150, 100), Is.EqualTo(0), "over-heal clamps to 0 penalty");
			Assert.That(FiresEconMath.HealthPreferencePenalty(Cell, -5, 100), Is.EqualTo(Cell), "negative HP clamps to the 0% penalty");

			// Long intermediate: a far target on a large map must not overflow int32 mid-multiply.
			Assert.That(FiresEconMath.HealthPreferencePenalty(int.MaxValue, 0, 100), Is.GreaterThan(0),
				"a huge range must not overflow to a negative penalty");
		}
	}
}
