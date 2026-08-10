#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Where a unit is in its supply run (see <see cref="SupplyHuntMath.NextState"/>).</summary>
	public enum SupplyHuntState
	{
		/// <summary>Walking toward the chosen provider, not yet inside its push aura.</summary>
		Approaching,

		/// <summary>Standing inside the aura, letting the provider's push refill us.</summary>
		Replenishing,

		/// <summary>Refilled (or the provider fell through) — walking back to where we started.</summary>
		Returning,

		/// <summary>Back at the origin; the activity ends and the unit resumes idle.</summary>
		Done,
	}

	/// <summary>
	/// Pure decision logic for infantry auto-seek-supplies (<see cref="AutoSeekSupplies"/>). Kept
	/// free of Actor/World so the rules are unit-testable and provably deterministic: integer and
	/// WDist math only, no RNG, no floating point, and a total order on candidates so two clients
	/// always pick the same provider.
	/// </summary>
	public static class SupplyHuntMath
	{
		/// <summary>WDist units per cell. Leash config is authored in cells; distances are WDist.</summary>
		public const int CellLength = 1024;

		/// <summary>
		/// Whether a pool has fallen far enough to justify walking off to resupply. Strictly below
		/// the threshold, so a unit sitting exactly on it stays put — the boundary must not
		/// oscillate a unit that is being topped up one batch at a time. Cross-multiplied rather
		/// than divided so there is no integer-truncation cliff on small pools (a 3-missile pool
		/// would otherwise trip at a different real percentage than a 900-round one).
		/// </summary>
		public static bool BelowSeekThreshold(int currentAmmo, int maxAmmo, int thresholdPerMille)
		{
			if (maxAmmo <= 0 || thresholdPerMille <= 0)
				return false;

			return (long)currentAmmo * 1000 < (long)maxAmmo * thresholdPerMille;
		}

		/// <summary>
		/// The stance gate: every axis must permit the run, and each says no for its own reason.
		/// Resupply must be Auto — Hold means "stay put, a truck will come to me", and Evacuate is
		/// owned by the out-of-ammo evac path, which takes precedence over any local errand.
		/// Engagement must not be HoldPosition — a holding unit never roams, that is the whole
		/// contract of the stance. Fire must not be Ambush — an ambusher that stands up and walks
		/// to a truck gives away the position it was placed to conceal.
		/// </summary>
		public static bool StancesPermitHunt(UnitStance fire, EngagementStance engagement, ResupplyBehavior resupply)
		{
			if (resupply != ResupplyBehavior.Auto)
				return false;

			if (engagement == EngagementStance.HoldPosition)
				return false;

			if (fire == UnitStance.Ambush)
				return false;

			return true;
		}

		/// <summary>Squared length of the leash, for comparison against a squared world distance.</summary>
		public static long LeashLengthSquared(int leashCells)
		{
			if (leashCells <= 0)
				return 0;

			var length = (long)leashCells * CellLength;
			return length * length;
		}

		/// <summary>
		/// Whether a provider is close enough to be worth the walk. Euclidean world distance (the
		/// same metric the provider's own aura uses), NOT the chessboard "cells away" a player
		/// reads off the minimap — so a source 20 cells out diagonally is outside a 20-cell leash.
		/// Inclusive at the boundary, matching the aura's own inclusive edge.
		/// </summary>
		public static bool WithinLeash(long distanceSquared, int leashCells)
		{
			return distanceSquared <= LeashLengthSquared(leashCells);
		}

		/// <summary>
		/// Whether a host sits inside a budget expressed in CHESSBOARD cells — max(|dx|, |dy|), the
		/// "cells away" a player reads off the map, NOT CVec.Length's Euclidean rounding (conventions.md).
		/// Used for the break-off-and-walk budget, where the number is a travel-cost cap rather than an
		/// aura edge, so the metric that matches the grid is the honest one. Inclusive at the boundary;
		/// a budget of 0 or less admits nothing.
		/// </summary>
		public static bool WithinCellBudget(int dx, int dy, int budgetCells)
		{
			if (budgetCells <= 0)
				return false;

			var ax = dx < 0 ? -dx : dx;
			var ay = dy < 0 ? -dy : dy;
			return (ax > ay ? ax : ay) <= budgetCells;
		}

		/// <summary>A provider under consideration, reduced to the two facts the pick depends on.</summary>
		public readonly struct Candidate
		{
			public readonly long DistanceSquared;
			public readonly uint ActorId;

			public Candidate(long distanceSquared, uint actorId)
			{
				DistanceSquared = distanceSquared;
				ActorId = actorId;
			}
		}

		/// <summary>
		/// Index of the nearest candidate, or -1 when the list is empty. Ties break on the lower
		/// ActorID so the choice is a total order — two equidistant trucks must not be picked by
		/// enumeration order, which is not a guarantee any caller should lean on.
		/// </summary>
		public static int SelectNearest(IReadOnlyList<Candidate> candidates)
		{
			var best = -1;
			for (var i = 0; i < candidates.Count; i++)
			{
				if (best < 0)
				{
					best = i;
					continue;
				}

				var c = candidates[i];
				var b = candidates[best];
				if (c.DistanceSquared < b.DistanceSquared ||
					(c.DistanceSquared == b.DistanceSquared && c.ActorId < b.ActorId))
					best = i;
			}

			return best;
		}

		/// <summary>
		/// The supply-run state machine. Out → wait → back, with every failure mode collapsing to
		/// "go home" rather than stranding the unit at the front: if the provider dies, drains, or
		/// drives off mid-run we return to the origin instead of standing where we happened to be.
		/// Being refilled by someone else while still walking also sends us straight home.
		/// Drifting back out of the aura re-approaches rather than giving up, since a mobile truck
		/// can simply have moved.
		/// </summary>
		public static SupplyHuntState NextState(SupplyHuntState current, bool providerUsable, bool inAura, bool replenished, bool atOrigin)
		{
			switch (current)
			{
				case SupplyHuntState.Approaching:
					if (replenished || !providerUsable)
						return SupplyHuntState.Returning;

					return inAura ? SupplyHuntState.Replenishing : SupplyHuntState.Approaching;

				case SupplyHuntState.Replenishing:
					if (replenished || !providerUsable)
						return SupplyHuntState.Returning;

					return inAura ? SupplyHuntState.Replenishing : SupplyHuntState.Approaching;

				case SupplyHuntState.Returning:
					return atOrigin ? SupplyHuntState.Done : SupplyHuntState.Returning;

				default:
					return SupplyHuntState.Done;
			}
		}
	}
}
