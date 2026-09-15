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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// "Unload All" on a garrisoned building must not hand it to Neutral while its firing ports are
	/// still manned.
	///
	/// <para>THE DEFECT. Two independent revert-to-neutral paths run on all four garrison families,
	/// and they disagree. GarrisonManager.CheckOwnershipAfterExit walks PortStates and the shelter
	/// list, so it sees every occupant. UnloadCargo's CargoInfo.Neutral flip tested
	/// <c>cargo.PassengerCount == 0</c> — the Cargo HOLD alone — and a soldier deployed to a firing
	/// port has been removed from the hold by GarrisonManager.DeployToPort. So emptying the shelter of
	/// a house whose ports were manned satisfied the flip's condition with men still inside, and the
	/// building went Neutral while its garrison went on shooting from it.</para>
	///
	/// <para>WHY A VETO AND NOT A SECOND CHECK. Cargo.Unload notifies INotifyPassengerExited
	/// SYNCHRONOUSLY (Cargo.cs), which reaches GarrisonManager.OnPassengerExited →
	/// CheckOwnershipAfterExit; UnloadCargo's flip is a frame-end task queued afterwards. The
	/// port-aware decision is therefore already taken and correct by the time the flip could run, and
	/// the only thing wrong was that the flip overwrote it. There is nothing to re-derive, and
	/// re-deriving it would recreate the disagreement this removes.</para>
	///
	/// <para>RED, both halves, and neither REDs on its own — which is why both are here.
	/// (a) Make MayRevertHoldToNeutral ignore its first argument (<c>return cargoNeutralFlag &amp;&amp;
	/// holdPassengerCount == 0;</c>) and TheVetoIsDecisiveOnItsOwn fails with "a garrisoned building
	/// was handed to Neutral with its ports still manned". (b) Revert the call site to the bare
	/// <c>cargo.PassengerCount == 0 &amp;&amp; cargo.Info.Neutral</c> and
	/// TheFlipStillAsksBeforeChangingOwner fails, because UnloadCargo then calls neither
	/// MayRevertHoldToNeutral nor anything else that has heard of a firing port.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonUnloadNeutralRevertTest
	{
		[Test]
		public void TheVetoIsDecisiveOnItsOwn()
		{
			// THE DEFECT, stated directly: the hold is empty and CargoInfo.Neutral is set — the exact
			// state "Unload All" leaves a house in while eight men keep firing from its ports.
			Assert.That(GarrisonOwnershipMath.MayRevertHoldToNeutral(true, 0, true), Is.False,
				"a garrisoned building was handed to Neutral with its ports still manned. " +
				"Cargo.PassengerCount is the hold, not the building: GarrisonManager.DeployToPort " +
				"removes a soldier from the hold when he mans a port, so zero here is not evidence " +
				"that anybody has left. The trait that owns the port-aware decision said no.");
		}

		[Test]
		public void TheVetoOverridesEveryOtherCombination()
		{
			// Decisive means decisive. No combination of the other two arguments may get past it —
			// guards the plausible-looking rewrite that ANDs the veto in last and lets a short-circuit
			// or a later edit reorder it into irrelevance.
			foreach (var count in new[] { 0, 1, 7 })
				foreach (var flag in new[] { true, false })
					Assert.That(GarrisonOwnershipMath.MayRevertHoldToNeutral(true, count, flag), Is.False,
						$"the veto was overridden by holdPassengerCount={count}, cargoNeutralFlag={flag}. " +
						"When a trait owns the revert decision it has already been made correctly and " +
						"nothing about the Cargo hold may reopen it.");
		}

		[Test]
		public void AnOrdinaryTransportStillRevertsWhenItEmpties()
		{
			// The other side of the fix, and the thing most at risk from it: an APC or a non-garrison
			// hold has no IOverridesCargoNeutralRevert trait, so CargoInfo.Neutral is its ONLY revert
			// path and must keep working exactly as before.
			Assert.That(GarrisonOwnershipMath.MayRevertHoldToNeutral(false, 0, true), Is.True,
				"CargoInfo.Neutral stopped working on actors that have no port-aware trait to defer " +
				"to. For those there is no second path — the building simply never goes back to " +
				"Neutral, and the veto has been applied where nothing was overriding anything.");
		}

		[Test]
		public void APartlyFullHoldNeverReverts()
		{
			Assert.That(GarrisonOwnershipMath.MayRevertHoldToNeutral(false, 1, true), Is.False,
				"an actor with a passenger still in the hold was reverted to Neutral.");
		}

		[Test]
		public void TheFlagStillGatesTheWholeThing()
		{
			Assert.That(GarrisonOwnershipMath.MayRevertHoldToNeutral(false, 0, false), Is.False,
				"an actor that never asked for CargoInfo.Neutral was reverted to Neutral anyway.");
		}

		/// <summary>
		/// Every method UnloadCargo owns, INCLUDING the compiler-generated closures. The flip lives in
		/// a lambda passed to World.AddFrameEndTask, so it is compiled onto a nested display class and
		/// is NOT reachable by scanning UnloadCargo's own declared methods — a scan that looked only at
		/// those would find nothing and pass no matter what the body did.
		/// </summary>
		static List<MethodBase> UnloadCargoBodies()
		{
			const BindingFlags All = BindingFlags.Instance | BindingFlags.Static
				| BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

			var types = new List<Type> { typeof(UnloadCargo) };
			types.AddRange(typeof(UnloadCargo).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

			var bodies = new List<MethodBase>();
			foreach (var t in types)
			{
				bodies.AddRange(t.GetMethods(All).Cast<MethodBase>());
				bodies.AddRange(t.GetConstructors(All).Cast<MethodBase>());
			}

			return bodies;
		}

		[Test]
		public void TheFlipStillAsksBeforeChangingOwner()
		{
			// THE CALL-SITE PIN. MayRevertHoldToNeutral can be perfect and unused: reverting the one
			// condition in UnloadCargo to `cargo.PassengerCount == 0 && cargo.Info.Neutral` restores
			// the defect and leaves every test above green.
			var bodies = UnloadCargoBodies();
			var callees = new List<MethodBase>();
			var resolved = 0;
			foreach (var body in bodies)
			{
				var scan = IlScan.Scan(body);
				callees.AddRange(scan.Callees);
				resolved += scan.ResolvedCalls;
			}

			Assert.That(resolved, Is.GreaterThan(20),
				$"IL scan resolved only {resolved} tokens across {bodies.Count} UnloadCargo bodies — " +
				"the scanner or the type enumeration is broken, not the code clean.");

			var changesOwner = callees.Any(c => c.Name == "ChangeOwnerSync");

			Assert.That(changesOwner, Is.True,
				"UnloadCargo no longer calls ChangeOwnerSync anywhere, so this fixture is pinning a " +
				"call site that has moved or gone. Either CargoInfo.Neutral was removed — in which " +
				"case delete this test — or the flip now lives somewhere this scan cannot see it.");

			var asksFirst = callees.Any(c => c.Name == "MayRevertHoldToNeutral");

			Assert.That(asksFirst, Is.True,
				"UnloadCargo changes an actor's owner but never calls " +
				"GarrisonOwnershipMath.MayRevertHoldToNeutral, so whatever now decides has not been " +
				"asked whether a trait owns that decision. The regression this guards is a one-line " +
				"revert to `cargo.PassengerCount == 0 && cargo.Info.Neutral`, which hands a garrisoned " +
				"house to Neutral while its firing ports are manned.");
		}

		[Test]
		public void GarrisonManagerIsStillTheTraitThatVetoes()
		{
			// The veto is only ever reached through the interface. If GarrisonManager stops
			// implementing it the flip silently returns to counting the hold, with nothing failing.
			Assert.That(typeof(IOverridesCargoNeutralRevert).IsAssignableFrom(typeof(GarrisonManager)), Is.True,
				"GarrisonManager no longer implements IOverridesCargoNeutralRevert. UnloadCargo finds " +
				"the veto by TraitsImplementing<IOverridesCargoNeutralRevert>() and will now find " +
				"nothing on a garrison building, restoring the hold-only flip in full.");
		}
	}
}
