#region Copyright & License Information
/*
 * WW3MOD transport drop-site risk/reachability scoring test.
 *
 * Pins TransportDropSiteMath — the fog-legal reshaping that stops the heli transport dropping infantry
 * deep behind the enemy Supply Route. The frozen picker always took the single omniscient WEAKEST enemy
 * cell (a lone cheap unit behind the enemy SR reads "weakest"), landing the drop somewhere unreachable
 * and lethal. The scorer instead penalises believed-enemy control DEPTH (ControlField), believed danger
 * (DangerFieldLayer) and distance from our OWN SR — belief-side / own-side inputs only, never a
 * ground-truth enemy position. A weight, not a filter: nothing is hard-banned. Pure integer math,
 * deterministic, zero RNG.
 *
 * Headline: ReachableFlankPoi_OutranksDeepBehindEnemySrCell.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TransportDropSiteMathTest
	{
		// The @experimental tuning (ai.yaml): control/danger weights x100, reach 5/cell.
		const int ControlW = 100;
		const int DangerW = 100;
		const int ReachW = 5;

		static int Score(int control, int ground, int air, int reach)
			=> TransportDropSiteMath.ScoreDrop(control, ground, air, reach, ControlW, DangerW, ReachW);

		// ---------- Chebyshev cell distance ----------

		[Test]
		public void CellDistance_IsChebyshev()
		{
			Assert.That(TransportDropSiteMath.CellDistance(new CPos(0, 0), new CPos(4, 4)), Is.EqualTo(4));
			Assert.That(TransportDropSiteMath.CellDistance(new CPos(3, 1), new CPos(3, 9)), Is.EqualTo(8));
			Assert.That(TransportDropSiteMath.CellDistance(new CPos(5, 5), new CPos(5, 5)), Is.EqualTo(0));
		}

		// ---------- individual penalty terms ----------

		[Test]
		public void AllZero_ScoresZero()
		{
			// No belief data, at our SR: nothing to penalise ⇒ the neutral 0 baseline.
			Assert.That(Score(0, 0, 0, 0), Is.EqualTo(0));
		}

		[Test]
		public void BelievedOursOrContested_AddsNoControlPenalty()
		{
			// Positive control (believed ours) contributes NO enemy-depth penalty — only the negative magnitude
			// is charged. A believed-ours cell at our SR still scores the neutral 0.
			Assert.That(Score(800, 0, 0, 0), Is.EqualTo(0));
			Assert.That(Score(1, 0, 0, 0), Is.EqualTo(0));
		}

		[Test]
		public void DeepBelievedEnemyControl_IsHeavilyPenalised()
		{
			// Behind the enemy SR anchor the control field floors to ~-800 ⇒ -800 * 100/100 = -800.
			Assert.That(Score(-800, 0, 0, 0), Is.EqualTo(-800));
			// Shallower enemy territory is a smaller penalty (monotone in depth).
			Assert.That(Score(-200, 0, 0, 0), Is.EqualTo(-200));
			Assert.That(Score(-800, 0, 0, 0), Is.LessThan(Score(-200, 0, 0, 0)));
		}

		[Test]
		public void Danger_GroundAndAir_BothPenalise()
		{
			// Ground + air danger sum, x DangerW/100.
			Assert.That(Score(0, 40, 0, 0), Is.EqualTo(-40));
			Assert.That(Score(0, 0, 30, 0), Is.EqualTo(-30));
			Assert.That(Score(0, 40, 30, 0), Is.EqualTo(-70));
		}

		[Test]
		public void Reach_PenalisesPerCellFromOwnSr()
		{
			// 12 cells from our SR at 5/cell = -60.
			Assert.That(Score(0, 0, 0, 12), Is.EqualTo(-60));
			// Farther is strictly worse.
			Assert.That(Score(0, 0, 0, 30), Is.LessThan(Score(0, 0, 0, 12)));
		}

		[Test]
		public void NegativeDangerAndReach_AreClampedToZero()
		{
			// Defensive: a field/metric can never REWARD a candidate by reading negative.
			Assert.That(Score(0, -50, -50, -10), Is.EqualTo(0));
		}

		[Test]
		public void Weights_ScaleLinearly()
		{
			// Control weight 50% halves the enemy-depth penalty; reach weight 0 removes the distance term.
			Assert.That(TransportDropSiteMath.ScoreDrop(-800, 0, 0, 10, 50, 100, 0), Is.EqualTo(-400));
			Assert.That(TransportDropSiteMath.ScoreDrop(0, 100, 0, 0, 100, 200, 5), Is.EqualTo(-200));
		}

		// ---------- headline: the user complaint ----------

		[Test]
		public void ReachableFlankPoi_OutranksDeepBehindEnemySrCell()
		{
			// The reported bug: a drop lands deep behind the enemy SR instead of a reachable side POI.
			//   deep cell : believed-enemy floor -800, high believed danger, 40 cells from our SR.
			//   flank POI : contested (control 0), low danger, 10 cells from our SR.
			// The flank POI must score higher so the picker prefers it — a weight, not a ban.
			var deepBehindEnemySr = Score(-800, 90, 40, 40);
			var reachableFlankPoi = Score(0, 10, 0, 10);

			Assert.That(reachableFlankPoi, Is.GreaterThan(deepBehindEnemySr));
		}

		[Test]
		public void NotAHardBan_DeepCellStillWinsWhenItIsTheLeastBadOption()
		{
			// Weight-not-filter: if the only alternative is even worse (farther, more dangerous, equally deep),
			// the deep cell can still be selected. The math never vetoes a candidate outright.
			var deep = Score(-400, 20, 0, 15);
			var worse = Score(-400, 60, 30, 45);

			Assert.That(deep, Is.GreaterThan(worse));
		}
	}
}
