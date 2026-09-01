#region Copyright & License Information
/*
 * WW3MOD capture dispatch maths (2026-09-01).
 *
 * WHAT THIS PINS. Technicians dispatched at several structures at once walk in PARALLEL, so the
 * time until the last structure is taken is the single longest walk — the bottleneck — not the sum
 * of the walks. CaptureDispatchMath.Assign minimises that bottleneck exactly (binary search over
 * distinct edge costs, each threshold tested by a maximum bipartite matching).
 *
 * WHY NOT GREEDY. "Each technician takes its nearest target", or equivalently "repeatedly take the
 * globally cheapest pair", optimises a different number and can strand a technician. GreedyBottleneck
 * below is that algorithm, written out so the fixtures can assert a strictly worse result on the
 * same input rather than merely asserting that ours looks plausible. Both two-unit and three-unit
 * cases are pinned, because the two-unit one is small enough to check by hand and the three-unit one
 * is where a plausible-looking implementation usually starts drifting.
 *
 * WHAT IS DELIBERATELY NOT PINNED. The tie-break towards lower TOTAL cost among bottleneck-optimal
 * assignments is a 2-opt local search and is not claimed to be optimal, so no fixture asserts a
 * minimum sum. Only the bottleneck is asserted, plus the pairings the bottleneck actually forces.
 *
 * WHAT NO FIXTURE HERE CAN COVER. Everything above the maths: whether the right-click gesture reaches
 * the dispatcher, whether the cursor changes, whether the order arrives at a technician. Those need a
 * live World, and this repo has no test infrastructure that builds one (no fixture in OpenRA.Test
 * constructs an Actor). They are checked in-game, by hand, or not at all — do not read a green run
 * here as behavioural cover for the gesture.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	[TestFixture]
	public class CaptureDispatchMathTest
	{
		/// <summary>Largest walk in an assignment — the number the dispatcher is minimising.</summary>
		static long Bottleneck(long[,] cost, int[] assignment)
		{
			var worst = 0L;
			for (var i = 0; i < assignment.Length; i++)
				if (assignment[i] != CaptureDispatchMath.Unassigned)
					worst = Math.Max(worst, cost[i, assignment[i]]);

			return worst;
		}

		static int AssignedCount(int[] assignment)
		{
			var n = 0;
			foreach (var a in assignment)
				if (a != CaptureDispatchMath.Unassigned)
					n++;

			return n;
		}

		/// <summary>
		/// The algorithm we are claiming to beat: repeatedly commit the globally cheapest remaining
		/// capturer/target pair. This is what "send the closest one, then the next closest" does.
		/// </summary>
		static long GreedyBottleneck(long[,] cost)
		{
			var capturers = cost.GetLength(0);
			var targets = cost.GetLength(1);
			var usedCapturer = new bool[capturers];
			var usedTarget = new bool[targets];
			var worst = 0L;

			var pairs = Math.Min(capturers, targets);
			for (var n = 0; n < pairs; n++)
			{
				var bestCost = long.MaxValue;
				var bestI = -1;
				var bestJ = -1;
				for (var i = 0; i < capturers; i++)
				{
					if (usedCapturer[i])
						continue;

					for (var j = 0; j < targets; j++)
					{
						if (usedTarget[j] || cost[i, j] == CaptureDispatchMath.Infeasible)
							continue;

						if (cost[i, j] < bestCost)
						{
							bestCost = cost[i, j];
							bestI = i;
							bestJ = j;
						}
					}
				}

				if (bestI < 0)
					break;

				usedCapturer[bestI] = true;
				usedTarget[bestJ] = true;
				worst = Math.Max(worst, bestCost);
			}

			return worst;
		}

		[TestCase]
		public void GreedyStrandsATechnicianAndTheBottleneckSolverDoesNot()
		{
			// Two technicians at 0 and 5; two structures at 4 and 100.
			// Greedy takes the cheapest pair first — technician B onto the near structure at cost 1 —
			// which leaves technician A the 100 walk. Pairing them the other way costs A only 4 and B
			// only 95, so the last capture lands 5 earlier.
			var cost = new long[,]
			{
				{ 4, 100 },
				{ 1, 95 }
			};

			var assignment = CaptureDispatchMath.Assign(cost);

			Assert.That(AssignedCount(assignment), Is.EqualTo(2), "both technicians should be given a structure");
			Assert.That(assignment[0], Is.EqualTo(0), "the technician at 0 should take the near structure");
			Assert.That(assignment[1], Is.EqualTo(1), "the technician at 5 should take the far structure");

			Assert.That(Bottleneck(cost, assignment), Is.EqualTo(95));
			Assert.That(GreedyBottleneck(cost), Is.EqualTo(100));
			Assert.That(Bottleneck(cost, assignment), Is.LessThan(GreedyBottleneck(cost)),
				"the whole point of the bottleneck solver is that it beats nearest-first here");
		}

		[TestCase]
		public void GreedyAlsoLosesWithThreeTechnicians()
		{
			// Technicians at 0, 10, 20; structures at 11, 21, 100.
			// Only the technician at 20 is anywhere near the far structure, and greedy spends both of
			// the cheap technicians on the two near structures before it looks at it.
			var cost = new long[,]
			{
				{ 11, 21, 100 },
				{ 1, 11, 90 },
				{ 9, 1, 80 }
			};

			var assignment = CaptureDispatchMath.Assign(cost);

			Assert.That(AssignedCount(assignment), Is.EqualTo(3));

			// Forced: the far structure costs 100/90/80, so the last technician must take it for the
			// bottleneck to be 80. Which of the other two takes which near structure is a free choice
			// the tie-break makes, so it is not asserted.
			Assert.That(assignment[2], Is.EqualTo(2), "only the nearest technician can take the far structure");

			Assert.That(Bottleneck(cost, assignment), Is.EqualTo(80));
			Assert.That(GreedyBottleneck(cost), Is.EqualTo(100));
			Assert.That(Bottleneck(cost, assignment), Is.LessThan(GreedyBottleneck(cost)));
		}

		[TestCase]
		public void EveryTargetIsTakenAtMostOnce()
		{
			var cost = new long[,]
			{
				{ 5, 9 },
				{ 6, 3 },
				{ 4, 8 }
			};

			var assignment = CaptureDispatchMath.Assign(cost);

			var seen = new HashSet<int>();
			foreach (var t in assignment)
			{
				if (t == CaptureDispatchMath.Unassigned)
					continue;

				Assert.That(seen.Add(t), Is.True, "a technician was sent at a structure another one already has");
			}
		}

		[TestCase]
		public void SpareTechniciansAreLeftAloneWhenTheyOutnumberTheStructures()
		{
			// Three technicians, two structures: exactly one technician must be left free, because a
			// successful capture consumes the unit and spending a third on nothing is a real loss.
			var cost = new long[,]
			{
				{ 5, 9 },
				{ 6, 3 },
				{ 4, 8 }
			};

			var assignment = CaptureDispatchMath.Assign(cost);

			Assert.That(AssignedCount(assignment), Is.EqualTo(2));
		}

		[TestCase]
		public void EveryTechnicianIsUsedWhenStructuresOutnumberThem()
		{
			// Two technicians, three structures: both go out, one structure waits.
			var cost = new long[,]
			{
				{ 10, 20, 30 },
				{ 30, 20, 10 }
			};

			var assignment = CaptureDispatchMath.Assign(cost);

			Assert.That(AssignedCount(assignment), Is.EqualTo(2));
			Assert.That(Bottleneck(cost, assignment), Is.EqualTo(10),
				"with a free choice of which structure to skip, both technicians should get their cheap one");
		}

		[TestCase]
		public void AForbiddenPairIsNeverAssigned()
		{
			// The first technician cannot reach the second structure at all.
			var cost = new long[,]
			{
				{ 50, CaptureDispatchMath.Infeasible },
				{ 60, 70 }
			};

			var assignment = CaptureDispatchMath.Assign(cost);

			Assert.That(assignment[0], Is.EqualTo(0));
			Assert.That(assignment[1], Is.EqualTo(1));
		}

		[TestCase]
		public void ATechnicianWithNoReachableStructureStaysHome()
		{
			var cost = new long[,]
			{
				{ CaptureDispatchMath.Infeasible, CaptureDispatchMath.Infeasible },
				{ 60, 70 }
			};

			var assignment = CaptureDispatchMath.Assign(cost);

			Assert.That(assignment[0], Is.EqualTo(CaptureDispatchMath.Unassigned));
			Assert.That(assignment[1], Is.Not.EqualTo(CaptureDispatchMath.Unassigned));
		}

		[TestCase]
		public void NoTechniciansOrNoStructuresIsNotAnError()
		{
			Assert.That(CaptureDispatchMath.Assign(new long[0, 0]).Length, Is.EqualTo(0));
			Assert.That(CaptureDispatchMath.Assign(new long[2, 0]),
				Is.EqualTo(new[] { CaptureDispatchMath.Unassigned, CaptureDispatchMath.Unassigned }));
		}

		[TestCase]
		public void TheSameInputAlwaysGivesTheSameAssignment()
		{
			// Order generation runs on one client and is transmitted as explicit per-actor orders, so
			// this is not a lockstep-sync requirement — it is what lets the fixtures above assert an
			// exact pairing instead of only a cost.
			var cost = new long[,]
			{
				{ 7, 7, 7 },
				{ 7, 7, 7 },
				{ 7, 7, 7 }
			};

			var first = CaptureDispatchMath.Assign(cost);
			for (var n = 0; n < 8; n++)
				Assert.That(CaptureDispatchMath.Assign(cost), Is.EqualTo(first));
		}

		[TestCase]
		public void CostMatrixMeasuresCapturerToTargetDistance()
		{
			var capturers = new[] { new WPos(0, 0, 0), new WPos(1024, 0, 0) };
			var targets = new[] { new WPos(3072, 0, 0) };

			var cost = CaptureDispatchMath.CostMatrix(capturers, targets);

			Assert.That(cost.GetLength(0), Is.EqualTo(2));
			Assert.That(cost.GetLength(1), Is.EqualTo(1));
			Assert.That(cost[0, 0], Is.EqualTo(3072));
			Assert.That(cost[1, 0], Is.EqualTo(2048));
		}

		// ---- the "already committed" rule ----

		[TestCase]
		public void AnIdleTechnicianIsAvailable()
		{
			Assert.That(CaptureDispatchMath.IsAvailableFor(0, 42), Is.True);
		}

		[TestCase]
		public void ATechnicianCapturingSomethingElseIsNotStolen()
		{
			// The load-bearing case. A technician is CONSUMED by a successful capture, so pulling one
			// off an in-flight job does not merely delay that job — nothing returns to finish it.
			Assert.That(CaptureDispatchMath.IsAvailableFor(7, 42), Is.False);
		}

		[TestCase]
		public void ATechnicianAlreadyHeadingAtThisStructureIsNotASecondDispatch()
		{
			// Re-issuing the same dispatch must be idempotent, or a double right-click spends two
			// technicians on one building.
			Assert.That(CaptureDispatchMath.IsAvailableFor(42, 42), Is.True);
		}

		[TestCase]
		public void AStructureSomeoneIsAlreadyWalkingAtCountsAsCovered()
		{
			var committed = new uint[] { 0, 7, 42 };

			Assert.That(CaptureDispatchMath.IsAlreadyCovered(committed, 42), Is.True);
			Assert.That(CaptureDispatchMath.IsAlreadyCovered(committed, 7), Is.True);
			Assert.That(CaptureDispatchMath.IsAlreadyCovered(committed, 99), Is.False);
		}

		[TestCase]
		public void IdleTechniciansDoNotMakeAStructureLookCovered()
		{
			// 0 means "not committed to anything". It must never match a target id, or the first idle
			// technician in the list would make every structure look taken.
			Assert.That(CaptureDispatchMath.IsAlreadyCovered(new uint[] { 0, 0, 0 }, 0), Is.False);
		}
	}
}
