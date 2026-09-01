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

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// Detects a follow that has stopped making ground, by the only evidence available: the follower has
	/// not changed cell for long enough.
	/// </summary>
	/// <remarks>
	/// <para><c>Mobile.MoveResult</c> is never assigned, so a move that cannot path reports InProgress
	/// forever instead of failing. Nothing anywhere reports a follow that can never arrive, and every
	/// caller that wants to break out of one has to infer it from the follower's cell.</para>
	/// <para>Shared rather than copied. The predicate has three separate traps in it — a legitimate halt
	/// at the destination must not read as a stall, one cell of progress must clear the whole
	/// accumulator, and the accumulator must reset when it fires or the caller re-fires every tick — and
	/// two copies of it would drift apart on the first one somebody edited.</para>
	/// <para>Keyed on <see cref="CPos"/> rather than on the follower so the rule can be asserted without a
	/// World. Deciding whether the TARGET changed stays with the caller: that is one reference
	/// comparison, and callers disagree about what a new target even is.</para>
	/// </remarks>
	public sealed class StallWatcher
	{
		CPos lastCell;
		int stalledTicks;

		/// <summary>Whether the last <see cref="IsStalled"/> call saw the follower on a new cell — i.e. it
		/// is making ground again. A caller that gave up on a stall reads this to know it may resume.</summary>
		public bool MovedOnLastCheck { get; private set; } = true;

		/// <summary>The follow is doing what it should and this tick is not evidence of a stall. Standing
		/// still AT the destination is the case that matters: it is the correct state, and without this
		/// the watcher would bench the very target the follower is successfully escorting.</summary>
		public void MarkProgress(CPos cell)
		{
			lastCell = cell;
			stalledTicks = 0;
			MovedOnLastCheck = true;
		}

		/// <summary>Returns true on the tick the follow is judged stalled, and not again until it has
		/// stalled afresh — the accumulator resets on the way out, so a caller ticking every frame gets
		/// one edge rather than a stream.</summary>
		/// <param name="cell">The follower's current cell. Unchanged from the previous call is the only
		/// evidence of a stall this class has.</param>
		/// <param name="elapsedTicks">Ticks since the previous call. 1 for a per-tick caller, the check
		/// interval for one that samples.</param>
		public bool IsStalled(CPos cell, int elapsedTicks, int maxStalledTicks)
		{
			MovedOnLastCheck = cell != lastCell;
			if (MovedOnLastCheck)
			{
				lastCell = cell;
				stalledTicks = 0;
				return false;
			}

			if ((stalledTicks += elapsedTicks) < maxStalledTicks)
				return false;

			stalledTicks = 0;
			return true;
		}
	}
}
