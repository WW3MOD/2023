-- Regression: the win condition runs end to end under TestMode when a scenario opts in.
--
-- SupplyRouteContestation.ResolveTeamElimination returned unconditionally under TestMode, so no
-- autotest could ever watch a contested Supply Route decide a match: the bars ran, the owner went
-- passive, and WinState stayed Undefined forever. RunInTestMode (set in this scenario's rules.yaml)
-- lets the real resolution run; MissionObjectives.CheckIfGameIsOver still declines to end the world
-- under TestMode, so this script keeps running and reads the WinStates it set.
--
-- Setup: USA's Supply Route, nothing of USA's that can move, four Russian T-90s parked inside its
-- 10-cell contestation ring. Nobody can relieve USA, so a full defeat bar must mark USA Lost.
--
-- GREEN: USA.WinState == "Lost", Russia not Lost (no cascade), within the deadline below.
-- RED (RunInTestMode: false in rules.yaml): fails fast with "defeat bar full and USA passive, but
--   WinState still Undefined" -- the bars filled and the resolution was skipped.
--
-- Russia is NOT asserted Won. run-test.sh seats one client, so only USA is Playable, and both
-- ResolveTeamElimination and AwardDecidedSurvivors consider Playable combatants only. Russia staying
-- Undefined here is the shipped filter working, not a missing award.

local AttackerCount = 4

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
	local russia = Player.GetPlayer("Russia")

	local raw = Test.SupplyRouteContestationState(OwnSR)
	local sides = Test.MatchSides()
	print("[contest-defeat] sides=" .. sides .. " initial=" .. raw)

	-- USA MUST BE THE SEATED, PLAYABLE SIDE, or both arms time out blaming the wrong thing:
	-- ResolveTeamElimination only marks Playable players, so a mis-seated USA is never resolved
	-- whatever RunInTestMode says. Lua has no Playable binding; IsLocalPlayer stands in for it,
	-- because run-test.sh seats exactly one client and that client takes the map's Playable slot.
	local seated = false
	for name in string.gmatch(sides, "[^,]+") do
		if name == "USA" then
			seated = true
		end
	end

	if usa == nil or not seated or not usa.IsLocalPlayer or usa.IsNonCombatant then
		Test.Fail("fail: SETUP -- USA is not the seated Playable side (sides=" .. sides ..
			", local=" .. tostring(usa ~= nil and usa.IsLocalPlayer) .. "); contestation could never resolve it")
		return
	end

	if raw == "absent" then
		Test.Fail("OwnSR carries no SupplyRouteContestation -- the scenario has no subject")
		return
	end

	if russia == nil then
		Test.Fail("Russia has no Player object -- the attackers would belong to Neutral and contest nothing")
		return
	end

	local attackers = { Tank1, Tank2, Tank3, Tank4 }
	for _, a in ipairs(attackers) do
		if a == nil or a.IsDead or a.Owner.InternalName ~= "Russia" then
			Test.Fail("an attacker is missing or not Russian at start; expected " .. AttackerCount)
			return
		end
	end

	local s = ParseState(raw)
	local minTicks = tonumber(s.min)
	local scan = tonumber(s.scan)

	-- DEADLINE, FROM THE RULES THAT ARE RUNNING. Four T-90s are 9600 of value against a defender with
	-- none, so the attacker's surplus clears ReferenceValue * BaseTicks / MinTicks (7500 at shipped
	-- values) and each bar drains at the MinTicks floor even for a HEALTHY defending team. The defender
	-- here has nothing mobile, so the collapse speedup makes the real run far faster (~100 ticks:
	-- CollapseMinTicks then LockoutCollapseTicks) -- this is the ceiling, not the expectation.
	-- Control bar to zero, then defeat bar to full, at <= MinTicks each; plus one ScanInterval before
	-- the first force recalculation, plus margin.
	local deadlineTicks = 2 * minTicks + scan + 60
	print("[contest-defeat] base=" .. s.base .. " min=" .. s.min .. " scan=" .. s.scan ..
		" runintestmode=" .. s.runintestmode .. " deadlineTicks=" .. deadlineTicks)

	TestHarness.FocusBetween(OwnSR, Tank3)

	local startTick = DateTime.GameTime

	TestHarness.AssertWithin(deadlineTicks / TestHarness.TicksPerSecond, function()
		local now = ParseState(Test.SupplyRouteContestationState(OwnSR))

		if usa.WinState == "Lost" then
			if russia.WinState == "Lost" then
				return "fail: USA resolved Lost but the defeat cascaded onto Russia too (" .. russia.WinState .. ")"
			end

			local census = "resolved at +" .. (DateTime.GameTime - startTick) .. " of " .. deadlineTicks ..
				" ticks: USA=" .. usa.WinState .. " Russia=" .. russia.WinState .. " state=" .. Test.SupplyRouteContestationState(OwnSR)
			print("[contest-defeat] " .. census)
			return "pass: " .. census
		end

		if usa.WinState == "Won" then
			return "fail: the overrun owner was marked Won"
		end

		-- ResolveTeamElimination runs on the same tick OnDefeatBarFull sets isPassive, so with the
		-- opt-in live these two are never observed apart. Seeing them apart IS the RED.
		if now.passive == "true" and tonumber(now.defeat) >= tonumber(now.barmax) then
			return "fail: defeat bar full and USA passive, but WinState still " .. usa.WinState ..
				" -- elimination did not resolve under TestMode (runintestmode=" .. now.runintestmode .. ")"
		end

		return false
	end, function()
		return "USA's Supply Route was not resolved within " .. deadlineTicks .. " ticks and the defeat bar never filled " ..
			"(state " .. Test.SupplyRouteContestationState(OwnSR) .. ") -- suspect the attackers' surplus or range, not the opt-in"
	end)
end
