#region Copyright & License Information
/*
 * WW3MOD — the single mover-bound terrain test the bot layers order against.
 *
 * PERCEIVED BEHAVIOUR: bots stop ordering units onto cells they cannot stand on — on-map water and cliff.
 *
 * WHY THIS EXISTS AS ONE FUNCTION. The predicate below was written out three times (PoiOffensiveBotModule,
 * SupplyFollowerBotModule, and inline in CaptureCoordinatorBotModule) and consumed by every "walk to a cell"
 * decision in the strategic layer. Three copies of one subtle test is the shape that produced the
 * phantom-anchor class — three copies of a grid descent, two of them wrong, found only after the divergence
 * had shipped. The bodies were identical when this was extracted (2026-08-17); keeping them identical is not
 * something prose can enforce, so there is one body.
 *
 * WHY IT IS NOT IN ForwardStagingMath. That class is deliberately engine-free so it can be pinned in NUnit
 * without mounting a world. This one needs Actor/Mobile/Locomotor, so it stays on the plumbing side of that
 * seam and is passed INTO the pure math as a delegate.
 */
#endregion

using System;
using OpenRA.Mods.Common.Pathfinder;

namespace OpenRA.Mods.Common.Traits
{
	public static class BotTerrain
	{
		/// <summary>How far the ENGINE will silently move a bot's destination. Both order paths a bot uses run
		/// the cell through <see cref="Mobile.NearestMoveableCell(CPos, int, int)"/>, whose default budget is a radius-10 annulus
		/// (Mobile.cs:814) — "Move" via Mobile.ResolveOrder (Mobile.cs:1073), "AttackMove" via
		/// AttackMove.ResolveOrder (AttackMove.cs:125). A bot clamping its own destination should use the same
		/// reach: clamping SHORTER gives up on deliveries the engine would have completed, and clamping FURTHER
		/// picks a cell the engine would not have chosen, so the two disagree again in the other direction.
		///
		/// <para>MATCHING THE RADIUS DOES NOT MAKE THE TWO AGREE OUTRIGHT, and as of 2026-08-30 the difference is
		/// NO LONGER NARROW. <see cref="PassableFor"/> tests TERRAIN only, whereas NearestMoveableCell requires
		/// THREE things: <c>CanEnterCell(..., BlockedByActor.Immovable)</c>, <c>CanStayInCell</c>, and now
		/// <c>CanReach</c> — a domain compare proving a path exists at all (Mobile.cs:853-858, :896). The first two
		/// were always here: a cell this clamp accepts as terrain-passable but which is occupied by a building, or
		/// is transit-only, is still relocated by the engine, and any module measuring against the cell it asked
		/// for is back in the original trap.</para>
		///
		/// <para>THE THIRD TERM IS NEW AND CHANGES THE SHAPE OF THE HOLE, not just its size. What used to keep the
		/// two aligned in the common case was the EARLY RETURN: the bot clamped to a standable cell, the engine's
		/// first <c>if</c> accepted it unchanged, the annulus never ran, and the two scan orders never had a chance
		/// to disagree. That early return now also requires <c>CanReach</c>, so a bot-clamped cell in a DIFFERENT
		/// CONNECTED COMPONENT falls through to the annulus and the engine returns a different cell — picked by a
		/// different scan order entirely (<see cref="FiresStandoffMath"/>'s NearestPassableCell walks Chebyshev
		/// rings; Map.FindTilesInAnnulus walks Euclidean-then-hash). Measured with nav-guard, river-zeta has 33
		/// components for a wheeled unit, so the new term can reject a clamped cell across any of the other 32.</para>
		///
		/// <para>THE SIGN OF THAT CHANGE IS UNMEASURED and deliberately not "fixed" back into alignment. Before the
		/// change, a carrier ordered to an unreachable drop cell also never arrived — it stalled STATIONARY instead
		/// of stalling ten cells away, which may well be the worse of the two. Nobody has built a scenario either
		/// way. Callers leaning on this contract: SquadManagerBotModule.cs:403, ScoutBotModule.cs:132,
		/// EngineerRouteOpenBotModule.cs:379, MountedTransportBotModule.cs:1425, LayeredDefenceBotModule.cs:530
		/// and :687. Closing the hole properly needs an occupancy-and-reachability-aware oracle, which is not a
		/// pure predicate and is not what this helper provides.</para></summary>
		public const int EngineRelocationCells = 10;

