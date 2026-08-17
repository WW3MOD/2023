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
		/// the cell through <see cref="Mobile.NearestMoveableCell"/>, whose default budget is a radius-10 annulus
		/// (Mobile.cs:811) — "Move" via Mobile.ResolveOrder (Mobile.cs:1030), "AttackMove" via
		/// AttackMove.ResolveOrder (AttackMove.cs:116). A bot clamping its own destination should use the same
		/// reach: clamping SHORTER gives up on deliveries the engine would have completed, and clamping FURTHER
		/// picks a cell the engine would not have chosen, so the two disagree again in the other direction.</summary>
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
		/// run the destination through <see cref="Mobile.NearestMoveableCell"/> over a radius-10 annulus
		/// (Mobile.cs:1030 for "Move", AttackMove.cs:116 for "AttackMove"). So a bounds-only destination rarely
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
	}
}
