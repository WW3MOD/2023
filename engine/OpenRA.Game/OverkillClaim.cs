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

namespace OpenRA
{
	/// <summary>Something that carries an incoming-damage tally other units can reserve against.
	/// Implemented by <see cref="Actor"/>; exists so the claim bookkeeping can be exercised without a World.</summary>
	public interface IOverkillTally
	{
		void AddIncomingDamage(int percent);
		void RemoveIncomingDamage(int percent);
	}

	/// <summary>
	/// <para>One shooter's single outstanding overkill claim.</para>
	///
	/// <para>A claim is a PREDICTION — "I am about to put this much of your health on the floor" — registered at the
	/// moment a unit commits, so that other units scanning in the same window see the target as already spoken
	/// for before any damage has landed. It is a RESERVATION, NOT A LEDGER ENTRY, and the two properties below
	/// are what make it one rather than the other:</para>
	///
	/// <para>
	///   * ONE SHOOTER IS ONE CLAIM. Re-committing replaces the held claim instead of stacking on it, so a unit
	///     that re-acquires the same target every rescan cannot inflate the tally on its own.
	///   * A CLAIM IS HANDED BACK WHEN THE SHOT RESOLVES (Armament, at projectile creation). Past that point the
	///     prediction has become a real projectile and the target's actual health carries the information; a
	///     reservation still held would double-count against the damage that is now inbound.
	/// </para>
	///
	/// <para>Without the second property claims only ever accumulate — the shared tally is nudged up by every
	/// commitment and pulled down by nothing but the periodic halving in Actor.Tick. A busy target then reads as
	/// permanently over-committed and AutoTarget.ChooseTarget declines it, which is the "my AA won't autotarget"
	/// defect: a battery engages one unit at a time, each joiner waiting out a decay period.</para>
	/// </summary>
	public sealed class OverkillClaim
	{
		IOverkillTally target;
		int percent;

		public bool IsHeld => target != null;

		/// <summary>Reserve <paramref name="claimPercent"/> of <paramref name="claimTarget"/>, releasing whatever
		/// this shooter had claimed before. A non-positive claim, or a null target, just releases.</summary>
		public void Claim(IOverkillTally claimTarget, int claimPercent)
		{
			Release();

			if (claimTarget == null || claimPercent <= 0)
				return;

			claimTarget.AddIncomingDamage(claimPercent);
			target = claimTarget;
			percent = claimPercent;
		}

		/// <summary>Hand the claim back. Safe to call when nothing is held, and safe to call repeatedly —
		/// every shot fired runs through here and most of them have no claim outstanding.</summary>
		public void Release()
		{
			if (target == null)
				return;

			// Read before clearing: RemoveIncomingDamage can re-enter nothing, but the ordering keeps this
			// correct if a future tally implementation ever notifies.
			var releasing = target;
			var releasingPercent = percent;
			target = null;
			percent = 0;

			releasing.RemoveIncomingDamage(releasingPercent);
		}
	}

	public static class OverkillClaimMath
	{
		/// <summary>
		/// <para>The tally left after a claim of <paramref name="claimPercent"/> is handed back.</para>
		///
		/// <para>PITFALL: clamped at zero rather than trusted. The tally decays independently of any claim — Actor.Tick
		/// halves it every 60 ticks — so a claim held across a decay boundary is worth less on the tally than the
		/// number the shooter recorded. Subtracting the recorded number unclamped would drive the tally negative.
		/// The clamp can under-count when a second shooter still holds a live claim, and that is the deliberate
		/// direction to err in: under-counting makes units MORE willing to engage, while over-counting is the
		/// defect this whole mechanism exists to avoid.</para>
		/// </summary>
		public static int Release(int tally, int claimPercent)
		{
			return Math.Max(0, tally - claimPercent);
		}
	}
}
