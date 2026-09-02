-- AUTO TEST: does an `AttackType: Strafe` airframe engage at all, and where does it go?
--
-- WHY THIS SCENARIO EXISTS, and why it is not a third lane bolted onto
-- test-aircraft-breakoff-midrun.
--
-- On 2026-09-01 that scenario's lane 3 was tried as `a10.airstrike` and then as
-- `frog.airstrike`. Both fired zero shots and the lane was disabled. The write-up in
-- WORKSPACE/DISCOVERIES.md concluded from lane 3's flight trace -- "a flat 11-13 cell standoff
-- for the whole window" -- that the airframe never closes, and pointed the next investigation at
-- FlyAttack.cs:183 to ask whether a StrafeAttackRun is ever queued.
--
-- THAT TRACE DOES NOT COVER THE WINDOW IT WAS READ AS COVERING, and the arithmetic is in the
-- scenario itself. In that script `observeTick` -- the only function that appends to a lane's
-- trace -- runs for a lane during the arming phase ONLY once the lane has ARMED (its `if l.armed
-- then observeTick(l)` branch), and lane 3 never armed. So lane 3 was first sampled at its arming
-- DEADLINE. The surviving result.json for run 260901_085215_p7281 shows exactly that: the two
-- live lanes carry 26 trace samples and `obs626`, the strafe lane carries 10. At one sample per
-- 25 observed ticks that is 250 ticks against 626 -- the strafe airframe's first 400 ticks, which
-- is precisely the window in which a fresh airframe would make its opening pass, produced NO
-- distance data at all. `minDistAfter-1` on that lane is not "it never got close" either: the
-- deadline branch assigns `l.minDistAfter = -1`, after which `if d < l.minDistAfter` can never be
-- true again, so the field is structurally dead for any lane that fails to arm.
--
-- What survives from that run is the part that needs no trace: `shotsBefore0`, `SHOTSAFTER0` and
-- `ammoLeft30` against a 30-round magazine. The FROG never fired, across 626 ticks. That is solid
-- and it is what falsified the `ValidTargets: Ground` explanation. What does NOT survive is any
-- claim about where the airframe went.
--
-- SO THIS SCENARIO TRACES FROM TICK 0, AND CARRIES ITS OWN POSITIVE CONTROL.
--
-- Lane STRAFE  = frog.airstrike -- AttackType: Strafe (aircraft-russia.yaml:723). The subject.
-- Lane CONTROL = frog           -- AttackType inherited Default. Same Armaments: primary ->
--                                  Armament@1 Weapon: RocketPods, same ^AutoTargetGroundAntiTank
--                                  chain, same owner, same t90, same 14-cell opening geometry.
--
-- The control is what makes a zero readable. EVERY explanation that lives in the weapon
-- (RocketPods ValidTargets: Ground / MinRange 4c0), in the target (t90 is Ground, Vehicle, Heavy
-- at vehicles-russia.yaml:328), in the terrain (TEMPERAT clear is TargetTypes: Ground), or in
-- acquisition range (ScanRadius unpinned -> GetMaximumRange -> 25c0, target at 14 cells) predicts
-- the SAME outcome on both lanes, because both lanes share all four. A run where CONTROL fires
-- and STRAFE does not eliminates all of them in one shot. A run where NEITHER fires says the
-- fault is upstream of AttackType and the strafe path was never the right place to look.
--
-- READ THE VERDICT, NOT THE PASS/FAIL. This scenario's Test.Fail is the INTERESTING outcome; it
-- is a diagnostic, and the traces are the payload.

local ObserveTicks = 600      -- long enough for several full strafe cycles, see the note below
local SampleEvery = 10        -- 60 trace samples per lane

