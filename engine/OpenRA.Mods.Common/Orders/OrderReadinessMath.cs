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
		/// <paramref name="candidates"/>.
		/// </summary>
		/// <param name="candidates">
		/// Everything the click might touch — typically the raw selection. It is NOT pre-filtered;
		/// <paramref name="canReceiveOrder"/> does that here so no call site can forget to.
		/// </param>
		/// <param name="queued">Whether the player is holding the queue modifier.</param>
		/// <param name="canReceiveOrder">
		/// Whether this actor could be given the order AT ALL. Required, and separate from
		/// <paramref name="cannotAct"/>, because an actor that cannot receive the order must not be
		/// able to answer "can act" and rescue the cursor. That is not hypothetical: AttackMove sits
		/// on ^AutoTarget, so immobile AA/ICBM defences carry it as a documented no-op, and
		/// AmmoPool.AllPoolsEmpty returns FALSE for an actor with no pools at all — so a poolless
		/// defence reads as fully able and paints a green cursor over a selection that cannot move.
		/// </param>
		/// <param name="cannotAct">Per-actor readiness test. True means "this one would be dropped".</param>
		/// <returns>
		/// True only when the order is unqueued AND at least one candidate can receive it AND every
		/// such candidate would be dropped. A set with no eligible candidate reads as NOT blocked:
		/// nothing was asked of anyone, so nothing was refused.
		/// </returns>
		/// <remarks>
		/// Generic in the candidate type purely so the polarity can be pinned without a World: the
		/// tests drive it with plain sentinels and explicit lambdas. Every production caller passes
		/// Actor.
		/// </remarks>
		public static bool ReadsAsBlocked<T>(
			IEnumerable<T> candidates, bool queued, Func<T, bool> canReceiveOrder, Func<T, bool> cannotAct)
		{
			if (queued)
				return false;

			// PERF: single pass, no LINQ. Callers that ask this per-actor must memoise — see
			// SelectionMemo — or the per-frame cost is quadratic in the selection size.
			var any = false;
			foreach (var a in candidates)
			{
				if (!canReceiveOrder(a))
					continue;

				any = true;
				if (!cannotAct(a))
					return false;
			}

			return any;
		}
	}

	/// <summary>
	/// A one-entry memo for a per-SELECTION question asked from a per-ACTOR code path.
	/// </summary>
	/// <remarks>
	/// <para>IOrderTargeter.CanTarget runs once per selected actor per mouse-move frame, so asking a
	/// selection-wide question inside it is O(n²) — and UnitOrderGenerator.ResolveSelection can run
	/// the whole pass twice. At a 100-unit selection that is tens of thousands of
	/// TraitsImplementing lookups per frame. One shared memo collapses it back to O(n).</para>
	///
	/// <para>Keyed on (world, selection hash, tick). Ammo and selection cannot change within a tick,
	/// so a hit is never stale by more than the frame it was asked in. Instances must be STATIC — a
	/// per-actor memo would compute once per actor and save nothing.</para>
	///
	/// <para>Sync-safe by construction: every caller uses this to choose a CURSOR STRING only. None
	/// of it reaches IssueOrder or ResolveOrder, so a stale or cold answer cannot change what the
	/// simulation does on any client.</para>
	/// </remarks>
	public sealed class SelectionMemo
	{
		World world;
		int hash;
		int tick;
		bool result;

		public bool ReadsAsBlocked(
			World w, IEnumerable<Actor> candidates, bool queued, Func<Actor, bool> canReceiveOrder, Func<Actor, bool> cannotAct)
		{
			// Not memoised across the modifier: queued is an input, not selection state.
			if (queued)
				return false;

			var h = w.Selection.Hash;
			var t = w.WorldTick;
			if (world == w && hash == h && tick == t)
				return result;

			result = OrderReadinessMath.ReadsAsBlocked(candidates, false, canReceiveOrder, cannotAct);
			world = w;
			hash = h;
			tick = t;
			return result;
		}
	}
}
