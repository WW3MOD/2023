-- ASSERTING AUTOTEST — does a strike clicked on a building's CORNER cell land on its CENTRE?
--
-- THE SHAPE OF THE RUN. Two identical Logistics Centers, two Kinzhal strikes, one difference: the
-- first is aimed at a corner footprint cell (30,10 of a building covering 30-32 x 10-12) and the
-- second at the other building's own centre cell (31,23 of 30-32 x 22-24). With
-- SupportPowerInfo.SnapToActorCenter at its shipped default of true, both orders resolve to their
-- building's CenterPosition and both buildings take the same damage.
--
-- THE CONTROL IS IN THIS RUN, NOT IN THE OTHER SCENARIO. The centre-clicked building is fired at by
-- the same power, on the same tick budget, in the same build. So if the two damages differ, the
-- clicked cell is the only thing that can explain it. test-power-aimpoint-unsnapped is the same
-- scenario with one rules line flipped and is where the numbers are expected to come apart; it
-- corroborates, it does not carry this verdict.
--
-- ================================================================================================
-- REWRITTEN 2026-09-05 AFTER A FAILED RUN, AND THE REWRITE IS STRUCTURAL RATHER THAN A WIDER BUDGET
-- ================================================================================================
-- The first version fixed SettleTicks at 90 and read each victim's health 90 ticks after ordering.
-- `MissileDelay: 150` landed on the Kinzhal in the same merge (player.yaml:137): the missile is
-- created at order time but held OUT OF THE WORLD by SpawnActorEffect for 150 ticks, and
-- Player.GetActorsByType filters on actor.IsInWorld (PlayerProperties.cs:100), so for those 150
-- ticks the poller correctly sees nothing. Both windows closed before either missile existed and
-- the verdict read `damage=0` twice — a run that measured nothing, not a feature that failed.
--
-- It also ALIASED. The second shot was ordered at t92 while the first missile was still inbound, so
-- the second shot's position tracker sampled the FIRST shot's missile: the failing verdict shows
-- `centre: ... impact=31232,10752`, which is beside the first building, not the second.
--
-- SO THE WINDOWS ARE NO LONGER TIMERS. Each shot now advances on OBSERVABLES — a missile appeared,
-- the missile is gone, the dust has settled — and the budgets below exist only to fail the run with
-- a diagnosis when an observable never arrives. A shot is not ordered until the world holds zero
-- missiles, and that emptiness is ASSERTED rather than assumed, so the aliasing above cannot recur
-- silently. Padding the old numbers would have hidden both faults behind one green run.
--
-- THE DERIVATION, from the shipped values rather than from the old constants:
--   MissileDelay 150 ticks                                      (player.yaml:137)
--   PreLaunchTicks 0 — LaunchRiseTicks is 0, and PreLaunchTicks
--     is LaunchRiseTicks > 0 ? ... : 0                          (BallisticMissile.cs:85)
--   flight: entry cell 1,17 -> both aim points, hDist 31328 at
--     Speed 2000 with Acceleration 0, so EstimateArcTicks takes
--     the hDist / Speed branch = 15; the terminal-boost loop
--     simulates 16                                              (BallisticMissileFly.cs:73-101)
--   damage: Warhead@Shockwave StartDelay 2 + MaxRadius 4 cells
--     at WaveSpeed 5 TICKS PER CELL = 22                        (weapons-explosions.yaml:551-556)
--   => order -> impact 166 ticks, order -> settled 188, and the two shots are sequential.
--
-- HOW THE IMPACT POSITION IS READ, and why it is still a DIAGNOSTIC rather than the assertion.
-- BallisticMissileFly's final tick does SetPosition(self, targetPos), queues a CallFunc and returns
-- true; the CallFunc kills the actor on the NEXT activity tick. So the missile sits alive on the
-- resolved aim point for one whole world tick, and a per-tick poll samples it under either tick
-- ordering — if Lua ticks after the actors it sees the position on tick T, and if Lua ticks before
-- them it sees it on T+1, before the kill lands. That closes the ordering hole the first version's
-- header worried about. It is still an argument about ordering rather than a measurement of it, so
-- the PASS/FAIL stays on the damage, which is a settled number read after everything has landed.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * Anything about the GBU-57 or the tactical nuke. Their warheads are SpreadDamage throughout,
--     which reads zero distance anywhere inside a building's hitshape, so neither can move under
--     this change and a run against them would be green for the wrong reason.
--   * The tactical nuke's airburst. DetonationAltitude is set on that power alone (player.yaml:305);
--     the Kinzhal has none, so this scenario's aim point is a ground burst exactly as before and
--     the airburst work cannot reach it.
--   * IskanderExplosion's own sizing. The 33% corner discount is a property of the warhead and it
--     is untouched here — the Iskander LAUNCHER still fires the same weapon through a direct-fire
--     path that never sees a support power order. That is filed separately.
--   * The cursor. It still draws a cell over the quadrant the mouse is on; see the note on
--     SelectGenericPowerTarget.GetCursor.
--
-- WHY 30,10 AND NOT ANOTHER CORNER, now that a run has been through here. In the shipped footprint
-- `=+= +++ =+=` all four corners are `=` (OccupiedPassable) — walkable cells inside the building,
-- which BuildingInfo.OccupiedTiles deliberately omits and which are therefore absent from the
-- ActorMap influence layer. The first resolver asked only that layer, found nothing at 30,10, and
-- left the order unsnapped: this scenario measured 24820 against a centred 60000 and named the bug
-- itself. The clicked cell is now load-bearing in two ways rather than one — worst proximity AND
-- passable — and SupportPowerAimPointTest pins the footprint so a change cannot quietly turn this
-- run green while it stops testing anything.

