-- AUTO TEST: each @experimental twin must BUY at least one Special Forces of its OWN faction,
-- and never more than UnitLimits (2), unaided.
--
-- WHAT IS UNDER TEST is three YAML lines per twin and nothing else — no module, no C#:
--   UnitsToBuild  sf.america: 10   (ai-america.yaml:193)   sf.russia: 10   (ai-russia.yaml:187)
--   UnitLimits    sf.america: 2    (ai-america.yaml:246)   sf.russia: 2    (ai-russia.yaml:234)
--   UnitFloors    sf.america: 1    (ai-america.yaml:351)   sf.russia: 1    (ai-russia.yaml:274)
-- The buy-side config shipped at 8845f711 and reached @stable at the 2026-09-02 promotion
-- (3318d5c7), but NO scenario has ever asserted it fires: `sf.america`/`sf.russia` had zero hits
-- under tools/autotest/scenarios/ before this file. That is the gap this closes — the YAML is
-- shipped-but-unverified, not missing.
--
-- WHY THE WEIGHT IS NOT WHAT IS BEING TESTED. sf is deliberately absent from UnitTargetShares and
-- UnitRoles on both twins, so under CompositionDirected + CompositionEnforceTargetCeiling it is
-- ineligible for the directed deficit pick (UnitBuilderBotModule.cs:328). The FLOOR lane is the
-- only path in, and ChooseBelowFloor gates on UnitsToBuild MEMBERSHIP
-- (UnitBuilderBotModule.cs:1390) — so the `10` buys nothing, and the floor entry would be inert
-- without the membership line. All three lines are load-bearing together, which is why reverting
-- any one of them must go red.
--
-- WHO ELSE COULD SATISFY THE PREDICATE (AUTOTEST.md "audit your own predicate"):
--   * no sf is pre-placed in map.yaml, and -SpawnStartingUnits is set, so one can only appear by
--     procurement;
--   * counts are read PER PLAYER via GetActorsByType, so neither bot can satisfy the other's arm;
--   * sf.russia carries `~player.russia` and sf.america `~player.america`
--     (infantry-russia.yaml:103, infantry-america.yaml:105), so neither twin can field the other's
--     SF even in principle — each arm can only be met by its own twin's own config;
--   * both players are `Bot: experimental`, so neither arm can be met by the @stable twin's
--     (identical, post-promotion) entries.
--
-- WHAT MAKES IT GO RED — the three arms report different text on purpose:
--   * delete `sf.america: 10` from UnitsToBuild on ai-america.yaml's @experimental twin and the
--     floor entry goes inert: timeout naming USA-bot, "never bought an SF".
--   * same on the russia twin: timeout naming Russia-bot.
--   * raise or remove UnitLimits and a rich bot buys a third: immediate named ceiling failure.

local DEADLINE_TICKS = 9000

-- Budget in TICKS and convert, per test-helpers.lua:30-35 — TicksPerSecond is 25 there while the
-- mod runs at Timestep 60 (16.667 t/s), so the argument is NOT seconds and must not be read as
-- such. 9000 is a multiple of 25, which is the only class of value AssertWithin's math.floor
-- round-trip is guaranteed not to shave a tick off.
local DEADLINE = DEADLINE_TICKS / TestHarness.TicksPerSecond

-- WHY 9000. sf's UnitDelays gate is 3000 ticks on both twins and is deliberately not stubbed out
-- (see rules.yaml). test-experimental-msar-deploy proves 6000 is enough for msar, which sits
-- FIFTH in the ordinal floor walk; sf is SIXTH, behind floors of 2 on medi/aa/dr and msar's own
-- 1600, so it needs strictly more post-gate cycles than msar did. 9000 leaves 6000 ticks after the
-- gate opens — double the margin msar passed on — plus the settle window below.
local SETTLE_TICKS = 750

-- UnitLimits on both twins. Named once so the failure text and the check cannot drift apart.
local UNIT_LIMIT = 2

-- The twins, one row each. `sfType` is the LOWERCASED actor name: the rules define `SF.america:`
-- (infantry-america.yaml:102) but actor names are lowercased at ruleset load (ActorNameCase), which
-- is why the AI config spells it `sf.america` and why GetActorsByType must too.
local twins = {
	{ playerName = "USA-bot",    sfType = "sf.america", player = nil, firstSeen = nil, maxSeen = 0 },
	{ playerName = "Russia-bot", sfType = "sf.russia",  player = nil, firstSeen = nil, maxSeen = 0 },
}

local tick = 0
local bothSeenAt = nil

