-- AUTOTEST — a Stryker SHORAD must spend exactly ONE Stinger to kill one
-- helicopter, at every range inside its 28-cell engagement envelope.
--
-- THE BUG. Stinger.quad carries Burst: 2, so the SHORAD fires missiles in
-- pairs. A single Stinger (5000 damage, Penetration 20) already kills any
-- helicopter in the game outright, so whenever missile two leaves the rail
-- while missile one is still flying, it is wasted on an already-dead target.
-- With BurstDelays: 30 the pair only stopped overlapping inside ~13 cells,
-- while AutoTarget opens fire the moment a target crosses the 28-cell ring —
-- so at any realistic engagement range the SHORAD burned two missiles out of
-- a pool of eight for one kill.
--
-- WHAT IS ASSERTED, AND WHY IT IS NOT "THE HELICOPTER DIED". The helicopter
-- dies either way; a test that checked only the kill is green against the
-- broken value and measures nothing. The assertion here is on LAUNCHES —
-- specifically, how many Stingers left the rail STRICTLY BEFORE the target
-- died. Launches are counted off the secondary AmmoPool, which
-- AmmoPool.cs:307-311 decrements once per shot from INotifyAttack.Attacking
-- (per SHOT, not per burst), making AmmoCount an exact launch counter.
--
-- THE LADDER IS THE POINT. Five ranges, because the failure is
-- range-dependent and a single-range test would hide that. The old value is
-- CORRECT at short range and only breaks as range grows, so a ladder both
-- fails in the RED direction and shows exactly where the old number ran out:
--
--   straight-line flight time, from Missile.cs (see below)
--     10 cells ~24 ticks   15 ~33   20 ~41   25 ~50   28 ~55
--
--   With BurstDelays: 30 the second missile launches at tick 30, so rung 10
--   (kill at ~24) is already fine and rungs 15/20/25/27 each waste a missile.
--   With BurstDelays: 58 every rung kills before the second launch is due.
--
-- FLIGHT-TIME MODEL. Missile.cs:536 ChangeSpeed adds Acceleration (35) per
-- tick clamped to Speed (600), from MaximumLaunchSpeed (50). Cumulative
-- distance is 4950 by tick 15, 5550 by tick 16, then +600/tick. Missile.cs:1159
-- accumulates distanceCovered and :1164 detonates the tick it passes RangeLimit
-- (30c0 = 30720) — tick 58. That ceiling is why 58 was chosen: it is the
-- longest a Stinger can exist, so no geometry, and no amount of weaving after
-- a moving target, can leave one airborne when the next launches.
--
-- SCOPE LIMIT, STATED HONESTLY. The targets here are spawned stationary, so
-- this measures flight time against a still hover, which is the SHORTEST case.
-- A manoeuvring helicopter makes the intercept longer, never shorter, so a
-- green here does not prove the 58-tick figure covers a crossing target — the
-- RangeLimit ceiling argument above is what covers that, and it is arithmetic
-- from the engine source, not something this scenario measures.
--
-- NOT COVERED, AND DELIBERATELY SO: whether BurstDelays (58) sitting just
-- under BurstWait (60) trips the stale-burst rearm at Armament.cs:367. Every
-- rung here kills with missile one, so a second shot never fires and the
-- boundary is never exercised. Making a target survive would need a Health
-- override, which — per the reasoning in test-aa-battery-volleys/rules.yaml —
-- is exactly the shape of edit that silently disarms an AA scenario. The
-- boundary is argued from the source instead; see the note on Stinger.quad in
-- weapons-missiles.yaml.

local ShooterCell = { X = 12, Y = 22 }
local AirAltitude = 1280

-- Ranges in cells, east of the shooter along row 22. 27 rather than 28 so the
-- target is inside the weapon's Range: 28c0 rather than exactly on it.
local Rungs = {
	{ range = 10 },
	{ range = 15 },
	{ range = 20 },
	{ range = 25 },
	{ range = 27 },
}

local MaxLaunchesBeforeKill = 1

-- Generous: acquisition (scan interval <= 32) + turret turn + ~55 ticks of
-- flight is under 150 even at the far rung. A rung that reaches this has not
-- produced a slow result, it has produced no result, and is reported as a
-- setup fault rather than folded into the verdict.
local RungTimeoutTicks = 300

-- Strictly greater than 58, the maximum lifetime of a Stinger. Guarantees no
-- missile from rung N can still exist when rung N+1 spawns its target, which
-- would otherwise let a stray kill land on the wrong measurement.
local SettleTicks = 70

