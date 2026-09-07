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
-- The two scenarios that used to be casualties of moving it (test-critical-no-panic,
-- test-autotarget-preempt-air) were re-authored on 2026-09-02 and are now immune; the fixture
-- engine/OpenRA.Mods.Common/AutotestTickRateTest.cs proves that at BOTH rates and still fails at
-- `dotnet test` if this number moves, because the REST of the suite has not been audited.
--
-- Note what that audit has to look for. Only one of those two actually went red at 16. The other
-- kept passing while an INNER deadline it contains became unreachable — a scenario that silently
-- stopped enforcing its own budget. Shortening every deadline by a third produces some red runs and
-- some greens that have quietly stopped measuring, and the second kind is the one to hunt.
--
-- WRITING A NEW SCENARIO: budget in TICKS and convert with `ticks / TestHarness.TicksPerSecond`,
-- as the medic scenarios do. That is immune to whatever this value is — but it does NOT always
-- round-trip exactly, contrary to what this comment claimed until 2026-09-02: AssertWithin recovers
-- the budget with math.floor, and 1145 of the first 20000 integers come back one tick short at 25,
-- 16 or both (402 -> 401; also 29, 57, 113-116, 201, 203, 205). Multiples of 25 are always safe.
-- Do not spend that tick twice by trimming a deadline to its measured margin as well.
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

-- ============================================================================================
-- BUYING A SUPPORT POWER — the ONLY way a scenario can put a shot in the magazine.
-- ============================================================================================
--
-- READ THIS BEFORE WRITING `ChargeInterval` IN A SCENARIO'S rules.yaml. Every support power that
-- ships in WW3MOD carries `RequiresPurchase: True`, and under that flag SupportPowerInstance's
-- constructor forces `TotalTicks = 0` and `remainingSubTicks = 0` unconditionally
-- (SupportPowerManager.cs:228-229). So a scenario override of ChargeInterval or StartFullyCharged
-- IS INERT — it is read, it is discarded, and nothing says so. What actually gates the power is
-- the BANK:
--
--     Ready    => Active && RemainingTicks == 0          (SupportPowerManager.cs:183)
--     Active   =  !Disabled && any unpaused instance     (SupportPowerManager.cs:250)
--     Disabled => !bank.IconVisible(Permitted)           (SupportPowerManager.cs:172)
--     IconVisible(p) => p && !(Enabled && Charges == 0)  (SupportPowerChargeBank.cs)
--
-- At zero charges that chain collapses to Ready == false, so Test.ActivateSupportPower returns
-- 'not-ready:0' forever and GetSupportPowerState reads 'hidden'. `GrantCharge` is called from
-- exactly ONE place in the engine — SupportPowerProductionQueue.BuildUnit — so there is no way to
-- bank a shot except to buy one. That is what this helper does.
--
-- WHAT A SCENARIO NEEDS FOR THIS TO WORK, all four, and missing any one of them is silent:
--   1. cash. `PlayerResources: DefaultCash:` >= the proxy's Valued Cost, on the buying player.
--   2. a producer. An actor the player owns whose Production trait lists `Powers` — in this mod
--      that is the Supply Route, and every scenario that fires a power already places one.
--   3. the TIER prerequisite. `powers.america` / `powers.russia` come from the player's faction;
--      `powers.event` comes from NO faction and only from the sandbox lobby option, so a scenario
--      firing an event-tier warhead must set `PowersLobbyOptions: PowersSandboxCheckboxEnabled`
--      and `PowersSandboxCheckboxLocked` to true in its rules.yaml.
--   4. a SHORT `BuildDuration` override on the proxy. The shipped loads run 500 to 4500 ticks
--      (30 s to 4.5 min) and a scenario that waited them out would spend a launch slot watching a
--      progress bar.
--
-- ONE PURCHASE IS ONE SHOT. SupportPowerInstance.Activate calls bank.Consume(), so a scenario that
-- fires the same power twice must buy it twice. Calling EnsurePower again after a shot does
-- exactly that with no extra bookkeeping, because the state reverts to 'hidden'.

