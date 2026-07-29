-- Phase 3 — StancePositioningExecutor RESIDUAL B1: a player redirect issued WHILE the executor's own
-- adjustment move is in flight (State == Adjusting).
--
-- Setup. The Rifle spawns 4 cells SOUTH of the zone-A treeline at (13,21); an enemy is sighted further
-- south. The executor (Defensive) relocates the Rifle NORTH to the treeline's south cover edge (13,17)
-- — a 4-cell adjustment (~164 ticks). That is the only cover edge inside the 4-cell Manhattan leash of
-- the spawn anchor, so the target is deterministically (13,17).
--
-- Injection. As soon as the Rifle has stepped off its spawn cell but has NOT yet reached the cover edge
-- (Location.Y strictly between 21 and 17 ⇒ the adjustment Move is in flight, State == Adjusting), the
-- test issues ONE scripted single-unit Move ~9 cells EAST to zone B (22,19). A single-unit Move does not
-- go through CohesionMoveModifier, so it never re-assigns the cohesion slot the executor set to (13,17).
--
-- The bug vs the fix.
--   * PRE-FIX: ITick returns early whenever State == Adjusting, so the mid-adjust redirect is never
--     caught. The cohesion slot stays pointed at (13,17). The redirect is short enough (~9 cells ≈ 450
--     ticks) that the trip finishes inside ForgetAfterTicks (750), so the slot is still FRESH when the
--     Rifle idles at B — and CohesionSlotMemory (declared before the executor) fires return-to-slot and
--     drags the Rifle back to (13,17) ONCE. FAIL: the Rifle comes within 6 cells of the abandoned cover
--     cell.
--   * FIXED: while Adjusting, ITick aborts the stale adjust the moment the Rifle leaves the
--     leash+margin band (Manhattan > LeashRadius(4)+AdjustLeashMargin(2) = 6) around the zone-A anchor.
--     ReleaseManagement clears the slot BEFORE the Rifle idles, so on arrival at B nothing drags it
--     back; the executor re-anchors at B (no threat there ⇒ it simply holds). PASS.
--
-- Enablement is the real Phase-3 path: USA is human, so GrantConditionOnHumanOwner grants
-- enable-tactical-positioning at spawn. All combatants are HoldFire so no shots ⇒ no suppression gate,
-- no AutoTarget chase to confound the movement assertions.

local SPAWN = { X = 13, Y = 21 }   -- Rifle spawn == zone-A anchor
local COVER = { X = 13, Y = 17 }   -- the zone-A south cover edge the executor adjusts toward
local B = { X = 22, Y = 19 }       -- redirect target (no cover, no enemy)

local function dist(loc, p) return math.abs(loc.X - p.X) + math.abs(loc.Y - p.Y) end

WorldLoaded = function()
	TestHarness.FocusBetween(Rifle, EnemyA)
	TestHarness.Select(Rifle)

	-- HoldFire silences shots (no suppression gate, no AutoTarget chase). The executor still runs — it
	-- is gated on the enable-tactical-positioning condition (granted to the human USA), not on firing.
	if not Rifle.IsDead then Rifle.Stance = "HoldFire" end
	if not EnemyA.IsDead then EnemyA.Stance = "HoldFire" end

	-- Deadlines (25 ticks/sec). Infantry Speed 25 ≈ 41 ticks/cell.
	local ADJUST_START_DEADLINE = 25 * 20   -- the executor must begin the north adjustment within this
	local REACH_B_DEADLINE = 25 * 60        -- ~9 cells + the injected detour, generous
	local HOLD = 25 * 14                     -- ticks the Rifle must hold near B without returning to A
	local HARD_TIMEOUT = 25 * 120

	-- Phase machine: waitAdjust -> (inject) -> reachB -> holdB.
	local phase = "waitAdjust"
	local leftA = false          -- set once the Rifle has first travelled clear of the zone-A vicinity
	local holdCount = 0
	local reanchorShot = false
	local elapsed = 0
	local finished = false

	-- One-shot verdict wrappers: the first verdict wins and stops the poll (avoids re-firing a verdict
	-- every tick once a terminal state is reached).
	local function pass() finished = true; Test.Pass() end
	local function fail(reason) finished = true; Test.Fail(reason) end

	local function step()
		if Rifle.IsDead then fail("Rifle died"); return end
		local loc = Rifle.Location

		if phase == "waitAdjust" then
			-- The executor has begun the adjustment once the Rifle has stepped NORTH off its spawn
			-- (Y < 21) but not yet reached the cover edge (Y > 17): the Move is in flight ⇒ Adjusting.
			if loc.Y < SPAWN.Y and loc.Y > COVER.Y then
				Rifle.Move(CPos.New(B.X, B.Y))
				phase = "reachB"
			elseif loc.Y <= COVER.Y then
				-- The Rifle reached the cover edge before we could inject mid-adjust. That means the
				-- adjustment window was missed (should not happen at 41 ticks/cell with a 1-tick poll);
				-- fail loudly rather than silently degrade into the already-covered post-Arrived case.
				fail("could not inject mid-adjust: Rifle reached cover edge at " ..
					loc.X .. "," .. loc.Y .. " before a redirect was issued")
			elseif elapsed >= ADJUST_START_DEADLINE then
				fail("executor never started the zone-A adjustment; Rifle at " .. loc.X .. "," .. loc.Y)
			end
		elseif phase == "reachB" then
			-- Once the Rifle has first travelled clear of the zone-A vicinity, guard the rest of the
			-- transit: it must never be dragged back toward the abandoned cover cell. (leftA avoids a
			-- false trip right after injection, when the unit is still legitimately near zone A.)
			if not leftA and dist(loc, COVER) > 6 then
				leftA = true
			end
			if leftA and dist(loc, COVER) <= 6 then
				fail("Rifle pulled back toward abandoned zone-A cover cell before reaching B; at " ..
					loc.X .. "," .. loc.Y)
			elseif dist(loc, B) <= 1 then
				phase = "holdB"
			elseif elapsed >= REACH_B_DEADLINE then
				fail("Rifle never reached zone B after the redirect; at " .. loc.X .. "," .. loc.Y)
			end
		elseif phase == "holdB" then
			-- THE residual-B1 assertion: having been redirected mid-adjust, the Rifle must hold at B and
			-- NEVER walk back to the abandoned zone-A cover cell.
			if dist(loc, COVER) <= 6 then
				fail("residual B1: Rifle walked back to abandoned zone-A cover cell; at " ..
					loc.X .. "," .. loc.Y)
			else
				if not reanchorShot and dist(loc, B) <= 1 then
					Test.Screenshot("held-at-zone-b",
						"expects: Rifle at/near zone B (x~22, y~19), NOT back west at zone-A cover (13,17)")
					reanchorShot = true
				end
				holdCount = holdCount + 1
				if holdCount >= HOLD then
					pass()
				end
			end
		end
	end

	local poll
	poll = function()
		if finished then return end
		elapsed = elapsed + 1
		step()
		if finished then return end
		if elapsed >= HARD_TIMEOUT then
			fail("hard timeout; phase=" .. phase .. " hold=" .. holdCount)
			return
		end
		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
