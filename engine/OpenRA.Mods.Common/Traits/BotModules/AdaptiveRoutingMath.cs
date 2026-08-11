#region Copyright & License Information
/*
 * WW3MOD @experimental — AdaptiveProduction unit-request routing decision (pure integer).
 *
 * PERCEIVED BEHAVIOUR: counter-composition call-ins actually get built. A player carries several UnitBuilder
 * twins (normal / experimental / fixedwing / heli, per faction); all but a few are condition-DISABLED per game.
 * A disabled twin still answers the IBotRequestUnitProduction interface but its BotTick never runs (ModularBot
 * ticks only enabled modules), so a request handed to it is silently lost — and RequestedProductionCount sums
 * that stuck queue, so the alreadyRequested>=2 gate then wedges re-issue. The legacy routing always picked
 * producer[0]; on @experimental NATO producer[0] is the disabled @russia.fixedwing twin (player.brics is
 * false), so every counter-buy vanished. This turns the "which producer" choice into an index so the fix is
 * NUnit-pinned without a game run (mirrors CaptureSupplyMath).
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, a single ordered walk over a caller-supplied
 * enabled/disabled flag list. No dictionary/hash enumeration feeds the decision.
 *
 * FROZEN PATH: routeToEnabled=false reproduces the pre-fix behaviour EXACTLY (always index 0).
 * NOTE (b8d2e601, 2026-08-02): @stable no longer omits RouteToEnabledProducer — it sets it true on both
 * factions (ai.yaml AdaptiveProductionBotModule@stable.america/.russia), so NO live profile selects the index-0 path any more. It survives only as
 * this function's documented off-switch, not as shipped behaviour; the old "@stable is unchanged" claim is dead.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class AdaptiveRoutingMath
	{
		/// <summary>Index of the UnitBuilder twin a call-in should be routed to, or -1 when the player carries
		/// no producer at all.
		///
		/// <paramref name="routeToEnabled"/> off ⇒ the pre-fix path: producer index 0 verbatim, even when
		/// producer 0 is condition-disabled. No live profile selects this any more (see the file header).
		/// On — which since b8d2e601 is every live profile, @stable and @experimental alike — ⇒ the first
		/// producer whose <paramref name="producerEnabled"/> flag is true, skipping the disabled twins
		/// whose BotTick never runs; -1 if somehow none is enabled. A single ordered walk, zero RNG.</summary>
		public static int SelectProducerIndex(IReadOnlyList<bool> producerEnabled, bool routeToEnabled)
		{
			if (producerEnabled == null || producerEnabled.Count == 0)
				return -1;

			if (!routeToEnabled)
				return 0; // frozen: legacy producer[0], regardless of its enabled state

			for (var i = 0; i < producerEnabled.Count; i++)
				if (producerEnabled[i])
					return i;

			return -1;
		}
	}
}