-- WHY 600 AND WHY SAMPLE AT 10. A full StrafeAttackRun cycle for frog.airstrike is bounded
-- below by its own three legs (FlyAttack.cs:313-320): dive to the target, FlyForward(exitRange)
-- where exitRange is StrafeRunLength 12c0, then fly out past exitRange + distanceToTurn, where
-- distanceToTurn = Speed * 256 / TurnSpeed.Angle = 200 * 256 / 8 = 6400. That last leg alone
-- wants 12288 + 6400 = 18688 (18.25 cells) of separation, and at Speed 200 a cell costs ~5
-- ticks, so one cycle cannot complete in under ~180 ticks. 600 ticks is three cycles' worth.
-- Sampling every 25 ticks (the break-off scenario's period) aliased the A10's cycle into
-- apparent noise -- 9,5,13,10,4,11,... -- which is unreadable. At 10 the shape survives.

local Lanes = {
	{
		id = "STRAFE",
		unit = "frog.airstrike",
		-- alt 1536 is FROG.Airstrike's own CruiseAltitude (1c512, aircraft-russia.yaml:719).
		-- Aircraft.AddedToWorld only marks an aircraft airborne AND cruising on the tick it
		-- appears if it is created at its own cruise height; a fixed-wing starting below it
		-- spends the opening ticks climbing instead of attacking, which is exactly the window
		-- this scenario exists to observe.
		ax = 10, ay = 6, alt = 1536,
		tx = 10, ty = 20,
	},
	{
		id = "CONTROL",
		unit = "frog",
		-- 1560 is plain FROG's CruiseAltitude (aircraft-russia.yaml:489), not 1536. Each lane is
		-- created at ITS OWN cruise height for the reason above.
		ax = 50, ay = 6, alt = 1560,
		tx = 50, ty = 20,
	},
}

-- Both airframes declare exactly ONE ammo pool, primary-ammo (FROG AmmoPool@1, and
-- FROG.Airstrike which only overrides its Ammo count). This is not cosmetic:
-- AmmoPoolProperties.AmmoCount THROWS a LuaException for a pool the actor does not declare
-- (AmmoPoolProperties.cs:36-38) rather than returning zero, so a shared {primary,secondary} list
-- would kill the whole run on the first read. Learned the expensive way on 2026-09-01.
local AmmoPool = "primary-ammo"

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

local function cellDist(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

local function finish()
	local report = {}

	for _, l in ipairs(Lanes) do
		local hpPct = -1
		if not l.target.IsDead then
			hpPct = math.floor(l.target.Health * 100 / l.target.MaxHealth)
		end

		local ammoLeft = -1
		if not l.plane.IsDead then ammoLeft = l.plane.AmmoCount(AmmoPool) end

		table.insert(report, table.concat({
			l.id,
			"shots" .. l.shots,
			"firstShotTick" .. l.firstShotTick,
			"minDist" .. l.minDist,
			"maxDist" .. l.maxDist,
			"finalDist" .. l.finalDist,
			"finalAlt" .. l.finalAlt,
			"planeDead" .. tostring(l.plane.IsDead),
			"tgtHp" .. hpPct .. "%",
			"ammoLeft" .. ammoLeft,
			"trace[" .. table.concat(l.trace, ",") .. "]",
		}, " "))
	end

	-- `strafe1/` and obs<n> are the EXECUTION MARKER and ride on every verdict: a Lua that aborted
	-- at load also reports status:fail, and only their presence separates that from a run that
	-- actually measured something.
	local summary = "strafe1/ obs" .. ObserveTicks .. " " .. table.concat(report, " | ")

	local strafe, control = Lanes[1], Lanes[2]

	-- ORDER MATTERS, and this is the guard that keeps a broken run from being read as a result.
	-- If the CONTROL lane fired nothing then the instrument proves nothing about the strafe lane:
	-- a shared fault (spawn geometry, ownership, stance, an engine regression in the whole attack
	-- path) would zero both, and reporting that as "strafe confirmed" is exactly the mistake this
	-- scenario was written to undo.
	if control.shots == 0 then
		Test.Fail("INSTRUMENT DEAD: the CONTROL lane (plain frog, AttackType Default, same"
			.. " RocketPods and same 14-cell geometry) also fired zero shots, so this run"
			.. " says nothing about AttackType: Strafe -- the fault is upstream of it || " .. summary)
		return
	end

	if strafe.shots == 0 then
		Test.Fail("STRAFE ZERO: control fired " .. control.shots .. " times, subject fired 0."
			.. " AttackType: Strafe is the discriminator; weapon, target types, terrain types and"
			.. " acquisition range are all eliminated (both lanes share them). Read the STRAFE"
			.. " trace: it says whether the airframe ever closed || " .. summary)
		return
	end

	Test.Pass(summary)
end

local function observeTick(l)
	if l.plane.IsDead then
		if l.observed % SampleEvery == 0 then table.insert(l.trace, -1) end
		l.observed = l.observed + 1
		return
	end

	local ammo = l.plane.AmmoCount(AmmoPool)
	if ammo < l.prevAmmo then
		l.shots = l.shots + 1
		if l.firstShotTick < 0 then l.firstShotTick = l.observed end
	end
	l.prevAmmo = ammo

	local d = cellDist(l.plane.Location, l.targetCell)
	if d < l.minDist then l.minDist = d end
	if d > l.maxDist then l.maxDist = d end
	l.finalDist = d
	l.finalAlt = l.plane.CenterPosition.Z

	if l.observed % SampleEvery == 0 then
		table.insert(l.trace, d)
	end

	l.observed = l.observed + 1
end

WorldLoaded = function()
	-- Printed before anything else can throw: this line in lua.log is the proof that the script
	-- loaded at all. A run whose lua.log lacks it never executed, whatever the verdict says
	-- (AUTOTEST.md: "the tell is lua.log at 0 bytes").
	print("[strafe1] loaded lanes=" .. #Lanes .. " observeTicks=" .. ObserveTicks
		.. " sampleEvery=" .. SampleEvery)

	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("strafe1/ setup: USA or Russia player not found")
		return
	end

	for _, l in ipairs(Lanes) do
		-- A Russian airframe under the USA player is deliberate and precedented by
		-- test-aircraft-breakoff-midrun's Mi-28 lane: Actor.Create takes the owner as given and
		-- nothing here is faction-gated. Both lanes are flown by USA against a Russian t90 so the
		-- two lanes are relationally identical.
		l.plane = Actor.Create(l.unit, true, {
			Owner = USA,
			CenterPosition = cellPos(l.ax, l.ay, l.alt),
			-- Facing the target on tick 0. Both t90s sit due south of their aircraft, so neither
			-- lane spends its opening ticks turning, and a difference in time-to-first-shot is
			-- not a difference in how far each had to turn.
			Facing = Angle.South,
		})
		l.target = Actor.Create("t90", true, {
			Owner = RUSSIA,
			Location = CPos.New(l.tx, l.ty),
			Facing = Angle.North,
		})

		if l.plane == nil or l.target == nil then
			Test.Fail("strafe1/ setup: could not spawn " .. l.unit .. " / t90 for lane " .. l.id)
			return
		end

		-- Silence the TARGET, never the unit under test (AUTOTEST.md gotcha 9): a stance set on
		-- the aircraft would gate the very trait being measured. Both aircraft keep the
		-- InitialStance: FireAtWill they inherit from FROG (aircraft-russia.yaml:491-493), which
		-- is what makes them acquire without an order.
		l.target.Stance = "HoldFire"

		l.targetCell = l.target.Location
		l.prevAmmo = l.plane.AmmoCount(AmmoPool)
		l.shots = 0
		l.firstShotTick = -1
		l.observed = 0
		l.minDist = 999
		l.maxDist = -1
		l.finalDist = -1
		l.finalAlt = -1
		l.trace = {}
	end

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
