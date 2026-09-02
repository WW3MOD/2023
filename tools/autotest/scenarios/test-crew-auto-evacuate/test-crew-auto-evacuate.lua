-- AUTO TEST: ejected crew evacuate themselves, and a real player order cancels that evacuation.
--
-- User request (2026-09-01): "Crew and pilots should auto evacuate as soon as they are out, if
-- another order is given it is canceled, so it is a one time order given when they spawn (exit the
-- vehicle/aircraft)."
--
-- Half A: crew left alone leave the world — RotateToEdge walks them past the map edge and disposes
--         them, banking the refund.
-- Half B: the ONE crew member handed a real Move order is still in the world at the deadline,
--         standing where he was sent.
--
-- Half B is the one worth a game. That the evacuation is ONE-SHOT and freely overridable is a
-- property of HOW it is queued — a plain top-level activity, which an unqueued order truncates —
-- and no unit test can see it. The wrong implementation it guards against is a standing mode or an
-- INotifyIdle re-issue, either of which would re-evacuate the man the moment his new move ended and
-- make crew impossible for a player to keep.
--
-- ============================================================================================
-- WHY THE FIRST VERSION OF THIS SCENARIO WAS RED, and it was NOT the engine (2026-09-01, seed
-- -1321472130, "the overridden crew member is at 33,27 rather than near his ordered cell 27,27").
-- 33,27 is the hull's own cell — the spawn cell — which is the tell. Two independent scenario bugs:
--
--   1. IT NEVER ISSUED AN OVERRIDE AT ALL. It used the Lua `kept.Move(cell)` API, which is
--      MobileProperties.Move (MobileProperties.cs:33-42) and does `Self.QueueActivity(new Move(..))`
--      — it APPENDS to the activity queue. It does not cancel anything, so the RotateToEdge it was
--      supposed to override was still sitting in front of it, untouched. That file carries a PITFALL
--      comment saying exactly this and naming the fix, and TestGlobal.IssueMoveOrder repeats it
--      (TestGlobal.cs:533-540): a scenario written against the Lua API "goes RED before the fix and
--      RED after it, for the same reason both times, with the code under test never executed."
--      That is precisely what happened. Now uses Test.IssueMoveOrder, which issues a real unqueued
--      "Move" through the order path, exactly as a player right-click does.
--
--   2. IT GRADED ON THE TICK IT ORDERED. The predicate claimed half A ("everyone else is gone") the
--      instant the FIRST man appeared — crew eject staggered ~15 ticks apart, so one man out is
--      trivially "all others gone" — and then measured the kept man's drift on that same tick,
--      before he had moved a step. It reported the spawn cell because he was still standing on it.
--      Now: wait for the whole crew to be out before ordering anybody, and let a miss on the drift
--      check KEEP WAITING rather than fail, so only the deadline can end the run.
--
-- WHAT THE ENGINE SIDE ACTUALLY LOOKS LIKE, read but not yet watched — which is why this runs.
-- The evacuation is queued at top level behind the dismount walk. An unqueued order reaches
-- Actor.QueueActivity(false, ..) -> CancelActivity -> Activity.Cancel with keepQueue false, which
-- sets NextActivity = null (Activity.cs:209-212) and so drops the queued RotateToEdge with it.
-- test-dry-evac-drops-queued-order already exercises that same truncation in the other direction.
-- RotateToEdge only refuses cancellation for its final two cells past the boundary
-- (RotateToEdge.cs:303-307), deliberately, so a man who has not yet reached the edge is
-- interruptible throughout.
-- ============================================================================================
--
-- RED before the change: half A fails outright, because nothing evacuated crew AT EJECTION. Stated
-- precisely, because two other evacuation paths exist and neither covers this case:
--   * PoiOffensiveBotModule.SweepEjectedCrew is bot-only (IsEjectedCrewSweepCandidate rejects any
--     actor whose Owner is not the module's own player) and is gated OFF on @stable.
--   * AmmoPool.EvacuateForRefund (AmmoPool.cs:823-830) would eventually take a crew member, but only
--     once he has run DRY — 24 pistol rounds away, and nothing to do with dismounting.

-- Budget in TICKS and divide back through the harness constant. TestHarness.TicksPerSecond is 25
-- while the mod runs at Timestep 60 = 16.67 ticks/second; the constant is deliberately wrong and is
-- pinned by AutotestTickRateTest.cs, so anything sized in "seconds" here would silently mean
-- something else. 1500 ticks is ~90 real seconds.
local function ticks(t) return t / TestHarness.TicksPerSecond end

local DeadlineTicks = 1500
local HullX = 33
local HullY = 27
local KeepX = 27
local KeepY = 27
local KeepCell = CPos.New(KeepX, KeepY)
local ArrivalTolerance = 2

local CommanderType = "crew.commander.america"
local GunnerType = "crew.gunner.america"
local DriverType = "crew.driver.america"

local kept = nil
local elapsed = 0

-- Live actors of one crew type. IsDead and IsInWorld sit on BaseActorProperties, which is
-- [ExposedForDestroyedActors] and makes no trait queries, so both stay callable after RotateToEdge
-- disposes a man at the map edge. Location does NOT — it reads OccupiesSpace — so every read of it
-- below happens only on an actor that passed this filter first.
local function LiveOf(owner, actorType)
	local live = {}
	for _, a in ipairs(owner.GetActorsByType(actorType)) do
		if not a.IsDead and a.IsInWorld then
			live[#live + 1] = a
		end
	end

	return live
end

WorldLoaded = function()
	TestHarness.FocusBetween(Tank)
	TestHarness.Select(Tank)

	local owner = Tank.Owner

	-- ~40% HP: past EjectionDamageState (Heavy = HP < 50%) so the whole crew bails. rules.yaml
	-- removes the hull's bleed-out, the finishing-shot crew damage and the inherited fire, so a man
	-- missing from the world can only mean he evacuated — never that he burned to death. That
	-- distinction is the whole scenario: a dead crew member and an evacuated one are both "gone".
	Tank.Health = math.floor(Tank.MaxHealth * 4 / 10)

	TestHarness.AssertWithin(ticks(DeadlineTicks), function()
		elapsed = elapsed + 1

		local commanders = LiveOf(owner, CommanderType)
		local gunners = LiveOf(owner, GunnerType)
		local drivers = LiveOf(owner, DriverType)

		-- PHASE 1 — wait for the WHOLE crew to be out before touching anybody. Ordering on the first
		-- man to appear is what made the previous version grade itself on an empty world.
		if kept == nil then
			if #commanders == 0 or #gunners == 0 or #drivers == 0 then
				return false
			end

			-- The driver ejects LAST (EjectionOrder: Commander, Gunner, Driver), so he is the least
			-- far along his evacuation and has the shortest walk back to the ordered cell.
			kept = drivers[1]

			-- A REAL unqueued Move order, not the Lua activity API. This is the thing under test.
			Test.IssueMoveOrder(kept, KeepCell)
			return false
		end

		-- THE DEFECT SIGNAL. If the man who was given an order leaves the world anyway, the
		-- evacuation survived the order and the disposition is not the one-shot the user asked for.
		if kept.IsDead or not kept.IsInWorld then
			return "fail: ONE-SHOT DEFECT — the crew member given a real unqueued Move order left " ..
				"the world anyway, so the auto-evacuation was NOT cancelled by the order. This is a " ..
				"standing disposition rather than the one-time order the user asked for; " ..
				"VehicleCrewInfo.AutoEvacuateOnEject is the switch to turn off while it is fixed"
		end

		-- HALF A — the two men left alone must have evacuated. Counted by TYPE rather than by
		-- comparing actor references, so nothing here depends on Lua userdata identity.
		if #commanders > 0 or #gunners > 0 then
			return false
		end

		-- HALF B — the kept man must have arrived where he was sent. A miss KEEPS WAITING; only the
		-- deadline ends the run, so a slow walk can never be misread as a cancelled order.
		if TestHarness.CellDrift(kept.Location.X, kept.Location.Y, KeepX, KeepY) > ArrivalTolerance then
			return false
		end

		return true
	end, function()
		local commanders = LiveOf(owner, CommanderType)
		local gunners = LiveOf(owner, GunnerType)
		local drivers = LiveOf(owner, DriverType)

		local note = "no verdict within " .. DeadlineTicks .. " ticks; elapsed=" .. elapsed ..
			" commanders=" .. #commanders .. " gunners=" .. #gunners .. " drivers=" .. #drivers ..
			" ordered=" .. tostring(kept ~= nil)

		if kept == nil then
			note = note .. " — the crew never all ejected, so nothing was ever ordered. This is a " ..
				"STAGING failure, not a verdict on the evacuation: check the tank reached Heavy and " ..
				"that rules.yaml kept the crew alive"
		elseif not kept.IsDead and kept.IsInWorld then
			note = note .. " kept=" .. kept.Location.X .. "," .. kept.Location.Y ..
				" target=" .. KeepX .. "," .. KeepY .. " idle=" .. tostring(kept.IsIdle)

			if #commanders > 0 or #gunners > 0 then
				note = note .. " — the OTHER crew are still here, so half A did not happen: the " ..
					"auto-evacuation either never started or is slower than the deadline. Check " ..
					"whether RotateToEdge was queued at all before assuming it is a timing problem"
			else
				note = note .. " — half A passed and the kept man is alive, so the one-shot IS being " ..
					"cancelled correctly; he simply has not reached the ordered cell yet. Raise " ..
					"DeadlineTicks rather than suspecting the engine"
			end
		end

		note = note .. " hull=" .. HullX .. "," .. HullY

		return note
	end)
end
