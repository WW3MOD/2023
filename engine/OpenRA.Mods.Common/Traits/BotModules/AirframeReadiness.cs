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

using System.Linq;
using OpenRA.Mods.Common.Activities;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// <para>Does a host that could restore this actor's health or ammunition exist in the world right now?</para>
	///
	/// <para>Inherited readiness logic answers that question from the RULES — a non-empty
	/// <see cref="RearmableInfo.RearmActors"/> or <see cref="RepairableInfo.RepairActors"/> list is
	/// taken to mean restoration will happen. That is an assumption about the WORLD, and in a mod
	/// where those lists name actors that are never placed and cannot be built it is simply false.
	/// Every gate phrased as "wait until healthy / wait until full" then becomes unsatisfiable rather
	/// than merely pessimistic, and the unit is benched for the rest of the match.</para>
	///
	/// <para>These predicates let the readiness gates ask the world instead.</para>
	///
	/// <para>One rule governs how they may be used: <b>a host appearing must never withdraw permission.</b>
	/// Keying a COMMITMENT bar off host existence looks natural and is backwards — it turns capturing
	/// a logistics center into a restriction, snapping the Apache bar from 35 to 75 so the
	/// helicopters get more conservative for having taken ground, with a step function at the moment
	/// of capture. So the commit floor is the flee bar unconditionally ("is this airframe worth
	/// sending?"), and host existence only ever adds the option of repairing first ("is there
	/// somewhere better for it to be?"). That split is monotone: every host is a gain.</para>
	/// </summary>
	public static class AirframeReadiness
	{
		/// <summary>
		/// <para>Which of the two host kinds actually restores THIS actor: the Reservable landing pad an
		/// aircraft flies to, or the RearmsUnits / SupplyProvider dock a ground unit drives to.</para>
		///
		/// <para>The dock term does not count for an aircraft, and that asymmetry is the whole point of this
		/// function. Reaching a dock is <see cref="AmmoPool.AutoRearm"/>'s job, and all three of its
		/// entry points refuse aircraft outright (<see cref="AmmoPool.AutoRearmIfAllEmpty"/>,
		/// <see cref="AmmoPool.AutoRearmIfAnyNotFull"/>, and the bot sweep's own candidate filter), so
		/// an aircraft is never dispatched to one. <see cref="Activities.Resupply"/> would not carry it
		/// there either — its approach block is guarded on the actor NOT being an aircraft, because in
		/// stock OpenRA a pad-bound aircraft has already been flown in by
		/// <see cref="Activities.ReturnToBase"/>, which selects on <see cref="Reservable"/> alone.</para>
		///
		/// <para>So for an aircraft, a dock it can never be sent to and could never be carried to is not a
		/// host. Counting it would report the airframe as hosted and flip its gates to the strict
		/// restore-first bars — full ammo to launch, the recovery bar to re-engage — with nothing on
		/// the map able to satisfy them. That is precisely the unsatisfiable-gate defect this class
		/// exists to prevent, re-entered through the other term.</para>
		/// </summary>
		public static bool CountsAsRearmHost(bool isAircraft, bool hasReservablePad, bool hasDockHost)
		{
			return hasReservablePad || (hasDockHost && !isAircraft);
		}

		/// <summary>
		/// Whether a host that could refill this actor's ammo pools is present and usable by it.
		/// Covers both host kinds, subject to <see cref="CountsAsRearmHost"/> deciding which of them
		/// this actor can actually reach.
		/// </summary>
		public static bool HasRearmHost(Actor self)
		{
			// Reservable-pad term first: it is a plain Any() over a trait index that is empty in this
			// mod, where ChooseResupplier walks two indices and sorts the result. These run per unit
			// per squad tick, so the order matters more than it looks.
			var hasReservablePad = ReturnToBase.AnyResupplierExists(self);
			if (hasReservablePad)
				return true;

			return CountsAsRearmHost(
				self.Info.HasTraitInfo<AircraftInfo>(),
				false,
				AmmoPool.ChooseResupplier(self) != null);
		}

		/// <summary>
		/// <para>Whether a repair host this actor could actually use is present. When false, health is a
		/// one-way resource for this actor.</para>
		///
		/// <para>Same filter as <see cref="Repairable.FindRepairBuilding"/>, minus its OrderBy/ThenBy — this
		/// is an existence test on a per-unit-per-tick path and has no use for the nearest one.</para>
		/// </summary>
		public static bool HasRepairHost(Actor self)
		{
			var info = self.Info.TraitInfoOrDefault<RepairableInfo>();
			if (info == null || info.RepairActors.Count == 0)
				return false;

			return self.World.ActorsHavingTrait<RepairsUnits>()
				.Any(a => !a.IsDead
					&& a.IsInWorld
					&& a.Owner.IsAlliedWith(self.Owner)
					&& info.RepairActors.Contains(a.Info.Name));
		}

		/// <summary>
		/// <para>The health below which a damaged airframe should be pulled out and ROUTED TO REPAIR
		/// rather than kept in the fight.</para>
		///
		/// <para><paramref name="recoveryBar"/> (<c>ReEngageHealthPercent</c>) is a post-repair bar — "the
		/// HP it must reach again before being sent out" — so it only answers a question that has a
		/// repair host in it. Where nothing can repair the airframe there is nowhere better for it to
		/// be, so this collapses to the flee bar and the routing order is refused as a no-op.</para>
		///
		/// <para>This is the ONLY thing the recovery bar governs. It deliberately does not gate commitment:
		/// see <see cref="AirframeReadiness"/> remarks on why the commit floor is unconditional.</para>
		/// </summary>
		public static int RepairRoutingBar(bool hasRepairHost, int recoveryBar, int fleeBar)
		{
			return hasRepairHost ? recoveryBar : fleeBar;
		}

		/// <summary>
		/// Whether an airframe's ammunition state clears the bar for being LAUNCHED on a new mission.
		/// Hosted, that is the inherited every-pool-full expectation, because a dry pool will be
		/// topped up before the airframe is needed again. Unhosted, ammunition is one-way and
		/// "full" is a state the airframe leaves once and never re-enters, so the answerable
		/// question is whether it can still shoot at all.
		/// </summary>
		public static bool AmmoReadyToLaunch(bool hasRearmHost, int totalPools, int loadedPools, int fullPools)
		{
			if (totalPools == 0)
				return true;

			return hasRearmHost ? fullPools == totalPools : loadedPools > 0;
		}

		/// <summary>
		/// Whether an airframe already in a squad still counts as carrying ammunition. Hosted, the
		/// inherited every-pool-loaded test; unhosted, any pool loaded — an airframe with a dry
		/// secondary and a loaded primary can still fight, and will never be able to say otherwise.
		/// </summary>
		public static bool AmmoReadyToFight(bool hasRearmHost, int totalPools, int loadedPools)
		{
			if (totalPools == 0)
				return true;

			return hasRearmHost ? loadedPools == totalPools : loadedPools > 0;
		}

		/// <summary>
		/// <para>Whether a squad member counts toward its squad still having ammunition.</para>
		///
		/// <para>The answer must NOT depend on <paramref name="allPoolsRearmable"/>, and that is the whole
		/// reason this function exists rather than the call being inlined. Inherited, that fact was
		/// used to SKIP a member — "a rearm host will handle this one" — inside a loop that then
		/// reported the squad dry if every member had been skipped. Every attack-helicopter squad in
		/// this mod is all-covered, so every one of them reported dry at full ammo and never launched.</para>
		///
		/// <para>Conditioning that skip on a host actually being present does not fix it; it relocates the
		/// failure onto exactly the maps where someone placed a pad, which is the case no test can
		/// reach today. So the fact is taken as a parameter and deliberately ignored, and the ignoring
		/// is pinned by test, rather than being an absence that reads like an oversight.</para>
		/// </summary>
		public static bool MemberStillShoots(bool hasRearmHost, bool allPoolsRearmable, int totalPools, int loadedPools)
		{
			// allPoolsRearmable is intentionally not consulted — see remarks.
			return AmmoReadyToFight(hasRearmHost, totalPools, loadedPools);
		}
	}
}
