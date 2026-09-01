--[[
  TEST: does an @experimental drone operator aim its drone at where an enemy was
  last seen, rather than at the stalest ground it could otherwise explore?

  WHAT THIS MEASURES, AND WHY IT IS A TRACE AND NOT A BOOLEAN.
  The question is a PREFERENCE, so "did a drone launch" cannot answer it — a drone
  launches either way. What separates the two hypotheses is WHERE it goes, so the
  observable is a sampled position trace of the drone actor, reduced to how much of
  its flight it spent near the vanish cell.

  Two things that look like reasonable observables and are not:

  1. IsIdle. USELESS FOR AIRCRAFT. An airframe with nothing to do is running
     FlyIdle/Hover, which IS an activity, so a parked drone never reads idle. Any
     gate written on it latches false forever.
  2. A min/max distance pair. Cannot distinguish "flew over once on the way
     somewhere else" from "flew there and stayed", which is exactly the difference
     between the two hypotheses. Hence SAMPLES, and a fraction-of-time statistic
     over them.

  ARMS. This scenario is the TREATMENT. The control is the same scenario with
  IntelSampleInterval raised beyond the run length in ai.yaml, which leaves the
  contact table empty and every candidate scoring intelSquares 0 — the pre-change
  behaviour. The control is EXPECTED TO FAIL here, and that failure is the RED.
  Do not "fix" a control-arm failure.

  Budgeted in TICKS throughout. TestHarness.TicksPerSecond says 25 and the real
  rate is 16.667 (CLAUDE.md), so any seconds conversion in this file would be
  wrong by 1.5x. The helper's own header tells new scenarios to budget in ticks.
]]

-- ===== Geometry. See map.yaml for why each distance is what it is. =====
local OperatorCell = { X = 18, Y = 45 }
local VanishCell = { X = 47, Y = 54 }

-- The scout stands ON the vanish cell's neighbour so the enemy is unambiguously
-- inside its vision, then dies to create the lost-observer condition.
local ScoutCell = { X = 47, Y = 53 }

-- A hover cell counts as "on the contact" within this many cells of the vanish
-- cell. The closest candidate the leash allows is ~11 cells from V, and grid
-- candidates are spaced 2 cells apart, so 18 admits the genuine hunt cells while
-- still excluding the whole western half of the hover disc.
local NearVanishCells = 18

-- ===== Schedule, in ticks =====
local SpawnTick = 50          -- let the world settle before anything is created
local KillScoutTick = 175     -- >= 4 BeliefStore passes (25 ticks each) at full confidence
local SampleEveryTicks = 15
local DeadlineTicks = 1800    -- ~108s at the real 16.667 t/s: first evaluation (200) + FireDelay (50)
                              -- + the 60s loiter, with slack. Only the FIRST launch is being judged.

local scout, enemy, operator
local samples = {}
local nearCount = 0
local minDist = 9999
local firstDroneTick = -1
local setupFaults = {}
local elapsed = 0

