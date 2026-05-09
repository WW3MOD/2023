-- AUTO TEST: realistic crew & pilot evacuation suite.
-- Five phases, each modelling a real-world combat outcome and asserting
-- per-unit-type survival counts.
--
-- The unit-class tuning lives in YAML (CrewDamageThresholdPercent +
-- EjectionSurvivalRate per actor). This test exists to lock those numbers
-- in: tighten / loosen YAML knobs, re-run, see if real-world behaviour
-- shifts as expected.
--
-- Phase 1 — Catastrophic kill (overkill):     all crew die in the wreck.
-- Phase 2 — Marginal MaxHP kill (single shot):class-dependent. Western MBTs
--           save most of their crew; Soviet autoloader designs lose most.
-- Phase 3 — Slow attrition (staged ejection): crew bails over time, all live.
-- Phase 4 — Helicopter mid-air crash:         SuppressEjection, no pilots.
-- Phase 5 — Helicopter safe autorotate:       crew alive, airframe → Neutral.

local TICKS_PER_SEC = TestHarness.TicksPerSecond  -- 25
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

-- Crew actor types (faction-suffixed). All vehicles share these.
local US_CREW = { "crew.driver.america", "crew.gunner.america", "crew.commander.america",
				   "crew.pilot.america", "crew.copilot.america" }
local RU_CREW = { "crew.driver.russia", "crew.gunner.russia", "crew.commander.russia",
				   "crew.pilot.russia", "crew.copilot.russia" }

-- Per-actor → which faction's crew pool to query.
local CREW_POOL_FOR = {
	abrams = US_CREW, bradley = US_CREW, m113 = US_CREW, humvee = US_CREW,
	heli = US_CREW,
	t90 = RU_CREW, bmp2 = RU_CREW, btr = RU_CREW,
	hind = RU_CREW, mi28 = RU_CREW,
}

local function totalCrew(player, types)
	local n = 0
	for _, t in ipairs(types) do n = n + #player.GetActorsByType(t) end
	return n
end

-- Snapshot all crew counts (US + RU) so a phase can compute its own delta.
local function snapshot()
	return totalCrew(USA, US_CREW) + totalCrew(RUSSIA, RU_CREW)
end

local results = {}

-- Forward-declare phases so each can reference the next without a global
-- lookup. `local function` doesn't hoist; an unforwarded reference resolves
-- to the global namespace at call time → nil → crashes inside Trigger.AfterDelay.
local phase1, phase2, phase3, phase4, phase5
local finalize

local function recordPhase(name, passed, detail)
	table.insert(results, { name = name, passed = passed, detail = detail })
	local prefix = passed and "PASS" or "FAIL"
	Media.DisplayMessage(prefix .. " — " .. name .. ": " .. detail, "EVAC SUITE")
end

finalize = function()
	Media.DisplayMessage("=== Suite complete ===", "EVAC SUITE")
	local fails = {}
	for _, r in ipairs(results) do
		if not r.passed then table.insert(fails, r.name .. ": " .. r.detail) end
	end
	if #fails == 0 then Test.Pass()
	else Test.Fail(table.concat(fails, " | ")) end
end

-- Pick a player object for an actor type by faction.
local function ownerFor(actorType)
	if CREW_POOL_FOR[actorType] == US_CREW then return USA end
	return RUSSIA
end

-- Spawn a row of vehicles/helicopters at the given y. `airborne=true` spawns
-- at altitude 1280 (cruise). `facing` defaults to South. All spawned units
-- are set to HoldFire so they don't open fire on each other before the test
-- applies its simulated damage — keeps results clean.
local function spawnRow(types, y, airborne, facing)
	facing = facing or Angle.South
	local spawned = {}
	for i, t in ipairs(types) do
		local x = 8 + (i - 1) * 6
		local pos = airborne and cellPos(x, y, 1280) or nil
		local actor = Actor.Create(t, true, {
			Owner = ownerFor(t),
			Location = (not airborne) and CPos.New(x, y) or nil,
			CenterPosition = pos,
			Facing = facing,
		})
		-- pcall — Stance setter exists on combat units, but a future test that
		-- spawns trucks etc. would error otherwise.
		pcall(function() actor.Stance = "HoldFire" end)
		table.insert(spawned, actor)
	end
	return spawned
end

