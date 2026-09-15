-- AUTO TEST: a soldier at a garrison firing port may only be shot from inside that port's arc.
--
-- THE DEFECT THIS GUARDS (fixed 2026-09-15, commit 22f409d6). `GarrisonPortOccupant` exists to
-- refuse targeting from outside the port's cone (GarrisonPortOccupant.cs:91-122), and its own
-- [Desc] states the precondition: "The regular Targetable trait should have RequiresCondition:
-- !garrisoned-at-port." ^Infantry honours that (infantry.yaml:62-63). ^MT and ^AT did NOT --
-- each declared a third targetable, Targetable@HighPriority, with no RequiresCondition at all.
--
-- Actor.IsTargetableBy ORs across every enabled ITargetable and returns on the first `true`
-- (Actor.cs:671-678), and the base Targetable.TargetableBy answers on trait-enabled alone
-- (Targetable.cs:46-55). So that one ungated trait said "yes" for every attacker and the arc
-- trait was never reached to say no. A garrisoned mortarman was shootable from directly behind
-- the building, and kept broadcasting HighPriorityInfantry from inside it.
--
-- THE RED, AND HOW TO STAGE IT. Delete the two `RequiresCondition: !garrisoned-at-port` lines
-- from Targetable@HighPriority on ^MT and ^AT (infantry.yaml:1625-1626, :1767-1768) and run
-- again. PHASE 2 fails with the message beginning "THE BEHIND SHOT CONNECTED" -- BehindShooter,
-- due south of a north-east-facing port, lands damage on the Gunner. Nothing else in the run
-- changes: both controls stay green, which is what makes the failure attributable.
--
-- WHY THE MEASUREMENT IS DAMAGE AND NOT A CURSOR OR AN ORDER STRING. Test.GetTargetOrder and
-- Test.ClickOrder walk the IOrderTargeter pipeline, and that pipeline NEVER consults
-- IsTargetableBy: AttackOrderTargeter.CanTargetActor asks ChooseArmamentsForTarget
-- (AttackBase.cs:849), which ends at WeaponInfo.IsValidAgainst -> victim.GetEnabledTargetTypes()
-- (WeaponInfo.cs:256-264) -- a UNION of target types, with no per-attacker question in it. Both
-- before and after the fix the garrisoned MT still has Ground and Infantry enabled, so every
-- order-layer instrument returns "Attack" either way and would be a RED that cannot fail. The
-- arc gate lives one layer down, in the attack ACTIVITY (Attack.cs:209, AttackBase.cs:288,
-- both `target.IsValidFor(self)` -> Actor.IsTargetableBy). Damage landing or not landing is the
-- only observable that sits below that line.
--
-- WHY THE GUNNER IS PINNED TO A NORTH-EAST PORT RATHER THAN TRUSTED TO LAND THERE. ^CivBuilding
-- declares 8 ports on the four diagonals with Cone: 140 (civilian.yaml:79-119). WAngle is
-- 1024 = 360 degrees -- GarrisonPortInfo.Cone defaults to WAngle(512), which its [Desc] calls a
-- half-angle, i.e. omnidirectional -- so 140 is a 49.2-degree HALF-angle and the four cones
-- UNION TO THE WHOLE CIRCLE. There is therefore no direction that is "behind the building"; only
-- "behind the port this man is standing at", because TargetableBy tests the occupant's own port
-- and nothing else. So which port gets manned decides the whole test. It is pinned by range, not
-- by luck: 60mm_Mortar is Range: 25c0, MinRange: 8c0 (weapons-ballistics.yaml:788-789), Bait
-- sits at 11.3 cells on the NE diagonal and is the ONLY actor any port can acquire, and every
-- other Russian is deliberately inside the 8-cell minimum where no port can see them at all.
--
-- VERDICTS:
--   PASS  — the behind shooter cannot touch the garrisoned mortarman, the in-cone shooter can,
--           and both controls are unchanged.
--   FAIL  — one of those four limbs broke. The message names which and what it implies.
--   SKIP  — the scenario never built the world it describes (nobody garrisoned, nobody deployed,
--           a trigger died early). A setup fault, never a finding about garrisons.

