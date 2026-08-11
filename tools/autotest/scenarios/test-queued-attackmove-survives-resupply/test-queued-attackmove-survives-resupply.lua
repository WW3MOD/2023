-- AUTO TEST: "resupply, then attack-move" must survive being issued to a dry unit.
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
