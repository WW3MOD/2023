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

		int lastAssignTick;
		int lastReturnTick;

		public CohesionSlotMemory(Actor self, CohesionSlotMemoryInfo info)
		{
			this.info = info;
			mobile = self.Trait<Mobile>();
		}

		public void Assign(CPos slot, int tick)
		{
			assignedSlot = slot;
			hasSlot = true;
			lastAssignTick = tick;
		}

		public CPos? AssignedSlot => hasSlot ? (CPos?)assignedSlot : null;

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