local SetupWithin = 40      -- s for two men to walk in and claim their houses
local DeployWithin = 40     -- s for GarrisonManager to confirm a target and man a port
local HitWithin = 20        -- s for an in-arc shooter to land its first round
-- 30 s, not 12. 5.56mm.E3 is ReloadDelay 60 with Burst 2, and a port occupant carries
-- DamageMultiplier@GarrisonCover: Modifier: 20 (infantry.yaml:213-215) -- so a permitted shooter
-- lands roughly one 5-damage hit per reload cycle, and the control limb's whole evidence in run
-- 260915_184416 was a single 200 -> 195. A 12 s window was about three chances to register a 5 hp
-- delta: "took zero" was as likely to mean "missed" as "was refused", which makes a PASS here worth
-- very little. This limb asserts a NEGATIVE, so it has to be the generous one.
local QuietFor = 30         -- s a blocked shooter is given to prove it lands nothing
local SettleFor = 3         -- s after a Stop order, before a fresh health baseline is taken
-- 45 s, not 20. The derived cells rotate with the held port while the spawns are fixed, so one of
-- them can land ON a shooter's own start cell -- run 260915_211447 sent the behind shooter to 24,8,
-- which is where the cone shooter was still standing, and he never left his spawn. He gets there
-- once the cell frees, but only if the order is re-issued, and only if there is time to walk the
-- long way round the house.
local MoveWithin = 45       -- s for the two shooters to walk onto their derived bearings
local MoveTraceEvery = 5    -- s between position traces while they walk
local HeldYaw = nil         -- the port yaw both shooters were positioned against
local ConeCellX, ConeCellY = nil, nil       -- derived cell the in-cone shooter must stand on
local BehindCellX, BehindCellY = nil, nil   -- derived cell the behind shooter must stand on

local function OwnerOf(actor)
	local o = actor.Owner
	if o == nil then
		return "<none>"
	end

	return o.InternalName
end

-- INSTRUMENT, and the first version of this file got it wrong in a way that cost a whole run. It
-- asked Test.ConditionCount(soldier, "garrisoned-at-port"). That binding opens with
--   if (!TestMode.IsActive || actor == null || actor.IsDead || !actor.IsInWorld) return 0;
-- and a Cargo passenger is out of world AND reads IsDead == true -- so it returns 0 for every man
-- inside a building, including one whose condition is granted precisely BECAUSE he boarded. A
-- guaranteed false negative, not a flaky one. Test.IsAtGarrisonPort reads GarrisonManager's own
-- PortStates and answers truthfully whatever those two flags say.
-- AND IsInWorld, WHICH IS NOT BELT-AND-BRACES -- it is the whole of the second bug this file hit.
-- DeployToPort assigns PortStates[i].DeployedSoldier SYNCHRONOUSLY but adds the man to the world in
-- a FRAME-END TASK (GarrisonManager.cs), so there is a window in which IsAtGarrisonPort already says
-- yes and the actor is still out of world. Target.Type returns Invalid for an out-of-world actor
-- (Target.cs:93-98), so an attack order issued inside that window is refused outright: run
-- 260915_182012 failed the in-cone limb with lua.log carrying exactly one line,
-- "mt 6 is an invalid target for e1 8!". Waiting for both flags closes it.
local function AtPort(soldier)
	if not soldier.IsInWorld then
		return false
	end

	return Test.IsAtGarrisonPort(soldier, HouseMT) or Test.IsAtGarrisonPort(soldier, HouseE1)
end

local function HealthOf(actor)
	if actor == nil or actor.IsDead then
		return -1
	end

	return actor.Health
end

-- THE PORT THE GUNNER ACTUALLY HOLDS DECIDES EVERYTHING, AND IT IS NOT PREDICTABLE FROM THE MAP.
-- Three runs were spent assuming it. GarrisonManager deploys to the first port that confirms an
-- IN-ARC, IN-RANGE target (ScanForTarget arc-filters at GarrisonManager.cs), so which port wins
-- depends on every enemy on the map, the scanning weapon's range, and the port declaration order --
-- run 260915_191454 put the Gunner on southwest2 (yaw 384) because BehindShooter sat due south at
-- 512, which is inside the south-west cone [244,524] by 12 units. The "in-cone" shooter was then
-- 512 units off the held port and correctly refused, and the "behind" shooter was inside it.
--
-- So the shooters are placed FROM the port, at runtime, instead of being guessed at map time. Both
-- distances sit inside 60mm_Mortar's MinRange of 8c0, which is what stops the garrison acquiring
-- either of them and being lured into swapping ports mid-measurement, and inside the shooters' own
-- 10c0 rifle so they can both reach.
local ConeDistance = 4      -- cells along the held port's yaw
local BehindDistance = 6    -- cells along the opposite bearing

