-- AUTO TEST: crew & pilot evacuation suite.
-- Three phases, paced for visual verification, each auto-asserting on counts.
--
-- Phase 1 — Vehicle overkill instant-kill: 6 vehicles take massive overkill
--          damage. Damage scaling on the onDeath path should kill all crew
--          inside; expect very few survivors. Regression for #2.
--
-- Phase 2 — Vehicle gentle finishing blow: 6 vehicles set to ~26% HP, then
--          dropped into Critical with a small 5%-of-MaxHP hit. Staged
--          ejection runs with low finishingDamage; crewDamage formula clamps
--          to 0; nearly all crew survive.
--
-- Phase 3 — Helicopter mid-air critical crash: 6 helicopters airborne, take
--          ~5% HP damage so HeliEmergencyLanding starts a crash sequence.
--          SuppressEjection blocks crew ejection; pilots/gunners die in wreck.

local TICKS_PER_SEC = TestHarness.TicksPerSecond  -- 25
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

-- Cell → WPos with optional altitude (helicopters fly at 1280 by default).
local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

-- Phase row layout: 6 columns at evenly spaced x, one y per phase.
local COLS = { 14, 22, 30, 38, 46, 54 }
local PHASE1_Y = 9
local PHASE2_Y = 16
local PHASE3_Y = 24

-- Crew actor types we count across phases.
local US_CREW = { "crew.driver.america", "crew.gunner.america", "crew.commander.america",
				   "crew.pilot.america", "crew.copilot.america" }
local RU_CREW = { "crew.driver.russia", "crew.gunner.russia", "crew.commander.russia",
				   "crew.pilot.russia", "crew.copilot.russia" }

local function countCrew(player, crewTypes)
	local total = 0
	for _, t in ipairs(crewTypes) do
		total = total + #player.GetActorsByType(t)
	end
	return total
end

-- Snapshot crew counts so we measure ONLY the crew spawned by this phase, not
-- any leakage from previous phases (in case a crew from phase 1 is still alive
-- when phase 2 evaluates).
local function snapshotCrew()
	return {
		usa = countCrew(USA, US_CREW),
		russia = countCrew(USA, RU_CREW),  -- USA owns kills, but Russia crew belong to Russia
	}
end

local results = {}

local function recordPhase(name, passed, detail)
	table.insert(results, { name = name, passed = passed, detail = detail })
	local prefix = passed and "PASS" or "FAIL"
	Media.DisplayMessage(prefix .. " — " .. name .. " (" .. detail .. ")", "EVAC SUITE")
end

local function finalize()
	Media.DisplayMessage("=== Suite complete ===", "EVAC SUITE")

	local allPassed = true
	for _, r in ipairs(results) do
		if not r.passed then allPassed = false end
	end

	if allPassed then
		Test.Pass()
	else
		local fails = {}
		for _, r in ipairs(results) do
			if not r.passed then table.insert(fails, r.name .. ": " .. r.detail) end
		end
		Test.Fail(table.concat(fails, " | "))
	end
end

