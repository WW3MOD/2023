-- AUTO TEST — one supply truck, TWO starving platoons on opposite bearings, and a need ordering
-- that keeps inverting. The truck must COMMIT to one of them and deliver, not drive back and forth.
--
-- ALWAYS RUN THIS AT `--seed -1848572889`, the seed the whole supply scenario family is pinned to.
-- Unseeded runs are NOT comparable across code changes.
--
-- ============================================================================================
-- WHAT COUNTS AS THE ANSWER
--
--   PASS = the truck reversed its x-travel AT MOST MAX_REVERSALS (1) times before a delivery
--          landed, AND a delivery landed inside the window.
--   FAIL = 2 or more reversals before the delivery, OR no delivery at all inside the window.
--   SKIP = the setup did not hold (the drain failed, a man died, the truck vanished early).
--
-- ONE reversal is allowed rather than zero on purpose. It is not slack for churn: it is the budget
-- for the single legitimate turn a truck may make when the FIRST scan after it starts rolling
-- genuinely re-ranks the two clusters. Two reversals cannot be explained that way — the truck has
-- gone back on a decision it had already gone back on once.
--
-- ASSERTED ON MOVEMENT, NOT ON THE MODULE'S INTERNAL TARGET, and that is deliberate. The user's
-- complaint is about what the truck visibly does ("going back and forth, not committing"), and a
-- test that read `[supply] truck ... target=` would keep passing if the churn moved to a different
-- variable. What is measured here is the thing a player watches.
-- ============================================================================================
--
-- HOW THE NEED IS MADE TO INVERT — no enemy, no combat, no RNG. AmmoNeed is a plain sum over pools
-- of (1 - current/max), so draining a rifleman raises his cluster's score by a known amount. The
-- pulse schedule below alternately drains the TRAILING platoon by 4 rounds a man, which is 200
-- points of NeedScore across five men (NeedScore = AmmoNeed * 1000), handing it a 100-point lead.
-- Every pulse therefore swaps which cluster is neediest, and does so at a tick that is NOT a
-- multiple of the module's 150-tick ScanInterval, so a pulse is never resolved in the same tick as
-- the scan that reads it.
--
-- 100 POINTS IS A DELIBERATELY TINY LEAD. It is a tenth of one rifleman's full load, i.e. the
-- ordinary consumption noise between two scans — precisely the regime in which a truck should NOT
-- change its mind. A cluster that is genuinely, substantially needier is a different question and
-- this scenario does not ask it: any stickiness margin worth shipping must still let a real
-- challenger win, which is why the fix's margin is expressed in NeedScore units and not as a latch.
--
-- WHY A CLIMB PROVES RESUPPLY. ^E3's ReloadAmmoPool@1 is gated on `replenish-soldiers`, a condition
-- only a SupplyProvider grants to units inside its aura. Ammo cannot regenerate on its own, ^E3 can
-- dock only at `truk, logisticscenter` and there is no logistics centre, SUPPLYROUTE has no supply
-- aura, and rules.yaml switches AutoSeekSupplies off so the men cannot walk anywhere. The truck
-- driving up and parking is the only thing in this world that can move the number.
--
-- WHAT THIS EXPECTS TO SCORE. Before per-truck cluster stickiness lands, RED: the follow path has no
-- memory of the cluster it is already serving, re-picks by live AmmoNeed every scan, and re-issues a
-- NON-QUEUED Move that cancels the drive — so the truck should oscillate about the midpoint,
-- reversing roughly once per pulse, and is not expected to reach either platoon while the contest
-- runs. After the fix, GREEN: it should hold whichever platoon won the first scan, drive the 30
-- cells, and serve it.

local SCAN = 150          -- SupplyFollowerBotModule ScanInterval (ai.yaml:1486), in ticks
local POLL = 25           -- sampling interval, ticks
local WINDOW = 3000       -- whole run budget, ticks

-- Pulse ticks. Spaced 200 apart so each pulse is picked up by exactly one scan, and offset off every
-- multiple of SCAN (150, 300, 450, ...) so no pulse and no scan share a tick.
local PULSES = { 260, 460, 660, 860, 1060, 1260 }
local PULSE_ROUNDS = 4    -- rounds drained per man per pulse; 4/100 x 5 men = 200 NeedScore

local START_EAST = 20     -- primary rounds each EAST rifleman starts on (of 100)
local START_WEST = 18     -- WEST starts 2 lower, so WEST leads by 100 NeedScore at the first scan
local STARVING = 25       -- 250 per mille of ^E3's 100-round pool — what the supply layer calls starving

