-- AUTO TEST: right-clicking on the player's OWN Supply Route must resolve to
-- the DeliverCash (Evacuate) order, not AttackSupplyRoute. The pre-fix
-- AttacksSupplyRoutes targeter caught own-owner clicks at priority 8 and
-- returned the AllyCursor, beating DeliversCash (priority 5).
--
-- Test.GetTargetOrder walks the same IIssueOrder pipeline as the UI cursor
-- resolver, so this catches order-priority and CanTargetActor regressions
-- that direct unit.Attack/Move calls bypass.

local DeadlineSeconds = 2

WorldLoaded = function()
	TestHarness.FocusBetween(Tank, OwnSR)
	TestHarness.Select(Tank)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local resolved = Test.GetTargetOrder(Tank, OwnSR)
		if resolved == "DeliverCash" then
			return true
		end
		if resolved == nil then
			return "fail: no order matched (expected DeliverCash)"
		end
		return "fail: resolved to '" .. tostring(resolved) ..
			"' (expected DeliverCash; AttackSupplyRoute is the regression)"
	end, "Test.GetTargetOrder did not return DeliverCash within " .. DeadlineSeconds .. "s")
end