-- Live state for the timeout message. AssertWithin evaluates its third argument EAGERLY at
-- registration when it is a string, so anything interpolated there reports its value at tick 1
-- forever; a FUNCTION is evaluated once at timeout instead (test-helpers.lua:78-84) and is the only
-- form that can report what actually happened.
local function describe()
	local parts = {}
	for _, t in ipairs(twins) do
		local now = #t.player.GetActorsByType(t.sfType)
		parts[#parts + 1] = t.playerName .. " " .. t.sfType
			.. ": now=" .. now
			.. " max=" .. t.maxSeen
			.. " firstSeen=" .. tostring(t.firstSeen)
			.. " cash=" .. t.player.Cash
	end
	return table.concat(parts, "; ")
end

WorldLoaded = function()
	for _, t in ipairs(twins) do
		t.player = Player.GetPlayer(t.playerName)
		if t.player == nil then
			Test.Fail("SETUP INVALID: " .. t.playerName .. " player not found at load")
			return
		end
	end

	-- Nothing to focus on yet — the map is deliberately empty of units — so frame the two
	-- beachheads the call-ins will arrive at.
	TestHarness.FocusBetween(OwnSR, OpponentSR)

	TestHarness.AssertWithin(DEADLINE, function()
		tick = tick + 1

		local allSeen = true
		for _, t in ipairs(twins) do
			local n = #t.player.GetActorsByType(t.sfType)
			if n > t.maxSeen then
				t.maxSeen = n
			end

			-- OVER-BUY IS A DISTINCT, NAMED FAILURE, not a pass. UnitLimits is 2 against a floor of
			-- 1, so the shipped configuration is effectively a cap of 1 with one slot of headroom
			-- reserved for the infiltration module that is NOT part of this change
			-- (ai-america.yaml:241-245). A third SF means the cap stopped binding, and this is the
			-- line that says so rather than letting the run pass on the first one bought.
			if n > UNIT_LIMIT then
				return "fail: " .. t.playerName .. " owns " .. n .. " " .. t.sfType
					.. " at tick " .. tick .. ", exceeds UnitLimits " .. UNIT_LIMIT
					.. " — the cap stopped binding (" .. describe() .. ")"
			end

			if n >= 1 and t.firstSeen == nil then
				t.firstSeen = tick
			end

			if t.firstSeen == nil then
				allSeen = false
			end
		end

		-- Latch the moment BOTH twins have been observed owning one, then keep running for
		-- SETTLE_TICKS so the ceiling arm above gets a real window rather than being evaluated once
		-- on the tick the lower arm is met. BOUNDED, NOT EXHAUSTIVE, stated plainly: 750 ticks is
		-- ~45 s of game time and a few production cycles, so this catches a cap that never binds,
		-- not one that fails only much later in a long match.
		--
		-- The latch is deliberately not cleared if an SF dies afterwards: "the bot bought one" is
		-- what the lower arm asserts, and having witnessed it is permanent. Clearing it would turn a
		-- combat loss into a timeout.
		if allSeen and bothSeenAt == nil then
			bothSeenAt = tick
		end

		if bothSeenAt ~= nil and tick - bothSeenAt >= SETTLE_TICKS then
			return true
		end

		return false
	end, function()
		local missing = {}
		for _, t in ipairs(twins) do
			if t.firstSeen == nil then
				missing[#missing + 1] = t.playerName .. " (" .. t.sfType .. ")"
			end
		end

		if #missing == #twins then
			return "NEITHER twin bought an SF within " .. DEADLINE_TICKS .. " ticks — the floor lane "
				.. "never fired on either faction. Check that UnitDelays sf 3000 has passed, that the "
				.. "bank cleared 600, and that UnitsToBuild/UnitFloors/UnitLimits sf entries are "
				.. "present on BOTH @experimental twins (ai-america.yaml, ai-russia.yaml). ["
				.. describe() .. "]"
		end

		if #missing > 0 then
			return "ONE twin never bought an SF within " .. DEADLINE_TICKS .. " ticks: "
				.. table.concat(missing, ", ") .. " — that faction's @experimental UnitBuilder twin "
				.. "is the one to look at; the other bought fine, so the delay, the bank and the "
				.. "floor-walk ordering are all exonerated. [" .. describe() .. "]"
		end

		return "both twins bought an SF but the " .. SETTLE_TICKS .. "-tick ceiling settle window "
			.. "never completed within " .. DEADLINE_TICKS .. " ticks (bothSeenAt="
			.. tostring(bothSeenAt) .. ") — DEADLINE_TICKS is too tight for the settle window, this "
			.. "is a scenario-budget bug, not a bot failure. [" .. describe() .. "]"
	end)
end
