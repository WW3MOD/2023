-- DIAGNOSTIC AUTOTEST — the frequency question, and the last measurement in
-- this series.
--
-- test-aa-overkill-pump proved the overkill counter CAN be held above the
-- threshold indefinitely when fed, but it fed the counter from Lua. That
-- settles the mechanism and says nothing about whether anything in the game
-- actually feeds it. This scenario removes the scripting entirely.
--
-- THE CANDIDATE PATH IS REAL: AttackFollow.Tick re-scans and calls
-- MarkTargetForAttack whenever a unit is NOT currently aiming (:156-172), and
-- OpportunityFire defaults true (AttackFollow.cs:26) with no infantry override.
-- MANPAD's BurstWait is 200 ticks, so a firing AA drops out of aiming between
-- shots and may re-acquire and re-apply its entire 500-point claim each cycle.
--
-- STAGING: four identical AA, one hostile helicopter, NOBODY ORDERED. There is
-- no Attack call anywhere in this file. Whichever AA commits first becomes the
-- shooter on its own; the other three ARE the measurement.
--
--   sustained re-marking  => exactly ONE of the four ever fires
--   single mark per commit => the other three join ~172 ticks later, which is
--                             the single-mark decay measured previously
--
-- The mark/damage decoupling that makes a multi-cycle fight possible at all is
-- explained in rules.yaml; in short, the estimate ignores warhead ValidTargets
-- while the real damage path does not, so the weapon can carry a big
-- estimate-only warhead and a small real one. Nothing in the TARGETING path is
-- touched.
--
-- HARNESS NOTE: the aircraft is spawned in WorldLoaded and joins the world in a
-- frame-end task (ActorGlobal.cs:113-116). Nothing here targets it explicitly,
-- so that trap does not apply -- but the observation window starts at tick 1
-- regardless, and the AA acquire it on their own once it exists.

-- MEASURED 2026-08-11 (seed -484693258), and the answer is NEGATIVE — ordinary
-- firing does not re-mark, so the pump does not occur through opportunity fire:
--
--   unit   first shot   gap from previous   total shots
--   AA3       t37             --                 8
--   AA1      t200            163                 7
--   AA2      t386            186                 6
--   AA4      t571            185                 5
--   shooter gaps all exactly 200 (= MANPAD BurstWait); target survived to t1500
--
-- THE TELL PASSED FIRST: the helicopter survived and exactly ONE unit fired
-- early. The previous run of this scenario was void and showed two firing
-- simultaneously at t34 with a third at t79 — that is what an un-applied mark
-- looks like, and it is why the tell is checked before the result is read.
--
-- ORDINARY FIRING DOES NOT RE-MARK. AA3 fired eight times across the window at
-- a strict 200-tick cadence, and the other three still joined on schedule. Had
-- each shot re-applied the 503-point claim, no one else could ever have
-- engaged. The mark is applied once per COMMITMENT, not once per shot, so the
-- AttackFollow re-mark path (:156-172) does not fire on the reload cycle.
--
-- BUT THE BATTERY SERIALISES, which is the finding worth carrying. Each new
-- unit that commits re-loads the mark to ~503, and the next one must wait out
-- roughly three halvings before it clears the threshold — hence the near
-- constant ~185-tick spacing. Four AA took 571 ticks, about 34 real seconds, to
-- all engage a single helicopter. They trickle in one at a time instead of
-- firing together.
--
-- HONEST CAVEAT ABOUT WHAT THIS RUN IS. A fight this long only exists because
-- the weapons.yaml split deliberately breaks the normal coupling between the
-- mark and the damage. With a stock MANPAD the first missile kills a 600-HP
-- helicopter and no second unit is ever needed. So the serialisation above is
-- what happens specifically WHEN the claim exceeds the damage actually dealt —
-- which is exactly the miss case, and exactly the ValidTargets over-count
-- defect. It is not a claim about a fight where every shot lands.

local AirRow = 8
local AirCol = 31
local AirAltitude = 1280
local ObserveSeconds = 60
local MaxShotsRecorded = 10

