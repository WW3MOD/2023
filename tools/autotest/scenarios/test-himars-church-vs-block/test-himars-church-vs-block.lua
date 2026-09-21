-- AUTO TEST: the central claim of the 2026-09-15 garrison retune, end to end.
--
-- THE CLAIM. "A HIMARS missile into a wooden church is a pretty bad day for the church"
-- (user ruling) while a concrete apartment block holds. The retune had to carry that in HP
-- rather than in armour type, because HIMARS' 36000 Warhead@Target and both tank rounds have
-- no Versus table at all -- only the 7000 Warhead@Shockwave discriminates, so the whole armour
-- vocabulary is worth 13% of one hit. This test is what makes that a measurement instead of
-- arithmetic on a warhead file.
--
-- THE NUMBERS BEING PINNED (WORKSPACE/audit/260915-garrison-tuning-table.md):
--   one HIMARS on Light   = 36000 + 2500 + 7000*80%  = 44100
--   one HIMARS on Concrete= 36000 + 2500 + 7000*25%  = 40250
--   V01      38000 HP Light    -> 44100 > 38000, so it bottoms out
--   RUSHOUSE 120000 HP Concrete-> 40250, so ~79750 left, about 66%
--
-- IT BOTTOMS OUT AT 1, NOT 0. GarrisonManager.Indestructible defaults true
-- (GarrisonManager.cs:85) and nothing overrides it on any garrisonable actor, so IDamageFloor
-- clamps both of these at one hit point forever. "Rubbled", not "destroyed".
--
-- WHY THE HEALTH IS LATCHED. HIMARS BurstWait is 250 ticks, so a launcher left alone fires
-- again ten seconds later and a poll that happens to run after the second salvo would see the
-- block at ~39500 and fail a correct build. Both readings are taken on the FIRST tick at which
-- each target has taken any damage, and held.

local ChurchStart, BlockStart
local ChurchSeen, BlockSeen = nil, nil
local Reported = false

WorldLoaded = function()
	ChurchStart = Church.Health
	BlockStart = Block.Health

	TestHarness.FocusBetween(Church, Block)
	Test.SetZoom(1)

	-- allowMove false: both launchers are already inside Range and outside MinRange, and a
	-- launcher that repositions changes the impact geometry between the two lanes.
	LauncherB.Attack(Church, false, true)
	LauncherA.Attack(Block, false, true)

	Trigger.OnTick(function()
		if Reported then
			return
		end

		if ChurchSeen == nil and not Church.IsDead and Church.Health < ChurchStart then
			ChurchSeen = Church.Health
		end

		if BlockSeen == nil and not Block.IsDead and Block.Health < BlockStart then
			BlockSeen = Block.Health
		end

		if ChurchSeen ~= nil and BlockSeen ~= nil then
			Reported = true
			Trigger.AfterDelay(2, Verdict)
		end
	end)

	-- Generous: the missile has to fly 20 cells. Fails with the census rather than timing out
	-- silently, so a run that produced no impact says which lane was quiet.
	TestHarness.AssertWithin(40, function() return Reported end, function()
		return "no impact within 40s — church " ..
			(Church.IsDead and "DEAD" or tostring(Church.Health)) .. "/" .. ChurchStart ..
			", block " .. (Block.IsDead and "DEAD" or tostring(Block.Health)) .. "/" .. BlockStart
	end)
end

function Verdict()
	local detail = "church " .. tostring(ChurchSeen) .. "/" .. ChurchStart ..
		", block " .. tostring(BlockSeen) .. "/" .. BlockStart

	TestHarness.Screenshot("01-after-one-salvo",
		"expects: the church at its rubble state (heavy damage overlay, burning), the apartment " ..
		"block visibly damaged but standing")

	Trigger.AfterDelay(25, function()
		if ChurchSeen > 1000 then
			Test.Fail("one HIMARS did NOT rubble the church: " .. detail ..
				" — expected it to reach the 1 HP Indestructible floor (38000 HP Light vs 44100 a hit)")
			return
		end

		if BlockSeen < math.floor(BlockStart / 2) then
			Test.Fail("one HIMARS took more than half the apartment block: " .. detail ..
				" — expected about 79750 of 120000 left (Concrete, 40250 a hit)")
			return
		end

		Test.Pass(detail)
	end)
end
