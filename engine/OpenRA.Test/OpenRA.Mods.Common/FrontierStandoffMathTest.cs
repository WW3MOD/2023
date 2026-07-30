#region Copyright & License Information
/*
 * WW3MOD influence stack — frontier standoff (@experimental) — rearward-push decision test.
 *
 * Pins the two coordinate-agnostic decisions both standoff consumers (the artillery echelon anchor and the
 * attack-heli standoff) share, so "standoff units hold BEHIND the believed front line" can't silently regress:
 *   (1) STEP — one coarse cell along the DOMINANT axis of the bearing (max-norm), so consecutive hops always
 *       land on DISTINCT coarse cells even on a diagonal (Euclidean scaling would under-advance and re-read).
 *   (2) ALREADY CLEAR — a point already past the minimum takes zero hops (⇒ unpopulated field is byte-identical).
 *   (3) WALK TO CLEAR — stops at the first hop whose sampled frontier distance reaches the minimum.
 *   (4) BOUNDED — the budget caps the walk.
 *   (5) ON-GRID — the walk halts at the grid boundary and NEVER returns an off-grid position.
 *   (6) DISABLED — minCells <= 0 or maxSteps <= 0 never pushes.
 * Pure integer stepping over synthetic samplers; no world mounted.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FrontierStandoffMathTest
	{
		const int CellSize = 2;
		const int Coarse = CellSize * 1024; // one coarse cell in WDist

		static WPos Pos(int x, int y) => new(x, y, 0);

		[Test]
		public void DiagonalStep_AdvancesADistinctCoarseCellEachHop()
		{
			// A 45° NE bearing. Max-norm scaling makes EACH axis advance one full coarse cell per hop, so no two
			// hops share a coarse grid cell (Euclidean scaling would advance only ~0.7 coarse cells/axis and
			// re-read the same cell every other hop).
			var step = FrontierStandoffMath.RearwardStep(new WVec(1024, 1024, 0), Coarse);
			Assert.That(step, Is.EqualTo(new WVec(Coarse, Coarse, 0)), "diagonal step advances one coarse cell per axis");

			var start = Pos(0, 0);
			var seen = new HashSet<(int, int)>();
			var last = (int.MinValue, int.MinValue);
			for (var i = 0; i <= 5; i++)
			{
				var p = start + new WVec(step.X * i, step.Y * i, 0);
				var cell = (p.X / Coarse, p.Y / Coarse);
				Assert.That(cell, Is.Not.EqualTo(last), $"hop {i} must land on a new coarse cell, not re-read the last");
				last = cell;
				seen.Add(cell);
			}

			Assert.That(seen.Count, Is.EqualTo(6), "six hops land on six distinct coarse cells");
		}

		[Test]
		public void ShallowStep_StillAdvancesTheDominantAxisEachHop()
		{
			// A shallow bearing (mostly east): the dominant axis still advances a full coarse cell per hop, so the
			// grid X changes every hop — distinct cells even where the minor axis barely moves.
			var step = FrontierStandoffMath.RearwardStep(new WVec(3072, 1024, 0), Coarse);
			Assert.That(step.X, Is.EqualTo(Coarse), "dominant (x) axis scaled to exactly one coarse cell");

			var lastGx = int.MinValue;
			for (var i = 0; i <= 4; i++)
			{
				var gx = (Pos(0, 0).X + step.X * i) / Coarse;
				Assert.That(gx, Is.Not.EqualTo(lastGx), $"hop {i} advances the dominant-axis coarse cell");
				lastGx = gx;
			}
		}

		[Test]
		public void AlreadyClear_TakesZeroSteps()
		{
			// The un-pushed point already reads at/above the minimum ⇒ no rearward walk.
			var steps = FrontierStandoffMath.RearwardSteps(Pos(0, 0), new WVec(Coarse, 0, 0),
				minCells: 4, maxSteps: 6, frontierAt: w => 10, onGrid: w => true);
			Assert.That(steps, Is.EqualTo(0), "a point already behind the front is not pushed");
		}

		[Test]
		public void WalksToTheFirstClearingStep()
		{
			// Frontier distance = the hop's grid X (grows one per coarse-cell hop east); clears the minimum of 4 at hop 4.
			var steps = FrontierStandoffMath.RearwardSteps(Pos(0, 0), new WVec(Coarse, 0, 0),
				minCells: 4, maxSteps: 10, frontierAt: w => w.X / Coarse, onGrid: w => true);
			Assert.That(steps, Is.EqualTo(4), "stops at the first hop whose sampled distance reaches the minimum");
		}

		[Test]
		public void Bounded_BudgetExhaustedReturnsMaxSteps()
		{
			// The axis never clears within the budget ⇒ push to the cap (the safe direction), never more.
			var steps = FrontierStandoffMath.RearwardSteps(Pos(0, 0), new WVec(Coarse, 0, 0),
				minCells: 4, maxSteps: 6, frontierAt: w => 0, onGrid: w => true);
			Assert.That(steps, Is.EqualTo(6), "an un-clearable axis returns the step budget, not more");
		}

		[Test]
		public void Walk_HaltsAtTheGridBoundary_NeverReturnsOffGrid()
		{
			// The frontier NEVER clears, so without the guard the walk would run to the budget and step off the
			// map. On-grid is X in [0, 3*Coarse) (coarse cells 0,1,2); the walk must halt at the last on-grid hop.
			var step = new WVec(Coarse, 0, 0);
			var start = Pos(0, 0);
			var steps = FrontierStandoffMath.RearwardSteps(start, step, minCells: 4, maxSteps: 10,
				frontierAt: w => 0, onGrid: w => w.X >= 0 && w.X < 3 * Coarse);

			Assert.That(steps, Is.EqualTo(2), "halts at the last on-grid hop (cells 0,1,2 reachable ⇒ 2 hops), not past the edge");

			var final = start + new WVec(step.X * steps, step.Y * steps, 0);
			Assert.That(final.X, Is.LessThan(3 * Coarse), "the returned position is on the playable area");
		}

		[Test]
		public void Disabled_NeverPushes()
		{
			Assert.Multiple(() =>
			{
				Assert.That(FrontierStandoffMath.RearwardSteps(Pos(0, 0), new WVec(Coarse, 0, 0),
					minCells: 0, maxSteps: 6, frontierAt: w => 0, onGrid: w => true), Is.EqualTo(0), "minCells <= 0 is off");
				Assert.That(FrontierStandoffMath.RearwardSteps(Pos(0, 0), new WVec(Coarse, 0, 0),
					minCells: 4, maxSteps: 0, frontierAt: w => 0, onGrid: w => true), Is.EqualTo(0), "a zero budget takes no hop");
				Assert.That(FrontierStandoffMath.RearwardStep(WVec.Zero, Coarse), Is.EqualTo(WVec.Zero),
					"a degenerate bearing yields no step");
			});
		}
	}
}