-- WAngle is 1024 = 360 degrees and WVec.Yaw is ArcTan(-Y, X) - 256 (WVec.cs:66-76), so inverting
-- it needs that 256 added back: dx = cos(yaw + 256), dy = -sin(yaw + 256). Checks out on the two
-- bearings this file talks about -- 896 gives (+x, -y) north-east, 384 gives (-x, +y) south-west.
local function YawToOffset(yaw, cells)
	local radians = (yaw + 256) / 1024 * 2 * math.pi
	return math.floor(cells * math.cos(radians) + 0.5), math.floor(cells * -math.sin(radians) + 0.5)
end

-- Arrival, with one cell of slack. An exact match would hang the run whenever the derived cell is
-- occupied or unreachable and the pathfinder parks the man next to it; at ~5.7 cells out, being one
-- cell off is about 10 degrees, against a cone of 140 units (49 degrees), so it cannot move a
-- shooter across the arc boundary in either direction.
local function AtCell(actor, x, y)
	return math.abs(actor.Location.X - x) <= 1 and math.abs(actor.Location.Y - y) <= 1
end

-- "index=5 name=southwest2 yaw=384 cone=140", or "none".
local function PortYawOf(soldier, building)
	local report = Test.GarrisonPortOf(soldier, building)
	local yaw = string.match(report, "yaw=(%d+)")
	if yaw == nil then
		return nil
	end

	return tonumber(yaw)
end

-- Everything the next reader needs to name a refusal, printed at the instant an order goes in.
-- TargetableReport walks Actor.IsTargetableBy trait by trait, so "which targetable said no" stops
-- being a guess; GarrisonPortOf names the port actually held.
local function ReportGeometry(label, shooter, building, occupant)
	print(label ..
		" | port " .. Test.GarrisonPortOf(occupant, building) ..
		" | occupant cell " .. occupant.Location.X .. "," .. occupant.Location.Y ..
		" inWorld=" .. tostring(occupant.IsInWorld) ..
		" | building cell " .. building.Location.X .. "," .. building.Location.Y ..
		" | shooter cell " .. shooter.Location.X .. "," .. shooter.Location.Y ..
		" | canTarget=" .. tostring(shooter.CanTarget(occupant)) ..
		" | " .. Test.TargetableReport(occupant, shooter))
end

local function State()
	return "HouseMT owner " .. OwnerOf(HouseMT) .. ", HouseE1 owner " .. OwnerOf(HouseE1) ..
		"; Gunner atPort=" .. tostring(AtPort(Gunner)) .. " hp=" .. HealthOf(Gunner) ..
		"; Rifleman atPort=" .. tostring(AtPort(Rifleman)) .. " hp=" .. HealthOf(Rifleman) ..
		"; Bait alive=" .. tostring(not Bait.IsDead) ..
		"; OpenMT hp=" .. HealthOf(OpenMT)
end

-- Poll until `predicate` holds. Deliberately NOT TestHarness.AssertWithin: that calls
-- Test.Pass() the moment its predicate is true, which would end the run at the first phase.
local function WaitUntil(seconds, predicate, onReady, onTimeout)
	local remaining = math.floor(seconds * TestHarness.TicksPerSecond)
	local check
	check = function()
		if predicate() then
			onReady()
			return
		end

		remaining = remaining - 1
		if remaining <= 0 then
			onTimeout()
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

-- Hold `seconds` and then report whether `actor` lost any health across the whole window. Used
-- for the two "this shooter must land nothing" limbs, where waiting the full window IS the
-- measurement - there is no early exit that could prove a negative.
local function HoldAndCompare(seconds, actor, baseline, onDone)
	local remaining = math.floor(seconds * TestHarness.TicksPerSecond)
	local worst = baseline
	local tick
	tick = function()
		local hp = HealthOf(actor)
		if hp < worst then
			worst = hp
		end

		remaining = remaining - 1
		if remaining <= 0 then
			onDone(worst)
			return
		end

		Trigger.AfterDelay(1, tick)
	end

	Trigger.AfterDelay(1, tick)
end

