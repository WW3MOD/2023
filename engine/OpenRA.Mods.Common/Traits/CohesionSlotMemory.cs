#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Records the cell this unit was assigned by CohesionMoveModifier on the last grouped",
		"Move/AttackMove. When the unit is nudged out of position by a passing unit, it walks",
		"back to the slot — keeping the squad's defensive line intact instead of letting one",
		"unit drift into open ground after a single bump. The memory expires after",
		"ForgetAfterTicks so a unit that's been doing something else for a while doesn't suddenly",
		"sprint back to a stale slot.")]
	public class CohesionSlotMemoryInfo : TraitInfo, Requires<MobileInfo>
	{
		[Desc("How many ticks since the last Assign() before the slot is treated as stale and the",
			"return-to-slot behavior switches off. 25 ticks = 1s; 750 = 30s.")]
		public readonly int ForgetAfterTicks = 750;

		[Desc("Cooldown in ticks between successive return-to-slot attempts, so a unit doesn't",
			"thrash if blocked repeatedly. 25 = 1s.")]
		public readonly int ReturnCooldownTicks = 25;

		public override object Create(ActorInitializer init) { return new CohesionSlotMemory(init.Self, this); }
	}

	public class CohesionSlotMemory : INotifyBlockingMove, INotifyIdle
	{
		readonly CohesionSlotMemoryInfo info;
		readonly Mobile mobile;

		[Sync]
		CPos assignedSlot;

		[Sync]
		bool hasSlot;

		// Render/UI-only: the player-commanded order point (click cell) this slot was spread around,
		// as opposed to assignedSlot (the per-unit destination). Deliberately NOT [Sync] — it is
		// never read by any sim decision (only by DrawLineToTarget for the primary order line and by
		// GroupScatterHotkeyLogic when issuing new orders), so it can never affect game state,
		// replays, or the sync hash.
		CPos assignedOrderPoint;
		bool hasOrderPoint;

		// Render/UI-only, non-[Sync]: (slot, orderPoint) for every grouped Move/AttackMove of the
		// CURRENT batch — a run of orders where the first is fresh (non-queued) and the rest are
		// shift-queued onto it. Group Scatter reads this to map each queued formation-slot cell back
		// to the human ORDER POINT it was spread around, so it redistributes the main points the
		// player clicked rather than near-identical slot cells. Never read by the sim.
		readonly List<(CPos Slot, CPos OrderPoint)> batch = new();

		// Upper bound on retained batch entries (see Assign). A human queued chain is well under this.
		const int MaxBatchEntries = 16;

		int lastAssignTick;
		int lastReturnTick;

		public CohesionSlotMemory(Actor self, CohesionSlotMemoryInfo info)
		{
			this.info = info;
			mobile = self.Trait<Mobile>();
		}

		public void Assign(CPos slot, CPos orderPoint, int tick, bool queued)
		{
			assignedSlot = slot;
			hasSlot = true;
			assignedOrderPoint = orderPoint;
			hasOrderPoint = true;
			lastAssignTick = tick;

			// A fresh (non-queued) order starts a new batch; shift-queued orders extend it.
			if (!queued)
				batch.Clear();

			batch.Add((slot, orderPoint));

			// Bound the batch: Group Scatter issues a Stop (which does NOT clear this) followed by
			// queued Moves, so repeated re-tasking without a fresh non-queued click would otherwise
			// grow it unbounded. Keep only the newest MaxBatchEntries; TryGetOrderPointForSlot scans
			// newest-first, so dropping the oldest is safe.
			if (batch.Count > MaxBatchEntries)
				batch.RemoveAt(0);
		}

		public CPos? AssignedSlot => hasSlot ? (CPos?)assignedSlot : null;

		// The order point (click cell) the last grouped Move/AttackMove spread this unit around.
		// Render/UI-only; see assignedOrderPoint.
		public CPos? OrderPoint => hasSlot && hasOrderPoint ? (CPos?)assignedOrderPoint : null;

		// Map a formation-slot cell back to the human order point it was spread around, for the
		// current batch. Returns false when this unit has no cohesion record for that cell (e.g. a
		// non-cohesion unit, or a stale/other-order cell) so the caller keeps the raw cell. Scans
		// newest-first so the most recent assignment for a reused cell wins. Render/UI-only.
		public bool TryGetOrderPointForSlot(CPos slot, out CPos orderPoint)
		{
			for (var i = batch.Count - 1; i >= 0; i--)
			{
				if (batch[i].Slot == slot)
				{
					orderPoint = batch[i].OrderPoint;
					return true;
				}
			}

			orderPoint = default;
			return false;
		}

		// Drop the remembered slot so return-to-slot stops. Used by the Phase-2 positioning
		// executor on abort/disengage so a released unit reverts to whatever the next grouped
		// order assigns, rather than drifting back to the executor's cover cell (B2).
		public void Clear()
		{
			hasSlot = false;
			hasOrderPoint = false;
			batch.Clear();
		}

		// PITFALL: this fires when ANOTHER actor wants to push through us — the Mobile trait's
		// own INotifyBlockingMove implementation queues a Nudge(blocking) activity for us
		// (Mobile.cs ~line 929). We piggyback on the same notification to queue a Move BACK to
		// the assigned slot once the Nudge completes. Multiple INotifyBlockingMove handlers
		// fire in arbitrary order, but activity queue order is deterministic: Mobile queues
		// Nudge first, we queue Move second, so the unit nudges aside and then returns.
		void INotifyBlockingMove.OnNotifyBlockingMove(Actor self, Actor blocking)
		{
			TryReturnToSlot(self);
		}

		void INotifyIdle.TickIdle(Actor self)
		{
			TryReturnToSlot(self);
		}

		void TryReturnToSlot(Actor self)
		{
			if (!hasSlot)
				return;

			if (self.Location == assignedSlot)
				return;

			var tick = self.World.WorldTick;

			if (tick - lastAssignTick > info.ForgetAfterTicks)
			{
				hasSlot = false;
				hasOrderPoint = false;
				batch.Clear();
				return;
			}

			if (tick - lastReturnTick < info.ReturnCooldownTicks)
				return;

			if (!mobile.CanEnterCell(assignedSlot))
				return;

			lastReturnTick = tick;
			self.QueueActivity(new Move(self, assignedSlot));
		}
	}
}
