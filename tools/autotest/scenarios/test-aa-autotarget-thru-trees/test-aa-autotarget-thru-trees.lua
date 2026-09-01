-- DIAGNOSTIC AUTOTEST — settles WHY an AA specialist standing in trees never
-- auto-engages an overflying hostile aircraft, while a manual attack order on
-- that same aircraft fires immediately.
--
-- Two hypotheses are on trial:
--
--   (1) LINE OF SIGHT. AutoTarget only scans from INotifyIdle.TickIdle
--       (AutoTarget.cs:606). In ChooseTarget a candidate failing
--       FiringLOS.HasClearLOS (AutoTarget.cs:1112-1123) is DISCARDED with
--       `continue`, so no activity is ever created and the unit stays idle.
--       The manual path skips ChooseTarget entirely (AttackBase.cs:458-468 ->
--       :628-645, allowMove hardcoded true) and Attack.cs:248-252 folds
--       losBlocked into needsToMove, so the unit REPOSITIONS via
--       MoveWithinRange (:276) until LOS clears, then fires.
--
--   (2) DETECTION. CanBeViewedByPlayer (AutoTarget.cs:1066) ->
--       Detectable.IsVisibleInner (Detectable.cs:93-116): an airborne actor
--       needs vision >= 2 at its ground-projected cell, or radar cover.
--       Detectable.ModifyRender (:222-234) hides an undetected actor while
--       ModifyScreenBounds (:247-250) still returns its bounds — so an
--       undetected aircraft stays CLICKABLE. That reproduces "manual works,
--       auto doesn't" with no foliage involved at all.
--
-- THE HOLE THIS TEST CLOSES. Every LOS gate on the manual path uses the same
-- weapon threshold as the autotarget gate — Armament.CheckFire re-checks it
-- per weapon at Armament.cs:364. So if the line were genuinely blocked, the
-- manual shot should have been refused too FROM THE SAME CELL. Hypothesis (1)
-- therefore only holds if the unit quietly MOVED before firing.
--
--   >>> THE DISCRIMINATOR IS WHETHER THE AA'S CELL CHANGED BEFORE IT FIRED. <<<
--
-- Fires without moving  -> the line was never blocked -> (1) is wrong at root.
-- Fires only after moving -> (1) confirmed: blocked LOS means "not a target"
--                            to the scanner but "walk until you can shoot" to
--                            the activity.
-- A test that only asserted "it fired" would be useless here, because the
-- reposition path masks a still-broken selection filter.
--
-- SCOPE LIMIT, STATED HONESTLY. rules.yaml disables fog, which makes
-- hypothesis (2) unable to fire, so this scenario measures (1) alone. Per
-- AUTOTEST.md ("a bug that cannot fire is indistinguishable from a bug that
-- does not exist"), (2) is left UNTESTED, not refuted. The visibility and
-- radar inputs are still recorded per rung so the report states what they were.
--
-- MEASURED 2026-08-10 (seed 772997303). The answer was NEITHER predicted branch:
--
--   trees  density  ceil(D/5)  auto   manual  cells moved
--     0       0         0      fire    fire        0
--     1      10         2      fire    fire        0
--     2      20         4      fire    fire        0
--     3      30         6      ----    ----        0
--     4      40         8      ----    ----        0
--
-- A blocked shooter fires on NEITHER path and does not move on either. The
-- reposition-until-LOS-clears behaviour does not exist: MoveWithinRange.ShouldStop
-- is a pure distance test (MoveWithinRange.cs:38-43,73-76) with no LOS term, so
-- with the target already in weapon range the queued move completes having moved
-- nothing, and Attack re-queues it forever.
--
-- CONSEQUENCE: "auto never engages but manual fires instantly" EXONERATES line of
-- sight instead of implicating it — a genuinely blocked unit is silent both ways.
-- Keep this test as the guard: if a future change makes rung 3/4 fire on the
-- manual order, the reposition path has become real and this comment is stale.
--
-- LADDER. Five rungs at 0/1/2/3/4 tree cells in the contributing band.
-- Predicted airborneShadow = ceil(sum(density)/5) vs MANPAD's default
-- ClearSightThreshold 5, so the predicted break is between rung 2 (shadow 4)
-- and rung 3 (shadow 6) — i.e. total density >= 26. A break anywhere else is
-- itself the finding.

local AaRow = 28
local AirRow = 8
local AirAltitude = 1280

-- The airborne shadow channel only accumulates where t > 0.75 along the traced
-- line (Map.cs:1160-1170), and FiringLOS swaps the lookup for ground-shoots-air
-- (FiringLOS.cs:84-96), so the contributing band is the 4 cells north of the
-- shooter on this 20-cell line. The shooter's own cell is skipped by the
-- from/to exclusion at Map.cs:1153-1155.
local ContribBand = 4

local AutoPhaseSeconds = 10
local ManualPhaseSeconds = 14

local Rungs = {
	{ id = 0, x = 3,  trees = 0 },
	{ id = 1, x = 17, trees = 1 },
	{ id = 2, x = 31, trees = 2 },
	{ id = 3, x = 45, trees = 3 },
	{ id = 4, x = 59, trees = 4 },
}

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

-- Chebyshev cell distance — "did it budge at all", direction-agnostic.
local function cellDist(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

-- A rung counts as having fired if its own ammo dropped OR its partner Halo
-- took damage. Recording both guards the attribution: ammo alone would also
-- tick if a shooter somehow reached a neighbouring rung's aircraft, and Halo
-- damage alone would miss a missile that misses.
local function firedSince(r, baseAmmo, baseHealth)
	if r.aa.IsDead then return false end
	if r.aa.AmmoCount("primary-ammo") < baseAmmo then return true end
	if not r.halo.IsDead and r.halo.Health < baseHealth then return true end
	return false
end

local function pollTick()
	for _, r in ipairs(Rungs) do
		if not r.aa.IsDead then
			local here = r.aa.Location

			if phase == "auto" then
				local drift = cellDist(here, r.startCell)
				if drift > r.autoMaxDrift then r.autoMaxDrift = drift end

				if not r.autoFired and firedSince(r, r.startAmmo, r.startHaloHealth) then
					r.autoFired = true
					r.autoFireCell = here
				end
			else
				local drift = cellDist(here, r.preManualCell)
				if drift > r.manualMaxDrift then r.manualMaxDrift = drift end

				if not r.manualFired and firedSince(r, r.baseAmmo, r.baseHealth) then
					r.manualFired = true
					r.manualFireCell = here
					-- THE DISCRIMINATOR, sampled at the instant of the shot
					-- rather than at the end of the phase: a unit that moved,
					-- fired and walked back would otherwise read as "never moved".
					r.movedBeforeFire = not sameCell(here, r.preManualCell)
				end
			end
		end
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
	for _, r in ipairs(Rungs) do
		local expectedDensity = r.trees * 10
		if r.density ~= expectedDensity then
			table.insert(setupFaults, "rung" .. r.id .. " density " .. r.density
				.. " != designed " .. expectedDensity)
		end
		if r.aa.IsDead then
			table.insert(setupFaults, "rung" .. r.id .. " AA died")
		end
		if r.halo.IsDead then
			table.insert(setupFaults, "rung" .. r.id .. " Halo died")
		end

		-- Compact per-rung record. Read as:
		--   r<id> t<trees> d<measured density> shadow<predicted ceil(d/5)>
		--   auto<F|-> vis<n> radar<0|1>
		--   man<F|-> moved<Y|N|?> drift<max cells moved during manual phase>
		--   cell<pre-manual -> at-shot>
		local predictedShadow = math.ceil(r.density / 5)
		local movedTag = "?"
		if r.movedBeforeFire == true then movedTag = "Y" end
		if r.movedBeforeFire == false then movedTag = "N" end

		local fireCell = "-"
		if r.manualFireCell ~= nil then fireCell = cellStr(r.manualFireCell) end

		table.insert(report, table.concat({
			"r" .. r.id,
			"t" .. r.trees,
			"d" .. r.density,
			"shadow" .. predictedShadow,
			"auto" .. (r.autoFired and "F" or "-"),
			"autodrift" .. r.autoMaxDrift,
			"vis" .. r.vis,
			"radar" .. (r.radar and "1" or "0"),
			"man" .. (r.manualFired and "F" or "-"),
			"moved" .. movedTag,
			"drift" .. r.manualMaxDrift,
			"cell" .. cellStr(r.preManualCell) .. ">" .. fireCell,
		}, " "))
	end

	local summary = table.concat(report, " | ")

	if #setupFaults > 0 then
		-- The measurement itself is untrustworthy — say so loudly rather than
		-- letting a broken ladder be read as a result.
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	-- Skip, not Pass: every failure path above is a staging fault, so nothing here
	-- grades the ladder it measured. The summary IS the deliverable; see expected-status.
	Test.Skip(summary)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")
	if USA == nil or Russia == nil then
		Test.Fail("USA or Russia player not found")
		return
	end

	local aaActors = { A0, A1, A2, A3, A4 }

	for i, r in ipairs(Rungs) do
		r.aa = aaActors[i]
		if r.aa == nil or r.aa.IsDead then
			Test.Fail("AA actor missing for rung " .. r.id)
			return
		end

		r.halo = Actor.Create("halo", true, {
			Owner = Russia,
			CenterPosition = cellPos(r.x, AirRow, AirAltitude),
			Facing = Angle.South,
		})
		if r.halo == nil then
			Test.Fail("could not spawn halo for rung " .. r.id)
			return
		end

		-- Measure the ladder instead of trusting the YAML arithmetic. If a
		-- footprint landed a cell off, this is what catches it.
		r.density = 0
		for d = 1, ContribBand do
			r.density = r.density + Test.GetDensity(CPos.New(r.x, AaRow - d))
		end

		r.startCell = r.aa.Location
		r.startAmmo = r.aa.AmmoCount("primary-ammo")
		r.startHaloHealth = r.halo.Health

		r.autoFired = false
		r.autoFireCell = nil
		r.autoMaxDrift = 0
		r.manualFired = false
		r.manualFireCell = nil
		r.manualMaxDrift = 0
		r.movedBeforeFire = nil
		r.vis = 0
		r.radar = false
	end

	TestHarness.FocusBetween(A0, A4)
	TestHarness.Select(A3)

	-- PHASE A — autotarget. No orders are issued to anything. Whatever the AA
	-- units do here is purely INotifyIdle.TickIdle -> ScanAndAttack.
	startPolling(AutoPhaseSeconds, function()
		for _, r in ipairs(Rungs) do
			-- Detection inputs, sampled at the end of the scan window. This
			-- mirrors Detectable.IsVisibleInner's airborne branch: the aircraft
			-- projects to ground (Detectable.Position: Ground, aircraft.yaml:45)
			-- so the lookup cell is the one directly beneath it, and the
			-- required level is DetectableInfo.Vision = 2 (Detectable.cs:25),
			-- which the aircraft templates do not override while airborne.
			-- NOTE: this is a REIMPLEMENTATION of that predicate from the two
			-- exposed primitives, not a call into CanBeViewedByPlayer itself —
			-- no Lua binding exposes that directly.
			r.vis = Test.GetVisibility(USA, CPos.New(r.x, AirRow))
			r.radar = Test.HasRadarCover(USA, CPos.New(r.x, AirRow))

			-- Nothing on this map can kill an AA (the Halos are unarmed and every
			-- shooter is allied), but reading .Location off a dead actor is a Lua
			-- error, which would kill the script and leave NO result file at all.
			-- Degrade to a recorded fault instead: a wasted run is expensive here.
			if r.aa.IsDead then
				r.preManualCell = r.startCell
				r.baseAmmo = 0
				r.baseHealth = 0
			else
				r.preManualCell = r.aa.Location
				r.baseAmmo = r.aa.AmmoCount("primary-ammo")
				r.baseHealth = r.halo.IsDead and 0 or r.halo.Health
			end
		end

		-- PHASE B — the manual path. allowMove=true mirrors what the player's
		-- attack order hardcodes (AttackBase.cs:628-645); forceAttack=false
		-- keeps it an ordinary attack order rather than a force-fire.
		phase = "manual"
		for _, r in ipairs(Rungs) do
			if not r.aa.IsDead and not r.halo.IsDead then
				r.aa.Attack(r.halo, true, false)
			end
		end

		startPolling(ManualPhaseSeconds, finish)
	end)
end
