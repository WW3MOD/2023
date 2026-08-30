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
		const int CallOpcode = 0x28;
		const int CallvirtOpcode = 0x6F;

		[Test]
		public void CanTargetFrozenActorOverridesNeverReadTheLiveActor()
		{
			var liveActorGetter = typeof(FrozenActor).GetProperty(nameof(FrozenActor.Actor)).GetGetMethod();
			Assert.That(liveActorGetter, Is.Not.Null, "FrozenActor.Actor getter not found — this test no longer scans what it claims to.");

			var resolvedCalls = 0;
			var offenders = new List<string>();

			foreach (var method in FrozenTargetingMethods())
			{
				var body = method.GetMethodBody();
				if (body == null)
					continue;

				var il = body.GetILAsByteArray();
				if (il == null)
					continue;

				foreach (var callee in CalledMethods(method, il, ref resolvedCalls))
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

			Assert.That(offenders, Is.Empty,
				"These CanTargetFrozenActor overrides dereference FrozenActor.Actor and so let a cursor " +
				"vary with state hidden by fog. Read target.Info / target.Owner / target.HP instead; where " +
				"the snapshot genuinely lacks the information, the cursor must not vary at all:" +
				Environment.NewLine + string.Join(Environment.NewLine, offenders));
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

			Assert.That(scanned.Count, Is.GreaterThan(5),
				$"Only {scanned.Count} CanTargetFrozenActor overrides found; the reflection query has drifted.");
		}

		static IEnumerable<MethodInfo> FrozenTargetingMethods()
		{
			var assemblies = new[] { typeof(EnterAlliedActorTargeter<>).Assembly, typeof(FrozenActor).Assembly };

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

		static IEnumerable<MethodBase> CalledMethods(MethodInfo method, byte[] il, ref int resolvedCalls)
		{
			var typeArgs = method.DeclaringType.IsGenericType
				? method.DeclaringType.GetGenericArguments()
				: Type.EmptyTypes;
			var methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : Type.EmptyTypes;

			var results = new List<MethodBase>();
			for (var i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != CallOpcode && il[i] != CallvirtOpcode)
					continue;

				var token = BitConverter.ToInt32(il, i + 1);
				try
				{
					var callee = method.Module.ResolveMethod(token, typeArgs, methodArgs);
					if (callee != null)
					{
						resolvedCalls++;
						results.Add(callee);
					}
				}
				catch (ArgumentException)
				{
					// Not a method token — the byte matched mid-operand of another instruction.
				}
			}

			return results;
		}
	}
}