local NEED_BACK = 2       -- men in ONE platoon that must climb clear of starving to count as delivered
local MAX_REVERSALS = 1   -- see the bar above
-- Cells the truck must travel AGAINST its current leg before that counts as a reversal rather than
-- path jitter. A churn leg is ~11 cells (150 ticks at Mobile Speed 75 = 75/1024 cells/tick), and
-- steering round the platoon column costs at most a cell or so, so 3 separates them with room.
local MIN_LEG = 3

WorldLoaded = function()
	local west = { West1, West2, West3, West4, West5 }
	local east = { East1, East2, East3, East4, East5 }

	TestHarness.FocusBetween(West3, East3)
	TestHarness.Select(Truck)

	-- Drain both platoons. The RPG (secondary-ammo, a single-round pool) is left FULL on purpose: an
	-- all-pools-empty unit would be moved by AmmoPool's legacy AutoRearmIfAllEmpty path, and the test
	-- would then be measuring that instead of the supply layer.
	local function drainTo(platoon, target)
		for _, r in ipairs(platoon) do
			if not r.IsDead then
				local have = r.AmmoCount("primary-ammo")
				if have > target then r.Reload("primary-ammo", -(have - target)) end
			end
		end
	end

	local function drainBy(platoon, rounds)
		for _, r in ipairs(platoon) do
			if not r.IsDead then r.Reload("primary-ammo", -rounds) end
		end
	end

	drainTo(east, START_EAST)
	drainTo(west, START_WEST)

	-- The alternating contest. Pulse 1 drains EAST (which starts behind), pulse 2 WEST, and so on, so
	-- the lead changes hands at every pulse and the LAST pulse leaves WEST in front for good — after
	-- which both the broken and the fixed code have a settled ordering to deliver against. A run that
	-- only ever failed because the contest never stopped would prove nothing.
	local pulseLog = {}
	for i, at in ipairs(PULSES) do
		local eastTurn = (i % 2 == 1)
		Trigger.AfterDelay(at, function()
			drainBy(eastTurn and east or west, PULSE_ROUNDS)
			pulseLog[#pulseLog + 1] = string.format("%d:%s", at, eastTurn and "E" or "W")
		end)
	end

	local verdict = false
	local truckSpawn = Truck.Location

	-- Reversal tracking, with a MIN_LEG deadband so that path jitter cannot manufacture a reversal.
	-- `dir` is the leg the truck is committed to (0 = has not travelled far enough to have one),
	-- `extremeX` the furthest x reached along it. A reversal is only booked once the truck has come
	-- MIN_LEG cells back off that extreme.
	local dir = 0
	local extremeX = truckSpawn.X
	local reversals = 0
	local minX, maxX = truckSpawn.X, truckSpawn.X
	local legs = { tostring(truckSpawn.X) }

	local function trackTravel(x)
		if x < minX then minX = x end
		if x > maxX then maxX = x end

		if dir == 0 then
			local d = x - extremeX
			if d >= MIN_LEG then
				dir, extremeX = 1, x
			elseif d <= -MIN_LEG then
				dir, extremeX = -1, x
			end
			return
		end

		local off = (x - extremeX) * dir
		if off > 0 then
			extremeX = x
		elseif -off >= MIN_LEG then
			reversals = reversals + 1
			if #legs < 14 then legs[#legs + 1] = tostring(extremeX) end
			dir = -dir
			extremeX = x
		end
	end

	local function fedCount(platoon)
		local n = 0
		for _, r in ipairs(platoon) do
			if not r.IsDead and r.AmmoCount("primary-ammo") > STARVING then n = n + 1 end
		end
		return n
	end

	local function ammoTrace(platoon)
		local parts = {}
		for i, r in ipairs(platoon) do
			parts[i] = r.IsDead and "dead" or tostring(r.AmmoCount("primary-ammo"))
		end
		return table.concat(parts, "/")
	end

	local function alive(platoon)
		local n = 0
		for _, r in ipairs(platoon) do
			if not r.IsDead then n = n + 1 end
		end
		return n
	end

	local function cellText(c)
		if c == nil then return "<none>" end
		return string.format("%d,%d", c.X, c.Y)
	end

	local truckLastCell = truckSpawn
	local truckGoneAtTick = 0

	local function travelTrace()
		return table.concat(legs, "->") .. "->" .. tostring(truckLastCell.X)
	end

	-- Setup precondition. If the drain did not take, nobody is starving, no cluster is selectable
	-- (SelectionMinStarvingUnits: 1) and the run proves nothing about commitment — skip, do not fail.
	Trigger.AfterDelay(POLL * 3, function()
		if verdict then return end

		for _, platoon in ipairs({ west, east }) do
			for _, r in ipairs(platoon) do
				if not r.IsDead and r.AmmoCount("primary-ammo") > STARVING then
					verdict = true
					Test.Skip("could not drain both platoons below the starving threshold — setup precondition failed")
					return
				end
			end
		end

		if Truck.IsDead then
			verdict = true
			Test.Skip("the truck was gone before the contest started — setup precondition failed")
		end
	end)

	for t = POLL, WINDOW, POLL do
		Trigger.AfterDelay(t, function()
			if verdict then return end

			if not Truck.IsDead then
				truckLastCell = Truck.Location
				trackTravel(truckLastCell.X)
			elseif truckGoneAtTick == 0 then
				truckGoneAtTick = t
			end

			local fedWest, fedEast = fedCount(west), fedCount(east)
			if fedWest < NEED_BACK and fedEast < NEED_BACK then
				return
			end

			-- A delivery has landed. The reversal count is frozen at this instant on purpose: once one
			-- platoon is fed the OTHER is legitimately far needier, and the truck turning toward it is
			-- correct behaviour, not churn. Counting past here would fail the fix for doing its job.
			verdict = true

			local served = fedWest >= NEED_BACK and "WEST" or "EAST"
			if reversals <= MAX_REVERSALS then
				Test.Pass()
				return
			end

			Test.Fail(string.format(
				"THE TRUCK DELIVERED, BUT ONLY AFTER GIVING UP ON A CLUSTER %d TIMES (allowed %d). It served "
				.. "the %s platoon at tick %d having reversed its x-travel at %s (spawn x=%d, range %d..%d). "
				.. "Each reversal is one scan re-picking the other cluster on a %d-point NeedScore lead and "
				.. "re-issuing a non-queued Move that cancelled the drive already in progress — the follow "
				.. "path keeps no memory of the cluster a truck is serving (SupplyFollowerBotModule.cs:1172 "
				.. "and :1103). need pulses %s. west ammo %s, east ammo %s.",
				reversals, MAX_REVERSALS, served, t, travelTrace(), truckSpawn.X, minX, maxX,
				PULSE_ROUNDS * 5 * 10, table.concat(pulseLog, " "), ammoTrace(west), ammoTrace(east)))
		end)
	end

	Trigger.AfterDelay(WINDOW, function()
		if verdict then return end

		-- The inconclusive shapes first, so a real commitment failure is never confused with a setup
		-- that fell apart. There is no enemy on this map, so neither of these should ever fire.
		if alive(west) < NEED_BACK or alive(east) < NEED_BACK then
			verdict = true
			Test.Skip(string.format(
				"only %d west / %d east riflemen survived — cannot judge commitment, inconclusive",
				alive(west), alive(east)))
			return
		end

		verdict = true

		local why
		if reversals > MAX_REVERSALS then
			why = string.format(
				"NEVER COMMITTED AND NEVER ARRIVED — the truck reversed %d times (allowed %d) and no platoon "
				.. "was resupplied inside %d ticks",
				reversals, MAX_REVERSALS, WINDOW)
		elseif maxX - minX < MIN_LEG then
			why = string.format(
				"THE TRUCK NEVER SET OFF — it stayed within %d cells of its spawn for the whole run and no "
				.. "platoon was resupplied. This is NOT the churn this test guards: look at cluster SELECTION "
				.. "first (SelectionMinStarvingUnits, the follow leash), not at commitment",
				maxX - minX)
		else
			why = string.format(
				"NO DELIVERY IN %d TICKS, and only %d reversal(s) — the truck committed but did not arrive or "
				.. "did not serve. Check whether it stopped short of its own 5-cell provider aura",
				WINDOW, reversals)
		end

		Test.Fail(string.format(
			"%s. truck spawned at %s carrying 80 supply, travelled %s, last seen %s%s; each platoon is 30 "
			.. "cells out and both stay inside MaxFollowDistance: 35 all run. need pulses %s (each %d points "
			.. "of NeedScore, handing the trailing platoon a 100-point lead). need >=%d men over %d rounds to "
			.. "count as fed: west %s, east %s. Module scan interval %d ticks; no enemy and zero believed "
			.. "danger anywhere on this map.",
			why, cellText(truckSpawn), travelTrace(), cellText(truckLastCell),
			truckGoneAtTick > 0 and string.format(", gone at tick %d", truckGoneAtTick) or "",
			table.concat(pulseLog, " "), PULSE_ROUNDS * 5 * 10, NEED_BACK, STARVING,
			ammoTrace(west), ammoTrace(east), SCAN))
	end)
end