-- PHASE 4 — an MT in the OPEN must still be shootable. Guards the wrong fix: a condition that
-- switches Targetable@HighPriority off everywhere rather than only at a port.
local function OpenMtControl()
	local baseline = HealthOf(OpenMT)
	OpenShooter.Attack(OpenMT)

	WaitUntil(HitWithin,
		function() return OpenMT.IsDead or HealthOf(OpenMT) < baseline end,
		function() Test.Pass() end,
		function()
			Test.Fail("an MT standing in the OPEN took no damage in " .. HitWithin ..
				"s from a rifleman 4 cells away. Nothing about a garrison is involved here: this " ..
				"man carries no garrisoned-at-port condition, so both his base Targetable " ..
				"(infantry.yaml:62-63) and Targetable@HighPriority should be enabled. If this limb " ..
				"is the only red one, the !garrisoned-at-port condition added to " ..
				"Targetable@HighPriority is disabling it unconditionally rather than only at a " ..
				"port - check the condition name for a typo against ExternalCondition@GarrisonPort " ..
				"(infantry.yaml:74-75). " .. State())
		end)
end

-- PHASE 3 — the control that proves the arc gate was never broken for ordinary infantry. e1 was
-- correct before the fix and must read identically after it. Without this limb, "the behind shot
-- did nothing" cannot be distinguished from "the arc gate now refuses everyone".
local function RiflemanControl()
	if not AtPort(Rifleman) then
		Test.Skip("the control rifleman never reached a firing port, so the unchanged-behaviour " ..
			"limb could not be measured. Only the MT limbs were exercised; treat the run as " ..
			"incomplete rather than as evidence either way. " .. State())
		return
	end

	local baseline = HealthOf(Rifleman)
	BehindShooterB.Attack(Rifleman)

	HoldAndCompare(QuietFor, Rifleman, baseline, function(worst)
		if worst < baseline then
			Test.Fail("THE ARC GATE IS NOW REFUSING NOBODY, OR REFUSING THE WRONG THING: a plain " ..
				"e1 rifleman at a north-east port lost health (" .. baseline .. " -> " .. worst ..
				") to a shooter due SOUTH of him. This actor was never affected by the " ..
				"Targetable@HighPriority defect - ^Infantry has gated its Targetable on " ..
				"!garrisoned-at-port all along (infantry.yaml:62-63) - so this is not a regression " ..
				"in the 2026-09-15 fix but in GarrisonPortOccupant.TargetableBy itself " ..
				"(GarrisonPortOccupant.cs:105-121), or in the condition that enables it. " .. State())
			return
		end

		if not AtPort(Rifleman) then
			Test.Skip("the control rifleman left his port during the measurement window, so his " ..
				"untouched health says nothing - a man in the shelter is untargetable by " ..
				"everything. " .. State())
			return
		end

		OpenMtControl()
	end)
end

-- PHASE 2 — THE ASSERTION. Due south of a north-east-facing port, at 384 WAngle units off a
-- cone of 140, this shooter must land nothing at all.
local function BehindShot()
	if not AtCell(BehindShooter, BehindCellX, BehindCellY) then
		Test.Skip("the behind shooter is at " .. BehindShooter.Location.X .. "," ..
			BehindShooter.Location.Y .. " rather than his derived cell " .. BehindCellX .. "," ..
			BehindCellY .. ", so a quiet window here would measure a bearing nobody chose. " .. State())
		return
	end

	ReportGeometry("BEHIND-SHOT", BehindShooter, HouseMT, Gunner)
	local baseline = HealthOf(Gunner)
	BehindShooter.Attack(Gunner)

	HoldAndCompare(QuietFor, Gunner, baseline, function(worst)
		-- Order matters: the port check comes FIRST on the pass path but the damage check comes
		-- first overall, because damage landing is a finding whatever else is true.
		if PortYawOf(Gunner, HouseMT) ~= HeldYaw then
			Test.Skip("the Gunner changed ports during the behind-shot window (was yaw " ..
				tostring(HeldYaw) .. ", now " .. Test.GarrisonPortOf(Gunner, HouseMT) .. "), so both " ..
				"shooters are now on bearings derived from a port he no longer holds and neither " ..
				"result means anything. " .. State())
			return
		end

		if worst < baseline then
			Test.Fail("THE BEHIND SHOT CONNECTED. A rifleman due SOUTH of the building took the " ..
				"garrisoned MT from " .. baseline .. " to " .. worst .. " hp, but the Gunner is at " ..
				"a NORTH-EAST port (yaw 896, Cone 140 = 49.2 degrees either side) and due south is " ..
				"yaw 512 - 384 units off, far outside the arc. GarrisonPortOccupant.TargetableBy " ..
				"(GarrisonPortOccupant.cs:105-121) should have refused him. The known cause is an " ..
				"enabled second targetable winning the OR in Actor.IsTargetableBy " ..
				"(Actor.cs:671-678): check that Targetable@HighPriority on ^MT still carries " ..
				"RequiresCondition: !garrisoned-at-port (infantry.yaml:1625-1626). " .. State())
			return
		end

		-- THE GUARD THAT STOPS THIS PASSING FOR THE WRONG REASON. A Gunner recalled to the
		-- shelter is out of world and untargetable by everything, so "took no damage" would be
		-- true and meaningless. Bait dying is the way that happens.
		if not AtPort(Gunner) then
			Test.Skip("the Gunner was no longer at a firing port at the end of the quiet window, " ..
				"so his untouched health proves nothing - a shelter occupant is out of world and " ..
				"untargetable by anything at any angle. The usual cause is Bait dying to the " ..
				"garrison's own mortar, which leaves the north-east port with no target and " ..
				"recalls the man. Nothing was measured. " .. State())
			return
		end

		RiflemanControl()
	end)
