-- DIAGNOSTIC AUTOTEST — the overkill filter, and the "can the mark go stale?"
-- question.
--
-- THE LEAK QUESTION IS ANSWERED BY READING, AND THE ANSWER IS NO.
-- AverageDamagePercent is not a claim held by the attacker that must be
-- released. It is a plain accumulator on the TARGET: AttackBase.AttackTarget
-- calls MarkTargetForAttack (:644) which adds
-- EstimatePercentDamage = totalDamage * 100 / MaxHP (AutoTarget.cs:1321), and
-- Actor.Tick halves it every 60 ticks (Actor.cs:309-310). There is no release
-- path and no owner, so an attacker dying, being given a new order, having its
-- own target die, or entering a transport cannot strand it. It can only decay.
-- Killing MarkerA mid-run puts that on the record empirically.
--
-- WHY IT MATTERS: THE MARK IS WILDLY OVERSIZED AGAINST AIRCRAFT. MANPAD deals
-- 3000 with Penetration 15 against the Halo's Light/Thickness 10 (no reduction,
-- since penetration >= thickness, and no Versus table), so ONE AA committing to
-- a stock 600-HP Halo marks it 3000*100/600 = 500 -- five times the threshold
-- of 100 (AutoTarget.cs:203). Decay 500 -> 250 -> 125 -> 62 crosses back under
-- 100 only after three halvings, so a single AA's commitment should blind every
-- OTHER AA to a healthy aircraft for on the order of 120-180 ticks.
--
-- =====================================================================
-- RUN 1 OF THIS SCENARIO WAS A NULL RESULT DRESSED AS A REAL ONE, and the
-- redesign below exists because of it. It applied the mark inside WorldLoaded,
-- but Actor.Create only adds the actor to the world in a frame-end task
-- (ActorGlobal.cs:113-116). The aircraft therefore was not in the world yet,
-- AttackBase.AttackTarget returned at its `if (!target.IsValidFor(self))` guard
-- (:633) BEFORE reaching MarkTargetForAttack (:644), and no mark was ever
-- applied. ObserverA then fired at tick 44 -- its natural latency -- and the
-- run read as "overkill does not suppress". The guard that should have caught
-- this was vacuous: it set markApplied = true unconditionally, asserting a
-- variable rather than the world.
--
-- Two changes: the marking order is delayed until the aircraft is really in
-- the world, and lane C measures the natural latency instead of assuming it.
-- Lane A minus lane C is the suppression; without lane C, lane A is unreadable.
-- =====================================================================
--
-- PREDICTIONS
--   lane C (control, unmarked)   : fires at its natural latency, ~40-50 ticks
--                                  after the aircraft exists.
--   lane A (marked, no orders)   : stands down far longer -- order of 120-180
--                                  ticks past the mark. Killing MarkerA at
--                                  tick 40 must NOT bring that forward.
--   lane B (marked, plain click) : fires ~18 ticks after the click, i.e. it
--                                  ignores the suppression entirely.

-- MEASURED 2026-08-10 (seed 1829504673), and every prediction landed:
--
--   lane                      first shot   moved   vs control
--   C control (unmarked)         t34         N         --
--   B marked, plain click@20     t38         N      168 ticks EARLIER
--   A marked, no orders          t206        N      172 ticks LATER
--
--   * ONE AA's commitment suppressed a second, healthy aircraft from every
--     other AA for 172 ticks -- about 10 real seconds at the mod's 60ms
--     timestep. Nothing was wrong with the target: full health, clear line,
--     no fog, well in range.
--   * MarkerA was KILLED at tick 40 and ObserverA still did not fire until
--     206. Killing the committing unit does not release the mark; only the
--     halving decay does. That is the leak question answered empirically as
--     well as by reading.
--   * The plain LEFT-CLICK at tick 20 fired at t38, from the same cell, deep
--     inside the window where lane A was still standing down. Overkill is
--     checked only in ChooseTarget and never re-checked in the attack
--     activity, so an ordinary order ignores the suppression completely.
--
-- That completes the discriminator table with both rows MEASURED rather than
-- half-measured and half-read:
--
--   mechanism                      auto   plain click   Ctrl+click
--   break-off (critical-damage)    skip      skip          FIRE
--   overkill  (damage >= 100%)     skip      FIRE          FIRE
--
-- So overkill is the better match for a report of "it ignored a HEALTHY
-- aircraft and then my normal click killed it instantly" -- it needs no
-- damage on the target, no Ctrl, and no foliage, only one other friendly
-- unit having committed first.

local AirRow = 8
local AirAltitude = 1280

