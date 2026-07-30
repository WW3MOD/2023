#region Copyright & License Information
/*
 * WW3MOD influence stack — frontier standoff (@experimental) — rearward-push decision (pure math).
 *
 * PERCEIVED BEHAVIOUR: standoff units (the artillery echelon anchor, attack-heli standoff) no longer hold
 * ON the believed front line — they hold BEHIND it. Given a chosen standoff point that sits too close to the
 * believed-enemy frontier (ControlField's distance-to-enemy-region), the consumer walks it rearward along its
 * existing away-from-target axis until it clears a minimum frontier distance.
 *
 * This class carries ONE coordinate-agnostic decision: HOW MANY rearward steps to take. The caller supplies a
 * stepper — frontierAtStep(i) = the believed frontier distance (coarse cells) at i steps back along the axis,
 * i=0 being the un-pushed point — so the same pure function serves both the WPos echelon anchor and the CPos
 * heli engage cell. The push is BOUNDED by a step budget: it is a walk-back, never a free search.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer comparisons over caller-sampled
 * readings. Two clients over the same synced belief state take the identical number of steps.
 *
 * v3-portable: engine-free static math (NUnit-pinned in FrontierStandoffMathTest); only the tasking plumbing
 * that binds the stepper (PoiOffensiveBotModule echelon, HelicopterStates heli standoff) is engine-specific.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class FrontierStandoffMath
	{
		/// <summary>Smallest number of rearward steps in [0, <paramref name="maxSteps"/>] at which the believed
		/// frontier distance reaches <paramref name="minCells"/>. <paramref name="frontierAtStep"/>(i) is the
		/// distance-to-enemy-frontier (coarse cells) i steps back along the away-from-target axis; i=0 is the
		/// un-pushed point. Returns 0 when the point is already clear (⇒ no push, so an un-consumed / unpopulated
		/// field leaves the path byte-identical), and <paramref name="maxSteps"/> when the budget is exhausted
		/// without clearing (push back as far as allowed — always the safe direction). Bounded, integer, zero RNG.</summary>
		public static int RearwardSteps(Func<int, int> frontierAtStep, int minCells, int maxSteps)
		{
			if (minCells <= 0 || maxSteps <= 0)
				return 0;

			for (var i = 0; i < maxSteps; i++)
				if (frontierAtStep(i) >= minCells)
					return i;

			return maxSteps;
		}
	}
}
