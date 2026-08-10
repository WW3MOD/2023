-- AUTO TEST — a starving platoon 41 cells out, BEYOND MaxFollowDistance: 35, must still be
-- resupplied, and must still be at the front when it happens.
--
-- ALWAYS RUN THIS AT `--seed -1848572889`, the seed the whole supply scenario family is pinned
-- to. Unseeded runs are NOT comparable across code changes: the platoon splits differently per
-- seed, and two rounds of analysis were burned on the danger sibling comparing what turned out
-- to be different matches under the same code.
--
-- WHAT THIS GUARDS. Measured live 2026-08-10:
--
--   truck=10@7,16 supply=750 target=<none> nearest-found=44,16 nearest-dist=37c/max-follow=35c
--
-- A full truck refused a dying five-man platoon because it was two cells past an arbitrary
-- number. `0ed01e0e` made starvation lift the leash per cluster, and until this file existed
-- nothing tested that. The failure mode is SILENT — a truck that never sets off logs nothing a
-- player would ever see — so a regression here would come back invisibly.
--
-- THE BAR IS THE DOCTRINE, NOT THE MECHANISM. How the ammo arrives is not asserted — aura
-- service in place, a crate dropped short and walked to, anything else — so this stays true if
-- the delivery route is later changed. What IS asserted is both halves of the doctrine
-- sentence: supplies reach the front, and the front is still the front when they get there.
--
-- WHY THE POSITION HALF EXISTS (measured at 14499b0a on the danger sibling). Without it the
-- ammo check alone passed while the doctrine was being violated: AutoSeekSupplies sends a
-- soldier under 25% ammo to the nearest provider inside a 20-cell leash, and a loaded truck IS
-- a provider, so the whole platoon walked out of position to meet it in open ground. "The
-- platoon got fed" is not the outcome the doctrine asks for. "The platoon got fed without
-- leaving its position" is. That trap is WORSE here, not better: the truck has 41 cells to
-- cover, so there is more time for the platoon to walk west while it comes.
--
-- WHY MAX DRIFT AND NOT FINAL POSITION — this is the trap, and a final-position check walks
-- straight into it. SeekSuppliesAndReturn walks the soldier BACK to where it was standing
-- (`origin`, settling for `HomeNearEnough = 2` cells), so the excursion is TRANSIENT: sample at
-- the end and the platoon is home, fed, and the abandonment is invisible. The drift that
-- matters is therefore the WORST seen over the whole run, not the drift at verdict time.
--
-- WHY A CLIMB PROVES RESUPPLY. ^E3's ReloadAmmoPool@1 is gated on `replenish-soldiers`
-- (rules/ingame/infantry.yaml:1196-1198), a condition only a SupplyProvider grants to units in
-- its aura. Ammo cannot regenerate on its own, ^E3 can dock only at `truk, logisticscenter` and
-- there is no logistics centre, and SUPPLYROUTE has no supply aura. Nothing but a real delivery
-- can move this number.
--
--   PASS = at least 2 of the 5 climb clear of starving AND no rifleman ever drifted more
--          than MAX_DRIFT cells from where it spawned.
--   FAIL = never fed at all (the leash still wins), or fed but displaced (the front collapsed
--          backwards into the supply line), or both.
--   SKIP = the setup did not hold (drain failed, platoon died) — inconclusive.

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

-- 10% — starving, but NOT empty: an all-pools-empty unit would be moved by the legacy
-- AmmoPool.AutoRearmIfAllEmpty path, and the test would pass without any delivery.
local DRAINED = 10
local STARVING = 25       -- 250 per mille of ^E3's 100-round pool — what the supply layer calls starving
local NEED_BACK = 2       -- how many of the 5 must climb clear for the platoon to count as resupplied
-- Cells a rifleman may stray from spawn and still count as holding the front. 6 = the doctrine's
-- own 5-cell crate standoff plus a cell of tolerance: "the truck can decide to stop a bit
-- early... like 5 cells behind the units in need, and the soldiers can themselves go to the
-- supply crate as needed." Walking 5 cells to a crate is CORRECT behaviour and must pass; the
-- 15-cell excursion measured at b632c36b is a front collapse and must not.
--
-- THIS IS THE TIGHTEST MARGIN IN THE SCENARIO — one cell. Both plausible delivery routes land
-- near 5: a crate dropped DropShortCells: 5 back along the cluster->truck line is 5 cells away,
-- and aura service brings the truck to within its own 5-cell provider range. That tightness is
-- deliberate and is not slack to be spent: it is the difference between the doctrine's crate
-- walk and a front that gave ground. If a run fails on drift alone at 7-8 cells, read the
-- per-man trace before touching this number — it is far more likely the platoon met the truck
-- halfway than that 6 is wrong.
local MAX_DRIFT = 6
local WINDOW = 90         -- harness-seconds of simulation before the window closes

