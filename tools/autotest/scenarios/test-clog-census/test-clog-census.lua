-- MEASUREMENT, NOT A VERDICT.
--
-- The number wanted from this run is "how much of a bot's undispersed reserve is
-- still sitting on its own Supply Route", read from the engine's [exp-clog] lines
-- in debug.log. This script exists to make that number TRUSTWORTHY, and it asserts
-- nothing about its value.
--
-- What it gates, and why each gate is here:
--
--  (1) THE POPULATION EXISTS. `near2 = 0` means "no congestion" and also "no units",
--      and they are the same number. Every previous attempt to read this quantity
--      returned zero because the pool was empty, which is indistinguishable from a
--      clean result. So the run FAILS unless all 8 armed units and all 4 technicians
--      are alive and in world at the sample point — a number is only reported from a
--      world that was built.
--
--  (2) THE COUNTER DISCRIMINATES. At the first poll near2 must be STRICTLY between 0
--      and pool on both populations (6 of 12, and 2 of 4, by placement). A counter
--      that is saturated at pool or pinned at 0 from tick 1 cannot report movement in
--      either direction, so if that shape is wrong the placement is wrong and the run
--      is void before it starts.
--
--  (3) THE PRE-CONTACT WINDOW HELD. The state under measurement is "no believed
--      enemy ⇒ no forward gradient ⇒ no anchor ⇒ no orders". If anything moves a unit
--      the census stops describing that state, so the sample is compared against the
--      opening one and any drift is printed rather than smoothed over.
--
-- The engine-side [exp-clog] lines and the counts below are TWO INDEPENDENT
-- observations of the same quantity: the bot counts its own free pool / reserve from
-- inside the module, this counts named actors from outside. Agreement is the check
-- that the module saw the population this scenario placed. Disagreement is itself the
-- finding — "the units exist but the code path never saw them" and "the units
-- dispersed" produce very different pairs.
--
-- PITFALL, paid for on 2026-08-15: AssertWithin's third argument is an ordinary Lua
-- expression, concatenated ONCE at registration, so any counter interpolated into it
-- reports its initial value forever. Every live number below goes through print() to
-- lua.log; the timeout string is static.

local CensusSeconds = 30      -- the fixed sample point the reported number comes from
local DeadlineSeconds = 36    -- a little past it, so the screenshot has its own RenderTick
local PrintEverySeconds = 5

local SrX, SrY = 6, 16        -- OwnSR in map.yaml; RallyCell() is the SR actor's own cell

-- Placement invariants, restated so a silent map.yaml edit is visible.
--
-- MIND THE TWO DENOMINATORS. This script counts the armed line ALONE (8) and the
-- technicians ALONE (4). The engine's [exp-clog] line counts BuildFreePool, which is
-- neither: measured at tick 1 it reads pool=8, because the role filter at
-- PoiOffensiveBotModule.cs:2604 drops capturers by class, so technicians are not in the
-- offensive free pool at all. Conflating those two denominators is what consumed the
-- first granted run of this scenario — ArmedNear2AtStart was written as 6, which is the
-- near2 of a 12-unit pool that does not exist, while the world underneath was built
-- exactly as designed.
local ArmedExpected = 8
local TecnExpected = 4
local ArmedNear2AtStart = 4   -- Rifle1 Rifle2 Rifle3 Tank1
local TecnNear2AtStart = 2    -- Tecn1 Tecn2

local Armed, Tecns
local Sr

local ticks = 0
local censusTick = math.floor(CensusSeconds * TestHarness.TicksPerSecond)
local printEveryTicks = math.floor(PrintEverySeconds * TestHarness.TicksPerSecond)

local opening = nil     -- census at the first poll
local sample = nil      -- census at CensusSeconds — the reported number

