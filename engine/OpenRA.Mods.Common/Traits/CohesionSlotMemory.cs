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
		"sprint back to a stale slot.",
		"The walk-back is BOT-ONLY as shipped: see ReturnToSlotForHumanOwners, which the mod sets",
		"false. The record itself is still kept for every owner because the order line and Group",
		"Scatter read it.")]
	public class CohesionSlotMemoryInfo : TraitInfo, Requires<MobileInfo>
	{
		[Desc("How many ticks since the last Assign() before the slot is treated as stale and the",
			"return-to-slot behavior switches off. At 16.67 tps, 750 ticks = 45s. (These two Descs",
			"previously said 25 ticks = 1s and 750 = 30s, the 1.5x tick-rate error catalogued in",
			"DOCS/reference/conventions.md; the VALUES are unchanged, only the seconds were wrong.)")]
		public readonly int ForgetAfterTicks = 750;

		[Desc("Cooldown in ticks between successive return-to-slot attempts, so a unit doesn't",
			"thrash if blocked repeatedly. At 16.67 tps, 25 ticks = 1.5s.")]
		public readonly int ReturnCooldownTicks = 25;

		[Desc("Whether a unit owned by a HUMAN player walks back to its formation slot. Engine default",
			"true preserves the historical behaviour; the mod sets it false on ^Combatant, because a",
			"player who parks a unit somewhere expects it to stay there, and a unit re-walking to a",
			"slot from an order given up to ForgetAfterTicks ago reads as moving on its own.",
			"BOT-OWNED UNITS ARE UNAFFECTED WHATEVER THIS IS SET TO: the guard in TryReturnToSlot is",
			"predicated on `Owner.Playable && !Owner.IsBot`, so no bot profile — @stable, @normal or",
			"@experimental — can reach it. Only the return MOVE is gated; the slot record itself keeps",
			"updating, so order lines (DrawLineToTarget) and Group Scatter are unchanged.")]
		public readonly bool ReturnToSlotForHumanOwners = true;

		public override object Create(ActorInitializer init) { return new CohesionSlotMemory(init.Self, this); }
	}

	// ISync is load-bearing here, not decoration: Actor.cs:206 hashes a trait only when
	// `trait is ISync`, so without it the [Sync] attributes below were inert and this trait's state —
	// which decides whether TryReturnToSlot queues a real Move — was absent from every sync report.
	// An unhashed trait that moves units turns a divergence into an effect with no visible cause.
	public class CohesionSlotMemory : INotifyBlockingMove, INotifyIdle, ISync
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

		// PIPELINE-6 idea #2 (settle facing + sector fan). The common formation front plus this unit's
		// H(ActorID) micro-fan, computed by CohesionMoveModifier and handed down on Assign. On arrival at
		// the slot the unit turns to it once (settleFacingDone latches the one-shot). Deliberately NOT
		// [Sync] — like assignedOrderPoint it is a pure deterministic function of already-synced state
		// (ActorID + integer positions) and only ever gates a Turn activity (whose facing IS synced), so
		// it can never itself desync or alter the sync hash. Null on the human/AI seam's bot & Tight side
		// (no facing passed) → no Turn is ever queued → bot behaviour is byte-identical.
		WAngle settleFacing;
		bool hasSettleFacing;
		bool settleFacingDone;

		// Render/UI-only, non-[Sync]: (slot, orderPoint) for every grouped Move/AttackMove of the
		// CURRENT batch — a run of orders where the first is fresh (non-queued) and the rest are
		// shift-queued onto it. Group Scatter reads this to map each queued formation-slot cell back
		// to the human ORDER POINT it was spread around, so it redistributes the main points the
		// player clicked rather than near-identical slot cells. Never read by the sim.
		readonly List<(CPos Slot, CPos OrderPoint)> batch = new();

		// Upper bound on retained batch entries (see Assign). A human queued chain is well under this.
		const int MaxBatchEntries = 16;

		// Both gate whether TryReturnToSlot queues a Move — lastAssignTick through the ForgetAfterTicks
		// expiry (which also clears hasSlot), lastReturnTick through the cooldown. They are as
		// load-bearing as assignedSlot and carried no annotation at all, so nobody had even flagged
		// them as missing from the hash.
		[Sync]
		int lastAssignTick;

		[Sync]
		int lastReturnTick;

		public CohesionSlotMemory(Actor self, CohesionSlotMemoryInfo info)
		{
			this.info = info;
			mobile = self.Trait<Mobile>();
		}

		public void Assign(CPos slot, CPos orderPoint, int tick, bool queued, WAngle? settle = null)
		{
			assignedSlot = slot;
			hasSlot = true;
			assignedOrderPoint = orderPoint;
			hasOrderPoint = true;
			lastAssignTick = tick;

			// Re-arm the one-shot settle turn. A null facing (bots / Tight / non-cohesion callers) leaves
			// hasSettleFacing false, so TickIdle never queues a Turn — behaviour is byte-identical there.
			hasSettleFacing = settle.HasValue;
			if (hasSettleFacing)
				settleFacing = settle.Value;
			settleFacingDone = false;

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
			hasSettleFacing = false;
			settleFacingDone = false;
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
			// Already settled on the slot: turn to the formation front once (idea #2), don't re-path.
			if (hasSlot && self.Location == assignedSlot)
			{
				TrySettleFacing(self);
				return;
			}

			TryReturnToSlot(self);
		}

		// One-shot turn-to-front on arrival. Only fires from the idle path (a settled, idle unit), never
		// from a blocking-move, so it can't fight movement. Turn is interruptible and AutoTarget cancels
		// the current activity to shoot, so a pending settle-turn never delays a shot (ideas doc #2).
		// ACCEPTED LIMITATION: the latch (settleFacingDone) is only re-armed by a fresh Assign, not by a
		// return-to-slot. So if a unit is nudged off its slot and walks back via TryReturnToSlot, it does
		// NOT re-face the front on re-arrival — it keeps whatever heading the return Move left. Cosmetic
		// only (the common case, arrival straight off the order, faces correctly); left as-is by design.
		void TrySettleFacing(Actor self)
		{
			if (!hasSettleFacing || settleFacingDone)
				return;

			// Don't turn to a stale front — expire on the same horizon as return-to-slot.
			if (self.World.WorldTick - lastAssignTick > info.ForgetAfterTicks)
				return;

			var facing = self.TraitOrDefault<IFacing>();
			if (facing == null)
				return;

			// Latch before queuing so we never re-queue every idle tick.
			settleFacingDone = true;

			if (facing.Facing != settleFacing)
				self.QueueActivity(new Turn(self, settleFacing));
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

			// THE ONLY GATE for human-owned return-to-slot. Both entry points — INotifyIdle.TickIdle and
			// INotifyBlockingMove — funnel through this method, so one guard covers both.
			// DELIBERATELY THE LAST CHECK, not the first. Everything above it still runs for a human
			// owner, which is the point: the ForgetAfterTicks branch above must keep clearing hasSlot on
			// schedule, because DrawLineToTarget.cs:181 draws the player's order line from that record
			// and GroupScatterHotkeyLogic.cs:240 maps slot cells back to the points the player actually
			// clicked. Gating earlier would leave a human's slot alive forever and quietly change what
			// both of those read; gating here suppresses the Move and nothing else. (Making the trait a
			// ConditionalTrait would have taken both consumers away outright.)
			// Owner is read LIVE rather than latched at construction, so a unit changing hands mid-match
			// gets the right behaviour with no owner-change notification: a bot that captures a human's
			// unit starts returning to slot, and vice versa. Predicate mirrors
			// GrantConditionOnHumanOwner.cs:43 exactly, so scenario garrisons (Playable: False) count as
			// non-human and keep the behaviour they have always had.
			if (!info.ReturnToSlotForHumanOwners && self.Owner.Playable && !self.Owner.IsBot)
				return;

			lastReturnTick = tick;
			self.QueueActivity(new Move(self, assignedSlot));
		}
	}
}
