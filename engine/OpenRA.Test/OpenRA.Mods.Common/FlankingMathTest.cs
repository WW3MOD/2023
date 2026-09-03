#region Copyright & License Information
/*
 * WW3MOD @experimental offense — FLANKING MANEUVER math test.
 *
 * Pins the decisions the flanking consumer turns an axis + the believed ground-danger field into:
 *   (1) SPLIT VIABILITY — a force too small to leave two fighting elements, or already too close to the
 *       objective to swing wide, assaults UNDIVIDED. Splitting is refused, not attempted badly.
 *   (2) ELEMENT SIZING — the flank is a MINORITY, both elements clear the configured floor, and a force
 *       under twice that floor can never split.
 *   (3) LATERAL OFFSET — scales with force size, and is clamped BOTH by the absolute ceiling and by half
 *       the approach, so the flank leg is never a longer walk than the assault it is supporting.
 *   (4) FIRES ON A CLEAR APPROACH — the property that distinguishes this from the Stage-E detour, which
 *       returns null the moment the beeline is safe. Flanking is doctrine: no danger still yields a lane.
 *   (5) LANE CHOICE — of the two lateral lanes the LOWER-exposure one wins on strict merit; an exact tie
 *       keeps the first-iterated side (+1); neither side standable yields null + SideNone, the caller's
 *       signal to assault undivided rather than order units into terrain.
 *   (6) GEOMETRY — the waypoint is genuinely perpendicular and genuinely off-axis, i.e. it produces a
 *       SECOND BEARING rather than a wider blob. This is the "looks like a flank to a human" claim.
 *   (7) CONVERGE — the main element holds only while the flank meaningfully lags AND the main is at
 *       standoff, and releases immediately once the flank is level, still far out, or already engaged.
 *   (8) HOLD BUDGET — bounded, monotone, never refunded on release, and FAIL-SAFE at a zero budget
 *       ("never hold", not "hold forever").
 * Plus a determinism guard. Pure math over synthetic samplers; no world mounted.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class FlankingMathTest
	{
		// A radial danger envelope: full intensity at the centre, linear falloff to 0 at the edge, 0 beyond —
		// the same shape DangerFieldLayer stamps. Matches GroundDangerNavTest's helper so the two fixtures
		// describe the same field.
		static Func<CPos, int> Envelope(CPos centre, int radius, int coreIntensity)
		{
			return c =>
			{
				var dx = c.X - centre.X;
				var dy = c.Y - centre.Y;
				var d = Exts.ISqrt(dx * dx + dy * dy);
				if (d > radius)
					return 0;

				return coreIntensity * (radius - d + 1) / (radius + 1);
			};
		}

		static readonly Func<CPos, int> NoDanger = _ => 0;
		static readonly Func<CPos, bool> AllPassable = _ => true;
		static readonly Func<CPos, bool> NonePassable = _ => false;

		#region (1) split viability

		[Test]
		public void SmallOrCloseForcesAssaultUndivided()
		{
			Assert.Multiple(() =>
			{
				Assert.That(FlankingMath.ShouldSplit(6, 30, minForceSize: 6, minApproachCells: 12), Is.True,
					"a 6-hull axis 30 cells out clears both bars and may split");
				Assert.That(FlankingMath.ShouldSplit(5, 30, minForceSize: 6, minApproachCells: 12), Is.False,
					"under the force floor the axis must assault undivided — two weak elements lose two fights");
				Assert.That(FlankingMath.ShouldSplit(6, 11, minForceSize: 6, minApproachCells: 12), Is.False,
					"inside the approach floor there is no room to swing wide before contact");
			});
		}

		#endregion

		#region (2) element sizing

		[Test]
		public void FlankElementIsAMinorityAndBothElementsClearTheFloor()
		{
			// 10 hulls at 35% => 3 flank, 7 main.
			var flank = FlankingMath.FlankElementSize(10, sharePct: 35, minElementSize: 2);

			Assert.Multiple(() =>
			{
				Assert.That(flank, Is.EqualTo(3), "35% of 10 is the 3-hull flank element");
				Assert.That(flank, Is.LessThan(10 - flank),
					"the flank must stay the MINORITY — the main element is the force the defender has to face");
				Assert.That(10 - flank, Is.GreaterThanOrEqualTo(2), "the main element clears the floor");
			});
		}

		[Test]
		public void ShareIsClampedUpToTheFloorAndDownToLeaveAMainElement()
		{
			Assert.Multiple(() =>
			{
				// 4 hulls at 35% => 1, below the floor of 2, so it is raised to 2 (leaving 2 main).
				Assert.That(FlankingMath.FlankElementSize(4, sharePct: 35, minElementSize: 2), Is.EqualTo(2),
					"a share under the floor is raised to it while a main element still fits");

				// 6 hulls at 90% => 5, which would leave 1 main; clamped to 4 so the main keeps the floor.
				Assert.That(FlankingMath.FlankElementSize(6, sharePct: 90, minElementSize: 2), Is.EqualTo(4),
					"an oversized share is clamped so the MAIN element still clears the floor");
			});
		}

		[Test]
		public void ForceUnderTwiceTheFloorCannotSplit()
		{
			Assert.Multiple(() =>
			{
				Assert.That(FlankingMath.FlankElementSize(3, sharePct: 35, minElementSize: 2), Is.EqualTo(0),
					"3 hulls cannot yield two 2-hull elements — 0 tells the caller to assault undivided");
				Assert.That(FlankingMath.FlankElementSize(4, sharePct: 35, minElementSize: 2), Is.EqualTo(2),
					"exactly twice the floor is the smallest splittable force");
			});
		}

		#endregion

		#region (3) lateral offset

		[Test]
		public void OffsetScalesWithForceAndIsClampedByCeilingAndByHalfTheApproach()
		{
			Assert.Multiple(() =>
			{
				// 6 hulls, 30 cells out: 4 + 1*6 = 10, under the 12 ceiling and under half of 30.
				Assert.That(FlankingMath.LateralOffsetCells(6, 30, baseCells: 4, perUnitCells: 1, maxCells: 12),
					Is.EqualTo(10), "base plus per-unit, inside both clamps");

				// 20 hulls: 4 + 20 = 24, clamped to the 12 ceiling.
				Assert.That(FlankingMath.LateralOffsetCells(20, 60, baseCells: 4, perUnitCells: 1, maxCells: 12),
					Is.EqualTo(12), "the absolute ceiling caps a large force's berth");

				// 6 hulls but only 14 cells out: 10 would exceed half the approach (7), so it is clamped to 7.
				Assert.That(FlankingMath.LateralOffsetCells(6, 14, baseCells: 4, perUnitCells: 1, maxCells: 12),
					Is.EqualTo(7),
					"half the approach caps the berth so the flank leg is never a longer walk than the assault");

				Assert.That(FlankingMath.LateralOffsetCells(6, 1, baseCells: 4, perUnitCells: 1, maxCells: 12),
					Is.EqualTo(0), "no room at all yields 0 — the caller's signal not to split");
			});
		}

		#endregion

		#region (4)+(5) lane choice

		[Test]
		public void ClearApproachStillYieldsALane()
		{
			// THE distinguishing property vs GroundDangerNav.DetourWaypoint, which returns null here. Flanking
			// is a doctrine, not an avoidance behaviour: an undefended objective is still approached on two
			// bearings.
			var via = FlankingMath.ChooseFlankWaypoint(new CPos(0, 10), new CPos(20, 10), offsetCells: 5,
				NoDanger, AllPassable, out var side);

			Assert.Multiple(() =>
			{
				Assert.That(via, Is.Not.Null, "a clear approach must still produce a flank lane");
				Assert.That(side, Is.EqualTo(1), "an exact tie keeps the first-iterated side, so the pick is stable");
			});
		}

		[Test]
		public void FlankSwingsToTheLowerExposureSide()
		{
			// Strongpoint parked on the +Y shoulder. The +1 lane (10,15) sits inside its envelope; the -1 lane
			// (10,5) is clear. The flank must take the weak shoulder.
			var ground = Envelope(new CPos(10, 16), 5, 100);

			var via = FlankingMath.ChooseFlankWaypoint(new CPos(0, 10), new CPos(20, 10), offsetCells: 5,
				ground, AllPassable, out var side);

			Assert.Multiple(() =>
			{
				Assert.That(via, Is.Not.Null, "a lane exists on the clear shoulder");
				Assert.That(side, Is.EqualTo(-1), "the LOWER-exposure side wins — the flank goes round the weak shoulder");
				Assert.That(via.Value.Y, Is.LessThan(10), "and the chosen waypoint is on the far side from the strongpoint");
			});
		}

		[Test]
		public void NeitherSideStandableYieldsNoSplit()
		{
			var via = FlankingMath.ChooseFlankWaypoint(new CPos(0, 10), new CPos(20, 10), offsetCells: 5,
				NoDanger, NonePassable, out var side);

			Assert.Multiple(() =>
			{
				Assert.That(via, Is.Null,
					"with no standable lane the caller must assault undivided, not order the flank into terrain");
				Assert.That(side, Is.EqualTo(FlankingMath.SideNone), "and the side reports as unset");
			});
		}

		[Test]
		public void DegenerateZeroLengthAxisYieldsNoSplit()
		{
			var via = FlankingMath.ChooseFlankWaypoint(new CPos(7, 7), new CPos(7, 7), offsetCells: 5,
				NoDanger, AllPassable, out var side);

			Assert.Multiple(() =>
			{
				Assert.That(via, Is.Null, "a zero-length approach has no perpendicular to offset along");
				Assert.That(side, Is.EqualTo(FlankingMath.SideNone), "and the side reports as unset");
			});
		}

		#endregion

		#region (6) geometry — a real second bearing

		[Test]
		public void WaypointIsPerpendicularToTheApproachAndMirrorsAcrossIt()
		{
			// Approach due east from (0,10) to (20,10): the perpendicular is the Y axis, so an offset of 5
			// lands 5 cells off the axis at the midpoint, on opposite sides for the two sides.
			var plus = FlankingMath.Waypoint(new CPos(0, 10), new CPos(20, 10), offsetCells: 5, side: 1);
			var minus = FlankingMath.Waypoint(new CPos(0, 10), new CPos(20, 10), offsetCells: 5, side: -1);

			Assert.Multiple(() =>
			{
				Assert.That(plus, Is.EqualTo(new CPos(10, 15)), "+1 offsets the midpoint 5 cells perpendicular");
				Assert.That(minus, Is.EqualTo(new CPos(10, 5)), "-1 mirrors it across the approach axis");
				Assert.That(plus.X, Is.EqualTo(minus.X), "both lanes sit at the midpoint along the axis");
			});
		}

		[Test]
		public void FlankRouteApproachesTheObjectiveOffTheMainBearing()
		{
			// The lethality/realism claim in geometric form: the flank's final leg must arrive on a materially
			// different bearing from the main element's. Main comes in along +X; the flank's last leg runs from
			// (10,5) to (20,10), i.e. with a real Y component. A "flank" whose final leg were still axis-parallel
			// would just be a wider blob.
			var via = FlankingMath.Waypoint(new CPos(0, 10), new CPos(20, 10), offsetCells: 8, side: -1);
			var target = new CPos(20, 10);

			var legDx = target.X - via.X;
			var legDy = target.Y - via.Y;

			Assert.Multiple(() =>
			{
				Assert.That(Math.Abs(legDy), Is.GreaterThan(0),
					"the flank's final leg carries a cross-axis component — it arrives on a second bearing");
				Assert.That(Math.Abs(legDy) * 2, Is.GreaterThanOrEqualTo(Math.Abs(legDx)),
					"and at the half-approach clamp that bearing is a wide one (>= ~26 degrees off axis), " +
					"not a token wobble a human would read as a single frontal push");
			});
		}

		#endregion

		#region (7) converge synchronisation

		[Test]
		public void MainHoldsAtStandoffWhileTheFlankLags()
		{
			Assert.That(FlankingMath.MainShouldHold(mainRemainingCells: 10, flankRemainingCells: 20,
				standoffCells: 14, toleranceCells: 4, flankEngaged: false), Is.True,
				"at standoff with the flank 10 cells further out, the main element waits so both arrive together");
		}

		[Test]
		public void MainReleasesOnceTheFlankIsLevel()
		{
			Assert.That(FlankingMath.MainShouldHold(mainRemainingCells: 10, flankRemainingCells: 13,
				standoffCells: 14, toleranceCells: 4, flankEngaged: false), Is.False,
				"inside the tolerance the flank counts as level — converge rather than dawdle");
		}

		[Test]
		public void MainDoesNotHoldWhileStillCrossingOpenGround()
		{
			Assert.That(FlankingMath.MainShouldHold(mainRemainingCells: 30, flankRemainingCells: 60,
				standoffCells: 14, toleranceCells: 4, flankEngaged: false), Is.False,
				"the converge is a decision made at the objective, not a reason to slow the whole approach");
		}

		[Test]
		public void EngagedFlankReleasesTheHoldImmediately()
		{
			// The degrade path: a flank in contact is fighting, not maneuvering. Holding the main element back
			// would let the defender fight half a force twice.
			Assert.That(FlankingMath.MainShouldHold(mainRemainingCells: 10, flankRemainingCells: 40,
				standoffCells: 14, toleranceCells: 4, flankEngaged: true), Is.False,
				"an engaged flank commits both elements regardless of how far it still had to go");
		}

		[Test]
		public void RouteRemainingIsTheTwoLegLength()
		{
			// (0,10) -> (10,5) -> (20,10): Chebyshev legs of 10 and 10.
			Assert.That(FlankingMath.RouteRemainingCells(new CPos(0, 10), new CPos(10, 5), new CPos(20, 10)),
				Is.EqualTo(20),
				"the flank's remaining route is both legs through its waypoint, not the crow-flies distance — " +
				"that is what makes it read as lagging while it swings wide");
		}

		#endregion

		#region (8) hold budget

		[Test]
		public void HoldBudgetIsBoundedMonotoneAndNotRefundedOnRelease()
		{
			Assert.Multiple(() =>
			{
				Assert.That(FlankingMath.HoldBudgetExhausted(0, 4), Is.False, "a fresh axis has budget");
				Assert.That(FlankingMath.HoldBudgetExhausted(4, 4), Is.True,
					"at the cap the main element assaults regardless of the flank — the wait terminates");

				// Monotone: holding steps up and saturates.
				var evals = FlankingMath.StepHold(0, holding: true, maxHoldEvals: 4);
				Assert.That(evals, Is.EqualTo(1), "a holding eval spends budget");
				Assert.That(FlankingMath.StepHold(4, holding: true, maxHoldEvals: 4), Is.EqualTo(4),
					"the counter saturates at the cap rather than running away");

				// NOT refunded on release — refunding would turn a terminating condition into a duty cycle,
				// re-holding next eval and oscillating the pair.
				Assert.That(FlankingMath.StepHold(3, holding: false, maxHoldEvals: 4), Is.EqualTo(3),
					"releasing the hold must NOT refund the budget, or the axis re-holds next eval forever");
			});
		}

		[Test]
		public void ZeroBudgetFailsSafeToNeverHolding()
		{
			Assert.Multiple(() =>
			{
				Assert.That(FlankingMath.HoldBudgetExhausted(0, 0), Is.True,
					"a zero budget reads as ALREADY spent — the fail-safe direction is 'never hold', never 'hold forever'");
				Assert.That(FlankingMath.StepHold(0, holding: true, maxHoldEvals: 0), Is.EqualTo(0),
					"and a zero budget cannot be spent into");
			});
		}

		#endregion

		#region determinism

		[Test]
		public void RepeatedEvaluationIsIdentical()
		{
			// Influence-stack invariant: zero random draws, so two clients over the same synced field pick the
			// same lane. Integer-only geometry — no floating point anywhere in the chain.
			var ground = Envelope(new CPos(12, 18), 6, 100);
			var from = new CPos(3, 9);
			var to = new CPos(25, 14);

			var first = FlankingMath.ChooseFlankWaypoint(from, to, 7, ground, AllPassable, out var firstSide);
			var second = FlankingMath.ChooseFlankWaypoint(from, to, 7, ground, AllPassable, out var secondSide);

			Assert.Multiple(() =>
			{
				Assert.That(second, Is.EqualTo(first), "the same field and axis must yield the same lane every time");
				Assert.That(secondSide, Is.EqualTo(firstSide), "and the same side");
				Assert.That(FlankingMath.LateralOffsetCells(8, 40, 4, 1, 12),
					Is.EqualTo(FlankingMath.LateralOffsetCells(8, 40, 4, 1, 12)), "sizing is pure");
			});
		}

		#endregion
	}
}
