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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class RearmableInfo : TraitInfo
	{
		[ActorReference]
		[FieldLoader.Require]
		[Desc("Actors that this actor can dock to and get rearmed by.")]
		public readonly HashSet<string> RearmActors = new();

		[Desc("Name(s) of AmmoPool(s) that use this trait to rearm.")]
		public readonly HashSet<string> AmmoPools = new() { "primary" };

		public override object Create(ActorInitializer init) { return new Rearmable(this); }
	}

	public class Rearmable : INotifyCreated, INotifyDockClient
	{
		public readonly RearmableInfo Info;

		public Rearmable(RearmableInfo info)
		{
			Info = info;
		}

		public AmmoPool[] RearmableAmmoPools { get; private set; }

		void INotifyCreated.Created(Actor self)
		{
			RearmableAmmoPools = self.TraitsImplementing<AmmoPool>().Where(p => Info.AmmoPools.Contains(p.Info.Name)).ToArray();
		}

		void INotifyDockClient.Docked(Actor self, Actor dock)
		{
			// Reset the ReloadDelay to avoid any issues with early cancellation
			// from previous reload attempts (explicit order, host building died, etc).
			foreach (var pool in RearmableAmmoPools)
				pool.RemainingTicks = pool.Info.ReloadDelay;
		}

		void INotifyDockClient.Undocked(Actor self, Actor dock) { }

		/// <summary>
		/// <para>The docking PULL half of resupply, and since 2026-08-27 it is METERED. It used to call
		/// <c>GiveAmmo</c> with no host in scope at all, which made a rearm at the Logistics Centre free —
		/// measured, not argued, in <c>test-vehicle-rearms-at-empty-depot</c>: a dry abrams refilled at a
		/// Centre holding ZERO with the depot's supply unmoved.</para>
		///
		/// <para>MUTUAL EXCLUSION WITH THE PUSH ARM, which is why <paramref name="host"/> had to arrive.
		/// A docked himars or iskander — the only two actors declaring <c>replenish-vehicles</c> — was
		/// being served by BOTH models at once: measured at ticks 150 and 225 of
		/// <c>test-who-pays-for-a-rearm</c>, one round paid for out of a 2250 depot and a second arriving
		/// while supply sat at 750, which is less than the 1500 a batch costs. Metering here without
		/// separating them would have converted that double-REARM into a double-CHARGE on two independent
		/// cadences. So this path DEFERS to the push arm for any client that arm can select, and serves
		/// only clients it cannot — which in this mod is every vehicle except those two.</para>
		///
		/// <para>Deferring rather than the reverse because the push arm carries machinery that would have
		/// to be duplicated here otherwise: its own RearmDelay/AuraRearmDelay cadence, the rearm condition
		/// grant, and <c>UpdateSupplyConditions</c>, which drives <c>SupplyLevelCondition</c> and through it
		/// the Logistics Centre's eight <c>Explodes@Band</c> traits. Cutting the push arm instead would have
		/// meant reimplementing all of that on the pull side to keep a cargo detonation honest.</para>
		///
		/// <para>Returns "done" — ending the errand — when every pool is full OR the host cannot afford any
		/// pool still wanting rounds. The second case is PARTIAL REFILL THEN LEAVE, and it must not become
		/// a wait: see <see cref="AmmoPool.TryServeBatch"/> for why a client parked at a depot that cannot
		/// pay is withheld from every bot module for the rest of the match.</para>
		/// </summary>
		public bool RearmTick(Actor self, Actor host)
		{
			var provider = host?.TraitOrDefault<SupplyProvider>();

			// The push arm owns any client it can select; serving here too is the double-serve.
			if (provider != null && provider.CanSelect(self))
				return false;

			var rearmComplete = true;
			foreach (var ammoPool in RearmableAmmoPools)
			{
				if (ammoPool.HasFullAmmo)
					continue;

				// Nothing here can ever pay for this pool, so waiting at the depot buys nothing.
				// Treated as "this pool is done" rather than as a reason to hold the client.
				if (provider != null && provider.CurrentSupply < ammoPool.Info.SupplyValue)
					continue;

				if (--ammoPool.RemainingTicks <= 0)
				{
					ammoPool.RemainingTicks = ammoPool.Info.ReloadDelay;
					AmmoPool.TryServeBatch(self, ammoPool, provider);
				}

				rearmComplete = false;
			}

			return rearmComplete;
		}
	}
}
