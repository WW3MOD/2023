-- AUTO TEST — a spent helicopter with no helipad EVACUATES; nothing else does.
--
-- User ruling 2026-08-19: "helicopters use helipad, if those do not exist they must
-- evacuate (They cannot be rearmed in that case)."
--
-- Map has no hpad and hpad is unbuildable, so HasRearmHost is false for all three
-- airframes — the state of every shipped map. What separates them is therefore NOT
-- the host term:
--
--   SpentHeli      drained, HAS Rearmable       => must leave (the behaviour under test)
--   LoadedHeli     full,    HAS Rearmable       => must stay  (keys on spent, not host)
--   Transport      no pools, no Rearmable       => must stay  (the transport guard)
--
-- NOTE on the transport leg: TRAN (Chinook) carries NO AmmoPool and NO Rearmable — it is
-- a Cargo airframe with no armament, as is HALO. So there is nothing to drain and the leg
-- pins the POOL-COUNT refusal, not the Rearmable one. The Rearmable term is pinned in
-- NUnit instead (AirframeEvacMathTest.ArmedTransportWithoutRearmableNeverEvacuates); no
-- shipped actor can reach it, because none has pools without a Rearmable.
--
-- RED (before EvacuateWhenUnrearmable): SpentHeli hovers on FlyIdle at its spawn for
-- the whole match and the verdict is
--   "SpentHeli never left: 0 cells from spawn, still in world"
--
-- WHY "left" IS TWO CONDITIONS. RotateToEdge flies the airframe past the map boundary
-- and only then removes and refunds it, so at the verdict tick it may legitimately be
-- either already gone OR still in transit. Asserting only "gone" would make the result
-- a race against the flight time; asserting only "moved" would pass a heli that merely
-- drifted. Either one counts, and the failure text reports both numbers.

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

local WINDOW = 60      -- seconds of simulation before the verdict
local LEFT_CELLS = 12  -- distance from spawn that counts as "committed to the exit"
local STAY_CELLS = 6   -- a control airframe may drift at most this far (FlyIdle circles)

local POOLS = { "primary-ammo", "secondary-ammo" }

local function drain(a)
	if a and not a.IsDead then
		for _, p in ipairs(POOLS) do a.Reload(p, -9999) end
	end
end

local function totalAmmo(a)
	if not a or a.IsDead then return -1 end
	local n = 0
	for _, p in ipairs(POOLS) do n = n + a.AmmoCount(p) end
	return n
end

-- Cell distance from a remembered spawn. An actor that has left the world keeps its
-- last Location, so callers must test IsInWorld separately rather than reading a big
-- number here as proof of anything.
local function movedFrom(a, sx, sy)
	if not a or a.IsDead then return -1 end
	local dx, dy = a.Location.X - sx, a.Location.Y - sy
	return math.floor(math.sqrt(dx * dx + dy * dy))
end

WorldLoaded = function()
	TestHarness.FocusBetween(SpentHeli, SpentTransport)

	local heliX, heliY = SpentHeli.Location.X, SpentHeli.Location.Y
	local loadedX, loadedY = LoadedHeli.Location.X, LoadedHeli.Location.Y
	local tranX, tranY = SpentTransport.Location.X, SpentTransport.Location.Y

	-- Keep SpentHeli at zero for the whole run. Airframes carry ReloadAmmoPool gated on
	-- `unit.docked && !airborne`, so an airborne heli cannot trickle-refill — but
	-- re-draining every second is cheap and removes the question from the verdict.
	--
	-- SpentTransport is deliberately NOT drained or queried: it has no AmmoPool at all, so
	-- Reload/AmmoCount on it would be a call against a pool that does not exist. It is
	-- already in the state the guard is about.
	drain(SpentHeli)
	for s = 1, WINDOW do
		Trigger.AfterDelay(sec(s), function()
			drain(SpentHeli)
		end)
	end

	-- Preconditions. If these do not hold the run proves nothing, so SKIP rather than
	-- report a pass or a failure that is really a broken setup.
	Trigger.AfterDelay(sec(3), function()
		if SpentHeli.IsDead or SpentTransport.IsDead or LoadedHeli.IsDead then
			Test.Skip("an airframe died during setup — inconclusive")
			return
		end
		if totalAmmo(SpentHeli) ~= 0 then
			Test.Skip(string.format("could not empty SpentHeli (ammo=%d) — setup precondition failed",
				totalAmmo(SpentHeli)))
			return
		end
		if totalAmmo(LoadedHeli) <= 0 then
			Test.Skip("LoadedHeli is not carrying ammo — the control leg would prove nothing")
			return
		end
	end)

	Trigger.AfterDelay(sec(WINDOW), function()
		-- 1. The airframe under test must have committed to the exit.
		local heliGone = SpentHeli.IsDead or not SpentHeli.IsInWorld
		local heliMoved = movedFrom(SpentHeli, heliX, heliY)
		if not heliGone and heliMoved < LEFT_CELLS then
			Test.Fail(string.format(
				"SpentHeli never left: %d cells from spawn, still in world (ammo=%d). " ..
				"A spent heli with no helipad must evacuate, not hover.",
				heliMoved, totalAmmo(SpentHeli)))
			return
		end

		-- 2. THE TRANSPORT GUARD. Same absent host as SpentHeli, but no ammo pools and no
		--    Rearmable — an unarmed Cargo airframe, which has not lost anything by being
		--    unable to rearm and must be left alone.
		local tranGone = SpentTransport.IsDead or not SpentTransport.IsInWorld
		local tranMoved = movedFrom(SpentTransport, tranX, tranY)
		if tranGone or tranMoved > STAY_CELLS then
			Test.Fail(string.format(
				"SpentTransport evacuated (gone=%s, moved=%d cells): an unarmed transport must not be " ..
				"retired for lacking a rearm host — this flies loaded troop transports off the map.",
				tostring(tranGone), tranMoved))
			return
		end

		-- 3. Being unhosted is not on its own a reason to leave — every heli on every
		--    shipped map is unhosted.
		local loadedGone = LoadedHeli.IsDead or not LoadedHeli.IsInWorld
		local loadedMoved = movedFrom(LoadedHeli, loadedX, loadedY)
		if loadedGone or loadedMoved > STAY_CELLS then
			Test.Fail(string.format(
				"LoadedHeli evacuated (gone=%s, moved=%d cells) while still carrying %d rounds — " ..
				"the trait is firing on the absent host alone instead of on being spent.",
				tostring(loadedGone), loadedMoved, totalAmmo(LoadedHeli)))
			return
		end

		Test.Pass()
	end)
end
