-- AUTO TEST: an idle technician takes a nearby capturable structure without being told to, and the
-- stances control how eager it is.
--
-- THE DESIGN BEING PINNED. AutoTarget carries TWO stance enums and they answer different questions,
-- which is why "Fire at will or Hunt?" has the answer "both, on different axes":
--
--     MAY it act?          UnitStance        HoldFire / Ambush / FireAtWill
--     HOW FAR will it go?  EngagementStance  HoldPosition / Defensive / Hunt
--
-- A fresh unit is FireAtWill + Defensive, so the behaviour is ON by default at the conservative
-- 8-cell radius with nothing configured. HoldFire is the per-unit off switch; Hunt widens to 20.
--
-- ------------------------------------------------------------------------------------------------
-- WHAT THE FIRST RUN OF THIS SCENARIO GOT WRONG (2026-09-03), because the lesson is in the shape of
-- the assertions and not only in the map.
--
-- The map's arms overlapped: TechRoam sat 7.81 cells from DerrickQuiet, inside its own 8-cell
-- radius, so IT captured the derrick the HoldFire technician was supposed to leave alone. The
-- verdict read "a technician on the HoldFire stance captured a derrick 5 cells away" and was
-- reported as a defect in shipped code. It was not: the off switch was never exercised.
--
-- The assertion was unfalsifiable in the direction that mattered. It read OWNERSHIP -- "is this
-- derrick USA-owned" -- which cannot say WHICH technician took it. So this version asserts
-- ATTRIBUTION as well, using the fact that a successful capture CONSUMES the captor
-- (^CapturesNeutralBuildings sets ConsumedByCapture, and EnterBehaviour defaults to Dispose):
--
--     a technician that captured something is DEAD;  one that captured nothing is ALIVE.
--
-- That makes "TechHoldFire is still alive" a direct statement about TechHoldFire rather than an
-- inference from who owns what. The trait also now writes an [autocap] line to debug.log naming the
-- captor, the target and the radius that admitted it, so the next failure is attributable from the
-- log without re-deriving any of this.
--
-- EVERY FAILURE REPORTS THE WHOLE BOARD. The first version returned on the first failed check, so a
-- run told us about exactly one derrick. Report() below dumps all four derricks and all four
-- technicians into every message: one run now distinguishes "one arm broke" from "nothing worked".

-- Budgeted in TICKS deliberately. TestHarness.TicksPerSecond is 25 while the game runs at 16.67 and
-- is documented as left that way on purpose (test-helpers.lua:16-25), so a "seconds" budget
-- silently buys 1.5x more real time than it reads.
--
-- ^Infantry Mobile Speed is 25 units/tick and a cell is 1024 => 40.96 TICKS PER CELL.
--   6-cell arms: 5 cells approach (205) + CaptureDelay 20 + entering ~60 + first scan <=40 = ~330
--  12.51-cell arm: 11 cells approach (451) + 20 + 60 + 40                                 = ~570
local SettleTicks = 700      -- ~2.1x the ~330 the 6-cell arms need.
local RoamTicks = 1100       -- ~1.9x the ~570 the 12-cell Hunt walk needs.

local function Alive(actor)
	return not actor.IsDead
end

local function Held(structure)
	return tostring(structure.Owner.Name)
end