-- Ticks to let pass before touching ANY production property. LOAD-BEARING, AND THE FAILURE IT
-- PREVENTS IS PERMANENT RATHER THAN TRANSIENT: ClassicProductionQueueProperties snapshots the
-- player's queues ONCE, in its constructor, filtered on `q.Enabled`
-- (ProductionProperties.cs:225-227), and that constructor runs on the FIRST access to Build or
-- IsProducing. ProductionQueue.Enabled is not set until the queue's first Tick finds a producer
-- (ClassicProductionQueue.cs:51-72), so a first touch on tick 1 captures an EMPTY map and the
-- Powers queue is missing for the rest of the run — at which point IsProducing returns true for an
-- unknown queue (ProductionProperties.cs:301) and this helper would report 'loading' forever.
-- 5 is the value test-power-buy-loop has proven; do not lower it, and do not reach for a
-- production property from WorldLoaded.
TestHarness.ProductionWarmupTicks = 5

-- Make sure `powerKey` has a shot in the magazine, buying `proxyType` through the real Powers
-- queue if it does not. CALL IT EVERY TICK until it returns true; it is stateless and idempotent.
--
-- Returns `ready, status`:
--   true,  'ready'        the power can be fired NOW. Test.ActivateSupportPower will return 'issued'.
--   false, 'warmup'       too early to touch production; see ProductionWarmupTicks above.
--   false, 'buying'       a purchase was just queued.
--   false, 'loading'      the Powers queue is busy (this purchase, or another one, is building).
--   false, 'refused'      the queue would not take the order. The proxy is not in BuildableItems:
--                         the tier prerequisite is unmet (event tier without the sandbox option),
--                         the power's lobby checkbox is off, or there is no producer.
--   false, 'absent'       no such power on the player at all — a wrong OrderName, or the trait is
--                         not on the Player actor. Buying can never fix this.
--   false, 'no-manager'   no SupportPowerManager, or test mode is off.
--   false, 'charging:<n>' the power is on a TIMER, not a bank, and is coming on its own. Nothing
--                         to buy; wait.
--
-- The status token is deliberately printable straight into a verdict or an on-screen message. A
-- scenario that goes quiet is a scenario nobody can debug from a log, and every one of the six
-- failures above is otherwise indistinguishable from "the shot has not landed yet".
function TestHarness.EnsurePower(player, proxyType, powerKey, tick)
	if player == nil then
		return false, "no-player"
	end

	-- Bare token, no decoration: GetSupportPowerState returns exactly one of the vocabulary in
	-- TestGlobal.SupportPowerState. Compared exactly on purpose.
	local state = Test.GetSupportPowerState(player, powerKey)
	if state == "ready" then
		return true, "ready"
	end

	-- 'hidden' is the ONLY state a purchase can move. Everything else is either a wiring fault
	-- (absent, no-manager) or a power that is not bank-gated at all (charging:<n>), and issuing a
	-- build order against any of them would take money and change nothing.
	if state ~= "hidden" then
		return false, state
	end

	if tick == nil or tick < TestHarness.ProductionWarmupTicks then
		return false, "warmup"
	end

	-- IsProducing asks whether the queue that builds `proxyType` has ANYTHING queued, not whether
	-- this particular item is in it (ProductionProperties.cs:295-303). That is the reading we want:
	-- a ClassicProductionQueue builds Queue[0] and only Queue[0] (ProductionQueue.cs:338-345), so
	-- one busy Powers queue means every other purchase must wait its turn regardless of what is in
	-- it. It is also what makes several EnsurePower calls in one scenario serialise by themselves.
	if player.IsProducing(proxyType) then
		return false, "loading"
	end

	if player.Build({ proxyType }) then
		return false, "buying"
	end

	return false, "refused"
end

