#region Copyright & License Information
/*
 * WW3MOD — where a recon drone gets sent, and whether it may be sent at all (pure math).
 *
 * Split out of DroneOperatorBotModule so the selection rules can be tested without standing up a
 * world. Every function here is a pure function of its arguments: no world, no RNG, no tick clock.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Why a candidate cell was refused. Diagnostic only — no decision reads it.</summary>
	public enum DroneRefusal
	{
		None,
		TooLittleRevealed,
		TooFarFromPoi,
		TooDangerous
	}

	public static class DroneTaskingMath
	{
		/// <summary>Score returned for a candidate that must never be chosen. Below every real score.</summary>
		public const long Ineligible = long.MinValue;

		/// <summary>
		/// The furthest a drone may be asked to hover from the operator, in cells.
		///
		/// TWO SEPARATE LIMITS BIND HERE AND THE SMALLER ONE WINS.
		/// (1) The weapon: DroneTargeter's range is what decides whether the launch order can be
		/// executed from where the operator is standing at all.
		/// (2) The leash: CarrierSlave.MaxDistance (25 cells, aircraft.yaml) checked every
		/// MaxDistanceCheckTicks. Past it the drone is NOT recalled — it is dragged 10% back and
		/// granted lost-connection, which zeroes its vision. A drone sitting past the leash is
		/// therefore worse than useless: it is a blind unit the bot believes is scouting.
		///
		/// CarrierMasterInfo.MaxSlaveDistance is NOT one of these limits. It has no readers
		/// engine-wide (CarrierMaster.cs:38) — the field is inert, and the 20c0 on ^DR is decoration.
		/// Anything built on that number is standoff arithmetic against a constant nothing reads.
		///
		/// The margin exists because the leash check is periodic rather than continuous and the
		/// operator may be nudged after launch; sitting exactly on the boundary invites the
		/// lost-connection grant on the next check.
		/// </summary>
		public static int MaxHoverDistanceCells(int weaponRangeCells, int leashCells, int marginCells)
		{
			var leash = leashCells - marginCells;
			if (leash < 0)
				leash = 0;

			return weaponRangeCells < leash ? weaponRangeCells : leash;
		}

		/// <summary>
		/// Score a candidate HOVER cell by the unobserved ground the drone would REVEAL from there.
		///
		/// THIS REPLACED A SCORING MODEL THAT COULD NEVER FIRE, AND THE DIFFERENCE IS THE WHOLE POINT.
		/// The previous version scored the hover cell by its OWN staleness. That is unsatisfiable by
		/// construction: the hover cell must be within the drone's leash of the operator (22 cells),
		/// while the operator itself verifies everything within 28 cells (^StandardVision's bands down
		/// to strength 2 — the strength-1 band at 28-32 does NOT verify, because
		/// ControlField.GridCellVisible tests MapLayers.IsVisible(cell, 1) and that comparison is
		/// STRICT, MapLayers.cs:579). So every reachable hover cell sits inside the operator's own
		/// verified bubble and is permanently fresh. Measured over one match: 674,584 of 674,584
		/// candidates refused as too fresh, exactly 100%, across 582 evaluations and 70k ticks.
		///
		/// What makes a drone worth launching is not that the cell it sits on is unknown — it is that
		/// the drone carries its own vision (quadcopterdrone inherits ^StandardVision via
		/// ^Drone -> ^Airborne -> ^NeutralAirborne), so parked at the leash edge it verifies a bubble
		/// centred 22 cells out and sees ground the operator cannot. Ground already inside the
		/// operator's bubble contributes nothing here automatically, because it is not stale — the
		/// exclusion needs no special case.
		///
		/// revealedStaleSquares is a count of COARSE ControlField grid squares, not map cells, and the
		/// caller obtains it in O(1) from a summed-area table. See DroneOperatorBotModule for why the
		/// resolution and the table are both load-bearing for cost rather than for correctness.
		/// </summary>
		/// <remarks>
		/// The danger term is AIR danger: this is where the DRONE hovers, and it dies to one hit of
		/// real AA (50 HP; its 1-world-unit hitshape buys nothing, since HitShape.TargetablePositions
		/// never reads Radius). Losing it costs 25 supply and leaves the operator holding a respawned
		/// drone it cannot launch, because `loaded` is re-granted while ammo-primary is still 0. The
		/// operator's own launch cell is judged on GROUND danger, separately, by the caller.
		/// </remarks>
		public static long ScoreCandidate(
			int revealedStaleSquares,
			int minRevealedSquares,
			int poiDistanceCells,
			int maxPoiDistanceCells,
			int airDanger,
			int maxAirDanger,
			int contactBonus)
		{
			return ScoreCandidate(revealedStaleSquares, minRevealedSquares, poiDistanceCells,
				maxPoiDistanceCells, airDanger, maxAirDanger, contactBonus, out _);
		}

		/// <summary>As the overload above, additionally reporting WHY a candidate was refused.
		/// Diagnostic only — no decision reads it — and it exists because "no eligible cell" and
		/// "which of three thresholds rejected every cell" are different questions, and only the
		/// second is actionable from a match log. That distinction is what identified the defect this
		/// function replaces.</summary>
		public static long ScoreCandidate(
			int revealedStaleSquares,
			int minRevealedSquares,
			int poiDistanceCells,
			int maxPoiDistanceCells,
			int airDanger,
			int maxAirDanger,
			int contactBonus,
			out DroneRefusal refusal)
		{
			// SINGLE IMPLEMENTATION, deliberately. The refusal reason is reported through an out
			// parameter rather than by a parallel diagnostic predicate, because a second copy of these
			// thresholds is exactly the kind of duplication that drifts and then lies in the log.
			refusal = DroneRefusal.None;

			// Not enough unseen ground to be worth a 60s sortie. This is also the guard that stops the
			// drone being spent to reveal one stale square at the edge of an otherwise-known area.
			if (revealedStaleSquares < minRevealedSquares)
			{
				refusal = DroneRefusal.TooLittleRevealed;
				return Ineligible;
			}

			// Outside the band of ground anyone is contesting. This is the unreachable-corner guard:
			// the most unobserved ground on any map is usually where nothing will ever happen.
			if (poiDistanceCells > maxPoiDistanceCells)
			{
				refusal = DroneRefusal.TooFarFromPoi;
				return Ineligible;
			}

			// The drone is unarmed and dies to one hit of real AA; hovering it over a hot square is
			// donating 25 supply and the next sortie with it.
			if (airDanger > maxAirDanger)
			{
				refusal = DroneRefusal.TooDangerous;
				return Ineligible;
			}

			// Revealed area dominates; the contact bonus expresses "prefer ground someone is believed
			// to be on"; POI distance breaks ties toward the contested middle. Scaled so that one
			// extra revealed square outweighs a one-cell POI-distance difference.
			return ((long)revealedStaleSquares * 1000) + contactBonus - poiDistanceCells;
		}

		/// <summary>
		/// Build an inclusive summed-area table over a grid of 0/1 values.
		/// <paramref name="sat"/> must be (gw+1) x (gh+1); row 0 and column 0 stay zero and are the
		/// sentinel border the query subtracts against.
		///
		/// THE THRESHOLD IS APPLIED HERE, AT BUILD TIME, and that is not a detail. The table sums an
		/// INDICATOR (is this square unobserved: 1 or 0). A table built over raw staleness values and
		/// thresholded at query time would sum ticks, and the resulting number would be meaningless —
		/// large where one square is ancient rather than where many squares are unseen.
		/// </summary>
		public static void BuildSummedArea(int[,] sat, int gw, int gh, Func<int, int, bool> isSet)
		{
			for (var x = 0; x < gw; x++)
			{
				for (var y = 0; y < gh; y++)
				{
					var v = isSet(x, y) ? 1 : 0;
					sat[x + 1, y + 1] = v + sat[x, y + 1] + sat[x + 1, y] - sat[x, y];
				}
			}
		}

		/// <summary>
		/// Count of set squares in the INCLUSIVE rectangle [x0..x1] x [y0..y1], clamped to the grid.
		/// Four array reads regardless of the rectangle's size — which is the whole reason the drone's
		/// vision radius costs nothing per candidate.
		///
		/// Clamping happens BEFORE the corner reads so a box hanging off two edges at once is still a
		/// valid query rather than an index throw or a silently wrong sum. An off-by-one here does not
		/// crash: it mis-scores every candidate near a grid edge, symmetrically, in a way that no
		/// score-comparison test would notice — hence the explicit boundary tests.
		/// </summary>
		public static int SumInclusive(int[,] sat, int gw, int gh, int x0, int y0, int x1, int y1)
		{
			if (x0 < 0) x0 = 0;
			if (y0 < 0) y0 = 0;
			if (x1 > gw - 1) x1 = gw - 1;
			if (y1 > gh - 1) y1 = gh - 1;
			if (x0 > x1 || y0 > y1)
				return 0;

			return sat[x1 + 1, y1 + 1] - sat[x0, y1 + 1] - sat[x1 + 1, y0] + sat[x0, y0];
		}

		/// <summary>
		/// Whether a launch order may be issued THIS tick.
		///
		/// EVERY TERM IS A SEPARATE FAILURE THAT LOOKS LIKE SUCCESS.
		///
		/// armamentReady: ^DR's primary Armament is PauseOnCondition "!loaded || !ammo-primary", so
		/// an unpaused armament is exactly "a drone is docked AND there is ammo to replace it if it
		/// dies". Reading the armament's own pause state rather than re-deriving those two conditions
		/// is deliberate: it cannot drift out of agreement with the YAML gate. Note the nasty state
		/// this catches — after a drone is killed the quadcopter respawns in ~9s and re-grants
		/// `loaded`, but ammo-primary is 0, so the operator visibly HAS a drone and cannot launch it.
		///
		/// noDroneAirborne: the retarget branch (CarrierMaster.cs:137-140) is unreachable for ^DR, so
		/// a second launch order while one is out cannot redirect anything. The reason is the launch
		/// itself: it revokes `loaded` (:161-162), which pauses the only armament, so Attacking()
		/// cannot be re-entered while a drone is out. A second order would burn the 3s FireDelay and a
		/// 12s BurstWait for nothing.
		///
		/// isStationary: the operator must not be MOVING. CarrierMaster carries PauseOnCondition
		/// "moving", and TraitPaused calls SetConnection(false) AND Recall() (CarrierMaster.cs:318-322)
		/// — so movement throws the sortie away. It also matters at launch: Attacking() early-returns
		/// on IsTraitPaused, and the spawn happens inside the FireDelay callback (Armament.cs:692), so
		/// a still-moving operator fires the weapon, starts the cooldown and spawns NOTHING.
		///
		/// THIS TERM IS "NOT MOVING", NOT "IDLE", AND THE DIFFERENCE IS THE WHOLE BUG.
		/// After its first launch the operator is never idle again: the Attack activity holds
		/// indefinitely because ChooseArmamentsForTarget filters IsTraitDisabled but not
		/// IsTraitPaused, and ^DR does not opt into AbandonWhenArmamentsPaused (Attack.cs:248-256
		/// documents exactly this wedge). An idle gate therefore latches false forever and the module
		/// issues exactly one sortie per operator for the rest of the match. A wedged operator is
		/// standing perfectly still and is a legitimate launch platform — what must be excluded is a
		/// WALKING one, which is what this asks.
		///
		/// inRange: force-firing a cell outside weapon range makes the attack activity WALK there,
		/// which grants `moving` and defeats the point of standing off.
		/// </summary>
		public static bool CanLaunch(
			bool armamentReady,
			bool noDroneAirborne,
			bool isStationary,
			int targetDistanceCells,
			int maxHoverDistanceCells)
		{
			if (!armamentReady || !noDroneAirborne || !isStationary)
				return false;

			return targetDistanceCells >= 0 && targetDistanceCells <= maxHoverDistanceCells;
		}

		/// <summary>
		/// Whether to issue a fresh launch order this cycle.
		///
		/// WHY THIS EXISTS AT ALL. The engine re-fires a held Attack activity by itself every time
		/// `loaded` comes back, so an operator left alone keeps shuttling its drone to the ONE cell it
		/// was first given — a fixed observation post, not the rolling sweep this module is for. The
		/// module only gets a different cell by ordering one, and an unqueued order is what clears the
		/// held activity (Actor.QueueActivity(false, …) calls CancelActivity first). So: re-order to
		/// move the post, stay silent to keep it.
		///
		/// settleTicks guards the FireDelay window: the spawn is a delayed action owned by the Armament,
		/// not by the activity, so re-ordering inside that gap does not cancel the pending launch — it
		/// just aims the operator somewhere else while the drone still departs for the old cell.
		///
		/// BE CLEAR ABOUT WHAT ACTUALLY PROTECTS THAT WINDOW TODAY: it is NOT this parameter. The
		/// caller stamps OrderedTick from the tick captured in its evaluation, and evaluations are
		/// exactly ReevaluateInterval apart, so ticksSinceLaunchOrder is always ReevaluateInterval at
		/// the shipped cadence and this branch is unreachable. The real invariant is
		/// ReevaluateInterval (200) > FireDelay (50), which nothing asserts. settleTicks is insurance
		/// that keeps the rule correct if the cadence is ever lowered, not a live guard — do not read
		/// its presence as evidence the window is defended.
		/// </summary>
		public static bool ShouldRetask(
			bool hasStandingOrder,
			bool sameCellAsOrdered,
			int ticksSinceLaunchOrder,
			int settleTicks)
		{
			if (!hasStandingOrder)
				return true;

			if (ticksSinceLaunchOrder < settleTicks)
				return false;

			// The held activity already re-fires at this cell unprompted; re-ordering it would cancel
			// and rebuild the same activity for nothing.
			return !sameCellAsOrdered;
		}

		/// <summary>
		/// Whether a square we previously sent a drone to has been covered well enough to retire it
		/// from the candidate set. Retiring is what stops the module re-picking one hot square every
		/// cycle while the rest of the map goes stale.
		/// </summary>
		public static bool IsCovered(int ticksSinceVerified, int minStalenessTicks)
		{
			return ticksSinceVerified < minStalenessTicks;
		}
	}
}
