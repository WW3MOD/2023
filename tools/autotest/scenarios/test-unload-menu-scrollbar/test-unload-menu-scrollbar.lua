-- AUTO TEST — the unload menu must be HONEST about whether it is showing you everything.
--
-- WHY THIS IS A SEPARATE SCENARIO FROM test-unload-menu-classes. That one asserts
-- clip >= content: nothing is cut off. It is a statement about one window size, and below
-- roughly 578px of screen height it fails — correctly, because at that size the 24 rows
-- genuinely do not fit. That makes it useless for looking AT the short-window case: it
-- returns before its own screenshot, so no capture of the clipped menu exists.
--
-- The defect this covers is the other half. Clipping is survivable — the wheel still
-- scrolls a panel whose ScrollBar is Hidden (ScrollPanelWidget handles Scroll regardless),
-- so the rows past the cap stay reachable. What was missing was any way for a player to
-- KNOW that. So the invariant is not "never clip", it is "clip only where you say so":
--
--     bar == 1  if and only if  clip < content
--
-- which is true on both sides of the cliff, and is therefore the assertion to run at a
-- deliberately short window size.
--
-- THE SECOND ASSERTION IS THE ONE THAT SANK THE FIRST ATTEMPT. A scrollbar was tried here
-- before and removed: ScrollPanelWidget draws a right-hand bar without insetting the rows
-- (ChildOrigin, ScrollPanelWidget.cs:236), so it landed on top of the count column and hid
-- the 'x1's entirely. Refresh now widens the menu by the bar's width to give it a gutter.
-- barleft >= countright is that gutter, in a number — without it this test would pass on a
-- build that draws the bar straight through the counts.
--
-- WHY THE CAPTURE GETS ITS OWN DELAY. Test.Screenshot arms a grab sampled at the end of the
-- NEXT RenderTick, so anything on the following line lands in the photograph instead of the
-- state that was asserted. That bit this project on 2026-08-17.

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

		-- A short load would shrink the content below the cap and quietly turn this into a
		-- test of the non-overflowing branch only, whatever window size it was asked for.
		local carried = BigTransport.PassengerCount
		if carried ~= ExpectedRows then
			Test.Fail("the Chinook holds " .. tostring(carried) .. " passengers, expected " ..
				tostring(ExpectedRows) .. " — one man per class, so the class set is incomplete")
			return
		end

		TestHarness.Select(BigTransport)

		if not Test.PressHotkey("UnloadMenu") then
			Test.Fail("the UnloadMenu hotkey was not consumed — the menu did not open")
			return
		end

		-- Delayed: a retarget detaches the old menu through Game.RunAfterTick, so for one
		-- frame both are attached and the state reads '2:N'.
		Trigger.AfterDelay(DateTime.Seconds(1), function()
			local geometry = Test.GetUnloadMenuGeometry()
			local content = fieldOf(geometry, "content")
			local clip = fieldOf(geometry, "clip")
			local bar = fieldOf(geometry, "bar")
			local barleft = fieldOf(geometry, "barleft")
			local countright = fieldOf(geometry, "countright")

			if content == nil or clip == nil or bar == nil or barleft == nil or countright == nil then
				Test.Fail("could not read unload menu geometry, got '" .. tostring(geometry) .. "'")
				return
			end

			print("[unload-scrollbar] geometry: " .. geometry)

			local clipped = clip < content

			if clipped and bar ~= 1 then
				Test.Fail("the list is clipped (" .. tostring(clip) .. "px of panel for " ..
					tostring(content) .. "px of rows) but no scrollbar is shown — the rows past the " ..
					"cap are reachable by wheel and nothing on screen says so; " .. geometry)
				return
			end

			if not clipped and bar ~= 0 then
				Test.Fail("the whole list fits (" .. tostring(clip) .. "px of panel for " ..
					tostring(content) .. "px of rows) but a scrollbar is shown anyway; " .. geometry)
				return
			end

			if bar == 1 and barleft < countright then
				Test.Fail("the scrollbar starts at x=" .. tostring(barleft) ..
					" but the count column runs to x=" .. tostring(countright) ..
					" — the bar is drawn over the counts, which is why the first one was removed; " ..
					geometry)
				return
			end

			local shot = clipped and "01-menu-clipped-with-scrollbar" or "01-menu-fits-no-scrollbar"
			local note = clipped
				and ("expects: the floating menu headed 'CHINOOK 24/36' is as tall as the window " ..
					"allows and has a SCROLLBAR down its right-hand edge — up arrow, down arrow and a " ..
					"thumb noticeably shorter than the track. Every visible row must still show its " ..
					"right-aligned 'x1' count: the bar sits beside the counts, not over them. The last " ..
					"row at the bottom may be cut mid-row; that is the point, and the bar is what " ..
					"admits it.")
				or ("expects: the floating menu headed 'CHINOOK 24/36' lists all 24 class rows with " ..
					"NO scrollbar anywhere on it, each row showing a name, an ALL chip and 'x1'. " ..
					"Nothing cut off at the bottom.")

			TestHarness.Screenshot(shot, note)

			Trigger.AfterDelay(DateTime.Seconds(1), function()
				Test.Pass("unload menu overflow state is advertised honestly; " .. geometry)
			end)
		end)
	end)
end
