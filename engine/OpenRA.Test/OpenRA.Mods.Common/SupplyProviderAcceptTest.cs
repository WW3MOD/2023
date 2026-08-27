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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Guards the one thing that makes SupplyProvider's shared accept test safe to share.
	///
	/// AcceptClient is called by THREE paths: the aura sweep, the garrison sweep, and CanSelect (which
	/// Rearmable.RearmTick consults to decide whether to stand down). The aura sweep guards it with
	/// IsValidTarget, which applies range. The garrison sweep deliberately does NOT, because a
	/// sheltered passenger has been removed from the world and carries a stale CenterPosition — its
	/// building is in range, so it is treated as in range.
	///
	/// So a range or position clause inside AcceptClient would be wrong for exactly one of its callers,
	/// and would delete the entire garrison clientele: soldiers sheltering in a building would stop
	/// being resupplied, with no exception, no lint error, and no failing scenario, because nothing in
	/// the autotest suite garrisons anyone. It would also look obviously correct to whoever wrote it —
	/// it is the "accept test", and testing whether a client is in range is what an accept test sounds
	/// like it should do.
	///
	/// The countermeasure is the SIGNATURE, not a comment: AcceptClient takes the Rearmable rather than
	/// the Actor, so there is no position in scope to test. Acquiring one means changing the parameter,
	/// which is a deliberate act rather than a one-line slip — and this fixture fails the build when it
	/// happens. Comments did not stop three copies of this predicate diverging in the first place, which
	/// is why this is a test and not a paragraph.
	///
	/// SCOPE, stated honestly rather than implied. This is a STRUCTURAL pin. It does not construct a
	/// provider, drive a serving cycle, or observe a garrisoned passenger being fed: that needs a live
	/// Actor and World, and this test project has no such harness — every fixture here is pure logic
	/// over structs or reflection over types. What it cannot catch is a range test smuggled in via the
	/// Rearmable itself (rearmable.Self.CenterPosition is reachable in principle) or added to
	/// CalculateNeed. The residual exposure is recorded rather than papered over.
	/// </summary>
	[TestFixture]
	public class SupplyProviderAcceptTest
	{
		static MethodInfo AcceptClient()
		{
			return typeof(SupplyProvider).GetMethod(
				"AcceptClient", BindingFlags.Instance | BindingFlags.NonPublic);
		}

		[Test]
		public void AcceptTestStillExists()
		{
			// The measured-something guard. Without it a rename turns every assertion below into a test
			// that passes by inspecting nothing, which is the exact failure mode this fixture exists to
			// prevent elsewhere.
			Assert.That(AcceptClient(), Is.Not.Null,
				"SupplyProvider.AcceptClient has been renamed or removed. It is the single accept test " +
				"shared by the aura sweep, the garrison sweep and CanSelect. If it was renamed, retarget " +
				"this fixture; if it was inlined back into its callers, that re-creates the duplicated " +
				"predicate whose divergence wedged a docked himars permanently — see b29930e4.");
		}

		[Test]
		public void AcceptTestCannotSeeAPosition()
		{
			var method = AcceptClient();
			Assert.That(method, Is.Not.Null, "SupplyProvider.AcceptClient not found — see AcceptTestStillExists.");

			var takesAnActor = method.GetParameters().Any(p => p.ParameterType == typeof(Actor));

			Assert.That(takesAnActor, Is.False,
				"SupplyProvider.AcceptClient now takes an Actor, which puts a POSITION back in scope for " +
				"the shared accept test. That breaks exactly one of its three callers, silently: the " +
				"garrison sweep feeds soldiers sheltering inside a building, and those passengers are " +
				"removed from the world with a stale CenterPosition, so any range or IsInWorld clause " +
				"added here refuses all of them. There is no exception, no lint error and no failing " +
				"scenario when that happens, because nothing in the autotest suite garrisons anyone — " +
				"the clientele just disappears. Range belongs in IsValidTarget, which the aura sweep " +
				"applies and the garrison sweep deliberately does not. Pass the Rearmable instead.");
		}

		[Test]
		public void NeedIsAPropertyOfThePoolsAlone()
		{
			// CalculateNeed is the one thing AcceptClient calls out to, so it is the obvious back door:
			// re-admitting an Actor there would put a position one call deeper without touching the
			// signature this fixture watches.
			var method = typeof(SupplyProvider).GetMethod(
				"CalculateNeed", BindingFlags.Static | BindingFlags.NonPublic);

			Assert.That(method, Is.Not.Null,
				"SupplyProvider.CalculateNeed has been renamed, removed, or is no longer static. It is " +
				"called from inside the shared accept test and is kept static-over-Rearmable so it " +
				"cannot reach provider state or a client position.");

			Assert.That(method.GetParameters().Any(p => p.ParameterType == typeof(Actor)), Is.False,
				"SupplyProvider.CalculateNeed now takes an Actor. It is called from AcceptClient, so this " +
				"re-opens the door AcceptTestCannotSeeAPosition closes: a position becomes reachable from " +
				"the shared accept path one call deeper. Need is a property of the ammo pools; pass the " +
				"Rearmable.");
		}
	}
}
