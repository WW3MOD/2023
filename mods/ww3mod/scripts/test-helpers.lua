-- WW3MOD developer test harness — shared Lua helpers.
-- Loaded by test rules.yaml via `LuaScript: Scripts: test-helpers.lua, <test>.lua`.
-- Idle when the harness isn't active; safe to leave referenced from regular maps.

TestHarness = {}

-- 25 ticks/sec at default game speed. Used for second→tick conversion in
-- AssertWithin and friends. Lifted into a helper so tests don't hardcode it.
TestHarness.TicksPerSecond = 25

-- Center the camera on the geometric midpoint of the given actors.
-- Usage: TestHarness.FocusBetween(Paladin, Target)
--        TestHarness.FocusBetween(actorA, actorB, actorC)
function TestHarness.FocusBetween(...)
	local actors = { ... }
	local sumX, sumY, count = 0, 0, 0
	for _, a in ipairs(actors) do
		if a and not a.IsDead then
			local pos = a.CenterPosition
			sumX = sumX + pos.X
			sumY = sumY + pos.Y
			count = count + 1
		end
	end
	if count > 0 then
		Camera.Position = WPos.New(math.floor(sumX / count), math.floor(sumY / count), 0)
	end
end

-- Pre-select the unit-under-test so the player doesn't have to click first.
-- Usage: TestHarness.Select(Paladin)
function TestHarness.Select(actor)
	if actor and not actor.IsDead then
		UserInterface.Select(actor)
	end
end

-- Poll a predicate every tick until it returns true (Pass) or `seconds`
-- elapse (Fail with the timeout reason). The predicate runs synchronously
-- on the simulation thread — keep it side-effect-free.
--
-- Usage:
--     TestHarness.AssertWithin(8, function() return Paladin.IsFiring end,
--         "Paladin did not fire within 8 seconds")
--
-- Notes:
--   * Predicate may return `false` to keep waiting, `true` to Pass, or the
--     string "fail: <reason>" to Fail immediately with that reason.
--   * If the harness isn't active (TestMode off), the polling still runs
--     but the eventual Pass/Fail are no-ops, so this is safe in regular maps.
function TestHarness.AssertWithin(seconds, predicate, timeoutReason)
	local timeoutTicks = math.floor(seconds * TestHarness.TicksPerSecond)
	local elapsed = 0
	local check
	check = function()
		local result = predicate()
		if result == true then
			Test.Pass()
			return
		end
		if type(result) == "string" then
			Test.Fail(result)
			return
		end
		elapsed = elapsed + 1
		if elapsed >= timeoutTicks then
			Test.Fail(timeoutReason or ("AssertWithin timed out after " .. seconds .. "s"))
			return
		end
		Trigger.AfterDelay(1, check)
	end
	Trigger.AfterDelay(1, check)
end

-- Sugar for "assert this is true after `seconds` have elapsed".
-- Useful when you want to give a system time to settle before checking.
--
-- Usage:
--     TestHarness.AssertAfter(3, function() return Tank.IsDead end,
--         "Tank still alive 3s in")
function TestHarness.AssertAfter(seconds, predicate, failReason)
	local ticks = math.floor(seconds * TestHarness.TicksPerSecond)
	Trigger.AfterDelay(ticks, function()
		if predicate() then
			Test.Pass()
		else
			Test.Fail(failReason or ("Assertion false after " .. seconds .. "s"))
		end
	end)
end