local USA, Russia
local faults = {}
local runRung, endRung

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function cellDist(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

local function fault(msg)
	table.insert(faults, msg)
end

local function finish()
	local report = {}
	local failed = {}

	for _, r in ipairs(Rungs) do
		local tag = "r" .. r.range

		if r.shooterDied then
			fault(tag .. " shooter died mid-rung")
		end
		if r.spawnFailed then
			fault(tag .. " could not spawn shooter or target")
		end
		if #r.launchTicks == 0 then
			fault(tag .. " SHORAD never launched a Stinger")
		end
		if r.deathTick == nil then
			fault(tag .. " target survived " .. RungTimeoutTicks .. " ticks")
		end
		-- 25mm.Bradley inherits ^30mm, whose ValidTargets is
		-- Infantry, Vehicle, Defense — it cannot engage an aircraft at all. If
		-- primary ammo moved anyway, the kill is not attributable to the
		-- Stinger and the launch count means nothing.
		if r.priDrop ~= nil and r.priDrop > 0 then
			fault(tag .. " 25mm fired " .. r.priDrop .. " rounds; kill not attributable to Stinger")
		end
		-- The whole ladder is indexed by range, so a shooter that CHASED was not
		-- at the range this rung claims to measure. Threshold 2 rather than 0:
		-- a cell of settle on spawn is worth ~2 ticks of flight and cannot move
		-- a launch count, while a real advance is many cells. Drift is printed
		-- for every rung regardless, so a suspicious 1-2 is still visible.
		if r.maxDrift ~= nil and r.maxDrift > 2 then
			fault(tag .. " shooter drifted " .. r.maxDrift .. " cells; range invalid")
		end

		local before = 0
		local flight = -1
		local gap = -1

		if r.deathTick ~= nil then
			for _, lt in ipairs(r.launchTicks) do
				if lt < r.deathTick then before = before + 1 end
			end
			if #r.launchTicks > 0 then
				flight = r.deathTick - r.launchTicks[1]
			end
		end

		if #r.launchTicks > 1 then
			gap = r.launchTicks[2] - r.launchTicks[1]
		end

		if before > MaxLaunchesBeforeKill then
			table.insert(failed, r.range .. "c(" .. before .. ")")
		end

		-- Read as: r<range> fire<tick of launch 1, from rung start>
		-- flight<ticks from launch 1 to kill> before<launches strictly before
		-- the kill> total<launches seen> gap<ticks between launch 1 and 2, -1
		-- if there was no second> drift<cells the shooter moved>
		table.insert(report, table.concat({
			tag,
			"fire" .. (r.launchTicks[1] or -1),
			"flight" .. flight,
			"before" .. before,
			"total" .. #r.launchTicks,
			"gap" .. gap,
			"drift" .. (r.maxDrift or -1),
		}, " "))
	end

	local summary = table.concat(report, " | ")

	if #faults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(faults, "; ") .. " || " .. summary)
		return
	end

	if #failed > 0 then
		Test.Fail("SHORAD launched a second Stinger before the kill at " .. #failed
			.. " range(s): " .. table.concat(failed, ", ")
			.. " - expected " .. MaxLaunchesBeforeKill .. " launch per kill || " .. summary)
		return
	end

	Test.Pass(summary)
end

endRung = function(i)
	local r = Rungs[i]

	if r.shooter ~= nil and not r.shooter.IsDead then
		r.priDrop = r.basePri - r.shooter.AmmoCount("primary-ammo")
		r.shooter.Destroy()
	end
	if r.target ~= nil and not r.target.IsDead then
		r.target.Destroy()
	end

	Trigger.AfterDelay(SettleTicks, function() runRung(i + 1) end)
end

runRung = function(i)
	if i > #Rungs then
		finish()
		return
	end

	local r = Rungs[i]
	r.launchTicks = {}
	r.deathTick = nil
	r.maxDrift = 0
	r.t = 0

	r.shooter = Actor.Create("strykershorad", true, {
		Owner = USA,
		Location = CPos.New(ShooterCell.X, ShooterCell.Y),
		Facing = Angle.North,
	})
	r.target = Actor.Create("halo", true, {
		Owner = Russia,
		CenterPosition = cellPos(ShooterCell.X + r.range, ShooterCell.Y, AirAltitude),
		Facing = Angle.North,
	})

	if r.shooter == nil or r.target == nil then
		r.spawnFailed = true
		endRung(i)
		return
	end

	r.startCell = r.shooter.Location
	r.baseSec = r.shooter.AmmoCount("secondary-ammo")
	r.basePri = r.shooter.AmmoCount("primary-ammo")
	r.lastSec = r.baseSec

	TestHarness.FocusBetween(r.shooter, r.target)
	TestHarness.Select(r.shooter)

	local step
	step = function()
		r.t = r.t + 1

		-- Nothing on this map can shoot back (the Halo is unarmed), but reading
		-- a property off a dead actor is a Lua error, which would kill the
		-- script and leave NO result file at all. Degrade to a recorded fault.
		if r.shooter.IsDead then
			r.shooterDied = true
			endRung(i)
			return
		end

		-- Ammo can fall by more than one between polls if two shots ever land in
		-- the same tick, so drain the difference rather than assuming one.
		local sec = r.shooter.AmmoCount("secondary-ammo")
		while sec < r.lastSec do
			r.lastSec = r.lastSec - 1
			table.insert(r.launchTicks, r.t)
		end

		local drift = cellDist(r.shooter.Location, r.startCell)
		if drift > r.maxDrift then r.maxDrift = drift end

		-- Sampled before the timeout check so a kill on the final tick still
		-- registers as a kill.
		if r.target.IsDead then
			r.deathTick = r.t
			endRung(i)
			return
		end

		if r.t >= RungTimeoutTicks then
			endRung(i)
			return
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	Russia = Player.GetPlayer("Russia")
	if USA == nil or Russia == nil then
		Test.Fail("USA or Russia player not found")
		return
	end

	runRung(1)
end
