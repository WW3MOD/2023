-- AUTO TEST — supplies must reach a starving platoon AT THE FRONT, without the platoon
-- leaving the front to fetch them.
--
-- ALWAYS RUN THIS AT `--seed -1848572889`. Unseeded runs of this scenario are NOT comparable
-- and two rounds of analysis were wasted comparing different matches: at one seed the platoon
-- split, two men walking west to the truck and three east toward the grads, which hit the
-- "2 of 5 fed" bar precisely while the platoon disintegrated in both directions; at another
-- all five went east and none were fed. Same code, opposite traces.
--
-- THE BAR IS THE DOCTRINE, NOT THE MECHANISM. How the ammo arrives is not asserted — aura
-- service in place, a crate dropped short and walked to, anything else — so this stays true
-- if the delivery route is later changed. What IS asserted is both halves of the doctrine
-- sentence: supplies reach the front, and the front is still the front when they get there.
--
-- WHY THE POSITION HALF EXISTS (measured at 14499b0a). Without it this scenario PASSED while
-- the doctrine was being violated. AutoSeekSupplies sends a soldier under 25% ammo to the
-- nearest provider inside a 20-cell leash, and a loaded truck IS a provider — so when the
-- truck drove to x=29 the whole platoon walked nine cells west out of its position and met it
-- in open ground under danger=308180. Ammo came up; the front had collapsed backwards into
-- the supply line. "The platoon got fed" is not the outcome the doctrine asks for. "The
-- platoon got fed without leaving its position" is.
--
-- WHY MAX DRIFT AND NOT FINAL POSITION — this is the trap, and a final-position check walks
-- straight into it. SeekSuppliesAndReturn walks the soldier BACK to where it was standing
-- (`origin`, settling for `HomeNearEnough = 2` cells), so the excursion is TRANSIENT: sample
-- at the end and the platoon is home, fed, and the abandonment is invisible. The drift that
-- matters is therefore the WORST seen over the whole run, not the drift at verdict time.
--
-- WHY A CLIMB PROVES RESUPPLY. ^E3's ReloadAmmoPool@1 is gated on `replenish-soldiers`
-- (rules/ingame/infantry.yaml:1196-1198), a condition only a SupplyProvider grants to units in
-- its aura. Ammo cannot regenerate on its own, ^E3 can dock only at `truk, logisticscenter`
-- and there is no logistics centre, and SUPPLYROUTE has no supply aura. Nothing but a real
-- delivery can move this number.
--
--   PASS = at least 2 of the 5 climb clear of starving AND no rifleman ever drifted more
--          than MAX_DRIFT cells from where it spawned.
--   FAIL = fed but displaced (the front collapsed backwards), or never fed at all, or both.
--   SKIP = the setup did not hold (drain failed, platoon or truck died) — inconclusive.

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

-- 10% — starving, but NOT empty: an all-pools-empty unit would be moved by the legacy
-- AmmoPool.AutoRearmIfAllEmpty path, and the test would pass without any delivery.
local DRAINED = 10
local STARVING = 25       -- 250 per mille of ^E3's 100-round pool — what the supply layer calls starving
local NEED_BACK = 2       -- how many of the 5 must climb clear for the platoon to count as resupplied
-- Cells a rifleman may stray from spawn and still count as holding the front. 6 = the doctrine's own
-- 5-cell crate standoff plus a cell of tolerance: "the truck can decide to stop a bit early... like 5
-- cells behind the units in need, and the soldiers can themselves go to the supply crate as needed."
-- Walking 5 cells back to a crate is CORRECT behaviour and must pass; the 15-cell excursion measured at
-- b632c36b is a front collapse and must not.
local MAX_DRIFT = 6
local WINDOW = 90         -- harness-seconds of simulation before the window closes