end

-- PHASE 1b — stop the in-cone shooter and let the last round in flight land before a fresh
-- baseline is taken. Without the settle the next phase would attribute his trailing shot to the
-- behind shooter and fail the run.
local function StopConeShooterThenMeasure()
	ConeShooter.Stop()
	Trigger.AfterDelay(math.floor(SettleFor * TestHarness.TicksPerSecond), BehindShot)
end

-- PHASE 1a — the in-cone shot MUST land. This is the instrument check: it proves a rifleman can
-- hurt this man at all, so that "no damage" in phase 2 means the arc refused him rather than
-- the measurement never working.
local function ConeShot()
	if not AtCell(ConeShooter, ConeCellX, ConeCellY) then
		Test.Skip("the in-cone shooter is at " .. ConeShooter.Location.X .. "," ..
			ConeShooter.Location.Y .. " rather than his derived cell " .. ConeCellX .. "," ..
			ConeCellY .. ". Round 4 failed exactly here, having ordered the attack from the spawn " ..
			"cell; the CONE-SHOT line below must describe the cell he fires FROM. " .. State())
		return
	end

	ReportGeometry("CONE-SHOT", ConeShooter, HouseMT, Gunner)
	local baseline = HealthOf(Gunner)
	-- Issued only after AwaitDeployment has seen the man in-world at a port, plus the settle below,
	-- so Target.FromActor(Gunner) cannot still be Invalid. Attack() only LOGS an invalid target and
	-- then queues the activity anyway (CombatProperties.cs:91-99), so without the wait the failure
	-- is silent in the verdict and visible only in lua.log.
	ConeShooter.Attack(Gunner)

	WaitUntil(HitWithin,
		function() return Gunner.IsDead or HealthOf(Gunner) < baseline end,
		StopConeShooterThenMeasure,
		function()
			Test.Fail("the IN-CONE shooter landed nothing in " .. HitWithin .. "s. ENGINE VERDICT AT " ..
				"ORDER TIME: canTarget=" .. tostring(ConeShooter.CanTarget(Gunner)) .. "; port " ..
				Test.GarrisonPortOf(Gunner, HouseMT) .. "; " .. Test.TargetableReport(Gunner, ConeShooter) ..
				" -- if canTarget is FALSE the refusal is in targeting and the report above names which " ..
				"targetable said no; if it is TRUE the refusal is downstream, in AttackBase or the " ..
				"activity, and nothing in this file's geometry is at fault. He stands on the " ..
				"same north-east diagonal as the port the Gunner is manning, 5.7 cells out, so " ..
				"GarrisonPortOccupant.TargetableBy should admit him (yaw 896 against a port at 896). " ..
				"Two readings and they need telling apart: either the arc gate now refuses " ..
				"EVERYONE - in which case the phase-2 result that follows would be a false pass and " ..
				"the rifleman control should also be red - or this is a scenario fault (he cannot " ..
				"reach, his rifle is out of range, or he never got the order). Check whether the " ..
				"rifleman control limb is green before reading this as a code regression. " .. State())
		end)
