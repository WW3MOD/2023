-- AUTO TEST: Abrams instant-killed at full HP must not spawn surviving crew.
--
-- Without the onDeath damage-scaling fix, the legacy 90% EjectionSurvivalRate
-- means ~2.7 of 3 crew slots spawn an actor on a one-shot kill (RED).
-- With the fix, finishingDamage = MaxHP + overkill is huge, crewDamage scales
-- to >= crewMaxHP for every slot, and no crew actors spawn (GREEN).

local DeadlineSeconds = 4

WorldLoaded = function()
	TestHarness.FocusBetween(Tank)
	TestHarness.Select(Tank)

	-- Cache owner before kill — Player ref stays valid even after Tank dies.
	local owner = Tank.Owner

	-- Apply overkill damage so finishingDamage = MaxHP + 100000, far above the
	-- 25% threshold. With my fix Killed captures this and EjectCrewMember
	-- gates each slot via the damage formula.
	-- Health setter: damage = HP - value. value = -100000 → damage = HP + 100000.
	Tank.Health = -100000

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if not Tank.IsDead then
			return false
		end

		local drivers = owner.GetActorsByType("crew.driver.america")
		local gunners = owner.GetActorsByType("crew.gunner.america")
		local commanders = owner.GetActorsByType("crew.commander.america")

		local total = #drivers + #gunners + #commanders
		if total > 0 then
			return "fail: " .. total .. " crew actor(s) spawned despite overkill instant-kill (expected 0)"
		end

		return true
	end, "Abrams did not die within " .. DeadlineSeconds .. "s")
end