WorldLoaded = function()
	local platoon = { Rifle1, Rifle2, Rifle3, Rifle4, Rifle5 }
	local grads = { Grad1, Grad2 }

	TestHarness.FocusBetween(Truck, Rifle3)
	TestHarness.Select(Truck)

	-- The grads exist to be BELIEVED, not to shoot. They paint the danger field either way
	-- (the kernel is built from the actor type's armament, not from its current stance), so
	-- holding fire keeps the verdict about resupply instead of about who survived a barrage.
	for _, g in ipairs(grads) do
		if not g.IsDead then g.Stance = "HoldFire" end
	end

	-- Pin the platoon against every OTHER reason to move, so drift means one thing.
	-- HoldFire is chosen deliberately because it does NOT suppress the behaviour under test:
	-- SupplyHuntMath.StancesPermitHunt vetoes only on resupply != Auto, engagement
	-- HoldPosition, or fire Ambush — HoldFire is not among them, so AutoSeekSupplies stays
	-- fully live and is free to walk them off the front if that is what it really does.
	for _, r in ipairs(platoon) do
		if not r.IsDead then r.Stance = "HoldFire" end
	end

	-- Drain each rifleman's primary to exactly DRAINED. The RPG (secondary-ammo) is left
	-- full on purpose, so the all-pools-empty legacy rearm path stays out of the way.
	for _, r in ipairs(platoon) do
		if not r.IsDead then
			local have = r.AmmoCount("primary-ammo")
			if have > DRAINED then r.Reload("primary-ammo", -(have - DRAINED)) end
		end
	end

	local spawnCell = {}
	local worstDrift = {}
	for i, r in ipairs(platoon) do
		spawnCell[i] = r.Location
		worstDrift[i] = 0
	end

	local truckStartX = Truck.Location.X
	local truckMaxX = Truck.Location.X
	local bestBack = 0
	local done = false

	local function alive()
		local n = 0
		for _, r in ipairs(platoon) do
			if not r.IsDead then n = n + 1 end
		end
		return n
	end

	local function resupplied()
		local n = 0
		for _, r in ipairs(platoon) do
			if not r.IsDead and r.AmmoCount("primary-ammo") > STARVING then n = n + 1 end
		end
		return n
	end

	-- Chebyshev cells from spawn. Both axes, because a platoon shoved sideways off its
	-- position has left it just as surely as one that walked west.
	local function sample()
		for i, r in ipairs(platoon) do
			if not r.IsDead then
				local dx = math.abs(r.Location.X - spawnCell[i].X)
				local dy = math.abs(r.Location.Y - spawnCell[i].Y)
				local d = math.max(dx, dy)
				if d > worstDrift[i] then worstDrift[i] = d end
			end
		end
	end

	local function peakDrift()
		local m = 0
		for _, d in ipairs(worstDrift) do
			if d > m then m = d end
		end
		return m
	end

	-- "spawnX->nowX(worst)" per man — says at a glance whether they held, walked out, or
	-- walked out and came home again (the SeekSuppliesAndReturn signature: worst is large
	-- while nowX is back at spawnX).
	local function driftTrace()
		local parts = {}
		for i, r in ipairs(platoon) do
			if r.IsDead then
				parts[i] = "dead"
			else
				parts[i] = string.format("%d->%d(%d)", spawnCell[i].X, r.Location.X, worstDrift[i])
			end
		end
		return table.concat(parts, " ")
	end

	local function ammoTrace()
		local parts = {}
		for i, r in ipairs(platoon) do
			if r.IsDead then
				parts[i] = "dead"
			else
				parts[i] = tostring(r.AmmoCount("primary-ammo"))
			end
		end
		return table.concat(parts, "/")
	end

	-- Setup precondition. If the drain did not take, the platoon was never starving and the
	-- run proves nothing about the doctrine — skip rather than report a verdict.
	Trigger.AfterDelay(sec(3), function()
		for _, r in ipairs(platoon) do
			if not r.IsDead and r.AmmoCount("primary-ammo") > STARVING then
				Test.Skip("could not drain the platoon below the starving threshold — setup precondition failed")
				return
			end
		end
	end)

	for s = 1, WINDOW do
		Trigger.AfterDelay(sec(s), function()
			if done then return end

			if not Truck.IsDead and Truck.Location.X > truckMaxX then
				truckMaxX = Truck.Location.X
			end

			sample()

			local back = resupplied()
			if back > bestBack then bestBack = back end

			-- BOTH halves, sampled together. Drift is cumulative, so a platoon that once left
			-- its position can never satisfy this again — which is the intent: the abandonment
			-- already happened, and walking home does not undo it.
			if back >= NEED_BACK and peakDrift() <= MAX_DRIFT then
				done = true
				Test.Pass()
			end
		end)
	end

	Trigger.AfterDelay(sec(WINDOW), function()
		if done then return end

		-- Inconclusive shapes first, so a real doctrine failure is never confused with a
		-- setup that fell apart.
		if Truck.IsDead then
			Test.Skip("the supply truck died before the window closed — inconclusive")
			return
		end

		if alive() < NEED_BACK then
			Test.Skip(string.format(
				"only %d of 5 riflemen survived — cannot judge resupply, inconclusive", alive()))
			return
		end

		local drift = peakDrift()
		local held = drift <= MAX_DRIFT
		local fed = bestBack >= NEED_BACK

		if fed and held then
			Test.Pass()
			return
		end

		local why
		if fed then
			why = string.format(
				"THE FRONT COLLAPSED BACKWARDS — %d of 5 were fed, but the platoon left its position "
				.. "(peak drift %d cells, allowed %d). Supply did not reach the front; the front went to the supply.",
				bestBack, drift, MAX_DRIFT)
		elseif held then
			why = string.format(
				"supply never reached the front — %d of 5 climbed clear of starving (need %d), platoon held position.",
				bestBack, NEED_BACK)
		else
			why = string.format(
				"worst case — the platoon left its position (peak drift %d cells, allowed %d) and STILL was not "
				.. "resupplied (%d of 5, need %d).",
				drift, MAX_DRIFT, bestBack, NEED_BACK)
		end

		Test.Fail(string.format(
			"%s primary ammo now %s (drained to %d, starving at <=%d). "
			.. "platoon spawnX->nowX(peak drift): %s. "
			.. "truck went from x=%d to a furthest x=%d (aura 5c); danger wall starts at x=16",
			why, ammoTrace(), DRAINED, STARVING, driftTrace(), truckStartX, truckMaxX))
	end)
end
