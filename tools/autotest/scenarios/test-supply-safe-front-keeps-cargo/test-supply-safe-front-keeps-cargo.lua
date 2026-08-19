-- AUTO TEST — on a front with NO believed enemy contact, the truck must drive up, serve the
-- starving platoon from its aura, and KEEP THE REST OF ITS LOAD.
--
-- This is the SAFE half of the settled supply doctrine, and the sibling of
-- test-supply-under-danger (same map, same geometry, enemy removed):
--
--   "If there is no danger the truck can go up to them and resupply them directly and not
--    unload, if more resupplying is needed elsewhere."
--
-- Four clauses, ALL of which must hold, evaluated together when the window closes:
--
--   (1) AMMO CAME UP. At least 2 of the 5 riflemen climb clear of the starving threshold.
--       Same shape as the danger scenario: measured on the ammo pool, never on an order or a
--       branch, so it stays true if the delivery route is later changed. ^E3's ReloadAmmoPool@1
--       is gated on `replenish-soldiers` (infantry.yaml:1196-1198), a condition only a
--       SupplyProvider grants inside its aura, so primary ammo cannot regenerate on its own and
--       any rise at all means real supply reached them.
--
--   (2) NO CRATE EXISTS — and none ever did. A `supplycache` anywhere on the map is a fail. On
--       an undefended front a dropped crate strands 750 supply in a field. Latched across the
--       whole window rather than sampled at the end, because SUPPLYCACHE self-removes once
--       drained (RemoveBelowSupply: 1, misc.yaml) and a crate that appeared and then vanished
--       must still fail.
--
--   (3) THE TRUCK STILL HOLDS SUPPLY. rules.yaml gives the truck RemoveBelowSupply: 1 purely so
--       this is readable — there is no Lua binding for CurrentSupply — so "truck still in the
--       world" IS "truck still has supply". The drop path is all-or-nothing
--       (DropsSupplyCache.cs:85-125 calls SetSupply(0)), so a truck at zero has dropped even if
--       the crate has already been consumed; and honest service cannot empty it, because
--       refilling five riflemen costs ~25 of 750 (^E3 primary: 20 rounds per 1 supply).
--
--   (4) THE PLATOON HELD THE FRONT. No rifleman ever strayed further than the allowance from
--       where it started, measured as the PEAK over the run rather than the value at verdict
--       time. Without this clause the scenario could pass while the doctrine was violated in
--       the one way clauses 1-3 cannot see: AutoSeekSupplies sends a man under 25% ammo to the
--       nearest provider inside a 20-cell leash and a loaded truck IS a provider, so the
--       platoon can walk backwards to meet the truck, get fed, and walk home again. Ammo up,
--       no crate, truck still loaded — clauses 1, 2 and 3 all satisfied by a front that
--       collapsed into its own supply line. Peak and not final position because
--       SeekSuppliesAndReturn walks the man BACK to `origin` (HomeNearEnough = 2 cells), so
--       the excursion is transient and a verdict-time sample sees the platoon home and tidy.
--       Measurement shared with test-supply-under-danger via TestHarness.DriftTracker.
--
--   PASS = all four clauses hold.
--   FAIL = any clause broke; the message says which, and reports enough to tell "the truck
--          never went" from "it went and served" from "it went and dumped a crate" from "the
--          platoon went and fetched it".
--   SKIP = the setup did not hold (drain failed, platoon died) — inconclusive rather than a
--          false verdict about the doctrine.

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

-- 10% — starving, but NOT empty: an all-pools-empty unit would be moved by the legacy
-- AmmoPool.AutoRearmIfAllEmpty path, and the test would pass without any delivery.
local DRAINED = 10
local STARVING = 25       -- 250 per mille of ^E3's 100-round pool — what the supply layer calls starving
local NEED_BACK = 2       -- how many of the 5 must climb clear for the platoon to count as resupplied
local WINDOW = 90         -- harness-seconds of simulation before the window closes
local CACHE_TYPE = "supplycache"

