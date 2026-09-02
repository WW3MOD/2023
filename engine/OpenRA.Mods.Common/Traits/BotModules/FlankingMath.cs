#region Copyright & License Information
/*
 * WW3MOD @experimental offense — FLANKING MANEUVER geometry + converge synchronisation (pure math).
 *
 * PERCEIVED BEHAVIOUR: an attack axis stops arriving as one frontal blob. A minority FLANK ELEMENT peels
 * off, swings wide through a lateral waypoint and comes in on a second bearing; the majority MAIN ELEMENT
 * presses the direct axis but HOLDS at standoff until the flank is level with it, so the two arrive
 * together instead of feeding in piecemeal. To a human it reads as a pincer; to the defender it is two
 * bearings at once, which is the lethality claim.
 *
 * RELATIONSHIP TO STAGE-E (GroundDangerNav.DetourWaypoint): Stage E is danger AVOIDANCE — it returns null
 * the moment the beeline is already safe, because its whole job is to skirt a strongpoint. Flanking is a
 * DOCTRINE, so it must fire against an undefended approach too; that is why this is separate math and not
 * a parameter on DetourWaypoint. What it does reuse is Stage E's exposure measure
 * (GroundDangerNav.PathMaxGroundDanger) and its RoundDiv, deliberately rather than by copy: the perpendicular
 * scale and the sign-correct rounding are exactly the "subtle logic" this codebase has already watched
 * diverge across copies (see BotGeometry's header). One line-walk, two callers.
 *
 * SIDE SELECTION is Stage E's rule applied to a different question: evaluate the +1 and -1 lateral lanes,
 * take the one whose worst-case exposure along from->waypoint->target is LOWER, strict merit, +1 first so a
 * tie breaks by iteration order. Against the Stage-B/C territory baseline the cheaper side is the one that
 * skirts believed-enemy ground, so "swing around the weak shoulder" emerges from the field rather than being
 * scripted.
 *
 * CONVERGE SYNCHRONISATION is an approximation and is meant to be read as one: the flank's remaining route
 * is the two-leg CHEBYSHEV length through its waypoint, not a pathfinder query, so broken ground makes the
 * real flank slower than this model believes. The hold is therefore BOUNDED (HoldBudgetExhausted /
 * StepHold, mirroring FrontlineAllocationMath.PostureBudgetExhausted / StepPostureHold) — an unbounded wait
 * on an estimate is a veto wearing the costume of a delay, which is the failure the Phase-5 posture hold
 * already paid for once. The budget is MONOTONE within an axis lifetime and is never refunded on release,
 * because refunding it converts a terminating condition into a hold/advance duty cycle.
 *
 * DEGRADE: a flank element that is already in contact stops being a maneuver and starts being a fight the
 * main element is sitting out. FlankEngaged short-circuits the hold to false so both elements commit.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws. Integer-only geometry over a fixed two-element
 * candidate order with an iteration-order tie-break; every scale uses Exts.ISqrt / RoundDiv rather than
 * floating point, so two clients over the same synced field pick the same lane. The caller partitions the
 * force by a stable ActorID order for the same reason.
 *
 * v3-portable: engine-free static math (NUnit-pinned in FlankingMathTest); only the tasking plumbing that
 * consumes it (PoiOffensiveBotModule.OrderFlankElement / OrderConvergeHold) is engine-specific.
 */
#endregion

using System;
using OpenRA.Mods.Common.Traits.BotModules.Squads;

namespace OpenRA.Mods.Common.Traits
{
	public static class FlankingMath
	{
		/// <summary>No lateral lane was viable (both sides unreachable, or a degenerate axis).</summary>
		public const int SideNone = 0;

		// Deterministic candidate order for the two lateral lanes: +1 before -1, so a tie between two
		// equally-exposed sides always resolves the same way. Matches GroundDangerNav.DetourSides.
		static readonly int[] FlankSides = { 1, -1 };

