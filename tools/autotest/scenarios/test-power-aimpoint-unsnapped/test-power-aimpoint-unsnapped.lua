-- ASSERTING AUTOTEST — with the aim-point snap OFF, does a corner click really cost two thirds?
--
-- THIS SCENARIO ASSERTS A DEFECT, WHICH IS UNUSUAL, SO BE CLEAR WHY. The corner-hit discount was
-- derived statically by a worker on 2026-09-04 (WORKSPACE/DISCOVERIES.md) and nobody had watched it
-- happen in a running game. `SnapToActorCenter: false` in this scenario's rules.yaml restores the
-- exact behaviour every support power had before that field existed, so this run is the shipped
-- pre-change game. If it goes GREEN the derivation is confirmed in a real world with real warheads.
-- If it goes RED because the two shots came out EQUAL, the derivation is wrong, the feature this
-- pair was built around is a convenience rather than a bug fix, and that is the more valuable
-- finding of the two — say so rather than tuning the thresholds until it passes.
--
-- IT IS OTHERWISE test-power-aimpoint-center, LINE FOR LINE. Same map, same two buildings, same two
-- clicked cells, same power, same settle window, same verdict fields in the same order. Only the
-- expectations at the bottom differ, because only one rules line differs.
--
-- THE EXPECTED NUMBERS, for a 3x3 Logistics Center (HitShape Rectangle +/-1536, half-diagonal 2172;
-- CenterOffset a full cell diagonally, |(1024,1024)| = 1448):
--     centre click -> offset 0    -> proximity 100% -> Warhead@Target 54000 -> total ~61000, a kill
--     corner click -> offset 1448 -> proximity  33% -> Warhead@Target ~17800 -> total ~24800
-- Read the printed `offset=` field first: it is the direct measurement, and it should be ~0 for the
-- centre shot and ~1448 for the corner shot. The damage is the consequence.

local OrderKey = "KinzhalStrike"
local MissileType = "kinzhalmissile"

-- Shot 1: the top-left corner cell of CornerVictim (footprint 30-32 x 10-12).
local CornerCellX, CornerCellY = 30, 10
-- Shot 2: the centre cell of CenterVictim (footprint 30-32 x 22-24).
local CenterCellX, CenterCellY = 31, 23

local SettleTicks = 90
local ObserveTicks = 400

-- The centre-clicked control must still deliver the warhead's stated damage: without this the run
-- could go green because BOTH shots were feeble, which would mean something else entirely is wrong.
local MinCenterDamage = 40000
-- The corner-clicked shot must NOT. Expected ~24800 against 60000 HP.
local MaxCornerDamage = 35000
-- And the two must be far enough apart that the difference cannot be shockwave timing noise.
local MinDamageGap = 20000

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
	local gap = centre.damage - corner.damage

	local summary = "snap=OFF state=" .. stateAtStart
		.. " | " .. shotText(corner)
		.. " | " .. shotText(centre)
		.. " | gap=" .. gap
		.. " | SRs own=" .. (OwnSR.IsDead and "DEAD" or (OwnSR.Health .. "hp"))
		.. " opp=" .. (OpponentSR.IsDead and "DEAD" or (OpponentSR.Health .. "hp"))
		.. " | observed=" .. tick .. "t"

	for i = 1, #shots do
		if shots[i].status ~= "issued" then
			Test.Fail("the " .. shots[i].name .. " shot was refused: " .. shots[i].status
				.. ". || " .. summary)
			return
		end

		if shots[i].damage < 0 then
			Test.Fail("the " .. shots[i].name .. " shot never settled within " .. ObserveTicks
				.. " ticks. || " .. summary)
			return
		end
	end

	-- 1. The control first. A feeble centre shot means the warhead, the flight or the settle window
	-- is wrong, and nothing below it would mean anything.
	if centre.damage < MinCenterDamage then
		Test.Fail("the CENTRE-clicked control only delivered " .. centre.damage .. " damage (needs >= "
			.. MinCenterDamage .. "). This run cannot say anything about the corner shot until the"
			.. " control lands properly — check the flight completed and the settle window is long"
			.. " enough. || " .. summary)
		return
	end

	-- 2. The finding. If this fails, the 33% corner discount does NOT happen in a running game and
	-- the DISCOVERIES entry it comes from is wrong.
	if corner.damage > MaxCornerDamage then
		Test.Fail("with the snap OFF, a CORNER click still delivered " .. corner.damage
			.. " damage (expected <= " .. MaxCornerDamage .. ", ~24800). THIS DISPROVES the"
			.. " corner-hit derivation in WORKSPACE/DISCOVERIES.md 2026-09-04: if an unsnapped"
			.. " corner click hits as hard as a centred one, then SupportPowerAimPoint is a"
			.. " convenience and not a damage fix, and the doc entry needs correcting. Report that"
			.. " rather than widening this threshold. || " .. summary)
		return
	end

	if gap < MinDamageGap then
		Test.Fail("the two shots differed by only " .. gap .. " (needs >= " .. MinDamageGap
			.. "). Both thresholds above passed, so this is a narrow-margin result rather than a"
			.. " clean one — treat the corner-hit derivation as unconfirmed. || " .. summary)
		return
	end

	Test.Pass("corner-hit defect reproduced with the snap off: corner click " .. corner.damage
		.. " vs centre click " .. centre.damage .. " on identical 3x3 buildings (gap " .. gap
		.. "). || " .. summary)
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