-- ---------------------------------------------------------------------------
-- PHASE 1 — Vehicle overkill instant-kill.
-- ---------------------------------------------------------------------------
local function phase1()
	Media.DisplayMessage("PHASE 1: Vehicle overkill instant-kill — expect ~0 crew",
		"EVAC SUITE")
	Camera.Position = cellPos(34, PHASE1_Y, 0)

	local types = { "abrams", "abrams", "t90", "t90", "bmp2", "bmp2" }
	local owners = { USA, USA, RUSSIA, RUSSIA, RUSSIA, RUSSIA }
	local spawned = {}

	for i = 1, 6 do
		local v = Actor.Create(types[i], true, {
			Owner = owners[i],
			Location = CPos.New(COLS[i], PHASE1_Y),
			Facing = Angle.South,  -- South
		})
		table.insert(spawned, v)
	end

	-- Let player see the row settle.
	Trigger.AfterDelay(sec(2), function()
		-- Snapshot AFTER spawn, BEFORE damage. Vehicle creation itself shouldn't
		-- spawn crew, but capturing baseline is defensive.
		local baseline = snapshotCrew()

		for _, v in ipairs(spawned) do
			if not v.IsDead then v.Health = -100000 end
		end

		Trigger.AfterDelay(sec(5), function()
			local after = snapshotCrew()
			local newCrew = (after.usa - baseline.usa) + (after.russia - baseline.russia)
			-- 6 vehicles × 2-3 crew each = 12-18 expected with the legacy 90% rate;
			-- with damage scaling on overkill, expect ≤ 2 (allows one rare survivor).
			local passed = newCrew <= 2
			recordPhase("Phase 1 vehicle overkill",
				passed, newCrew .. " crew spawned (expected ≤ 2)")
			Trigger.AfterDelay(sec(2), phase2)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 2 — Vehicle gentle finishing blow (staged ejection).
-- ---------------------------------------------------------------------------
function phase2()
	Media.DisplayMessage("PHASE 2: Vehicle gentle finishing blow — expect most crew alive",
		"EVAC SUITE")
	Camera.Position = cellPos(34, PHASE2_Y, 0)

	local types = { "abrams", "abrams", "bradley", "bradley", "t90", "t90" }
	local owners = { USA, USA, USA, USA, RUSSIA, RUSSIA }
	local spawned = {}

	for i = 1, 6 do
		local v = Actor.Create(types[i], true, {
			Owner = owners[i],
			Location = CPos.New(COLS[i], PHASE2_Y),
			Facing = Angle.South,
		})
		table.insert(spawned, v)
	end

	-- Step 1: let them settle, then drop to ~26% HP (Heavy state, no eject yet).
	Trigger.AfterDelay(sec(2), function()
		for _, v in ipairs(spawned) do
			if not v.IsDead and v.MaxHealth > 0 then
				v.Health = math.floor(v.MaxHealth * 26 / 100)
			end
		end

		local baseline = snapshotCrew()

		-- Step 2: small finishing blow (~5% MaxHP) drops to Critical with
		-- a tiny finishingDamage. crewDamage formula clamps to 0, so all
		-- crew should survive ejection.
		Trigger.AfterDelay(sec(1), function()
			for _, v in ipairs(spawned) do
				if not v.IsDead and v.MaxHealth > 0 then
					local target = math.floor(v.MaxHealth * 21 / 100)  -- now in Critical
					v.Health = target
				end
			end

			-- Wait for staged ejection cycle (PostStopDelay + per-crew EjectionDelay × N).
			Trigger.AfterDelay(sec(12), function()
				-- Clean up any survivors still inside the wreck so the count is final.
				for _, v in ipairs(spawned) do
					if not v.IsDead then v.Health = -10000 end
				end

				Trigger.AfterDelay(sec(3), function()
					local after = snapshotCrew()
					local newCrew = (after.usa - baseline.usa) + (after.russia - baseline.russia)
					-- 6 vehicles × ~3 crew = 18 expected. Generous threshold accounts
					-- for the cleanup-overkill possibly killing some via damage scaling.
					local passed = newCrew >= 8
					recordPhase("Phase 2 staged kill",
						passed, newCrew .. " crew spawned (expected ≥ 8)")
					Trigger.AfterDelay(sec(2), phase3)
				end)
			end)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 3 — Helicopter mid-air critical crash.
-- ---------------------------------------------------------------------------
function phase3()
	Media.DisplayMessage("PHASE 3: Helicopter mid-air crash — expect ~0 pilots",
		"EVAC SUITE")
	Camera.Position = cellPos(34, PHASE3_Y, 0)

	-- Heli actor names use lowercase (engine internalizes); verify against YAML.
	local types = { "heli", "heli", "hind", "hind", "mi28", "mi28" }
	local owners = { USA, USA, RUSSIA, RUSSIA, RUSSIA, RUSSIA }
	local spawned = {}

	-- Spawn airborne via CenterPosition with Z=1280 (default helicopter cruise altitude).
	for i = 1, 6 do
		local h = Actor.Create(types[i], true, {
			Owner = owners[i],
			CenterPosition = cellPos(COLS[i], PHASE3_Y, 1280),
			Facing = Angle.South,
		})
		table.insert(spawned, h)
	end

	-- Let them settle airborne.
	Trigger.AfterDelay(sec(3), function()
		local baseline = snapshotCrew()

		-- Drop to Critical (~5% HP). HeliEmergencyLanding sees the heavy→critical
		-- jump while airborne, calls StartCrash → sets SuppressEjection, queues
		-- HeliCrashLand. Heli falls, Kill(self) fires, VehicleCrew.Killed sees
		-- SuppressEjection=true and bails before ejecting anyone.
		for _, h in ipairs(spawned) do
			if not h.IsDead and h.MaxHealth > 0 then
				h.Health = math.floor(h.MaxHealth * 5 / 100)
			end
		end

		-- Wait for crash impact + a buffer.
		Trigger.AfterDelay(sec(15), function()
			local after = snapshotCrew()
			local newCrew = (after.usa - baseline.usa) + (after.russia - baseline.russia)
			-- 6 helis × 1-2 pilots = 6-12 expected without suppress; with suppress
			-- in critical-crash, expect 0. Allow 1 for edge cases (heli somehow
			-- safe-landed before crash logic kicked in).
			local passed = newCrew <= 1
			recordPhase("Phase 3 heli crash",
				passed, newCrew .. " pilots spawned (expected ≤ 1)")
			Trigger.AfterDelay(sec(2), finalize)
		end)
	end)
end

-- ---------------------------------------------------------------------------
WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("Required players (USA, Russia) not found")
		return
	end

	-- Initial camera centered on the test rows.
	Camera.Position = cellPos(34, 16, 0)

	-- Tiny delay so World traits settle (TechTree, queues, etc.) before we
	-- start spawning + damaging actors.
	Trigger.AfterDelay(sec(1), phase1)
end
