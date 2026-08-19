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
--   PASS = a crate was placed, AND at least 2 of the 5 climbed clear of starving, AND no rifleman
--          ever drifted further than the allowance -- which is 6 cells once a crate is on the
--          ground to walk to, and 1 cell while there is not.
--   FAIL = no crate placed (the safe mode executed in a dangerous place), or fed but displaced
--          (the front collapsed backwards), or never fed at all.
--   SKIP = the setup did not hold -- the drain failed, the platoon died, or the truck was
--          DESTROYED. A truck that merely vanished at full health finished its errand and drove
--          home to be sold; that is a success ending, not an inconclusive one.

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

-- Cells a rifleman may stray when there is NO crate to walk to. The 6-cell allowance above exists
-- for one specific correct behaviour -- walking to a crate dropped short -- and unconditional it
-- also licensed the behaviour it was meant to catch: at 9861bcf4 all five men walked out to meet
-- the TRUCK (`[seek] leave ... provider=truk@19,16 dist=19c leash=20c`) and the run passed at drift
-- 5 with `crate=NONE placed`. A walk toward a crate is the doctrine; a walk toward a truck is the
-- front collapsing. With no crate on the ground there is nothing legitimate to walk to, so the
-- platoon must hold: one cell of slack for pathing and crowding, no more.
local HOLD_DRIFT = 1

