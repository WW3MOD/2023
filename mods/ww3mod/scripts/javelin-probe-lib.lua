-- Shared rig for the Javelin terminal-geometry probes (WORKSPACE/audit/javelin-terminal-geometry.md
-- sections 6.1 and 6.3). Both scenarios are the same eight-lane sweep and differ only in what they
-- do to the target when the missile crosses the lane's trigger range, so the machinery lives here
-- and each scenario supplies a perturbation and a set of trigger ranges.
--
-- THE POINT OF KEYING OFF MEASURED RANGE. The audit specifies the perturbation by the missile's
-- REMAINING DISTANCE (800-2000 wdist), not by a delay after launch. Those are not interchangeable:
-- the ATGM launches at 100 and accelerates by 30/tick to 300 while also climbing, so ticks-to-
-- intercept depends on launch geometry. Test.GetLiveMissileRange reads the live missile's true
-- separation from its target (Missile.cs `currentDistance`), which is the quantity the audit's
-- correction-budget arithmetic is written in.
--
-- LANE LAYOUT. Two columns x eight rows is impossible on a 64x32 playfield once each lane needs
-- >= 20 cells of empty downrange (audit 6.1), so it is two columns x four rows:
--
--     column A: launcher x=4,  target track x=9    -> 55 cells of clear downrange
--     column B: launcher x=34, target track x=39   -> 25 cells of clear downrange
--     rows:     y = 6, 13, 20, 27, patrolled +/- 3
--
-- ENGAGEMENT RANGE. Launch position is the MUZZLE, which sits half a cell downrange of the AT's
-- own cell, so a five-cell separation is 4620 wdist of actual launch range, not 5120. With the
-- Humvee patrolling +/- 3 cells across the line of sight the range runs 4620-5726, which is inside
-- the audit's 4c0-6c0 band at both ends. Three cells is also the widest patrol that stays in band:
-- at +/- 4 the crossing geometry pushes launch range past 6144.
--
-- A surviving column-A missile fuels out around x=25, nine cells short of column B's launchers, so
-- the columns cannot contaminate each other. Terrain is flat by construction, not by map authoring:
-- this mod's MapGrid declares no MaximumTerrainHeight, so Map.cs clamps every cell height to 0 and
-- no map in ww3mod has relief at all. That satisfies the audit's "flat, single height level"
-- requirement structurally and removes the ground clause as a confounder.

JavelinProbe = {}

JavelinProbe.Columns = { { launcher = 4, track = 9 }, { launcher = 34, track = 39 } }
JavelinProbe.Rows = { 6, 13, 20, 27 }

-- A scenario may replace the columns to change engagement range; lane count is 4 per column.
function JavelinProbe.SetColumns(cols)
	JavelinProbe.Columns = cols
end

function JavelinProbe.LaneCount()
	return #JavelinProbe.Columns * #JavelinProbe.Rows
end

local USA, RUSSIA
local lanes = {}
local traceOk = false
local sweepIn = 0

local function horDist(a, b)
	local dx, dy = a.X - b.X, a.Y - b.Y
	return math.floor(math.sqrt(dx * dx + dy * dy))
end

-- The Humvee is left at its shipped 8000 HP and simply replaced when a missile kills it, rather
-- than given the usual test-rig health override. `humvee` carries TWO RenderSprites blocks in
-- vehicles-america.yaml (lines 28 and 156), and MiniYaml.Merge rejects duplicate sibling keys the
-- moment a second rules source mentions that actor — so ANY map rules node for `humvee` makes the
-- map fail to load. Recorded in WORKSPACE/bugs/discovered.md; not fixed here, because collapsing
-- the two blocks would drop the actor from two RenderSprites traits to one and that is a live
-- rendering change to shipped content, not a measurement-rig concern.
local function spawnTarget(lane)
	local target = Actor.Create("humvee", true, {
		Owner = RUSSIA,
		Location = CPos.New(lane.trackX, lane.row - lane.half),
		Facing = Angle.North,
	})

	-- Silence the ENEMY, never the unit under test (AUTOTEST.md gotcha 7).
	target.Stance = "HoldFire"
	lane.target = target
	lane.moveDir = -1
	lane.lastPos = target.CenterPosition
	return target
end

-- Husks and ejected crew from killed Humvees would otherwise pile up on the five-cell patrol track
-- and wall it off within a couple of dozen shots, quietly turning a moving-target lane into a
-- stationary one.
--
-- PITFALL: this is a whitelist, not "everything Russia owns that is not a lane target". Player
-- traits live on an invisible per-player actor that Player.GetActors() returns alongside real
-- units, so a blacklist sweep destroys the Russia player itself and the next warhead to run
-- crashes the game with "Attempted to get trait from destroyed object (player 2)".
local function isDebris(actorType)
	return string.find(actorType, "%.husk$") ~= nil or string.find(actorType, "^crew%.") ~= nil
