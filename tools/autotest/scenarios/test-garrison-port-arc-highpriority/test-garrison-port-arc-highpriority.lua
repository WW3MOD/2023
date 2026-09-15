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

local SetupWithin = 25      -- s for two men to walk in and claim their houses
local DeployWithin = 25     -- s for GarrisonManager to confirm a target and man a port
local HitWithin = 20        -- s for an in-arc shooter to land its first round
local QuietFor = 12         -- s a blocked shooter is given to prove it lands nothing
local SettleFor = 3         -- s after a Stop order, before a fresh health baseline is taken

local function OwnerOf(actor)
	local o = actor.Owner
	if o == nil then
		return "<none>"
	end

	return o.InternalName
end

-- A deployed port soldier is IN WORLD and carries garrisoned-at-port; a shelter occupant is out
-- of world entirely. ConditionCount is read rather than IsInWorld because it is the same
-- condition that gates both traits under test, so this asks exactly the question the assertions
-- depend on. PITFALL: do NOT use IsDead to tell these apart - a soldier in a Cargo hold reads
-- IsDead == true (DOCS/recipes/AUTOTEST.md), so shelter and casualty are indistinguishable.
local function AtPort(soldier)
	return Test.ConditionCount(soldier, "garrisoned-at-port") > 0
end

local function HealthOf(actor)
	if actor == nil or actor.IsDead then
		return -1
	end

	return actor.Health
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
	local baseline = HealthOf(Gunner)
	BehindShooter.Attack(Gunner)

	HoldAndCompare(QuietFor, Gunner, baseline, function(worst)
		-- Order matters: the port check comes FIRST on the pass path but the damage check comes
		-- first overall, because damage landing is a finding whatever else is true.
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
	local baseline = HealthOf(Gunner)
	ConeShooter.Attack(Gunner)

	WaitUntil(HitWithin,
		function() return Gunner.IsDead or HealthOf(Gunner) < baseline end,
		StopConeShooterThenMeasure,
		function()
			Test.Fail("the IN-CONE shooter landed nothing in " .. HitWithin .. "s. He stands on the " ..
				"same north-east diagonal as the port the Gunner is manning, 5.7 cells out, so " ..
				"GarrisonPortOccupant.TargetableBy should admit him (yaw 896 against a port at 896). " ..
				"Two readings and they need telling apart: either the arc gate now refuses " ..
				"EVERYONE - in which case the phase-2 result that follows would be a false pass and " ..
				"the rifleman control should also be red - or this is a scenario fault (he cannot " ..
				"reach, his rifle is out of range, or he never got the order). Check whether the " ..
				"rifleman control limb is green before reading this as a code regression. " .. State())
		end)
end

local function AwaitDeployment()
	WaitUntil(DeployWithin,
		function() return AtPort(Gunner) end,
		ConeShot,
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

	Gunner.EnterTransport(HouseMT)
	Rifleman.EnterTransport(HouseE1)

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
