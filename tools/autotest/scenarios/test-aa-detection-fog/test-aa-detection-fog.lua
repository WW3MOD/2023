-- DIAGNOSTIC AUTOTEST — fog ON. Companion to test-aa-autotarget-thru-trees.
--
-- That scenario ran fog OFF to isolate the line-of-fire gate, and returned a
-- result that killed the LOS explanation of the user report: a shooter whose
-- line is genuinely blocked fires on NEITHER path and moves on NEITHER, because
-- MoveWithinRange.ShouldStop is a pure distance test (MoveWithinRange.cs:38-43,
-- 73-76) and cannot clear line of sight. The reported symptom — auto dead, a
-- manual order fires instantly — is therefore NOT an LOS signature.
--
-- Fog off also meant GetVisibility took the "explored => 10" branch
-- (MapLayers.cs:611-618), so every lane read vis10 and the DETECTION gate never
-- had a chance to fire. That left hypothesis (2) untested rather than refuted.
-- This scenario tests it.
--
-- THE STORY UNDER TEST, stated so it can lose:
--   fog + tree-degraded vision leaves the aircraft undetected, ChooseTarget
--   drops it at CanBeViewedByPlayer (AutoTarget.cs:1066), and the unit never
--   engages — but the aircraft stays CLICKABLE (Detectable.ModifyRender
--   :222-234 hides it while ModifyScreenBounds :247-250 still returns bounds),
--   so a manual order fires at once, from where the unit stands, no movement.
--
-- HOW IT CAN LOSE (this is the point — the design is built to falsify):
--   * lane 0 carries ZERO trees. If it ALSO fails to auto-engage, then
--     tree-degraded vision is not the mechanism and plain fog-vs-aircraft
--     detection is — a different and more general bug.
--   * if the blind lane's MANUAL shot also fails, either line of fire is not
--     actually clear (it is predicted to be exactly zero here) or something
--     other than detection is gating.
--   * if the blind lane auto-engages anyway, the vision arithmetic below is
--     wrong and the hypothesis is dead.
--
-- LINE OF FIRE IS HELD AT ZERO BY CONSTRUCTION. The two shadow channels read
-- the same DensityLayer but integrate differently: airborne counts only cells
-- with t > 0.75 (Map.cs:1160-1170), ground counts every cell between the
-- endpoints (Map.cs:1150). All trees here sit at d >= 5 from the shooter on a
-- 20-cell line, i.e. t <= 0.75, so they are excluded from the airborne channel
-- (the test is a strict `512 > 2048*(1-t)`) and contribute fully to vision.
-- The manual phase re-proves it empirically rather than trusting the algebra.
--
-- PREDICTION. Vision strength decays with range (defaults.yaml:47-86): a
-- 20-cell line starts at strength 4. MapLayers subtracts groundShadow and
-- floors at 1 (MapLayers.cs:371-374), and detection needs resolved visibility
-- STRICTLY GREATER than Detectable.Vision = 2 (MapLayers.cs:579 is
-- `> visibility`, not >=) i.e. >= 3:
--   lane 0: 0 trees -> density  0 -> gs 0 -> vis 4 -> DETECTED  (control)
--   lane 1: 1 tree  -> density 10 -> gs 1 -> vis 3 -> DETECTED
--   lane 2: 2 trees -> density 20 -> gs 2 -> vis 2 -> BLIND
-- Note the asymmetry that makes this worth testing: two tree cells BLIND the
-- unit, while the companion test measured two tree cells as comfortably
-- non-blocking for line of fire. Vision fails first.

-- MEASURED 2026-08-10 (seed 1152069348). The vision arithmetic held exactly,
-- the control behaved, and the HYPOTHESIS STILL LOST on its second half:
--
--   lane  trees  density  gs  vision  detected  auto   manual  cells moved
--     0     0       0      0     4       yes    fire    fire        0
--     1     1      10      1     3       yes    fire    fire        0
--     2     2      20      2     2       NO     ----    ----        0
--   overflight on lane 2: acquired at 19 cells, fired at 14 cells.
--
-- Lane 0 auto-engaged, so plain fog is not the cause and tree-degraded vision
-- really is the blinding mechanism. But lane 2's MANUAL order also did nothing:
-- Attack.Tick sets useLastVisibleTarget from targetIsHiddenActor, and with an
-- actor that was never seen lastVisibleTarget is invalid, so
-- `if (useLastVisibleTarget && !lastVisibleTarget.IsValidFor(self)) return true;`
-- (Attack.cs:154-155) ends the activity on its first tick. forceAttack does not
-- appear in that path, so Ctrl+click cannot rescue it either.
--
-- CONSEQUENCE, taken together with the companion scenario: BOTH candidate gates
-- fail closed on BOTH paths. Neither blocked line of fire nor failed detection
-- can produce "auto never engaged but my manual order fired instantly from the
-- same cell". That report describes a target that was detected AND in clear
-- line of fire, so the cause is elsewhere in ChooseTarget.
--
-- Keep both scenarios as guards: if lane 2 ever fires on the manual order, the
-- hidden-target early-out has changed and this comment is stale.

