-- AUTO TEST — a technician ordered at something it can never reach must GIVE THE UNIT BACK,
-- not spin on the approach forever.
--
-- THE DEFECT. Enter.Tick's Approaching branch reaches its "we are not next to the target - lets
-- fix that" case only when an approach move has just ENDED without putting the unit beside the
-- target, and the only thing it can do there is queue another one. Nothing counted those and
-- nothing timed them out, so a target that can never be reached looped for the rest of the
-- match. It is not rescued from below either: MoveAdjacentTo cannot report failure at all
-- (Mobile.MoveResult is declared, read in three places and assigned in none), and
-- MoveCooldownHelper's designed escape for a blocked destination is doubly dead here because
-- Enter opts into RetryIfDestinationBlocked.
--
-- WHAT IS ASSERTED, and why it is not "did he capture it". Nobody can capture it — that is the
-- premise. The assertion is that CaptureDispatchManager.CommittedTarget stops naming the
-- derrick, i.e. the technician reports itself FREE again. That is the exact quantity the
-- reported harm is about: CommittedTarget reads the activity queue, so a technician wedged in an
-- approach counts as busy forever and every later dispatch skips him. Idle is asserted too, but
-- committed-target is the one that matters, because it is what the dispatcher actually reads.
--
-- THE CONTROL ARM. A second technician on open ground gets the same kind of order on the same
-- tick and must capture DerrickFree. If it does not, then the order routing, the actors or the
-- map is the confound and the wedge arm's result means nothing — so that is a FAIL with its own
-- message rather than a quiet pass.

-- Deadlines in TICKS, passed to Trigger.AfterDelay raw. Deliberately not DateTime.Seconds and
-- not TestHarness.AssertWithin: those carry two different and both-wrong ticks-per-second
-- constants (16 and 25 against a real 16.67), and this budget is derived from a tick quantity in
-- the engine, so converting through seconds would only add error.
--
-- The shipped budget is Enter.DefaultMaxStalledApproachTicks = 500, accumulated only on ticks
-- where an approach move has just ended and the technician has not changed cell. The deadline is
-- ~1.8x that so a GREEN has room and a RED is unambiguous rather than marginal.
local SettleTicks = 50
local VerdictTicks = 900
local TraceEvery = 100

-- The release must take roughly the shipped budget. Without a FLOOR this scenario has a
-- false-green path that passes on an UNFIXED build: if the order were refused outright at issue
-- time, or the technician were never able to start an approach at all, he would read as
-- uncommitted within a handful of ticks while never entering the retry loop this exists to
-- bound. Free at tick ~5 and free at tick ~500 are the same boolean and completely different
-- findings.
local MinReleaseTicks = 300

local ticks = 0
local ordered = false
local orderedAt = nil
local sawCommitted = false
local releasedTick = nil
local ringedIdleTick = nil
local sawBusy = false
local freeCapturedTick = nil
local ringBroken = nil
local wedgeOrderString = nil
local freeOrderString = nil

local ring = {}
local ringHome = {}
local ringNames = { "RingNW", "RingN", "RingNE", "RingW", "RingE", "RingSW", "RingS", "RingSE" }

-- The cage is the premise of the whole measurement, so its integrity is checked every tick
-- rather than assumed. A tank that died or wandered opens a way out, the technician walks off,
-- and any result after that is about walking rather than about the bound.
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

	if not TechRinged.IsDead then
		-- The technician must LEAVE his cell for this to be about walking rather than about the
		-- bound; if he ever does, the cage failed and checkRing above will usually have said so.
		if Test.CommittedCaptureTarget(TechRinged) ~= 0 then
			sawCommitted = true
		elseif sawCommitted and releasedTick == nil then
			releasedTick = ticks - orderedAt
		end

		if not TechRinged.IsIdle then
			sawBusy = true
		elseif sawBusy and ringedIdleTick == nil then
			ringedIdleTick = ticks - orderedAt
		end
	end

	-- The control technician is CONSUMED by a successful capture (^CapturesNeutralBuildings sets
	-- ConsumedByCapture), so the honest test of the control arm is the derrick's owner, not the
	-- technician's survival.
	if freeCapturedTick == nil and not DerrickFree.IsDead and DerrickFree.Owner == USAPlayer then
		freeCapturedTick = ticks - orderedAt
	end

	-- Live counters go to lua.log, NOT into a failure string: TestHarness.AssertWithin evaluates
	-- its message eagerly at registration, and the habit of interpolating counters into verdict
	-- text has already produced a run that reported zeros while a trace in the same closure
	-- reported the true values.
	if ticks % TraceEvery == 0 then
		print(string.format(
			"[capture-wedge] t=%d since-order=%d committed=%s released=%s idle=%s freeCaptured=%s ring=%s",
			ticks, ticks - orderedAt, tostring(Test.CommittedCaptureTarget(TechRinged)),
			tostring(releasedTick), tostring(ringedIdleTick), tostring(freeCapturedTick),
			tostring(ringBroken)))
	end
end

