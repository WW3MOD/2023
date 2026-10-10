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

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// When a transport changes hands, only the OUTGOING owner's passengers follow it.
	///
	/// <para>Cargo.OnOwnerChanged used to re-own every passenger. That is right for a vehicle, whose
	/// crew all share its owner, and wrong for a garrison building, which holds allied players' men
	/// side by side and whose owner is only whoever boarded first. The neutral->owner flip is a
	/// frame-end task, so an ally who boarded in the same drain was converted to the claimant —
	/// measured by test-garrison-allied-cogarrison, run 261010_233134 (both legs converted).</para>
	///
	/// <para>RED, both halves: (a) make PassengerFollowsOwnerChange return true unconditionally and
	/// AnAllyInsideAFlippingBuildingKeepsHisOwner fails; (b) revert Cargo.OnOwnerChanged to the bare
	/// loop and TheOwnerChangeHookStillAsks fails, because the body no longer calls the helper.</para>
	/// </summary>
	[TestFixture]
	public class CargoOwnerChangePassengerTest
	{
		// Stand-ins for Player, which cannot be constructed outside a World. Distinct instances, compared
		// by reference, exactly as Cargo compares Players.
		static readonly object Neutral = new();
		static readonly object Usa = new();
		static readonly object Allied = new();
		static readonly object Enemy = new();

		[Test]
		public void AnAllyInsideAFlippingBuildingKeepsHisOwner()
		{
			// THE DEFECT: neutral house flips to USA (first boarder) with an Allied man already inside.
			Assert.That(GarrisonOwnershipMath.PassengerFollowsOwnerChange(Allied, Neutral, Usa), Is.False,
				"an allied soldier inside a garrison building was re-owned to the player who claimed it. " +
				"Allied co-garrison silently hands one ally's men to the other.");
		}

		[Test]
		public void AnAllyKeepsHisOwnerWhenTheBuildingPassesToAnotherAlly()
		{
			// CheckOwnershipAfterExit's heir transfer: USA -> Allied with a third player's man inside.
			var third = new object();

			Assert.That(GarrisonOwnershipMath.PassengerFollowsOwnerChange(third, Usa, Allied), Is.False,
				"the heir transfer re-owned a third ally's soldier to the heir.");
		}

		[Test]
		public void TheOutgoingOwnersMenStillFollowTheTransport()
		{
			// The vehicle case the original loop exists for: a captured APC's crew changes sides with it.
			Assert.That(GarrisonOwnershipMath.PassengerFollowsOwnerChange(Usa, Usa, Enemy), Is.True,
				"the outgoing owner's own passenger no longer follows a transport that changes hands — " +
				"a captured or mind-controlled vehicle would now carry the previous owner's crew.");
			Assert.That(GarrisonOwnershipMath.PassengerFollowsOwnerChange(Neutral, Neutral, Usa), Is.True,
				"a Neutral-owned occupant of a neutral building no longer follows its flip, which " +
				"changes behaviour the allied fix had no reason to touch.");
		}

		[Test]
		public void APassengerAlreadyOwnedByTheNewOwnerIsLeftAlone()
		{
			Assert.That(GarrisonOwnershipMath.PassengerFollowsOwnerChange(Usa, Neutral, Usa), Is.False);
			Assert.That(GarrisonOwnershipMath.PassengerFollowsOwnerChange<object>(null, Neutral, Usa), Is.False);
		}

		[Test]
		public void TheOwnerChangeHookStillAsks()
		{
			// THE CALL-SITE PIN. The helper can be perfect and unused: reverting OnOwnerChanged to the
			// bare loop restores the defect and leaves every test above green.
			var method = typeof(Cargo)
				.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.SingleOrDefault(m => m.Name.EndsWith("INotifyOwnerChanged.OnOwnerChanged", StringComparison.Ordinal));

			Assert.That(method, Is.Not.Null,
				"Cargo no longer implements INotifyOwnerChanged.OnOwnerChanged explicitly, so this fixture " +
				"is no longer pinning the passenger re-owning hook at all.");

			var scan = IlScan.Scan(method);

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(2),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in Cargo.OnOwnerChanged — the scanner " +
				"is broken, not the code clean.");

			Assert.That(scan.Callees.Any(c => c.Name == "PassengerFollowsOwnerChange"), Is.True,
				"Cargo.OnOwnerChanged no longer calls GarrisonOwnershipMath.PassengerFollowsOwnerChange, " +
				"so every passenger is re-owned again — an ally inside a garrison building that changes " +
				"hands becomes the new owner's soldier.");
		}
	}
}
