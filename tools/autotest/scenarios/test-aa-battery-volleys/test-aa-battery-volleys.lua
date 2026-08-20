-- ASSERTING AUTOTEST — does an AA battery volley, or does it serialise?
--
-- THE REPORT THIS EXISTS FOR (user, live play, 2026-08-20):
--   "My AA soldiers are not autotargeting/firing sometimes, but as soon as I
--    give a manual order to fire they do. I saw them shoot automatically
--    sometimes, not sure if there is a pattern. There was no obstacles in some
--    of the cases and they still didn't fire."
--
-- THE MECHANISM UNDER TEST. Actor.AverageDamagePercent is a single unattributed
-- accumulator on the TARGET (Actor.cs:83-87), bumped by MarkTargetForAttack
-- whenever any unit commits (AttackBase.cs:673, AttackFollow.cs:189) and halved
-- once every 60 ticks (Actor.cs:309-310). ChooseTarget hard-skips any target
-- whose accumulator is >= OverkillThreshold (AutoTarget.cs:1436).
--
-- Against aircraft one AA soldier is enough to trip that on its own:
--   MANPAD Damage 3000, Penetration 15 vs the Halo's Armor Thickness 3
--   (^Airborne, aircraft.yaml:22-23) so no armour reduction, and no Versus
--   table => min(3000 * 100 / 600, 100) = EXACTLY 100.
-- The per-shooter cap (AutoTarget.cs:1654) is 100 and OverkillThreshold
-- (AutoTarget.cs:217) is 100 and the comparison is `>=`. One committed AA
-- therefore blinds every other AA to a completely healthy helicopter until the
-- next halving. Each new joiner re-loads the accumulator, so the battery
-- engages one unit at a time instead of together.
--
-- A manual attack order goes through AttackBase.AttackTarget and never calls
-- ChooseTarget, which is why clicking works instantly. That discriminator is
-- already measured in test-aa-overkill-suppression lane B (fired at t38, deep
-- inside a window where the auto lane was still standing down) and is not
-- re-measured here.
--
-- WHY THE CONTROL BATTERY IS NOT OPTIONAL. ^CamoSoldier draws its scan interval
-- randomly per unit from 16-32 ticks (infantry.yaml:289-290), so four AA never
-- acquire on the same tick even with nothing suppressing them. Any absolute
-- "all four must fire by tick N" assertion would be measuring that stagger, and
-- would be tuned rather than derived. The control battery is the same actor
-- template with OverkillThreshold: -1 and nothing else changed, so the stagger
-- is present in both arms and cancels. What is left is the mechanism.
--
-- WHAT WOULD MAKE THIS RUN UNREADABLE, all checked below rather than assumed:
--   * the control battery not fielding all four shooters -> no baseline
--   * a helicopter taking damage -> the RangeLimit cut in weapons.yaml did not
--     apply, so shots are landing, targets are dying, and "did not fire" is
--     confounded with "had nothing left to fire at"
--   * a helicopter never entering the world -> nothing to acquire
--   * the test battery not firing AT ALL -> range, LOS or targeting is broken
--     rather than suppressed, which is a different bug and must not be reported
--     as this one
--
-- EXPECTED VERDICT ON CURRENT CODE: FAIL, with the test battery fielding
-- markedly fewer shooters than the control. If it PASSES, that is a finding
-- about the diagnosis and must be reported as one -- do not retune the
-- threshold to force a red.

local ObserveSeconds = 20

local AirRow = 8
local AirAltitude = 1280

local Lanes = {
	{ id = "TEST", airX = 13, overkill = "stock" },
	{ id = "CTRL", airX = 49, overkill = "disabled" },
}

local tick = 0
local setupFaults = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function pollTick()
	tick = tick + 1

	for _, l in ipairs(Lanes) do
		for _, u in ipairs(l.units) do
			if u.firstShot == nil and not u.actor.IsDead then
				if u.actor.AmmoCount("primary-ammo") < u.startAmmo then
					u.firstShot = tick
				end
			end
		end

		if not l.halo.IsDead then
			-- Baseline captured HERE, not in WorldLoaded. Actor.Create only adds the
			-- actor to the world in a frame-end task (ActorGlobal.cs:113-116), so
			-- anything read about it during WorldLoaded describes an actor that is not
			-- in the world yet. Reading the baseline on the first in-world tick is the
			-- same defence that cost this scenario family a whole run once already.
			if l.halo.IsInWorld then
				l.haloEverInWorld = true
				if l.haloStartHealth == nil then
					l.haloStartHealth = l.halo.Health
				elseif l.halo.Health < l.haloStartHealth then
					l.haloDamaged = true
				end
			end
		else
			l.haloDied = true
		end
	end

	-- Live counters go to lua.log, never into a failure string. A message passed
	-- to a helper is concatenated at REGISTRATION, so any counter interpolated
	-- into one reports its initial value forever (AUTOTEST.md, "Two Lua traps").
	if tick % 25 == 0 then
		local parts = {}
		for _, l in ipairs(Lanes) do
			local fired = 0
			for _, u in ipairs(l.units) do
				if u.firstShot ~= nil then
					fired = fired + 1
				end
			end
			table.insert(parts, l.id .. " fired=" .. fired
				.. " haloHP=" .. (l.halo.IsDead and "DEAD" or tostring(l.halo.Health)))
		end
		print("[volleys] t" .. tick .. " " .. table.concat(parts, " | "))
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

