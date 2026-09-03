-- WITH A FREE CENTRE IN REACH, THE BOT MUST NOT BUY ONE.
--
-- USER REPORT 2026-09-03: "On this particular map there are already a neutral LC on each side, so
-- the bots should instead capture the exisiting one."
--
-- WHAT IS ASSERTED, AND WHAT IS DELIBERATELY NOT.
--
-- ASSERTED (the pass condition): the bot never fields an `lccv`. That is the PURCHASE — the LCCV is
-- the only thing any player can buy on this path, since LOGISTICSCENTER is `Prerequisites: ~disabled`
-- and transforming an LCCV is the only route to the building. So "never owned an lccv" is exactly
-- "never bought a Centre", and it stays true whether or not the capture succeeds.
--
-- NOT ASSERTED: that the capture actually completes. Capturing is CaptureCoordinatorBotModule's job
-- — it must field a technician, route it across ~14 cells and survive the trip — and none of that is
-- this change. Folding it into the pass condition would make this scenario fail for reasons in a
-- module it is not testing, which is how a suite acquires tests nobody trusts. The capture IS
-- watched and reported (see below), because it is the behaviour the user actually wants and a run
-- where it never happens is worth knowing about; it is reported as a NOTE, not a verdict.
--
-- THE LIVENESS PROBLEM, which this scenario has in a sharper form than its sibling. A negative
-- assertion passes for free if the bot is inert, and here it would also pass for free if the DEMAND
-- never materialised — a bot that declines to buy because nobody is forward and nobody is short of
-- ammo has told us nothing about the capture veto. So the pass requires evidence that the gate was
-- genuinely tempted:
--   1. production must be live (the bot fields something it did not start with), and
--   2. `[logistics] refuse-buy` must appear in debug.log with capturable >= 1.
-- (2) cannot be read from Lua, so it is stated here and in description.txt as the thing to check on
-- the run rather than asserted. If it is absent, the pass is not evidence and should not be counted
-- — that is the honest limit of what this scenario proves on its own.
local WindowTicks = 3600

local elapsed = 0
local startingUnits = 0
local sawProduction = false
local sawCapture = false

WorldLoaded = function()
	local usa = Player.GetPlayer("USA-bot")
	TestHarness.FocusBetween(OwnSR, OpponentSR)

	local srCell = OwnSR.Location
	startingUnits = #usa.GetActors()

	local function cellDistance(a, b)
		local dx = a.X - b.X
		local dy = a.Y - b.Y
		if dx < 0 then dx = -dx end
		if dy < 0 then dy = -dy end
		if dx > dy then return dx end
		return dy
	end

	TestHarness.AssertWithin(WindowTicks / TestHarness.TicksPerSecond, function()
		elapsed = elapsed + 1

		-- THE FAIL. A settled one: a bought LCCV does not un-buy itself.
		local lccvs = usa.GetActorsByType("lccv")
		if #lccvs > 0 then
			return "the bot BOUGHT a Logistics Center (lccv appeared at tick " .. elapsed
				.. ") while a neutral one stood 14 cells from its Supply Route. The capture veto did"
				.. " not fire. Read debug.log for `[logistics] refuse-buy`: if the line is present but"
				.. " `capturable=0`, PoiMap did not return the neutral Centre as a capture target —"
				.. " check that `logisticscenter` still carries an IncomeWeights entry"
				.. " (SupplyDepotIncomeWeight) and a CaptureManager via ^NeutralOrOccupiedCapturable."
				.. " If `capturable=1` and it bought anyway, CaptureCoversDemand is not being"
				.. " consulted or DesiredCenters exceeds 1."
		end

		if not sawProduction and #usa.GetActors() > startingUnits then
			sawProduction = true
		end

		-- THE NOTE, not the verdict. A Centre owned by the bot without an lccv ever existing can only
		-- have been captured, which is precisely the behaviour the user asked for.
		if not sawCapture then
			local owned = usa.GetActorsByType("logisticscenter")
			if #owned > 0 then
				sawCapture = true
				local d = cellDistance(owned[1].Location, srCell)
				Media.Debug("[lc-capture-test] NOTE the bot now OWNS a logisticscenter at ("
					.. owned[1].Location.X .. "," .. owned[1].Location.Y .. "), " .. d
					.. " cells from its SR, with no lccv ever bought — it was CAPTURED. This is the"
					.. " behaviour the ruling asked for. If d > CaptureConsiderCells (40) the distance"
					.. " bound is not doing what this map assumes.")
			end
		end

		if elapsed >= WindowTicks then
			if not sawProduction then
				return "INCONCLUSIVE, reported as a failure on purpose: the bot produced NOTHING in "
					.. WindowTicks .. " ticks, so its refusal to buy a Centre is not evidence that the"
					.. " capture veto works. Check the experimental profile is actually running."
			end

			if sawCapture then
				Media.Debug("[lc-capture-test] PASS no lccv was ever bought, and the neutral Centre"
					.. " was captured.")
			else
				Media.Debug("[lc-capture-test] PASS no lccv was ever bought. NOTE: the neutral Centre"
					.. " was NOT captured within the window — the veto held but CaptureCoordinator did"
					.. " not complete. Not a failure of this change; worth raising separately.")
			end

			Media.Debug("[lc-capture-test] CONFIRM ON THE RUN: debug.log must contain `[logistics]"
				.. " refuse-buy` with `capturable=1` and a non-zero `fwd-value`. Without that line the"
				.. " gate was never tempted and this PASS is not evidence.")
			return true
		end

		return nil
	end, function()
		return "AssertWithin timed out before the scenario's own " .. WindowTicks
			.. "-tick window elapsed (reached " .. elapsed .. "). Harness clock mismatch, not a"
			.. " bot-behaviour result."
	end)
end