end

-- Move both shooters onto bearings derived from the port the Gunner is ACTUALLY holding, then hand
-- off to the measurement. This is what makes the two limbs test what they claim no matter which
-- port the deploy loop picked -- and it is the third attempt at that, the first two having tried to
-- pin the port by map geometry and been wrong in different ways each time.
-- A position trace that says WHY a man is not where he was sent, not just that he is not there.
-- ClickOrderAtCell with issue=false asks the real order pipeline what a click on that cell would
-- produce without issuing anything: "Move" means the order is available and he is simply walking,
-- anything else (or nil) means the pipeline is refusing the destination -- which is what an occupied
-- or unreachable cell looks like from here.
local function DescribeMove(label, actor, x, y)
	return label .. " at " .. actor.Location.X .. "," .. actor.Location.Y ..
		" wants " .. x .. "," .. y ..
		" arrived=" .. tostring(AtCell(actor, x, y)) ..
		" orderAtCell=" .. tostring(Test.ClickOrderAtCell(actor, CPos.New(x, y), "", false))
end

local function MoveTrace()
	return DescribeMove("cone", ConeShooter, ConeCellX, ConeCellY) .. " | " ..
		DescribeMove("behind", BehindShooter, BehindCellX, BehindCellY)
end

-- WAIT ON POSITION, NOT ON IsIdle. Round 4: Test.IssueMoveOrder goes through World.IssueOrder, so
-- the order sits in the queue and only becomes an activity a tick or more later, while IsIdle is
-- CurrentActivity == null -- TRUE BOTH BEFORE THE ORDER LANDS AND AFTER THE MOVE FINISHES. The wait
-- fired on its first poll and the attack was issued from the spawn cell.
--
-- Round 5 added the re-issue. The destination is derived from a port chosen at runtime, so it can
-- collide with a fixed spawn: a man ordered onto an occupied cell simply does not go, and nothing
-- retries him. Re-issuing on each trace costs nothing when he is already walking (the order
-- resolves to the same destination) and is the whole fix when the cell was blocked at order time.
local function AwaitArrival()
	local remaining = math.floor(MoveWithin * TestHarness.TicksPerSecond)
	local interval = math.floor(MoveTraceEvery * TestHarness.TicksPerSecond)
	local sinceTrace = 0
	local check

	check = function()
		if AtCell(ConeShooter, ConeCellX, ConeCellY) and AtCell(BehindShooter, BehindCellX, BehindCellY) then
			print("MOVE-ARRIVED | " .. MoveTrace())
			-- A settle beat after arrival, so the last move tick is behind us before anyone aims.
			Trigger.AfterDelay(math.floor(SettleFor * TestHarness.TicksPerSecond), ConeShot)
			return
		end

		remaining = remaining - 1
		sinceTrace = sinceTrace + 1

		if sinceTrace >= interval then
			sinceTrace = 0
			print("MOVE-TRACE | " .. MoveTrace())

			if not AtCell(ConeShooter, ConeCellX, ConeCellY) then
				Test.IssueMoveOrder(ConeShooter, CPos.New(ConeCellX, ConeCellY))
			end

			if not AtCell(BehindShooter, BehindCellX, BehindCellY) then
				Test.IssueMoveOrder(BehindShooter, CPos.New(BehindCellX, BehindCellY))
			end
		end

		if remaining <= 0 then
			Test.Skip("the shooters did not reach their derived cells within " .. MoveWithin ..
				"s. FINAL STATE: " .. MoveTrace() .. " -- orderAtCell=Move means the pipeline would " ..
				"accept the destination and he is simply too slow or blocked en route; anything else " ..
				"means it refuses that cell outright, which is what a still-occupied or unreachable " ..
				"cell looks like. The per-5s MOVE-TRACE lines in lua.log show whether he was moving " ..
				"at all. Neither limb can be measured from a bearing nobody is standing on. " .. State())
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

