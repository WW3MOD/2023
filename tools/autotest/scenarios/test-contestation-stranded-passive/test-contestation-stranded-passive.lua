-- Regression: a PASSIVE player whose last relieving teammate leaves is resolved, not stranded.
--
-- SupplyRouteContestation makes an overrun owner passive (not Lost) while a teammate still holds an
-- active Supply Route. OnDefeatBarFull used to return on `isPassive` before asking anything, so if
-- that teammate then left by a route the trait never sees -- surrender, ConcedeOnDisconnect, another
-- win condition -- the passive owner stayed Undefined forever. Its SR is untargetable and
-- MustBeDestroyed, so ConquestVictoryConditions could never fail it either: the match never ended.
--
-- Setup: USA (the client's seat) and USA2 (an `idle` bot seat, Playable, allied) each own a Supply
-- Route. Four Russian T-90s sit in USA's 10-cell ring; USA2's SR is ~23 cells away and untouched.
--   Phase 1: USA's defeat bar fills. USA2 is an active rescuer, so USA must go PASSIVE, not Lost.
--   Phase 2: USA2 leaves -- a Primary objective added and failed, which is exactly what surrender
--            (MissionObjectives.ForceDefeat) and ConcedeOnDisconnect reduce to. ForceDefeat itself
--            would no-op here: ConquestVictoryConditions never registers its objective under
--            TestMode, so there is nothing Incomplete for it to fail.
--
-- GREEN: within StrandedBudgetTicks of USA2 going Lost, USA.WinState == "Lost" and Russia is not.
-- RED (fix sabotaged -- restore the `isPassive` early return in OnDefeatBarFull): phase 1 passes,
--   then times out with "USA still Undefined and passive ... after its last rescuer USA2 went Lost".

-- "key=value|key=value" -> table
local function ParseState(s)
	local t = { }
	for k, v in string.gmatch(s, "([%w_]+)=([^|]*)") do
		t[k] = v
	end
	return t
end

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local usa2 = Player.GetPlayer("USA2")
	local russia = Player.GetPlayer("Russia")

	local raw = Test.SupplyRouteContestationState(OwnSR)
	print("[stranded-passive] sides=" .. Test.MatchSides() .. " own=" .. raw ..
		" ally=" .. Test.SupplyRouteContestationState(AllySR))

	if raw == "absent" then
		Test.Fail("OwnSR carries no SupplyRouteContestation -- the scenario has no subject")
		return
	end

	-- SEAT CHECK. Every failure here means the harness never built the 2v1 this scenario measures,
	-- so it says so instead of reporting on the fix.
	if usa == nil or not usa.IsLocalPlayer then
		Test.Fail("seat: the client is not sitting in USA -- USA must be the first Playable slot in map.yaml")
		return
	end
	if usa2 == nil then
		Test.Fail("seat: USA2 has no Player object -- Game.LoadMap did not seat the `idle` bot (slot_bot refused? " ..
			"check that rules.yaml's ModularBot@idle merged)")
		return
	end
	if not usa2.IsBot then
		Test.Fail("seat: USA2 exists but is not a bot -- the Playable-bot seating path did not run")
		return
	end
	if AllySR == nil or AllySR.IsDead or AllySR.Owner.InternalName ~= "USA2" then
		Test.Fail("seat: AllySR is missing or not owned by USA2 -- USA has no rescuer and phase 1 would eliminate it outright")
		return
	end
	if russia == nil then
		Test.Fail("Russia has no Player object -- the attackers would belong to Neutral and contest nothing")
		return
	end
	for _, a in ipairs({ Tank1, Tank2, Tank3, Tank4 }) do
		if a == nil or a.IsDead or a.Owner.InternalName ~= "Russia" then
			Test.Fail("an attacker is missing or not Russian at start")
			return
		end
	end

	local s = ParseState(raw)
	local minTicks = tonumber(s.min)
	local scan = tonumber(s.scan)

	-- Phase 1 ceiling, from the running rules: control bar to zero then defeat bar to full at <=
	-- MinTicks each, plus one ScanInterval before the first force recalculation, plus margin. The real
	-- run is far faster (nothing mobile on the team -> collapse speedup).
	local passiveBudgetTicks = 2 * minTicks + scan + 60

	-- Phase 2: the re-check runs on USA's SR's very next tick in the depleting branch, and the T-90s
	-- never leave. Two ScanIntervals plus margin is generous; anything later is not this mechanism.
	local strandedBudgetTicks = 2 * scan + 60

	print("[stranded-passive] min=" .. s.min .. " scan=" .. s.scan .. " runintestmode=" .. s.runintestmode ..
		" passiveBudget=" .. passiveBudgetTicks .. " strandedBudget=" .. strandedBudgetTicks)

	TestHarness.FocusBetween(OwnSR, Tank3)

	local startTick = DateTime.GameTime
	local passiveTick = nil
	local leftTick = nil

	-- SOLE VERDICT AUTHORITY (AssertWithin shape 2): nothing is scheduled after it, and every outcome
	-- is a `pass:`/`fail:` string from inside the predicate. The outer budget is the sum of both phases;
	-- each phase also enforces its own deadline so a slow phase 1 cannot lend time to phase 2.
	TestHarness.AssertWithin((passiveBudgetTicks + strandedBudgetTicks + 10) / TestHarness.TicksPerSecond, function()
		local now = ParseState(Test.SupplyRouteContestationState(OwnSR))
		local t = DateTime.GameTime

		if russia.WinState == "Lost" then
			return "fail: Russia was marked Lost -- the defeat cascaded across sides"
		end
		if usa.WinState == "Won" then
			return "fail: the overrun owner was marked Won"
		end

		if passiveTick == nil then
			if usa.WinState == "Lost" then
				return "fail: phase 1: USA was eliminated outright at +" .. (t - startTick) ..
					" -- USA2 was not counted as a rescuer (not Playable? not allied?), so this run never reached passive"
			end

			if now.passive == "true" then
				if usa2.WinState ~= "Undefined" then
					return "fail: phase 1: USA2 was already " .. usa2.WinState .. " before the scenario removed it"
				end

				passiveTick = t
				print("[stranded-passive] phase1 passive at +" .. (t - startTick) .. " state=" .. Test.SupplyRouteContestationState(OwnSR))

				-- USA2 leaves. AddPrimaryObjective + MarkFailedObjective is the MarkFailed call that
				-- surrender and ConcedeOnDisconnect both end in.
				usa2.MarkFailedObjective(usa2.AddPrimaryObjective("Stay in the game"))
				if usa2.WinState ~= "Lost" then
					return "fail: phase 2 setup: failing USA2's objective left it " .. usa2.WinState .. " -- the departure never happened"
				end

				leftTick = t
				print("[stranded-passive] phase2 USA2 Lost at +" .. (t - startTick) .. "; USA=" .. usa.WinState)
				return false
			end

			if t - startTick > passiveBudgetTicks then
				return "fail: phase 1: USA never went passive within " .. passiveBudgetTicks .. " ticks (state " ..
					Test.SupplyRouteContestationState(OwnSR) .. ") -- suspect the attackers' surplus or range, not the fix"
			end

			return false
		end

		if usa.WinState == "Lost" then
			local census = "USA resolved Lost +" .. (t - leftTick) .. " ticks after USA2 left (budget " .. strandedBudgetTicks ..
				"); passive at +" .. (passiveTick - startTick) .. "; USA2=" .. usa2.WinState .. " Russia=" .. russia.WinState ..
				" state=" .. Test.SupplyRouteContestationState(OwnSR)
			print("[stranded-passive] " .. census)
			return "pass: " .. census
		end

		if now.passive ~= "true" then
			return "fail: phase 2: USA stopped being passive before it was resolved (state " ..
				Test.SupplyRouteContestationState(OwnSR) .. ") -- the attackers let the bar drain; not attributable to the fix"
		end

		if t - leftTick > strandedBudgetTicks then
			return "fail: USA still " .. usa.WinState .. " and passive " .. (t - leftTick) .. " ticks after its last rescuer USA2 went Lost " ..
				"(state " .. Test.SupplyRouteContestationState(OwnSR) .. ") -- the stranded-passive re-check did not run"
		end

		return false
	end, function()
		return "outer watchdog expired (passiveTick=" .. tostring(passiveTick) .. " leftTick=" .. tostring(leftTick) ..
			" state=" .. Test.SupplyRouteContestationState(OwnSR) .. ")"
	end)
end
