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

namespace OpenRA.Test
{
	/// <summary>
	/// A garrisoned building whose owner has no men left inside may pass to an ALLY still inside, and
	/// to nobody else.
	///
	/// <para>GarrisonManager.CheckOwnershipAfterExit builds the set of players with a living occupant
	/// and, when the current owner is not among them, hands the building over. Its comment has always
	/// said "but an ally does → transfer"; the code said <c>remainingOwners.First()</c> — any player at
	/// all, with no relationship test anywhere in the branch. A building whose owner was killed out
	/// therefore handed itself to whoever was left, including an enemy: a capture with no
	/// CaptureManager, no Capturable, no technician and no timer.</para>
	///
	/// <para>WHY THIS IS A UNIT TEST AND NOT A SCENARIO. Reaching the bad branch in a running game
	/// needs two hostile players holding one building at once, and EnterAlliedActorTargeter refuses to
	/// let a player create that (it admits allied or neutral owners only). A scenario could only stage
	/// it through a test binding that bypasses the very gate that makes it unreachable, which measures
	/// the staging as much as the fix. The part that was WRONG is the choice, and the choice is
	/// ordinary bookkeeping over a sequence — so it is extracted and tested directly, and the call
	/// site is pinned separately by IL scan below.</para>
	///
	/// <para>RED, both halves: (a) change ChooseHeir to return the first element regardless of the
	/// predicate and NoHeirWhenNobodyLeftInsideIsAnAlly fails with "an enemy occupant was chosen as
	/// heir"; (b) revert the call site to <c>remainingOwners.First()</c> and
	/// TheTransferBranchStillAsksForAnAlly fails, because the body then calls Enumerable.First and no
	/// longer calls ChooseHeir at all. Neither half REDs on its own, which is why both are here.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonHeirSelectionTest
	{
		// Stand-ins for Player, which cannot be constructed outside a World. The predicate is what the
		// call site supplies (self.Owner.IsAlliedWith), so a string set models it exactly.
		static Func<string, bool> AlliesAre(params string[] allies)
		{
			var set = new HashSet<string>(allies);
			return p => set.Contains(p);
		}

		[Test]
		public void TheAllyInheritsTheBuilding()
		{
			var remaining = new[] { "Ally" };

			Assert.That(GarrisonOwnershipMath.ChooseHeir(remaining, AlliesAre("Ally")), Is.EqualTo("Ally"),
				"the one remaining occupant is an ally of the outgoing owner and did not inherit — an " +
				"allied co-garrison now loses the building to Neutral when its partner is killed out, " +
				"which is the behaviour the transfer branch exists to provide.");
		}

		[Test]
		public void NoHeirWhenNobodyLeftInsideIsAnAlly()
		{
			// THE DEFECT, stated directly. Pre-fix this returned "Enemy".
			var remaining = new[] { "Enemy" };

			Assert.That(GarrisonOwnershipMath.ChooseHeir(remaining, AlliesAre("Ally")), Is.Null,
				"an enemy occupant was chosen as heir. The building hands itself to a hostile player " +
				"with no CaptureManager, no Capturable, no technician and no capture timer — a free " +
				"capture that none of the capture documentation describes.");
		}

		[Test]
		public void AnEnemyAheadOfAnAllyIsSkippedRatherThanTakingThePlace()
		{
			// Guards the lazy fix: testing only element [0]'s relationship instead of filtering. The
			// enemy is deliberately first in enumeration order.
			var remaining = new[] { "Enemy", "Ally" };

			Assert.That(GarrisonOwnershipMath.ChooseHeir(remaining, AlliesAre("Ally")), Is.EqualTo("Ally"),
				"the first element was taken without the relationship test being applied to it, or the " +
				"search stopped at the first rejection instead of continuing.");
		}

		[Test]
		public void TheFirstAllyInEnumerationOrderWins()
		{
			// Determinism: no sorting of its own, first accepted element in the caller's order. The
			// caller's sequence is sim-deterministic (PortStates by index, then the shelter list).
			var remaining = new[] { "Enemy", "AllyA", "AllyB" };

			Assert.That(GarrisonOwnershipMath.ChooseHeir(remaining, AlliesAre("AllyA", "AllyB")), Is.EqualTo("AllyA"),
				"a later ally was preferred over an earlier one, so the pick depends on something other " +
				"than the caller's enumeration order and two clients can disagree about who owns a house.");
		}

		[Test]
		public void EmptyAndNullSequencesYieldNoHeir()
		{
			Assert.That(GarrisonOwnershipMath.ChooseHeir(Array.Empty<string>(), AlliesAre("Ally")), Is.Null);
			Assert.That(GarrisonOwnershipMath.ChooseHeir<string>(null, AlliesAre("Ally")), Is.Null);
			Assert.That(GarrisonOwnershipMath.ChooseHeir(new[] { "Ally" }, null), Is.Null,
				"a null predicate was treated as 'everyone qualifies', which is the permissive direction " +
				"and would restore the defect wherever a caller passed one.");
		}

		static MethodBase CheckOwnershipAfterExit()
		{
			var method = typeof(GarrisonManager)
				.GetMethod("CheckOwnershipAfterExit", BindingFlags.Instance | BindingFlags.NonPublic);

			Assert.That(method, Is.Not.Null,
				"GarrisonManager.CheckOwnershipAfterExit was renamed or removed, so this fixture is no " +
				"longer pinning the ownership-transfer branch at all.");

			return method;
		}

		[Test]
		public void TheTransferBranchStillAsksForAnAlly()
		{
			// THE CALL-SITE PIN. The pure function above can be perfect and unused: reverting this one
			// line to remainingOwners.First() restores the defect and leaves every test above green.
			var scan = IlScan.Scan(CheckOwnershipAfterExit());

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(3),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in CheckOwnershipAfterExit — the " +
				"scanner is broken, not the code clean.");

			var asksForAnAlly = scan.Callees.Any(c => c.Name == "ChooseHeir");

			Assert.That(asksForAnAlly, Is.True,
				"CheckOwnershipAfterExit no longer calls GarrisonOwnershipMath.ChooseHeir, so whatever it " +
				"now uses to pick the new owner has not been checked for a relationship test. The " +
				"regression this guards is a one-line revert to remainingOwners.First().");

			var takesWhoeverIsFirst = scan.Callees.Any(c => c.Name == "First" && c.DeclaringType == typeof(Enumerable));

			Assert.That(takesWhoeverIsFirst, Is.False,
				"CheckOwnershipAfterExit calls Enumerable.First — the unfiltered pick is back in the " +
				"body. A building whose owner is killed out will hand itself to whoever happens to be " +
				"first among the remaining occupants, enemy included.");
		}
	}
}