-- The aircraft is created in WorldLoaded and joins the world at the end of that
-- tick, so anything targeting it must wait. 5 ticks is generous.
local MarkTick = 5
local LaneBClickTick = 20
local MarkerKillTick = 40
local ObserveSeconds = 24

local Lanes = {
	{ id = "A", x = 3,  marked = true,  mode = "auto" },
	{ id = "B", x = 30, marked = true,  mode = "click" },
	{ id = "C", x = 57, marked = false, mode = "control" },
}

local tick = 0
local report = {}
local setupFaults = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function sameCell(a, b)
	return a.X == b.X and a.Y == b.Y
end

local function pollTick()
	tick = tick + 1

	for _, l in ipairs(Lanes) do
		if not l.observer.IsDead and l.fireTick == nil then
			if l.observer.AmmoCount("primary-ammo") < l.startAmmo then
				l.fireTick = tick
				l.fireMoved = not sameCell(l.observer.Location, l.startCell)
			end
		end
		if l.haloDeathTick == nil and l.halo.IsDead then
			l.haloDeathTick = tick
		end
	end

	if tick == MarkTick then
		for _, l in ipairs(Lanes) do
			if l.marked and not l.marker.IsDead and not l.halo.IsDead then
				-- Issuing the order is what calls MarkTargetForAttack. HoldFire
				-- then stops the marker firing AND stops it re-scanning and
				-- re-marking every few ticks, which would pump the counter
				-- faster than it decays and turn a decay measurement into a
				-- plateau measurement.
				l.marker.Attack(l.halo, true, false)
				l.marker.Stance = "HoldFire"
				l.markOrderIssued = true
			end
		end
	end

	if tick == LaneBClickTick then
		local b = Lanes[2]
		if not b.observer.IsDead and not b.halo.IsDead then
			b.observer.Attack(b.halo, true, false)
			b.clickIssued = true
		end
	end

	if tick == MarkerKillTick then
		local a = Lanes[1]
		if not a.marker.IsDead then
			a.marker.Kill()
			a.markerKilled = true
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
	for _, l in ipairs(Lanes) do
		if l.observer.IsDead then
			table.insert(setupFaults, "lane" .. l.id .. " observer died")
		end
		if l.marked and not l.markOrderIssued then
			table.insert(setupFaults, "lane" .. l.id .. " marking order was never issued")
		end

		table.insert(report, table.concat({
			"L" .. l.id .. "(" .. l.mode .. ")",
			"fire" .. (l.fireTick or -1),
			"moved" .. (l.fireMoved == true and "Y" or (l.fireMoved == false and "N" or "?")),
			"death" .. (l.haloDeathTick or -1),
		}, " "))
	end

	local a, b, c = Lanes[1], Lanes[2], Lanes[3]

	-- The control MUST fire, or there is no baseline and nothing below means
	-- anything. This is the check run 1 lacked.
	if c.fireTick == nil then
		table.insert(setupFaults, "control lane C never fired - no baseline latency, run is unreadable")
	end

	local suppressionTicks = -1
	if a.fireTick ~= nil and c.fireTick ~= nil then
		suppressionTicks = a.fireTick - c.fireTick
	elseif a.fireTick == nil and c.fireTick ~= nil then
		suppressionTicks = 9999 -- never fired inside the window
	end

	local verdict = table.concat({
		"mark@" .. MarkTick,
		"click@" .. LaneBClickTick .. "=" .. (b.clickIssued and "Y" or "N"),
		"markerAkill@" .. MarkerKillTick .. "=" .. (a.markerKilled and "Y" or "N"),
		"suppressionVsControl" .. suppressionTicks,
	}, " ")

	local summary = table.concat(report, " | ") .. " || " .. verdict

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

	local markers = { MarkerA, MarkerB, nil }
	local observers = { ObserverA, ObserverB, ObserverC }

	for i, l in ipairs(Lanes) do
		l.marker = markers[i]
		l.observer = observers[i]
		if l.observer == nil or (l.marked and l.marker == nil) then
			Test.Fail("actors missing for lane " .. l.id)
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

		l.startAmmo = l.observer.AmmoCount("primary-ammo")
		l.startCell = l.observer.Location
		l.fireTick = nil
		l.fireMoved = nil
		l.haloDeathTick = nil
		l.clickIssued = false
		l.markerKilled = false
		l.markOrderIssued = false
	end

	TestHarness.FocusBetween(ObserverA, ObserverC)
	TestHarness.Select(ObserverA)

	startPolling(ObserveSeconds, finish)
end
