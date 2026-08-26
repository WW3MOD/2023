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

			return ChessboardCells(dx, dy) <= budgetCells;
		}

		/// <summary>
		/// Chessboard distance — max(|dx|, |dy|), the "cells away" a player reads off the map. Factored
		/// out of <see cref="WithinCellBudget"/> so the budget test and the exit comparison below cannot
		/// end up measuring in two different metrics.
		/// </summary>
		public static int ChessboardCells(int dx, int dy)
		{
			var ax = dx < 0 ? -dx : dx;
			var ay = dy < 0 ? -dy : dy;
			return ax > ay ? ax : ay;
		}

		/// <summary>
		/// <para>For a unit under a standing Evacuate disposition that has run dry: is a rearm host worth
		/// detouring to, or should it keep leaving? True only when the host is STRICTLY nearer than the
		/// way out, so a unit ordered off the map never travels backwards — deeper into the battlefield
		/// it was told to quit — to fetch ammunition.</para>
		///
		/// <para>Ties go to evacuating. The evacuation is an order the player (or the unit's shipped
		/// InitialResupplyBehavior) actually expressed; the detour is the unit's own idea, and an
		/// unordered errand does not get to win a coin flip against an ordered one.</para>
		///
		/// <para>Both distances are chessboard, from the same origin, so the comparison is
		/// apples-to-apples. Caller supplies the exit's offset — see AmmoPool's Evacuate arm for what it
		/// measures to and why that is a proxy rather than the literal edge cell.</para>
		/// </summary>
		public static bool ResupplyBeatsExit(int hostDx, int hostDy, int exitDx, int exitDy)
		{
			return ChessboardCells(hostDx, hostDy) < ChessboardCells(exitDx, exitDy);
		}

		/// <summary>
		/// <para>What an Auto-stance unit that has run dry should DO. The three outcomes are exhaustive
		/// and mutually exclusive; see <see cref="DecideAutoDisposition"/> for which is chosen when.</para>
		/// </summary>
		public enum DryAutoDisposition
		{
			/// <summary>A host is reachable — self-dispatch to it and reload.</summary>
			SeekRearm,

			/// <summary>Stay put and raise NeedsResupply, because something can still come to us.</summary>
			HoldAndFlag,

			/// <summary>Nothing can deliver and nothing can be reached — leave via the Supply Route.</summary>
			Evacuate,
		}

		/// <summary>
		/// <para>The disposition of a unit on <see cref="ResupplyBehavior.Auto"/> that has run
		/// ESSENTIAL-dry. USER RULING 2026-08-27: "'Auto' should mean that they evacuate if no rearm
		/// actor exists" — leaving immediately, with no grace period. Before this, every no-host path
		/// ended at "raise NeedsResupply and stand still", which is not a decision to hold; it is a unit
		/// stuck with its hand up.</para>
		///
		/// <para>WHY HOLDING IS ONLY SOMETIMES WORTH IT, which is the whole content of this function:
		/// NeedsResupply has exactly ONE reader in the engine —
		/// <c>SupplyProvider.FindNeedsResupplyTarget</c>, swept only by a Hunt-stance provider that then
		/// DRIVES to the flagged unit. So raising the flag pays off if and only if some host can travel.
		/// Against a host that cannot move, the flag is addressed to nobody and the unit waits forever.
		/// That is not hypothetical: in the shipped corpus every vehicle names
		/// <c>RearmActors: logisticscenter</c> and nothing else, the Logistics Centre is a building, and
		/// the two MOBILE providers (truk, supplycache) serve <c>RearmCondition: replenish-soldiers</c>
		/// while vehicles declare replenish-vehicles — so no truck can rearm a vehicle even on arrival.
		/// An Iskander with no Logistics Centre is therefore flagging for a rescue the ruleset makes
		/// impossible, which is exactly the bug this was reported as.</para>
		///
		/// <para>ZERO-SEMANTICS, and this is the trap: <paramref name="seekingEnabled"/> is a SEPARATE
		/// input from <paramref name="hostWithinLeash"/> precisely so a disabled leash cannot be
		/// mistaken for a distant host. <c>AmmoPoolInfo.DryRearmLeashCells</c> at 0 or less is
		/// documented as "a dry unit never self-dispatches, only flags" — a deliberate instruction not
		/// to travel. Escalating THAT into leaving the map would turn an opt-out of one behaviour into
		/// an opt-in to a louder one. Note the opposite convention next door:
		/// <c>PoiOffensiveBotModule.OutOfAmmoRearmSeekRadiusCells</c> reads 0 as UNLIMITED. Two opposite
		/// zero-semantics for one idea already exist here; state which you mean and never infer it.</para>
		///
		/// <para>Deliberately NOT reusing <c>AmmoEvacMath.Decide</c>, which answers a very similar
		/// question for the bot module: its budget parameter carries the UNLIMITED-at-zero convention,
		/// so feeding it a unit-side leash would silently invert the meaning of a 0. Same shape, opposite
		/// dialect.</para>
		///
		/// <para>Pure integer/bool, zero RNG — two clients over the same synced state decide
		/// identically (the influence-stack determinism invariant).</para>
		/// </summary>
		/// <param name="canMove">False for an immobile actor, which can reach neither a host nor the map
		/// edge; ordering either would only cancel whatever it is doing. Matches AmmoEvacMath's guard.</param>
		/// <param name="whollyDry">Whether EVERY pool is empty, not merely every Essential one. Gates
		/// the evacuation tier only — see the tiering paragraph.</param>
		/// <param name="hostExists">Whether the actor owns ANY actor named in its Rearmable.RearmActors
		/// that still has something to give (AmmoPool.ChooseResupplier found a candidate).</param>
		/// <param name="seekingEnabled">Whether self-dispatch is permitted at all, i.e. the dry leash is
		/// positive. See the zero-semantics paragraph above.</param>
		/// <param name="hostWithinLeash">Whether the nearest host is inside the dry leash.</param>
		/// <param name="hostCanReachUs">Whether any candidate host could travel to us — in practice
		/// whether any of them is mobile. A static depot beyond the leash can never close the gap.</param>
		public static DryAutoDisposition DecideAutoDisposition(bool canMove, bool whollyDry,
			bool hostExists, bool seekingEnabled, bool hostWithinLeash, bool hostCanReachUs)
		{
			// An immobile actor cannot act on any disposition; leave it exactly as it was. Static
			// defences self-reload via ReloadAmmoPool (economy.md) and never needed this path.
			if (!canMove)
				return DryAutoDisposition.HoldAndFlag;

			// TIERED BY HOW DRY, and this is the guard that keeps the ruling from over-reaching. The
			// enclosing path triggers on OutOfEssentialAmmo, which is TRUE for a unit that can still
			// shoot something — a rifleman whose magazine is spent but who still holds an RPG round, a
			// tunguska out of SAMs with a full cannon (WORKSPACE/balance/260821-essential-ammo-pools.md).
			// Seeking is recoverable and such a unit may well go looking; evacuation is TERMINAL, and
			// spending a still-armed unit for a refund is a far larger commitment than walking it to a
			// depot. So only a unit that can fire NOTHING may take the evacuation tier; the rest keep
			// today's flag-and-stay. Exactly the tiering AutoSeekSupplies already applies to its leash,
			// for the stated reason that "a unit that can still fire something should not abandon a live
			// order to cross the map" — this says it should not abandon the map either.
			if (!whollyDry)
				return hostExists && seekingEnabled && hostWithinLeash
					? DryAutoDisposition.SeekRearm
					: DryAutoDisposition.HoldAndFlag;

			// The user's ruling, and it ignores the leash on purpose: there is no distance to judge
			// when there is no host. Nothing owned can ever serve us, so leaving is the only
			// disposition that terminates.
			if (!hostExists)
				return DryAutoDisposition.Evacuate;

			// Travel switched off by configuration. Hold, and do not escalate — see above.
			if (!seekingEnabled)
				return DryAutoDisposition.HoldAndFlag;

			if (hostWithinLeash)
				return DryAutoDisposition.SeekRearm;

			// Too far to be worth driving to. Waiting is only worth it if it can come to us.
			return hostCanReachUs ? DryAutoDisposition.HoldAndFlag : DryAutoDisposition.Evacuate;
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