local OrderKey = "KinzhalStrike"
local MissileType = "kinzhalmissile"

-- Shot 1: the top-left corner cell of CornerVictim (footprint 30-32 x 10-12).
local CornerCellX, CornerCellY = 30, 10
-- Shot 2: the centre cell of CenterVictim (footprint 30-32 x 22-24).
local CenterCellX, CenterCellY = 31, 23

-- Order -> the missile appearing in the world. Expected 150 exactly: SpawnActorEffect cannot take
-- longer than the count it was handed. 240 is a diagnosis window, not slack for a slow tool — if it
-- is exceeded, MissileDelay has moved again and the failure message says so.
local ArrivalBudget = 240
-- First sighting -> the missile gone. Expected ~16.
local FlightBudget = 90
-- Missile gone -> read the victim's health. Expected 22 for the outermost shockwave band.
local DamageSettleTicks = 45
-- 2 x (240 + 90 + 45) = 750 worst case for two sequential shots, plus slack.
local ObserveTicks = 900

-- The corner shot must arrive at full strength. Expected ~54000 from Warhead@Target alone (the
-- building has 60000 HP and may simply die, in which case the recorded damage is 60000). The
-- unsnapped number this must clear is ~24800.
local MinCornerDamage = 40000

-- The two shots are identical once snapped, and nothing in the chain is random
-- (RandomDamagePercentFrom is 100 on every IskanderExplosion warhead), so the honest expectation is
-- zero. 2000 is slack for a Shockwave band landing on a different tick, not a tolerance on the
-- effect being measured — the effect is 36000 wide.
local MaxDamageGap = 2000

local tick = 0
local Russia
local stateAtStart = "never-read"
local finished = false

local shots = {
	{
		name = "corner",
		cellX = CornerCellX, cellY = CornerCellY,
		victim = nil, phase = "pending",
		status = "never-called", orderTick = nil,
		missilesAtOrder = -1, firstSeenTick = nil, goneTick = nil,
		lastMissilePos = nil, victimCentre = nil,
		startHealth = 0, healthAtOrder = -1, endHealth = nil, damage = -1,
	},
	{
		name = "centre",
		cellX = CenterCellX, cellY = CenterCellY,
		victim = nil, phase = "pending",
		status = "never-called", orderTick = nil,
		missilesAtOrder = -1, firstSeenTick = nil, goneTick = nil,
		lastMissilePos = nil, victimCentre = nil,
		startHealth = 0, healthAtOrder = -1, endHealth = nil, damage = -1,
	},
}

local shotIndex = 1

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

local function healthOf(actor)
	if actor.IsDead then
		return 0
	end

	return actor.Health
end

-- Live missiles owned by Russia. GetActorsByType already filters on IsDead and IsInWorld
-- (PlayerProperties.cs:100), which is exactly the "exists on the map right now" question both the
-- arrival check and the emptiness assertion want.
local function liveMissiles()
	return Russia.GetActorsByType(MissileType)
end