		/// <summary>Chebyshev (max-norm) cell distance — the metric the offense module already measures axis
		/// geometry in, so a "remaining distance" compared here means the same thing it does there.</summary>
		public static int CellDistance(CPos a, CPos b)
		{
			var dx = Math.Abs(a.X - b.X);
			var dy = Math.Abs(a.Y - b.Y);
			return dx > dy ? dx : dy;
		}

		/// <summary>Is this force worth splitting at all? A force must be big enough that BOTH elements are
		/// still a fighting unit after the cut (splitting four hulls into 3+1 is not a pincer, it is feeding
		/// them in one at a time) and far enough from the objective that there is room to swing wide before
		/// contact. Both bars are configured, not baked.</summary>
		public static bool ShouldSplit(int forceSize, int distToTargetCells, int minForceSize, int minApproachCells)
		{
			return forceSize >= minForceSize && distToTargetCells >= minApproachCells;
		}

		/// <summary>How many units go to the FLANK element — the minority, so the main element remains the
		/// force the defender must face. Returns 0 when no viable split exists, which is the caller's signal
		/// to fall through to the undivided single-group path.</summary>
		public static int FlankElementSize(int forceSize, int sharePct, int minElementSize)
		{
			if (minElementSize < 1)
				minElementSize = 1;

			// Both elements must clear the floor, so a force under twice the floor can never split.
			if (forceSize < 2 * minElementSize)
				return 0;

			var flank = forceSize * sharePct / 100;

			if (flank < minElementSize)
				flank = minElementSize;

			var maxFlank = forceSize - minElementSize;
			if (flank > maxFlank)
				flank = maxFlank;

			return flank;
		}

		/// <summary>How far off-axis the flank swings, in cells. Scales with the force (a bigger element needs
		/// a wider berth to be a separate bearing rather than a wider blob) and is clamped two ways: an
		/// absolute ceiling, and never more than half the approach — beyond that the "flank" is a longer walk
		/// than the assault and the angle it buys stops being worth the time. 0 ⇒ no room, do not split.</summary>
		public static int LateralOffsetCells(int forceSize, int distToTargetCells, int baseCells, int perUnitCells, int maxCells)
		{
			if (distToTargetCells <= 0)
				return 0;

			var offset = baseCells + perUnitCells * forceSize;

			if (offset > maxCells)
				offset = maxCells;

			var halfApproach = distToTargetCells / 2;
			if (offset > halfApproach)
				offset = halfApproach;

			return offset < 1 ? 0 : offset;
		}

		/// <summary>The raw lateral waypoint on <paramref name="side"/> (+1 / -1): the midpoint of the approach
		/// pushed <paramref name="offsetCells"/> perpendicular to it. Perpendicular to from→to is (-dy, dx),
		/// scaled through the integer axis length. RAW — neither bounds- nor terrain-tested; ChooseFlankWaypoint
		/// is what applies the passability guard, and no caller should order a unit to this cell directly.</summary>
		public static CPos Waypoint(CPos from, CPos to, int offsetCells, int side)
		{
			var dx = to.X - from.X;
			var dy = to.Y - from.Y;
			var axisLen = Exts.ISqrt(dx * dx + dy * dy);
			if (axisLen <= 0)
				return from;

			var mx = from.X + dx / 2;
			var my = from.Y + dy / 2;
			var mag = side * offsetCells;

			return new CPos(
				mx + GroundDangerNav.RoundDiv(-dy * mag, axisLen),
				my + GroundDangerNav.RoundDiv(dx * mag, axisLen));
		}

