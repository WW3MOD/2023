#region Copyright & License Information
/*
 * WW3MOD cursor-readiness polarity — the ONE statement of "when does a readiness-gated order read as
 * blocked", shared by every site that has one.
 *
 * THE RULE, from the user, 2026-08-30:
 *   "Just make sure it works for selections with both able and blocked units (shows the cursor for
 *    the capable and silently drops the others I guess is best)."
 *
 * So: NORMAL cursor while ANY subject can act. BLOCKED only when NOTHING in the selection can. The
 * incapable ones are dropped in silence, which is what their ResolveOrder already does.
 *
 * This is the cursor-side restatement of OrderFallbackMath.SelectionSuppressesRefusers, and it is
 * here rather than hand-written at each call site for the reason the whole cursor-honesty audit
 * exists: two predicates that look equivalent drift, and the drift is invisible until a player
 * reports it. There were three sites wanting this rule; there is one copy of it.
 *
 * WHAT IS *NOT* SHARED, DELIBERATELY: the per-actor readiness test. The three sites ask genuinely
 * different questions —
 *   - attack-move / guard: "can this unit fight at all" (AmmoPool.CannotFight, every pool dry)
 *   - minelayer:           "does this unit have MINES" (one specific pool)
 * — so cannotAct is a parameter. Collapsing them onto AmmoPool.CannotFight would silently stop
 * blocking a minelayer that is out of mines but still holding rifle rounds, which is the exact
 * scope-mismatch bug this audit found in AttackBase (armament-scoped display vs actor-scoped
 * execution). Share the polarity, not the question.
 *
 * THE QUEUED TERM IS PART OF THE POLARITY, not an afterthought. Every execution-side gate in this
 * family is scoped to !order.Queued, because shift-queued means "when you are able" and unqueued
 * means "now". A blocked cursor on a shift-queued click would deny an order that genuinely runs once
 * the unit has rearmed — trading one lie for its exact mirror. Stated here so no site can forget it.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Orders
{
	public static class OrderReadinessMath
	{
		/// <summary>
		/// Whether a readiness-gated order should DISPLAY as blocked for a click resolved over
		/// <paramref name="subjects"/>.
		/// </summary>
		/// <param name="subjects">
		/// The actors that would receive this order — for a grouped order generator, its subject
		/// list; for a per-unit targeter, the current selection filtered to actors carrying the
		/// trait. Every subject must be asked the SAME question or the answer is meaningless.
		/// </param>
		/// <param name="queued">Whether the player is holding the queue modifier.</param>
		/// <param name="cannotAct">Per-actor readiness test. True means "this one would be dropped".</param>
		/// <returns>
		/// True only when the order is unqueued AND the set is non-empty AND every member would be
		/// dropped. An empty set reads as NOT blocked: nothing was asked, so nothing is refused.
		/// </returns>
		public static bool ReadsAsBlocked(IEnumerable<Actor> subjects, bool queued, Func<Actor, bool> cannotAct)
		{
			if (queued)
				return false;

			// PERF: single pass, no LINQ — reached from GetCursor on every mouse-move frame.
			var any = false;
			foreach (var a in subjects)
			{
				any = true;
				if (!cannotAct(a))
					return false;
			}

			return any;
		}
	}
}