		/// <summary>A terrain-passability predicate bound to <paramref name="mover"/>'s locomotor: true when that
		/// mover can actually stand on the cell (not on-map water/cliff, not off-map). What is impassable depends
		/// on the MOVER — a cell an infantryman can hold is not one a tank can — so this must be bound to the unit
		/// being ordered, not to a representative of its group. Falls back to "all passable" when the mover has no
		/// <see cref="Mobile"/> (it then has no locomotor to answer with, and refusing every cell would be the
		/// worse failure).</summary>
		public static Func<CPos, bool> PassableFor(Actor mover)
		{
			var loco = mover.TraitOrDefault<Mobile>()?.Locomotor;
			if (loco == null)
				return _ => true;

			return c => loco.MovementCostForCell(c) != PathGraph.MovementCostForUnreachableCell;
		}

		/// <summary>The cell a ground unit should actually be ORDERED to, given the one the bot picked:
		/// <paramref name="ideal"/> if the mover can stand there, otherwise the nearest cell within
		/// <paramref name="clampCells"/> that it can. Returns false — and leaves <paramref name="cell"/> at
		/// <paramref name="ideal"/> — when nothing standable is in reach, which is the caller's signal to issue
		/// no order rather than a doomed one.
		///
		/// <para>WHY A CLAMP AND NOT JUST A REJECTION. The engine already relocates: both order paths a bot uses
		/// run the destination through <see cref="Mobile.NearestMoveableCell(CPos, int, int)"/> over a radius-10 annulus
		/// (Mobile.cs:1073 for "Move", AttackMove.cs:125 for "AttackMove"). So a bounds-only destination rarely
		/// strands a unit — it silently MOVES THE GOALPOSTS, and the bot is never told. That is worse than a
		/// stall wherever the module then measures against the cell it asked for: MountedTransportBotModule
		/// compares the carrier's position to its drop cell within DropOffArrivalRadius (3), the engine parks it
		/// up to 10 cells away, and the arrival test can then never pass — the carrier re-issues the same move
		/// forever and never unloads. Clamping HERE makes the bot's cell and the engine's cell the same cell.</para>
		///
		/// <para><paramref name="passable"/> is not optional and throws when null, for the reason
		/// <see cref="ForwardStagingMath.SpreadSlot"/> gives: both call sites of the spread assembled a
		/// bounds-only guard identically and nothing said so. "This mover can stand anywhere" is a thing a
		/// caller can say — with an all-true predicate, explicitly — and aircraft callers do say it.</para></summary>
		public static bool TryNearestStandable(CPos ideal, int clampCells,
			Func<CPos, bool> inBounds, Func<CPos, bool> passable, out CPos cell)
		{
			if (passable == null)
				throw new ArgumentNullException(nameof(passable), "a bot destination must be terrain-tested for the mover it is ordering");

			// Bounds and terrain are one oracle to the search, but stay two arguments to the caller: a mover with
			// no Mobile gets an all-true `passable` (see PassableFor), which would otherwise admit off-map cells.
			bool Standable(CPos c) => (inBounds == null || inBounds(c)) && passable(c);

			cell = FiresStandoffMath.NearestPassableCell(ideal, clampCells, Standable);
			return Standable(cell);
		}

