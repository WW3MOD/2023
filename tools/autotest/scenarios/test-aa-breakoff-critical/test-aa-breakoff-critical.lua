-- DIAGNOSTIC AUTOTEST — third in the series, and the first one that tests a
-- filter which can actually produce the reported asymmetry.
--
-- WHY THIS EXISTS. The first two scenarios refuted both candidate gates by the
-- same argument: each fails CLOSED ON BOTH PATHS.
--   blocked line of fire -> auto silent, manual silent (MoveWithinRange has no
--                           LOS term, so the "walk until you can shoot" half of
--                           that story was never implemented)
--   undetected           -> auto silent, manual silent (Attack.cs:154-155 ends
--                           the activity when the target was never visible)
-- The user could SEE the aircraft — he clicked it — so visibility, which is
-- per-PLAYER not per-unit, was true for his side. The target he clicked was
-- therefore detected AND in clear line of fire, and something dropped it on the
-- auto path only.
--
-- Reading ChooseTarget end to end, exactly two filters have that shape:
--   overkill   (AutoTarget.cs:1129) - skip if AverageDamagePercent >= 100
--   BREAK-OFF  (AutoTarget.cs:1135-1137) - skip any target holding
--              Info.BreakOffCondition, default "critical-damage"
--              (AutoTarget.cs:218), granted at DamageState.Critical = HP < 25%
--              (defaults.yaml:194-196, Health.cs:95).
-- Break-off is the one that can be staged exactly, so it goes first.
--
-- THE PREDICTION — a three-way split on ONE target, which nothing else in this
-- series produces:
--   auto          -> skipped  (ChooseTarget break-off filter)
--   normal manual -> skipped  (Attack.cs:201-207 re-checks it when !forceAttack)
--   FORCE attack  -> FIRES    (both sites are gated on !forceAttack)
-- If that lands, "he issued a manual attack order and it fired instantly" means
-- he Ctrl+clicked — which is precisely what a player does when a unit refuses to
-- engage — and the entire report is explained with no foliage and no fog.
--
-- Note the honest asymmetry with the earlier scenarios: this one predicts the
-- normal manual order ALSO fails. If instead the normal manual order fires, the
-- break-off re-check does not bite on this path and the overkill filter becomes
-- the better candidate, since nothing in the Attack activity re-checks THAT.

-- MEASURED 2026-08-10 (seed -2050768512). THE PREDICTION LANDED EXACTLY, and
-- this is the first mechanism in the series that produces the reported shape:
--
--   lane            hp    auto   normal manual   force attack   cells moved
--   0 healthy      89%    fire       fire            fire            0
--   1 CRITICAL     10%    ----       ----            FIRE            0
--
-- A critically damaged target is invisible to autotarget AND refuses an
-- ordinary attack order, but a FORCE attack kills it instantly from a
-- standstill. That is the user's report, reproduced, with no foliage and no
-- fog anywhere on the map.
--
-- Run 1 of this scenario failed as SETUP INVALID because the damaged Halo died:
-- HeliEmergencyLanding crashes any helicopter that reaches Critical
-- (HeliEmergencyLanding.cs:22,102). The trait is disabled in rules.yaml so the
-- filter itself can be measured — see the note there about why that makes the
-- Halo a carrier for a generic mechanism rather than a claim about helicopters.
--
-- STILL AMBIGUOUS, and it needs one answer from the user rather than another
-- test. The overkill filter (AutoTarget.cs:1129) produces almost the same
-- split, differing in exactly one cell of the table: it is checked ONLY in
-- ChooseTarget — there is no AverageDamagePercent re-check anywhere in
-- Attack.cs / AttackBase.cs / AttackFollow.cs — so an overkill-suppressed
-- target would still fall to an ORDINARY click.
--
--   mechanism                       auto   normal click   Ctrl+click
--   break-off (critical-damage)     skip       skip          FIRE
--   overkill  (damage >= 100%)      skip       FIRE          FIRE
--
-- So: if the player left-clicked, it was overkill. If the player Ctrl+clicked,
-- it was break-off. Both are WW3MOD additions to ChooseTarget, and both are
-- invisible to the player.

local AaRow = 28
local AirRow = 8
local AirAltitude = 1280

-- 15% of max: comfortably inside Critical's <25% band with margin for the
-- missiles that land during the force phase.
local CriticalFraction = 15

local AutoPhaseSeconds = 10
local ManualPhaseSeconds = 12
local ForcePhaseSeconds = 12

