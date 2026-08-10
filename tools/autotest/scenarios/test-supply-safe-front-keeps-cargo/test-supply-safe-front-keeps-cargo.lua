-- AUTO TEST — on a front with NO believed enemy contact, the truck must drive up, serve the
-- starving platoon from its aura, and KEEP THE REST OF ITS LOAD.
--
-- This is the SAFE half of the settled supply doctrine, and the sibling of
-- test-supply-under-danger (same map, same geometry, enemy removed):
--
--   "If there is no danger the truck can go up to them and resupply them directly and not
--    unload, if more resupplying is needed elsewhere."
--
-- Three clauses, ALL of which must hold, evaluated together when the window closes:
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
--   PASS = all three clauses hold.
--   FAIL = any clause broke; the message says which, and reports enough to tell "the truck
--          never went" from "it went and served" from "it went and dumped a crate".
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

		local truckHere = not Truck.IsDead
		local clause1 = bestBack >= NEED_BACK
		local clause2 = crateEver == 0
		local clause3 = truckHere

		if clause1 and clause2 and clause3 then
			Test.Pass()
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

		Test.Fail(string.format(
			"safe-front supply doctrine broken: %s. "
			.. "primary ammo now %s (drained to %d, starving at <=%d). "
			.. "truck went from x=%d to a furthest x=%d and is %s; platoon is at x=44, no enemy actor "
			.. "exists and believed danger is 0 everywhere. Read it as: furthest x still near %d = the "
			.. "truck never committed; furthest x in the low 40s with no crate and the truck still here = "
			.. "it served from its aura, which is the doctrine; a crate anywhere = it unloaded when it "
			.. "did not have to.",
			table.concat(broke, "; "), ammoTrace(), DRAINED, STARVING,
			truckStartX, truckMaxX, truckHere and "still on the map" or "gone",
			truckStartX))
	end)
end