		/// <summary>The cell a ground unit should actually be ORDERED to when a line its owner may not cross
		/// — the DEFCON 3 border — sits between the mover and the cell the bot picked. Walks back from
		/// <paramref name="ideal"/> along the straight line toward <paramref name="from"/> to the first cell on
		/// the mover's own side, then clamps THAT to standable ground exactly as
		/// <see cref="TryNearestStandable"/> does. Returns false — the caller's signal to issue no order — when
		/// no legal cell is reachable.
		///
		/// <para>WHY A STAGING CELL AND NOT A REFUSAL. The engine refuses a "Move" beyond the border
		/// (Mobile.cs:1111-1115) but ACCEPTS an "AttackMove" and then does nothing with it: AttackMove has no
		/// border guard, <see cref="Mobile.NearestMoveableCell(CPos, int, int)"/> fails its CanReach term on the far cell,
		/// searches a radius-10 annulus that is also beyond the border, returns the cell unchanged, and Move
		/// finds no path and COMPLETES — so the unit does not move, does not turn and says nothing
		/// (Mobile.cs:894-898). The bot is told nothing either way. A module that records "ordered" against that
		/// cell has parked its axis for the rest of the phase. Staging on the near side is an order the unit can
		/// actually execute, so the bot's cell and the unit's cell are the same cell again.</para>
		///
		/// <para>THE WALK IS BACK ALONG THE APPROACH AXIS, not perpendicular to the line. The perpendicular foot
		/// (DefconWall.NearestPositionOnOwnSide) is the right answer for UNDOING a violation — it is the shortest
		/// way out — but this is the opposite problem: the unit has not moved yet, and the useful place to put it
		/// is on the line it will advance along when the border opens. It also needs no geometry, only the
		/// predicate, which is what keeps this pure and testable without mounting a world.</para>
		///
		/// <para><paramref name="isBeyondLine"/> null — no wall trait, or the wall is down — makes this exactly
		/// <see cref="TryNearestStandable"/>, so every caller can route through it unconditionally and Skirmish
		/// pays one null test. The relocation clamp is conjoined with the same predicate: without that,
		/// NearestPassableCell is free to relocate the staging cell straight back across the line.</para></summary>
		public static bool TryStageOnNearSide(CPos ideal, CPos from, Func<CPos, bool> isBeyondLine,
			int clampCells, Func<CPos, bool> inBounds, Func<CPos, bool> passable, out CPos cell)
		{
			if (passable == null)
				throw new ArgumentNullException(nameof(passable), "a bot destination must be terrain-tested for the mover it is ordering");

			if (isBeyondLine == null || !isBeyondLine(ideal))
				return TryNearestStandable(ideal, clampCells, inBounds, passable, out cell);

			// Never relocate back across the line: the clamp below searches outward from the staging cell and
			// the far side is usually the nearer open ground.
			bool NearSidePassable(CPos c) => passable(c) && !isBeyondLine(c);

			if (isBeyondLine(from))
			{
				// The mover is already on the wrong side. Nothing here can order it home — that is
				// DefconWallTurnBack's job for aircraft and the player's for ground — and inventing a
				// destination would fight whatever is doing it.
				cell = ideal;
				return false;
			}

			var steps = Math.Max(Math.Abs(from.X - ideal.X), Math.Abs(from.Y - ideal.Y));
			for (var i = 1; i <= steps; i++)
			{
				var probe = new CPos(
					ideal.X + DivRound((from.X - ideal.X) * i, steps),
					ideal.Y + DivRound((from.Y - ideal.Y) * i, steps));

				if (isBeyondLine(probe))
					continue;

				return TryNearestStandable(probe, clampCells, inBounds, NearSidePassable, out cell);
			}

			// steps == 0 means ideal IS from, which the isBeyondLine(from) test above already excluded.
			cell = ideal;
			return false;
		}

		/// <summary>Integer division rounded to nearest, symmetric about zero. Used for the line walk above
		/// rather than floating point so the cell sequence is bit-identical on every client.</summary>
		static int DivRound(int num, int den)
		{
			if (den == 0)
				return 0;

			return num >= 0 ? (num + den / 2) / den : -((-num + den / 2) / den);
		}
	}
}
