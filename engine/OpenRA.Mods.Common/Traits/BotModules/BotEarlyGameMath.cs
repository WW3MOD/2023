#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	// ============================================================
	// WW3MOD experimental AI — early-game economy decisions (PIPELINE item 12).
	// Engine-free, deterministic (zero RNG), NUnit-pinned (BotEarlyGameMathTest).
	// The bot-module glue reads live trait state and calls these; keeping the
	// decision here makes it testable and portable to a future v3 brain.
	// ============================================================

	/// <summary>
	/// Behaviour 1 — "no supply trucks while every unit has full ammo". The current-need
	/// gate: does any fielded unit a truck can rearm actually need ammo? Mirrors
	/// <see cref="SupplyProvider"/>'s own need metric (missing rounds weighted by SupplyValue
	/// over capacity) so the bot reads the SAME signal the economy uses, not a parallel one.
	/// Designed so a smarter ANTICIPATED-need predicate can replace <see cref="MeetsThreshold"/>
	/// later without touching the caller.
	/// </summary>
	public static class ResupplyDemand
	{
		/// <summary>Per-unit ammo need in [0,1]: sum(missing × SupplyValue) / sum(capacity × SupplyValue)
		/// over the unit's truck-rearmable ammo pools. 0 when the unit has no weighted capacity (nothing a
		/// truck can top up). Matches SupplyProvider.CalculateNeed exactly.</summary>
		public static float UnitNeed(IEnumerable<(int Ammo, int Current, int SupplyValue)> pools)
		{
			if (pools == null)
				return 0f;

			var totalMissing = 0f;
			var totalCapacity = 0f;
			foreach (var (ammo, current, supplyValue) in pools)
			{
				var weight = supplyValue;
				totalMissing += (ammo - current) * weight;
				totalCapacity += ammo * weight;
			}

			if (totalCapacity <= 0)
				return 0f;

			return totalMissing / totalCapacity;
		}

		/// <summary>Is a unit's need "meaningful"? Mirrors SupplyProvider's MinNeedThreshold gate
		/// (a nearly-full unit — e.g. 499/500 — is skipped). Swap this out for an anticipated-need
		/// model later; the caller only asks "is there meaningful demand?".</summary>
		public static bool MeetsThreshold(float need, float minNeedThreshold)
		{
			return need >= minNeedThreshold;
		}
	}

	/// <summary>
	/// Behaviour 2 — "AA proportionate to the actual air threat". Caps how many gated AA units
	/// (the expensive vehicle SHORAD/Tunguska; cheap AA infantry stay an ungated picket) the bot
	/// may field, scaled to the OBSERVED enemy air count. observedAir is supplied fog-legally by the
	/// caller (visible enemy aircraft only — no omniscient read), so the cap grows only as real air
	/// is seen and multiple vehicle-AA at game start (zero air observed, baseline small) is prevented.
	/// </summary>
	public static class AntiAirDemand
	{
		/// <summary>Maximum gated AA units allowed given the observed air threat:
		/// baseline + observedAir × perObservedAir. Negative inputs are floored to 0.</summary>
		public static int MaxAllowed(int observedAir, int baseline, int perObservedAir)
		{
			return Math.Max(0, baseline) + Math.Max(0, observedAir) * Math.Max(0, perObservedAir);
		}

		/// <summary>Should the bot call in another gated AA unit? True only while owned+pending count is
		/// under the observed-threat cap.</summary>
		public static bool ShouldBuildMore(int ownedOrPending, int observedAir, int baseline, int perObservedAir)
		{
			return ownedOrPending < MaxAllowed(observedAir, baseline, perObservedAir);
		}
	}

	/// <summary>
	/// Behaviour 3 — "spread out and capture fast, in small groups, early". Pure phase gate: while
	/// the match is young the offensive layer swaps in smaller UnitsPerAxis / MinAxisSize so early,
	/// few units disperse into several small packets from the beachhead instead of massing one armada.
	/// worldTick is the synced sim tick (deterministic, no RNG).
	/// </summary>
	public static class EarlyGamePhase
	{
		/// <summary>Is the match still in its early-spread window? False (⇒ normal axis sizing) when the
		/// feature is disabled or the duration has elapsed.</summary>
		public static bool IsEarly(int worldTick, bool enabled, int durationTicks)
		{
			return enabled && worldTick < durationTicks;
		}
	}
}
