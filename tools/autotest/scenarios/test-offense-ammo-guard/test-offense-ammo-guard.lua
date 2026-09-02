-- AUTO TEST — Fix 1: out-of-ammo unit is NOT recruited onto an offensive axis.
--
-- Setup (map.yaml): a USA experimental bot with a 6-abrams offense pool + one
-- EmptyTank abrams on the south flank, and enemy income structures + the enemy SR
-- so PoiOffensiveBotModule opens an attack axis and streams the pool EAST along
-- row 16. This Lua keeps EmptyTank drained to zero ammo for the WHOLE run (a unit
-- idling near the SR would otherwise slowly rearm and re-qualify).
--
-- With @experimental SkipOutOfAmmoUnits: true (the fix), EmptyTank is excluded
-- from the axis and stays parked on the flank; the 6 witnesses advance east.
--   PASS  = at least one witness moved clearly EAST (axis is live) AND EmptyTank
--           did NOT move east (it was excluded from recruitment).
--   FAIL  = EmptyTank was pulled east (recruited despite being empty) — the RED
--           case with the fix off — or no axis ever formed (harness dead).

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

local EAST_MOVED = 6   -- a witness must advance at least this many cells east
local EMPTY_MAX = 4    -- the empty tank may drift at most this many cells east
local WITNESS_SPAWN_X = 8
local WINDOW = 30      -- seconds of simulation before the verdict

WorldLoaded = function()
	local witnesses = { Witness1, Witness2, Witness3, Witness4, Witness5, Witness6 }

	TestHarness.FocusBetween(EmptyTank, EnemyFcom)

	local emptyStartX = EmptyTank.Location.X

	-- Diagnostics: track the max ammo seen AFTER the initial drain (a rearm) and the
	-- first second EmptyTank moved > EMPTY_MAX cells east, so the verdict explains why.
	local maxAmmoAfterStart = 0
	local firstMoveSec = -1

	-- Empty EmptyTank once at tick 0; the rules.yaml None override keeps it empty.
	-- Re-drain every second anyway as belt-and-suspenders.
	local function drain()
		if not EmptyTank.IsDead then
			EmptyTank.Reload("primary-ammo", -9999)
		end
	end
	drain()
	for s = 1, WINDOW do
		Trigger.AfterDelay(sec(s), function()
			-- Sample BEFORE re-draining so we catch any rearm that happened this second.
			if not EmptyTank.IsDead then
				local a = EmptyTank.AmmoCount("primary-ammo")
				if a > maxAmmoAfterStart then maxAmmoAfterStart = a end
				if firstMoveSec < 0 and (EmptyTank.Location.X - emptyStartX) > EMPTY_MAX then
					firstMoveSec = s
				end
			end
			drain()
		end)
	end

	-- Confirm the drain actually emptied the pool, else the test proves nothing.
	Trigger.AfterDelay(sec(3), function()
		if EmptyTank.IsDead then
			Test.Skip("EmptyTank died during setup — inconclusive")
			return
		end
		if EmptyTank.AmmoCount("primary-ammo") ~= 0 then
			Test.Skip("could not keep EmptyTank at zero ammo — setup precondition failed")
			return
		end
	end)

	Trigger.AfterDelay(sec(WINDOW), function()
		if EmptyTank.IsDead then
			Test.Skip("EmptyTank died before verdict — inconclusive")
			return
		end

		-- Witness liveness: at least one witness moved clearly east => an axis formed.
		local bestWitnessEast = 0
		for _, w in ipairs(witnesses) do
			if not w.IsDead then
				local east = w.Location.X - WITNESS_SPAWN_X
				if east > bestWitnessEast then bestWitnessEast = east end
			end
		end

		if bestWitnessEast < EAST_MOVED then
			Test.Fail(string.format(
				"no offensive axis observed — best witness advanced only %d cells east (need >= %d)",
				bestWitnessEast, EAST_MOVED))
			return
		end

		-- The out-of-ammo unit must NOT have been marched east.
		local emptyEast = EmptyTank.Location.X - emptyStartX
		if emptyEast > EMPTY_MAX then
			Test.Fail(string.format(
				"EmptyTank advanced %d cells east (ammo now=%d, maxAmmoAfterStart=%d, firstMoveSec=%d); guard failed",
				emptyEast, EmptyTank.AmmoCount("primary-ammo"), maxAmmoAfterStart, firstMoveSec))
			return
		end

		Test.Pass()
	end)
end
