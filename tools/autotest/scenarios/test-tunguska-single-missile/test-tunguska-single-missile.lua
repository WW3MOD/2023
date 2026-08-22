-- AUTOTEST — a Tunguska must spend exactly ONE 9M311 to kill one helicopter,
-- at every range inside its 28-cell missile envelope.
--
-- THE BUG. 9M311 (weapons-missiles.yaml:591) is three lines: `Inherits: Stinger`
-- and `BurstWait: 40`. It does not override Burst, so it takes WeaponInfo.cs:113's
-- default of 1 -- and with Burst: 1 the `--Burst < 1` branch at Armament.cs:655
-- is taken on EVERY shot, so BurstDelays is unreachable and BurstWait alone is
-- the interval between consecutive missiles. 9M311 also sets no Magazine and no
-- ReloadDelay, so there is nothing else in the chain: 40 ticks is the whole gap.
--
-- A single 9M311 (5000 damage, Penetration 20) already kills any helicopter in
-- the game outright, so whenever missile two leaves the rail while missile one
-- is still flying, it is wasted on an already-dead target -- out of a pool of
-- only eight (vehicles-russia.yaml:906).
--
-- THIS IS THE SAME BUG THE STRYKER SHORAD HAD, ARRIVING THROUGH A DIFFERENT
-- FIELD. The SHORAD fired two missiles INSIDE one burst (Burst: 2, spaced by
-- BurstDelays); the Tunguska fires two consecutive SINGLE-shot bursts (Burst: 1,
-- spaced by BurstWait). Same cause both times: an inter-shot interval shorter
-- than the missile's flight. See test-shorad-single-missile, whose shape this
-- scenario reuses deliberately.
--
-- WHAT IS ASSERTED, AND WHY IT IS NOT "THE HELICOPTER DIED". The helicopter dies
-- either way; a test that checked only the kill is green against the broken
-- value and measures nothing. The assertion here is on LAUNCHES -- specifically,
-- how many 9M311s left the rail STRICTLY BEFORE the target died. Launches are
-- counted off the secondary AmmoPool, which AmmoPool.cs:307-311 decrements once
-- per shot from INotifyAttack.Attacking (per SHOT, not per burst), making
-- AmmoCount an exact launch counter.
--
-- THE LADDER IS THE POINT. Five ranges, because the failure is range-dependent
-- and a single-range test would hide that. The broken value is CORRECT at short
-- range and only breaks as range grows, so a ladder both fails in the RED
-- direction and shows exactly where 40 ran out:
--
--   straight-line flight time, from Missile.cs (see below)
--     10 cells ~24 ticks   15 ~33   20 ~41   25 ~50   27 ~55
--
--   With BurstWait: 40 the second missile launches at tick 40, so rungs 10 and
--   15 (kills at ~24 and ~33) are already fine and rungs 20/25/27 each waste a
--   missile. With BurstWait: 58 every rung kills before the second is due.
--
-- FLIGHT-TIME MODEL. 9M311 inherits Stinger's projectile untouched.
-- Missile.cs:538 ChangeSpeed adds Acceleration (35) per tick clamped to Speed
-- (600), from MaximumLaunchSpeed (50). Cumulative distance is 4950 by tick 15,
-- 5550 by tick 16, then +600/tick. Missile.cs:1159 accumulates distanceCovered
-- and :1164 detonates the tick it passes RangeLimit (30c0 = 30720) -- tick 58,
-- at 30750. ExplodeWhenEmpty defaults true (Missile.cs:120) and Stinger sets it
-- explicitly, so that cull really applies. 58 is therefore the longest a 9M311
-- can exist, which is why it was chosen: no geometry, and no amount of weaving
-- after a moving target, can leave one airborne when the next launches.
--
-- All tick figures above are engine ticks. The mod runs at Timestep: 60
-- (mod.yaml:381) = 16.67 ticks/second, so 40 ticks is 2.4 s and 58 is 3.48 s.
--
-- SCOPE LIMIT, STATED HONESTLY. The targets here are spawned stationary, so this
-- measures flight time against a still hover, which is the SHORTEST case. A
-- manoeuvring helicopter makes the intercept longer, never shorter, so a green
-- here does not prove the 58-tick figure covers a crossing target -- the
-- RangeLimit ceiling argument above is what covers that, and it is arithmetic
-- from the engine source, not something this scenario measures.
--
-- SECOND SCOPE LIMIT, INHERITED FROM THE SHORAD SCENARIO. If missile one ever
-- MISSED, it would self-destruct at tick 58 without killing, missile two would
-- launch legitimately, and this test would record before=2 and fail. That makes
-- a red rung ambiguous between "fired too early" and "missed" -- which is what
-- the per-rung `flight` and `gap` figures in the summary are for: a genuine
-- early launch shows gap == the configured interval, a miss shows flight > 58.

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
-- produced a slow result, it has produced no result, and is reported as a setup
-- fault rather than folded into the verdict.
local RungTimeoutTicks = 300

