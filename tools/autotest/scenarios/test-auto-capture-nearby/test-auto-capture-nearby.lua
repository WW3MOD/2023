-- AUTO TEST: an idle technician takes a nearby capturable structure without being told to, and the
-- stances control how eager it is.
--
-- THE DESIGN BEING PINNED. AutoTarget carries TWO stance enums and they answer different questions,
-- which is why "Fire at will or Hunt?" has the answer "both, on different axes":
--
--     MAY it act?      UnitStance        HoldFire / Ambush / FireAtWill
--     HOW FAR will it go?  EngagementStance  HoldPosition / Defensive / Hunt
--
-- A fresh unit is FireAtWill + Defensive, so the behaviour is ON by default at the conservative
-- 8-cell radius with nothing configured. HoldFire is the per-unit off switch; Hunt widens the
-- radius to 20.
--
-- THE NEGATIVE ARMS ARE THE POINT. The behaviour ships enabled, so "it captures things" is the easy
-- half and would pass even if the radius and the off switch did nothing at all. Arms 2 and 3 are
-- what make this test worth running: a technician that captures when it must not is a unit the
-- player cannot park, and a radius that does not bind is the "goes far to find them" the brief
-- explicitly ruled out.
--
-- NO ORDERS ARE ISSUED ANYWHERE IN THIS SCRIPT. That is deliberate and load-bearing: the entire
-- claim is that this happens on the unit's own initiative. The only calls made are stance changes
-- and reads.

-- Budgeted in TICKS deliberately. TestHarness.TicksPerSecond is 25 while the game runs at 16.67 and
-- is documented as left that way on purpose (test-helpers.lua:16-25), so a "seconds" budget
-- silently buys 1.5x more real time than it reads.
--
-- ScanInterval is 40 ticks and the phase is staggered per actor, so the first scan lands within 40
-- ticks. A 5-cell walk plus the 20-tick neutral CaptureDelay is well inside SettleTicks.
local SettleTicks = 700      -- ~42 s real: first scan + 5-cell walk + capture.
local RoamTicks = 900        -- ~54 s real: 15-cell walk after the switch to Hunt.

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")

	TestHarness.FocusBetween(TechDefault, DerrickEnemy)

	-- ARM 2 SETUP. Must happen before the first scan can fire. `Stance` is the FIRE stance, which is
	-- the axis that gates the behaviour outright.
	TechHoldFire.Stance = "HoldFire"
	if TechHoldFire.Stance ~= "HoldFire" then
		Test.Fail("[acn] setup invalid: could not put TechHoldFire on the HoldFire stance, so the " ..
			"off-switch arm would pass vacuously")
		return
	end

	-- Assert the OTHER technicians are on the shipped defaults rather than assuming it. If the
	-- default stance ever changes, this test should say so rather than quietly measure something
	-- else -- the whole "default is that they do capture" claim rests on this being FireAtWill.
	if TechDefault.Stance ~= "FireAtWill" then
		Test.Fail("[acn] setup invalid: a fresh technician is on the '" .. tostring(TechDefault.Stance) ..
			"' fire stance, not FireAtWill. The default-ON behaviour this test exists to verify is " ..
			"gated on that default, so the premise has moved")
		return
	end

	Trigger.AfterDelay(SettleTicks, function()
		TestHarness.Screenshot("acn-settled",
			"the near neutral and near enemy derricks should be USA; the quiet and far ones untouched")

		-- ARM 1: the headline behaviour. Nobody ordered this.
		if DerrickNear.Owner ~= USA then
			Test.Fail("[acn] fail: an idle technician 5 cells from a neutral derrick did not capture " ..
				"it on its own after " .. SettleTicks .. " ticks -- it is still owned by " ..
				tostring(DerrickNear.Owner.Name) .. ". 5 cells is inside the 8-cell Defensive " ..
				"radius and the technician was on the default FireAtWill stance, so either the " ..
				"trait is not on TECN at all or the idle scan is not firing")
			return
		end

		-- ARM 4: the same thing on an enemy-held structure. A technician's Captures trait leaves
		-- ValidRelationships at Neutral|Enemy, so this is legal and must actually happen.
		if DerrickEnemy.Owner ~= USA then
			Test.Fail("[acn] fail: an idle technician 5 cells from an ENEMY derrick did not capture " ..
				"it -- still owned by " .. tostring(DerrickEnemy.Owner.Name) .. ". The brief was " ..
				"explicit that this applies to enemy structures as well as neutral ones; if the " ..
				"neutral arm above passed and this did not, the scan is filtering on owner")
			return
		end

		-- ARM 2: the off switch. This is the assertion that protects a player's ability to park a
		-- technician somewhere and have it stay put and stay out of trouble.
		if DerrickQuiet.Owner == USA then
			Test.Fail("[acn] fail: a technician on the HoldFire stance captured a derrick 5 cells " ..
				"away. HoldFire is the per-unit off switch for autonomous capture -- the behaviour " ..
				"ships ON, so a broken off switch means the player has no way to stop a technician " ..
				"wandering into a building they were saving")
			return
		end

		-- ARM 3, first half: the leash binds. 15 cells is outside the 8-cell Defensive radius.
		if DerrickFar.Owner == USA then
			Test.Fail("[acn] fail: a technician on the DEFENSIVE stance captured a derrick 15 cells " ..
				"away, which is outside its 8-cell radius. The brief was explicit that a technician " ..
				"should take what is near it and not go far to find targets, so a radius that does " ..
				"not bind is a real failure and not a harmless keenness")
			return
		end

		-- ARM 3, second half: asking for eagerness works. Same technician, same derrick, only the
		-- ENGAGEMENT stance changes -- which is the claim that the two axes do different jobs.
		if not Test.SetEngagementStance(TechRoam, "Hunt") then
			Test.Fail("[acn] setup invalid: could not put TechRoam on the Hunt engagement stance, so " ..
				"the graded-radius arm cannot be measured")
			return
		end

		Trigger.AfterDelay(RoamTicks, function()
			TestHarness.Screenshot("acn-hunt", "the far derrick should now be USA-owned after Hunt")

			if DerrickFar.Owner ~= USA then
				Test.Fail("[acn] fail: after switching to the Hunt engagement stance, a technician " ..
					"still did not reach a derrick 15 cells away in " .. RoamTicks .. " ticks -- it " ..
					"is owned by " .. tostring(DerrickFar.Owner.Name) .. ". 15 cells is inside the " ..
					"20-cell Hunt radius. Hunt is the only way the player can ask for more " ..
					"eagerness, so if it reaches no further than Defensive the stance grading " ..
					"conveys nothing")
				return
			end

			-- Re-checked at the END as well as above. The off-switch arm has now had
			-- SettleTicks + RoamTicks of idle time, which is the interesting duration for it: a
			-- HoldFire technician that merely scans SLOWLY would have passed the earlier check.
			if DerrickQuiet.Owner == USA then
				Test.Fail("[acn] fail: the HoldFire technician captured its derrick eventually, " ..
					"after " .. (SettleTicks + RoamTicks) .. " ticks. The off switch delays the " ..
					"behaviour rather than preventing it, which is worse than not having one")
				return
			end

			Test.Pass("[acn] idle technicians captured the near neutral and near enemy derricks " ..
				"unbidden, stayed off the one they were told to hold fire on, respected the 8-cell " ..
				"default radius, and reached the 15-cell derrick only once switched to Hunt")
		end)
	end)
end
