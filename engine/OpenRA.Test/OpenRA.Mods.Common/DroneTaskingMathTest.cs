#region Copyright & License Information
/*
 * WW3MOD DroneTaskingMath tests — the recon-drone tasking rules for the @experimental bot.
 *
 * These cover the three things that would each produce a bot that LOOKS like it works: a leash
 * computed from the wrong constant, an unbounded staleness argmax, and a launch issued in a state
 * where the weapon fires and no drone spawns.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DroneTaskingMathTest
	{
		// ---------- MaxHoverDistanceCells ----------

		[Test]
		public void MaxHoverDistance_TakesTheSmallerOfWeaponAndLeash()
		{
			Assert.Multiple(() =>
			{
				// Leash binds: weapon reaches 25, but the 25-cell leash less a 3-cell margin is 22.
				Assert.That(DroneTaskingMath.MaxHoverDistanceCells(25, 25, 3), Is.EqualTo(22));

				// Weapon binds: a shorter weapon is the limit even with a generous leash.
				Assert.That(DroneTaskingMath.MaxHoverDistanceCells(10, 25, 3), Is.EqualTo(10));
			});
		}

		[Test]
		public void MaxHoverDistance_LeavesMarginInsideTheLeash()
		{
			// THE REGRESSION THIS PINS: the leash check is periodic (MaxDistanceCheckTicks: 20), so a
			// drone parked exactly on 25 is one nudge from being dragged back and granted
			// lost-connection, which zeroes its vision. The result must be strictly inside the leash.
			var d = DroneTaskingMath.MaxHoverDistanceCells(25, 25, 3);
			Assert.That(d, Is.LessThan(25));
		}

		[Test]
		public void MaxHoverDistance_NeverGoesNegative()
		{
			// A margin larger than the leash must clamp to 0, not produce a negative distance that
			// would make every candidate "in range" through a signed comparison.
			Assert.That(DroneTaskingMath.MaxHoverDistanceCells(25, 2, 5), Is.EqualTo(0));
		}

		[Test]
		public void MaxHoverDistance_IsNotBuiltOnTheInertMaxSlaveDistance()
		{
			// CarrierMasterInfo.MaxSlaveDistance had no readers engine-wide and the 20c0 that used to
			// sit on ^DR enforced nothing; the real leash is CarrierSlave.MaxDistance: 25 cells. If
			// someone ever "restores" the 20 as the leash, this fails.
			Assert.That(DroneTaskingMath.MaxHoverDistanceCells(25, 25, 3), Is.Not.EqualTo(20));
		}

		// ---------- ScoreCandidate ----------
		//
		// Signature is (revealedStaleSquares, minRevealed, poiDistance, maxPoiDistance, airDanger,
		// maxAirDanger, contactBonus). The first argument is what the drone would REVEAL from the
		// hover cell, NOT the staleness of the hover cell itself — see the regression test below.

		[Test]
		public void Score_RefusesACellThatRevealsTooLittle()
		{
			// Not worth a 60s sortie to uncover a couple of squares.
			Assert.That(
				DroneTaskingMath.ScoreCandidate(3, 12, 5, 40, 0, 100, 0),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_AcceptsALargeRevealRegardlessOfAnythingAboutTheCellItself()
		{
			// NOTE ON WHAT THIS DOES AND DOES NOT PIN. It checks only that a healthy revealed-area
			// count is accepted. It is NOT a regression test for the defect that motivated this model:
			// that defect was in the CALLER (it passed the hover cell's own staleness here), and this
			// function — a pure function of integers — was always correct. An earlier version of this
			// test claimed to pin the defect and did not: read positionally against the old signature
			// its arguments were (ticksSinceVerified: 40, minStalenessTicks: 12, ...), which the old
			// body scored 40 + 0 - 5 = 35, i.e. it passed on the code it claimed to catch.
			// The scenario that actually distinguishes the two models is
			// Model_AtTheLeashEdgeTheHoverSquareIsObservedButRevealsUnobservedGround below.
			var score = DroneTaskingMath.ScoreCandidate(40, 12, 5, 40, 0, 100, 0);
			Assert.That(score, Is.Not.EqualTo(DroneTaskingMath.Ineligible));
			Assert.That(score, Is.GreaterThan(0));
		}

		[Test]
		public void Score_RefusesTheUnreachableCorner()
		{
			// The unreachable-corner guard survives the model change: revealing a lot of ground nobody
			// will ever contest is still not worth the sortie.
			Assert.That(
				DroneTaskingMath.ScoreCandidate(200, 12, 90, 40, 0, 100, 0),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_RefusesHotAirspace()
		{
			// The drone dies to one hit of real AA. Revealed area must not buy its way past danger.
			Assert.That(
				DroneTaskingMath.ScoreCandidate(200, 12, 5, 40, 900, 100, 0),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_PrefersRevealingMore()
		{
			var less = DroneTaskingMath.ScoreCandidate(20, 12, 5, 40, 0, 100, 0);
			var more = DroneTaskingMath.ScoreCandidate(60, 12, 5, 40, 0, 100, 0);
			Assert.That(more, Is.GreaterThan(less));
		}

		[Test]
		public void Score_RevealedAreaOutweighsPoiDistanceTieBreak()
		{
			// One extra revealed square must beat any plausible POI-distance difference, or the tie
			// break silently becomes the primary term.
			var nearerButBlinder = DroneTaskingMath.ScoreCandidate(20, 12, 0, 40, 0, 100, 0);
			var fartherButRicher = DroneTaskingMath.ScoreCandidate(21, 12, 40, 40, 0, 100, 0);
			Assert.That(fartherButRicher, Is.GreaterThan(nearerButBlinder));
		}

		[Test]
		public void Score_PrefersGroundNearBelievedContacts()
		{
			var blank = DroneTaskingMath.ScoreCandidate(20, 12, 5, 40, 0, 100, 0);
			var nearContact = DroneTaskingMath.ScoreCandidate(20, 12, 5, 40, 0, 100, 2000);
			Assert.That(nearContact, Is.GreaterThan(blank));
		}

		[Test]
		public void Score_ReportsWhichGateRefused()
		{
			// The diagnostic that identified the original defect. Each threshold must be
			// distinguishable in the log, because "no eligible cell" is compatible with three
			// different bugs needing three different fixes.
			Assert.Multiple(() =>
			{
				DroneTaskingMath.ScoreCandidate(3, 12, 5, 40, 0, 100, 0, out var r1);
				Assert.That(r1, Is.EqualTo(DroneRefusal.TooLittleRevealed));

				DroneTaskingMath.ScoreCandidate(200, 12, 90, 40, 0, 100, 0, out var r2);
				Assert.That(r2, Is.EqualTo(DroneRefusal.TooFarFromPoi));

				DroneTaskingMath.ScoreCandidate(200, 12, 5, 40, 900, 100, 0, out var r3);
				Assert.That(r3, Is.EqualTo(DroneRefusal.TooDangerous));

				DroneTaskingMath.ScoreCandidate(200, 12, 5, 40, 0, 100, 0, out var r4);
				Assert.That(r4, Is.EqualTo(DroneRefusal.None));
			});
		}

		// ---------- Summed-area table ----------
		//
		// An off-by-one here does not crash. It mis-scores every candidate near a grid edge,
		// symmetrically, and every score-COMPARISON test still passes — so the arithmetic is pinned
		// against brute force rather than against itself.

		static int[,] BuildSat(bool[,] g, int gw, int gh)
		{
			var sat = new int[gw + 1, gh + 1];
			DroneTaskingMath.BuildSummedArea(sat, gw, gh, (x, y) => g[x, y]);
			return sat;
		}

		static int BruteForce(bool[,] g, int gw, int gh, int x0, int y0, int x1, int y1)
		{
			if (x0 < 0) x0 = 0;
			if (y0 < 0) y0 = 0;
			if (x1 > gw - 1) x1 = gw - 1;
			if (y1 > gh - 1) y1 = gh - 1;

			var n = 0;
			for (var x = x0; x <= x1; x++)
				for (var y = y0; y <= y1; y++)
					if (g[x, y])
						n++;

			return n;
		}

		[Test]
		public void Sat_MatchesBruteForceOnEveryRectangle()
		{
			// Exhaustive over a small deterministic grid: every inclusive rectangle, compared against
			// a direct count. This is what catches a corner sign error or an inclusive/exclusive slip.
			const int Gw = 7, Gh = 5;
			var g = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					g[x, y] = ((x * 3) + (y * 5)) % 4 == 0;

			var sat = BuildSat(g, Gw, Gh);

			Assert.Multiple(() =>
			{
				for (var x0 = 0; x0 < Gw; x0++)
					for (var y0 = 0; y0 < Gh; y0++)
						for (var x1 = x0; x1 < Gw; x1++)
							for (var y1 = y0; y1 < Gh; y1++)
								Assert.That(
									DroneTaskingMath.SumInclusive(sat, Gw, Gh, x0, y0, x1, y1),
									Is.EqualTo(BruteForce(g, Gw, Gh, x0, y0, x1, y1)),
									$"rect ({x0},{y0})-({x1},{y1})");
			});
		}

		[Test]
		public void Sat_ClampsABoxHangingOffTwoEdgesAtOnce()
		{
			// The drone's vision box is centred on a candidate and routinely hangs off a corner of the
			// map. Clamping must happen before the corner reads, and the result must equal the
			// clamped-rectangle count — not zero, and not an index throw.
			const int Gw = 6, Gh = 4;
			var g = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					g[x, y] = true;

			var sat = BuildSat(g, Gw, Gh);

			Assert.Multiple(() =>
			{
				// Off the top-left corner in both axes at once.
				Assert.That(DroneTaskingMath.SumInclusive(sat, Gw, Gh, -10, -10, 1, 1), Is.EqualTo(4));

				// Off the bottom-right in both axes at once.
				Assert.That(DroneTaskingMath.SumInclusive(sat, Gw, Gh, Gw - 2, Gh - 2, 99, 99), Is.EqualTo(4));

				// Larger than the grid in every direction: the whole grid, counted once.
				Assert.That(DroneTaskingMath.SumInclusive(sat, Gw, Gh, -99, -99, 99, 99), Is.EqualTo(Gw * Gh));
			});
		}

		[Test]
		public void Sat_ReturnsZeroForAnEmptyRectangle()
		{
			// A box entirely off-grid must be 0 rather than wrapping into a negative or throwing.
			const int Gw = 4, Gh = 4;
			var g = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					g[x, y] = true;

			var sat = BuildSat(g, Gw, Gh);
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.SumInclusive(sat, Gw, Gh, 10, 10, 20, 20), Is.EqualTo(0));
				Assert.That(DroneTaskingMath.SumInclusive(sat, Gw, Gh, -20, -20, -10, -10), Is.EqualTo(0));
			});
		}

		[Test]
		public void Sat_SumsAnIndicatorSoTheThresholdMustBeAppliedAtBuildTime()
		{
			// The table counts SQUARES, not magnitudes. If a future edit feeds raw staleness values in
			// here, this fails: 3 set squares must read 3, whatever their underlying ages were.
			const int Gw = 3, Gh = 3;
			var g = new bool[Gw, Gh];
			g[0, 0] = g[1, 1] = g[2, 2] = true;

			var sat = BuildSat(g, Gw, Gh);
			Assert.That(DroneTaskingMath.SumInclusive(sat, Gw, Gh, 0, 0, Gw - 1, Gh - 1), Is.EqualTo(3));
		}

		// ---------- The model, on synthetic geometry ----------

		[Test]
		public void Model_AtTheLeashEdgeTheHoverSquareIsObservedButRevealsUnobservedGround()
		{
			// THE SCENARIO THE OLD MODEL COULD NOT ESCAPE, expressed in grid squares.
			//
			// CellSize 2, so a grid square is 2 map cells. The operator verifies 28 map cells = 14
			// squares in every direction. The drone's leash allows 22 map cells = 11 squares. So every
			// reachable hover square is inside the observed bubble: its OWN staleness is always zero,
			// which is why the old caller refused 674,584 of 674,584 candidates.
			//
			// But the drone sees 14 squares from wherever it parks, so from the leash edge at 11 it
			// reaches 25 squares out — into ground the operator never observed. This test asserts both
			// halves at once: the hover square is observed (old model: refuse) AND the box around it
			// contains plenty of unobserved ground (new model: launch).
			const int Gw = 60, Gh = 60;
			const int OpX = 20, OpY = 20;
			const int OperatorVisionSquares = 14;
			const int LeashSquares = 11;
			const int DroneVisionSquares = 14;

			// Unobserved everywhere EXCEPT inside the operator's own bubble.
			var unobserved = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					unobserved[x, y] = ((x - OpX) * (x - OpX)) + ((y - OpY) * (y - OpY))
						> OperatorVisionSquares * OperatorVisionSquares;

			var sat = BuildSat(unobserved, Gw, Gh);

			// A hover square at the leash edge, due east of the operator.
			var hx = OpX + LeashSquares;
			var hy = OpY;

			Assert.Multiple(() =>
			{
				// Half one: the hover square itself is observed. The old model scored THIS and refused.
				Assert.That(unobserved[hx, hy], Is.False,
					"the reachable hover square must be inside the operator's bubble — that is the trap");

				// Half two: what the drone would actually see from there is substantially unobserved.
				var revealed = DroneTaskingMath.SumInclusive(sat, Gw, Gh,
					hx - DroneVisionSquares, hy - DroneVisionSquares,
					hx + DroneVisionSquares, hy + DroneVisionSquares);

				Assert.That(revealed, Is.GreaterThan(12),
					"a drone at the leash edge must reveal more than MinRevealedSquares");

				// And the model accepts it on that basis.
				Assert.That(
					DroneTaskingMath.ScoreCandidate(revealed, 12, 5, 40, 0, 100, 0),
					Is.Not.EqualTo(DroneTaskingMath.Ineligible));
			});
		}

		[Test]
		public void Model_TheBoxCornerArtefactIsLargeEnoughToMakeTheRevealFloorInert()
		{
			// MEASURED, and it is a real consequence rather than a rounding detail.
			//
			// The revealed-area query is a rectangular summed-area box, but the drone's vision is a
			// DISC. With a 14-square radius the box is 29x29 = 841 squares and the disc is 613, so up
			// to 228 squares of every query are corners the drone will NEVER see.
			//
			// Two things follow, and the second is the one that matters:
			//   1. Ranking still works. A hover square at the leash edge scores ~439 against ~228 for
			//      one sitting on the operator, so the argmax does prefer the frontier.
			//   2. MinRevealedSquares CANNOT BIND. The artefact alone is ~228, about 19x the shipped
			//      floor of 12, so EVERY candidate clears the floor on corner artefact regardless of
			//      whether it would reveal anything real. The floor is inert, and raising it to a value
			//      that bites would have to clear 228 first.
			// This is why the score is effectively a pure argmax of revealed area — recorded rather
			// than fixed, so the first match measures one change and not three.
			const int Gw = 60, Gh = 60;
			const int OpX = 30, OpY = 30;
			const int VisionSquares = 14;

			var unobserved = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					unobserved[x, y] = ((x - OpX) * (x - OpX)) + ((y - OpY) * (y - OpY))
						> VisionSquares * VisionSquares;

			var sat = BuildSat(unobserved, Gw, Gh);

			int RevealedAt(int cx, int cy) => DroneTaskingMath.SumInclusive(sat, Gw, Gh,
				cx - VisionSquares, cy - VisionSquares, cx + VisionSquares, cy + VisionSquares);

			var atOperator = RevealedAt(OpX, OpY);
			var atLeashEdge = RevealedAt(OpX + 11, OpY);

			Assert.Multiple(() =>
			{
				// The artefact floor: nothing real is visible from the operator's own square, yet the
				// query still reports this much.
				Assert.That(atOperator, Is.EqualTo(228));

				// It dwarfs the shipped floor, which is therefore unable to refuse anything.
				Assert.That(atOperator, Is.GreaterThan(12 * 15));

				// Ranking survives the artefact: the frontier still scores far higher.
				Assert.That(atLeashEdge, Is.GreaterThan(atOperator * 3 / 2));
			});
		}

		// ---------- CanLaunch ----------

		[Test]
		public void CanLaunch_AllowsTheGoodCase()
		{
			Assert.That(DroneTaskingMath.CanLaunch(true, true, true, 10, 22), Is.True);
		}

		[Test]
		public void CanLaunch_RefusesWhileTheOperatorIsMoving()
		{
			// THE EXPENSIVE ONE. CarrierMaster is PauseOnCondition "moving", and Attacking()
			// early-returns on IsTraitPaused — so a launch ordered while moving fires the weapon,
			// burns the 3s FireDelay and the 12s BurstWait, plays the animation, and spawns NOTHING.
			Assert.That(DroneTaskingMath.CanLaunch(true, true, false, 10, 22), Is.False);
		}

		[Test]
		public void CanLaunch_AllowsAnOperatorThatIsStationaryButNotIdle()
		{
			// THE REGRESSION THAT SHIPPED. This term is "not moving", NOT "idle". After its first
			// launch the operator is never idle again: the Attack activity holds forever because
			// ChooseArmamentsForTarget filters IsTraitDisabled but not IsTraitPaused and ^DR does not
			// set AbandonWhenArmamentsPaused. An idle gate here latched false for the rest of the
			// match and capped the module at ONE sortie per operator — invisible to every other test
			// in this file, because it lives in the activity layer rather than in the arithmetic.
			// A wedged operator is standing perfectly still and is a valid launch platform.
			const bool StationaryButHoldingAnAttackActivity = true;
			Assert.That(
				DroneTaskingMath.CanLaunch(true, true, StationaryButHoldingAnAttackActivity, 10, 22),
				Is.True);
		}

		// ---------- ShouldRetask ----------

		[Test]
		public void ShouldRetask_OrdersTheFirstSortie()
		{
			Assert.That(DroneTaskingMath.ShouldRetask(false, false, int.MaxValue, 75), Is.True);
		}

		[Test]
		public void ShouldRetask_MovesThePostWhenABetterCellAppears()
		{
			// The sweep depends entirely on this: the engine re-fires the held activity at the OLD
			// cell by itself, so a new cell only ever gets flown if the module orders it.
			Assert.That(DroneTaskingMath.ShouldRetask(true, false, 500, 75), Is.True);
		}

		[Test]
		public void ShouldRetask_LeavesAStandingOrderAloneWhenTheCellIsUnchanged()
		{
			// Re-ordering the same cell would cancel and rebuild an identical activity every cycle.
			Assert.That(DroneTaskingMath.ShouldRetask(true, true, 500, 75), Is.False);
		}

		[Test]
		public void ShouldRetask_DoesNotDisturbAPendingFireDelay()
		{
			// The spawn is a delayed action owned by the Armament, not the activity, so re-ordering
			// inside the 50-tick FireDelay does not cancel the launch — it just aims the operator
			// elsewhere while the drone departs for the old cell. Settle first.
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.ShouldRetask(true, false, 20, 75), Is.False);
				Assert.That(DroneTaskingMath.ShouldRetask(true, false, 75, 75), Is.True);
			});
		}

		[Test]
		public void ShouldRetask_SettleWindowClearsTheFireDelay()
		{
			// Guards the config relationship rather than the function: a settle window at or below the
			// 50-tick FireDelay would re-order mid-launch, which is the case above.
			Assert.That(DroneTaskingMath.ShouldRetask(true, false, 50, 75), Is.False);
		}

		[Test]
		public void CanLaunch_RefusesWhenTheArmamentIsPaused()
		{
			// Covers the state that looks like success: after a kill the quadcopter respawns and
			// `loaded` is re-granted, but ammo-primary is 0, so the armament stays paused. The
			// operator visibly has a drone and cannot launch it.
			Assert.That(DroneTaskingMath.CanLaunch(false, true, true, 10, 22), Is.False);
		}

		[Test]
		public void CanLaunch_RefusesASecondLaunchWhileOneIsAirborne()
		{
			// The retarget branch is unreachable for ^DR, so this could not redirect the drone; it
			// would only burn the cooldown.
			Assert.That(DroneTaskingMath.CanLaunch(true, false, true, 10, 22), Is.False);
		}

		[Test]
		public void CanLaunch_RefusesOutOfRangeSoTheOperatorNeverWalks()
		{
			// Out of weapon range the attack activity would WALK the operator there, granting
			// `moving` — which recalls the drone and defeats standing off at all.
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.CanLaunch(true, true, true, 23, 22), Is.False);
				Assert.That(DroneTaskingMath.CanLaunch(true, true, true, 22, 22), Is.True);
			});
		}

		// ---------- IsCovered ----------

		[Test]
		public void IsCovered_RetiresASquareOnceItIsFreshAgain()
		{
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.IsCovered(10, 500), Is.True);
				Assert.That(DroneTaskingMath.IsCovered(900, 500), Is.False);
			});
		}
	}
}