-- Horizontal distance between two positions, in world units. Z is ignored on purpose: it is what
-- CenterProximityPercent ignores too (the percentage comes from v.HorizontalLength), so a vertical
-- difference is not part of the quantity under test.
local function hDist(a, b)
	if a == nil or b == nil then
		return -1
	end

	local dx = a.X - b.X
	local dy = a.Y - b.Y
	return math.floor(math.sqrt(dx * dx + dy * dy) + 0.5)
end

local function posText(p)
	if p == nil then
		return "none"
	end

	return p.X .. "," .. p.Y
end

local function pollTick()
	tick = tick + 1

	if shotIndex > #shots then
		return
	end

	local shot = shots[shotIndex]
	local missiles = liveMissiles()

	if shot.phase == "pending" then
		-- The world must be EMPTY of missiles before this shot is ordered. Recorded rather than
		-- merely waited for: the first version of this scenario aliased the second shot onto the
		-- first shot's missile, and a number in the verdict is what makes that visible next time.
		shot.missilesAtOrder = #missiles
		if #missiles > 0 then
			return
		end

		if shotIndex == 1 then
			stateAtStart = Test.GetSupportPowerState(Russia, OrderKey)
		end

		shot.healthAtOrder = healthOf(shot.victim)
		shot.status = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(shot.cellX, shot.cellY))
		if shot.status == "issued" then
			shot.orderTick = tick
			shot.phase = "waiting"
		else
			-- A refused order ends the run immediately rather than burning the whole budget: there
			-- is nothing left to observe and the status string already says why.
			shot.phase = "done"
			shotIndex = #shots + 1
		end

		return
	end

	if shot.phase == "waiting" then
		if #missiles > 0 then
			shot.firstSeenTick = tick
			shot.phase = "flying"
		elseif tick - shot.orderTick > ArrivalBudget then
			shot.phase = "done"
			shotIndex = shotIndex + 1
		end

		return
	end

	if shot.phase == "flying" then
		if #missiles > 0 then
			local p = missiles[1].CenterPosition
			shot.lastMissilePos = { X = p.X, Y = p.Y }

			if tick - shot.firstSeenTick > FlightBudget then
				shot.phase = "settling"
				shot.goneTick = tick
			end
		else
			shot.goneTick = tick
			shot.phase = "settling"
		end

		return
	end

	if shot.phase == "settling" and tick - shot.goneTick >= DamageSettleTicks then
		shot.endHealth = healthOf(shot.victim)
		shot.damage = shot.startHealth - shot.endHealth
		shot.phase = "done"
		shotIndex = shotIndex + 1
	end
end

local function shotText(shot)
	return shot.name .. ": clicked " .. shot.cellX .. "," .. shot.cellY
		.. " order=" .. shot.status .. "@t" .. n(shot.orderTick)
		.. " missilesAtOrder=" .. shot.missilesAtOrder
		.. " spawn@t" .. n(shot.firstSeenTick) .. " gone@t" .. n(shot.goneTick)
		.. " impact=" .. posText(shot.lastMissilePos)
		.. " victimCentre=" .. posText(shot.victimCentre)
		.. " offset=" .. hDist(shot.lastMissilePos, shot.victimCentre) .. "wd"
		.. " hp " .. shot.startHealth .. "(atOrder " .. shot.healthAtOrder .. ")->" .. n(shot.endHealth)
		.. " damage=" .. shot.damage
end

