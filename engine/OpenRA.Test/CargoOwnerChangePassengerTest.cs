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
	/// When a GARRISON building changes hands, only the OUTGOING owner's passengers follow it; a
	/// vehicle's whole crew still follows, exactly as before.
	///
	/// <para>Cargo.OnOwnerChanged used to re-own every passenger. That is right for a vehicle (two
	/// allies boarding one APC on a tick rely on it to converge on one owner), and wrong for a garrison
	/// building, which holds allied players' men side by side and whose owner is only whoever boarded
	/// first. The neutral->owner flip is a frame-end task, so an ally who boarded in the same drain was
	/// converted to the claimant — measured by test-garrison-allied-cogarrison, run 261010_233134 (both
	/// legs converted).</para>
	///
	/// <para>RED: (a) make PassengerFollowsOwnerChange return true unconditionally and
	/// AnAllyInsideAFlippingBuildingKeepsHisOwner fails; (b) revert Cargo.OnOwnerChanged to the bare
	/// loop and TheOwnerChangeHookStillAsks fails, because the body no longer calls the helper;
	/// (c) swap oldOwner/newOwner at the call and TheHelperIsPassedOldThenNewOwner fails; (d) drop the
	/// GarrisonManagerInfo test and VehiclesKeepTheAllConvertLoop fails.</para>
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
		public void TheOutgoingOwnersMenStillFollowTheBuilding()
		{
			Assert.That(GarrisonOwnershipMath.PassengerFollowsOwnerChange(Usa, Usa, Enemy), Is.True,
				"the outgoing owner's own passenger no longer follows a garrison that changes hands.");
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

		static MethodBase OnOwnerChanged()
		{
			var method = typeof(Cargo)
				.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.SingleOrDefault(m => m.Name.EndsWith("INotifyOwnerChanged.OnOwnerChanged", StringComparison.Ordinal));

			Assert.That(method, Is.Not.Null,
				"Cargo no longer implements INotifyOwnerChanged.OnOwnerChanged explicitly, so this fixture " +
				"is no longer pinning the passenger re-owning hook at all.");

			return method;
		}

		[Test]
		public void TheOwnerChangeHookStillAsks()
		{
			// THE CALL-SITE PIN. The helper can be perfect and unused: reverting OnOwnerChanged to the
			// bare loop restores the defect and leaves every test above green.
			var scan = IlScan.Scan(OnOwnerChanged());

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(2),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in Cargo.OnOwnerChanged — the scanner " +
				"is broken, not the code clean.");

			Assert.That(scan.Callees.Any(c => c.Name == "PassengerFollowsOwnerChange"), Is.True,
				"Cargo.OnOwnerChanged no longer calls GarrisonOwnershipMath.PassengerFollowsOwnerChange, " +
				"so every passenger is re-owned again — an ally inside a garrison building that changes " +
				"hands becomes the new owner's soldier.");
		}

		[Test]
		public void TheHelperIsPassedOldThenNewOwner()
		{
			// ARGUMENT ORDER, which a callee list cannot see: swapping oldOwner/newOwner compiles, still
			// calls the helper, and stops the claimant's own men from following. The explicit
			// implementation's arguments are (this, self, oldOwner, newOwner), so the two loads directly
			// before the call must be ldarg.2 (0x04) then ldarg.3 (0x05). Raw bytes, because IlScan
			// records callees and not their operands.
			var method = OnOwnerChanged();
			var il = method.GetMethodBody().GetILAsByteArray();
			var sites = 0;

			for (var i = 2; i + 4 < il.Length; i++)
			{
				if (il[i] != 0x28)
					continue;

				MethodBase callee;
				try
				{
					callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
				}
				catch (ArgumentException)
				{
					// Not a method token — the byte matched mid-operand of another instruction.
					continue;
				}

				if (callee?.Name != "PassengerFollowsOwnerChange")
					continue;

				sites++;
				Assert.That(new[] { il[i - 2], il[i - 1] }, Is.EqualTo(new byte[] { 0x04, 0x05 }),
					"PassengerFollowsOwnerChange is not passed (oldOwner, newOwner) as its last two " +
					"arguments. Swapped, it asks whether a passenger belonged to the NEW owner, which nobody " +
					"about to be converted did — so the claimant's own men would stop following the building.");
			}

			Assert.That(sites, Is.EqualTo(1),
				$"found {sites} call(s) to PassengerFollowsOwnerChange in Cargo.OnOwnerChanged; the " +
				"argument-order check only means something against exactly one.");
		}

		[Test]
		public void VehiclesKeepTheAllConvertLoop()
		{
			// The helper is GARRISON-ONLY. Two allies boarding one APC on a tick each flip it in
			// Cargo.Load, and the all-convert loop is what makes the crew converge on the last of them;
			// the per-passenger rule would leave mixed owners. Keyed exactly like Load's own exemption.
			var scan = IlScan.Scan(OnOwnerChanged());

			var keyedOnGarrison = scan.Callees.Any(c => c.Name == "HasTraitInfo" && c.IsGenericMethod &&
				c.GetGenericArguments().SingleOrDefault() == typeof(GarrisonManagerInfo));

			Assert.That(keyedOnGarrison, Is.True,
				"Cargo.OnOwnerChanged no longer tests HasTraitInfo<GarrisonManagerInfo>, so the " +
				"per-passenger rule now reaches vehicles too and two allies sharing an APC are left with " +
				"mixed owners instead of converging.");
		}
	}
}
