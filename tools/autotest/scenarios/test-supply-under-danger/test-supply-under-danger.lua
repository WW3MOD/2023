-- AUTO TEST — supplies must reach a starving platoon that sits inside believed danger.
--
-- THE BAR IS THE DOCTRINE, NOT THE MECHANISM. The assertion is only that the platoon's
-- ammo actually comes back up. It deliberately does NOT check that a drop order was
-- issued, that a crate was spawned, or that the truck entered any particular branch, so
-- it stays true if the delivery route is later changed — aura service in place, a crate
-- dropped short that the soldiers walk to, or anything else that gets ammo forward.
--
-- WHY A CLIMB PROVES RESUPPLY. ^E3's ReloadAmmoPool@1 is gated on `replenish-soldiers`
-- (rules/ingame/infantry.yaml:1196-1198), a condition only a SupplyProvider grants to
-- units in its aura. So primary ammo cannot regenerate on its own: any rise at all means
-- a truck or a dropped cache actually reached them. And nothing else on the map can feed
-- them: ^E3 can only dock at `truk, logisticscenter` and there is no logistics centre,
-- SUPPLYROUTE has no supply aura, and the platoon sits outside the 20-cell seek leash of
-- anywhere the truck can loiter. Nothing but a real delivery can move this number.
--
--   PASS = at least 2 of the 5 riflemen climb clear of the starving threshold.
--   FAIL = the window closes with the platoon still starving (supply never arrived).
--   SKIP = the setup did not hold (drain failed, platoon or truck died) — inconclusive
--          rather than a false verdict about the doctrine.

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

-- 10% — starving, but NOT empty: an all-pools-empty unit would be moved by the legacy
-- AmmoPool.AutoRearmIfAllEmpty path, and the test would pass without any delivery.
local DRAINED = 10
local STARVING = 25       -- 250 per mille of ^E3's 100-round pool — what the supply layer calls starving
local NEED_BACK = 2       -- how many of the 5 must climb clear for the platoon to count as resupplied
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

	-- Drain each rifleman's primary to exactly DRAINED. The RPG (secondary-ammo) is left
	-- full on purpose, so the all-pools-empty legacy rearm path stays out of the way.
	for _, r in ipairs(platoon) do
		if not r.IsDead then
			local have = r.AmmoCount("primary-ammo")
			if have > DRAINED then r.Reload("primary-ammo", -(have - DRAINED)) end
		end
	end

	-- Diagnostics, so a failure says WHY rather than just "timed out". How far east the
	-- truck ever got is the single most informative number here: a truck that turned back
	-- short of the platoon refused to close, which is a different failure from one that
	-- arrived and still delivered nothing.
	local truckMaxX = Truck.Location.X
	local truckStartX = Truck.Location.X
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

			if not Truck.IsDead then
				local x = Truck.Location.X
				if x > truckMaxX then truckMaxX = x end
			end

			local back = resupplied()
			if back > bestBack then bestBack = back end

			-- Pass as soon as the bar is met; Test.Pass can defer its exit, so latch to keep
			-- the next second's tick from calling it again.
			if back >= NEED_BACK then
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

		if bestBack >= NEED_BACK then
			Test.Pass()
			return
		end

		Test.Fail(string.format(
			"supply never reached the front: %d of 5 riflemen climbed clear of starving (need %d). "
			.. "primary ammo now %s (drained to %d, starving at <=%d). "
			.. "truck went from x=%d to a furthest x=%d; platoon is at x=38, danger wall starts at x=16",
			bestBack, NEED_BACK, ammoTrace(), DRAINED, STARVING, truckStartX, truckMaxX))
	end)
end