-- Chebyshev (king-move), the same metric PoiOffenseMath.Chebyshev uses inside the
-- module, so the two observations are in the same units.
local function CellDistance(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

-- Returns alive-and-in-world count, count within Chebyshev 2 of the SR, within 4,
-- and the furthest any of them has got from the SR.
local function Census(list)
	local pool, near2, near4, far = 0, 0, 0, 0
	for i = 1, #list do
		local u = list[i]
		if u ~= nil and not u.IsDead and u.IsInWorld then
			pool = pool + 1
			local d = CellDistance(u.Location, Sr)
			if d <= 2 then near2 = near2 + 1 end
			if d <= 4 then near4 = near4 + 1 end
			if d > far then far = d end
		end
	end
	return { pool = pool, near2 = near2, near4 = near4, far = far }
end

local function Describe(tag, a, t)
	return string.format(
		"[clog-census] %s tick=%d armed{pool=%d near2=%d near4=%d far=%d} tecn{pool=%d near2=%d near4=%d far=%d}",
		tag, ticks, a.pool, a.near2, a.near4, a.far, t.pool, t.near2, t.near4, t.far)
end

WorldLoaded = function()
	Armed = { Rifle1, Rifle2, Rifle3, Rifle4, Rifle5, Rifle6, Tank1, Tank2 }
	Tecns = { Tecn1, Tecn2, Tecn3, Tecn4 }
	Sr = CPos.New(SrX, SrY)

	TestHarness.FocusBetween(Tank1, Rifle6)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		ticks = ticks + 1

		local armed = Census(Armed)
		local tecn = Census(Tecns)

		-- Gate (2), on the very first poll: the counter must be able to move both ways.
		if opening == nil then
			opening = { armed = armed, tecn = tecn }
			print(Describe("opening", armed, tecn))

			if armed.pool ~= ArmedExpected or tecn.pool ~= TecnExpected then
				return "fail: scenario did not build — expected " .. ArmedExpected ..
					" armed and " .. TecnExpected .. " technicians in world at t=0, got " ..
					armed.pool .. " and " .. tecn.pool .. "; no census is meaningful from this world"
			end

			-- HARD: the PROPERTY that makes the census diagnostic — near2 must be able to move
			-- in both directions. Asserted as the property itself, deliberately NOT as a
			-- transcription of the expected count: the first granted run of this scenario was
			-- consumed by an arithmetic slip in exactly such a transcription, on a world that
			-- was in fact built correctly. A wrong constant must never again cost a run, and
			-- this form cannot be got wrong — it reads the two bounds off the same census.
			if armed.near2 == 0 or armed.near2 == armed.pool
				or tecn.near2 == 0 or tecn.near2 == tecn.pool then
				return "fail: near2 starts saturated or empty (armed " .. armed.near2 .. "/" ..
					armed.pool .. ", tecn " .. tecn.near2 .. "/" .. tecn.pool .. ") — the census " ..
					"cannot then tell 'nothing congested' from 'nothing there', which is the one " ..
					"confusion this scenario exists to escape"
			end

			-- SOFT: the exact placement, loud in the log but never fatal. This is the check
			-- that catches a real map.yaml drift; it is not worth a granted run to enforce.
			if armed.near2 ~= ArmedNear2AtStart or tecn.near2 ~= TecnNear2AtStart then
				print("[clog-census] PLACEMENT DRIFT: expected near2 armed=" .. ArmedNear2AtStart ..
					" tecn=" .. TecnNear2AtStart .. ", got armed=" .. armed.near2 ..
					" tecn=" .. tecn.near2 .. " — the run continues, but check map.yaml before " ..
					"trusting the bands")
			end
		end

		if ticks % printEveryTicks == 0 then
			print(Describe("poll", armed, tecn))
		end

		-- The reported sample, plus a screenshot of the beachhead at the same beat.
		-- Capture is async (lands at the end of the next RenderTick), which is why the
		-- deadline sits several seconds further out rather than passing immediately.
		if ticks == censusTick then
			sample = { armed = armed, tecn = tecn }
			print(Describe("SAMPLE", armed, tecn))
			TestHarness.Screenshot("clog-census",
				"expects: the bot's 12 placed units still clumped on and around the Supply Route " ..
				"at 6,16 — nothing fanned out, because with no believed enemy there is no staging anchor")
		end

		if ticks < math.floor(DeadlineSeconds * TestHarness.TicksPerSecond) - 2 then
			return false
		end

		-- Gate (1) at the sample point: the populations were intact when the number
		-- was taken. Anything else and the number describes a different world.
		if sample == nil then
			return "fail: sample point was never reached"
		end

		if sample.armed.pool ~= ArmedExpected or sample.tecn.pool ~= TecnExpected then
			return "fail: population changed before the sample — armed " .. sample.armed.pool ..
				"/" .. ArmedExpected .. ", tecn " .. sample.tecn.pool .. "/" .. TecnExpected ..
				"; the census describes a world other than the one placed"
		end

		-- Gate (3): say plainly whether the window held. Not a failure either way — a
		-- run in which something DID disperse the reserve is a result, not a broken
		-- test — but it must be visible in the log rather than inferred from the number.
		if sample.armed.near2 ~= opening.armed.near2 or sample.tecn.near2 ~= opening.tecn.near2
			or sample.armed.far ~= opening.armed.far or sample.tecn.far ~= opening.tecn.far then
			print("[clog-census] MOVED: the census changed between the opening poll and the sample — " ..
				"read the [exp-clog] anchor field in debug.log, the pre-contact window did not hold")
		else
			print("[clog-census] STATIC: not one unit changed band between t=0 and the sample")
		end

		print(Describe("final", sample.armed, sample.tecn))
		return true
	end, "clog census never reached its sample point — check lua.log is non-empty; " ..
		"an empty one means rules.yaml never loaded and this run measured nothing")
end
