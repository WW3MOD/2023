-- AUTO TEST: what does an AIRCRAFT do when its target goes doomed MID ATTACK-RUN?
--
-- The break-off guard added to AttackFollow.Tick (merged 936f1fe9) clears a locked
-- RequestedTarget the moment that target acquires critical-damage. Its population is
-- everything deriving from AttackFollow, and AttackAircraft (11 declarations) has no
-- Tick override, so the guard fires for aircraft too. The turreted half was proven
-- with a RED/GREEN pair. The AIRCRAFT half shipped unobserved, and it is the half
-- with room to be ugly: an airframe is mid-manoeuvre when the target is dropped, and
-- nothing in FlyAttack was written with that in mind.
--
-- WHAT THE CODE PREDICTS, and why the two lanes are the two lanes.
-- Activity.TickOuter:123-126 -- with ChildHasPriority (FlyAttack's default) the parent
-- tick is short-circuited by `TickChild(self) && ...`, so FlyAttack.Tick DOES NOT RUN
-- while a child activity is alive. FlyAttack's own abort check for exactly this
-- situation ("Check that AttackFollow hasn't cancelled the target", FlyAttack.cs:99-101)
-- lives in that tick. Therefore:
--
--   MI28  AttackType: Hover, CanHover -- in range, FlyAttack queues no run child, so
--         FlyAttack.Tick runs essentially every tick and the abort check is live.
--         Predict: near-immediate clean abort.
--   A10   AttackType: Default, !CanHover -- FlyAttack queues MoveWithinRange, then
--         FlyAttackRun (Fly-in, FlyForward 1, Fly-out). Those are children, so the
--         abort check cannot be consulted until the run ENDS. FlyAttackRun.Tick only
--         self-cancels when the target becomes INVALID or has no valid weapons
--         (FlyAttack.cs:263-265); a critically damaged target is neither.
--         Predict: the pass is flown to completion with the guns silent, and the
--         activity only ends afterwards.
--
-- They also differ on the second axis the merge commit flagged as load-bearing:
-- A10 leaves PersistentTargeting at its default TRUE, so ClearRequestedTarget PROMOTES
-- the doomed target to OpportunityTarget (AttackFollow.cs:69-79) and relies on the
-- opportunity guard in the else branch to undo it the same tick. MI28 pins
-- PersistentTargeting: false and takes the plain-clear path. If the promotion ever
-- survives, the A10 lane is where it shows.
--
-- INFERENCE, NOT OBSERVATION (stated so it cannot be read as measured): the other
-- nine AttackAircraft actors are not in this scenario. F16/FROG/MIG share A10's
-- Default/!CanHover shape; littlebird/HELI/HIND share MI28's Hover shape.
-- A10.Airstrike and FROG.Airstrike are AttackType: Strafe and are a THIRD shape that
-- neither lane covers -- see the note in WORKSPACE/DISCOVERIES.md.
--
-- THE OBSERVABLE, and what could make it RED.
-- shotsAfterCritical = ticks on which the aircraft's total ammo DECREASED after its
-- target was driven under 25% health. Decrements, not net, because both airframes
-- carry ReloadAmmoPool and a refill would otherwise mask a shot.
-- RED arm = comment out the break-off guard in AttackFollow.Tick and rebuild. It has to
-- be the engine edit rather than a YAML `BreakOffCondition:` switch, because the A10
-- cannot be overridden from map rules at all right now — see the note in rules.yaml.
--
-- WHY A GREEN HERE IS NOT VACUOUS. Four ways a zero could be manufactured rather than
-- earned, each checked as a setup fault before the verdict is trusted:
--   * the aircraft never engaged at all      -> firedBeforeCritical must be true
--   * it ran out of ammo                     -> ammo must be > 0 at the end
--   * the target died, so there was nothing left to shoot -> target must be alive
--   * the manipulation never took            -> target hp must be < 25% at the end
-- Plus a contamination guard: 44 cells separate the lanes, and an aircraft that
-- wanders within 20 cells of the OTHER lane's target invalidates its own ammo trace.

local ArmDeadlineTicks = 400    -- must have opened fire by here, or the setup is void
local ObserveTicks = 250        -- watch window after the target goes critical
local CriticalFraction = 15     -- % of max health; inside Critical's <25% band
local ContaminationCells = 20

-- Shots landing within GraceTicks of the trigger are RECORDED but not failed on.
-- Measured 2026-08-27: shipped behaviour leaks exactly 2 shot-ticks on the A10 lane
-- (firstMiss0 lastMiss1 -- the first two ticks and nothing for the remaining ~248) and
-- ZERO on the Mi-28. Two ticks is 0.12s at the mod's 16.67 tps: the trigger fires from
-- Lua inside the world tick, so the trait may already have run its DoAttack for that
-- tick with pre-doom knowledge, and ordnance already released cannot be recalled.
-- Failing on that would leave a permanently-red scenario over a latency nobody can act
-- on. It does NOT weaken the gate: in the RED arm the lanes took 16 and 10 shot-ticks,
-- and at most one decrement per lane per tick fits in the grace, so at least 14 and 8
-- of them necessarily fall outside it. The grace cannot hide a disabled guard.
local GraceTicks = 2

local Lanes = {
	{
		id = "A10",
		unit = "a10",
		ax = 10, ay = 6, alt = 2560,
		tx = 10, ty = 20,
	},
	{
		id = "MI28",
		unit = "mi28",
		ax = 54, ay = 6, alt = 1280,
		tx = 54, ty = 20,
	},
}

local setupFaults = {}

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

local function cellDist(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

local function totalAmmo(a)
	if a.IsDead then return 0 end
	return a.AmmoCount("primary-ammo") + a.AmmoCount("secondary-ammo")
end

local function addFault(s)
	table.insert(setupFaults, s)
end

local function finish()
	local report = {}
	local totalShotsAfter = 0

	for _, l in ipairs(Lanes) do
		totalShotsAfter = totalShotsAfter + l.shotsAfterGrace

		local hpPct = -1
		if not l.target.IsDead then
			hpPct = math.floor(l.target.Health * 100 / l.target.MaxHealth)
		end

		if l.plane.IsDead then addFault(l.id .. " aircraft died") end
		if l.target.IsDead then addFault(l.id .. " target died") end
		if not l.plane.IsDead and totalAmmo(l.plane) <= 0 then
			addFault(l.id .. " aircraft ended dry - a silent gun proves nothing")
		end
		if not l.target.IsDead and hpPct >= 25 then
			addFault(l.id .. " target ended at hp" .. hpPct .. "%, outside the <25% Critical band")
		end
		if not l.firedBefore then
			addFault(l.id .. " never opened fire before the trigger - no engagement to break off")
		end
		if l.contaminated then
			addFault(l.id .. " came within " .. ContaminationCells .. " cells of the other lane's target")
		end

		table.insert(report, table.concat({
			l.id,
			"shotsBefore" .. l.shotsBefore,
			"SHOTSAFTER" .. l.shotsAfter,
			"postGrace" .. l.shotsAfterGrace,
			"distAtCrit" .. l.distAtCritical,
			"minDistAfter" .. l.minDistAfter,
			"maxDistAfter" .. l.maxDistAfter,
			"finalDist" .. l.finalDist,
			"ticksToIdle" .. l.ticksToIdle,
			"idleSpans" .. l.idleSpans,
			"reEngage" .. l.reEngagements,
			"firstMiss" .. l.firstMissTick,
			"lastMiss" .. l.lastMissTick,
			"diedTick" .. l.diedTick,
			"trace[" .. table.concat(l.trace, ",") .. "]",
			"hpAtCrit" .. l.hpAtCritical .. "%",
			"tgtHp" .. hpPct .. "%",
			"ammoLeft" .. totalAmmo(l.plane),
			-- Altitude is pure diagnosis. Nothing in the suite has ever spawned a
			-- fixed-wing airborne, so if the A10 lane fails mutely these two numbers say
			-- whether it was flying at all. Both lanes are created at exactly their own
			-- CruiseAltitude (A10 2560, Mi-28 the 1280 engine default), which is what
			-- Aircraft.AddedToWorld:397-401 needs to mark them airborne AND cruising on
			-- the tick they appear.
			"altAtCrit" .. l.altAtCritical,
			"finalAlt" .. l.finalAlt,
		}, " "))
	end

	local summary = table.concat(report, " | ")

	-- Order matters. The break-off verdict is read FIRST so the RED arm reports the
	-- mechanism rather than the collateral (a target the un-guarded aircraft shot to
	-- death would otherwise surface as "target died" and hide why).
	if totalShotsAfter > 0 then
		Test.Fail("BREAK-OFF DID NOT FIRE: " .. totalShotsAfter
			.. " shots taken at a critically damaged target after break-off"
			.. " (beyond a " .. GraceTicks .. "-tick grace) || " .. summary)
		return
	end

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	Test.Pass(summary)
end

local function observeTick(l)
	if l.plane.IsDead then return end

	local ammo = totalAmmo(l.plane)
	if ammo < l.prevAmmo then
		l.shotsAfter = l.shotsAfter + 1
		if l.observed >= GraceTicks then l.shotsAfterGrace = l.shotsAfterGrace + 1 end
		if l.firstMissTick < 0 then l.firstMissTick = l.observed end
		l.lastMissTick = l.observed
	end
	l.prevAmmo = ammo

	if l.diedTick < 0 and l.target.IsDead then l.diedTick = l.observed end

	local here = l.plane.Location
	local d = cellDist(here, l.targetCell)
	if d < l.minDistAfter then l.minDistAfter = d end
	if d > l.maxDistAfter then l.maxDistAfter = d end
	l.finalDist = d
	l.finalAlt = l.plane.CenterPosition.Z

	if cellDist(here, l.otherTargetCell) < ContaminationCells then
		l.contaminated = true
	end

	local idle = l.plane.IsIdle
	if idle and not l.wasIdle then
		l.idleSpans = l.idleSpans + 1
		if l.ticksToIdle < 0 then l.ticksToIdle = l.observed end
	elseif not idle and l.wasIdle then
		l.reEngagements = l.reEngagements + 1
	end
	l.wasIdle = idle

	-- The flight TRACE is the instrument that actually answers the question. IsIdle
	-- turned out to be useless for aircraft (never true in either arm -- an idle
	-- airframe is running FlyIdle/Hover, which is an activity, so IsIdle never goes
	-- true), and a min/max pair cannot tell "flew over once and left" from "flew over,
	-- came back, flew over again". A distance sample every 25 ticks can.
	if l.observed % 25 == 0 then
		table.insert(l.trace, d)
	end

	l.observed = l.observed + 1
end

local function observePhase()
	local remaining = ObserveTicks
	local step
	step = function()
		for _, l in ipairs(Lanes) do
			observeTick(l)
		end
		remaining = remaining - 1
		if remaining <= 0 then
			finish()
		else
			Trigger.AfterDelay(1, step)
		end
	end
	Trigger.AfterDelay(1, step)
end

-- Phase A: let both aircraft engage normally, and the instant a lane's aircraft has
-- actually fired, drive THAT lane's target under 25%. Keying the trigger on a real
-- shot is what makes "mid attack-run" true rather than assumed: the aircraft is
-- demonstrably committed and discharging when the target becomes doomed.
local function armPhase()
	local elapsed = 0
	local step
	step = function()
		local allArmed = true

		for _, l in ipairs(Lanes) do
			-- A lane that armed on an earlier tick is already past its trigger, so it is
			-- observed from here rather than left unwatched until the slower lane catches
			-- up. Without this its shots during the wait collapse into a single decrement
			-- against a stale prevAmmo, undercounting exactly the RED arm's evidence.
			if l.armed then
				observeTick(l)
			else
				if l.plane.IsDead or l.target.IsDead then
					addFault(l.id .. " died during the arming phase")
					l.armed = true
					l.distAtCritical = -1
				else
					local ammo = totalAmmo(l.plane)
					if ammo < l.prevAmmo then
						l.shotsBefore = l.shotsBefore + 1
						l.firedBefore = true
					end
					l.prevAmmo = ammo

					if l.firedBefore then
						l.target.Health = math.floor(l.target.MaxHealth * CriticalFraction / 100)

						-- SAME-TICK CONTROL. Read the fraction back on the tick the clock
						-- starts, not merely at the end. There is no Lua API for
						-- GetConditionCount, so the damage fraction is the closest available
						-- proxy for "critical-damage is actually held" — and checking it HERE
						-- rather than only in finish() is what separates "the manipulation
						-- landed when the measurement began" from "it was true by the time
						-- anyone looked". A zero shotsAfter is meaningless if the target was
						-- never doomed at the moment the aircraft was supposed to notice.
						l.hpAtCritical = math.floor(l.target.Health * 100 / l.target.MaxHealth)
						if l.hpAtCritical >= 25 then
							addFault(l.id .. " target was at hp" .. l.hpAtCritical
								.. "% ON THE TRIGGER TICK - never entered the <25% Critical band,"
								.. " so the guard was never given anything to react to")
						end

						l.distAtCritical = cellDist(l.plane.Location, l.targetCell)
						l.altAtCritical = l.plane.CenterPosition.Z
						l.minDistAfter = l.distAtCritical
						l.maxDistAfter = l.distAtCritical
						l.finalDist = l.distAtCritical
						l.wasIdle = l.plane.IsIdle
						l.armed = true
					else
						allArmed = false
					end
				end
			end
		end

		elapsed = elapsed + 1

		if allArmed then
			observePhase()
		elseif elapsed >= ArmDeadlineTicks then
			for _, l in ipairs(Lanes) do
				if not l.armed then
					addFault(l.id .. " never opened fire within " .. ArmDeadlineTicks .. " ticks")
					l.firedBefore = false
					l.armed = true
					l.distAtCritical = -1
					l.minDistAfter = -1
					l.maxDistAfter = -1
					l.finalDist = -1
					l.wasIdle = false
				end
			end
			observePhase()
		else
			Trigger.AfterDelay(1, step)
		end
	end
	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("setup: USA or Russia player not found")
		return
	end

	for _, l in ipairs(Lanes) do
		l.plane = Actor.Create(l.unit, true, {
			Owner = USA,
			CenterPosition = cellPos(l.ax, l.ay, l.alt),
			Facing = Angle.South,
		})
		l.target = Actor.Create("t90", true, {
			Owner = RUSSIA,
			Location = CPos.New(l.tx, l.ty),
			Facing = Angle.North,
		})

		if l.plane == nil or l.target == nil then
			Test.Fail("setup: could not spawn " .. l.unit .. " / t90 for lane " .. l.id)
			return
		end

		-- Silence the TARGET, never the unit under test (AUTOTEST.md gotcha 9): a
		-- stance set on the aircraft would gate the very trait being measured.
		l.target.Stance = "HoldFire"

		l.targetCell = l.target.Location
		l.prevAmmo = totalAmmo(l.plane)
		l.shotsBefore = 0
		l.shotsAfter = 0
		l.shotsAfterGrace = 0
		l.firedBefore = false
		l.armed = false
		l.observed = 0
		l.idleSpans = 0
		l.reEngagements = 0
		l.ticksToIdle = -1
		l.distAtCritical = -1
		l.altAtCritical = -1
		l.hpAtCritical = -1
		l.firstMissTick = -1
		l.lastMissTick = -1
		l.diedTick = -1
		l.trace = {}
		l.finalAlt = -1
		l.minDistAfter = 999
		l.maxDistAfter = -1
		l.finalDist = -1
		l.contaminated = false
		l.wasIdle = false
	end

	Lanes[1].otherTargetCell = Lanes[2].targetCell
	Lanes[2].otherTargetCell = Lanes[1].targetCell

	TestHarness.FocusBetween(Lanes[1].plane, Lanes[1].target)
	TestHarness.Select(Lanes[1].plane)

	armPhase()
end
