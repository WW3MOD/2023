#region Copyright & License Information
/*
 * WW3MOD — external (FIFO) unit-request cap decision (pure integer).
 *
 * PERCEIVED BEHAVIOUR: the bot does not buy an unbounded number of one type. Counter-composition call-ins
 * from AdaptiveProductionBotModule ride the single-name BuildUnit overload, which applies NO UnitsToBuild,
 * NO UnitDelays and NO UnitLimits. The composition ceiling was supposed to be the backstop, but it cannot be
 * one here: the request is routed to the first ENABLED UnitBuilder twin (AdaptiveRoutingMath), and on every
 * live profile that twin is a .fixedwing twin, which sets no CompositionDirected at all — so its
 * compositionTypes is null and RequestIsOverCompositionCeiling short-circuits to "not over" before it ever
 * reaches the per-slot test. The result is a lane with no cap of any kind.
 *
 * The cap that DOES exist for these types is the authored UnitLimits entry, but it is written on the twin
 * that owns the type (heli's cap lives on the .heli twin), not on the twin that drains the FIFO. So the
 * limit has to be resolved ACROSS the player's twins rather than read off the draining one.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws. A single ordered walk over caller-supplied
 * parallel arrays; the result is a minimum, which is order-independent anyway. No dictionary/hash
 * enumeration feeds the decision.
 *
 * FROZEN PATH: a type nobody authored a limit for yields 0 ⇒ IsOverLimit is false ⇒ byte-identical to the
 * uncapped behaviour. Only types whose cap someone already wrote down can bind.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class ExternalRequestLimitMath
	{
		/// <summary><para>The tightest cap any ENABLED UnitBuilder twin authored for a type, or 0 when none did.</para>
		///
		/// <para><paramref name="authoredLimits"/> is parallel to <paramref name="builderEnabled"/>; an entry of 0
		/// (or less) means that twin wrote no limit for this type. Disabled twins are skipped because their
		/// BotTick never runs — their opinion about the roster is not in force this game, so letting one cap a
		/// buy would apply a limit from a profile nobody is playing.</para>
		///
		/// <para>MINIMUM, not first-match: several enabled twins may name the same type, and the strictest
		/// author wins. That is the conservative direction — it can only ever refuse a buy the uncapped lane
		/// would have made, never permit one it would have refused.</para></summary>
		public static int TightestLimit(IReadOnlyList<bool> builderEnabled, IReadOnlyList<int> authoredLimits)
		{
			if (builderEnabled == null || authoredLimits == null)
				return 0;

			var count = builderEnabled.Count < authoredLimits.Count ? builderEnabled.Count : authoredLimits.Count;

			var tightest = 0;
			for (var i = 0; i < count; i++)
			{
				if (!builderEnabled[i] || authoredLimits[i] <= 0)
					continue;

				if (tightest == 0 || authoredLimits[i] < tightest)
					tightest = authoredLimits[i];
			}

			return tightest;
		}

		/// <summary><para>Whether one more of this type would breach the cap.</para>
		///
		/// <para>A <paramref name="limit"/> of 0 means "nobody authored one" and is NOT a cap of zero — it
		/// yields false, preserving the uncapped path exactly.</para>
		///
		/// <para>The comparison is &gt;=, matching the existing UnitLimits gate on the normal purchase path
		/// (UnitBuilderBotModule's post-pick test): at the limit, we stop, so the limit is the standing
		/// population rather than one below it.</para></summary>
		public static bool IsOverLimit(int liveCount, int limit)
		{
			return limit > 0 && liveCount >= limit;
		}
	}
}