-- ---------------------------------------------------------------------------
-- PHASE 1 — Catastrophic kill: overkill on all vehicles.
-- ---------------------------------------------------------------------------
local PHASE1_Y = 4
phase1 = function()
	Media.DisplayMessage("PHASE 1: Catastrophic kill (overkill) — expect ~0 crew",
		"EVAC SUITE")
	Camera.Position = cellPos(28, PHASE1_Y, 0)

	-- 2× of each: Abrams (US MBT), T-90 (RU autoloader), Bradley (US IFV),
	-- BMP-2 (RU autoloader IFV), Humvee (light unarmored), BTR (RU APC).
	local types = { "abrams", "abrams", "t90", "t90", "bradley", "bradley",
					 "bmp2", "bmp2", "humvee", "humvee" }
	local spawned = spawnRow(types, PHASE1_Y, false)

	Trigger.AfterDelay(sec(2), function()
		local before = snapshot()
		for _, v in ipairs(spawned) do
			if not v.IsDead then v.Health = -100000 end
		end

		Trigger.AfterDelay(sec(5), function()
			local after = snapshot()
			local newCrew = after - before
			-- 10 vehicles × 2-3 crew = 22 expected without scaling. Real-world
			-- catastrophic kill: all crew die. Allow 2 for variance.
			local passed = newCrew <= 2
			recordPhase("P1 catastrophic", passed,
				newCrew .. " crew survived (≤ 2)")
			Trigger.AfterDelay(sec(2), phase2)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 2 — Marginal MaxHP kill: tests class-dependent survival.
-- ---------------------------------------------------------------------------
-- Apply finishingDamage ≈ MaxHP. crewDamage formula:
--   crewDamage = crewMaxHP × (MaxHP - threshold%×MaxHP) / MaxHP
--              = crewMaxHP × (1 - threshold/100) + variance
--
-- High-threshold class (Abrams ≈ 38%): crewDamage ≈ 62-81% crewMaxHP. Crew
--   spawns with low HP but survives the damage gate.
-- Low-threshold class (T-90 ≈ 14%):    crewDamage ≈ 86-105%. Half the time
--   ≥ crewMaxHP, crew dies inside.
--
-- Per-class assertion exposes whether the YAML tuning is loaded and applied.
local PHASE2_Y = 10
phase2 = function()
	Media.DisplayMessage("PHASE 2: Marginal MaxHP kill — Abrams crew lives, T-90 doesn't",
		"EVAC SUITE")
	Camera.Position = cellPos(28, PHASE2_Y, 0)

	-- 3× each of the two MBTs we want to compare.
	local abramsList = spawnRow({ "abrams", "abrams", "abrams" }, PHASE2_Y, false)
	local t90List = spawnRow({ "t90", "t90", "t90" }, PHASE2_Y + 4, false)

	Trigger.AfterDelay(sec(2), function()
		local beforeUS = totalCrew(USA, US_CREW)
		local beforeRU = totalCrew(RUSSIA, RU_CREW)

		-- Health = 0 → damage = current HP = MaxHP (vehicles at full).
		for _, v in ipairs(abramsList) do
			if not v.IsDead then v.Health = 0 end
		end
		for _, v in ipairs(t90List) do
			if not v.IsDead then v.Health = 0 end
		end

		Trigger.AfterDelay(sec(5), function()
			local abramsCrew = totalCrew(USA, US_CREW) - beforeUS
			local t90Crew = totalCrew(RUSSIA, RU_CREW) - beforeRU

			-- Abrams: 9 slots, expect ≥ 4 (crew survives damage gate, may be
			--   injured; 95% EjectionSurvivalRate gives ~8.5 spawned).
			-- T-90:   9 slots, expect ≤ 4 (autoloader cookoff + low survival
			--   rate: ~50% spawn rate × 50% damage-gate-survival ≈ 25% = 2-3).
			local abramsOK = abramsCrew >= 4
			local t90OK = t90Crew <= 4
			local passed = abramsOK and t90OK

			recordPhase("P2 marginal MBT", passed,
				"Abrams=" .. abramsCrew .. "/9 (need ≥4), T-90=" .. t90Crew .. "/9 (need ≤4)")
			Trigger.AfterDelay(sec(2), phase3)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 3 — Slow attrition (staged ejection).
-- ---------------------------------------------------------------------------
local PHASE3_Y = 18
phase3 = function()
	Media.DisplayMessage("PHASE 3: Slow attrition — most crew bails out alive",
		"EVAC SUITE")
	Camera.Position = cellPos(28, PHASE3_Y, 0)

	local types = { "abrams", "abrams", "bradley", "bradley", "t90", "t90" }
	local spawned = spawnRow(types, PHASE3_Y, false)

	Trigger.AfterDelay(sec(2), function()
		-- Step 1: drop to 26% (Heavy state, no eject yet).
		for _, v in ipairs(spawned) do
			if not v.IsDead and v.MaxHealth > 0 then
				v.Health = math.floor(v.MaxHealth * 26 / 100)
			end
		end

		local beforeUS = totalCrew(USA, US_CREW)
		local beforeRU = totalCrew(RUSSIA, RU_CREW)

		-- Step 2: 5% finishing blow → Critical with tiny finishingDamage.
		-- Below all class thresholds → crewDamage clamps to 0 → all spawn alive.
		Trigger.AfterDelay(sec(1), function()
			for _, v in ipairs(spawned) do
				if not v.IsDead and v.MaxHealth > 0 then
					v.Health = math.floor(v.MaxHealth * 21 / 100)
				end
			end

			-- Wait for staged ejection cycle.
			Trigger.AfterDelay(sec(12), function()
				-- Clean up any survivors still inside.
				for _, v in ipairs(spawned) do
					if not v.IsDead then v.Health = -10000 end
				end

				Trigger.AfterDelay(sec(3), function()
					local crew = (totalCrew(USA, US_CREW) - beforeUS)
						+ (totalCrew(RUSSIA, RU_CREW) - beforeRU)
					-- 6 vehicles × ~3 crew = 18 expected. Cleanup overkill at
					-- the end may damage one or two via post-eject scaling.
					local passed = crew >= 12
					recordPhase("P3 attrition", passed,
						crew .. " crew survived (≥ 12)")
					Trigger.AfterDelay(sec(2), phase4)
				end)
			end)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 4 — Helicopter mid-air crash.
-- ---------------------------------------------------------------------------
local PHASE4_Y = 24
phase4 = function()
	Media.DisplayMessage("PHASE 4: Helicopter mid-air crash — expect 0 pilots",
		"EVAC SUITE")
	Camera.Position = cellPos(28, PHASE4_Y, 0)

	local types = { "heli", "heli", "hind", "hind", "mi28", "mi28" }
	local spawned = spawnRow(types, PHASE4_Y, true)

	Trigger.AfterDelay(sec(3), function()
		local before = snapshot()
		-- Drop airborne helicopters to Critical → StartCrash → SuppressEjection.
		for _, h in ipairs(spawned) do
			if not h.IsDead and h.MaxHealth > 0 then
				h.Health = math.floor(h.MaxHealth * 5 / 100)
			end
		end

		Trigger.AfterDelay(sec(15), function()
			local newCrew = snapshot() - before
			local passed = newCrew <= 1
			recordPhase("P4 heli crash", passed,
				newCrew .. " pilots survived (≤ 1)")
			Trigger.AfterDelay(sec(2), phase5)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 5 — Helicopter safe autorotation.
-- ---------------------------------------------------------------------------
-- Drop to ~30% (Heavy state, NOT Critical). HeliEmergencyLanding fires
-- StartAutorotation. After ~10s of descent on grass: OnSafeLanding ejects
-- all crew alive, ChangeOwner → Neutral.
local PHASE5_Y = 30  -- spawn near south edge, autorotate north into the map
phase5 = function()
	Media.DisplayMessage("PHASE 5: Heli safe autorotate — crew alive, husk → Neutral",
		"EVAC SUITE")
	Camera.Position = cellPos(28, PHASE5_Y - 6, 0)

	-- Face NORTH (Angle.North = 0) so autorotation drift carries them
	-- back into the map rather than off the south edge.
	local types = { "heli", "heli", "hind", "hind" }
	local spawned = spawnRow(types, PHASE5_Y, true, Angle.North)

	Trigger.AfterDelay(sec(3), function()
		local before = snapshot()
		-- Drop to ~30% (Heavy) → HeliEmergencyLanding triggers autorotation.
		for _, h in ipairs(spawned) do
			if not h.IsDead and h.MaxHealth > 0 then
				h.Health = math.floor(h.MaxHealth * 30 / 100)
			end
		end

		-- Autorotate descent + flare + landing + crew eject — give it 18s.
		Trigger.AfterDelay(sec(18), function()
			local newCrew = snapshot() - before

			-- Husks should have transferred to Neutral.
			local neutralHusks = 0
			local stillOriginal = 0
			for _, h in ipairs(spawned) do
				if not h.IsDead and h.IsInWorld then
					if h.Owner.InternalName == "Neutral" then
						neutralHusks = neutralHusks + 1
					else
						stillOriginal = stillOriginal + 1
					end
				end
			end

			-- 4 helis × 2 crew (heli=Pilot+Gunner, hind=Pilot+Gunner) = 8 expected.
			-- Allow 5 minimum (some may have unsafe-landed or terrain-died).
			local crewOK = newCrew >= 5
			local huskOK = neutralHusks >= 2  -- at least half transferred
			local passed = crewOK and huskOK
			recordPhase("P5 heli autorotate", passed,
				newCrew .. " crew alive (≥ 5), " .. neutralHusks .. " neutral husks (≥ 2), " ..
				stillOriginal .. " still owned")
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

	Camera.Position = cellPos(28, 16, 0)
	Trigger.AfterDelay(sec(1), phase1)
end
