-- The @experimental bot must BUY an LCCV and TRANSFORM it into a FORWARD Logistics Center.
--
-- LOGISTICSCENTER is `Prerequisites: ~disabled` and nothing in the mod grants `disabled`, so
-- transforming an LCCV is the only route to one that any player has. Before
-- LogisticsCenterBotModule the string `lccv` appeared nowhere in rules/ai/ except a TODO, so the
-- bot fielded supply trucks (750 supply) and never the Centre (2250 — 3x on both axes).
--
-- THE PASS CONDITION IS DELIBERATELY TWO-PART, and the second part is the point. "A logisticscenter
-- exists" would be satisfied by one deployed on top of the Supply Route, which is 3000 credits
-- spent to shorten a resupply round-trip that was already zero. The Centre must also stand CLEAR of
-- the SR, which is what makes this a test of the siting descent and not merely of the purchase.
--
-- The threshold is 8 cells against an expected ~18 (see map.yaml §GEOMETRY), so a substantially
-- under-performing descent still passes and only an effectively-at-the-SR deploy fails. That gap is
-- intentional: this asserts the DIRECTION of the siting rule, and leaves its exact standoff — which
-- moves with StandoffCells and the believed frontier — free to be tuned without editing the test.

-- Budgeted in TICKS and converted, per test-helpers.lua: TestHarness.TicksPerSecond is 25 while the
-- engine runs 16 ticks/s, so a number written as "seconds" is neither.
--
-- 2400 ticks has to cover the whole chain end to end, and every link is slow: the module's first
-- evaluation at ScanInterval (100), the priority request draining at UnitBuilder's FeedbackTime
-- (30), production of a 3000-cost vehicle, the walk-in from the map edge that IS how reinforcements
-- arrive in WW3MOD, up to another ScanInterval before it is sited, the ~18-cell drive, up to another
-- ScanInterval before the deploy order, then the transform itself.
local WindowTicks = 2400

-- Cells from the Supply Route beyond which the Centre counts as FORWARD rather than at the beachhead.
local MinForwardCells = 8

local sawMcv = false
local sawCenter = false

local function cellDistance(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	-- Chebyshev: the same "cells away" a player would count, and it needs no square root.
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	if dx > dy then return dx end
	return dy
end

WorldLoaded = function()
	local usa = Player.GetPlayer("USA-bot")
	TestHarness.FocusBetween(OwnSR, OpponentSR)

	local srCell = OwnSR.Location

	TestHarness.AssertWithin(WindowTicks / TestHarness.TicksPerSecond, function()
		-- Record the BUY half independently of the deploy half. Without this the timeout message
		-- cannot separate "never bought one" (a demand-gate or production-queue failure) from
		-- "bought one and never got it sited" (a descent or placement failure) — two different
		-- defects that look identical from the outside, in a test too slow to rerun casually.
		if #usa.GetActorsByType("lccv") > 0 then
			sawMcv = true
		end

		local centers = usa.GetActorsByType("logisticscenter")
		if #centers == 0 then
			return nil
		end

		sawCenter = true

		for _, c in ipairs(centers) do
			local d = cellDistance(c.Location, srCell)
			if d >= MinForwardCells then
				Media.Debug("[lccv-test] PASS centre at (" .. c.Location.X .. "," .. c.Location.Y
					.. ") is " .. d .. " cells from the SR")
				return true
			end

			-- A building does not move, so a Centre this close is a settled failure rather than a
			-- not-yet: fail immediately instead of burning the rest of the window.
			return "deployed a Logistics Center at (" .. c.Location.X .. "," .. c.Location.Y
				.. "), only " .. d .. " cells from the Supply Route at ("
				.. srCell.X .. "," .. srCell.Y .. ") — needed >= " .. MinForwardCells
				.. ". The siting descent did not move it forward; check for `[logistics] "
				.. "move-to-site` in debug.log, and whether the ControlField gradient was flat."
		end

		return nil
	end, function()
		if sawCenter then
			return "a Logistics Center existed but never cleared " .. MinForwardCells .. " cells from the SR."
		end

		if sawMcv then
			return "the bot BOUGHT an LCCV but never deployed it as a Logistics Center within "
				.. WindowTicks .. " ticks. The buy half works; the deploy half did not complete. "
				.. "Read debug.log for `[logistics] move-to-site` (sited and driving) vs "
				.. "`[logistics] deploy-refused` (site was not placeable) — if NEITHER appears, "
				.. "ChooseSite returned null every scan, which is the stalled-descent hold."
		end

		return "the bot never fielded an LCCV at all within " .. WindowTicks .. " ticks — the demand "
			.. "gate never fired or the request never built. Read debug.log for `[logistics] request=lccv`: "
			.. "if it is absent the gate did not fire (check DesiredCenters, MinCashToRequest, and that "
			.. "LogisticsCenterBotModule@experimental is enabled); if it is present the Vehicle queue "
			.. "never delivered."
	end)
end
