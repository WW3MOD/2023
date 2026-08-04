#region Copyright & License Information
/*
 * WW3MOD out-of-ammo disposition (@experimental) — rearm-or-evacuate decision (pure math).
 *
 * PERCEIVED BEHAVIOUR: a bot combat vehicle that has shot itself dry no longer stands at the front as a free
 * kill. It either drives to a rearm source (Logistics Centre) or, when the sector holds no source at all, rotates
 * off the map edge and returns its residual budget to the treasury.
 *
 * WHY THIS LAYER EXISTS (the root cause it works around): AmmoPool's auto-rearm path already handles the case
 * where a source EXISTS — AutoRearm queues a Resupply/SeekSupplyProvider (AmmoPool.cs:277-312). Its ELSE branch,
 * taken when ChooseResupplier finds nothing, is FLAG-ONLY: it sets NeedsResupply = true on every pool and returns
 * (AmmoPool.cs:313-320, with the "Evacuation only happens when ResupplyBehavior is explicitly set to Evacuate"
 * comment). Ground vehicles default to ResupplyBehavior.Auto (defaults.yaml:318-319), so an empty vehicle with no
 * reachable Logistics Centre raises a flag nobody answers and keeps fighting with an empty gun. Nothing in the
 * unit-side trait can fix this: the decision "is there a source worth driving to, and if not do we cut our
 * losses?" is a COMMANDER-level judgement about the sector, so it belongs in the bot module.
 *
 * DECIDED DEFAULT — TERMINAL EVAC. When no source is available the disposition is final: evacuate and sell. There
 * is deliberately NO hold-and-recheck loop (park, wait, re-scan, maybe evacuate later). A parked empty vehicle is
 * exactly the pooling pathology this work removes, and the recheck loop is what turns a one-shot decision into an
 * oscillation. The unit's residual value is recovered instead: RotateToEdge refunds GetSellValue x HP/MaxHP
 * (RotateToEdge.cs:275-280), which is the DOCS/reference/economy.md evac formula — so an empty tank still returns
 * most of its budget (ammo is a small share of a tank's cost) and the money buys a fresh, loaded one.
 *
 * WHAT IS AND IS NOT IN THIS CLASS: the decision only. Source DISCOVERY stays with the caller
 * (AmmoPool.ChooseResupplier, which already filters ownership / RearmActors / remaining supply), because it needs
 * the world. That split is what makes the judgement testable without a game: the caller reduces the world to
 * "does a source exist, and how far is it", and this class turns those integers into an action.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer comparisons, no collection iteration.
 * Two clients over the same synced state decide identically.
 *
 * v3-portable: engine-free static math (NUnit-pinned in AmmoEvacMathTest); only the world-reading plumbing that
 * feeds it the pool state + source distance is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Disposition for a combat unit that has run dry. <see cref="None"/> means "leave the unit alone" —
	/// it is either still fighting effectively or physically cannot be sent anywhere.</summary>
	public enum AmmoEvacAction
	{
		/// <summary>Not out of ammo, or immobile — no bot action.</summary>
		None,

		/// <summary>A rearm source exists within the seek budget — drive to it and reload.</summary>
		SeekRearm,

		/// <summary>No source within the seek budget — TERMINAL evac: rotate off the map edge and refund.</summary>
		Evacuate,
	}

	public static class AmmoEvacMath
	{
		/// <summary>Is a source at <paramref name="distanceCells"/> worth driving to? A budget of
		/// <c>&lt;= 0</c> means UNLIMITED (any existing source is worth the drive) — the inert/legacy reading, since
		/// the pre-change engine path drove to the closest source at any range. A positive budget caps the detour so
		/// a unit does not cross the whole map past the enemy to reach the one surviving depot; beyond it the unit is
		/// worth more as a refund than as a very slow reinforcement. Pure.</summary>
		public static bool WithinSeekBudget(int distanceCells, int maxSeekDistanceCells)
			=> maxSeekDistanceCells <= 0 || distanceCells <= maxSeekDistanceCells;

		/// <summary>The out-of-ammo disposition. Mirrors the engine's own "all pools empty" predicate: a unit with
		/// no ammo pools, or with any pool still holding rounds, is NOT out of ammo and is left alone.
		///   * <paramref name="outOfAmmo"/> false ⇒ <see cref="AmmoEvacAction.None"/> (still has rounds).
		///   * <paramref name="canMove"/> false ⇒ <see cref="AmmoEvacAction.None"/>. An immobile unit can neither
		///     reach a source nor reach the map edge, so neither action is issuable; ordering one would only cancel
		///     whatever it is doing. (Static defences self-reload via ReloadAmmoPool — see economy.md.)
		///   * a source exists and is inside the seek budget ⇒ <see cref="AmmoEvacAction.SeekRearm"/>.
		///   * otherwise ⇒ <see cref="AmmoEvacAction.Evacuate"/> — the terminal disposition (see the header note on
		///     why there is no hold-and-recheck state).
		/// Pure integer/bool, zero RNG.</summary>
		public static AmmoEvacAction Decide(bool outOfAmmo, bool canMove, bool sourceExists,
			int sourceDistanceCells, int maxSeekDistanceCells)
		{
			if (!outOfAmmo || !canMove)
				return AmmoEvacAction.None;

			if (sourceExists && WithinSeekBudget(sourceDistanceCells, maxSeekDistanceCells))
				return AmmoEvacAction.SeekRearm;

			return AmmoEvacAction.Evacuate;
		}

		/// <summary>Cash an evacuating unit returns to the treasury: <paramref name="sellValue"/> scaled by the
		/// surviving health fraction. This is the DOCS/reference/economy.md evac rule and mirrors the engine's own
		/// arithmetic at RotateToEdge.cs:275-280 (integer-truncating, long-widened so a large sell value on a
		/// high-MaxHP hull cannot overflow). Exposed so the sweep can LOG what a disposition is worth (the `banked=`
		/// field of the `[exp-ooa] sweep` line) — the refund itself is paid by the activity, not by this class. A
		/// non-positive <paramref name="maxHp"/> reads as full health (the engine's health == null fallback). Pure.</summary>
		public static int EvacRefund(int sellValue, int hp, int maxHp)
		{
			if (sellValue <= 0)
				return 0;

			if (maxHp <= 0)
				return sellValue;

			var clamped = hp < 0 ? 0 : (hp > maxHp ? maxHp : hp);
			return (int)((long)sellValue * clamped / maxHp);
		}
	}
}
