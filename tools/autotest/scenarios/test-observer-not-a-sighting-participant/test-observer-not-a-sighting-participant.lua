-- An authored `Spectating: True` seat with a lobby client in it must NOT be a sighting participant.
--
-- WHY THIS SHAPE. `Player`'s client branch used to drop NonCombatant/Playable/Spectating on the
-- floor, so the Observer seat below was a full combatant to `player.NonCombatant ||
-- player.Spectating`. That is now fixed at source -- but the fix alone CANNOT reach this seat,
-- because `Player.Spectating` is `!inMissionMap && (...)` (Player.cs:86) and every autotest map is
-- MissionSelector, so the runtime property stays false no matter what the constructor copies.
-- Reading the AUTHORED flag (CombatantSides.CountsAsASide) is the only thing that excludes it, and
-- SightingThreatLayer.Recompute is where that now happens.
--
-- WHAT IS ASSERTED, on one tick, over one cell:
--   USA      -- a real side with its own armour -- must read > 0.   (control)
--   Observer -- the authored spectator seat      -- must read = 0.   (the fix)
-- The control is not decoration: a dead layer, a wrong cell, or fog quietly re-armed would drive
-- BOTH readings to 0, and without it the Observer's 0 would be indistinguishable from success.

WorldLoaded = function()
	local observer = Player.GetPlayer("Observer")
	local usa = Player.GetPlayer("USA")

	if observer == nil then
		Test.Fail("no player named Observer -- the scenario's own premise is gone")
		return
	end

	if usa == nil then
		Test.Fail("no player named USA -- the control is gone")
		return
	end

	TestHarness.FocusBetween(Abrams, Foe1)

	-- SightingThreatLayer.UpdateInterval is 25 ticks with a deterministic first-tick stagger of 0,
	-- so three intervals is comfortably past the first Recompute AND past the first decay pass --
	-- long enough that a field which merely had not been built yet would have been by now.
	Trigger.AfterDelay(80, function()
		local cell = Foe0.Location

		local control = Test.GetThreatIntensity(usa, cell)
		local leaked = Test.GetThreatIntensity(observer, cell)

		if control <= 0 then
			Test.Fail("CONTROL FAILED, so the Observer reading proves nothing: USA has no threat " ..
				"intensity at (" .. cell.X .. "," .. cell.Y .. ") where three T-90s stand. Either " ..
				"the sighting layer never ran, fog is on, or the Russians are dead -- fix the " ..
				"scenario before reading anything into the Observer value (" .. leaked .. ")")
			return
		end

		if leaked ~= 0 then
			Test.Fail("the Observer seat was built a sighting field: threat " .. leaked .. " at (" ..
				cell.X .. "," .. cell.Y .. ") -- an authored `Spectating: True` slot with a lobby " ..
				"client in it is still a participant (control USA reads " .. control .. ")")
			return
		end

		Test.Pass()
	end)
end