		/// <summary><para>Picks the lateral lane the flank element takes: of the two candidate sides, the one
		/// whose worst-case ground-danger exposure along from→waypoint→target is LOWER. Strict merit, so an
		/// exact tie keeps the first-iterated side (+1) and the choice is stable across clients.</para>
		///
		/// <para>Unlike the Stage-E detour this does NOT require the lane to beat the direct route — the flank
		/// exists to open a second bearing, not to dodge, so it fires against a clear approach too. A candidate
		/// whose waypoint cell the mover cannot stand on is discarded (unstamped water and cliff read danger 0,
		/// i.e. maximally safe, and would otherwise be actively preferred); if NEITHER side survives that guard
		/// the result is null with <paramref name="side"/> = SideNone, and the caller must fall back to the
		/// undivided path rather than ordering a flank into terrain.</para></summary>
		public static CPos? ChooseFlankWaypoint(CPos from, CPos to, int offsetCells,
			Func<CPos, int> groundDangerAt, Func<CPos, bool> waypointPassable, out int side)
		{
			side = SideNone;

			if (offsetCells < 1)
				return null;

			var dx = to.X - from.X;
			var dy = to.Y - from.Y;
			if (Exts.ISqrt(dx * dx + dy * dy) <= 0)
				return null;

			var best = int.MaxValue;
			CPos? bestWp = null;

			foreach (var candidate in FlankSides)
			{
				var wp = Waypoint(from, to, offsetCells, candidate);

				if (!waypointPassable(wp))
					continue;

				var worst = Math.Max(
					GroundDangerNav.PathMaxGroundDanger(from, wp, groundDangerAt),
					GroundDangerNav.PathMaxGroundDanger(wp, to, groundDangerAt));

				if (worst < best)
				{
					best = worst;
					bestWp = wp;
					side = candidate;
				}
			}

			return bestWp;
		}

		/// <summary>The flank element's remaining route: both legs of from→via→target, Chebyshev. This is the
		/// synchronisation estimate, and it is an ESTIMATE — it assumes open ground, so broken terrain makes
		/// the real flank slower than this reads. The hold budget exists because of that.</summary>
		public static int RouteRemainingCells(CPos from, CPos via, CPos to)
		{
			return CellDistance(from, via) + CellDistance(via, to);
		}

		/// <summary><para>Should the MAIN element hold at standoff and let the flank catch up? Three ways to
		/// answer no, in order:</para>
		///
		/// <para>(1) the flank is already ENGAGED — then it is not maneuvering, it is fighting, and holding the
		/// main element back means the defender fights half a force twice. Commit both.
		/// (2) the main element is still further out than <paramref name="standoffCells"/> — the converge is a
		/// decision made at the objective, not a reason to dawdle across open ground.
		/// (3) the flank's remaining route is already within <paramref name="toleranceCells"/> of the main's —
		/// it is level, so go.</para>
		///
		/// <para>Otherwise hold. Pure comparison of two integers the caller measured; the CALLER owns the
		/// budget that stops this being permanent (see HoldBudgetExhausted).</para></summary>
		public static bool MainShouldHold(int mainRemainingCells, int flankRemainingCells,
			int standoffCells, int toleranceCells, bool flankEngaged)
		{
			if (flankEngaged)
				return false;

			if (mainRemainingCells > standoffCells)
				return false;

			return flankRemainingCells > mainRemainingCells + toleranceCells;
		}

		/// <summary><para>True when this axis has spent its converge-hold budget and must assault regardless of
		/// where the flank has got to. Mirrors FrontlineAllocationMath.PostureBudgetExhausted.</para>
		///
		/// <para>A budget of 0 or less reads as ALREADY EXHAUSTED, not as unbounded. That direction is chosen
		/// deliberately: the caller also declines to enter the hold path at 0, so the two agree today, but if
		/// that guard is ever dropped the failure mode here is "never holds" rather than "holds forever" — and
		/// a permanent hold is the silent freeze this whole budget exists to prevent.</para></summary>
		public static bool HoldBudgetExhausted(int holdEvals, int maxHoldEvals)
		{
			return maxHoldEvals <= 0 || holdEvals >= maxHoldEvals;
		}

		/// <summary>Steps the converge-hold counter. MONOTONE and saturating within one axis lifetime: it is
		/// deliberately NOT refunded when the axis stops holding, because giving the budget back on the release
		/// eval turns a terminating condition into a duty-cycle limiter — the axis re-holds next eval and the
		/// pair oscillates. Mirrors FrontlineAllocationMath.StepPostureHold.</summary>
		public static int StepHold(int holdEvals, bool holding, int maxHoldEvals)
		{
			if (!holding || maxHoldEvals <= 0)
				return holdEvals;

			if (holdEvals >= maxHoldEvals)
				return holdEvals;

			return holdEvals + 1;
		}
	}
}
