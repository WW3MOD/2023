-- AUTO TEST: crew & pilot evacuation suite.
--
-- Every phase asserts WHO GOT OUT of a stricken vehicle, and none asserts who is
-- still alive afterwards. That is not a style choice — see "THE TWO RULES THIS
-- SUITE HAS TO OBEY" below the helpers. Ejected crew burn to death on an
-- unbounded timer by design, so a survivor count measures the clock, not the
-- mechanic, and every phase that tried it eventually became a coin flip.
--
-- Phase 1 — Catastrophic kill (overkill):  nobody gets out. Expect 0 of 22.
-- Phase 2 — Staged ejection:               every slot empties a wrecked-but-alive
--                                          Abrams. Expect 12 of 12.
-- Phase 3 — Two-step attrition:            damaged, then finished; the roster
--                                          still empties. Expect 18 of 18.
-- Phase 4 — Helicopter mid-air crash:      SuppressEjection holds. Expect 0.
-- Phase 5 — Helicopter safe autorotate:    every airframe resolves — destroyed
--                                          or handed to Neutral. Expect 0 left
--                                          flying.
--
-- Per-class tuning (CrewDamageThresholdPercent) still lives in YAML, but note
-- that no phase here can currently discriminate it: it only bites when
-- crewDamage >= crewMaxHP, which none of these damage profiles reaches. Phase 1
-- covers the total-loss end and the rest cover the pipeline. If you want the
-- class curve pinned, that needs a new phase built for it.

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

-- ---------------------------------------------------------------------------
-- THE TWO RULES THIS SUITE HAS TO OBEY, AND WHY
-- ---------------------------------------------------------------------------
-- Ejected crew emerge on fire and have NO WAY TO PUT THEMSELVES OUT. That is
-- deliberate game behaviour, ruled on 2026-08-10 and not up for change here:
-- crew are supposed to burn and die when the hull was badly hurt. It means the
-- crew population after an ejection only ever SHRINKS, and every man in it dies
-- eventually — the only thing that varies is when.
--
-- Two consequences, and every phase below is built on them:
--
-- 1. NEVER ASSERT "N ARE STILL ALIVE AT TICK T". That measures how far along a
--    death timer everyone is, with the boundary sitting inside the per-crew
--    random damage band. It is why P2 went 3 pass / 4 fail across 7 runs on
--    2026-08-10 and why its threshold was walked 12 -> 8 -> 6 in May by three
--    people in turn. No threshold fixes that shape. Assert the getting OUT,
--    which the engine does guarantee, via the PEAK count below.
--
-- 2. CLEAR THE FIELD FIRST. snapshot() is global across both factions, so crew
--    left burning by an earlier phase die inside the next phase's window and
--    move its numbers. P4 was the worst case: phases 2 and 3 leave up to 30
--    doomed men on the ground and P4's window is exactly when they die, so a
--    negative drift could absorb a dozen real pilot ejections and still pass.

local POLL_INTERVAL = 25  -- 1s

-- Poll snapshot() over a window and hand the PEAK to `done`. A man who ejects
-- and then burns is still an ejection that happened, so the maximum ever seen is
-- the honest count and it does not depend on when anyone dies.
--
-- POLL_INTERVAL must be shorter than the shortest possible crew lifetime, or
-- someone could appear and die between two polls. The shortest in this suite is
-- ~256 ticks (a Bradley crewman emerging at 36% MaxHP, burning 1% per 8 ticks);
-- 25 ticks leaves an order of magnitude of margin.
local function pollPeakCrew(samples, done)
	local peak = 0
	local function step(remaining)
		local n = snapshot()
		if n > peak then peak = n end
		if remaining <= 0 then
			done(peak)
		else
			Trigger.AfterDelay(POLL_INTERVAL, function() step(remaining - 1) end)
		end
	end
	step(samples)
end

