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

namespace OpenRA.Mods.Common.Traits
{
	// Pure, RNG-free deterministic helpers for the PIPELINE-6 formation-realism micro-wave:
	//   #1 arrival jitter        (lateral scatter off the exact slot point)
	//   #3 rolling halt          (along-axis depth stagger — the same 2D offset, depth component)
	//   #2 settle-facing fan     (per-unit micro-variation on the common formation front)
	//
	// Every value here is a pure function of a unit's stable ActorID — no world.SharedRandom, no
	// LocalRandom — so it is byte-identical on every client and in replay (see CohesionMoveModifier's
	// determinism note). Extracted out of CohesionMoveModifier / CohesionSlotMemory so the raw
	// arithmetic (hash -> offset, hash -> fan angle, the clamps) can be pinned in NUnit without the
	// Actor/Map/Mobile coupling that keeps the traits themselves out of the unit-test harness.
	// Source-of-truth for the pins in engine/OpenRA.Test/OpenRA.Mods.Common/FormationRealismMathTest.cs.
	public static class FormationRealism
	{
		// Three decorrelated streams from one ActorID (arrival-lateral, arrival-depth, facing-fan).
		// Arbitrary odd constants — only their mutual distinctness matters, so the three offsets a
		// single unit receives are independent rather than sharing a sign/magnitude.
		public const uint LateralSalt = 0x9E3779B1u;
		public const uint DepthSalt = 0x85EBCA77u;
		public const uint FanSalt = 0xC2B2AE3Du;

		// Integer avalanche hash (xxHash-style finalizer) of ActorID mixed with a salt. The low bits
		// of a bare multiplicative hash are poor (they echo actorId's low bits), so we finalize with
		// shift-xor-multiply rounds and read the whole word — every output bit depends on the input.
		// Pure and deterministic; used only to derive small per-unit offsets, never to consume RNG.
		public static uint Hash(uint actorId, uint salt)
		{
			unchecked
			{
				var h = actorId ^ salt;
				h *= 2654435761u;       // Knuth
				h ^= h >> 15;
				h *= 2246822519u;
				h ^= h >> 13;
				return h;
			}
		}

		// Uniform-ish signed integer in [-cap, cap] from a hash. cap <= 0 disables the offset (0),
		// so a config value of 0 turns a behaviour off without any special-casing at the call site.
		public static int SignedOffset(uint hash, int cap)
		{
			if (cap <= 0)
				return 0;

			var span = (uint)(2 * cap + 1);
			return (int)(hash % span) - cap;
		}

		// Arrival-offset components (WDist) for a unit, along the formation's lateral (perpendicular)
		// and depth (move) axes. The caller derives latCap/depthCap from the mode's spacing via
		// LateralCap/DepthCap below, then projects these scalars onto the integer axis vectors.
		public static int LateralOffset(uint actorId, int latCap) => SignedOffset(Hash(actorId, LateralSalt), latCap);
		public static int DepthOffset(uint actorId, int depthCap) => SignedOffset(Hash(actorId, DepthSalt), depthCap);

		// Settle-facing micro-fan: a small signed WAngle delta in [-fanWAngle, fanWAngle] added to the
		// common formation front so no two units stare down the identical azimuth. WAngle wraps mod
		// 1024, so adding this to the front azimuth is always valid. Pure, no RNG.
		public static WAngle FacingFan(uint actorId, int fanWAngle) => new WAngle(SignedOffset(Hash(actorId, FanSalt), fanWAngle));

		// Lateral-jitter clamp: the offset must stay strictly under HALF the minimum slot spacing so
		// two adjacent slots (which are >= minSlotSpacing apart after the footprint cap floors at
		// MinSlotSpacing) can never be jittered onto the same cell — "slots never overlap" (idea #1
		// mitigation a). Returns 0 when the ceiling collapses, disabling jitter rather than overlapping.
		public static int LateralCap(int requested, int minSlotSpacing)
		{
			var ceiling = minSlotSpacing / 2 - 1;
			return Math.Max(0, Math.Min(requested, ceiling));
		}

		// Depth (rolling-halt) clamp: below rowSpacing/2 AND below minSlotSpacing (idea #3 mitigation),
		// so a rear unit's stagger can never pull it into the rank behind or overlap a neighbour.
		public static int DepthCap(int requested, int rowSpacing, int minSlotSpacing)
		{
			var ceiling = Math.Min(rowSpacing / 2, minSlotSpacing) - 1;
			return Math.Max(0, Math.Min(requested, ceiling));
		}
	}
}
