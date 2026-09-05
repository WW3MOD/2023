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
-- clicked cells, same power, same observable-driven phases, same verdict fields in the same order.
-- Only the expectations at the bottom differ, because only one rules line differs.
--
-- REWRITTEN 2026-09-05 ALONGSIDE ITS SIBLING, and for the same reason: `MissileDelay: 150` landed on
-- the Kinzhal in the same merge (player.yaml:137), the missile is held out of the world by
-- SpawnActorEffect for those ticks, and Player.GetActorsByType filters on IsInWorld
-- (PlayerProperties.cs:100) — so a fixed 90-tick settle window closed before anything existed and
-- the sibling read `damage=0` twice. Both files now advance on OBSERVABLES (a missile appeared, the
-- missile is gone, the dust settled) and order a shot only into a world holding zero missiles,
-- which is asserted rather than assumed. This scenario was never run with the stale budget; it is
-- rewritten because running it with one would have proved nothing.
--
-- THE DERIVATION, from the shipped values:
--   MissileDelay 150 + PreLaunchTicks 0 (BallisticMissile.cs:85) + ~16 ticks of flight
--   (hDist ~31000 at Speed 2000, Acceleration 0) = ~166 ticks order -> impact; + 22 ticks for
--   Warhead@Shockwave's outermost band (StartDelay 2 + 4 cells at WaveSpeed 5 TICKS PER CELL)
--   = ~188 order -> settled, twice, sequentially.
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

local ArrivalBudget = 240
local FlightBudget = 90
local DamageSettleTicks = 45
local ObserveTicks = 900

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

local function liveMissiles()
	return Russia.GetActorsByType(MissileType)
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

local function pollTick()
	tick = tick + 1

	if shotIndex > #shots then
		return
	end

	local shot = shots[shotIndex]
	local missiles = liveMissiles()

	if shot.phase == "pending" then
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
	local gap = centre.damage - corner.damage

	local summary = "snap=OFF state=" .. stateAtStart
		.. " | " .. shotText(corner)
		.. " | " .. shotText(centre)
		.. " | gap=" .. gap
		.. " | SRs own=" .. (OwnSR.IsDead and "DEAD" or (OwnSR.Health .. "hp"))
		.. " opp=" .. (OpponentSR.IsDead and "DEAD" or (OpponentSR.Health .. "hp"))
		.. " | observed=" .. tick .. "t"

	for i = 1, #shots do
		local s = shots[i]

		if s.status ~= "issued" then
			Test.Fail("the " .. s.name .. " shot was refused: " .. s.status .. ". || " .. summary)
			return
		end

		if s.missilesAtOrder ~= 0 then
			Test.Fail("the " .. s.name .. " shot was ordered with " .. s.missilesAtOrder
				.. " missile(s) already in the world, so its position tracker cannot be trusted to"
				.. " be following its own missile. || " .. summary)
			return
		end

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

	if centre.healthAtOrder ~= centre.startHealth then
		Test.Fail("the centre victim had already taken " .. (centre.startHealth - centre.healthAtOrder)
			.. " damage before its own shot was ordered, so the first strike reached it. || " .. summary)
		return
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

	-- Captured NOW, while both buildings are certainly alive. CenterPosition on a destroyed actor is
	-- unreadable, and the centre-clicked control is expected to die.
	for i = 1, #shots do
		local p = shots[i].victim.CenterPosition
		shots[i].victimCentre = { X = p.X, Y = p.Y }
		shots[i].startHealth = shots[i].victim.Health
	end

	TestHarness.FocusBetween(CornerVictim, CenterVictim)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
