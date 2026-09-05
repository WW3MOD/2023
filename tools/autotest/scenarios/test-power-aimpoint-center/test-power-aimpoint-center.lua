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
-- WHAT THE VERDICT STRING CARRIES, because the harness's other artefacts are not trustworthy: for
-- each shot, the missile's last observed position before it detonated, that position's offset from
-- the building's own CenterPosition, and the damage the building took. Those three numbers are the
-- whole finding; everything else here is plumbing to produce them.
--
-- HOW THE IMPACT POSITION IS READ, and why it is a DIAGNOSTIC rather than the assertion.
-- BallisticMissileFly's final tick does SetPosition(self, targetPos) then queues a CallFunc that
-- kills the actor on the NEXT activity tick, so there is at least one whole world tick in which the
-- missile is alive and sitting exactly on the resolved aim point, and a per-tick poll sees it. But
-- "at least one" is an ordering argument, not a measurement, and a poll that lost the race would
-- report the position one tick short — up to 2400 wdist away at terminal speed, which is larger
-- than the 1448 offset under test. So the PASS/FAIL is on the damage, which is a settled number
-- read after the dust clears, and the position is printed to explain it.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * Anything about the GBU-57 or the tactical nuke. Their warheads are SpreadDamage throughout,
--     which reads zero distance anywhere inside a building's hitshape, so neither can move under
--     this change and a run against them would be green for the wrong reason.
--   * IskanderExplosion's own sizing. The 33% corner discount is a property of the warhead and it
--     is untouched here — the Iskander LAUNCHER still fires the same weapon through a direct-fire
--     path that never sees a support power order. That is filed separately.
--   * The cursor. It still draws a cell over the quadrant the mouse is on; see the note on
--     SelectGenericPowerTarget.GetCursor.

local OrderKey = "KinzhalStrike"
local MissileType = "kinzhalmissile"

-- Shot 1: the top-left corner cell of CornerVictim (footprint 30-32 x 10-12).
local CornerCellX, CornerCellY = 30, 10
-- Shot 2: the centre cell of CenterVictim (footprint 30-32 x 22-24).
local CenterCellX, CenterCellY = 31, 23

-- Ticks from an order to reading the victim's health. The flight is ~16 ticks at Speed 2000 over
-- ~30 cells; Warhead@Shockwave has StartDelay 2 and WaveSpeed 5 out to MaxRadius 4c0, so its last
-- band lands around tick 22. 90 is four times the longest of those.
local SettleTicks = 90
local ObserveTicks = 400

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
		victim = nil,
		status = "never-called", orderTick = nil,
		lastMissilePos = nil, startHealth = 0, endHealth = nil, damage = -1,
	},
	{
		name = "centre",
		cellX = CenterCellX, cellY = CenterCellY,
		victim = nil,
		status = "never-called", orderTick = nil,
		lastMissilePos = nil, startHealth = 0, endHealth = nil, damage = -1,
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

-- Keep the newest position of any live missile. Only one is ever in the world at a time: the
-- scenario waits SettleTicks after each order before issuing the next, and DefaultCash 0 means
-- nothing else can produce one.
local function trackMissile(shot)
	local missiles = Russia.GetActorsByType(MissileType)
	for i = 1, #missiles do
		local m = missiles[i]
		if not m.IsDead then
			local p = m.CenterPosition
			shot.lastMissilePos = { X = p.X, Y = p.Y }
		end
	end
end

local function pollTick()
	tick = tick + 1

	if shotIndex > #shots then
		return
	end

	local shot = shots[shotIndex]

	if shot.orderTick == nil then
		if shotIndex == 1 then
			stateAtStart = Test.GetSupportPowerState(Russia, OrderKey)
		end

		shot.startHealth = healthOf(shot.victim)
		shot.status = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(shot.cellX, shot.cellY))
		if shot.status == "issued" then
			shot.orderTick = tick
		else
			-- A refused order ends the run immediately rather than burning the whole budget: there
			-- is nothing left to observe and the status string already says why.
			shotIndex = #shots + 1
		end

		return
	end

	trackMissile(shot)

	if tick - shot.orderTick >= SettleTicks then
		shot.endHealth = healthOf(shot.victim)
		shot.damage = shot.startHealth - shot.endHealth
		shotIndex = shotIndex + 1
	end
end

local function shotText(shot)
	local centre = shot.victim.IsDead and nil or shot.victim.CenterPosition
	local offset = hDist(shot.lastMissilePos, centre)

	return shot.name .. ": clicked " .. shot.cellX .. "," .. shot.cellY
		.. " order=" .. shot.status .. "@t" .. n(shot.orderTick)
		.. " impact=" .. posText(shot.lastMissilePos)
		.. " victimCentre=" .. (centre ~= nil and posText(centre) or "DEAD-unreadable")
		.. " offset=" .. offset .. "wd"
		.. " hp " .. shot.startHealth .. "->" .. n(shot.endHealth)
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
		if shots[i].status ~= "issued" then
			Test.Fail("the " .. shots[i].name .. " shot was refused: " .. shots[i].status
				.. ". 'not-ready:<n>' means the ChargeInterval override in rules.yaml did not take;"
				.. " 'unknown-power' means the OrderName moved. || " .. summary)
			return
		end

		if shots[i].damage < 0 then
			Test.Fail("the " .. shots[i].name .. " shot never settled within " .. ObserveTicks
				.. " ticks. || " .. summary)
			return
		end
	end

	-- 1. Did the corner shot deliver a full-strength warhead? This is the assertion the change
	-- exists for. Below MinCornerDamage means the impact was NOT on the building's centre and
	-- TargetDamageWarhead's CenterProximityPercent discounted it.
	if corner.damage < MinCornerDamage then
		Test.Fail("a Kinzhal clicked on the CORNER cell " .. CornerCellX .. "," .. CornerCellY
			.. " of a 3x3 Logistics Center delivered only " .. corner.damage
			.. " damage (needs >= " .. MinCornerDamage .. "). The impact offset from the building's"
			.. " centre is printed below: ~1448 means the order's target was never snapped —"
			.. " check SupportPowerInfo.SnapToActorCenter and SupportPowerAimPoint.Resolve, and that"
			.. " ActorMap.GetActorsAt still answers for every footprint cell. || " .. summary)
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

	TestHarness.FocusBetween(CornerVictim, CenterVictim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
