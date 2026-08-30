-- test-critical-no-panic.lua
--
-- A critically damaged technician neither panics nor changes cell.
--
-- Observation window opens only once the man is unambiguously in the Critical damage state, so the
-- whole measurement sits inside the interval where the gate under test is the thing holding him still.
-- Setup is asserted rather than assumed: if he is not actually critical when the window opens, the test
-- fails there and says so, instead of quietly measuring a healthy man who was never going to move.

local ObserveTicks = 300      -- 18s: 300 ticks at the mod's real 16.67 tps (mod.yaml Timestep 60)
local CriticalHp = 40         -- 20% of a technician's 200 HP; Critical is anything under 25%
local CriticalCeiling = 50    -- 25% of 200 — at or below this he is in the Critical band

local startX, startY
local observing = false
local ticks = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Tecn, Tecn)

	-- Phase 1: wound him into Critical. The Health setter inflicts real damage, so this drives the
	-- INotifyDamage path a bullet would — ScaredyCat's panic trigger — rather than editing HP behind
	-- the sim's back.
	Tecn.Health = CriticalHp

	Trigger.AfterDelay(25, function()
		if Tecn.IsDead then
			Test.Fail("technician died before the observation window opened")
			return
		end

		-- Assert the setup took effect. A green run against a man who never reached Critical would
		-- measure nothing at all.
		if Tecn.Health > CriticalCeiling then
			Test.Fail("setup did not take: technician is not in the Critical damage band")
			return
		end

		startX = Tecn.Location.X
		startY = Tecn.Location.Y

		-- Phase 2: a second wound, taken while ALREADY critical. This is the exact event the gate has
		-- to refuse. Without the gate it starts a fresh 250-tick panic, which cancels his activity and
		-- queues a move to an adjacent cell as soon as he goes idle.
		Tecn.Health = Tecn.Health - 2
		observing = true

		print(string.format("[crit-panic] window open hp=%d/%d cell=%d,%d",
			Tecn.Health, Tecn.MaxHealth, startX, startY))
	end)

	-- Failure string is deliberately STATIC: AssertWithin's message argument is evaluated eagerly at
	-- registration, so any counter interpolated here would report its value from before the run began.
	-- Live numbers go to lua.log through the periodic print below.
	TestHarness.AssertWithin(20, function()
		if Tecn.IsDead then
			return "fail: technician died before the observation window closed"
		end

		if not observing then
			return false
		end

		ticks = ticks + 1

		if ticks % 25 == 0 then
			print(string.format("[crit-panic] t=%d hp=%d/%d cell=%d,%d",
				ticks, Tecn.Health, Tecn.MaxHealth, Tecn.Location.X, Tecn.Location.Y))
		end

		-- Actor.Location is Mobile.ToCell, which Move claims via SetLocation BEFORE consulting speed.
		-- So this catches a panic move even though SpeedMultiplier@CriticalDamage pins the travel rate
		-- at zero and the sprite never leaves the old cell.
		if Tecn.Location.X ~= startX or Tecn.Location.Y ~= startY then
			return "fail: critically damaged technician changed cell"
		end

		return ticks >= ObserveTicks
	end, "observation window never completed")
end
