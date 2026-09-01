-- AUTO TEST — a transport that cannot put anybody down must GIVE UP and become responsive
-- again, not spin forever.
--
-- THE DEFECT. Cargo.CanUnload's blocking-strictness argument defaults to BlockedByActor.None,
-- which asks only "is any adjacent cell passable TERRAIN" and cannot see the units standing on
-- it. UnloadCargo.Tick gates on that loose predicate (:157) and then picks a real exit with
-- GetAvailableSubCell (:111-115), which DOES see occupancy. When the two disagree the activity
-- took a branch that did NotifyBlocker -> Wait(10) -> return false, with no counter and no
-- timeout: the transport never completed the activity, therefore never went idle, therefore
-- everything idle-driven it owns went quiet — including the AI's own "has it finished?" gate.
--
-- WHAT IS ASSERTED, and why it is the wedge rather than the symptom. Not "did anyone get out"
-- — nobody can get out, that is the premise. The assertion is that the ringed APC becomes IDLE
-- AGAIN while still holding all four men. Idle is the whole point: it is the difference between
-- a transport that refused an order and a transport that is lost.
--
-- WHY THE RING IS RUSSIAN AND NOT AMERICAN. Mobile.OnNotifyBlockingMove (:1019-1031) queues a
-- Nudge on any blocker that is FRIENDLY and idle — so a ring of friendly tanks dissolves itself
-- the moment the blocked unload notifies it, the unload then succeeds, and the scenario proves
-- nothing while reporting a confident pass. An enemy blocker returns early at the
-- AppearsFriendlyTo check and stays put. Both sides are put on HoldFire so the ring is a
-- geometric obstacle and not a firefight.
--
-- WHY THE RING IS TANKS AND NOT INFANTRY. Infantry take SUBCELLS — up to three share a cell —
-- so a ring of riflemen leaves free subcells, GetAvailableSubCell succeeds, and the unload
-- works. Only a full-cell occupant actually blocks an infantry exit.
--
-- THE CONTROL ARM. A second identical APC on open ground gets the same order on the same tick.
-- It must empty. If it does not, then the order path, the terrain, the passengers or the map is
-- the confound and the ringed arm's result means nothing — so that case is a FAIL with its own
-- message rather than a pass.

-- Deadlines in TICKS, passed to Trigger.AfterDelay raw. Deliberately not DateTime.Seconds and
-- not TestHarness.AssertWithin: those carry two different and both-wrong ticks-per-second
-- constants (16 and 25 against a real 16.67), and this scenario's budget is derived from a tick
-- quantity in the engine, so converting through seconds would only add error.
--
-- The shipped budget is Cargo.BlockedUnloadTimeout = 500, spent in Wait(10) increments, plus
-- BeforeUnloadDelay (8) at the start and AfterUnloadDelay (25) on the way out — about 533 ticks
-- from the order to idle. The deadline below is ~1.7x that, so a GREEN has plenty of room and a
-- RED is unambiguous rather than marginal.
local SettleTicks = 50
local VerdictTicks = 900
local TraceEvery = 100
local Carried = 4

-- The give-up must take roughly the shipped budget. Without a FLOOR this scenario has a false-green
-- path that passes on an UNFIXED build: if the cells around the ringed APC were impassable TERRAIN
-- rather than merely occupied, CanUnload() is false at the top of Tick, the activity takes the
-- ordinary completion branch on its first tick, and the APC is idle-and-still-loaded within ~10
-- ticks — satisfying every other assertion here while never entering the retry loop at all. Idle at
-- tick ~10 and idle at tick ~533 are the same boolean and completely different findings.
local MinIdleTicks = 400

local ticks = 0
local ordered = false
local orderedAt = nil
local sawBusy = false
local ringedIdleTick = nil
local freeEmptiedTick = nil
local ringedMinPax = Carried
local ringBroken = nil

local ring = {}
local ringHome = {}
local ringNames = { "RingN", "RingS", "RingW", "RingE", "RingNW", "RingNE", "RingSW", "RingSE" }

-- The ring is the premise of the whole measurement, so its integrity is checked every tick
-- rather than assumed. A tank that died or wandered opens an exit, and the unload would then
-- succeed for a reason that has nothing to do with the fix.
local function checkRing()
	for i, a in ipairs(ring) do
		if a.IsDead then
			return ringNames[i] .. " died"
		end

		local home = ringHome[i]
		if a.Location.X ~= home.X or a.Location.Y ~= home.Y then
			return ringNames[i] .. " moved off its cell"
		end
	end

	return nil
end

-- ScriptContext caches runtime.Globals["Tick"] once at load (ScriptContext.cs:242), so Tick has
-- to exist at file scope — one assigned inside WorldLoaded never runs.
Tick = function()
	ticks = ticks + 1

	if not ordered then
		return
	end

	if ringBroken == nil then
		ringBroken = checkRing()
	end

	if not RingedAPC.IsDead then
		if RingedAPC.PassengerCount < ringedMinPax then
			ringedMinPax = RingedAPC.PassengerCount
		end

		if not RingedAPC.IsIdle then
			sawBusy = true
		elseif sawBusy and ringedIdleTick == nil then
			ringedIdleTick = ticks - orderedAt
		end
	end

	if freeEmptiedTick == nil and not FreeAPC.IsDead and FreeAPC.PassengerCount == 0 then
		freeEmptiedTick = ticks - orderedAt
	end

	-- Live counters go to lua.log, NOT into a failure string: TestHarness.AssertWithin evaluates
	-- its message eagerly at registration, and the habit of interpolating counters into verdict
	-- text has already produced a run that reported zeros while a trace in the same closure
	-- reported the true values.
	if ticks % TraceEvery == 0 then
		print(string.format(
			"[unload-wedge] t=%d since-order=%d ringedPax=%d ringedIdle=%s busy=%s freeEmptied=%s ring=%s",
			ticks, ticks - orderedAt, RingedAPC.PassengerCount, tostring(ringedIdleTick),
			tostring(sawBusy), tostring(freeEmptiedTick), tostring(ringBroken)))
	end
