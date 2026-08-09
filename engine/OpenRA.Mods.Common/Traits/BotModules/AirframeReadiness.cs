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

using OpenRA.Mods.Common.Activities;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Does a host that could restore this actor's health or ammunition exist in the world right now?
	///
	/// Inherited readiness logic answers that question from the RULES — a non-empty
	/// <see cref="RearmableInfo.RearmActors"/> or <see cref="RepairableInfo.RepairActors"/> list is
	/// taken to mean restoration will happen. That is an assumption about the WORLD, and in a mod
	/// where those lists name actors that are never placed and cannot be built it is simply false.
	/// Every gate phrased as "wait until healthy / wait until full" then becomes unsatisfiable rather
	/// than merely pessimistic, and the unit is benched for the rest of the match.
	///
	/// These predicates let the readiness gates ask the world instead. They are self-correcting: put
	/// a host on a map, or make one buildable, and the classic restore-to-full behaviour returns with
	/// no further code change.
	/// </summary>
	public static class AirframeReadiness
	{
		/// <summary>
		/// Whether a host that could refill this actor's ammo pools is present and usable by it.
		/// Covers both host kinds: the RearmsUnits / SupplyProvider dock that ground units pull from,
		/// and the Reservable landing pad that aircraft fly to.
		/// </summary>
		public static bool HasRearmHost(Actor self)
		{
			return AmmoPool.ChooseResupplier(self) != null || ReturnToBase.AnyResupplierExists(self);
		}

		/// <summary>
		/// Whether a repair host this actor could actually use is present. When false, health is a
		/// one-way resource for this actor.
		/// </summary>
		public static bool HasRepairHost(Actor self)
		{
			var repairable = self.TraitOrDefault<Repairable>();
			return repairable != null && repairable.FindRepairBuilding(self) != null;
		}

		/// <summary>
		/// The health percentage an airframe must hold to be worth committing.
		///
		/// <paramref name="recoveryBar"/> is a post-repair bar ("the HP it must reach again before
		/// being sent out"). It is only answerable where repair exists. Where it does not, health
		/// only ever decreases, so that bar can be crossed exactly once, downward, and everything
		/// under it is benched for the match — including the whole band between the flee bar and it,
		/// which is airframes too healthy to run away and too damaged to be sent anywhere. The flee
		/// bar is the bar that still means something: above it the airframe fights.
		/// </summary>
		public static int CommitHealthBar(bool hasRepairHost, int recoveryBar, int fleeBar)
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
	}
}
