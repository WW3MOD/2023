-- Phase 2 — StancePositioningExecutor (§4 Defensive).
--
-- The experimental-bot AR spawns at (13,19), a few cells SOUTH of a treeline (tank-trap density
-- row at y=16, x=10..16), with an enemy tank sighted further south at (13,22). The executor should
-- read Defensive + the sighting/threat bearing (south) + the affordance layer, and relocate the AR
-- to the treeline's THREAT-FACING (south) cover edge — a y=17 cell inside its 4-cell leash of the
-- spawn anchor — then HOLD it there indefinitely.
--
-- Two phases: (1) reach the cover edge within 15s; (2) stay on the EXACT cell for 500 ticks with no
-- oscillation (the B1 ledger / B2 slot-ownership / no-self-re-issue guarantees under test). Any drift
-- off the chosen cell after arrival fails immediately.

WorldLoaded = function()
	local unit = Rifle
	local foe = Enemy

	TestHarness.FocusBetween(unit, foe)
	TestHarness.Select(unit)

	-- Enablement is SCENARIO-LOCAL as of 2026-08-30: the shipped mod no longer grants
	-- enable-tactical-positioning to human-owned units, so this scenario's rules.yaml re-adds the
	-- token to the executor's gate AND the granter. The AR relocates to the cover edge on its own.

	-- Silence both sides WITHOUT touching the AR's fire stance. The executor deliberately declines to
	-- reposition any unit below FireAtWill (the Ambush/HoldFire "un-ambush" opt-out,
	-- StancePositioningExecutor.cs:318), so putting the unit-under-test on HoldFire — as this test used
	-- to — disables the very trait under test. The AR therefore stays FireAtWill; the enemy is silenced
	-- by HoldFire (no incoming fire ⇒ no suppression, so the S4 gate stays open) and is made
	-- non-auto-targetable in rules.yaml so the AR never acquires it. That matters twice: an attack
	-- activity would also make the AR non-idle, and the executor evaluates in TickIdle only (S5).
	if not unit.IsDead then unit.Stance = "FireAtWill" end
	if not foe.IsDead then foe.Stance = "HoldFire" end

	local ARRIVE_DEADLINE = 25 * 15   -- 15s to relocate to the cover edge
	local STABLE_TARGET = 500         -- ticks the unit must then hold the exact cell
	local HARD_TIMEOUT = 25 * 45      -- absolute safety cap

	local elapsed = 0
	local arrived = false
	local rest = nil
	local stable = 0
	local shot = false

	local poll
	poll = function()
		elapsed = elapsed + 1

		if unit.IsDead then
			Test.Fail("unit died")
			return
		end

		local loc = unit.Location

		if not arrived then
			-- Threat-facing (south) treeline edge is y==17; the 4-cell leash from anchor (13,19)
			-- keeps the reachable edge cells at x in 11..15.
			local inLeash = (math.abs(loc.X - 13) + math.abs(loc.Y - 19)) <= 4
			if loc.Y == 17 and loc.X >= 11 and loc.X <= 15 and inLeash then
				arrived = true
				rest = { X = loc.X, Y = loc.Y }
			elseif elapsed >= ARRIVE_DEADLINE then
				Test.Fail("AR did not reach a threat-facing cover edge within leash in 15s; at " ..
					loc.X .. "," .. loc.Y)
				return
			end
		else
			if loc.X ~= rest.X or loc.Y ~= rest.Y then
				Test.Fail("AR oscillated off its cover cell " .. rest.X .. "," .. rest.Y ..
					" to " .. loc.X .. "," .. loc.Y)
				return
			end

			if not shot and stable == 250 then
				Test.Screenshot("held-cover-edge",
					"expects: AR hull-down at the south treeline edge, holding, no oscillation")
				shot = true
			end

			stable = stable + 1
			if stable >= STABLE_TARGET then
				Test.Pass()
				return
			end
		end

		if elapsed >= HARD_TIMEOUT then
			Test.Fail("hard timeout; arrived=" .. tostring(arrived) .. " stable=" .. stable)
			return
		end

		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