end

local function sweepDebris()
	for _, a in ipairs(RUSSIA.GetActors()) do
		if not a.IsDead and isDebris(a.Type) then
			a.Destroy()
		end
	end
end

-- Lane index -> (column, row). Lanes 1-4 are column A top to bottom, 5-8 column B. The trace
-- records launch_pos, so this mapping is what the analysis script inverts to recover each
-- missile's trigger range from where its launcher stood.
function JavelinProbe.LaneCell(i)
	local nrows = #JavelinProbe.Rows
	local col = JavelinProbe.Columns[math.floor((i - 1) / nrows) + 1]
	local row = JavelinProbe.Rows[((i - 1) % nrows) + 1]
	return col, row
end

-- `triggerRanges[i] == 0` marks a CONTROL lane: identical geometry, identical target motion, no
-- perturbation ever applied. Without it a survival fingerprint anywhere in the sweep proves
-- nothing, because the sweep would have no arm that was supposed to stay negative.
function JavelinProbe.Build(triggerRanges, patrolHalfCells)
	USA = Player.GetPlayer("USA")
	RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return false
	end

	traceOk = Test.IsMissileTraceEnabled()
	if not traceOk then
		Test.Fail("MissileTrace is off, so Test.GetLiveMissileRange returns -1 forever and no " ..
			"perturbation would ever fire — run with tools/autotest/run-test.sh --missile-trace")
		return false
	end

	for i = 1, JavelinProbe.LaneCount() do
		local col, row = JavelinProbe.LaneCell(i)
		local half = patrolHalfCells

		local launcher = Actor.Create("at", true, {
			Owner = USA,
			Location = CPos.New(col.launcher, row),
			Facing = Angle.East,
		})

		if launcher == nil then
			Test.Fail("lane " .. i .. " failed to spawn its launcher")
			return false
		end

		lanes[i] = {
			index = i,
			trackX = col.track,
			row = row,
			half = half,
			trigger = triggerRanges[i],
			target = nil,
			launcher = launcher,
			respawns = 0,
			-- Starts at -1 so the first patrol flip sends the humvee to row+half rather than to the
			-- cell it was spawned on.
			moveDir = -1,
			fireIn = 0,
			lastId = -1,
			perturbs = 0,
			holdFor = 0,
			postIn = 0,
			preVel = 0,
			velPre = 0,
			velPost = 0,
			velSamples = 0,
			lastPos = WPos.New(0, 0, 0),
		}

		-- PITFALL (inherited from test-missile-latch-probe): a GROUND actor must be created with
		-- `Location`. Created with `CenterPosition` it exists and reports alive but no ground
		-- weapon will engage it.
		if spawnTarget(lanes[i]) == nil then
			Test.Fail("lane " .. i .. " failed to spawn its target")
			return false
		end
	end

	TestHarness.FocusBetween(lanes[1].target, lanes[#lanes].target)
	return true
end

local function patrol(lane)
	if lane.target.IsDead or not lane.target.IsIdle then
		return
	end

	-- Idle means the humvee reached its waypoint (or was just stopped), so flip and drive back.
	-- Re-issuing a Move on a fixed cadence instead would restart the activity mid-leg and inject
	-- an uncontrolled velocity change, which is the very thing the sweep is measuring.
	lane.moveDir = -lane.moveDir
	lane.target.Move(CPos.New(lane.trackX, lane.row + lane.half * lane.moveDir))
end

function JavelinProbe.Tick(perturb)
	-- One hit kills a stock Humvee, so most shots end their target. Replacing it immediately keeps
	-- every lane firing for the whole run.
	--
	-- The debris sweep walks the whole Russia actor list, so it runs every fifth tick rather than
	-- every tick: often enough that a husk cannot survive long enough to make the replacement Humvee
	-- path around it (which drifts the engagement range out of the audit's band), cheap enough that
	-- the run finishes inside its wall-clock timeout.
	if sweepIn > 0 then
		sweepIn = sweepIn - 1
	else
		sweepDebris()
		sweepIn = 4
	end

	for _, lane in ipairs(lanes) do
		if lane.target.IsDead then
			lane.respawns = lane.respawns + 1
			lane.holdFor = 0
			lane.postIn = 0
			spawnTarget(lane)
		else
			local pos = lane.target.CenterPosition
			local vel = horDist(pos, lane.lastPos)
			lane.lastPos = pos

			-- Velocity control: the pre-order speed and the speed eight ticks later. A perturbation
			-- that did not move the needle here did not change the lead term either, so a null
			-- result from this rig would be a null result about nothing.
			if lane.postIn > 0 then
				lane.postIn = lane.postIn - 1
				if lane.postIn == 0 then
					lane.velPre = lane.velPre + lane.preVel
					lane.velPost = lane.velPost + vel
					lane.velSamples = lane.velSamples + 1
				end
			end

			local d = Test.GetLiveMissileRange(lane.target)
			local id = Test.GetLiveMissileNearestId(lane.target)

			-- AtSpeed gates the perturbation on the Humvee actually moving near its cap. Measured top
			-- speed on clear terrain is 105 wdist/tick, not the 150 its Mobile.Speed reads, and over a
			-- short patrol leg the median is under half of that — so an ungated trigger would fire
			-- most of its samples at a target that had barely any lead term to collapse, and a null
			-- result would say nothing about the audit's arithmetic.
			local atSpeed = vel >= 80

			if d >= 0 and lane.trigger > 0 and d <= lane.trigger and id ~= lane.lastId and atSpeed then
				lane.lastId = id
				lane.perturbs = lane.perturbs + 1
				lane.preVel = vel
				lane.postIn = 8
				lane.holdFor = 12
				perturb(lane)
			elseif lane.holdFor > 0 then
				lane.holdFor = lane.holdFor - 1
			else
				patrol(lane)
			end

			-- The AT holds one round and the pool refills on a timer, so it fires exactly one
			-- missile per order and cannot put two in the air at once. When the pool is dry the
			-- armament is paused and the attack activity ends, which leaves the launcher idle —
			-- hence the re-press, throttled so a dry launcher is not re-ordered every tick.
			if lane.fireIn > 0 then
				lane.fireIn = lane.fireIn - 1
			elseif lane.launcher ~= nil and not lane.launcher.IsDead and lane.launcher.IsIdle then
				lane.launcher.Attack(lane.target, false, true)
				lane.fireIn = 10
			end
		end
	end
end

-- The verdict is a CONTROL on the rig, not on the hypothesis: it fails when the rig did not fire,
-- did not perturb, or perturbed without changing the target's speed. The answer to the audit's
-- question lives in the .missiles.jsonl, not here.
function JavelinProbe.Report(minMissiles)
	local n = Test.GetMissileRecordCount()
	if n < minMissiles then
		Test.Fail(string.format("only %d missile records (need >= %d) — the lanes did not shoot, " ..
			"so an empty trace is NOT evidence", n, minMissiles))
		return
	end

	local parts = {}
	local totalPerturbs, totalSamples, sumPre, sumPost = 0, 0, 0, 0
	for _, lane in ipairs(lanes) do
		totalPerturbs = totalPerturbs + lane.perturbs
		totalSamples = totalSamples + lane.velSamples
		sumPre = sumPre + lane.velPre
		sumPost = sumPost + lane.velPost
		table.insert(parts, string.format("L%d@%d[n=%d,kills=%d]", lane.index, lane.trigger,
			lane.perturbs, lane.respawns))
	end

	if totalPerturbs == 0 then
		Test.Fail(string.format("%d missiles flew but not one perturbation fired — the trigger " ..
			"ranges were never crossed, so every lane ran as an unperturbed control", n))
		return
	end

	if totalSamples > 0 then
		local pre = sumPre / totalSamples
		local post = sumPost / totalSamples
		if math.abs(pre - post) < 10 then
			Test.Fail(string.format("%d perturbations fired but target speed was unchanged " ..
				"(%.0f -> %.0f wdist/tick) — the order did not alter the lead term",
				totalPerturbs, pre, post))
			return
		end

		Test.Pass(string.format("%d missiles, %d perturbations, target speed %.0f -> %.0f " ..
			"wdist/tick over %d samples; %s",
			n, totalPerturbs, pre, post, totalSamples, table.concat(parts, " ")))
		return
	end

	Test.Pass(string.format("%d missiles, %d perturbations; %s", n, totalPerturbs,
		table.concat(parts, " ")))
end

function JavelinProbe.Drive(perturb, runSeconds, minMissiles)
	local step
	step = function()
		JavelinProbe.Tick(perturb)
		Trigger.AfterDelay(1, step)
	end
	step()

	Trigger.AfterDelay(runSeconds * TestHarness.TicksPerSecond, function()
		JavelinProbe.Report(minMissiles)
	end)
end
