-- WW3MOD developer test harness — shared Lua helpers.
-- Loaded by test rules.yaml via `LuaScript: Scripts: test-helpers.lua, <test>.lua`.
-- Idle when the harness isn't active; safe to leave referenced from regular maps.

TestHarness = {}

-- Second→tick conversion for AssertWithin and friends.
--
-- THIS IS NOT THE GAME'S TICK RATE, and the name has misled readers. Single-test runs play at the
-- mod's "default" GameSpeed — Game.LoadMap hardcodes "default" and run-test.sh never passes
-- Test.GameSpeed — whose Timestep is 60 ms (mod.yaml). The engine's own Lua converter derives
-- 1000 / 60 = 16 ticks/second by INTEGER division (DateTimeGlobal.cs:31). So one "second" handed to
-- AssertWithin is 25 ticks where DateTime.Seconds(1) is 16: harness deadlines are ~1.56x longer
-- than they read, always in the lenient direction.
--
-- IT IS DELIBERATELY LEFT AT 25. 91 deadlines across 137 scenarios were authored and accepted
-- against this value, several knowingly (test-tunguska-missile-standoff:25 "Left alone
-- deliberately"; test-depot-vacate-phantom:32 "Generous on purpose"), and correcting it shortens
-- all of them by a third in one edit that cannot be validated without running the whole suite.
-- Two scenarios provably stop passing the moment it moves; both are pinned by
-- engine/OpenRA.Test/OpenRA.Mods.Common/AutotestTickRateTest.cs, which fails at `dotnet test`
-- rather than silently in a game nobody reran. Change this number only together with those.
--
-- WRITING A NEW SCENARIO: budget in TICKS and convert with `ticks / TestHarness.TicksPerSecond`,
-- as the medic scenarios do. That round-trips exactly and is immune to whatever this value is.
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
--   * `timeoutReason` may be a STRING or a FUNCTION returning one. The function form is
--     evaluated once, at the moment of timeout, so the note can report end-of-run state
--     (position, activity chain, counters) that no string built at setup time could carry.
--     This matters more than it sounds: a verdict saying only "the unit never went idle" is
--     compatible with opposite root causes, and diagnosing that by reading code instead has
--     already produced one published wrong answer (WORKSPACE/bugs/discovered.md 2026-09-01).
--     Every pre-existing caller passes a string and is unaffected.
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
			local reason = timeoutReason
			if type(reason) == "function" then reason = reason() end
			Test.Fail(reason or ("AssertWithin timed out after " .. seconds .. "s"))
			return
		end
		Trigger.AfterDelay(1, check)
	end
	Trigger.AfterDelay(1, check)
end

-- Does `actor` still hold an ATTACK activity anywhere in its queue?
--
-- USE THIS INSTEAD OF `not actor.IsIdle` WHEN THE QUESTION IS "did the unit drop its attack
-- order". The two are not the same and the difference has cost real time:
-- `Actor.IsIdle` is `CurrentActivity == null`, and Actor.Tick re-runs the queue in the SAME
-- tick immediately after raising INotifyBecomingIdle (Actor.cs:322-325, deliberately, "to
-- avoid an 'empty' null tick"). So if ANY handler queues on that edge -- AmmoPool's resupply
-- disposition does, Aircraft always does -- the unit is never observed idle even though its
-- order genuinely ended. Measured 2026-09-01: two dry-unit scenarios reported `idleTicks=0`
-- while their activity chains showed the attack activity had been replaced by RotateToEdge.
-- Asserting on idleness there tested the resupply layer; asserting on this tests the guard.
--
-- HEURISTIC, stated plainly: this is a TYPE-NAME prefix test. Attack activities share no
-- interface or base class to query (Activities.Attack and AttackFollow.AttackActivity both
-- derive straight from Activity), so there is nothing more precise to ask. It matches any
-- queue component whose type name starts with "Attack" -- today Attack, AttackActivity and
-- AttackMoveActivity, all three of which ARE attack orders for this purpose. If someone adds
-- an unrelated activity named Attack*, this widens silently; that is the known cost.
function TestHarness.HoldsAttackActivity(actor)
	local chain = Test.ActivityChain(actor)
	if chain == "" or chain == "(idle)" then
		return false
	end

	-- Test.ActivityChain separates parent>child with ">" and queued entries with " | ".
	-- Normalise to one separator and prepend it, so every component is preceded by ">" and a
	-- single plain (non-pattern) search finds any component starting with "Attack".
	local normalised = ">" .. chain:gsub(" | ", ">")
	return normalised:find(">Attack", 1, true) ~= nil
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