-- Strictly greater than 58, the maximum lifetime of a 9M311. Guarantees no
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
			fault(tag .. " Tunguska never launched a 9M311")
		end
		if r.deathTick == nil then
			fault(tag .. " target survived " .. RungTimeoutTicks .. " ticks")
		end
		-- rules.yaml restricts AttackTurreted to `secondary`, so the 30mm cannon
		-- should never acquire. If primary ammo moved anyway that override did not
		-- apply, the cannon contributed to the kill, and the launch count means
		-- nothing -- 30mm.Tunguska.AA reaches 18c0, covering the 10c and 15c rungs.
		if r.priDrop ~= nil and r.priDrop > 0 then
			fault(tag .. " 30mm fired " .. r.priDrop .. " rounds; kill not attributable to 9M311")
		end
		-- The whole ladder is indexed by range, so a shooter that CHASED was not at
		-- the range this rung claims to measure. Threshold 2 rather than 0: a cell
		-- of settle on spawn is worth ~2 ticks of flight and cannot move a launch
		-- count, while a real advance is many cells. Drift is printed for every
		-- rung regardless, so a suspicious 1-2 is still visible.
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

		-- Read as: r<range> fire<tick of launch 1, from rung start> flight<ticks
		-- from launch 1 to kill> before<launches strictly before the kill>
		-- total<launches seen> gap<ticks between launch 1 and 2, -1 if there was
		-- no second> drift<cells the shooter moved> tdrift<cells the target moved>
		table.insert(report, table.concat({
			tag,
			"fire" .. (r.launchTicks[1] or -1),
			"flight" .. flight,
			"before" .. before,
			"total" .. #r.launchTicks,
			"gap" .. gap,
			"drift" .. (r.maxDrift or -1),
			"tdrift" .. (r.maxTargetDrift or -1),
		}, " "))
	end

	local summary = table.concat(report, " | ")

	if #faults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(faults, "; ") .. " || " .. summary)
		return
	end

	if #failed > 0 then
		Test.Fail("Tunguska launched a second 9M311 before the kill at " .. #failed
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
	r.maxTargetDrift = 0
	r.t = 0

	-- Fresh shooter per rung. Reusing one would carry its 8-missile pool across
	-- rungs, and in the RED direction (two missiles per rung) that pool runs dry
	-- partway through the ladder.
	r.shooter = Actor.Create("tunguska", true, {
		Owner = Russia,
		Location = CPos.New(ShooterCell.X, ShooterCell.Y),
		Facing = Angle.North,
	})
	r.target = Actor.Create("TRAN", true, {
		Owner = USA,
		CenterPosition = cellPos(ShooterCell.X + r.range, ShooterCell.Y, AirAltitude),
		Facing = Angle.North,
	})

	if r.shooter == nil or r.target == nil then
		r.spawnFailed = true
		endRung(i)
		return
	end

	r.startCell = r.shooter.Location
	r.targetStartCell = r.target.Location
	r.baseSec = r.shooter.AmmoCount("secondary-ammo")
	r.basePri = r.shooter.AmmoCount("primary-ammo")
	r.lastSec = r.baseSec

	TestHarness.FocusBetween(r.shooter, r.target)
	TestHarness.Select(r.shooter)

	local step
	step = function()
		r.t = r.t + 1

		-- The Chinook is unarmed so nothing on this map can shoot back, but reading
		-- a property off a dead actor is a Lua error, which would kill the script
		-- and leave NO result file at all. Degrade to a recorded fault.
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

		-- Diagnostic only, deliberately not a fault. A spawned Chinook should hover
		-- where it was placed, but if it ever drifts, intercept time grows and this
		-- is the number that explains an anomalous rung.
		local tdrift = cellDist(r.target.Location, r.targetStartCell)
		if tdrift > r.maxTargetDrift then r.maxTargetDrift = tdrift end

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
