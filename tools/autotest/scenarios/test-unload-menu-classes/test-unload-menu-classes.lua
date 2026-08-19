-- AUTO TEST — photograph the class-grouped unload menu at the widest hold the game
-- can legally produce: 24 distinct classes in one Chinook.
--
-- WHY THIS EXISTS. The menu's list was a fixed 380px, sized in its own comment for
-- "16 combat classes". `Cargo Types: Infantry` also admits civilians, a pilot and six
-- ejected-crew keys, so 24 rows / 552px is reachable — and rows past the cap were
-- drawn nowhere while `ScrollBar: Hidden` advertised nothing. The ceiling now derives
-- from screen height. Nobody had ever seen this menu render past 16 rows.
--
-- A ROW COUNT CANNOT SEE THAT BUG, WHICH IS THIS TEST'S WHOLE TRAP. Refresh adds every
-- class row to the list regardless of the panel's height, so Test.GetUnloadMenuState()
-- reports 24 rows on the BROKEN build too. Asserting "1:24" would go green against the
-- exact defect it was written for. What separates the two builds is the CLIP height:
-- Test.GetUnloadMenuGeometry() reports `content` (what the rows need) against `clip`
-- (what the panel gives them), and clip < content IS the bug. That is the assertion.
-- The screenshot is corroboration, not the measurement — SCREENSHOT.md is explicit that
-- counting more than ~5 similar rows in a PNG is not something to trust.
--
-- WHY THE CAPTURE GETS A FULL SECOND TO ITSELF. Test.Screenshot does not sample pixels
-- when called; it arms a grab that happens at the end of the NEXT RenderTick. A mutation
-- on the following line lands before the pixels are read, so the shot photographs the
-- state you were about to move to. That bit this project on 2026-08-17.

local ExpectedRows = 24
local SettleSeconds = 3

local function fieldOf(geometry, name)
	local v = string.match(geometry, name .. "=(%-?%d+)")
	return v and tonumber(v) or nil
end

WorldLoaded = function()
	Camera.Position = BigTransport.CenterPosition

	Trigger.AfterDelay(DateTime.Seconds(SettleSeconds), function()
		if BigTransport.IsDead then
			Test.Fail("the Chinook died before the capture")
			return
		end

		-- The hold is built by Cargo InitialUnits, so a passenger count short of 24 means
		-- a name in rules.yaml did not resolve to an actor and the class set under test was
		-- never assembled. Checked before anything else, because the menu would still open
		-- and still photograph happily with a partial load.
		local carried = BigTransport.PassengerCount
		if carried ~= ExpectedRows then
			Test.Fail("the Chinook holds " .. tostring(carried) .. " passengers, expected " ..
				tostring(ExpectedRows) .. " — one man per class, so the class set is incomplete")
			return
		end

		TestHarness.Select(BigTransport)

		if Test.GetSelectedCount() ~= 1 then
			Test.Fail("selection is " .. tostring(Test.GetSelectedCount()) ..
				" actors, not 1 — the menu only opens for a single owned transport")
			return
		end

		if not Test.PressHotkey("UnloadMenu") then
			Test.Fail("the UnloadMenu hotkey was not consumed — the menu did not open")
			return
		end

		-- Delayed rather than immediate: a retarget detaches the old menu through
		-- Game.RunAfterTick, so for one frame both are attached and the state reads '2:N'.
		Trigger.AfterDelay(DateTime.Seconds(1), function()
			local state = Test.GetUnloadMenuState()
			if state ~= "1:" .. tostring(ExpectedRows) then
				Test.Fail("unload menu state is '" .. tostring(state) .. "', expected '1:" ..
					tostring(ExpectedRows) .. "' (menus attached : class rows listed)")
				return
			end

			local geometry = Test.GetUnloadMenuGeometry()
			local content = fieldOf(geometry, "content")
			local clip = fieldOf(geometry, "clip")
			local panel = fieldOf(geometry, "panel")
			local screen = fieldOf(geometry, "screen")

			if content == nil or clip == nil or panel == nil or screen == nil then
				Test.Fail("could not read unload menu geometry, got '" .. tostring(geometry) .. "'")
				return
			end

			-- The failure string is built eagerly at registration, so it must not carry a
			-- live counter. Everything interpolated here is already final.
			print("[unload-classes] geometry: " .. geometry)

			if clip < content then
				Test.Fail("the list is clipped: " .. tostring(clip) .. "px of panel for " ..
					tostring(content) .. "px of rows — classes past the cap are drawn nowhere")
				return
			end

			if panel > screen then
				Test.Fail("the menu is " .. tostring(panel) .. "px tall on a " .. tostring(screen) ..
					"px screen — it grew past the display instead of being capped by it")
				return
			end

			TestHarness.Screenshot("01-menu-24-classes",
				"expects: one floating menu headed 'CHINOOK 24/36' listing 24 class rows, each " ..
				"with a name, an ALL chip and 'x1'. Rifleman/Grenadier/Sniper/Medic/Pilot etc, " ..
				"then Civilian and Scientist, then Driver, Gunner and Commander appearing TWICE " ..
				"(America and Russia crew share Tooltip names). The whole list must sit on screen " ..
				"with nothing cut off at the bottom and no scrollbar. " ..
				"SECOND THING IN THIS SHOT, unasserted and written without ever having been seen: " ..
				"the sidebar CARGO panel, bottom-right, should show its own manifest for the same " ..
				"Chinook — 9 named class rows with right-aligned 'x1' counts, then a tenth row " ..
				"reading '+15 more' with 'x15'. It must NOT simply stop after ten class names as if " ..
				"that were the whole hold. If the panel is absent, misplaced, overlapping the " ..
				"command bar, or its rows collide with the buttons below them, say so — no capture " ..
				"of it exists yet")

			Trigger.AfterDelay(DateTime.Seconds(1), function()
				Test.Pass("unload menu listed " .. tostring(ExpectedRows) ..
					" classes unclipped; " .. geometry)
			end)
		end)
	end)
end
