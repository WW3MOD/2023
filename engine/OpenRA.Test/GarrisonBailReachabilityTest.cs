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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Cargo.Damaged does three unrelated things in one method, and one of them is skipped for
	/// garrison buildings. That skip has to be scoped to the block it names, because a guard at
	/// METHOD scope silently acquires every block appended below it later.
	///
	/// That is not hypothetical — it is the history of this method. c9699af9 (2026-03-20) added
	/// `if (self.Info.HasTraitInfo&lt;GarrisonProtectionInfo&gt;()) return;` when the method held only
	/// damage forwarding, so returning early was correct and complete. 4e8e29e2 (2026-08-10) then
	/// appended the emergency bail underneath it, and the guard's meaning widened from "skip the
	/// forwarding" to "skip the rest of the method" without one character of it changing. Every
	/// garrisonable building has GarrisonProtection, so the bail was dead on exactly the actors it
	/// was written for, and stayed dead for four months while its comment still read "skip legacy
	/// damage forwarding".
	///
	/// No autotest can see this: the failure is a thing that does not happen. So the invariant is
	/// pinned structurally instead — the garrison guard must not be reachable at method scope in
	/// Damaged at all. Keeping it inside the helper it guards is what makes a future append safe by
	/// construction rather than by whoever reviews it remembering this.
	/// </summary>
	[TestFixture]
	public class GarrisonBailReachabilityTest
	{
		static MethodInfo DamagedMethod()
		{
			var map = typeof(Cargo).GetInterfaceMap(typeof(INotifyDamage));
			var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == nameof(INotifyDamage.Damaged));
			Assert.That(index, Is.GreaterThanOrEqualTo(0),
				"INotifyDamage.Damaged not found on Cargo — this test no longer scans what it claims to.");

			return map.TargetMethods[index];
		}

		static IEnumerable<MethodInfo> CargoMethods()
		{
			return typeof(Cargo).GetMethods(BindingFlags.Instance | BindingFlags.Static |
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
		}

		/// <summary>Whether a scanned body mentions GarrisonProtectionInfo as a generic argument,
		/// which is how the guard is spelled: ActorInfo.HasTraitInfo&lt;GarrisonProtectionInfo&gt;().</summary>
		static bool MentionsGarrisonProtection(IlScan.Result scan)
		{
			return scan.Callees.Any(c => c.IsGenericMethod &&
				c.GetGenericArguments().Contains(typeof(GarrisonProtectionInfo)));
		}

		[Test]
		public void TheGarrisonGuardIsNotAtMethodScopeInDamaged()
		{
			var damaged = DamagedMethod();
			var scan = IlScan.Scan(damaged);

			// Guard against a silent false GREEN: if token resolution broke, the scan would find
			// nothing and report a clean method without having inspected one.
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(10),
				$"IL scan resolved only {scan.ResolvedCalls} call targets in Cargo.Damaged — the scanner " +
				"is broken, not the method clean.");

			Assert.That(MentionsGarrisonProtection(scan), Is.False,
				"Cargo.Damaged tests HasTraitInfo<GarrisonProtectionInfo> at method scope. Every " +
				"garrisonable building carries GarrisonProtection, so whatever sits below that test is " +
				"dead for all of them — which is how the emergency bail shipped inert for four months. " +
				"Move the check inside the block it guards (the legacy damage forwarding) so it cannot " +
				"acquire code appended after it.");
		}

		[Test]
		public void DamagedStillDecidesTheEmergencyBail()
		{
			// Without this, deleting the bail outright would satisfy the test above. The point of
			// scoping the guard is that the bail becomes REACHABLE, so the bail has to still be here.
			var scan = IlScan.Scan(DamagedMethod());

			var shouldBail = typeof(Cargo).GetMethod(nameof(Cargo.ShouldEmergencyBail),
				BindingFlags.Public | BindingFlags.Static);
			Assert.That(shouldBail, Is.Not.Null, "Cargo.ShouldEmergencyBail not found.");

			var callsBail = scan.Callees.Any(c => c.MetadataToken == shouldBail.MetadataToken &&
				c.Module == shouldBail.Module);

			Assert.That(callsBail, Is.True,
				"Cargo.Damaged no longer consults ShouldEmergencyBail. The emergency bail decision is " +
				"made here; if it moved, this fixture is pinning the wrong method.");
		}

		[Test]
		public void TheGarrisonGuardStillExistsSomewhereInCargo()
		{
			// Scoping the guard must not become deleting it. GarrisonProtection genuinely does own
			// pass-through for garrison buildings (GarrisonProtection.cs:102-113 inflicts it), so the
			// legacy per-passenger forwarding must still be skipped for them or occupants are hit twice.
			var damaged = DamagedMethod();

			var holders = CargoMethods()
				.Where(m => m.MetadataToken != damaged.MetadataToken)
				.Where(m => MentionsGarrisonProtection(IlScan.Scan(m)))
				.Select(m => m.Name)
				.ToArray();

			Assert.That(holders, Is.Not.Empty,
				"No method on Cargo checks for GarrisonProtectionInfo any more. The guard was removed " +
				"rather than scoped, so garrison occupants now take GarrisonProtection's pass-through " +
				"AND the legacy forwarding on the same hit.");
		}
	}
}
