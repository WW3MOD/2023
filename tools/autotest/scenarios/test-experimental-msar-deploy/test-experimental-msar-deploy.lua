-- AUTO TEST: the @experimental bot must BUY exactly one MSAR and DEPLOY it, unaided.
--
-- Before this feature both halves were human-only: `msar` had zero hits anywhere under
-- mods/ww3mod/rules/ai (never bought) and no bot module issued the "GrantConditionOnDeploy" order
-- (never deployed). MSAR is the mod's only counter-battery vision source, and both Radar and
-- CounterBatteryRadar carry `RequiresCondition: deployed`, so an MSAR the bot buys and parks grants
-- nothing at all — which is why this asserts the DEPLOYED state and not merely ownership.
--
-- WHY THE PREDICATE IS EXCLUSIVE (AUTOTEST.md "who ELSE could satisfy your predicate"):
--   * no msar is pre-placed in map.yaml, so one can only appear by procurement;
--   * this script never calls Test.IssueDeploy, so `deployed` can only be reached by the bot's own
--     order;
--   * MSAR is the ONLY actor in the mod carrying the top-level `CounterBatteryRadar:` trait
--     (mods/ww3mod/rules/ingame/vehicles.yaml:441 is the sole source — the other grep hits are
--     `Detectable: CounterBatteryRadar: 1` fields, i.e. detectABILITY, not sources), so CBR cover
--     for USA-bot cannot come from anything else;
--   * the opponent is @stable, whose UnitBuilder twins have no msar entry, and cover is queried
--     per-player regardless.
--
-- WHAT MAKES IT GO RED. Revert either half and this times out: drop the UnitFloors/UnitsToBuild/
-- UnitLimits msar entries on the @experimental twins and no MSAR is ever bought (fails at the
-- "never bought one" arm); drop CounterBatteryRadarBotModule and one is bought but never deployed
-- (fails at the "bought N, none deployed" arm). The two arms report different text on purpose.

local DEADLINE_TICKS = 6000

-- Budget in TICKS and convert, per test-helpers.lua:24-25 — TicksPerSecond is 25 there while the
-- mod runs at Timestep 60 (16.667 t/s), so the argument is not seconds and must not be read as such.
local DEADLINE = DEADLINE_TICKS / TestHarness.TicksPerSecond

local usa = nil

-- Live state for the timeout message. AssertWithin evaluates its third argument EAGERLY at
-- registration when it is a string, so anything interpolated there reports its value at tick 1
-- forever; a FUNCTION is evaluated once at timeout instead (test-helpers.lua:68-74) and is the only
-- form that can report what actually happened.
local seenCount = 0
local seenImmobile = false

WorldLoaded = function()
	usa = Player.GetPlayer("USA-bot")
	if usa == nil then
		Test.Fail("SETUP INVALID: USA-bot player not found at load")
		return
	end

	-- Nothing to focus on yet — the map is deliberately empty of units — so frame the beachhead the
	-- radar will be sited from.
	TestHarness.FocusBetween(OwnSR, OpponentSR)

	TestHarness.AssertWithin(DEADLINE, function()
		local radars = usa.GetActorsByType("msar")
		seenCount = #radars

		-- OVER-BUY IS A DISTINCT, NAMED FAILURE, not a pass. The design is exactly one: coverage does
		-- not stack, so a second 1600-cost radar inside the first one's disc is wasted budget. Both
		-- UnitLimits and UnitFloors are 1, and if either stops binding this is the line that says so
		-- rather than letting it pass on the first deployed one.
		if seenCount > 1 then
			return "fail: bot bought " .. seenCount .. " MSARs, expected exactly 1 (UnitLimits/UnitFloors msar no longer binding)"
		end

		if seenCount == 0 then
			return false
		end

		local radar = radars[1]
		if radar.IsDead then
			return false
		end

		-- `deployed` disables Mobile (RequiresCondition: !deployed on MSAR's Mobile), so this is a
		-- direct readout of the deploy state. It is ALSO false under EMP or while being captured
		-- (^Vehicle's Mobile carries PauseOnCondition: !(!empdisable && !being-captured)) — neither
		-- exists in this scenario, but that is why it is the corroborating term and not the verdict.
		seenImmobile = not radar.IsMobile

		if Test.HasCounterBatteryRadarCover(usa, radar.Location) then
			return true
		end

		-- IMMOBILE BUT NO COVER: the two readouts disagree, which cannot happen if the deploy
		-- succeeded. Named rather than left to time out, because a silent timeout here would look
		-- identical to "the bot never deployed it" and wants the opposite investigation — most
		-- likely the probe assumption (that a source covers its own cell) rather than the feature.
		if seenImmobile then
			return "fail: MSAR reports immobile (deployed) but USA-bot has no counter-battery cover at its own cell "
				.. radar.Location.X .. "," .. radar.Location.Y .. " — probe assumption or MapLayers.AddSource is wrong, not the bot"
		end

		return false
	end, function()
		if seenCount == 0 then
			return "bot never bought an MSAR within " .. DEADLINE_TICKS .. " ticks — floor lane never fired "
				.. "(check UnitDelays msar 3000 has passed, that the bank cleared 1600, and that UnitsToBuild/UnitFloors msar are present on the experimental twin)"
		end

		return "bot bought " .. seenCount .. " MSAR but it was never deployed within " .. DEADLINE_TICKS
			.. " ticks (immobile=" .. tostring(seenImmobile) .. ") — CounterBatteryRadarBotModule never reached a deployable cell, "
			.. "or something else moved the radar and UndeployOnMove revoked it"
	end)
end
