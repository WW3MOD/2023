-- AUTO TEST: a click on the rally point with no modifier resolves to Move,
-- Alt resolves to AttackMove, and Ctrl resolves to ForceMove. Walks the same
-- IIssueOrder.IssueOrder pipeline used by the UI, then decodes the resulting
-- ExtraData — so this catches modifier-detection AND encoding regressions.

WorldLoaded = function()
	TestHarness.FocusBetween(OwnSR)

	local cell = CPos.New(40, 16)

	local function expect(label, modifiers, expected)
		local actual = Test.GetRallyOrderTypeForClick(OwnSR, cell, modifiers)
		if actual ~= expected then
			Test.Fail(label .. ": expected '" .. expected .. "' but got '" .. tostring(actual) .. "'")
			return false
		end
		return true
	end

	if not expect("default click", "", "Move") then return end
	if not expect("Alt-click", "Alt", "AttackMove") then return end
	if not expect("Ctrl-click", "Ctrl", "ForceMove") then return end
	if not expect("Shift+default", "Shift", "Move") then return end
	if not expect("Shift+Alt", "Shift Alt", "AttackMove") then return end

	Test.Pass()
end
