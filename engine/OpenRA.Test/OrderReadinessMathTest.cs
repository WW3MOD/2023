#region Copyright & License Information
/*
 * WW3MOD OrderReadinessMath tests — the POLARITY of every readiness-gated cursor, pinned.
 *
 * The rule under test, from the user (2026-08-30): "shows the cursor for the capable and silently
 * drops the others". So blocked ONLY when nothing in the selection can act.
 *
 * WHY THIS FILE EXISTS AT ALL. The polarity is one `!` away from its own mirror, and BOTH readings
 * are plausible sentences in English — "blocked when they cannot act" describes either. Get it
 * backwards and a click that would have worked for half the selection reads as refused, which is a
 * worse lie than the one the fix replaced, and it is invisible without a mixed selection to test on.
 * Three call sites share this function precisely so there is one thing to get right; these pin it.
 *
 * The subject lists here are nulls rather than Actors on purpose: the combinator never dereferences
 * a subject, it only counts them and asks the caller's predicate. That is the whole point of taking
 * the readiness question as a parameter — the three real sites ask genuinely different questions
 * (can this unit fight at all, versus does it have MINES) and must not be collapsed onto one.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA;
using OpenRA.Mods.Common.Orders;

namespace OpenRA.Test
{
	[TestFixture]
	public class OrderReadinessMathTest
	{
		// The combinator never touches a subject, so a list of nulls exercises it honestly.
		static List<Actor> Subjects(int count)
		{
			var l = new List<Actor>();
			for (var i = 0; i < count; i++)
				l.Add(null);

			return l;
		}

		static Func<Actor, bool> AllCan => _ => false;
		static Func<Actor, bool> NoneCan => _ => true;

		// ---------- the rule itself ----------

		// "Blocked only when NOTHING in the selection can act."
		[Test]
		public void BlockedOnlyWhenNothingCanAct()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(3), false, NoneCan), Is.True,
				"every subject would be dropped, so the click achieves nothing and must say so");
		}

		// THE MIRROR THIS FILE EXISTS TO CATCH. One capable unit in a selection of five is enough to
		// keep the normal cursor: the click still does something, and the four refusers are dropped
		// in silence exactly as their ResolveOrder already drops them.
		[Test]
		public void OneCapableSubjectKeepsTheNormalCursor()
		{
			var calls = 0;
			var onlyTheThirdCanAct = new Func<Actor, bool>(_ => ++calls != 3);

			Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(5), false, onlyTheThirdCanAct), Is.False,
				"a single able unit means the click is worth making; blocking it is the mirror lie");
		}

		// Position must not matter — the capable unit being FIRST is the case that would pass by
		// accident under a short-circuiting implementation that only ever looked at the head.
		[Test]
		public void PositionOfTheCapableSubjectDoesNotMatter()
		{
			var seen = 0;
			var onlyTheFirstCanAct = new Func<Actor, bool>(_ => ++seen != 1);

			Assert.Multiple(() =>
			{
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(4), false, onlyTheFirstCanAct), Is.False);

				var seenAgain = 0;
				var onlyTheLastCanAct = new Func<Actor, bool>(_ => ++seenAgain != 4);
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(4), false, onlyTheLastCanAct), Is.False,
					"a capable unit at the tail counts as much as one at the head");
			});
		}

		[Test]
		public void EveryoneCapableIsNeverBlocked()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(3), false, AllCan), Is.False);
		}

		// ---------- the queued term, which is part of the polarity ----------

		// Shift means "when you are able". Every execution-side gate in this family is scoped to
		// !order.Queued, so blocking a queued click would deny an order that genuinely runs after
		// the unit rearms — trading one lie for its exact mirror.
		[Test]
		public void QueuingReopensTheOrderEvenWhenNobodyCanActNow()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(3), true, NoneCan), Is.False,
				"shift-queued means 'when you are able', and that order really does run once rearmed");
		}

		// ---------- degenerate input ----------

		// Nothing was asked, so nothing is refused. Guards the `any` flag: a naive All()-style
		// implementation returns TRUE for an empty set and would paint a blocked cursor over a
		// selection that simply contains no subjects for this order.
		[Test]
		public void AnEmptySelectionIsNotBlocked()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(0), false, NoneCan), Is.False,
				"vacuous truth must not become a blocked cursor");
		}

		[Test]
		public void ASingleSubjectStillWorksBothWays()
		{
			Assert.Multiple(() =>
			{
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(1), false, NoneCan), Is.True);
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Subjects(1), false, AllCan), Is.False);
			});
		}
	}
}
