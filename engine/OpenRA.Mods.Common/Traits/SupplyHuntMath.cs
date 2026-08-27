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
		/// <para>WHAT SHIPS IS BROADER THAN THAT SENTENCE, and the difference is worth stating rather
		/// than letting a doc claim outrun the code. The predicate is not "no rearm actor exists
		/// anywhere" but "none WITHIN <c>AmmoPoolInfo.DryRearmLeashCells</c>, and none that can travel
		/// to us". That leash ships at 30 and is overridden in no mod YAML, while maps run from 66x34 up
		/// to 128x128 — so "more than 30 cells from your Logistics Centre" is an ordinary battlefield
		/// distance, not an exotic one, and a wholly dry vehicle with a fully stocked LC 31 cells away
		/// DOES evacuate. That is deliberate and pinned by
		/// <c>DrainedDistantStaticDepotStillEvacuates</c>: a static depot beyond the leash cannot close
		/// the gap and the unit will not cross to it, so waiting never terminates. Describe it as "none
		/// within 30 cells", never as "none exists".</para>
		///
		/// <para>WHY HOLDING IS ONLY SOMETIMES WORTH IT, which is the whole content of this function:
		/// NeedsResupply has exactly ONE reader in the whole engine —
		/// <c>SupplyProvider.FindNeedsResupplyTarget</c> (SupplyProvider.cs:622), swept only by a
		/// Hunt-stance provider that then DRIVES to the flagged unit. So raising the flag pays off if
		/// and only if some host can travel. (Grep the PROPERTY ACCESS, not the string:
		/// <c>UnitBuilderBotModule.AnyFieldedUnitNeedsResupply</c> merely contains the name — its body
		/// never touches the property, computing need from Info.Ammo / CurrentAmmoCount / SupplyValue
		/// via ResupplyDemand.UnitNeed. A previous revision of this comment miscounted it as a second
		/// reader on the strength of the method name alone.)
		/// Against a host that cannot move, the flag is addressed to nobody and the unit waits forever.
		/// That is not hypothetical: in the shipped corpus every vehicle names
		/// <c>RearmActors: logisticscenter</c> and nothing else, and the Logistics Centre is a
		/// BUILDING — so for a vehicle this test is structurally false, always. (Count carefully before
		/// repeating a number here; two revisions of this comment have already got it wrong. There are
		/// four SupplyProvider actors in the mod — truk, lccv, logisticscenter, supplycache — and only
		/// TWO carry IMove, which is what AnyMobileRearmHost actually tests: supplycache is a dropped
		/// crate declaring nothing but <c>Inherits: ^SpriteActor</c>. Of those two, lccv appears in no
		/// <c>RearmActors</c> list anywhere in the mod, so the only mobile rearm host anyone can reach
		/// is truk — and truk serves <c>RearmCondition: replenish-soldiers</c>, declared only on
		/// ^Soldier, so it could not serve a vehicle even if a vehicle did name it.)
		/// An Iskander with no Logistics Centre is therefore flagging for a rescue the ruleset makes
		/// impossible, which is exactly the bug this was reported as.</para>
		///
		/// <para>DRAINED IS NOT ABSENT, and conflating the two is the defect this signature exists to
		/// make impossible. <c>RearmsUnits</c> appears NOWHERE in mods/ww3mod, so every rearm host in
		/// this mod is a <c>SupplyProvider</c> and <c>AmmoPool.ChooseResupplier</c> filters them on
		/// <c>CurrentSupply &gt; 0</c> — meaning "no host found" silently also means "the depot is
		/// standing right there but empty". Evacuating on THAT spends the unit permanently against a
		/// recoverable condition: <c>AbsorbsSupplyCache</c> calls <c>SupplyProvider.AddSupply</c> from
		/// nearby caches, so a drained Logistics Centre is one truck away from serving again. And it is
		/// the ROUTINE state, not an edge case — the iskander's pool is <c>SupplyValue: 1500</c> against
		/// the Logistics Centre's <c>TotalSupply: 2250</c>, so one LC cannot fill one Iskander twice and
		/// <c>CurrentSupply == 0</c> is where it normally ends up. Hence
		/// <paramref name="anyHostWithinLeash"/> and <paramref name="anyHostCanReachUs"/> are asked
		/// about hosts that EXIST, ignoring their current stock, while
		/// <paramref name="suppliedHostWithinLeash"/> is the separate question of whether one can serve
		/// us right now.</para>
		///
		/// <para>ZERO-SEMANTICS, and this is the trap: <paramref name="seekingEnabled"/> is a SEPARATE
		/// input from the leash booleans precisely so a disabled leash cannot be mistaken for a distant
		/// host. <c>AmmoPoolInfo.DryRearmLeashCells</c> at 0 or less is documented as "a dry unit never
		/// self-dispatches, only flags" — a deliberate instruction not to travel — so it suppresses
		/// evacuation too, and is checked BEFORE the hopelessness test rather than after it. (The first
		/// cut of this function checked it after, which made the surrounding commit message's claim that
		/// a 0 leash is "never escalated into leaving the map" false; it is true now.) Note the opposite
		/// convention next door: <c>PoiOffensiveBotModule.OutOfAmmoRearmSeekRadiusCells</c> reads 0 as
		/// UNLIMITED. Two opposite zero-semantics for one idea already exist here; state which you mean
		/// and never infer it.</para>
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
		/// <param name="namesRearmActors">Whether the actor declares any rearm actors at all. False for
		/// a unit with no <c>Rearmable</c>, which is not a unit whose depot is MISSING — it is one that
		/// was never meant to be rearmed, and is out of this feature's scope entirely.</param>
		/// <param name="seekingEnabled">Whether self-dispatch is permitted at all, i.e. the dry leash is
		/// positive. See the zero-semantics paragraph above.</param>
		/// <param name="suppliedHostWithinLeash">Whether a host that can afford a batch we are short of
		/// sits inside the leash — the seek trigger, and the only input that reads current stock.</param>
		/// <param name="anyHostWithinLeash">Whether any host EXISTS inside the leash, drained or not.
		/// A depot we are standing next to is worth waiting at even while it is empty.</param>
		/// <param name="anyHostCanReachUs">Whether any host that exists could travel to us — in practice
		/// whether any is mobile. A static depot beyond the leash can never close the gap.</param>
		public static DryAutoDisposition DecideAutoDisposition(bool canMove, bool whollyDry,
			bool namesRearmActors, bool seekingEnabled, bool suppliedHostWithinLeash,
			bool anyHostWithinLeash, bool anyHostCanReachUs)
		{
			// An immobile actor cannot act on any disposition; leave it exactly as it was. Static
			// defences self-reload via ReloadAmmoPool (economy.md) and never needed this path.
			if (!canMove)
				return DryAutoDisposition.HoldAndFlag;

			// OUT OF SCOPE BY CONSTRUCTION. A unit that names no rearm actors — ^CrewMember and every
			// ejected crewman under it, which inherit ^CamoSoldier without a Rearmable anywhere in the
			// chain — has no depot to be missing. Its ammunition is a one-shot allowance, not a supply
			// relationship that has failed, so "your depot is gone, go home" is not a judgement this
			// feature is entitled to make about it. Without this guard every crewman who empties his
			// pistol walks off the map from wherever his vehicle just died.

			if (!namesRearmActors)
				return DryAutoDisposition.HoldAndFlag;

			// Travel switched off by configuration. Hold, and never escalate — see the zero-semantics
			// paragraph. Deliberately ahead of the hopelessness test below.
			if (!seekingEnabled)
				return DryAutoDisposition.HoldAndFlag;

			// Something can serve us now and is close enough to walk to. Ahead of the evacuation test
			// for legibility only: a supplied host inside the leash also satisfies anyHostWithinLeash,
			// so the two can never both be true.
			if (suppliedHostWithinLeash)
				return DryAutoDisposition.SeekRearm;

			// TIERED BY HOW DRY, and this is the guard that keeps the ruling from over-reaching. The
			// enclosing path triggers on OutOfEssentialAmmo, which is TRUE for a unit that can still
			// shoot something — a rifleman whose magazine is spent but who still holds an RPG round, a
			// tunguska out of SAMs with a full cannon (WORKSPACE/balance/260821-essential-ammo-pools.md).
			// Seeking is recoverable; evacuation is TERMINAL, and spending a still-armed unit for a
			// refund is a far larger commitment than walking it to a depot. So only a unit that can fire
			// NOTHING may take the evacuation tier. Exactly the tiering AutoSeekSupplies already applies
			// to its leash, for the stated reason that "a unit that can still fire something should not
			// abandon a live order to cross the map" — this says it should not abandon the map either.
			//
			// HOPELESS is the conjunction below: no host near enough to wait beside, AND none that could
			// drive to us. An ABSENT host satisfies both trivially, which is the reported Iskander case;
			// a DRAINED but nearby host satisfies neither, which is the case this must not fire on.
			if (whollyDry && !anyHostWithinLeash && !anyHostCanReachUs)
				return DryAutoDisposition.Evacuate;

			return DryAutoDisposition.HoldAndFlag;
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
		/// <para>Index of the nearest candidate that can actually AFFORD to serve us, or -1 when none
		/// can. <paramref name="affordable"/> is parallel to <paramref name="candidates"/>.</para>
		///
		/// <para>THE ORDERING IS THE WHOLE POINT: filter, THEN pick. Testing affordability on the
		/// already-chosen nearest host instead strands a unit that had a usable depot available — two
		/// owned Logistics Centres, one at 3 cells holding 750 and one at 8 cells holding 2250, against
		/// an iskander needing 1500: pick-then-filter selects the 3-cell LC, finds it cannot pay, and
		/// reports "nothing can serve us" while a fully stocked depot sits eight cells away. That was a
		/// live defect in this file's first revision, which is why the rule now lives here with a test
		/// rather than as two lines at the call site.</para>
		///
		/// <para>Same tie-break as <see cref="SelectNearest"/> — lower ActorID wins — so the choice is a
		/// total order and two clients cannot diverge on equidistant depots.</para>
		/// </summary>
		public static int SelectNearestAffordable(IReadOnlyList<Candidate> candidates, IReadOnlyList<bool> affordable)
		{
			var best = -1;
			for (var i = 0; i < candidates.Count; i++)
			{
				if (!affordable[i])
					continue;

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
