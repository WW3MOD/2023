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
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Scripting;

namespace OpenRA.Test
{
	/// <summary>
	/// A script queues an activity SYNCHRONOUSLY; an order for the same thing resolves a tick or
	/// more later. That one tick is the whole difference between the two paths, and it decided
	/// whether a rifleman could enter a neutral civilian building at all: Enter seeds its
	/// last-visible target only while the target is not hidden (Enter.cs:105-106) and gives up
	/// BEFORE queueing any move when it has none (:130-132), so a scripted approach begun before the
	/// world has finished computing visibility ends on tick one with the unit still on its start
	/// cell. FrozenUnderFog returns visible unconditionally for an ALLY-owned actor (:24, :130-133),
	/// which is why only NON-allied transports were affected and why this read for a week as a rule
	/// about ownership rather than about timing.
	///
	/// <para>Measured in run 260921_164455 by a three-lane scenario holding one variable still per
	/// lane: neutral via Lua moved 0 cells, owned via Lua loaded 2, neutral via the order layer
	/// loaded 2, and the cargo filter answered True throughout.</para>
	///
	/// <para>WHY THIS IS STRUCTURAL AND NOT BEHAVIOURAL. The behaviour needs a World, a shroud, two
	/// players and a tick loop — Enter's constructor alone takes an Actor with an IMove trait — so
	/// there is nothing here NUnit can drive. What NUnit can do is stop the plumbing from being
	/// quietly removed, which is the realistic regression: the parameter is optional and defaults to
	/// null, so dropping the argument compiles, passes every other test, and reinstates the bug in
	/// silence. tools/autotest/scenarios/test-garrison-neutral-entry is what asserts the behaviour,
	/// and its lane A exists for exactly this.</para>
	/// </summary>
	[TestFixture]
	public class ScriptedEnterTransportTest
	{
		[Test]
		public void TheScriptedPathHandsEnterAPositionToFallBackOn()
		{
			var enterCtor = typeof(Enter).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
				.SingleOrDefault(c => c.GetParameters().Any(p => p.ParameterType == typeof(WPos?)));

			Assert.That(enterCtor, Is.Not.Null,
				"Enter no longer accepts an initial target position, so a caller that knows where the " +
				"target is has no way to tell it. Without that, an Enter constructed against a target " +
				"the world has not yet computed visibility for ends on its first tick having moved " +
				"nothing — which is the defect measured in run 260921_164455.");

			var scan = IlScan.Scan(typeof(MobileProperties)
				.GetMethod(nameof(MobileProperties.EnterTransport)));

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(1),
				$"the IL scan resolved only {scan.ResolvedCalls} tokens in EnterTransport — the scanner " +
				"is broken, not the code clean. Read nothing into the assertion below.");

			// The argument is what regresses, not the parameter: RideTransport's position parameter is
			// optional, so `new RideTransport(self, target, null)` still compiles and still calls the
			// same four-argument constructor with `default` pushed. Arity therefore proves nothing.
			// Reading the transport's CenterPosition is the thing that only happens when a real
			// position is being passed, so that is what this asserts.
			Assert.That(scan.Callees.Any(c => c.DeclaringType == typeof(Actor)
				&& c.Name == "get_" + nameof(Actor.CenterPosition)), Is.True,
				"MobileProperties.EnterTransport no longer reads the transport's CenterPosition, so it " +
				"is handing Enter no fallback position and a scripted EnterTransport into a transport " +
				"its owner is not allied to will again end with the unit standing on its start cell, " +
				"having moved zero cells, with no refusal logged anywhere.");
		}
	}
}