-- The whole board, appended to every failure so one run tells the entire story rather than the
-- first thing that went wrong. Technician liveness is the attribution channel: dead means it
-- captured something, alive means it did not.
local function Report()
	return " [board] DerrickNear=" .. Held(DerrickNear) ..
		" DerrickEnemy=" .. Held(DerrickEnemy) ..
		" DerrickQuiet=" .. Held(DerrickQuiet) ..
		" DerrickFar=" .. Held(DerrickFar) ..
		" | consumed(=captured something): TechDefault=" .. tostring(not Alive(TechDefault)) ..
		" TechEnemy=" .. tostring(not Alive(TechEnemy)) ..
		" TechHoldFire=" .. tostring(not Alive(TechHoldFire)) ..
		" TechRoam=" .. tostring(not Alive(TechRoam)) ..
		". Cross-reference the [autocap] lines in debug.log, which name the captor and the radius."
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")

	TestHarness.FocusBetween(TechDefault, TechRoam)

	-- ARM 2 SETUP. Must happen before the first scan can fire. `Stance` is the FIRE stance, the axis
	-- that gates the behaviour outright.
	TechHoldFire.Stance = "HoldFire"
	if TechHoldFire.Stance ~= "HoldFire" then
		Test.Fail("[acn] setup invalid: could not put TechHoldFire on the HoldFire stance, so the " ..
			"off-switch arm would pass vacuously")
		return
	end

	-- Assert the OTHER technicians are on the shipped defaults rather than assuming it. The whole
	-- "default is that they do capture" claim rests on this being FireAtWill.
	if TechDefault.Stance ~= "FireAtWill" then
		Test.Fail("[acn] setup invalid: a fresh technician is on the '" .. tostring(TechDefault.Stance) ..
			"' fire stance, not FireAtWill. The default-ON behaviour this test exists to verify is " ..
			"gated on that default, so the premise has moved")
		return
	end

	Trigger.AfterDelay(SettleTicks, function()
		TestHarness.Screenshot("acn-settled",
			"DerrickNear and DerrickEnemy should be USA; DerrickQuiet and DerrickFar untouched")

		-- ARM 1: the headline behaviour. Nobody ordered this.
		if DerrickNear.Owner ~= USA then
			Test.Fail("[acn] fail: an idle technician 6 cells from a neutral derrick did not capture " ..
				"it on its own within " .. SettleTicks .. " ticks. 6 cells is inside the 8-cell " ..
				"Defensive radius and the technician was on the default FireAtWill stance, so either " ..
				"the trait is not on TECN, the idle scan is not firing, or the budget is too tight " ..
				"(a 6-cell arm needs ~330 ticks at 40.96 ticks/cell)." .. Report())
			return
		end

		if Alive(TechDefault) then
			Test.Fail("[acn] fail: DerrickNear changed hands but TechDefault was not consumed, so " ..
				"something OTHER than the technician under test captured it. A capture consumes the " ..
				"captor, so the intended captor must be dead." .. Report())
			return
		end

		-- ARM 4: the same thing on an enemy-held structure.
		if DerrickEnemy.Owner ~= USA then
			Test.Fail("[acn] fail: an idle technician 6 cells from an ENEMY derrick did not capture " ..
				"it. The brief was explicit that this applies to enemy structures as well as neutral " ..
				"ones; if arm 1 passed and this did not, the scan is filtering on owner." .. Report())
			return
		end

		-- ARM 2: the off switch. THE assertion this scenario exists for, and the one the first run
		-- could not actually make. Both halves are required: the derrick untaken AND the technician
		-- still alive, because either alone is satisfiable by the wrong unit doing the work.
		if DerrickQuiet.Owner == USA or not Alive(TechHoldFire) then
			Test.Fail("[acn] fail: the HoldFire arm broke. DerrickQuiet is owned by " ..
				Held(DerrickQuiet) .. " and TechHoldFire is " ..
				(Alive(TechHoldFire) and "alive" or "consumed") .. ". HoldFire is the per-unit off " ..
				"switch -- the behaviour ships ON, so a broken off switch means the player has no " ..
				"way to stop a technician wandering into a building they were saving. NOTE this " ..
				"derrick is 26.73 cells from the nearest other technician, outside even the Hunt " ..
				"radius, so unlike the first version of this map no other unit can reach it." .. Report())
			return
		end

		-- ARM 3, first half: the leash binds. 12.51 cells is outside the 8-cell Defensive radius,
		-- TechRoam has NO other candidate within 8, so this is a real statement about the radius
		-- rather than about TechRoam being busy elsewhere.
		if DerrickFar.Owner == USA then
			Test.Fail("[acn] fail: a technician on the DEFENSIVE stance captured a derrick 12.51 cells " ..
				"away, outside its 8-cell radius. The brief was explicit that a technician should " ..
				"take what is near it and not go far to find targets, so a radius that does not bind " ..
				"is a real failure and not harmless keenness." .. Report())
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
			TestHarness.Screenshot("acn-hunt", "DerrickFar should now be USA-owned after Hunt")

			if DerrickFar.Owner ~= USA then
				Test.Fail("[acn] fail: after switching to the Hunt engagement stance, a technician " ..
					"still did not reach a derrick 12.51 cells away in " .. RoamTicks .. " ticks. " ..
					"That is inside the 20-cell Hunt radius and needs ~570 ticks at 40.96 " ..
					"ticks/cell, so the budget is not the explanation. Hunt is the only way the " ..
					"player can ask for more eagerness; if it reaches no further than Defensive the " ..
					"stance grading conveys nothing." .. Report())
				return
			end

			-- Re-checked at the END as well as above. The off-switch arm has now had
			-- SettleTicks + RoamTicks of idle time, which is the interesting duration for it: a
			-- HoldFire technician that merely scanned SLOWLY would have passed the earlier check.
			if DerrickQuiet.Owner == USA or not Alive(TechHoldFire) then
				Test.Fail("[acn] fail: the HoldFire arm broke eventually, after " ..
					(SettleTicks + RoamTicks) .. " ticks. The off switch delays the behaviour rather " ..
					"than preventing it, which is worse than not having one." .. Report())
				return
			end

			Test.Pass("[acn] idle technicians captured the near neutral and near enemy derricks " ..
				"unbidden and were consumed doing it, the HoldFire technician took nothing and is " ..
				"still alive, the 8-cell default radius held a derrick at 12.51 cells out of reach, and " ..
				"switching to Hunt brought it in range")
		end)
	end)
end
