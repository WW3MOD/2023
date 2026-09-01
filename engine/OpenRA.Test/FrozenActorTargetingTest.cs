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
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// A CanTargetFrozenActor override runs while the target is under fog. FrozenActor.Actor hands back
	/// the REAL, live actor, so anything read through it is state the player is not allowed to see — and
	/// because the return value picks a CURSOR, that state is rendered under the mouse. Reading live
	/// occupancy there tells the player whether a transport or garrison they cannot see is already full.
	/// This scans the IL of every such override and fails on any that dereferences FrozenActor.Actor.
	/// </summary>
	[TestFixture]
	public class FrozenActorTargetingTest
	{
		/// <summary>
		/// The single site permitted to observe FrozenActor.Actor, and only to DECLINE the click. A dead
		/// ghost stays clickable (FrozenActorLayer only drops a frozen actor once it stops being
		/// visible), and claiming it would issue an order the resolver discards, leaving the unit
		/// motionless instead of letting UnitOrderGenerator rewrite the click into a Move. The argument
		/// is written out in full at the call site. Any other entry here is a defect.
		/// </summary>
		static readonly string[] LivenessCheckExemptions =
		{
			"OpenRA.Mods.Common.Orders.EnterAlliedActorTargeter`1.CanTargetFrozenActor"
		};

		[Test]
		public void CanTargetFrozenActorOverridesNeverReadTheLiveActor()
		{
			var liveActorGetter = typeof(FrozenActor).GetProperty(nameof(FrozenActor.Actor)).GetGetMethod();
			Assert.That(liveActorGetter, Is.Not.Null, "FrozenActor.Actor getter not found — this test no longer scans what it claims to.");

			var resolvedCalls = 0;
			var offenders = new List<string>();

			foreach (var method in FrozenTargetingMethods())
			{
				var scan = IlScan.Scan(method);
				resolvedCalls += scan.ResolvedCalls;

				foreach (var callee in scan.Callees)
				{
					if (callee.MetadataToken == liveActorGetter.MetadataToken &&
						callee.Module == liveActorGetter.Module)
					{
						offenders.Add($"{method.DeclaringType.FullName}.{method.Name}");
						break;
					}
				}
			}

			// Guard against a silent false GREEN: if token resolution broke, the scan above would find
			// nothing and report success without having inspected anything.
			Assert.That(resolvedCalls, Is.GreaterThan(20),
				$"IL scan resolved only {resolvedCalls} call targets — the scanner is broken, not the code clean.");

			Assert.That(offenders.Except(LivenessCheckExemptions), Is.Empty,
				"These CanTargetFrozenActor overrides dereference FrozenActor.Actor and so let a cursor " +
				"vary with state hidden by fog. Read target.Info / target.Owner / target.HP instead; where " +
				"the snapshot genuinely lacks the information, the cursor must not vary at all:" +
				Environment.NewLine + string.Join(Environment.NewLine, offenders.Except(LivenessCheckExemptions)));

			// A stale exemption is as bad as a missing one: it would silently license a future live read
			// at a site that no longer needs the allowance.
			Assert.That(LivenessCheckExemptions.Except(offenders), Is.Empty,
				"An entry in LivenessCheckExemptions no longer reads FrozenActor.Actor. Remove it, so the " +
				"allowance cannot quietly cover a live read added later.");
		}

		[Test]
		public void TheScanCoversTheKnownFrozenTargetingSites()
		{
			var scanned = FrozenTargetingMethods()
				.Select(m => m.DeclaringType.Name)
				.ToList();

			// EnterAlliedActorTargeter is the choke point every "enter that building" cursor flows
			// through, and is the reason this fixture exists. If the scan stops seeing it, the fixture
			// has quietly stopped guarding the thing it was written for.
			Assert.That(scanned.Any(n => n.StartsWith("EnterAlliedActorTargeter", StringComparison.Ordinal)), Is.True,
				"EnterAlliedActorTargeter is no longer covered by the frozen-targeting scan.");

			// 18 overrides across Mods.Common, Game and Mods.Cnc as of 2026-08-30. The floor is set AT the
			// known population, not comfortably below it, so that losing an assembly reference (which is
			// how Mods.Cnc went unscanned in the first version of this fixture) fails here instead of
			// passing quietly on a smaller set.
			Assert.That(scanned.Count, Is.GreaterThanOrEqualTo(18),
				$"Only {scanned.Count} CanTargetFrozenActor overrides found, expected at least 18. Either " +
				"an assembly is no longer referenced by OpenRA.Test, or the reflection query has drifted.");

			Assert.That(scanned, Does.Contain("DisguiseOrderTargeter"),
				"Mods.Cnc is not being scanned — ww3mod loads that assembly, so its targeters are live code.");
		}

		static IEnumerable<MethodInfo> FrozenTargetingMethods()
		{
			// Mods.Cnc is included because ww3mod loads it (mod.yaml Assemblies), so Disguise and
			// Infiltrates ship and their frozen targeters are live code.
			var assemblies = new[]
			{
				typeof(EnterAlliedActorTargeter<>).Assembly,
				typeof(FrozenActor).Assembly,
				typeof(OpenRA.Mods.Cnc.Traits.Infiltrates).Assembly
			};

			foreach (var assembly in assemblies)
			{
				foreach (var type in assembly.GetTypes())
				{
					MethodInfo[] methods;
					try
					{
						methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
							BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
					}
					catch (TypeLoadException)
					{
						continue;
					}

					foreach (var method in methods)
						if (method.Name == "CanTargetFrozenActor" && !method.IsAbstract)
							yield return method;
				}
			}
		}

	}
}