WorldLoaded = function()
	local platoon = { Rifle1, Rifle2, Rifle3, Rifle4, Rifle5 }

	TestHarness.FocusBetween(Truck, Rifle3)
	TestHarness.Select(Truck)

	-- Stances are deliberately left alone, unlike the danger sibling which pins them to
	-- HoldFire. There is no enemy on this map, so autotarget has nothing to send them at and
	-- cannot be a source of drift. AutoSeekSupplies — the one thing that CAN walk them off the
	-- front — stays fully live either way (SupplyHuntMath.StancesPermitHunt vetoes only on
	-- resupply != Auto, engagement HoldPosition, or fire Ambush), which is the point: the test
	-- must be free to catch a front collapse, not configured so it cannot happen.

	-- Drain each rifleman's primary to exactly DRAINED. The RPG (secondary-ammo) is left full on
	-- purpose, so the all-pools-empty legacy rearm path stays out of the way.
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

	local truckSpawn = Truck.Location
	local truckMaxX = truckSpawn.X
	local truckLastCell = truckSpawn
	local truckGoneAtSecond = 0
	local bestBack = 0

	-- The straight-line spawn separation, in the SAME metric the follow leash uses:
	-- SupplyFollowerBotModule compares `(cluster.Center - truck.CenterPosition).Length / 1024`
	-- against the leash in cells, i.e. Euclidean, not Chebyshev. Measured to Rifle3, the man on
	-- the lane, which is also the cluster centroid's row.
	local sepDx = spawnCell[3].X - truckSpawn.X
	local sepDy = spawnCell[3].Y - truckSpawn.Y
	local SEPARATION = math.floor(math.sqrt(sepDx * sepDx + sepDy * sepDy))

	-- A verdict has already been written. Test.Pass/Skip can defer their exit, so later triggers
	-- still fire and would otherwise write a second, contradicting verdict over the first.
	local verdict = false

	-- Formatted from X/Y rather than via tostring: a CPos reaches Lua as a bound object, and
	-- whether it carries a __tostring is not something a diagnostic should depend on.
	local function cellText(c)
		if c == nil then return "<none>" end
		return string.format("%d,%d", c.X, c.Y)
	end

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

	-- Chebyshev cells from spawn. Both axes, because a platoon shoved sideways off its position
	-- has left it just as surely as one that walked west.
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

	-- "spawnX->nowX(worst)" per man — says at a glance whether they held, walked out, or walked
	-- out and came home again (the SeekSuppliesAndReturn signature: worst is large while nowX is
	-- back at spawnX).
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

	-- Setup precondition. If the drain did not take, the platoon was never starving, the
	-- starvation leash lift was never entitled to fire, and the run proves nothing about the
	-- doctrine — skip rather than report a verdict.
	Trigger.AfterDelay(sec(3), function()
		for _, r in ipairs(platoon) do
			if not r.IsDead and r.AmmoCount("primary-ammo") > STARVING then
				verdict = true
				Test.Skip("could not drain the platoon below the starving threshold — setup precondition failed")
				return
			end
		end
	end)

	for s = 1, WINDOW do
		Trigger.AfterDelay(sec(s), function()
			if verdict then return end

			if not Truck.IsDead then
				truckLastCell = Truck.Location
				if truckLastCell.X > truckMaxX then truckMaxX = truckLastCell.X end
			elseif truckGoneAtSecond == 0 then
				truckGoneAtSecond = s
			end

			sample()

			local back = resupplied()
			if back > bestBack then bestBack = back end

			-- BOTH halves, sampled together. Drift is cumulative, so a platoon that once left its
			-- position can never satisfy this again — which is the intent: the abandonment already
			-- happened, and walking home does not undo it.
			if back >= NEED_BACK and peakDrift() <= MAX_DRIFT then
				verdict = true
				Test.Pass()
			end
		end)
	end

	Trigger.AfterDelay(sec(WINDOW), function()
		if verdict then return end

		-- The inconclusive shape first, so a real doctrine failure is never confused with a setup
		-- that fell apart. NOTE the truck's absence is deliberately NOT one of these: no enemy
		-- exists on this map so the truck cannot be shot, and a truck that emptied itself into a
		-- crate and then evacuated as unusable residue has done nothing wrong. It is reported in
		-- the failure text, never used as a verdict.
		if alive() < NEED_BACK then
			verdict = true
			Test.Skip(string.format(
				"only %d of 5 riflemen survived — cannot judge resupply, inconclusive", alive()))
			return
		end

		local drift = peakDrift()
		local held = drift <= MAX_DRIFT
		local fed = bestBack >= NEED_BACK

		if fed and held then
			verdict = true
			Test.Pass()
			return
		end

		local why
		if held then
			why = string.format(
				"SUPPLY NEVER REACHED A FRONT %d CELLS OUT — %d of 5 climbed clear of starving (need %d) "
				.. "while the platoon held its position. If the truck never left its spawn cell, the follow "
				.. "leash refused the cluster: the starvation lift (StarvingFollowMinUnits / "
				.. "StarvingMaxFollowDistance, ai.yaml) is what should have admitted it past "
				.. "MaxFollowDistance: 35.",
				SEPARATION, bestBack, NEED_BACK)
		elseif fed then
			why = string.format(
				"THE FRONT COLLAPSED BACKWARDS — %d of 5 were fed, but the platoon left its position "
				.. "(peak drift %d cells, allowed %d). Supply did not reach the front; the front went to "
				.. "the supply.",
				bestBack, drift, MAX_DRIFT)
		else
			why = string.format(
				"worst case — the platoon left its position (peak drift %d cells, allowed %d) and STILL was "
				.. "not resupplied (%d of 5, need %d).",
				drift, MAX_DRIFT, bestBack, NEED_BACK)
		end

		Test.Fail(string.format(
			"%s primary ammo now %s (drained to %d, starving at <=%d). "
			.. "platoon spawnX->nowX(peak drift): %s. "
			.. "truck spawned at %s, %d cells from the platoon, and reached a furthest x=%d (last seen %s%s); "
			.. "provider aura 5c, MaxFollowDistance 35c, no believed enemy anywhere on this map",
			why, ammoTrace(), DRAINED, STARVING, driftTrace(),
			cellText(truckSpawn), SEPARATION, truckMaxX, cellText(truckLastCell),
			truckGoneAtSecond > 0 and string.format(", gone after %ds", truckGoneAtSecond) or ""))
	end)
end