local Lanes = {
	{ id = 0, x = 3,  damaged = false },
	{ id = 1, x = 57, damaged = true },
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

local function cellDist(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

-- Ammo only. The companion scenarios also watched target health, but here the
-- target's health is the independent variable being held fixed — reading it as
-- a fire detector would confuse the measurement with the manipulation.
local function firedSince(l, baseAmmo)
	if l.aa.IsDead then return false end
	return l.aa.AmmoCount("primary-ammo") < baseAmmo
end

local function pollTick()
	for _, l in ipairs(Lanes) do
		if not l.aa.IsDead then
			local here = l.aa.Location

			if phase == "auto" then
				local drift = cellDist(here, l.startCell)
				if drift > l.autoMaxDrift then l.autoMaxDrift = drift end
				if not l.autoFired and firedSince(l, l.startAmmo) then
					l.autoFired = true
				end
			elseif phase == "manual" then
				local drift = cellDist(here, l.preManualCell)
				if drift > l.manualMaxDrift then l.manualMaxDrift = drift end
				if not l.manualFired and firedSince(l, l.baseAmmo) then
					l.manualFired = true
					l.manualMoved = not sameCell(here, l.preManualCell)
				end
			else
				local drift = cellDist(here, l.preForceCell)
				if drift > l.forceMaxDrift then l.forceMaxDrift = drift end
				if not l.forceFired and firedSince(l, l.forceBaseAmmo) then
					l.forceFired = true
					l.forceMoved = not sameCell(here, l.preForceCell)
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

local function tag(b)
	if b == true then return "Y" end
	if b == false then return "N" end
	return "?"
end

local function finish()
	for _, l in ipairs(Lanes) do
		if l.aa.IsDead then table.insert(setupFaults, "lane" .. l.id .. " AA died") end
		if l.halo.IsDead then table.insert(setupFaults, "lane" .. l.id .. " Halo died") end

		-- The manipulation must have actually taken: a damaged lane has to be
		-- sitting under 25% of max health at the END of the run, or the target
		-- never carried critical-damage and the whole comparison is void.
		local pct = -1
		if not l.halo.IsDead then
			pct = math.floor(l.halo.Health * 100 / l.halo.MaxHealth)
			if l.damaged and pct >= 25 then
				table.insert(setupFaults, "lane" .. l.id .. " hp" .. pct
					.. "% is NOT under the 25% Critical band - break-off never applied")
			end
			if not l.damaged and pct < 25 then
				table.insert(setupFaults, "lane" .. l.id .. " control fell to hp" .. pct
					.. "% and became critical itself")
			end
		end

		table.insert(report, table.concat({
			"L" .. l.id,
			l.damaged and "CRIT" or "healthy",
			"hp" .. pct .. "%",
			"auto" .. (l.autoFired and "F" or "-"),
			"autodrift" .. l.autoMaxDrift,
			"man" .. (l.manualFired and "F" or "-"),
			"manmoved" .. tag(l.manualMoved),
			"mandrift" .. l.manualMaxDrift,
			"force" .. (l.forceFired and "F" or "-"),
			"forcemoved" .. tag(l.forceMoved),
			"forcedrift" .. l.forceMaxDrift,
			"cell" .. cellStr(l.startCell),
		}, " "))
	end

	local summary = table.concat(report, " | ")

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	-- Skip, not Pass: every failure path above is a staging fault, so nothing here
	-- grades the lanes it measured. The summary IS the deliverable; see expected-status.
	Test.Skip(summary)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")
	if USA == nil or Russia == nil then
		Test.Fail("USA or Russia player not found")
		return
	end

	local aaActors = { A0, A1 }

	for i, l in ipairs(Lanes) do
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

		-- Drive the damaged lane into DamageState.Critical. The Lua Health
		-- setter routes through InflictDamage (HealthProperties.cs:33) so the
		-- damage-state notifications fire normally and GrantConditionOnDamageState
		-- actually grants critical-damage — assigning the field is not a
		-- back-door poke that skips the trait.
		if l.damaged then
			l.halo.Health = math.floor(l.halo.MaxHealth * CriticalFraction / 100)
		end

		l.startCell = l.aa.Location
		l.startAmmo = l.aa.AmmoCount("primary-ammo")

		l.autoFired = false
		l.autoMaxDrift = 0
		l.manualFired = false
		l.manualMoved = nil
		l.manualMaxDrift = 0
		l.forceFired = false
		l.forceMoved = nil
		l.forceMaxDrift = 0
	end

	TestHarness.FocusBetween(A0, A1)
	TestHarness.Select(A1)

	-- PHASE A — autotarget only, no orders.
	startPolling(AutoPhaseSeconds, function()
		for _, l in ipairs(Lanes) do
			l.preManualCell = l.aa.IsDead and l.startCell or l.aa.Location
			l.baseAmmo = l.aa.IsDead and 0 or l.aa.AmmoCount("primary-ammo")
		end

		-- PHASE B — ORDINARY manual attack (forceAttack = false), the plain
		-- left-click order.
		phase = "manual"
		for _, l in ipairs(Lanes) do
			if not l.aa.IsDead and not l.halo.IsDead then
				l.aa.Attack(l.halo, true, false)
			end
		end

		startPolling(ManualPhaseSeconds, function()
			for _, l in ipairs(Lanes) do
				if not l.aa.IsDead then
					l.aa.Stop()
					l.preForceCell = l.aa.Location
					l.forceBaseAmmo = l.aa.AmmoCount("primary-ammo")
				else
					l.preForceCell = l.startCell
					l.forceBaseAmmo = 0
				end
			end

			-- PHASE C — FORCE attack (forceAttack = true), the Ctrl+click order.
			-- This is the only one of the three that both break-off sites let
			-- through, so it is the phase that decides the hypothesis.
			phase = "force"
			for _, l in ipairs(Lanes) do
				if not l.aa.IsDead and not l.halo.IsDead then
					l.aa.Attack(l.halo, true, true)
				end
			end

			startPolling(ForcePhaseSeconds, finish)
		end)
	end)
end