local function PlaceShooters()
	local yaw = PortYawOf(Gunner, HouseMT)
	if yaw == nil then
		Test.Skip("the Gunner reads as deployed but GarrisonPortOf could not name his port (" ..
			Test.GarrisonPortOf(Gunner, HouseMT) .. "), so the shooters cannot be positioned " ..
			"relative to it and nothing under test can be staged. " .. State())
		return
	end

	HeldYaw = yaw

	local cx, cy = YawToOffset(yaw, ConeDistance)
	local bx, by = YawToOffset(yaw, -BehindDistance)

	ConeCellX, ConeCellY = HouseMT.Location.X + cx, HouseMT.Location.Y + cy
	BehindCellX, BehindCellY = HouseMT.Location.X + bx, HouseMT.Location.Y + by

	Test.IssueMoveOrder(ConeShooter, CPos.New(ConeCellX, ConeCellY))
	Test.IssueMoveOrder(BehindShooter, CPos.New(BehindCellX, BehindCellY))

	print("PLACEMENT | held port " .. Test.GarrisonPortOf(Gunner, HouseMT) ..
		" | cone shooter -> " .. ConeCellX .. "," .. ConeCellY ..
		" | behind shooter -> " .. BehindCellX .. "," .. BehindCellY)

	AwaitArrival()
end

local function AwaitDeployment()
	WaitUntil(DeployWithin,
		function() return AtPort(Gunner) end,
		function()
			-- One settle beat after the port reads manned AND in-world, so the deploy's frame-end
			-- task (SetPosition, w.Add) is fully behind us before anything is aimed at him.
			Trigger.AfterDelay(math.floor(SettleFor * TestHarness.TicksPerSecond), PlaceShooters)
		end,
		function()
			Test.Skip("the Gunner never deployed to a firing port within " .. DeployWithin ..
				"s, so there was nothing at a port to shoot at. GarrisonManager only mans a port " ..
				"once a target is CONFIRMED across TargetConfirmTicks (GarrisonManager.cs:803-828), " ..
				"and the only actor any port can acquire here is Bait at 11.3 cells on the " ..
				"north-east diagonal - every other Russian is inside 60mm_Mortar's MinRange of 8c0. " ..
				"If Bait died first, or if that minimum range changed, nothing can deploy. " ..
				State())
		end)
end

WorldLoaded = function()
	TestHarness.FocusBetween(HouseMT, HouseE1)

	-- THROUGH THE ORDER LAYER, NOT MobileProperties.EnterTransport. The first version of this file
	-- used soldier.EnterTransport(house), which queues a RideTransport activity directly, and NOBODY
	-- boarded in 25 s: run 260915_175730 skipped with both houses still Neutral. Test.ClickOrder
	-- issues a real EnterTransport order through Passenger.ResolveOrder instead, and that is proven
	-- on this exact tree, map and building type -- test-garrison-hostile-cogarrison used it in run
	-- 260915_175947 and walked a rifleman SEVEN cells into a v09, flipping the house to its owner.
	-- Distance and footprint were therefore never the problem; the API was.
	--
	-- Asserting the returned order string also turns a silent staging failure into a named SKIP.
	local gunnerOrder = Test.ClickOrder(Gunner, HouseMT)
	local riflemanOrder = Test.ClickOrder(Rifleman, HouseE1)

	if gunnerOrder ~= "EnterTransport" or riflemanOrder ~= "EnterTransport" then
		Test.Skip("the enter order was not offered against a neutral house (Gunner got '" ..
			tostring(gunnerOrder) .. "', Rifleman got '" .. tostring(riflemanOrder) .. "'), so no " ..
			"garrison could be staged. EnterAlliedActorTargeter admits allied OR neutral owners " ..
			"(EnterAlliedActorTargeter.cs:49-54), so a refusal here means the entry gate changed. " ..
			State())
		return
	end

	-- Ownership IS the setup proof. DynamicOwnership flips a Neutral building to the entering
	-- soldier's player in OnPassengerEntered (GarrisonManager.cs:263-267), so "both houses read
	-- USA" states unambiguously that both men are inside - and it is not confounded by the
	-- IsDead ambiguity that makes a sheltered soldier unreadable by any other property.
	WaitUntil(SetupWithin,
		function() return OwnerOf(HouseMT) == "USA" and OwnerOf(HouseE1) == "USA" end,
		AwaitDeployment,
		function()
			Test.Skip("one or both houses never became USA-owned within " .. SetupWithin ..
				"s, so no garrison formed and nothing under test was reached. Either the men could " ..
				"not path to the buildings, or DynamicOwnership stopped claiming neutral buildings " ..
				"on entry (GarrisonManager.cs:263-267). " .. State())
		end)
end
