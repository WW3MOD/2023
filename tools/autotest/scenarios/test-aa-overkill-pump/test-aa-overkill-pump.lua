-- DIAGNOSTIC AUTOTEST — does repeated marking outrun the decay?
--
-- Bounds the overkill bug measured in test-aa-overkill-suppression, where one
-- AA's commitment blinded a second AA to a healthy aircraft for 172 ticks.
-- Ten seconds is annoying; indefinite is a different bug. The mark halves every
-- 60 ticks (Actor.cs:345-346), so a source adding A per 60 ticks settles at a
-- steady state of A: anything sustaining >= 100 per 60 ticks suppresses forever.
--
-- LANE R — REALISTIC BATTERY. Four AA, never ordered, one 30000-HP aircraft.
-- EstimatePercentDamage = totalDamage * 100 / MaxHP (AutoTarget.cs:1321), so
-- each commit marks 3000*100/30000 = 10 and four of them total 40 — well under
-- the threshold, because four missiles genuinely are only 40% of this target.
-- ALL FOUR should engage. This is the control on the claim that aggregate
-- commitment is self-limiting rather than runaway: attackers commit until the
-- marked total covers the target's health and no further.
--
-- LANE S — FORCED PUMP. A mechanism probe, not a realistic situation, and
-- flagged as such wherever it is reported. The Lua re-issues an attack order
-- every 5 ticks; each order re-enters AttackBase.AttackTarget and calls
-- MarkTargetForAttack again (:644), adding 10 per 5 ticks = 120 per 60 ticks.
-- Fixed point V = V/2 + 120 => V = 240, permanently over the threshold. The
-- observer should stay down for the whole pump and engage only after it stops.
--
-- WHY BOTH: lane S shows the mechanism ADMITS unbounded suppression, lane R
-- shows ordinary autotarget does not produce it. The reason is structural — a
-- unit that has committed is no longer idle, so TickIdle never runs for it and
-- it never re-scans or re-marks. Lane S has to be driven from Lua to happen at
-- all.
--
-- HARNESS NOTE, learned the hard way in the previous scenario: Actor.Create
-- only joins the world in a frame-end task (ActorGlobal.cs:113-116), so nothing
-- may be ordered against the aircraft during WorldLoaded — AttackBase's
-- IsValidFor guard would discard it silently. All ordering starts at PumpStart.

-- MEASURED 2026-08-10 (seed -2058490156):
--
--   LANE R  4 of 4 AA engaged, at ticks 41/46/39/49.
--   LANE S  615 pump orders over ticks 5-600; the observer stayed silent for
--           the ENTIRE pump and fired only at tick 818 -- 218 ticks after the
--           pump stopped. The pumper itself never fired (silencing worked).
--
-- SO THE SEVERITY IS TWO-SIDED, and both sides matter:
--
--   * The mechanism ADMITS unbounded suppression. Sustained marking held the
--     counter over the threshold for as long as it ran, and the observer was
--     blind to a healthy aircraft the whole time. It cleared promptly once the
--     marking stopped, confirming again there is nothing stuck -- only fed.
--   * Ordinary AGGREGATE commitment does NOT produce it. Lane R's four
--     attackers marked 10 each against a 30000-HP target, totalling 40 against
--     a threshold of 100, and all four correctly engaged. Commitment is
--     self-limiting: units commit until the marked total covers the target's
--     health and no further. The pathology measured earlier is GRANULARITY --
--     one MANPAD marking a 600-HP helicopter at 500 -- not runaway.
--
-- NOT MEASURED, AND IT IS THE OPEN QUESTION: there IS an in-engine re-marking
-- path. AttackFollow.Tick re-scans and marks whenever a unit is not currently
-- aiming (:156-172), and OpportunityFire defaults true (:26) with no mod
-- override for infantry. A real AA cycling through MANPAD's 200-tick BurstWait
-- drops out of "aiming" between shots and can re-acquire and re-mark each
-- cycle, which against a 600-HP helicopter re-applies 500 every cycle and would
-- hold neighbours down almost continuously. This scenario drives the pump from
-- Lua rather than through that path, so it proves the mechanism, not the
-- frequency. Measuring the opportunity-fire cadence is the next test.

local AirRow = 8
local AirAltitude = 1280

-- RUN 1 WAS INCONCLUSIVE ON LANE S, and the reason is worth keeping: the pump
-- ramped at 10 per 5 ticks, so it needed ~50 ticks to cross the threshold of
-- 100, while lane R measured the natural idle-scan-to-first-shot latency at
-- 35-47 ticks. The observer simply committed before the mark ever got there
-- (it fired at t48), which reads as "the pump does not suppress" but actually
-- means "the pump had not started suppressing yet". Fixed by front-loading the
-- mark above the threshold in one tick and sustaining every tick thereafter.
--
-- The pumper is also silenced now. In run 1 it fired (t23) and its own missiles
-- were damaging the very target whose health sets the mark size; over a long
-- window that could kill it and end the measurement. It is now HoldFire, and
-- each pump order is cancelled immediately after being issued -- the mark is
-- applied inside AttackBase.AttackTarget (:644) BEFORE any shot, so cancelling
-- the activity keeps the mark and drops the missile.
local PumpStart = 5
local PumpPreloadOrders = 20
local PumpEvery = 1
local PumpStopTick = 600
local ObserveSeconds = 45

