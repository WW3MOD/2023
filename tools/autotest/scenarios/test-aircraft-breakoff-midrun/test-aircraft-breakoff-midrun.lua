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
-- situation ("Check that AttackFollow hasn't cancelled the target", FlyAttack.cs:101-102)
-- lives in that tick. Therefore:
--
--   MI28  AttackType: Hover, CanHover -- in range, FlyAttack queues no run child, so
--         FlyAttack.Tick runs essentially every tick and the abort check is live.
--         Predict: near-immediate clean abort.
--   A10   AttackType: Default, !CanHover -- FlyAttack queues MoveWithinRange, then
--         FlyAttackRun (Fly-in, FlyForward 1, Fly-out). Those are children, so the
--         abort check cannot be consulted until the run ENDS. FlyAttackRun.Tick only
--         self-cancels when the target becomes INVALID or has no valid weapons
--         (FlyAttack.cs:275-276); a critically damaged target is neither.
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
--   FROGSTRIKE  frog.airstrike -- AttackType: Strafe. Added 2026-09-01 as a10.airstrike and
--         SWAPPED to frog.airstrike the same day, because the first run of the A10 variant
--         found something sharper than the exemption this lane was built to catch.
--
--         ONE WRITE, TWO SYMPTOMS, and which one an airframe gets is decided entirely by its
--         weapons. FlyAttack.cs:187-188 queues StrafeAttackRun, whose Tick RE-SETS the
--         requested target every tick as
--         `SetRequestedTarget(Target.FromTargetPositions(target), true)` (FlyAttack.cs:323-325)
--         -- a TERRAIN target (Target.cs:33-35 -- FromTargetPositions builds one from the
--         actor's positions), force-attack TRUE, and no source argument, so the source
--         defaults to AttackSource.Default (AttackFollow.cs:58-59).
--
--         (a) BREAK-OFF EXEMPTION. The guard needs `RequestedTarget.Type == TargetType.Actor`
--             AND `BreakOffApplies(Default, true)` = `!forceAttack && source != Default`
--             = false. EITHER ALONE EXEMPTS IT. FlyAttack.Tick:108, which writes the real
--             actor target and source, cannot rescue it: FlyAttack leaves ChildHasPriority at
--             its default true, so its own Tick is skipped for as long as StrafeAttackRun is
--             alive, and on the one tick it does run it queues the child and
--             Activity.TickOuter:132-140 ticks that child in the SAME tick -- the terrain
--             write always lands last. World.Tick runs every actor's activities before any
--             ITick trait, so AttackFollow.Tick never once observes an Actor-type
--             RequestedTarget while the run is in flight.
--
--         (b) CANNOT FIRE AT ALL. A terrain target is unconditionally IsValidFor
--             (Target.cs:123-125), so AttackFollow.Tick:188 takes the requested branch and the
--             opportunity-fire fallback at :216 is UNREACHABLE -- the airframe is locked aiming
--             at ground it may not be able to shoot. Whether it can is decided by
--             WeaponInfo.IsValidAgainst, which resolves a terrain target to the CELL's
--             TargetTypes (WeaponInfo.cs:235-249) -- `Ground` on every TEMPERAT tile.
--
--         MEASURED 2026-09-01, and it is (b) that the A10 variant hits: lane 3 as
--         `a10.airstrike` never armed -- "A10STRIKE never opened fire within 400 ticks", target
--         still at hp100%. Neither of its weapons lists Ground. 30mm.A10 inherits ^30mm's
--         `ValidTargets: Infantry, Vehicle, Defense` (weapons-ballistics.yaml:582) and Hellfire
--         is `Vehicle, Air, Defense` (weapons-missiles.yaml:243). Both are perfectly valid
--         against the t90 as an ACTOR (^Vehicle is `Ground, Vehicle`, vehicles.yaml:46-48),
--         which is why it acquires the target, flies the run, and then never shoots. That
--         defect is filed in WORKSPACE/bugs/discovered.md; it is NOT what this scenario asks.
--
--         frog.airstrike is the only strafe airframe in the mod that can fire through its own
--         run: its single armament is RocketPods, `ValidTargets: Ground`
--         (weapons-ballistics.yaml:912). So this lane tests (a), which is the question the
--         scenario exists to ask.
--         Predict: lane 3 keeps firing at a doomed target indefinitely, and this scenario
--         FAILS ON THE SHIPPED BUILD naming FROGSTRIKE alone.
--
-- INFERENCE, NOT OBSERVATION (stated so it cannot be read as measured): the other
-- eight AttackAircraft actors are not in this scenario. F16/FROG/MIG share A10's
-- Default/!CanHover shape; littlebird/HELI/HIND share MI28's Hover shape; A10.Airstrike
-- shares FROG.Airstrike's Strafe shape but cannot fire through it, per (b) above.
--
-- THE OBSERVABLE, and what could make it RED.
-- shotsAfterCritical = ticks on which the aircraft's total ammo DECREASED after its
-- target was driven under 25% health. Decrements, not net, because the A10 and Mi-28
-- carry ReloadAmmoPool and a refill would otherwise mask a shot. (The Airstrike variants
-- strip it, so lane 3 cannot refill -- see the ammoLeft0 note at the dry-gun fault.)
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
-- Plus a contamination guard: 40 cells separate adjacent lanes, and an aircraft that
-- wanders within 20 cells of ANY other lane's target invalidates its own ammo trace.
--
-- WHY LANE 3's ARMING TRIGGER IS ALSO ITS PROOF-OF-POSTURE, which is the one thing a
-- Strafe lane needs that the other two do not. The trigger fires on the lane's first
-- ammo DECREMENT, and for a Strafe airframe a shot can only have been fired from inside
-- a live StrafeAttackRun: FlyAttack.cs:180 pins minimumRange to zero for Strafe, so
-- FlyAttack.Tick:183 reduces to "in max range?" -- out of range it queues MoveWithinRange
-- and cannot fire at all, in range it queues StrafeAttackRun, which overwrites the
-- requested target with the position write in that same tick. So "lane 3 fired" is not
-- merely evidence that it engaged; it is evidence that the run child was alive and had
-- written the exempt target. Without that, a break-off observed during the APPROACH --
-- where FlyAttack.Tick's actor-target write is the last one standing and the guard CAN
-- fire -- would read as "strafe airframes break off fine" and bury the defect.
--
-- EXECUTION MARKER. Every verdict string starts with `bkoff3/` and carries `obs<n>`, the
-- number of observation ticks actually executed, and WorldLoaded prints `[bkoff3] loaded`
-- to lua.log before it touches anything. A Lua load abort also reports status:fail, so
-- without these a never-executed run is indistinguishable from a real RED. If a fail
-- verdict does not begin with `bkoff3/`, the script did not run and the result is void --
-- check lua.log is non-empty and that map.yaml still ends with its `Rules: rules.yaml`.

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

-- `pools` is per-lane and is NOT cosmetic: AmmoPoolProperties.AmmoCount THROWS a LuaException
-- for a pool the actor does not declare (AmmoPoolProperties.cs:36-38), which aborts the whole
-- run rather than returning zero. frog.airstrike carries ONE pool -- FROG declares only
-- `primary-ammo` (aircraft-russia.yaml:505) -- while the A10 and Mi-28 carry both. A shared
-- {primary,secondary} list would have killed the script on lane 3's first ammo read.
local Lanes = {
	{
		id = "A10",
		unit = "a10",
		ax = 10, ay = 6, alt = 2560,
		tx = 10, ty = 20,
		pools = { "primary-ammo", "secondary-ammo" },
	},
	{
		id = "MI28",
		unit = "mi28",
		ax = 50, ay = 6, alt = 1280,
		tx = 50, ty = 20,
		pools = { "primary-ammo", "secondary-ammo" },
	},
	-- alt 1536 is FROG.Airstrike's own CruiseAltitude (1c512, aircraft-russia.yaml:719) --
	-- NOT the FROG's 1560, and not the A10's 2560. Aircraft.AddedToWorld only marks an
	-- aircraft airborne AND cruising on the tick it appears if it is created at its own
	-- cruise height, and a fixed-wing that starts below it spends the opening ticks climbing
	-- instead of attacking, which eats the arming deadline for no reason.
	--
	-- A Russian airframe under the USA player is deliberate and already precedented by the
	-- Mi-28 lane above: Actor.Create takes the owner as given, and nothing here is
	-- faction-gated. What matters is that FROG inherits ^AutoTargetGroundAntiTank
	-- (aircraft-russia.yaml:462) with InitialStance FireAtWill, so the lane acquires without
	-- an order, and that AutoTargetInfo.BreakOffCondition defaults to `critical-damage`
	-- (AutoTarget.cs:244) -- the guard has something to test.
	{
		id = "FROGSTRIKE",
		unit = "frog.airstrike",
		ax = 90, ay = 6, alt = 1536,
		tx = 90, ty = 20,
		pools = { "primary-ammo" },
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

local function totalAmmo(l)
	if l.plane.IsDead then return 0 end
	local n = 0
	for _, p in ipairs(l.pools) do
		n = n + l.plane.AmmoCount(p)
	end
	return n
end

local function addFault(s)
	table.insert(setupFaults, s)
end

local function finish()
	local report = {}
	local totalShotsAfter = 0
	local offenders = {}
	local observedTicks = 0

	for _, l in ipairs(Lanes) do
		totalShotsAfter = totalShotsAfter + l.shotsAfterGrace
		if l.shotsAfterGrace > 0 then table.insert(offenders, l.id) end
		if l.observed > observedTicks then observedTicks = l.observed end

		local hpPct = -1
		if not l.target.IsDead then
			hpPct = math.floor(l.target.Health * 100 / l.target.MaxHealth)
		end

		if l.plane.IsDead then addFault(l.id .. " aircraft died") end
		if l.target.IsDead then addFault(l.id .. " target died") end
		-- Lane 3 is the one that can realistically reach this, and only in the arm where the
		-- guard fails: frog.airstrike carries 30 rockets, no ReloadAmmoPool and no Rearmable,
		-- and RocketPods is Burst 10, so an unguarded lane empties in ~3 bursts. That is not a
		-- void run -- the break-off verdict above is evaluated FIRST and will already have
		-- latched, because a single Burst-10 volley at BurstDelays 1 puts ~8 decrements outside
		-- the 2-tick grace. A dry lane 3 reported WITHOUT a break-off failure is the real
		-- setup fault: it means the gun went silent for a reason other than the guard.
		if not l.plane.IsDead and totalAmmo(l) <= 0 then
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
			"ammoLeft" .. totalAmmo(l),
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

	-- `bkoff3/` and obs<n> are the EXECUTION MARKER and ride on every verdict, pass or fail:
	-- a Lua that aborted at load also reports status:fail, and only their presence separates
	-- that from a run that actually measured something. obs<n> is the observation ticks the
	-- slowest-armed lane completed, so obs0 says the arming phase never finished.
	local summary = "bkoff3/ obs" .. observedTicks .. " " .. table.concat(report, " | ")

	-- Order matters. The break-off verdict is read FIRST so the RED arm reports the
	-- mechanism rather than the collateral (a target the un-guarded aircraft shot to
	-- death would otherwise surface as "target died" and hide why).
	--
	-- THE LANE LIST IN BRACKETS IS LOAD-BEARING, not decoration. Two different runs fail
	-- here and they must never be read as the same result:
	--   [A10,MI28,FROGSTRIKE] -- the RED arm, break-off guard commented out of
	--                            AttackFollow.Tick. Every lane fires; the gate works.
	--   [FROGSTRIKE]          -- the SHIPPED build, and the finding this lane was added
	--                            for: the Strafe airframe is structurally exempt while the
	--                            two guarded shapes break off correctly.
	-- A bare total would collapse those into one number.
	if totalShotsAfter > 0 then
		Test.Fail("BREAK-OFF DID NOT FIRE [" .. table.concat(offenders, ",") .. "]: "
			.. totalShotsAfter
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

	local ammo = totalAmmo(l)
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

	for _, c in ipairs(l.otherTargetCells) do
		if cellDist(here, c) < ContaminationCells then
			l.contaminated = true
		end
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
					local ammo = totalAmmo(l)
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

						-- Live values, printed rather than interpolated into a failure
						-- string: AssertWithin-style messages are concatenated eagerly at
						-- registration and would report the pre-run zeros forever.
						print("[bkoff3] armed lane=" .. l.id .. " elapsed=" .. elapsed
							.. " hpAtCrit=" .. l.hpAtCritical .. "% dist=" .. l.distAtCritical
							.. " alt=" .. l.altAtCritical .. " ammo=" .. totalAmmo(l))
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
	-- Printed before anything else can throw: this line in lua.log is the proof that the
	-- script loaded at all. A run whose lua.log lacks it never executed, whatever the
	-- verdict says (AUTOTEST.md: "the tell is lua.log at 0 bytes").
	print("[bkoff3] loaded lanes=" .. #Lanes .. " observeTicks=" .. ObserveTicks
		.. " armDeadline=" .. ArmDeadlineTicks .. " grace=" .. GraceTicks)

	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("bkoff3/ setup: USA or Russia player not found")
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
			Test.Fail("bkoff3/ setup: could not spawn " .. l.unit .. " / t90 for lane " .. l.id)
			return
		end

		-- Silence the TARGET, never the unit under test (AUTOTEST.md gotcha 9): a
		-- stance set on the aircraft would gate the very trait being measured.
		l.target.Stance = "HoldFire"

		l.targetCell = l.target.Location
		l.prevAmmo = totalAmmo(l)
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

	for i, l in ipairs(Lanes) do
		l.otherTargetCells = {}
		for j, o in ipairs(Lanes) do
			if i ~= j then table.insert(l.otherTargetCells, o.targetCell) end
		end
	end

	TestHarness.FocusBetween(Lanes[1].plane, Lanes[1].target)
	TestHarness.Select(Lanes[1].plane)

	armPhase()
end