local tick = 0
local setupFaults = {}
local units = {}
local halo = nil
local haloDeathTick = nil

local function cellPos(cx, cy, alt)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, alt or 0)
end

local function pollTick()
	tick = tick + 1

	for _, u in ipairs(units) do
		if not u.actor.IsDead then
			local ammo = u.actor.AmmoCount("primary-ammo")
			if ammo < u.lastAmmo then
				u.shots = u.shots + (u.lastAmmo - ammo)
				if #u.shotTicks < MaxShotsRecorded then
					table.insert(u.shotTicks, tick)
				end
				u.lastAmmo = ammo
			end
		end
	end

	if haloDeathTick == nil and halo.IsDead then
		haloDeathTick = tick
	end
end

local function startPolling(seconds, onDone)
	local remaining = math.floor(seconds * TestHarness.TicksPerSecond)
	local step
	step = function()
		pollTick()
		remaining = remaining - 1
		if remaining <= 0 then
			onDone()
		else
			Trigger.AfterDelay(1, step)
		end
	end
	Trigger.AfterDelay(1, step)
end

local function finish()
	local firedCount = 0
	local parts = {}

	for _, u in ipairs(units) do
		if u.shots > 0 then firedCount = firedCount + 1 end
		table.insert(parts, u.name
			.. " shots" .. u.shots
			.. " first" .. (u.shotTicks[1] or -1)
			.. " ticks[" .. table.concat(u.shotTicks, ",") .. "]")
	end

	-- The shooter's inter-shot gaps are the cadence itself, and they should sit
	-- near MANPAD's BurstWait of 200 if the fight ran normally.
	local gaps = {}
	local shooter = nil
	for _, u in ipairs(units) do
		if shooter == nil or u.shots > shooter.shots then shooter = u end
	end
	if shooter ~= nil then
		for i = 2, #shooter.shotTicks do
			table.insert(gaps, shooter.shotTicks[i] - shooter.shotTicks[i - 1])
		end
	end

	-- Second-engager latency is the answer: how long the OTHER units waited.
	local firsts = {}
	for _, u in ipairs(units) do
		if u.shotTicks[1] ~= nil then table.insert(firsts, u.shotTicks[1]) end
	end
	table.sort(firsts)
	local secondEngager = firsts[2]
	local gapToSecond = -1
	if firsts[1] ~= nil and secondEngager ~= nil then
		gapToSecond = secondEngager - firsts[1]
	end

	if firedCount == 0 then
		table.insert(setupFaults, "no AA fired at all - nothing was measured")
	end

	local summary = table.concat(parts, " | ")
		.. " || firedOf4=" .. firedCount
		.. " gapFirstToSecond=" .. gapToSecond
		.. " shooterGaps[" .. table.concat(gaps, ",") .. "]"
		.. " haloDeath" .. (haloDeathTick or -1)

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	Test.Pass(summary)
end

WorldLoaded = function()
	local Russia = Player.GetPlayer("Russia")
	if Russia == nil then
		Test.Fail("Russia player not found")
		return
	end

	local actors = { AA1, AA2, AA3, AA4 }
	local names = { "AA1", "AA2", "AA3", "AA4" }
	for i, a in ipairs(actors) do
		if a == nil then
			Test.Fail("AA actor missing: " .. names[i])
			return
		end
		table.insert(units, {
			name = names[i],
			actor = a,
			lastAmmo = a.AmmoCount("primary-ammo"),
			shots = 0,
			shotTicks = {},
		})
	end

	halo = Actor.Create("halo", true, {
		Owner = Russia,
		CenterPosition = cellPos(AirCol, AirRow, AirAltitude),
		Facing = Angle.South,
	})
	if halo == nil then
		Test.Fail("could not spawn halo")
		return
	end

	TestHarness.FocusBetween(AA1, AA4)
	TestHarness.Select(AA1)

	-- No orders. No marking. Everything from here is the units' own autotarget.
	startPolling(ObserveSeconds, finish)
end