local function cellDist(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	-- Integer Euclidean, matching CVec.Length on the engine side so the numbers in
	-- the summary are comparable with DroneVisionCells and the falloff radius.
	return math.floor(math.sqrt((dx * dx) + (dy * dy)))
end

-- The drone is a separate actor from its operator, spawned by CarrierMaster on
-- launch. Found by type rather than held as a reference because it is destroyed
-- and respawned across sorties.
local function findDrone(usa)
	local found = nil
	for _, a in ipairs(usa.GetActorsByType("quadcopterdrone")) do
		if not a.IsDead and a.IsInWorld then
			found = a
			break
		end
	end

	return found
end

-- THE CONFOUND GUARD. A friendly within vision of the vanish cell makes that cell
-- currently-visible, and BeliefStore removes any contact whose cell is visible
-- (ResolveUnobserved, :263). If the bot builds something that wanders east, the
-- memory under test is erased by the bot's own economy and the run would report a
-- clean-looking negative for entirely the wrong reason. Named explicitly so that
-- cannot happen silently. The drone itself is exempt: going there is the point.
-- ASK THE SPATIAL QUESTION SPATIALLY. The obvious form — walk Player.GetActors()
-- and compare each actor's Location — ABORTS THE WHOLE SCRIPT, and did: that
-- collection includes the PLAYER ACTOR, which defines no Location, and reading a
-- property an actor does not define THROWS rather than returning nil, so an
-- `a.Location ~= nil` guard never gets the chance to fire. Map.ActorsInCircle only
-- ever returns world actors with positions, which makes the entire class of error
-- unreachable instead of merely guarded. (Its own documented pitfall — returns
-- nothing when called from WorldLoaded — does not apply: this runs only from the
-- tick, long after load.)
local function contaminatingFriendly(usa)
	local near = Map.ActorsInCircle(
		Map.CenterOfCell(CPos.New(VanishCell.X, VanishCell.Y)),
		WDist.FromCells(28),
		function(a)
			-- The drone is exempt: going there is the entire point.
			--
			-- The scout needs no exemption and deliberately gets none. This guard first
			-- runs the tick AFTER the scripted kill, so `not a.IsDead` already excludes
			-- it. An `a ~= scout` term would have been an identity comparison between two
			-- script actor wrappers — which nothing in the engine implements (__eq/Equals
			-- are absent) and which no existing scenario performs; every comparison in the
			-- corpus is against nil. Unverified semantics buying nothing.
			return a.Owner == usa
				and not a.IsDead
				and a.Type ~= "quadcopterdrone"
				and a.Type ~= "supplyroute"
		end)

	if #near > 0 then
		return near[1].Type
	end

	return nil
end

local function summary()
	local pct = 0
	if #samples > 0 then
		pct = math.floor((nearCount * 100) / #samples)
	end

	local trace = ""
	for i, s in ipairs(samples) do
		if i > 1 then trace = trace .. " " end
		trace = trace .. s.X .. ":" .. s.Y
	end

	return string.format(
		"samples=%d near(<=%dc of %d:%d)=%d (%d%%) mindist=%d firstdrone=t%d op=%d:%d || trace: %s",
		#samples, NearVanishCells, VanishCell.X, VanishCell.Y, nearCount, pct, minDist,
		firstDroneTick, OperatorCell.X, OperatorCell.Y, trace)
end

local function finish()
	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary())
		return
	end

	-- "No drone ever appeared" is NOT evidence about targeting preference, and
	-- must never be reported as though it were. It is the wiring/cost-gate class of
	-- failure that has hit this module twice, and it wants a different investigation
	-- entirely: a non-empty [drone] counter log means suspect the wiring, an empty
	-- one means the cost gate skipped the tick again.
	if firstDroneTick < 0 then
		Test.Fail("NO DRONE EVER LAUNCHED — this says nothing about targeting. Check the "
			.. "[drone] lines in debug.log: counters present => wiring; log empty => cost gate. || " .. summary())
		return
	end

	if #samples < 5 then
		Test.Fail("drone seen but only " .. #samples .. " samples — too few to call a preference || " .. summary())
		return
	end

	-- The preference criterion. A drone that merely passed overhead on its way
	-- somewhere else cannot clear a majority-of-samples bar.
	if (nearCount * 2) >= #samples then
		Test.Pass("drone spent the majority of its flight on the lost-track contact || " .. summary())
	else
		Test.Fail("drone did NOT prefer the lost-track contact || " .. summary())
	end
end

local tick
tick = function()
	elapsed = elapsed + 1

	local usa = Player.GetPlayer("USA-bot")
	if usa == nil then
		Test.Fail("USA-bot player not found")
		return
	end

	if elapsed == SpawnTick then
		-- UNARMED ON PURPOSE. An armed enemy would stamp the danger fields from its own
		-- believed contact, and the drone's hover cell is gated on AIR danger
		-- (MaxAirDanger 100) — a btr inherits ^AutoTargetHMG, and WeaponThreatensAir
		-- keys on ValidTargets containing Helicopter, so an HMG can refuse the very
		-- hunt cell this test is trying to observe. That failure would look exactly
		-- like "the feature does not work". truk has no Armament at all, so it
		-- contributes zero to both channels, and it still carries Health (via ^Vehicle,
		-- HP 10000) which BeliefStore requires before it will record a contact at all.
		-- It is Mobile, so it decays as a mobile contact — the tier under test.
		enemy = Actor.Create("truk", true, {
			Owner = Player.GetPlayer("Russia-bot"),
			Location = CPos.New(VanishCell.X, VanishCell.Y)
		})

		scout = Actor.Create("e1.america", true, {
			Owner = usa,
			Location = CPos.New(ScoutCell.X, ScoutCell.Y)
		})

		if enemy == nil or enemy.IsDead then
			setupFaults[#setupFaults + 1] = "enemy did not spawn at the vanish cell"
		end

		if scout == nil or scout.IsDead then
			setupFaults[#setupFaults + 1] = "scout did not spawn"
		end
	end

	-- Kill the OBSERVER, not the target. An enemy that merely leaves our vision is
	-- verified-clear at once; only losing the observer leaves a decaying memory.
	if elapsed == KillScoutTick then
		if scout ~= nil and not scout.IsDead then
			scout.Kill()
		else
			setupFaults[#setupFaults + 1] = "scout was already dead before the planned kill"
		end

		if enemy == nil or enemy.IsDead then
			setupFaults[#setupFaults + 1] = "enemy died before the observation window"
		end
	end

	if elapsed > KillScoutTick then
		local intruder = contaminatingFriendly(usa)
		if intruder ~= nil then
			setupFaults[#setupFaults + 1] = "friendly '" .. intruder .. "' gained vision of the vanish cell"
			finish()
			return
		end
	end

	if elapsed > KillScoutTick and (elapsed % SampleEveryTicks) == 0 then
		local drone = findDrone(usa)
		if drone ~= nil then
			if firstDroneTick < 0 then
				firstDroneTick = elapsed
			end

			local c = drone.Location
			local d = cellDist(c, VanishCell)
			samples[#samples + 1] = { X = c.X, Y = c.Y }
			if d <= NearVanishCells then
				nearCount = nearCount + 1
			end

			if d < minDist then
				minDist = d
			end
		end
	end

	if elapsed >= DeadlineTicks then
		finish()
		return
	end

	Trigger.AfterDelay(1, tick)
end

WorldLoaded = function()
	local usa = Player.GetPlayer("USA-bot")
	if usa == nil then
		Test.Fail("USA-bot player not found at load")
		return
	end

	local ops = usa.GetActorsByType("dr.america")
	if #ops == 0 then
		Test.Fail("SETUP INVALID: no drone operator (dr.america) on the map")
		return
	end

	operator = ops[1]

	-- Pin the premise the whole geometry rests on. If the operator's own 28-cell
	-- verifying bubble reaches the vanish cell, the contact is verified-clear the
	-- moment the scout dies and there is nothing left to remember — the test would
	-- then be measuring an empty table and reporting it as "no preference".
	local d = cellDist(operator.Location, VanishCell)
	if d <= 28 then
		Test.Fail(string.format(
			"SETUP INVALID: vanish cell is %d cells from the operator, inside its own 28-cell "
			.. "verifying radius — the contact can never become 'lost'", d))
		return
	end

	Trigger.AfterDelay(1, tick)
end
