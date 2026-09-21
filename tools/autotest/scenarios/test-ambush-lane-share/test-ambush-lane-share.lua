-- PIPELINE item 86 — the ambush lane's share of a small army.
--
-- THE ASSERTION, in one line: units held under an `ambush:` commitment must stay at ZERO
-- while offense's own free pool is at or under its advance floor.
--
-- WHY IT IS NOT A POSITIONAL OR SURVIVAL TEST, which is the mistake that cost three runs.
-- "The lane did not recruit" is invisible from the outside: no bot module logs which ACTOR
-- it sent where (LayeredDefenceBotModule.cs:545 says so outright), and an un-recruited unit
-- standing at the Supply Route is indistinguishable by position from an idle one, from a
-- garrisoned one, and from one a held axis is sitting on. test-combined-arms-rendezvous
-- tried to read this through a tank's survival and measured the OFFENSIVE AXIS instead —
-- twice, in both arms, with identical verdicts (260922_012732 / 260922_012930).
--
-- So the verdict reads the LEDGER, through three test-mode bindings that return the same
-- numbers AmbushLaneMath.ReserveAllowance itself decides on:
--   Test.GetBotLedgerHeld(p, "ambush")      -- units this module currently holds
--   Test.GetBotOffenseFreePool(p)           -- offense's published free pool (= [exp-ledger] free=)
--   Test.GetBotOffenseAdvanceFloor(p)       -- offense's FreePoolMinAdvanceUnits as IT applies it
-- Verdict and mechanism therefore cannot drift apart: if the reserve's arithmetic changes,
-- this test changes with it rather than silently measuring something else.

local DeadlineSeconds = 200      -- generous; the window below ends the run, not this
local ObserveUntilTick = 500     -- five lane evals (ReevaluateInterval 100, first at t100)
local ExpectedFloor = 40         -- rules.yaml override; SKIP unless it really merged
local MinLanePool = 2            -- MinUnitsPerAmbush on both profiles

local Bot
local MaxAmbushHeld = 0
local AmbushHeldAtTick = -1      -- first tick a violation was seen
local MaxFree, MinFree = -1, -1
local SawGovernedState = false   -- offense had a lane-sized pool AND no surplus
local FloorSeen = -1
local Trace = {}

local function Note(tick, free, floor, held)
	Trace[#Trace + 1] = string.format("t%d:free=%d/min=%d,ambush=%d", tick, free, floor, held)
end

local function TraceText()
	return table.concat(Trace, " ")
end

WorldLoaded = function()
	Bot = Player.GetPlayer("USA-bot")
	if Bot == nil then
		Test.Skip("scenario bug: no player named USA-bot")
		return
	end

	TestHarness.AssertWithin(DeadlineSeconds, function()
		local tick = DateTime.GameTime

		local floor = Test.GetBotOffenseAdvanceFloor(Bot)
		local free = Test.GetBotOffenseFreePool(Bot)
		local held = Test.GetBotLedgerHeld(Bot, "ambush")

		-- -1 is information, not zero: no enabled offensive module, or it has not evaluated
		-- yet. Keep waiting rather than reading a floor of -1 as "no floor".
		if floor < 0 or free < 0 then
			if tick >= ObserveUntilTick then
				Test.Skip("offense never published a free pool (floor=" .. floor .. " free=" .. free ..
					") — the reserve had nothing to decide against and this run measured nothing")
				return false
			end
			return false
		end

		FloorSeen = floor

		-- THE SCAFFOLD CHECKS ITSELF. rules.yaml lifts the axis floor so that no axis can
		-- form and offense stays under its advance floor all run. If that block ever stops
		-- merging -- a renamed trait, a changed @suffix, the MiniYaml case trap -- the floor
		-- reverts to the shipped 2, offense behaves normally, and a green here would mean
		-- nothing. An inert override must not be able to produce a PASS.
		if floor ~= ExpectedFloor then
			Test.Skip(string.format(
				"rules.yaml override did not merge: offense advance floor reads %d, expected %d. " ..
				"The constructed opening does not exist, so this run cannot judge the reserve. " ..
				"Check the PoiOffensiveBotModule@experimental block in rules.yaml.", floor, ExpectedFloor))
			return false
		end

		if MaxFree < 0 or free > MaxFree then MaxFree = free end
		if MinFree < 0 or free < MinFree then MinFree = free end
		if held > MaxAmbushHeld then MaxAmbushHeld = held end

		-- The state ruling (a) governs: offense holds a pool big enough for the lane to want
		-- (>= MinUnitsPerAmbush) and has NO surplus over its own floor. Unless this occurred,
		-- the reserve was never actually asked the question and a green proves nothing.
		if free >= MinLanePool and free <= floor then
			SawGovernedState = true
		end

		if tick % 50 == 0 then
			Note(tick, free, floor, held)
			print("[lane-share] t=" .. tick .. " offense-free=" .. free .. " min=" .. floor ..
				" ambush-held=" .. held)
		end

		-- THE VERDICT. Offense has no surplus, so the reserve's allowance is
		-- max(0, free - floor) = 0 and the lane may hold NOTHING. Any ambush commitment here
		-- is the item-86 defect, live.
		if free <= floor and held > 0 then
			AmbushHeldAtTick = tick
			Note(tick, free, floor, held)
			return string.format(
				"fail: the ambush lane recruited while offense had no surplus — %d unit(s) held under " ..
				"ambush: at t%d with offense free pool %d against its own advance floor %d " ..
				"(allowance max(0, %d-%d) = 0). This is PIPELINE item 86: the lane taking the army's " ..
				"share at the opening. trace: %s",
				held, tick, free, floor, free, floor, TraceText())
		end

		if tick < ObserveUntilTick then
			return false
		end

		-- WINDOW CLOSED WITHOUT A VIOLATION. Only a PASS if the reserve was really exercised.
		if not SawGovernedState then
			Test.Skip(string.format(
				"offense never held a lane-sized pool with no surplus (free ranged %d..%d against floor %d, " ..
				"needed >= %d and <= floor) — the reserve was never asked the question, so a green would be " ..
				"vacuous. Most likely both abrams died early or an axis formed anyway. trace: %s",
				MinFree, MaxFree, FloorSeen, MinLanePool, TraceText()))
			return false
		end

		return string.format(
			"pass: the ambush lane held NOTHING through t%d while offense sat under its own advance floor " ..
			"(free ranged %d..%d against floor %d; peak ambush-held %d). trace: %s",
			ObserveUntilTick, MinFree, MaxFree, FloorSeen, MaxAmbushHeld, TraceText())

	-- FUNCTION FORM, NOT A STRING. The third argument is evaluated EAGERLY at registration
	-- (test-helpers.lua), so a string here would be concatenated at tick 0 with every counter
	-- at its initial value and would report the same numbers on every timeout no matter what
	-- happened. That trap is documented at test-combined-arms-rendezvous.lua:193-206 and cost
	-- that scenario a run.
	end, function()
		return string.format(
			"the observation window never closed: last floor=%d, offense free ranged %d..%d, peak " ..
			"ambush-held=%d (first violation at t%d, -1 = none). A timeout here is a SCENARIO fault, not " ..
			"a verdict on the reserve — the window ends at t%d and the deadline is %ds. trace: %s",
			FloorSeen, MinFree, MaxFree, MaxAmbushHeld, AmbushHeldAtTick, ObserveUntilTick,
			DeadlineSeconds, TraceText())
	end)
end
