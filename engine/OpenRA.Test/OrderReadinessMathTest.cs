#region Copyright & License Information
/*
 * WW3MOD OrderReadinessMath tests — the POLARITY of every readiness-gated cursor, and the
 * ELIGIBILITY rule that goes with it.
 *
 * The rule under test, from the user (2026-08-30): "shows the cursor for the capable and silently
 * drops the others". So blocked ONLY when nothing in the selection can act.
 *
 * WHY THIS FILE EXISTS. The polarity is one `!` away from its own mirror, and BOTH readings are
 * plausible sentences in English — "blocked when they cannot act" describes either. Get it backwards
 * and a click that would have worked for half the selection reads as refused, which is a worse lie
 * than the one the fix replaced and is invisible without a mixed selection to test on.
 *
 * SCOPE, HONESTLY. An adversarial review found three defects in the CALL SITES while every test here
 * stayed green, because each site supplies three things — the candidate set, the eligibility test
 * and the queued flag — and the original tests pinned none of them. Two of the three are now
 * pinnable and pinned below (eligibility, and the queued term). The third, "does the guard site read
 * shift from the MouseInput rather than hardcoding false", needs a World and a widget and is NOT
 * covered here; it is verified by reading only. Said plainly so the next reader does not mistake a
 * green run for whole-feature cover.
 *
 * The candidates are ints, not Actors, so eligibility and readiness can be told apart per candidate
 * without a World. The combinator is generic for exactly this reason.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Orders;

namespace OpenRA.Test
{
	[TestFixture]
	public class OrderReadinessMathTest
	{
		// Sentinels. Read them as "unit 1, unit 2, ..." — identity is all the lambdas need.
		static int[] Units(params int[] ids) { return ids; }

		static Func<int, bool> All => _ => true;
		static Func<int, bool> None => _ => false;
		static Func<int, bool> Only(params int[] ids) { return i => Array.IndexOf(ids, i) >= 0; }

		// ---------- the polarity ----------

		[Test]
		public void BlockedOnlyWhenNothingCanAct()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3), false, All, All), Is.True,
				"every eligible unit would be dropped, so the click achieves nothing and must say so");
		}

		// THE MIRROR THIS FILE EXISTS TO CATCH. One capable unit in a selection of five keeps the
		// normal cursor: the click still does something, and the refusers are dropped in silence
		// exactly as their ResolveOrder already drops them.
		[Test]
		public void OneCapableUnitKeepsTheNormalCursor()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3, 4, 5), false, All, Only(1, 2, 4, 5)), Is.False,
				"unit 3 can act, so the click is worth making; blocking it is the mirror lie");
		}

		// Position must not matter. A capable unit at the HEAD is the case that passes by accident
		// under an implementation that only ever looks at the first candidate.
		[Test]
		public void PositionOfTheCapableUnitDoesNotMatter()
		{
			Assert.Multiple(() =>
			{
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3, 4), false, All, Only(2, 3, 4)), Is.False,
					"capable at the head");
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3, 4), false, All, Only(1, 2, 3)), Is.False,
					"capable at the tail counts as much as one at the head");
			});
		}

		[Test]
		public void EveryoneCapableIsNeverBlocked()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3), false, All, None), Is.False);
		}

		[Test]
		public void ASingleUnitWorksBothWays()
		{
			Assert.Multiple(() =>
			{
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1), false, All, All), Is.True);
				Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1), false, All, None), Is.False);
			});
		}

		// ---------- eligibility, which is NOT readiness ----------
		//
		// The defect these were added for: both attack-move sites counted every actor carrying
		// AttackMoveInfo, but the trait rides on ^AutoTarget and immobile AA/ICBM defences carry it
		// as a no-op that both the targeter and ResolveOrder then refuse. Because
		// AmmoPool.AllPoolsEmpty returns FALSE for an actor with no pools at all, such a defence
		// answers "can act" — so box-selecting one beside a dry tank painted a GREEN cursor over a
		// click that does nothing. A false blocked was replaced by a false green.

		// Unit 1 cannot receive the order but CAN act; unit 2 can receive it and cannot act.
		// The only unit that could be ordered would be dropped, so this is blocked.
		[Test]
		public void AnIneligibleCandidateCannotRescueTheCursor()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2), false, Only(2), Only(2)), Is.True,
				"unit 1 is able but cannot be given this order at all; it must not paint the cursor green");
		}

		// The same shape with the ineligible one at the tail, so this cannot pass by short-circuiting.
		[Test]
		public void AnIneligibleCandidateCannotRescueTheCursorFromTheTailEither()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2), false, Only(1), Only(1)), Is.True);
		}

		// Nobody could be given the order, so nothing was refused — this must NOT read as blocked,
		// or selecting a pure group of defences would grey out a cursor that was never theirs.
		[Test]
		public void ASelectionWithNoEligibleCandidateIsNotBlocked()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3), false, None, All), Is.False,
				"vacuous refusal is not refusal");
		}

		// Eligibility is asked FIRST and an ineligible candidate is never asked whether it can act.
		// Pins the two questions apart: a call site that conflated them would still pass the tests
		// above by accident.
		[Test]
		public void ReadinessIsNeverAskedOfAnIneligibleCandidate()
		{
			var asked = new List<int>();
			OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3), false, Only(2), i => { asked.Add(i); return true; });

			Assert.That(asked, Is.EqualTo(new[] { 2 }),
				"only the eligible candidate should ever be asked the readiness question");
		}

		// ---------- the queued term, which is part of the polarity ----------

		// Shift means "when you are able". Every execution-side gate in this family is scoped to
		// !order.Queued, so blocking a queued click would deny an order that genuinely runs once the
		// unit rearms — trading one lie for its exact mirror. The guard site hardcoded this to false
		// and shipped that mirror; the flag is a required parameter so the choice stays visible.
		[Test]
		public void QueuingReopensTheOrderEvenWhenNobodyCanActNow()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(1, 2, 3), true, All, All), Is.False,
				"shift-queued means 'when you are able', and that order really does run once rearmed");
		}

		// ---------- degenerate input ----------

		// Guards the `any` flag: a naive All()-style implementation returns TRUE for an empty set and
		// would paint a blocked cursor over a selection containing no subjects for this order.
		[Test]
		public void AnEmptySelectionIsNotBlocked()
		{
			Assert.That(OrderReadinessMath.ReadsAsBlocked(Units(), false, All, All), Is.False,
				"vacuous truth must not become a blocked cursor");
		}
	}
}
