-- AUTO TEST: "resupply, then attack-move" must survive being issued to a dry unit.
--
-- WHY THE AUTO-ARM EVACUATION FALLBACK CANNOT BREAK THIS, and why the answer is ROBUST rather
-- than lucky. Asked and settled statically on 2026-08-27, when AmmoPool's Auto arm gained a
-- disposition that cancels the running activity AND everything queued behind it
-- (EvacuateForRefund -> QueueActivity(false, ...) -> Activity.Cancel, which nulls NextActivity).
-- That is precisely an order-eater, so "does it eat THIS test's queued order?" is the obvious
-- worry. It does not, for a reason that has nothing to do with the affordability pick or any
-- other detail of that change:
--
--   * GEOMETRY MAKES Evacuate UNREACHABLE HERE. Hunter is at 12,16 and the truck at 8,16 -- four
--     cells apart, against an unoverridden AmmoPoolInfo.DryRearmLeashCells of 30. So
--     AnyRearmHostWithinLeash is TRUE and the evacuation conjunction (wholly dry AND nothing
--     inside the leash AND nothing able to travel to us) is structurally false. The truck being
--     MOBILE is a second, independent guard on the same conjunction.
--   * THE OTHER TWO DISPOSITIONS CANNOT EAT A QUEUED ORDER EITHER. HoldAndFlag queues nothing and
--     cancels nothing. SeekRearm does cancel -- but it fires at t=0, when the man first falls
--     idle dry, and the attack-move is not issued until t=75; after that there is no re-entry,
--     because AmmoPool is not ITick and INotifyBecomingIdle fires only on the transition INTO
--     idle (Actor.cs:317-323).
--
-- The durable point for whoever touches the Auto arm next: this scenario's safety rests on a
-- rearm host EXISTING WITHIN THE LEASH, not on which host the arm picks or how it judges
-- affordability -- so it is insensitive to that entire class of change. What WOULD put it at
-- risk is moving the truck outside the leash, making it immobile, or widening the evacuation
-- conjunction so that a reachable host no longer suppresses it.
--
-- Refusing attack orders on an empty unit is right for an order that executes NOW, but
-- asking about ammo at ISSUE time is the wrong question for a QUEUED order: going to a
-- supply source and then attacking is the correct play with a dry unit, and refusing the
-- queued half punishes exactly the player who got it right. So the refusal is scoped to
-- unqueued orders and the queued one is left for AttackMoveActivity's own guard to rule on
-- when it actually comes up — still dry then, it ends at once; rearmed, it runs.
--
-- Sequence: Hunter starts with an empty magazine, so AutoSeekSupplies sends him to the
-- truck on its own. Three seconds in — errand under way, man still empty — a queued
-- attack-move is issued at him. He must rearm and then carry it out.
--
-- Test.IssueAttackMove, not Hunter.AttackMove: the Lua actor API constructs
-- AttackMoveActivity directly and never touches AttackMove.ResolveOrder, so it cannot see
-- the order layer this test exists to check. Test.IssueAttackMove issues a real order.
--
-- The assertion line is EAST and the truck is WEST, so no part of the resupply errand can
-- satisfy it — neither SeekSupplyProvider (ends at the truck) nor SeekSuppliesAndReturn
-- (ends back at the start cell). Only the attack-move goes east.

local DeadlineSeconds = 60
local QueueAfterTicks = 75 -- 3s: the resupply errand has latched, the man is still dry
local AssertLineX = 22 -- start is x=12, truck is x=8, attack-move destination is x=28

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Supply)
	TestHarness.Select(Hunter)

	Trigger.AfterDelay(QueueAfterTicks, function()
		if not Hunter.IsDead then
			Test.IssueAttackMove(Hunter, CPos.New(28, 16), true)
		end
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died first" end

		-- Rearmed AND east of the line. The ammo half is not redundant: it is what makes
		-- "he went east" mean "he rearmed first and then carried out the order".
		if Hunter.AmmoCount("primary-ammo") <= 0 then return false end

		return Hunter.Location.X >= AssertLineX
	end, "Queued attack-move was lost: the Hunter never carried it out after rearming")
end
