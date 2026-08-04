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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Guards the actor-exit contract on SupplyProvider.
	///
	/// The rearm condition is granted to a target as a PERMANENT external-condition token keyed by
	/// the granting provider. ExternalCondition has no source-death sweep — its Tick expiry walks
	/// only timedTokens, and the ReduceTicks decay path is inert unless configured, which infantry's
	/// ExternalCondition@AmmoReplenish does not do. So the only thing that ever releases the token
	/// is the provider itself, and a provider that leaves play without revoking orphans the grant:
	/// the target keeps replenish-soldiers, and with it a free ReloadAmmoPool trickle, for the rest
	/// of the match. That is not a corner case — the token is held during every serving cycle.
	///
	/// SCOPE, stated honestly: this is a STRUCTURAL pin, not a behavioural one. It asserts the trait
	/// still subscribes to the notifications that make the revoke reachable. It does NOT drive a
	/// serving cycle and observe a revoke — that needs a live Actor/World, and this test project has
	/// no such harness (every other fixture here is pure logic over structs). What it does catch is
	/// the realistic regression: someone refactoring the trait and dropping an interface from the
	/// declaration, which would silently restore the orphan with no other symptom.
	/// </summary>
	[TestFixture]
	public class SupplyProviderExitTest
	{
		[Test]
		public void RevokesOnRemovalFromWorld()
		{
			// The universal catch, and the load-bearing one: every exit path routes through
			// World.Remove, which is also the moment ITick stops and SyncTargetCondition can no
			// longer run. Also the only cover for being picked up by a Carryall, which removes the
			// provider without ever disposing it.
			Assert.That(typeof(INotifyRemovedFromWorld).IsAssignableFrom(typeof(SupplyProvider)), Is.True);
		}

		[Test]
		public void RevokesOnDeath()
		{
			// Dispose is a frame-end task; Killed releases the target at the moment of death instead.
			Assert.That(typeof(INotifyKilled).IsAssignableFrom(typeof(SupplyProvider)), Is.True);
		}

		[Test]
		public void RevokesOnDisposal()
		{
			// Actor.Dispose only calls World.Remove `if (IsInWorld)`, so an actor removed earlier and
			// destroyed afterwards would never fire RemovedFromWorld again.
			Assert.That(typeof(INotifyActorDisposing).IsAssignableFrom(typeof(SupplyProvider)), Is.True);
		}
	}
}
