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

-- Chebyshev cell distance. Both axes, because a platoon shoved sideways off its position
-- has left it just as surely as one that walked west. Pure, and separately pinned by
-- LuaDriftTrackerTest so the two supply scenarios cannot drift apart on the metric itself.
function TestHarness.CellDrift(fromX, fromY, toX, toY)
	return math.max(math.abs(toX - fromX), math.abs(toY - fromY))
end

-- Track how far each of `actors` strays from where it started, keeping the WORST value seen
-- per actor across the whole run.
--
-- WHY PEAK AND NOT FINAL POSITION — this is the trap, and a final-position check walks
-- straight into it. SeekSuppliesAndReturn walks the soldier BACK to where it was standing
-- (`origin`, settling for `HomeNearEnough = 2` cells), so the excursion is TRANSIENT: sample
-- at the end and the platoon is home, fed, and the abandonment is invisible. The drift that
-- matters is the worst seen over the run, not the drift at verdict time.
--
-- Shared rather than copied: this exact descent existed once per supply scenario and the
-- standing rule in this repo is that three copies of one grid computation diverged, two of
-- them wrong. Callers keep their OWN allowance — how many cells are forgivable is doctrine
-- and differs per scenario — but the measurement is one implementation.
--
-- Usage:
--     local drift = TestHarness.DriftTracker(platoon)
--     -- once per poll:  drift.Sample()
--     -- at verdict:     drift.Peak(), drift.Trace()
function TestHarness.DriftTracker(actors)
	local spawnX = {}
	local spawnY = {}
	local worst = {}

	-- X/Y copied out as numbers at construction rather than holding the CPos: a cell reaches
	-- Lua as a bound object and this must not depend on whether that object keeps value
	-- semantics for the life of the run.
	for i, a in ipairs(actors) do
		spawnX[i] = a.Location.X
		spawnY[i] = a.Location.Y
		worst[i] = 0
	end

	local tracker = {}

	-- Dead actors stop contributing but KEEP the peak they already reached: a man shot while
	-- out of position still left it, and the run should say so.
	function tracker.Sample()
		for i, a in ipairs(actors) do
			if not a.IsDead then
				local d = TestHarness.CellDrift(spawnX[i], spawnY[i], a.Location.X, a.Location.Y)
				if d > worst[i] then worst[i] = d end
			end
		end
	end

	function tracker.Peak()
		local m = 0
		for _, d in ipairs(worst) do
			if d > m then m = d end
		end
		return m
	end

	-- "spawnX->nowX(worst)" per actor — says at a glance whether they held, walked out, or
	-- walked out and came home again (the SeekSuppliesAndReturn signature: worst is large
	-- while nowX is back at spawnX).
	function tracker.Trace()
		local parts = {}
		for i, a in ipairs(actors) do
			if a.IsDead then
				parts[i] = "dead"
			else
				parts[i] = string.format("%d->%d(%d)", spawnX[i], a.Location.X, worst[i])
			end
		end
		return table.concat(parts, " ")
	end

	return tracker
end

-- Capture a screenshot tagged with `label`. Thin wrapper around the Test.Screenshot
-- engine binding; included here so test code calls a consistent TestHarness.* API.
-- Optional `note` is a semantic expectation surfaced in the verdict JSON, e.g.
-- "expects: muzzle flash visible, T-90 in frame".
--
-- Capture is async — the PNG appears on disk a moment after this returns. Path is
-- recorded immediately in the verdict's screenshots[] array, so the runner can
-- print it post-exit. No-op when TestMode is inactive (safe in regular maps).
function TestHarness.Screenshot(label, note)
	return Test.Screenshot(label, note or "")
end

-- Sugar: schedule a screenshot `seconds` from now. Useful for capturing a moment
-- mid-test ("3 seconds after Paladin starts moving, screenshot to see where it
-- got to") without manually composing Trigger.AfterDelay.
function TestHarness.ScreenshotAfter(seconds, label, note)
	local ticks = math.floor(seconds * TestHarness.TicksPerSecond)
	Trigger.AfterDelay(ticks, function()
		Test.Screenshot(label, note or "")
	end)
end