-- ============================================================================
-- THE MISSILE-STRIKE APPROACH CONTRACT
-- ============================================================================
--
-- WHAT REPLACED WHAT, on 2026-09-07 (engine commit b69681d2). MissileStrikePower used to spawn
-- every warhead at `map.ChooseClosestEdgeCell(HomeLocation)` -- ONE on-map cell on the owner's own
-- border -- and then face each warhead at its OWN aim point. Four scenarios asserted that shape
-- the only way it could be read from Lua: `entryToHome <= 15` cells. That number is now
-- meaningless, and worse than meaningless: the spawn is deliberately far off-map, so the old
-- assertion FAILS ON CORRECT BEHAVIOUR.
--
-- THE SHIPPED RULE IS A VECTOR EQUATION, and it is the whole contract:
--
--     entry = aimPoint - unit(home -> salvoCentroid) * standoff
--     standoff = mapDiagonal + ApproachMargin      (16 cells; MissileStrikePower.ApproachMargin)
--
-- so for a ONE-WARHEAD strike, where the centroid IS the aim point, the entry sits exactly on the
-- ray from the player's own position through the target, `standoff` cells back from the target --
-- which is `standoff - |home -> target|` cells BEHIND the player, and never less than
-- ApproachMargin behind, because the map diagonal is by construction the largest separation of any
-- two in-map points. That last inequality is a proof, not a tuned number, and it is what makes the
-- band below map-independent.
--
-- WHY THIS IS NOT THE OLD TEST WITH A BIGGER ALLOWANCE. `entryToHome <= 60` would have gone green
-- on a missile arriving from ANY compass direction at the right radius -- including the "always
-- from the east" rule the user rejected by name. The three readings below pin a DIRECTION, a SIDE
-- and a DISTANCE, and the shipped geometry is the only thing that satisfies all three:
--
--   lateral  -- how far off the home->target axis the warhead entered. Zero in exact arithmetic.
--              This is the BEARING, and it is the reading that fails for a faction constant, a
--              fixed compass bearing, a per-map bearing, or a bearing taken per-warhead from a
--              shared spawn point -- which is precisely the fan b69681d2 removed. NOTE THE
--              LIMITATION, because it is not obvious: on a map where the aim point is due east of
--              home, EVERY eastward rule scores zero here. A scenario that wants this reading to
--              have teeth must put its target off the launching player's row; see
--              test-missile-strike-power, which was moved off it for exactly this reason.
--   along    -- the signed projection onto that axis. NEGATIVE means the warhead came in over the
--              player's own shoulder, which is the property the old map-edge rule protected and
--              the one the user's ruling preserved: the player picks WHERE, never WHICH WAY. A
--              positive reading is a sign flip in the WAngle rotation (WAngle is counterclockwise)
--              and means the strike arrived from beyond the target, out of enemy territory.
--   drift    -- along, minus its derived expectation. This is what catches a REGRESSION TO THE OLD
--              RULE: an on-map edge cell is a few cells behind home, the shipped spawn is
--              `standoff - |home->target|` behind it, and on a 66x34 map those are 5 and 56. It
--              also catches a wrong standoff, a wrong ApproachMargin, and a wiring bug that hands
--              MissileStrikeApproach.For the wrong player's home or the wrong map size -- none of
--              which the World-free NUnit suite can see, because it is handed those values.
--
-- The Lua arithmetic here is float and the engine's is integer, which is fine in both directions:
-- this MEASURES a shipped position, it does not compute one, and nothing it returns is fed back
-- into world state. Determinism is the engine's problem and is pinned in MissileStrikeApproachTest.

-- MissileStrikePowerInfo.ApproachMargin, in cells. Not overridden by any shipped power or scenario.
TestHarness.ApproachMarginCells = 16

-- The standoff the engine will walk back, in cells, for a map of this CELL SIZE -- MapSize from
-- map.yaml, NOT Bounds: MissileStrikePower.ApproachFor passes map.MapSize straight through.
function TestHarness.ApproachStandoffCells(mapCellsX, mapCellsY)
	return math.sqrt(mapCellsX * mapCellsX + mapCellsY * mapCellsY) + TestHarness.ApproachMarginCells
end

-- Measure one warhead's entry against the approach it should have flown. Returns nil when the aim
-- point is the player's own cell, where there is no axis to project onto.
function TestHarness.MeasureApproach(entryX, entryY, homeX, homeY, targetX, targetY, mapCellsX, mapCellsY)
	local dx, dy = targetX - homeX, targetY - homeY
	local axis = math.sqrt(dx * dx + dy * dy)
	if axis == 0 then
		return nil
	end

	local ex, ey = entryX - homeX, entryY - homeY
	local standoff = TestHarness.ApproachStandoffCells(mapCellsX, mapCellsY)
	local along = (ex * dx + ey * dy) / axis
	local expectedAlong = axis - standoff

	return {
		axis = axis,
		standoff = standoff,
		along = along,
		expectedAlong = expectedAlong,
		drift = along - expectedAlong,
		-- The 2D cross product over the axis length: perpendicular distance, unsigned.
		lateral = math.abs(ex * dy - ey * dx) / axis,
	}