local AaRow = 28
local AirRow = 8
local AirAltitude = 1280

-- Detectable.Vision for an airborne aircraft: the templates do not override it
-- and the ground modifier requires !airborne (aircraft.yaml:42-48).
local DetectableVision = 2

local AutoPhaseSeconds = 10
local ManualPhaseSeconds = 14
local PassPhaseSeconds = 22

local Lanes = {
	{ id = 0, x = 3,  trees = 0, predictDetected = true },
	{ id = 1, x = 30, trees = 1, predictDetected = true },
	{ id = 2, x = 57, trees = 2, predictDetected = false },
}

-- The lane whose aircraft flies a pass at the end. Lane 2 is the interesting
-- one: it is predicted blind while hovering, so the pass answers "is it blind
-- for the WHOLE overflight, or only the far part of it?" — which is the
-- difference between a unit that looks broken and one that looks late.
local PassLane = 3

local phase = "auto"
local report = {}
local setupFaults = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function cellStr(c)
	return c.X .. ":" .. c.Y
end

local function sameCell(a, b)
	return a.X == b.X and a.Y == b.Y
end

local function cellDist(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

-- Map.cs:1102-1120, reimplemented so the report can show what the engine's
-- ground channel SHOULD produce from the density this test actually measured.
local function forestGroundShadow(d)
	if d <= 0 then return 0 end
	if d <= 20 then return math.ceil(d / 10) end
	return 2 + math.ceil((d - 20) / 5)
end

-- Detectable.IsVisibleInner's airborne branch, reimplemented from the two
-- primitives the harness exposes. NOT a call into CanBeViewedByPlayer — no Lua
-- binding reaches that — so this is the harness's reconstruction of the engine's
-- decision, and it is labelled as such everywhere it is reported.
local function detectedByUsa(usa, cell)
	local vis = Test.GetVisibility(usa, cell)
	local radar = Test.HasRadarCover(usa, cell)
	return (vis > DetectableVision) or radar, vis, radar
end

local function firedSince(l, baseAmmo, baseHealth)
	if l.aa.IsDead then return false end
	if l.aa.AmmoCount("primary-ammo") < baseAmmo then return true end
	if not l.halo.IsDead and l.halo.Health < baseHealth then return true end
	return false
end

local function pollTick()
	for _, l in ipairs(Lanes) do
		if not l.aa.IsDead then
			local here = l.aa.Location

			if phase == "auto" then
				local drift = cellDist(here, l.startCell)
				if drift > l.autoMaxDrift then l.autoMaxDrift = drift end
				if not l.autoFired and firedSince(l, l.startAmmo, l.startHaloHealth) then
					l.autoFired = true
				end
			elseif phase == "manual" then
				local drift = cellDist(here, l.preManualCell)
				if drift > l.manualMaxDrift then l.manualMaxDrift = drift end
				if not l.manualFired and firedSince(l, l.baseAmmo, l.baseHealth) then
					l.manualFired = true
					l.manualFireCell = here
					l.movedBeforeFire = not sameCell(here, l.preManualCell)
				end
			end
		end
	end

	if phase ~= "pass" then
		return
	end

	-- Fly-past sampling, lane PassLane only.
	local l = Lanes[PassLane]
	if l.aa.IsDead or l.halo.IsDead then
		return
	end

	local haloCell = l.halo.Location
	local dist = AaRow - haloCell.Y
	local det, vis, _ = detectedByUsa(l.usa, haloCell)

	if l.passMinDist == nil or dist < l.passMinDist then
		l.passMinDist = dist
	end

	-- First range at which the harness's reconstruction says the aircraft
	-- became visible enough to be auto-targetable.
	if det and l.passAcquireDist == nil then
		l.passAcquireDist = dist
		l.passAcquireVis = vis
	end

	if not l.passFired and firedSince(l, l.passBaseAmmo, l.passBaseHealth) then
		l.passFired = true
		l.passFireDist = dist
	end
end

local function startPolling(seconds, onDone)
	local remaining = math.floor(seconds * TestHarness.TicksPerSecond)
	local step
	step = function()
		pollTick()
		remaining = remaining - 1
		if remaining <= 0 then
			onDone()
		else
			Trigger.AfterDelay(1, step)
		end
	end
	Trigger.AfterDelay(1, step)
end

local function finish()
	local anyBelowTen = false

	for _, l in ipairs(Lanes) do
		local expectedDensity = l.trees * 10
		if l.density ~= expectedDensity then
			table.insert(setupFaults, "lane" .. l.id .. " density " .. l.density
				.. " != designed " .. expectedDensity)
		end
		if l.aa.IsDead then table.insert(setupFaults, "lane" .. l.id .. " AA died") end
		if l.halo.IsDead then table.insert(setupFaults, "lane" .. l.id .. " Halo died") end
		if l.vis < 10 then anyBelowTen = true end

		local movedTag = "?"
		if l.movedBeforeFire == true then movedTag = "Y" end
		if l.movedBeforeFire == false then movedTag = "N" end

		table.insert(report, table.concat({
			"L" .. l.id,
			"t" .. l.trees,
			"d" .. l.density,
			"gs" .. forestGroundShadow(l.density),
			"vis" .. l.vis,
			"radar" .. (l.radar and "1" or "0"),
			"det" .. (l.detected and "Y" or "N"),
			"auto" .. (l.autoFired and "F" or "-"),
			"autodrift" .. l.autoMaxDrift,
			"man" .. (l.manualFired and "F" or "-"),
			"moved" .. movedTag,
			"drift" .. l.manualMaxDrift,
		}, " "))
	end

	-- Fog self-check. With fog off GetVisibility short-circuits to 10 on every
	-- explored cell (MapLayers.cs:611-618), which is exactly how the companion
	-- scenario read. If nothing came back below 10 the fog lock did not take and
	-- the run measures nothing — that must fail loudly, not read as "detected".
	if not anyBelowTen then
		table.insert(setupFaults, "every lane read vis10 - fog is NOT active, run is void")
	end

	local p = Lanes[PassLane]
	local passReport = table.concat({
		"PASS(L" .. p.id .. ")",
		"acquireDist" .. (p.passAcquireDist or -1),
		"acquireVis" .. (p.passAcquireVis or -1),
		"fired" .. (p.passFired and "Y" or "N"),
		"fireDist" .. (p.passFireDist or -1),
		"minDist" .. (p.passMinDist or -1),
	}, " ")

	local summary = table.concat(report, " | ") .. " || " .. passReport

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	Test.Pass(summary)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")
	if USA == nil or Russia == nil then
		Test.Fail("USA or Russia player not found")
		return
	end

	local aaActors = { A0, A1, A2 }

	for i, l in ipairs(Lanes) do
		l.usa = USA
		l.aa = aaActors[i]
		if l.aa == nil or l.aa.IsDead then
			Test.Fail("AA actor missing for lane " .. l.id)
			return
		end

		l.halo = Actor.Create("halo", true, {
			Owner = Russia,
			CenterPosition = cellPos(l.x, AirRow, AirAltitude),
			Facing = Angle.South,
		})
		if l.halo == nil then
			Test.Fail("could not spawn halo for lane " .. l.id)
			return
		end

		-- Ground shadow counts EVERY cell between the endpoints, so measure the
		-- whole corridor rather than just the airborne band the companion test
		-- sampled. Endpoints excluded, matching Map.cs:1153-1155.
		l.density = 0
		for y = AirRow + 1, AaRow - 1 do
			l.density = l.density + Test.GetDensity(CPos.New(l.x, y))
		end

		l.startCell = l.aa.Location
		l.startAmmo = l.aa.AmmoCount("primary-ammo")
		l.startHaloHealth = l.halo.Health

		l.autoFired = false
		l.autoMaxDrift = 0
		l.manualFired = false
		l.manualFireCell = nil
		l.manualMaxDrift = 0
		l.movedBeforeFire = nil
		l.vis = 0
		l.radar = false
		l.detected = false
		l.passFired = false
	end

	TestHarness.FocusBetween(A0, A2)
	TestHarness.Select(A2)

	-- PHASE A — autotarget only. No orders issued to anything.
	startPolling(AutoPhaseSeconds, function()
		for _, l in ipairs(Lanes) do
			local det, vis, radar = detectedByUsa(USA, CPos.New(l.x, AirRow))
			l.detected = det
			l.vis = vis
			l.radar = radar

			if l.aa.IsDead then
				l.preManualCell = l.startCell
				l.baseAmmo = 0
				l.baseHealth = 0
			else
				l.preManualCell = l.aa.Location
				l.baseAmmo = l.aa.AmmoCount("primary-ammo")
				l.baseHealth = l.halo.IsDead and 0 or l.halo.Health
			end
		end

		-- PHASE B — the manual path, mirroring what a player's click issues.
		phase = "manual"
		for _, l in ipairs(Lanes) do
			if not l.aa.IsDead and not l.halo.IsDead then
				l.aa.Attack(l.halo, true, false)
			end
		end

		startPolling(ManualPhaseSeconds, function()
			-- PHASE C — the overflight. The user reported an OVERFLIGHT while
			-- phases A/B stage a hover, and a moving target re-evaluates both
			-- gates every tick against changing range. Cancel the manual order
			-- first so only autotarget can act, then fly the aircraft in.
			local p = Lanes[PassLane]
			phase = "pass"

			if not p.aa.IsDead then
				p.aa.Stop()
				p.passBaseAmmo = p.aa.AmmoCount("primary-ammo")
			else
				p.passBaseAmmo = 0
			end
			p.passBaseHealth = p.halo.IsDead and 0 or p.halo.Health

			if not p.halo.IsDead then
				p.halo.Move(CPos.New(p.x, AaRow - 2))
			end

			startPolling(PassPhaseSeconds, finish)
		end)
	end)
end
