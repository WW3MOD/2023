-- AUDIT 6.2 — "stationary tank, maximal offset re-roll".
--
-- ATGM's Inaccuracy is 512 Absolute, so the aim point is displaced by a flat horizontal offset at
-- every range, re-rolled every 5 ticks while the missile is further than 1536 wdist from its
-- target and frozen inside that. The audit's section 3.1 arithmetic says a missile can null a
-- lateral aim shift of X only if it still has D >= sqrt(1630*X) to run — and for the largest
-- possible opposed re-roll (1448) that threshold is 1537, one wdist outside the freeze radius.
-- A maximal opposed re-roll taken on the last eligible tick is therefore right on the edge of
-- producing a miss larger than the 298 fuse radius against a completely stationary target.
--
-- This is a tail event that cannot be forced without editing the weapon, so the scenario just
-- fires a lot of rounds and varies the geometry. Each lane sits at a different launch range across
-- 6c0-8c0, which changes the tick on which the missile crosses 1536 relative to its own 5-tick
-- re-roll cadence — the only deterministic axis available. Everything else is left alone: this is
-- the shipped Javelin against the same stationary t90 the existing 39-record corpus used, so the
-- results are directly comparable.
--
-- Success signature: ANY record with min_aim_dist > 298. Across the whole shipped corpus the
-- maximum is 6, so a single one is significant.

-- (dx, dy) in cells from the launcher's cell. Launch range is measured from the MUZZLE, which sits
-- half a cell downrange, so the effective separation is dx*1024 - 500. These eight are chosen to be
-- distinct and to span the audit's 6c0-8c0 band: 6426, 6668, 6746, 6976, 7343, 7692, 7760, 7960.
local OFFSETS = { { 6, 3 }, { 7, 0 }, { 7, 1 }, { 7, 2 }, { 7, 3 }, { 8, 0 }, { 8, 1 }, { 8, 2 } }
local COLUMNS = { 4, 34 }
local ROWS = { 6, 13, 20, 27 }

local RunSeconds = 150
local MinMissiles = 100

local USA, RUSSIA
local lanes = {}

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	if not Test.IsMissileTraceEnabled() then
		Test.Fail("MissileTrace is off — this scenario's entire output is the .missiles.jsonl, so " ..
			"run it with tools/autotest/run-test.sh --missile-trace")
		return
	end

	for i = 1, 8 do
		local lx = COLUMNS[math.floor((i - 1) / 4) + 1]
		local row = ROWS[((i - 1) % 4) + 1]
		local off = OFFSETS[i]

		-- PITFALL: a GROUND actor must be created with `Location`; created with `CenterPosition` it
		-- exists and reports alive but no ground weapon will engage it.
		local target = Actor.Create("t90", true, {
			Owner = RUSSIA,
			Location = CPos.New(lx + off[1], row + off[2]),
			Facing = Angle.East,
		})

		local launcher = Actor.Create("at", true, {
			Owner = USA,
			Location = CPos.New(lx, row),
			Facing = Angle.East,
		})

		if target == nil or launcher == nil then
			Test.Fail("lane " .. i .. " failed to spawn")
			return
		end

		-- Silence the ENEMY, never the unit under test (AUTOTEST.md gotcha 7).
		target.Stance = "HoldFire"
		lanes[i] = { target = target, launcher = launcher, fireIn = 0 }
	end

	TestHarness.FocusBetween(lanes[1].target, lanes[8].target)

	local step
	step = function()
		for _, lane in ipairs(lanes) do
			if lane.fireIn > 0 then
				lane.fireIn = lane.fireIn - 1
			elseif not lane.launcher.IsDead and not lane.target.IsDead and lane.launcher.IsIdle then
				lane.launcher.Attack(lane.target, false, true)
				lane.fireIn = 10
			end
		end

		Trigger.AfterDelay(1, step)
	end
	step()

	-- Control on the rig, not on the hypothesis: fail if the lanes did not actually shoot, because
	-- a trace with no ATGM records in it would otherwise read as "no wide misses observed".
	Trigger.AfterDelay(RunSeconds * TestHarness.TicksPerSecond, function()
		local n = Test.GetMissileRecordCount()
		if n < MinMissiles then
			Test.Fail(string.format("only %d missile records (need >= %d) — an empty trace is NOT " ..
				"evidence that the offset tail does not exist", n, MinMissiles))
			return
		end

		Test.Pass(string.format("%d missile records across %d stationary-t90 lanes", n, #lanes))
	end)
end