end

local function finish()
	print("[unload-wedge] verdict reached")

	if ringBroken ~= nil then
		Test.Fail(string.format(
			"VOID, not a result about the fix: the ring did not hold — %s. An open exit means the " ..
			"ringed APC was never in the blocked state this scenario exists to measure. Re-site the " ..
			"ring or re-check the HoldFire staging.", ringBroken))
		return
	end

	-- Control arm first: if the ordinary unload did not work, nothing about the ringed arm is
	-- attributable to the ring.
	if freeEmptiedTick == nil then
		Test.Fail(string.format(
			"CONTROL ARM FAILED, so the ringed arm proves nothing: the unringed APC on open ground " ..
			"still holds %d of %d men %d ticks after the same order. The order path, the terrain or " ..
			"the passengers are the confound here, not the blocked-unload fix.",
			FreeAPC.PassengerCount, Carried, VerdictTicks))
		return
	end

	if ringedMinPax < Carried then
		Test.Fail(string.format(
			"VOID, not a result about the fix: the ringed APC got %d man/men out (low-water mark %d " ..
			"of %d), so its exits were not actually blocked and it was never in the wedge state. " ..
			"Control arm emptied in %d ticks.",
			Carried - ringedMinPax, ringedMinPax, Carried, freeEmptiedTick))
		return
	end

	if not sawBusy then
		Test.Fail(string.format(
			"the ringed APC never became busy after the Unload order, so the order was dropped " ..
			"before an UnloadCargo activity ever started. That is a different defect from the wedge " ..
			"— check Cargo.ResolveOrder's gate. Control arm emptied in %d ticks.", freeEmptiedTick))
		return
	end

	if ringedIdleTick == nil then
		Test.Fail(string.format(
			"WEDGE: the ringed APC is STILL not idle %d ticks after its Unload order. It holds all " ..
			"%d men, its exits are all blocked, and UnloadCargo has neither placed anybody nor " ..
			"given up — it is spinning on the retry loop with no counter and no timeout, so the " ..
			"transport never goes idle and every idle-driven behaviour it owns stays silenced. " ..
			"Control arm emptied normally in %d ticks, so the order path itself is fine.",
			VerdictTicks, Carried, freeEmptiedTick))
		return
	end

	if ringedIdleTick < MinIdleTicks then
		Test.Fail(string.format(
			"VOID, not a result about the fix: the ringed APC went idle after only %d ticks, far short " ..
			"of the ~533 the blocked-unload budget costs. It never entered the retry loop — the most " ..
			"likely cause is that its surrounding cells are impassable TERRAIN, which makes CanUnload " ..
			"false and ends the activity on its first tick without ever reaching the wedge. Re-site the " ..
			"ringed APC onto open ground. Control arm emptied in %d ticks.",
			ringedIdleTick, freeEmptiedTick))
		return
	end

	Test.Pass(string.format(
		"a blocked unload now RELEASES the transport. The ringed APC took the order, found every " ..
		"adjacent cell occupied, retried, and went idle again %d ticks later still holding all %d " ..
		"men — responsive rather than lost. Control arm on open ground emptied in %d ticks over the " ..
		"same window, so the ring is the only variable.",
		ringedIdleTick, Carried, freeEmptiedTick))
end

WorldLoaded = function()
	Camera.Position = RingedAPC.CenterPosition

	-- Listed literally rather than looked up by name: map actors arrive as engine-injected globals
	-- and the corpus has no precedent for indexing them dynamically. Order must match ringNames.
	ring = { RingN, RingS, RingW, RingE, RingNW, RingNE, RingSW, RingSE }
	for i, a in ipairs(ring) do
		ringHome[i] = { X = a.Location.X, Y = a.Location.Y }
	end

	-- HoldFire everywhere: the ring must be an obstacle, not a battle. A firefight would kill ring
	-- tanks, open exits, and turn a wedge measurement into a shooting measurement.
	RingedAPC.Stance = "HoldFire"
	FreeAPC.Stance = "HoldFire"
	for _, a in ipairs(ring) do
		a.Stance = "HoldFire"
	end

	print("[unload-wedge] staged")

	Trigger.AfterDelay(SettleTicks, function()
		if RingedAPC.PassengerCount ~= Carried or FreeAPC.PassengerCount ~= Carried then
			Test.Fail(string.format(
				"staging failed before anything was measured: expected %d aboard each APC, got %d " ..
				"(ringed) and %d (free). Check the m113 Cargo.InitialUnits override actually merged.",
				Carried, RingedAPC.PassengerCount, FreeAPC.PassengerCount))
			return
		end

		local broken = checkRing()
		if broken ~= nil then
			Test.Fail("staging failed before anything was measured: " .. broken)
			return
		end

		-- The real player path: Cargo's IIssueDeployOrder, the same one the deploy hotkey and the
		-- unload cursor both send.
		orderedAt = ticks
		ordered = true
		Test.IssueDeploy(RingedAPC)
		Test.IssueDeploy(FreeAPC)

		print("[unload-wedge] ordered at tick " .. tostring(orderedAt))

		Trigger.AfterDelay(VerdictTicks, finish)
	end)
end