end

-- One line of the three readings, for the verdict summary. Always printed, pass or fail.
function TestHarness.ApproachSummary(m)
	if m == nil then
		return "approach=degenerate(target is home)"
	end

	return string.format(
		"approach lateral=%.1fc along=%.1fc (expected %.1fc, drift %+.1fc) axis=%.1fc standoff=%.1fc",
		m.lateral, m.along, m.expectedAlong, m.drift, m.axis, m.standoff)
end

-- The verdict. Returns nil when the warhead flew the shipped approach, or a diagnosis string.
--
-- maxLateral -- cells off the axis. Exact answer is 0; a cell coordinate is the FLOOR of a world
--               position, which costs up to ~1.5 cells on the diagonal.
-- maxLag     -- cells of travel allowed between the spawn and the first tick the poller saw the
--               actor. A per-tick poll normally sees it within one or two ticks; the fastest
--               shipped missile (Kinzhal, Speed 2000) covers 1.95 cells per tick, so 8 cells is
--               about four ticks of slack. It is one-sided on purpose: the missile can only move
--               TOWARD the target, so drift below the expectation is bounded by cell flooring.
function TestHarness.ApproachFault(m, maxLateral, maxLag)
	maxLateral = maxLateral or 4
	maxLag = maxLag or 8

	if m == nil then
		return "the aim point is the launching player's own cell, so there is no approach axis to"
			.. " measure. This is a scenario fault, not a shipped one"
	end

	if m.lateral > maxLateral then
		return string.format("THE BEARING IS WRONG: the warhead entered %.1f cells off the axis"
			.. " running from the launching player's own position through the aim point (allowance"
			.. " %g). MissileStrikeApproach.Bearing takes that azimuth from HomeLocation toward the"
			.. " salvo centroid, so a reading here means the approach stopped being"
			.. " player-situated -- a faction constant, a fixed compass bearing, a per-map bearing,"
			.. " or a bearing taken per-warhead from a shared spawn point, which is the fan"
			.. " b69681d2 removed", m.lateral, maxLateral)
	end

	if m.along >= 0 then
		return string.format("THE STRIKE CAME IN FROM THE WRONG SIDE: the warhead entered %.1f"
			.. " cells PAST the launching player, on the far side from its own base, so it arrived"
			.. " out of enemy territory rather than over the player's own shoulder. Expected %.1f."
			.. " A positive reading is a sign flip in the walk-back: SpawnPosition SUBTRACTS the"
			.. " rotated WVec(0, -standoff, 0), and WAngle is COUNTERCLOCKWISE",
			m.along, m.expectedAlong)
	end

	if m.drift > maxLag then
		return string.format("THE WARHEAD WAS BORN TOO CLOSE IN: it entered %.1f cells behind the"
			.. " launching player, where the shipped standoff puts it %.1f behind (drift %+.1f,"
			.. " allowance %g). THE LIKELY CAUSE IS A REGRESSION TO ChooseClosestEdgeCell(home),"
			.. " which lands an on-map edge cell a handful of cells from home instead of a"
			.. " map-diagonal plus ApproachMargin off-map -- read the entry cell in the summary: if"
			.. " it is inside the map bounds at all, the off-map spawn is gone. A smaller"
			.. " ApproachMargin, or a standoff taken from Bounds rather than MapSize, reads the"
			.. " same way and more mildly", -m.along, -m.expectedAlong, m.drift, maxLag)
	end

	if m.drift < -2 then
		return string.format("THE WARHEAD WAS BORN TOO FAR OUT: %.1f cells behind the launching"
			.. " player against an expected %.1f (drift %+.1f). The standoff is bounded at"
			.. " MissileStrikeApproach.MaxStandoff = 1024 cells precisely because CPos packs each"
			.. " axis into 12 signed bits and WRAPS outside -2048..2047, so an over-long walk-back"
			.. " decodes as a cell on the far side of the world", -m.along, -m.expectedAlong, m.drift)
	end

	return nil
end