local function finish()
	print("[capture-wedge] verdict reached")

	if ringBroken ~= nil then
		Test.Fail(string.format(
			"VOID, not a result about the fix: the cage did not hold — %s. A way out means TechRinged " ..
			"was never in the unreachable state this scenario exists to measure. Re-site the ring or " ..
			"re-check the HoldFire staging.", ringBroken))
		return
	end

	if wedgeOrderString == nil then
		Test.Fail(
			"VOID, not a result about the fix: the right-click on DerrickTarget was REFUSED — no order " ..
			"string came back — so no capture order was ever issued and nothing was measured. Check " ..
			"that tecn still carries a Captures trait that can target a neutral OILB.")
		return
	end

	-- Control arm first: if an ordinary capture did not work, nothing about the wedge arm is
	-- attributable to the cage.
	if freeCapturedTick == nil then
		Test.Fail(string.format(
			"CONTROL ARM FAILED, so the wedge arm proves nothing: TechFree on open ground still has " ..
			"not captured DerrickFree %d ticks after a '%s' order. The order routing, the actors or " ..
			"the map is the confound here, not the approach bound.",
			VerdictTicks, tostring(freeOrderString)))
		return
	end

	if not sawCommitted then
		Test.Fail(string.format(
			"VOID, not a result about the fix: TechRinged never read as committed to anything after a " ..
			"'%s' order, so no CaptureActor activity was ever queued and the approach loop was never " ..
			"entered. That is a different defect from the wedge — check the order gate. Control arm " ..
			"captured in %d ticks.", tostring(wedgeOrderString), freeCapturedTick))
		return
	end

	if releasedTick == nil then
		Test.Fail(string.format(
			"WEDGE: TechRinged is STILL committed to DerrickTarget %d ticks after the order. He cannot " ..
			"leave his own cell, so the approach can never arrive, and Enter has neither given up nor " ..
			"stopped re-queueing it — he is spinning with no counter and no timeout. Because " ..
			"CommittedTarget reads the activity queue he counts as busy for the rest of the match, so " ..
			"every later dispatch skips him. Control arm captured normally in %d ticks, so the order " ..
			"path itself is fine.", VerdictTicks, freeCapturedTick))
		return
	end

	if releasedTick < MinReleaseTicks then
		Test.Fail(string.format(
			"VOID, not a result about the fix: TechRinged reported free after only %d ticks, far short " ..
			"of the ~500 the approach budget costs. He never entered the retry loop — the most likely " ..
			"cause is that the order was dropped or cancelled for an unrelated reason rather than " ..
			"bounded. Control arm captured in %d ticks.", releasedTick, freeCapturedTick))
		return
	end

	if ringedIdleTick == nil then
		Test.Fail(string.format(
			"HALF-FIXED: TechRinged stopped being committed to DerrickTarget after %d ticks but is " ..
			"still not IDLE at %d. The capture was released without the unit being handed back, so " ..
			"idle-driven behaviour stays silenced even though the dispatcher now sees him as free — " ..
			"which is arguably worse than the original wedge, because he will be dispatched again and " ..
			"cannot act. Control arm captured in %d ticks.",
			releasedTick, VerdictTicks, freeCapturedTick))
		return
	end

	Test.Pass(string.format(
		"an unreachable capture order now RELEASES the technician. TechRinged took the order, spent " ..
		"the approach budget without gaining a cell, and reported itself uncommitted after %d ticks " ..
		"and idle after %d — available to the dispatcher again rather than counted busy for the rest " ..
		"of the match. Control arm captured DerrickFree in %d ticks over the same window, so the cage " ..
		"is the only variable.", releasedTick, ringedIdleTick, freeCapturedTick))
end

USAPlayer = nil

WorldLoaded = function()
	USAPlayer = Player.GetPlayer("USA")
	Camera.Position = TechRinged.CenterPosition

	-- Listed literally rather than looked up by name: map actors arrive as engine-injected globals
	-- and the corpus has no precedent for indexing them dynamically. Order must match ringNames.
	ring = { RingNW, RingN, RingNE, RingW, RingE, RingSW, RingS, RingSE }
	for i, a in ipairs(ring) do
		ringHome[i] = { X = a.Location.X, Y = a.Location.Y }
	end

	-- HoldFire everywhere: the ring must be a cage, not a battle. A firefight would kill ring
	-- tanks, open the cage, and turn a wedge measurement into a shooting measurement.
	TechRinged.Stance = "HoldFire"
	TechFree.Stance = "HoldFire"
	for _, a in ipairs(ring) do
		a.Stance = "HoldFire"
	end

	print("[capture-wedge] staged")

	Trigger.AfterDelay(SettleTicks, function()
		local broken = checkRing()
		if broken ~= nil then
			Test.Fail("staging failed before anything was measured: " .. broken)
			return
		end

		if TechRinged.Location.X ~= 20 or TechRinged.Location.Y ~= 16 then
			Test.Fail(string.format(
				"staging failed before anything was measured: TechRinged settled on %d,%d instead of " ..
				"20,16, so the ring is not around him.",
				TechRinged.Location.X, TechRinged.Location.Y))
			return
		end

		-- The real player path: Test.ClickOrder resolves the whole targeter chain in descending
		-- OrderPriority exactly as a right-click does, so this exercises the routing a player gets
		-- rather than naming the order we hope for.
		orderedAt = ticks
		ordered = true
		wedgeOrderString = Test.ClickOrder(TechRinged, DerrickTarget)
		freeOrderString = Test.ClickOrder(TechFree, DerrickFree)

		print(string.format("[capture-wedge] ordered at tick %d wedge='%s' free='%s'",
			orderedAt, tostring(wedgeOrderString), tostring(freeOrderString)))

		Trigger.AfterDelay(VerdictTicks, finish)
	end)
end