local function finish()
	local corner = shots[1]
	local centre = shots[2]
	local gap = math.abs(corner.damage - centre.damage)

	local summary = "state=" .. stateAtStart
		.. " | " .. shotText(corner)
		.. " | " .. shotText(centre)
		.. " | gap=" .. gap
		.. " | SRs own=" .. (OwnSR.IsDead and "DEAD" or (OwnSR.Health .. "hp"))
		.. " opp=" .. (OpponentSR.IsDead and "DEAD" or (OpponentSR.Health .. "hp"))
		.. " | observed=" .. tick .. "t"

	for i = 1, #shots do
		local s = shots[i]

		if s.status ~= "issued" then
			Test.Fail("the " .. s.name .. " shot was refused: " .. s.status
				.. ". 'not-ready:<n>' means the ChargeInterval override in rules.yaml did not take;"
				.. " 'unknown-power' means the OrderName moved. || " .. summary)
			return
		end

		if s.missilesAtOrder ~= 0 then
			Test.Fail("the " .. s.name .. " shot was ordered with " .. s.missilesAtOrder
				.. " missile(s) already in the world, so its position tracker cannot be trusted to"
				.. " be following its own missile. || " .. summary)
			return
		end

		-- The failure the first version of this scenario actually hit, now named rather than
		-- reported as a mysterious zero.
		if s.firstSeenTick == nil then
			Test.Fail("the " .. s.name .. " shot was accepted but no " .. MissileType
				.. " ever entered the world within " .. ArrivalBudget .. " ticks of the order."
				.. " MissileStrikePower holds the missile out of the world for MissileDelay ticks"
				.. " (150 on the Kinzhal, player.yaml:137) and GetActorsByType filters on IsInWorld,"
				.. " so if that value has grown past this budget, raise ArrivalBudget rather than"
				.. " reading this as a delivery failure. || " .. summary)
			return
		end

		if s.damage < 0 then
			Test.Fail("the " .. s.name .. " shot never settled within " .. ObserveTicks
				.. " ticks. || " .. summary)
			return
		end
	end

	-- The two buildings are 12 cells apart and the widest Kinzhal warhead reaches 4, so the second
	-- victim must be untouched when its own shot is ordered. If it is not, the two shots are not
	-- independent and neither number below means what it says.
	if centre.healthAtOrder ~= centre.startHealth then
		Test.Fail("the centre victim had already taken " .. (centre.startHealth - centre.healthAtOrder)
			.. " damage before its own shot was ordered, so the first strike reached it. The two"
			.. " buildings are 12 cells apart against a 4-cell MaxRadius — check the placements in"
			.. " map.yaml. || " .. summary)
		return
	end

	-- 1. Did the corner shot deliver a full-strength warhead? This is the assertion the change
	-- exists for. Below MinCornerDamage means the impact was NOT on the building's centre and
	-- TargetDamageWarhead's CenterProximityPercent discounted it.
	if corner.damage < MinCornerDamage then
		Test.Fail("a Kinzhal clicked on the CORNER cell " .. CornerCellX .. "," .. CornerCellY
			.. " of a 3x3 Logistics Center delivered only " .. corner.damage
			.. " damage (needs >= " .. MinCornerDamage .. "). The impact offset from the building's"
			.. " centre is printed below: ~1448 means the order's target was never snapped, and the"
			.. " known cause is the clicked cell being invisible to whichever index"
			.. " SupportPowerAimPoint.CandidatesAt asked. 30,10 is an `=` OccupiedPassable cell,"
			.. " which BuildingInfo.OccupiedTiles omits and BuildingInfluence includes — so check"
			.. " that BOTH indices are still being read, then SupportPowerInfo.SnapToActorCenter."
			.. " || " .. summary)
		return
	end

	-- 2. And did it deliver the SAME warhead the centre-clicked control got? This is the half that
	-- makes the run about the clicked cell rather than about a damage number: a change that raised
	-- both would pass check 1 and fail nothing, and this catches it.
	if gap > MaxDamageGap then
		Test.Fail("the corner-clicked and centre-clicked Logistics Centers took different damage: "
			.. corner.damage .. " vs " .. centre.damage .. " (gap " .. gap .. ", allowance "
			.. MaxDamageGap .. "). With the aim point snapped to the actor's centre both orders"
			.. " resolve to the same position, so which of the nine cells was clicked must not be"
			.. " visible in the damage at all. || " .. summary)
		return
	end

	Test.Pass("aim-point snap: a corner click and a centre click on identical 3x3 buildings"
		.. " delivered " .. corner.damage .. " and " .. centre.damage
		.. " (gap " .. gap .. "). || " .. summary)
end

local function step()
	pollTick()

	if not finished and (shotIndex > #shots or tick >= ObserveTicks) then
		finished = true
		finish()
		return
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	Russia = Player.GetPlayer("Russia")
	if Russia == nil then
		Test.Fail("Russia player not found")
		return
	end

	if CornerVictim == nil or CenterVictim == nil then
		Test.Fail("a Logistics Center is missing from the map")
		return
	end

	shots[1].victim = CornerVictim
	shots[2].victim = CenterVictim

	-- Captured NOW, while both buildings are certainly alive. CenterPosition on a destroyed actor
	-- is unreadable, and a destroyed building is the expected outcome of a snapped hit — so reading
	-- the centre at verdict time is exactly when it is least likely to be available.
	for i = 1, #shots do
		local p = shots[i].victim.CenterPosition
		shots[i].victimCentre = { X = p.X, Y = p.Y }
		shots[i].startHealth = shots[i].victim.Health
	end

	TestHarness.FocusBetween(CornerVictim, CenterVictim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