-- Remove every crew actor on the map, so a phase starts from a clean field.
-- Destroy() removes them without a death sequence or a cookoff.
local function clearAllCrew()
	for _, t in ipairs(US_CREW) do
		for _, c in ipairs(USA.GetActorsByType(t)) do c.Destroy() end
	end
	for _, t in ipairs(RU_CREW) do
		for _, c in ipairs(RUSSIA.GetActorsByType(t)) do c.Destroy() end
	end
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

		-- Overkill kills the hull outright: VehicleCrew.Killed clears `ejecting`
		-- in the same InflictDamage call that armed it, and ITick bails on
		-- self.IsDead, so no crew actor is ever created. The engine calls this
		-- total loss and none of these hulls carries EjectOnDeath. So the
		-- guarantee is ZERO, not "about zero" — the old "allow 2 for variance"
		-- was slack over a deterministic outcome.
		--
		-- Peak, not endpoint: a crewman who spawned against that guarantee would
		-- be burning from a 0%-HP wreck and could die before a single late
		-- sample, hiding the very regression this phase exists to catch.
		pollPeakCrew(5, function(peak)
			local out = peak - before
			local passed = out == 0
			recordPhase("P1 catastrophic", passed,
				out .. " crew got out of a catastrophic kill (expected 0)")
			Trigger.AfterDelay(sec(2), phase2)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 2 — Staged ejection: every crew slot empties out of a wrecked hull.
-- ---------------------------------------------------------------------------
-- Measures THE GETTING OUT (see the two rules at the top of this file). It used
-- to count crew still alive 15s after the hull was wrecked and went 3 pass /
-- 4 fail across 7 runs on 2026-08-10.
--
-- The numbers behind the window, from VehicleCrew.cs + the Abrams block in
-- vehicles-america.yaml:
--   LAST possible ejection  ≈ tick 126. PostStopDelay 20 ±15 (no YAML override;
--                           the old "40 ±10" here was wrong), then
--                           EjectionDelay 30 ±15 twice.
--   FIRST possible death    ≈ tick 310. Emerge HP is at worst 38% of crewMaxHP
--                           (crewDamage = 42% + rand(0..20%), never lethal here),
--                           and 38 × 8 ticks of burn must elapse to reach zero.
-- 200 ticks of polling covers the first with 74 ticks to spare; the peak makes
-- the second a margin rather than a dependency. Nothing else can kill a crewman
-- in there — the roster is single-faction so there is no crossfire, and the hull
-- cookoff only fires when we execute the wreck, which happens AFTER the verdict.
--
-- NOT COVERED HERE: the class-dependent "died inside the hull" outcome the old
-- comment described. That needs crewDamage >= crewMaxHP, and an Abrams tops out
-- near 78% at any HP this suite uses, so it is unreachable with an Abrams-only
-- roster. The Soviet autoloader case that does reach it is Phase 1's job.
local PHASE2_Y = 10
phase2 = function()
	Media.DisplayMessage("PHASE 2: Staged eject — all 12 Abrams crew get out",
		"EVAC SUITE")
	Camera.Position = cellPos(28, PHASE2_Y, 0)

	-- Abrams-only so ejected crew is single-faction and doesn't cross-fire.
	local tanks = spawnRow({ "abrams", "abrams", "abrams", "abrams" }, PHASE2_Y, false)

	Trigger.AfterDelay(sec(2), function()
		clearAllCrew()

		-- One tick for the removals to leave the world before the baseline.
		Trigger.AfterDelay(1, function()
			local before = snapshot()

			for _, v in ipairs(tanks) do
				if not v.IsDead and v.MaxHealth > 0 then
					-- 20% HP: Heavy→Critical crossing arms the staged eject,
					-- and auto-bleed is off in this scenario's rules.yaml so the
					-- hull parks here until we execute it below.
					v.Health = math.floor(v.MaxHealth * 20 / 100)
				end
			end

			-- 8 polls = 200 ticks, comfortably past the tick-126 worst case.
			pollPeakCrew(8, function(peak)
				local out = peak - before
				-- 4 hulls × 3 slots. Not a tuned threshold — it is the whole
				-- roster, so there is no number here to walk. Fewer than 12 means
				-- a slot failed to empty: the eject never armed, the staged cycle
				-- stalled, or crewDamage turned lethal on emerge.
				local passed = out == 12
				recordPhase("P2 staged eject", passed,
					out .. " of 12 Abrams crew got out")

				-- Execute the wrecks only now — after the verdict. This keeps the
				-- phase's total length, and everything downstream of it, at the
				-- timing Phase 3 was calibrated against.
				Trigger.AfterDelay(sec(4), function()
					for _, v in ipairs(tanks) do
						if not v.IsDead then v.Health = -10000 end
					end

					Trigger.AfterDelay(sec(5), phase3)
				end)
			end)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 3 — Two-step attrition: a hull damaged, then finished, still empties.
-- ---------------------------------------------------------------------------
-- Same measurement rules as P2, for the same reason — this phase asserted
-- "crew >= 8 still alive" and was the next one to flake once P2 was fixed
-- (P3 attrition: 7 crew survived, seed -1931316743).
--
-- What is different from P2 is the damage path: the eject arms on the FIRST
-- drop (100% → 26% crosses into Heavy, finishingDamage = 74% MaxHP), and the
-- second drop only deepens the burn. The old comment claimed the opposite —
-- that the 5% finishing blow set finishingDamage and "clamps crewDamage to 0" —
-- which is not what VehicleCrew.DamageStateChanged does; it latches on the
-- transition and ignores later hits.
--
-- Nobody dies inside either, so the whole roster gets out:
--   Abrams  threshold 38 → crewDamage (74-38)% + rand(0..20%) = 36-56%
--   Bradley threshold 30 → crewDamage (74-30)% + rand(0..20%) = 44-64%
-- Worst emerge HP is 36% (a Bradley crewman), and at 3 stacks that is
-- 36 × 8 = 288 ticks of burn before the first death can land, against a last
-- ejection at ≈ tick 126 (Bradley: PostStopDelay 20 ±10, EjectionDelay 25 ±10).
--
-- The old comment also described "mixed-faction crew engaging each other" as
-- the reason for the loose bound. The roster is all-USA; there is no crossfire
-- and there was none to tolerate.
local PHASE3_Y = 18
phase3 = function()
	Media.DisplayMessage("PHASE 3: Two-step attrition — all 18 crew get out",
		"EVAC SUITE")
	Camera.Position = cellPos(28, PHASE3_Y, 0)

	-- All-USA so ejected crew doesn't gun down its counterpart on the ground
	-- (default ^AutoTargetLMG stance is FireAtWill, and there's no clean way to
	-- force HoldFire on actors that don't exist yet at test setup).
	local types = { "abrams", "abrams", "abrams", "bradley", "bradley", "bradley" }
	local spawned = spawnRow(types, PHASE3_Y, false)

	Trigger.AfterDelay(sec(2), function()
		clearAllCrew()

		Trigger.AfterDelay(1, function()
			local before = snapshot()

			-- Step 1: drop to 26% (Heavy). This is the hit that arms the eject.
			for _, v in ipairs(spawned) do
				if not v.IsDead and v.MaxHealth > 0 then
					v.Health = math.floor(v.MaxHealth * 26 / 100)
				end
			end

			-- Step 2, 1s later: down to 21% (Critical). Deepens the inherited
			-- burn from 2 stacks to 3; does not re-latch finishingDamage.
			Trigger.AfterDelay(sec(1), function()
				for _, v in ipairs(spawned) do
					if not v.IsDead and v.MaxHealth > 0 then
						v.Health = math.floor(v.MaxHealth * 21 / 100)
					end
				end
			end)

			-- 8 polls = 200 ticks from step 1: past the tick-126 worst-case
			-- ejection, well short of the tick-313 earliest possible death.
			pollPeakCrew(8, function(peak)
				local out = peak - before
				-- 3 Abrams × 3 slots + 3 Bradleys × 3 slots. The whole roster
				-- again, so there is no threshold here to walk either.
				local passed = out == 18
				recordPhase("P3 two-step eject", passed,
					out .. " of 18 crew got out")

				-- Execute the hulls after the verdict, keeping this phase's
				-- total length unchanged for everything downstream.
				Trigger.AfterDelay(sec(4), function()
					for _, v in ipairs(spawned) do
						if not v.IsDead then v.Health = -10000 end
					end

					Trigger.AfterDelay(sec(5), phase4)
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
		clearAllCrew()

		Trigger.AfterDelay(1, function()
			local before = snapshot()

			-- Drop airborne helicopters to Critical → StartCrash, which sets
			-- VehicleCrew.SuppressEjection and leaves it set through impact
			-- (HeliEmergencyLanding.cs:242-258). Nobody gets out of a mid-air
			-- crash, so the guarantee is zero — the old "<= 1" was slack.
			for _, h in ipairs(spawned) do
				if not h.IsDead and h.MaxHealth > 0 then
					h.Health = math.floor(h.MaxHealth * 5 / 100)
				end
			end

			-- Peak matters most here. A pilot ejected against the suppression
			-- would come out of a 5%-HP airframe at 6-7 fire stacks and burn
			-- down in well under this window, so a single late sample could
			-- report zero for a fully broken suppression. Combined with the
			-- clear above — phases 2 and 3 leave up to 30 doomed men whose
			-- deaths used to push this delta negative — the old form could
			-- absorb a dozen real ejections and still pass.
			pollPeakCrew(15, function(peak)
				local out = peak - before
				local passed = out == 0
				recordPhase("P4 heli crash", passed,
					out .. " pilots got out of a mid-air crash (expected 0)")
				Trigger.AfterDelay(sec(2), phase5)
			end)
		end)
	end)
end

-- ---------------------------------------------------------------------------
-- PHASE 5 — Helicopter safe autorotation.
-- ---------------------------------------------------------------------------
-- Drop to ~30% (Heavy, NOT Critical) → HeliEmergencyLanding.StartAutorotation.
--
-- THE OLD ASSERTION COULD NOT FAIL. It was
--     crashDisabled >= 1 or destroyed == #spawned
-- where crashDisabled counted helis alive-and-in-world and destroyed counted the
-- rest — so the two always summed to #spawned and one disjunct was always true.
-- Worse, it was a tautology that specifically hid its own regression: a heli
-- whose autorotation never fired just hovers at 30% HP, alive and in-world, and
-- was counted as "safe-landed (burning)".
--
-- What the engine actually guarantees for an airframe that entered emergency
-- descent is that it RESOLVES — every heli ends up either destroyed, or on the
-- ground handed to Neutral (OnSafeLanding → ChangeOwner, HeliEmergencyLanding.cs
-- :352-356; TransferToNeutralOnSafeLanding defaults true and aircraft.yaml does
-- not override it). Still airborne and still ours is the failure, and it is the
-- one state the old form treated as success.
--
-- Crew survival is deliberately NOT asserted here: OnSafeLanding releases the
-- ejection suppression, so crew come out burning like everywhere else and are
-- on the same unbounded timer. Both terminal states pass, including a heli that
-- safe-lands and then burns out, so nothing here depends on when anything dies.
--
-- Sampled at 10s. Descent is ~64 ticks (1280 altitude / AutorotationDescentRate
-- 20), so this is roughly 4x the time needed — the old "~10s of descent" comment
-- was another stale figure.
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
		-- Drop to ~30% (Heavy) → HeliEmergencyLanding triggers autorotation.
		for _, h in ipairs(spawned) do
			if not h.IsDead and h.MaxHealth > 0 then
				h.Health = math.floor(h.MaxHealth * 30 / 100)
			end
		end

		Trigger.AfterDelay(sec(10), function()
			local abandoned = 0   -- on the ground, handed to Neutral
			local destroyed = 0   -- gone: crash-landed, burned out, or off-map
			local stillFlying = 0 -- alive and still ours: the pipeline never ran

			for _, h in ipairs(spawned) do
				if h.IsDead or not h.IsInWorld then
					destroyed = destroyed + 1
				elseif h.Owner == NEUTRAL then
					abandoned = abandoned + 1
				else
					stillFlying = stillFlying + 1
				end
			end

			local passed = stillFlying == 0
			recordPhase("P5 heli autorotate", passed,
				abandoned .. " abandoned to Neutral, " .. destroyed ..
				" destroyed, " .. stillFlying .. " never resolved (expected 0)")
			Trigger.AfterDelay(sec(2), finalize)
		end)
	end)
end

-- ---------------------------------------------------------------------------
WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	RUSSIA = Player.GetPlayer("Russia")
	-- P5 needs Neutral by identity: a safe-landed airframe is handed to it, and
	-- that transfer is what distinguishes "resolved" from "still flying".
	NEUTRAL = Player.GetPlayer("Neutral")
	if USA == nil or RUSSIA == nil or NEUTRAL == nil then
		Test.Fail("Required players (USA, Russia, Neutral) not found")
		return
	end

	Camera.Position = cellPos(28, 16, 0)
	Trigger.AfterDelay(sec(1), phase1)
end
