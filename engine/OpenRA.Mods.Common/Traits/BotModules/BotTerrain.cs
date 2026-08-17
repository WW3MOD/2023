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
	}
}