-- Cells a rifleman may stray from spawn and still count as holding the front WITH NO CRATE on the
-- ground — which here is the doctrine's steady state, not an edge case: clause 2 fails the run if a
-- crate ever appears, so this is the allowance that governs every run that could pass.
--
-- ONE CELL, AND THE NUMBER IS GEOMETRIC RATHER THAN CONVENTIONAL. This scenario's safe branch is
-- "the truck closes to aura range, serves in place and KEEPS its cargo" (SupplyFollowerBotModule.cs
-- :354, :1468) — the truck comes to the platoon, so the platoon has nothing it needs to walk to.
-- What it may need is a step to get INSIDE the aura, and one cell covers that on this map's exact
-- geometry. TRUK's aura is Range: 5c0 (rules/ingame/vehicles.yaml:569), tested as horizontal
-- distance SQUARED (SupplyProvider.InAuraRange, SupplyProvider.cs:1124-1127, so dx*dx + dy*dy <= 25
-- in cells, not Chebyshev). The platoon is a column at x=44, y=14..18 and the truck drives in along
-- y=16. A truck that has closed to the CENTRE man's aura edge sits at (39,16) and covers only him:
-- (44,16) is 25 <= 25, but (44,15) is 26 and (44,14) is 29, both outside. One cell west puts every
-- man inside — (43,15) is 17, (43,14) is 20 — so a single cell is exactly enough for the doctrine's
-- own service pattern, and it is enough for clause 1 too (the centre man is served at drift 0, a
-- second man at drift 1, which is the 2-of-5 bar).
--
-- SO A PEAK ABOVE 1 INDICTS SOMETHING REAL, and the verdict does not have to guess which: either the
-- platoon abandoned the front to meet the truck, or the truck never closed to aura range and the men
-- walked the rest of the way. Both break the safe doctrine, which is why one number can carry both.
--
-- NOT 6. The sibling's 6-cell MAX_DRIFT is licensed by ONE specific correct behaviour — walking to a
-- crate dropped short of the platoon — and that behaviour cannot legitimately occur here, because a
-- crate existing at all is clause 2 failing. The sibling records what happens when 6 is applied with
-- no crate on the ground: at 9861bcf4 all five men walked out to meet the TRUCK and the run PASSED at
-- drift 5 with `crate=NONE placed`, which is precisely the front collapse the clause exists to catch.
-- A permanently-no-crate scenario with a 6-cell allowance would be that broken configuration
-- permanently, so the sibling's HOLD_DRIFT is the number that transfers here, not its MAX_DRIFT.
local HOLD_DRIFT = 1

-- Cells allowed once a supplycache HAS existed: the doctrine's 5-cell crate standoff plus a cell of
-- tolerance, same number and same reason as the sibling's MAX_DRIFT.
--
-- This is unreachable in a PASSING run — clause 2 has already failed any run where a crate appeared —
-- and it is here so that when clause 2 fails, the verdict stays ABOUT THE CRATE instead of also
-- reporting a bogus front collapse for men who legitimately walked to one. That is not hypothetical:
-- this scenario is currently red for exactly that reason (a crate dropped on a quiet front, PIPELINE
-- item 51 / item 56), and a new clause that piles a second wrong diagnosis on top of a real signal
-- would make the instrument worse rather than better.
--
-- Latched on crateEver, not on a live count: SUPPLYCACHE self-removes when drained
-- (RemoveBelowSupply: 1, misc.yaml:437), so a crate that was walked to and then consumed must still
-- license the walk it caused. An allowance read from a live count would relax while the crate existed
-- and snap back to 1 at verdict time, failing the platoon for a trip the run itself authorised.
local MAX_DRIFT = 6

