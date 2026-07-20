-- AUTO TEST — Fix 2: an attack-heli squad forms and launches without an hpad.
--
-- Setup (map.yaml): a USA experimental bot owns 3 HELI (AttackHeavy) near its SR;
-- there is NO hpad, so they can never rearm to full. This Lua drains a little ammo
-- from each so HelicopterSquadBotModule.IsReadyForMission (full-ammo gate) is false.
-- The enemy is a cluster of 4 t90 tanks ~31 cells east — attackable by the heli, but
-- far beyond a lone heli's auto-engage range, so a heli only moves if a SQUAD flies it.
--
-- With the experimental bypass on (module SkipRearmReadyCheck + the FSM's rearm-ready
-- gate), a squad forms, leaves idle, and the FSM Attack-orders the helis to fly east.
-- The verdict passes the instant any heli leaves its spawn (fast exit, no long run):
--   PASS = a heli advanced >= EAST_MOVED cells east (a squad launched it).
--   FAIL = no heli moved within the window (parked — the RED case, fix off).

local EAST_MOVED = 4   -- a heli must advance at least this many cells east to count as launched
local SPAWN_X = 9      -- all three helis spawn at X=9

WorldLoaded = function()
	local helis = { Heli1, Heli2, Heli3 }

	TestHarness.FocusBetween(Heli2, EnemyTank2)

	-- Knock each heli below full ammo so IsReadyForMission is false without the
	-- bypass (they keep plenty of ammo, so the squad still has something to fight with).
	local function drainOne(h)
		if not h.IsDead then
			h.Reload("secondary-ammo", -2)
			h.Reload("primary-ammo", -20)
		end
	end
	for _, h in ipairs(helis) do drainOne(h) end
	Trigger.AfterDelay(TestHarness.TicksPerSecond, function()
		for _, h in ipairs(helis) do drainOne(h) end
	end)

	-- Poll every tick; pass the moment any heli has left its spawn cell heading east.
	-- AssertWithin exits the game as soon as the predicate is true, so a passing run is
	-- only a few seconds long (and a failing RED run ends cleanly at the timeout).
	TestHarness.AssertWithin(25, function()
		for _, h in ipairs(helis) do
			if not h.IsDead and h.IsInWorld and (h.Location.X - SPAWN_X) >= EAST_MOVED then
				return true
			end
		end
		return false
	end, "no heli left spawn within 25s — squad never launched (helis parked)")
end
