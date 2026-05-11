-- AUTO TEST: Counter-battery radar coverage must be removed when the source
-- MSAR dies. Reproduces the MapLayers.RemoveSource bug where non-Vision
-- (Radar / CounterBatteryRadar) sources incremented their per-cell counts on
-- AddSource but were never decremented on remove, leaving coverage permanent.
--
-- Pre-fix: probe cell still reports CBR cover after MSAR dies → FAIL.
-- Post-fix: probe cell reports cover while alive, no cover after death → PASS.

local USA = nil
local ProbeCell = CPos.New(33, 24)  -- 10 cells south of MSAR (which sits at 33,14). MSAR's CBR Range=42c0 so this is well inside.

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	TestHarness.FocusBetween(Msar)
	TestHarness.Select(Msar)

	-- Step 1: confirm CBR cover is present while MSAR is alive and deployed.
	-- Give the trait one tick to register its source on AddedToWorld.
	Trigger.AfterDelay(2, function()
		if Msar.IsDead then
			Test.Fail("Msar died before probe — map setup wrong?")
			return
		end

		if not Test.HasCounterBatteryRadarCover(USA, ProbeCell) then
			Test.Fail("MSAR alive + deployed but CBR cover missing at probe cell — sanity precondition failed")
			return
		end

		-- Step 2: kill MSAR, then poll until coverage clears.
		Msar.Kill()

		TestHarness.AssertWithin(3, function()
			-- Pass when coverage is gone.
			if not Test.HasCounterBatteryRadarCover(USA, ProbeCell) then
				return true
			end
			-- Otherwise keep polling until timeout.
			return false
		end, "CBR cover at probe cell still present after MSAR died (RemoveSource didn't decrement counterBatteryRadarCount)")
	end)
end