-- THIS SCENARIO IS THE DANGEROUS MODE, so drop-and-leave is not optional here. The doctrine splits
-- on danger: safe means serve from the aura and keep the cargo, dangerous means drive in, unload and
-- leave. Feeding men from the aura while parked at the front is the SAFE behaviour, and under danger
-- it is the thing the mode exists to prevent -- so however well fed anyone is, a run with no crate
-- has not demonstrated the doctrine. The sibling scenario test-supply-safe-front-keeps-cargo asserts
-- the opposite and must keep passing with no crate.
local REQUIRE_CRATE = true
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

	-- Peak-over-the-run drift, shared with test-supply-safe-front-keeps-cargo (test-helpers.lua).
	-- The MEASUREMENT is shared; the ALLOWANCE is not — see driftAllowance below, which is this
	-- scenario's own doctrine.
	local driftTracker = TestHarness.DriftTracker(platoon)

	local truckStartX = Truck.Location.X
	local truckMaxX = Truck.Location.X
	local bestBack = 0
	local done = false

	-- KILLED AND REMOVED ARE THE SAME BOOLEAN, so the test has to separate them itself.
	-- Actor.IsDead is `Disposed || health.IsDead` (Actor.cs:76), and DropsSupplyCache evacuates a
	-- spent truck to the map edge and SELLS it — which disposes the actor. So a truck that finished
	-- its job and drove home reads exactly like a truck that was destroyed, and a run at d1134422 was
	-- reported as "the truck died" on a map where the only enemies are held at HoldFire and provably
	-- cannot fire (HoldFire gates the auto-target scan at AutoTarget.cs:969/:997 and retaliation at
	-- :554-557). Tracking health lets the verdict say which happened: damage taken before death means
	-- destroyed, full health at the last sample means removed.
	local truckLastHealth = Truck.Health
	local truckMinHealth = Truck.Health
	local truckFullHealth = Truck.MaxHealth
	local truckLastX = Truck.Location.X

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

	-- THE CRATE IS THE DOCTRINE'S CENTRAL ACT, so the verdict states it rather than leaving it to be
	-- inferred from the truck's supply hitting zero. Neither the verdict nor the log named it before,
	-- and a grep for "supplycache" in debug.log returned nothing because the log never prints the
	-- word — which was read as "no crate was ever placed" when in fact one had been.
	local function crateList()
		local bot = Player.GetPlayer("USA-bot")
		if bot == nil then return {} end
		return bot.GetActorsByType("supplycache")
	end

	-- The allowance is CONDITIONAL ON A CRATE EXISTING. With one on the ground a short walk to it is
	-- the doctrine; with none, any walk is a walk toward a truck, which is the collapse.
	local function driftAllowance()
		if #crateList() > 0 then return MAX_DRIFT end
		return HOLD_DRIFT
	end

	local function crateReport()
		local bot = Player.GetPlayer("USA-bot")
		if bot == nil then return "crate=<no USA-bot player>" end

		local crates = bot.GetActorsByType("supplycache")
		if #crates == 0 then return "crate=NONE placed" end

		local parts = {}
		for i, c in ipairs(crates) do
			-- Standoff actually achieved, measured against the nearest living rifleman: the doctrine
			-- asks for the crate a few cells SHORT of the platoon, so the distance is the thing to
			-- report, not merely that a crate exists somewhere.
			local best = -1
			for _, r in ipairs(platoon) do
				if not r.IsDead then
					local dx = c.Location.X - r.Location.X
					local dy = c.Location.Y - r.Location.Y
					local d = math.max(math.abs(dx), math.abs(dy))
					if best < 0 or d < best then best = d end
				end
			end

			parts[i] = string.format("%d,%d (standoff %dc from nearest rifleman)", c.Location.X, c.Location.Y, best)
		end

		return "crate=" .. table.concat(parts, " + ")
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
				truckLastX = Truck.Location.X
				if truckLastX > truckMaxX then truckMaxX = truckLastX end

				truckLastHealth = Truck.Health
				if truckLastHealth < truckMinHealth then truckMinHealth = truckLastHealth end
			end

			driftTracker.Sample()

			local back = resupplied()
			if back > bestBack then bestBack = back end

			-- BOTH halves, sampled together. Drift is cumulative, so a platoon that once left
			-- its position can never satisfy this again — which is the intent: the abandonment
			-- already happened, and walking home does not undo it.
			local allowance = driftAllowance()
			local crateOk = not REQUIRE_CRATE or #crateList() > 0
			if back >= NEED_BACK and driftTracker.Peak() <= allowance and crateOk then
				done = true
				Test.Pass(string.format(
					"%d of 5 resupplied holding position (peak drift %d, allowed %d). %s",
					back, driftTracker.Peak(), allowance, crateReport()))
			end
		end)
	end

	Trigger.AfterDelay(sec(WINDOW), function()
		if done then return end

		-- ONLY A DESTROYED TRUCK IS INCONCLUSIVE. A truck that is simply GONE has almost certainly
		-- finished: the all-or-nothing drop empties it, it leaves the roster, and DropsSupplyCache
		-- drives it to the map edge and sells it — which disposes the actor and reads as IsDead
		-- (Actor.cs:76). Treating that as a setup failure skipped a run in which the doctrine had
		-- executed end to end, so the successful ending no longer aborts the verdict; the run is
		-- judged on ammo and drift like any other, with the truck's fate reported alongside.
		if Truck.IsDead and truckMinHealth < truckFullHealth then
			Test.Skip(string.format(
				"the supply truck was DESTROYED before the window closed (health fell to %d/%d) — "
				.. "inconclusive. last seen at x=%d, furthest x=%d. %s. platoon spawnX->nowX(peak drift): %s",
				truckMinHealth, truckFullHealth, truckLastX, truckMaxX, crateReport(), driftTracker.Trace()))
			return
		end

		local truckFate = Truck.IsDead
			and string.format("truck left at full health (%d/%d) after its errand, last seen x=%d",
				truckMinHealth, truckFullHealth, truckLastX)
			or string.format("truck still alive at x=%d", truckLastX)

		if alive() < NEED_BACK then
			Test.Skip(string.format(
				"only %d of 5 riflemen survived — cannot judge resupply, inconclusive", alive()))
			return
		end

		local drift = driftTracker.Peak()
		local allowance = driftAllowance()
		local held = drift <= allowance
		local fed = bestBack >= NEED_BACK
		local crateCount = #crateList()

		-- THE DANGEROUS-MODE CLAUSE, checked before anything else can pass the run. Under danger the
		-- doctrine's central act is the drop; a truck that fed the platoon from its aura and never
		-- unloaded has executed the SAFE mode in a dangerous place, and the ammo bar cannot tell the
		-- difference because the men end up fed either way.
		if REQUIRE_CRATE and crateCount == 0 then
			Test.Fail(string.format(
				"NO CRATE PLACED — under danger the doctrine is drive in, drop, drive out, and nothing "
				.. "was ever unloaded (%d of 5 fed from the truck aura instead, peak drift %d). "
				.. "primary ammo now %s. platoon spawnX->nowX(peak drift): %s. "
				.. "truck went from x=%d to a furthest x=%d, %s",
				bestBack, drift, ammoTrace(), driftTracker.Trace(), truckStartX, truckMaxX, truckFate))
			return
		end

		if fed and held then
			Test.Pass(string.format(
				"%d of 5 resupplied holding position (peak drift %d, allowed %d). %s. %s",
				bestBack, drift, allowance, crateReport(), truckFate))
			return
		end

		local why
		if fed then
			why = string.format(
				"THE FRONT COLLAPSED BACKWARDS — %d of 5 were fed, but the platoon left its position "
				.. "(peak drift %d cells, allowed %d with %d crate(s) down). Supply did not reach the "
				.. "front; the front went to the supply.",
				bestBack, drift, allowance, crateCount)
		elseif held then
			why = string.format(
				"supply never reached the front — %d of 5 climbed clear of starving (need %d), platoon held position.",
				bestBack, NEED_BACK)
		else
			why = string.format(
				"worst case — the platoon left its position (peak drift %d cells, allowed %d with %d "
				.. "crate(s) down) and STILL was not resupplied (%d of 5, need %d).",
				drift, allowance, crateCount, bestBack, NEED_BACK)
		end

		Test.Fail(string.format(
			"%s primary ammo now %s (drained to %d, starving at <=%d). "
			.. "platoon spawnX->nowX(peak drift): %s. %s. "
			.. "truck went from x=%d to a furthest x=%d (aura 5c), %s; danger wall starts at x=16",
			why, ammoTrace(), DRAINED, STARVING, driftTracker.Trace(), crateReport(),
			truckStartX, truckMaxX, truckFate))
	end)
end
