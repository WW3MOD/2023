#region Copyright & License Information
/*
 * WW3MOD DroneTaskingMath tests — the recon-drone tasking rules for the @experimental bot.
 *
 * These cover the three things that would each produce a bot that LOOKS like it works: a leash
 * computed from the wrong constant, an unbounded staleness argmax, and a launch issued in a state
 * where the weapon fires and no drone spawns.
 */
#endregion

using System;
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
		// EVERY CALL BELOW GOES THROUGH THIS HELPER, AND THAT IS A DELIBERATE GUARD RATHER THAN BREVITY.
		// These tests were written against a 7-argument positional signature. When `intelSquares` was
		// inserted as the second parameter, every one of those calls STILL COMPILED and still passed —
		// re-bound one position to the right, so `Score_RefusesACellThatRevealsTooLittle` was quietly
		// asserting a POI refusal instead. A test that passes for the wrong reason is worse than no
		// test, and this file has already shipped one (see the note in the reveal test below). Named
		// optional parameters mean the next signature change is a compile error in one place instead of
		// sixteen silent rebindings.
		//
		// `revealed` is what the drone would REVEAL from the hover cell, NOT the staleness of the hover
		// cell itself — see the regression test further down.
		static long Score(
			int revealed,
			int intel = 0,
			int minRevealed = 12,
			int poi = 5,
			int maxPoi = 40,
			int air = 0,
			int maxAir = 100)
		{
			return DroneTaskingMath.ScoreCandidate(revealed, intel, minRevealed, poi, maxPoi, air, maxAir);
		}

		static long Score(
			out DroneRefusal refusal,
			int revealed,
			int intel = 0,
			int minRevealed = 12,
			int poi = 5,
			int maxPoi = 40,
			int air = 0,
			int maxAir = 100)
		{
			return DroneTaskingMath.ScoreCandidate(revealed, intel, minRevealed, poi, maxPoi, air, maxAir, out refusal);
		}

		[Test]
		public void Score_RefusesACellThatRevealsTooLittle()
		{
			// Not worth a 60s sortie to uncover a couple of squares.
			Assert.That(
				Score(revealed: 3),
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
			var score = Score(revealed: 40);
			Assert.That(score, Is.Not.EqualTo(DroneTaskingMath.Ineligible));
			Assert.That(score, Is.GreaterThan(0));
		}

		[Test]
		public void Score_RefusesTheUnreachableCorner()
		{
			// The unreachable-corner guard survives the model change: revealing a lot of ground nobody
			// will ever contest is still not worth the sortie.
			Assert.That(
				Score(revealed: 200, poi: 90),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_RefusesHotAirspace()
		{
			// The drone dies to one hit of real AA. Revealed area must not buy its way past danger.
			Assert.That(
				Score(revealed: 200, air: 900),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		[Test]
		public void Score_PrefersRevealingMore()
		{
			var less = Score(revealed: 20);
			var more = Score(revealed: 60);
			Assert.That(more, Is.GreaterThan(less));
		}

		[Test]
		public void Score_RevealedAreaOutweighsPoiDistanceTieBreak()
		{
			// One extra revealed square must beat any plausible POI-distance difference, or the tie
			// break silently becomes the primary term.
			var nearerButBlinder = Score(revealed: 20, poi: 0);
			var fartherButRicher = Score(revealed: 21, poi: 40);
			Assert.That(fartherButRicher, Is.GreaterThan(nearerButBlinder));
		}

		[Test]
		public void Score_PrefersGroundNearBelievedContacts()
		{
			var blank = Score(revealed: 20);
			var nearContact = Score(revealed: 20, intel: 250);
			Assert.That(nearContact, Is.GreaterThan(blank));
		}

		[Test]
		public void Score_IntelIsInTheSameCurrencyAsRevealedArea()
		{
			// THE REGRESSION THIS FEATURE EXISTS TO PREVENT, AND IT IS AN ARITHMETIC ONE.
			// The shipped contact term was `revealed * 1000 + contactBonus` with contactBonus = 2000 —
			// worth two revealed squares against a term that reaches ~841 squares. It was configured, it
			// typechecked, no test failed, and it could never change a decision. Pinning the EXCHANGE
			// RATE rather than merely "contacts help" is what makes that unrepeatable: N squares of
			// intel must move the score exactly as far as N squares of revealed area.
			Assert.Multiple(() =>
			{
				Assert.That(Score(revealed: 20, intel: 40), Is.EqualTo(Score(revealed: 60)));
				Assert.That(Score(revealed: 0, intel: 250), Is.EqualTo(Score(revealed: 250)));

				// The old bonus, converted into the new currency, is worth two squares — kept executable
				// so the measurement cannot decay into folklore.
				Assert.That(
					Score(revealed: 20, intel: 2000 / 1000) - Score(revealed: 20),
					Is.EqualTo(2000));
			});
		}

		[Test]
		public void Score_TheRevealFloorCountsIntelToo()
		{
			// A contact that has JUST vanished sits on ground the player was looking at moments ago, so
			// that ground is not yet stale and reveals almost nothing. Gate on revealed area alone and
			// the hunt cell is refused for exactly as long as the trail is warm — the same
			// "unsatisfiable by construction" shape as the defect that once stopped this module
			// launching at all. The floor must therefore see the intel term.
			//
			// THIS BECAME A LIVE GUARD WHEN THE QUERY BECAME A DISC. It used to be insurance: the
			// box-corner artefact put ~228 squares into every query (see
			// Model_TheDiscQueryRemovesTheBoxCornerArtefact), so the floor could not refuse anything and
			// this rule never fired. With the artefact gone the floor bites for the first time, and the
			// warm-trail case below is now the situation it actually has to get right.
			Assert.Multiple(() =>
			{
				Assert.That(Score(revealed: 2), Is.EqualTo(DroneTaskingMath.Ineligible),
					"a cell revealing nothing and worth no intel is still refused");

				Assert.That(Score(revealed: 2, intel: 250), Is.Not.EqualTo(DroneTaskingMath.Ineligible),
					"a freshly-lost contact must clear the floor on its own");
			});
		}

		[Test]
		public void Score_IntelCannotBuyItsWayIntoHotAirspace()
		{
			// The drone dies to one hit of real AA. A believed contact is a reason to look, never a
			// reason to donate the airframe — which is why intel joins the revealed term BEFORE the
			// danger gate rather than being added after it.
			Assert.That(
				Score(revealed: 200, intel: 250, air: 900),
				Is.EqualTo(DroneTaskingMath.Ineligible));
		}

		// ---------- IntelSquares: the lost-track tiering ----------

		[Test]
		public void Intel_LosingVisualIsWorthMoreThanKeepingIt()
		{
			// The whole feature in one assertion: the step UP at the moment a unit disappears. "Even more
			// so if we lost track of a target" is this inequality and nothing else.
			var watched = DroneTaskingMath.IntelSquares(10, false, 250, 60, 20, 50, 2000);
			var justLost = DroneTaskingMath.IntelSquares(60, false, 250, 60, 20, 50, 2000);

			Assert.Multiple(() =>
			{
				Assert.That(watched, Is.EqualTo(60));
				Assert.That(justLost, Is.GreaterThan(watched));
				Assert.That(justLost, Is.GreaterThan(200));
			});
		}

		[Test]
		public void Intel_DecaysTowardTheAreaFloorRatherThanToZero()
		{
			var fresh = DroneTaskingMath.IntelSquares(60, false, 250, 60, 20, 50, 2000);
			var middling = DroneTaskingMath.IntelSquares(1000, false, 250, 60, 20, 50, 2000);
			var cold = DroneTaskingMath.IntelSquares(1950, false, 250, 60, 20, 50, 2000);

			Assert.Multiple(() =>
			{
				Assert.That(middling, Is.LessThan(fresh),
					"a four-minute-old sighting is not worth a ten-second-old one");
				Assert.That(cold, Is.LessThan(middling));

				// THE FLOOR IS THE POINT. The unit is long gone from that cell, but the ground is still
				// where the enemy was operating — so an old record degrades into a weak area preference
				// instead of falling off a cliff into nothing.
				Assert.That(cold, Is.GreaterThanOrEqualTo(60));

				// Past the horizon it locates nothing at all and the caller drops the record.
				Assert.That(DroneTaskingMath.IntelSquares(2000, false, 250, 60, 20, 50, 2000), Is.EqualTo(0));
			});
		}

		[Test]
		public void Intel_StaticsAreCheapAndDoNotDecay()
		{
			// A structure is not going anywhere, so its position is not the open question a sortie
			// answers — and it must not decay, because it has not moved and never will.
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.IntelSquares(10, true, 250, 60, 20, 50, 2000), Is.EqualTo(20));
				Assert.That(DroneTaskingMath.IntelSquares(50000, true, 250, 60, 20, 50, 2000), Is.EqualTo(20));
			});
		}

		[Test]
		public void Intel_FalloffUsesTheDroneVisionRadiusNotAnAdjacencyStep()
		{
			// The previous model asked "is the hover cell within 6 cells of a contact", which understates
			// the drone by ~4.5x: parked anywhere it verifies a 28-cell bubble. A contact 20 cells from
			// the hover cell is comfortably observed and must still be worth something.
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.IntelFalloff(250, 0, 28), Is.EqualTo(250));
				Assert.That(DroneTaskingMath.IntelFalloff(250, 20, 28), Is.GreaterThan(0));
				Assert.That(DroneTaskingMath.IntelFalloff(250, 20, 28), Is.LessThan(250));

				// The old 6-cell step would have scored this at nothing.
				Assert.That(DroneTaskingMath.IntelFalloff(250, 12, 28), Is.GreaterThan(100));

				// Beyond vision the drone would not see the cell at all, so it is worth exactly nothing.
				Assert.That(DroneTaskingMath.IntelFalloff(250, 29, 28), Is.EqualTo(0));

				// Centring matters: closer to the last-known cell is worth more, because the uncertainty
				// disc grows around that point.
				Assert.That(
					DroneTaskingMath.IntelFalloff(250, 5, 28),
					Is.GreaterThan(DroneTaskingMath.IntelFalloff(250, 15, 28)));
			});
		}

		[Test]
		public void Score_ReportsWhichGateRefused()
		{
			// The diagnostic that identified the original defect. Each threshold must be
			// distinguishable in the log, because "no eligible cell" is compatible with three
			// different bugs needing three different fixes.
			Assert.Multiple(() =>
			{
				Score(out var r1, revealed: 3);
				Assert.That(r1, Is.EqualTo(DroneRefusal.TooLittleRevealed));

				Score(out var r2, revealed: 200, poi: 90);
				Assert.That(r2, Is.EqualTo(DroneRefusal.TooFarFromPoi));

				Score(out var r3, revealed: 200, air: 900);
				Assert.That(r3, Is.EqualTo(DroneRefusal.TooDangerous));

				Score(out var r4, revealed: 200);
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
					Score(revealed: revealed),
					Is.Not.EqualTo(DroneTaskingMath.Ineligible));
			});
		}

		[Test]
		public void Disc_MatchesADirectCountIncludingOffGridClamping()
		{
			// The strip decomposition against a brute-force count of the same disc. An off-by-one in the
			// half-widths, or a row-range that clamps wrongly, mis-scores every candidate SYMMETRICALLY
			// and so is invisible to any test that only compares two scores — the same blind spot the
			// SumInclusive boundary tests exist for.
			const int Gw = 49, Gh = 41, V = 14;

			// A deterministic, irregular pattern: a uniform fill would hide a half-width that is one too
			// small in the same way it hides a sign error.
			var set = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					set[x, y] = ((x * 7) + (y * 13)) % 3 == 0;

			var sat = BuildSat(set, Gw, Gh);
			var halfWidths = DroneTaskingMath.BuildDiscHalfWidths(V);

			int Direct(int cx, int cy)
			{
				var n = 0;
				for (var x = 0; x < Gw; x++)
					for (var y = 0; y < Gh; y++)
						if (set[x, y] && ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= V * V)
							n++;

				return n;
			}

			Assert.Multiple(() =>
			{
				// Inland, and hanging off each edge and each corner — clamping must not wrap or throw.
				foreach (var (cx, cy) in new[] { (24, 20), (0, 0), (48, 40), (0, 40), (48, 0), (5, 20), (24, 3) })
					Assert.That(DroneTaskingMath.SumDisc(sat, Gw, Gh, cx, cy, V, halfWidths),
						Is.EqualTo(Direct(cx, cy)), $"disc at {cx},{cy}");
			});
		}

		[Test]
		public void Disc_CountsTheDroneSVisionExactlyAndNotItsBoundingBox()
		{
			// THE TWO NUMBERS THE WHOLE FIX RESTS ON. At the shipped 14-square radius the bounding box is
			// 29x29 = 841 squares and the disc is 613 — so the box counted 228 squares of corner the drone
			// can never see. If either number moves, the artefact arithmetic recorded everywhere else in
			// this file and in the module is stale.
			const int Gw = 60, Gh = 60, V = 14;

			var all = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					all[x, y] = true;

			var sat = BuildSat(all, Gw, Gh);
			var halfWidths = DroneTaskingMath.BuildDiscHalfWidths(V);

			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.SumInclusive(sat, Gw, Gh, 30 - V, 30 - V, 30 + V, 30 + V),
					Is.EqualTo(841), "the bounding box is 29x29");

				Assert.That(DroneTaskingMath.SumDisc(sat, Gw, Gh, 30, 30, V, halfWidths),
					Is.EqualTo(613), "the drone's vision disc");

				Assert.That(841 - 613, Is.EqualTo(228), "the corner artefact the box used to add");

				// The half-widths are the disc's own definition, so pin the boundary row rather than only
				// the total: 5² + 13² = 194 <= 196 fits, 6² + 13² = 205 does not. This is the row a float
				// sqrt would be most likely to disagree about on a different runtime.
				Assert.That(halfWidths[13], Is.EqualTo(5));
				Assert.That(halfWidths[14], Is.EqualTo(0));
				Assert.That(halfWidths[0], Is.EqualTo(V));
			});
		}

		[Test]
		public void Model_TheDiscQueryRemovesTheBoxCornerArtefact()
		{
			// WHAT THE ARTEFACT ACTUALLY COST, and why it was not a constant offset that cancels out of an
			// argmax. It is STATE-DEPENDENT: a candidate whose CORNERS are stale and whose disc is fresh
			// outscored one that would genuinely reveal something. And at ~19x MinRevealedSquares (12) it
			// meant EVERY candidate cleared the launch floor on corner alone, so the floor was inert and
			// the score was effectively a pure argmax of revealed area.
			//
			// Construction: everything OUTSIDE the operator's own vision disc is unobserved, so a query
			// centred on the operator has in truth nothing to reveal and any non-zero answer is artefact.
			const int Gw = 60, Gh = 60;
			const int OpX = 30, OpY = 30;
			const int VisionSquares = 14;

			var unobserved = new bool[Gw, Gh];
			for (var x = 0; x < Gw; x++)
				for (var y = 0; y < Gh; y++)
					unobserved[x, y] = ((x - OpX) * (x - OpX)) + ((y - OpY) * (y - OpY))
						> VisionSquares * VisionSquares;

			var sat = BuildSat(unobserved, Gw, Gh);
			var halfWidths = DroneTaskingMath.BuildDiscHalfWidths(VisionSquares);

			int BoxAt(int cx, int cy) => DroneTaskingMath.SumInclusive(sat, Gw, Gh,
				cx - VisionSquares, cy - VisionSquares, cx + VisionSquares, cy + VisionSquares);

			int DiscAt(int cx, int cy) => DroneTaskingMath.SumDisc(sat, Gw, Gh, cx, cy, VisionSquares, halfWidths);

			Assert.Multiple(() =>
			{
				// RED, kept live: the box credits the operator's own square with 228 squares of corner.
				Assert.That(BoxAt(OpX, OpY), Is.EqualTo(228),
					"the box query must still show the artefact this fix removes");
				Assert.That(BoxAt(OpX, OpY), Is.GreaterThan(12 * 15),
					"and it must still dwarf the launch floor, which is the consequence that mattered");

				// GREEN: the disc reports the truth — nothing is revealable from where the operator stands.
				Assert.That(DiscAt(OpX, OpY), Is.EqualTo(0),
					"a disc-shaped query over ground already inside the operator's bubble reveals nothing");

				// THE POINT OF THE FIX: MinRevealedSquares can refuse something for the first time.
				Assert.That(DiscAt(OpX, OpY), Is.LessThan(12));

				// AND THE SIGNAL SURVIVES. A fix that made every candidate score zero would also satisfy
				// the assertions above and would silently stop the module flying at all.
				Assert.That(DiscAt(OpX + 11, OpY), Is.GreaterThan(12));
				Assert.That(DiscAt(OpX + 11, OpY), Is.GreaterThan(DiscAt(OpX, OpY)));
			});
		}

		// ---------- The non-playable border ----------

		[Test]
		public void Revealable_TheNonPlayableBorderIsNotUnobservedGround()
		{
			// RIVER ZETA'S REAL GEOMETRY, in grid squares. MapSize 98x82, Bounds 1,1,96,80
			// (mods/ww3mod/maps/river-zeta-ww3/map.yaml) — every shipped map carries the same one-cell
			// non-playable ring. ControlField sizes its grid from MapSize (ControlField.cs:520-521), so
			// at CellSize 2 the grid is 49x41, and GridCellToMapCell returns the block CENTRE 2g+1. The
			// low ring is invisible to that test (gx=0 -> map x=1, which IS playable); the HIGH edge is
			// not: gx=48 -> x=97 > 96, gy=40 -> y=81 > 80. So exactly the last column and the last row
			// are squares no player can ever see.
			//
			// A square nobody can see is never marked verified, so TicksSinceVerified returns
			// int.MaxValue forever (ControlField.cs:921-928), which clears any >= MinStalenessTicks test.
			// The exploration term therefore counts it as ground worth flying to.
			const int Gw = 49, Gh = 41;
			const int PlayableMaxX = 47, PlayableMaxY = 39;
			const int MinStalenessTicks = 500;

			bool InPlayableBounds(int gx, int gy) => gx <= PlayableMaxX && gy <= PlayableMaxY;

			// EVERY PLAYABLE SQUARE IS FRESHLY OBSERVED. Real revealable ground is zero by construction,
			// so anything the query reports is pure artefact and the number is unambiguous.
			int Staleness(int gx, int gy) => InPlayableBounds(gx, gy) ? 0 : int.MaxValue;

			var sat = new int[Gw + 1, Gh + 1];
			DroneTaskingMath.BuildSummedArea(sat, Gw, Gh,
				(gx, gy) => DroneTaskingMath.IsRevealable(
					InPlayableBounds(gx, gy), Staleness(gx, gy), MinStalenessTicks));

			// The uncorrected predicate: staleness alone, exactly what BuildStaleSat used to pass.
			var inflated = new int[Gw + 1, Gh + 1];
			DroneTaskingMath.BuildSummedArea(inflated, Gw, Gh,
				(gx, gy) => Staleness(gx, gy) >= MinStalenessTicks);

			// The worst legal candidate: the playable square nearest the high corner. Candidates are
			// themselves bounds-checked, so gx=48/gy=40 are never offered — 47,39 is as close as a drone
			// can be asked to hover. DroneVisionCells 28 at CellSize 2 is +/-14 squares, a 29x29 box that
			// SumInclusive clamps to the grid.
			const int Cx = PlayableMaxX, Cy = PlayableMaxY, VisionSquares = 14;

			int At(int[,] table) => DroneTaskingMath.SumInclusive(table, Gw, Gh,
				Cx - VisionSquares, Cy - VisionSquares, Cx + VisionSquares, Cy + VisionSquares);

			Assert.Multiple(() =>
			{
				// RED: 31 squares of "unobserved ground" over a neighbourhood that is fully observed.
				// 16 squares of the last column + 16 of the last row, less the corner counted twice.
				Assert.That(At(inflated), Is.EqualTo(31),
					"the uncorrected term must show the border inflation this test exists to remove");

				// It is not a rounding detail: 31 is 2.5x MinRevealedSquares (12), so the border alone
				// can carry a candidate over the launch floor with nothing real to see.
				Assert.That(At(inflated), Is.GreaterThan(12 * 2));

				// GREEN: the corrected term reports the truth.
				Assert.That(At(sat), Is.EqualTo(0),
					"fully-observed playable ground must reveal nothing");
			});
		}

		[Test]
		public void Revealable_StillCountsGenuinelyStalePlayableGround()
		{
			// The other direction, so the fix cannot be "return false" — a stale square INSIDE the
			// playable area is exactly what the drone exists to go and look at.
			Assert.Multiple(() =>
			{
				Assert.That(DroneTaskingMath.IsRevealable(true, 500, 500), Is.True, "stale and playable");
				Assert.That(DroneTaskingMath.IsRevealable(true, int.MaxValue, 500), Is.True, "never seen, playable");
				Assert.That(DroneTaskingMath.IsRevealable(true, 499, 500), Is.False, "too fresh");
				Assert.That(DroneTaskingMath.IsRevealable(false, int.MaxValue, 500), Is.False, "outside the playable map");
			});
		}

		[Test]
		public void Revealable_TheDiscAndTheBoundsTestRemoveBothArtefacts()
		{
			// THE TWO ARTEFACTS WERE NEAR-ORTHOGONAL AND EACH NEEDED ITS OWN FIX. The historical numbers
			// are kept live because they are what says so: an audit that treats "the border band" as the
			// whole story mis-sizes the problem by 4x inland, and one that treats the corner as the whole
			// story misses that an EDGE candidate's corner is already partly truncated by the grid clamp.
			//
			// Inflation 1 (BOX CORNER, geometric): the query was a 29x29 BOX against a 613-square DISC, so
			// up to 228 squares of every query were corners it will never see. Fixed by SumDisc.
			// Inflation 2 (BORDER, map-dependent): squares outside the playable Bounds are never verified,
			// so TicksSinceVerified reads int.MaxValue forever. Fixed by IsRevealable's bounds term.
			//
			// "Phantom" is every square the query COUNTS that the drone cannot actually reveal — outside
			// its disc, or outside the playable map. River Zeta's geometry: MapSize 98x82, Bounds
			// 1,1,96,80, grid 49x41 at CellSize 2.
			const int Gw = 49, Gh = 41, PlayableMaxX = 47, PlayableMaxY = 39, V = 14;

			bool Playable(int gx, int gy) => gx <= PlayableMaxX && gy <= PlayableMaxY;

			int Phantom(int cx, int cy, bool disc, bool bounded)
			{
				var n = 0;
				for (var gx = Math.Max(0, cx - V); gx <= Math.Min(Gw - 1, cx + V); gx++)
					for (var gy = Math.Max(0, cy - V); gy <= Math.Min(Gh - 1, cy + V); gy++)
					{
						var beyondDisc = ((gx - cx) * (gx - cx)) + ((gy - cy) * (gy - cy)) > V * V;

						// A square the query does not look at cannot be a phantom in it.
						if (disc && beyondDisc)
							continue;

						if (bounded && !Playable(gx, gy))
							continue;

						if (beyondDisc || !Playable(gx, gy))
							n++;
					}

				return n;
			}

			Assert.Multiple(() =>
			{
				// HISTORY, INLAND: the border term could not reach a box that never leaves Bounds, so
				// fixing the border alone left the corner artefact exactly as it was.
				Assert.That(Phantom(24, 20, false, false), Is.EqualTo(228));
				Assert.That(Phantom(24, 20, false, true), Is.EqualTo(228),
					"the border fix did not move an inland candidate at all");

				// HISTORY, AT THE EDGE: the border fix helped only there, and only by its own component.
				Assert.That(Phantom(47, 20, false, false), Is.EqualTo(143));
				Assert.That(Phantom(47, 20, false, true), Is.EqualTo(114));
				Assert.That(Phantom(47, 39, false, false), Is.EqualTo(88));
				Assert.That(Phantom(47, 39, false, true), Is.EqualTo(57));

				// WHY ONE FIX WAS NOT ENOUGH: with only the border removed, the inland candidate was the
				// MOST over-credited of the three and by a WIDER margin than before — correcting one of
				// two biases made candidates less comparable, not more.
				Assert.That(Phantom(24, 20, false, true), Is.GreaterThan(Phantom(47, 39, false, true)));
				Assert.That(Phantom(24, 20, false, true) - Phantom(47, 39, false, true),
					Is.GreaterThan(Phantom(24, 20, false, false) - Phantom(47, 39, false, false)));

				// SHIPPED TODAY: disc-shaped and bounds-tested, so no candidate is credited with ground it
				// cannot reveal, and the three are finally comparable to each other.
				Assert.That(Phantom(24, 20, true, true), Is.EqualTo(0));
				Assert.That(Phantom(47, 20, true, true), Is.EqualTo(0));
				Assert.That(Phantom(47, 39, true, true), Is.EqualTo(0));
			});
		}

		[Test]
		public void Revealable_TheBoundsTestStillBitesInsideTheDisc()
		{
			// THE ORTHOGONALITY CLAIM ABOVE, MADE ABLE TO FAIL. Phantom(..., true, true) is 0 by the
			// helper's own construction, so on its own it proves nothing about the shipped code. This runs
			// the REAL query over the REAL predicate and shows the bounds term is still load-bearing after
			// the disc fix: at a high-corner candidate the non-playable border is INSIDE the vision disc,
			// not only in the box corners the disc now discards.
			//
			// If someone reads "the disc removes the corner artefact" as "the bounds test is redundant
			// now" and deletes it, this fails.
			const int Gw = 49, Gh = 41, PlayableMaxX = 47, PlayableMaxY = 39, V = 14;
			const int MinStalenessTicks = 500;

			bool InPlayableBounds(int gx, int gy) => gx <= PlayableMaxX && gy <= PlayableMaxY;

			// Every PLAYABLE square freshly observed, so real revealable ground is zero by construction
			// and anything the query reports is pure border artefact.
			int Staleness(int gx, int gy) => InPlayableBounds(gx, gy) ? 0 : int.MaxValue;

			var corrected = new int[Gw + 1, Gh + 1];
			DroneTaskingMath.BuildSummedArea(corrected, Gw, Gh,
				(gx, gy) => DroneTaskingMath.IsRevealable(
					InPlayableBounds(gx, gy), Staleness(gx, gy), MinStalenessTicks));

			// Staleness alone — what BuildStaleSat passed before the bounds term existed.
			var unbounded = new int[Gw + 1, Gh + 1];
			DroneTaskingMath.BuildSummedArea(unbounded, Gw, Gh,
				(gx, gy) => Staleness(gx, gy) >= MinStalenessTicks);

			var halfWidths = DroneTaskingMath.BuildDiscHalfWidths(V);

			int Disc(int[,] t, int cx, int cy) => DroneTaskingMath.SumDisc(t, Gw, Gh, cx, cy, V, halfWidths);

			Assert.Multiple(() =>
			{
				// RED: without the bounds term the disc STILL counts the non-playable border, because that
				// border falls inside the disc rather than in the corners the disc discards.
				Assert.That(Disc(unbounded, PlayableMaxX, PlayableMaxY), Is.GreaterThan(12),
					"the disc alone must not be mistaken for a fix for the border inflation");

				// GREEN: with it, fully-observed playable ground reveals nothing.
				Assert.That(Disc(corrected, PlayableMaxX, PlayableMaxY), Is.EqualTo(0));
				Assert.That(Disc(corrected, 24, 20), Is.EqualTo(0));
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