local function laneSummary(l)
	local shots = {}
	local fired = 0
	for _, u in ipairs(l.units) do
		table.insert(shots, u.name .. "=" .. (u.firstShot or -1))
		if u.firstShot ~= nil then
			fired = fired + 1
		end
	end

	local first, last
	for _, u in ipairs(l.units) do
		if u.firstShot ~= nil then
			if first == nil or u.firstShot < first then first = u.firstShot end
			if last == nil or u.firstShot > last then last = u.firstShot end
		end
	end

	l.firedCount = fired
	l.spread = (first ~= nil and last ~= nil) and (last - first) or -1

	return l.id .. "(" .. l.overkill .. ") fired" .. fired .. "/4"
		.. " spread" .. l.spread
		.. " [" .. table.concat(shots, " ") .. "]"
		.. " halo" .. (l.halo.IsDead and "DEAD" or tostring(l.halo.Health))
end

local function finish()
	local report = {}
	for _, l in ipairs(Lanes) do
		table.insert(report, laneSummary(l))

		-- A dead AA never fires, which is indistinguishable from a suppressed one in
		-- the shooter count. Nothing in this scenario is supposed to be able to kill
		-- them, so if one died the staging is not what the assertion assumes.
		for _, u in ipairs(l.units) do
			if u.actor.IsDead then
				table.insert(setupFaults, l.id .. " lost " .. u.name
					.. " - a dead AA cannot be told apart from a suppressed one")
			end
		end

		if not l.haloEverInWorld then
			table.insert(setupFaults, l.id .. " helicopter never entered the world")
		end
		if l.haloDamaged or l.haloDied then
			table.insert(setupFaults, l.id .. " helicopter took damage - the RangeLimit cut"
				.. " did not apply, so shots are landing and 'did not fire' is confounded"
				.. " with 'target already dead'")
		end
	end

	local test, ctrl = Lanes[1], Lanes[2]
	local summary = table.concat(report, " || ")

	-- The baseline must exist before any comparison against it means anything.
	if ctrl.firedCount < 4 then
		table.insert(setupFaults, "control battery fielded only " .. ctrl.firedCount
			.. "/4 shooters with overkill prevention DISABLED - there is no baseline,"
			.. " and something other than the mark is holding these units back")
	end

	-- Separates "suppressed" from "could not shoot at all". If the test battery
	-- is silent outright the fault is range, LOS or targeting, not overkill, and
	-- reporting it as overkill would be wrong.
	if test.firedCount == 0 then
		table.insert(setupFaults, "test battery never fired at all - that is a targeting,"
			.. " range or LOS failure, not overkill suppression")
	end

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	if test.firedCount < ctrl.firedCount then
		Test.Fail("the stock battery serialised: " .. test.firedCount .. "/4 AA engaged a"
			.. " healthy helicopter where the same battery with overkill prevention off"
			.. " engaged " .. ctrl.firedCount .. "/4. One AA's commitment marks an aircraft"
			.. " at exactly OverkillThreshold, so its neighbours hard-skip a target nothing"
			.. " is wrong with. || " .. summary)
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

	local names = {
		TEST = { "TestAA1", "TestAA2", "TestAA3", "TestAA4" },
		CTRL = { "CtlAA1", "CtlAA2", "CtlAA3", "CtlAA4" },
	}
	local actors = {
		TEST = { TestAA1, TestAA2, TestAA3, TestAA4 },
		CTRL = { CtlAA1, CtlAA2, CtlAA3, CtlAA4 },
	}

	for _, l in ipairs(Lanes) do
		l.units = {}
		for i, a in ipairs(actors[l.id]) do
			if a == nil then
				Test.Fail("AA actor missing: " .. names[l.id][i])
				return
			end
			table.insert(l.units, {
				name = names[l.id][i],
				actor = a,
				startAmmo = a.AmmoCount("primary-ammo"),
				firstShot = nil,
			})
		end

		l.halo = Actor.Create("halo", true, {
			Owner = Russia,
			CenterPosition = cellPos(l.airX, AirRow, AirAltitude),
			Facing = Angle.South,
		})
		if l.halo == nil then
			Test.Fail("could not spawn halo for lane " .. l.id)
			return
		end

		l.haloStartHealth = nil
		l.haloDamaged = false
		l.haloDied = false
		l.haloEverInWorld = false
	end

	TestHarness.FocusBetween(TestAA1, CtlAA4)
	TestHarness.Select(TestAA1)

	startPolling(ObserveSeconds, finish)
end