local tick = 0
local report = {}
local setupFaults = {}
local pumpCount = 0

local LaneR = {}
local LaneS = {}

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function noteFire(u)
	if u.fireTick == nil and not u.actor.IsDead then
		if u.actor.AmmoCount("primary-ammo") < u.startAmmo then
			u.fireTick = tick
		end
	end
end

local function pollTick()
	tick = tick + 1

	for _, u in ipairs(LaneR) do noteFire(u) end
	noteFire(LaneS.observer)
	noteFire(LaneS.pumper)

	-- Drive the pump. Each Attack order re-enters AttackBase.AttackTarget and so
	-- re-marks the target (:644); the immediate Stop() cancels the resulting
	-- activity, so the mark lands but the missile never does.
	if tick >= PumpStart and tick <= PumpStopTick and (tick - PumpStart) % PumpEvery == 0 then
		if not LaneS.pumper.actor.IsDead and not LaneS.halo.IsDead then
			-- Front-load past the threshold in one tick so the observer cannot
			-- simply commit before the ramp arrives, which is what made run 1
			-- unreadable.
			local orders = (tick == PumpStart) and PumpPreloadOrders or 1
			for _ = 1, orders do
				LaneS.pumper.actor.Attack(LaneS.halo, true, false)
				pumpCount = pumpCount + 1
			end
			LaneS.pumper.actor.Stop()
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
	local firedR = 0
	local rTicks = {}
	for _, u in ipairs(LaneR) do
		if u.fireTick ~= nil then
			firedR = firedR + 1
			table.insert(rTicks, tostring(u.fireTick))
		else
			table.insert(rTicks, "-")
		end
	end

	if pumpCount == 0 then
		table.insert(setupFaults, "pump never issued an order")
	end
	if LaneR[1].actor.IsDead or LaneS.observer.actor.IsDead then
		table.insert(setupFaults, "a measuring unit died")
	end

	local obs = LaneS.observer.fireTick
	-- The decisive question: did the observer hold off for the WHOLE pump?
	local suppressedThroughPump = (obs == nil) or (obs > PumpStopTick)

	local summary = table.concat({
		"LANE_R firedOf4=" .. firedR,
		"ticks[" .. table.concat(rTicks, ",") .. "]",
		"|| LANE_S pumps" .. pumpCount,
		"pumpWindow" .. PumpStart .. "-" .. PumpStopTick,
		"observerFire" .. (obs or -1),
		"suppressedThroughPump" .. (suppressedThroughPump and "Y" or "N"),
		"pumperFire" .. (LaneS.pumper.fireTick or -1),
	}, " ")

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	Test.Pass(summary)
end

local function wrap(actor)
	return { actor = actor, startAmmo = actor.AmmoCount("primary-ammo"), fireTick = nil }
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")
	if USA == nil or Russia == nil then
		Test.Fail("USA or Russia player not found")
		return
	end

	for _, a in ipairs({ R1, R2, R3, R4 }) do
		if a == nil then
			Test.Fail("lane R actor missing")
			return
		end
		table.insert(LaneR, wrap(a))
	end

	if Pumper == nil or ObserverS == nil then
		Test.Fail("lane S actors missing")
		return
	end
	LaneS.pumper = wrap(Pumper)
	LaneS.observer = wrap(ObserverS)

	LaneR.halo = Actor.Create("halo", true, {
		Owner = Russia,
		CenterPosition = cellPos(5, AirRow, AirAltitude),
		Facing = Angle.South,
	})
	LaneS.halo = Actor.Create("halo", true, {
		Owner = Russia,
		CenterPosition = cellPos(56, AirRow, AirAltitude),
		Facing = Angle.South,
	})
	if LaneR.halo == nil or LaneS.halo == nil then
		Test.Fail("could not spawn aircraft")
		return
	end

	-- Silence the pumper: it must apply marks without putting missiles into the
	-- target, because the target's health is what sets the mark size and its
	-- death would end the measurement. HoldFire stops its autotarget path
	-- (TickIdle returns early below Ambush, AutoTarget.cs:608) while explicit
	-- Lua orders still reach AttackTarget and still mark.
	Pumper.Stance = "HoldFire"

	TestHarness.FocusBetween(R1, ObserverS)
	TestHarness.Select(ObserverS)

	startPolling(ObserveSeconds, finish)
end