WorldLoaded = function()
	local platoon = { Rifle1, Rifle2, Rifle3, Rifle4, Rifle5 }

	TestHarness.FocusBetween(Truck, Rifle3)
	TestHarness.Select(Truck)

	-- Drain each rifleman's primary to exactly DRAINED. The RPG (secondary-ammo) is left full
	-- on purpose, so the all-pools-empty legacy rearm path stays out of the way.
	for _, r in ipairs(platoon) do
		if not r.IsDead then
			local have = r.AmmoCount("primary-ammo")
			if have > DRAINED then r.Reload("primary-ammo", -(have - DRAINED)) end
		end
	end

	-- Peak-over-the-run drift, measured by the same code as test-supply-under-danger
	-- (TestHarness.DriftTracker, mods/ww3mod/scripts/test-helpers.lua). The MEASUREMENT is shared
	-- deliberately; the ALLOWANCE is not, because how far a man may stray is doctrine and the two
	-- scenarios sit on opposite sides of it.
	local driftTracker = TestHarness.DriftTracker(platoon)

	local truckStartX = Truck.Location.X
	local truckMaxX = truckStartX
	local truckLastCell = Truck.Location
	local truckGoneAtSecond = 0
	local bestBack = 0
	local cratesNow = 0
	local crateEver = 0
	local crateFirstCell = nil
	local crateFirstSecond = 0

	-- A verdict has already been written. Test.Pass/Skip can defer their exit, so later triggers
	-- still fire and would otherwise write a second, contradicting verdict over a Skip.
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

	-- Every supplycache in the world, owner-agnostic on purpose: SUPPLYCACHE is
	-- ProximityCapturable, so a crate that changed hands is still a crate that was dropped.
	local function countCaches()
		local n = 0
		local first = nil
		local all = Map.ActorsInWorld
		for i = 1, #all do
			local a = all[i]
			if not a.IsDead and a.Type == CACHE_TYPE then
				n = n + 1
				if first == nil then first = a.Location end
			end
		end
		return n, first
	end

	-- Setup precondition. If the drain did not take, the platoon was never starving and the run
	-- proves nothing about the doctrine — skip rather than report a verdict.
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
			if not Truck.IsDead then
				truckLastCell = Truck.Location
				if truckLastCell.X > truckMaxX then truckMaxX = truckLastCell.X end
			elseif truckGoneAtSecond == 0 then
				truckGoneAtSecond = s
			end

			-- Once per second, which is the same cadence the other three clauses are latched at. A
			-- walk out to a truck five cells away takes several seconds at infantry speed, so a
			-- 1 Hz sample cannot miss one; a sub-second excursion is not a front collapse.
			driftTracker.Sample()

			local back = resupplied()
			if back > bestBack then bestBack = back end

			local n, first = countCaches()
			cratesNow = n
			if n > crateEver then crateEver = n end
			if first ~= nil and crateFirstCell == nil then
				crateFirstCell = first
				crateFirstSecond = s
			end
		end)
	end

	Trigger.AfterDelay(sec(WINDOW), function()
		if verdict then return end

		-- Inconclusive shapes first, so a real doctrine failure is never confused with a setup
		-- that fell apart. NOTE the truck's absence is deliberately NOT a skip here: no enemy
		-- exists on this map, so the truck cannot be shot, and under RemoveBelowSupply: 1 its
		-- removal means it emptied — which is clause 3 failing, not an inconclusive run.
		if alive() < NEED_BACK then
			verdict = true
			Test.Skip(string.format(
				"only %d of 5 riflemen survived — cannot judge resupply, inconclusive", alive()))
			return
		end

		-- Relaxed only by a crate having EXISTED, which is clause 2's own failure — see MAX_DRIFT.
		local driftAllowance = crateEver > 0 and MAX_DRIFT or HOLD_DRIFT
		local peakDrift = driftTracker.Peak()

		local truckHere = not Truck.IsDead
		local clause1 = bestBack >= NEED_BACK
		local clause2 = crateEver == 0
		local clause3 = truckHere
		local clause4 = peakDrift <= driftAllowance

		if clause1 and clause2 and clause3 and clause4 then
			-- The green verdict carries its numbers on purpose. A drift clause that passes silently
			-- cannot be told apart from a drift clause that never measured anything, and this
			-- scenario's own history is a test that "passed for the right reason but did not assert
			-- it". The peak belongs in the record so the next reader can see it was 0 or 1, not 6.
			Test.Pass(string.format(
				"%d of 5 resupplied from the truck aura with no crate dropped, truck still loaded at %s, "
				.. "platoon held the front (peak drift %d, allowed %d). platoon spawnX->nowX(peak drift): %s",
				bestBack, cellText(truckLastCell), peakDrift, driftAllowance, driftTracker.Trace()))
			return
		end

		local broke = {}
		if not clause1 then
			broke[#broke + 1] = string.format(
				"(1) ammo never came up: %d of 5 climbed clear of starving, need %d", bestBack, NEED_BACK)
		end
		if not clause2 then
			broke[#broke + 1] = string.format(
				"(2) a supplycache was dropped on a front with no believed enemy: first seen at %s after %ds, "
				.. "%d on the map now", cellText(crateFirstCell), crateFirstSecond, cratesNow)
		end
		if not clause3 then
			broke[#broke + 1] = string.format(
				"(3) the truck emptied itself (gone after %ds, last seen at %s) instead of keeping its "
				.. "remainder for the next platoon", truckGoneAtSecond, cellText(truckLastCell))
		end
		if not clause4 then
			broke[#broke + 1] = string.format(
				"(4) the platoon left the front to be resupplied — peak drift %d cells, allowed %d. "
				.. "Either it walked back to meet the truck, or the truck never closed to aura range "
				.. "(5c) and the men covered the rest; compare the peak against the truck's furthest x "
				.. "below to tell which", peakDrift, driftAllowance)
		end

		Test.Fail(string.format(
			"safe-front supply doctrine broken: %s. "
			.. "platoon spawnX->nowX(peak drift): %s. "
			.. "primary ammo now %s (drained to %d, starving at <=%d). "
			.. "truck went from x=%d to a furthest x=%d and is %s; platoon is at x=44, no enemy actor "
			.. "exists and believed danger is 0 everywhere. Read it as: furthest x still near %d = the "
			.. "truck never committed; furthest x in the low 40s with no crate and the truck still here = "
			.. "it served from its aura, which is the doctrine; a crate anywhere = it unloaded when it "
			.. "did not have to.",
			table.concat(broke, "; "), driftTracker.Trace(), ammoTrace(), DRAINED, STARVING,
			truckStartX, truckMaxX, truckHere and "still on the map" or "gone",
			truckStartX))
	end)
end
