-- AUTO TEST — photograph the class-grouped unload menu at both ends of its range.
--
-- Two shots in ONE run, because game launches on this machine are rationed:
--
--   Shot 1, SMALL — a Bradley carrying 3 men across 2 classes. This is the case
--                   Option B is supposed to win: the menu appears under the
--                   pointer instead of at the screen edge, so there is no travel.
--   Shot 2, FULL  — a Chinook carrying 36, its exact MaxWeight, across 9 classes.
--                   This is the case that kills the alternatives. 36 men as
--                   individual rows would be 504px into a 240px panel; 9 class
--                   rows is 198px. If the grouping does not hold here it does not
--                   hold anywhere, and showing only the flattering 3-man shot is
--                   how the previous design pass went wrong.
--
-- WHY THE MENU IS OPENED BY A SYNTHESISED KEY AND NOT A MOUSE CLICK. The menu
-- opens on a hotkey (UnloadMenu, default J) because every mouse gesture in the
-- viewport is already bound — right-click issues orders. Test.PressHotkey pushes
-- a KeyInput through Ui.HandleKeyPress, so it walks the real widget chain in the
-- real order and reads the player's live binding rather than hardcoding J.
--
-- ITS RETURN VALUE IS NOT THE ASSERTION, THOUGH — THAT WAS THIS SCENARIO'S OWN
-- FIRST BUG. A press that DISMISSES an open menu is consumed exactly as happily as
-- one that opens a fresh one, so `true` proved nothing. The first run went green
-- and shot 2 photographed the Bradley's menu with a blank header, because pressing
-- the key right after selecting the Chinook closed the old menu instead of
-- retargeting it. The fix was in the widget logic, not here; what belongs here is
-- an assertion that can tell the two apart, so every shot now checks
-- Test.GetUnloadMenuState() for exactly one attached menu with the expected number
-- of class rows before the shutter opens.
--
-- WHY EVERY CAPTURE GETS A FULL SECOND TO ITSELF. Test.Screenshot does NOT sample
-- pixels when called. It sets Game.takeScreenshot and the grab happens at the end
-- of the NEXT RenderTick, after Ui.Draw() has redrawn the HUD from whatever the
-- state is by then (Game.cs:926-930). A mutation on the following Lua line lands
-- BEFORE the pixels are read, so the shot silently photographs the state you were
-- about to move to. That bit this project on 2026-08-17: a shot labelled "10
-- passengers" passed its PassengerCount == 10 assertion and came out showing 3.
--
-- WHY SHOT 2 DOES NOT CLOSE SHOT 1'S MENU BY HAND. Pressing the key while a menu is
-- open for a DIFFERENT transport retargets it rather than dismissing it, so the
-- Chinook press opens a Chinook menu directly. The row-count assertion is what
-- holds that honest: a stale Bradley menu would report 2 rows where 9 are
-- expected, and a close that failed to detach its widget would report 2 menus.

local SmallPassengers = 3
local FullPassengers = 36
local SettleSeconds = 3

local function menuOpensOn(transport, label, expected)
	if transport.IsDead then
		Test.Fail(label .. " transport died before the capture")
		return false
	end

	local carried = transport.PassengerCount
	if carried ~= expected then
		Test.Fail(label .. " transport holds " .. tostring(carried) .. " passengers, expected " ..
			tostring(expected) .. " — the menu is not in the state this shot is meant to show")
		return false
	end

	TestHarness.Select(transport)

	if Test.GetSelectedCount() ~= 1 then
		Test.Fail(label .. ": selection is " .. tostring(Test.GetSelectedCount()) ..
			" actors, not 1 — the menu only opens for a single owned transport")
		return false
	end

	if not Test.PressHotkey("UnloadMenu") then
		Test.Fail(label .. ": the UnloadMenu hotkey was not consumed — the menu did not open")
		return false
	end

	return true
end

-- Checked on a delay rather than straight after the press, and that delay is load-bearing.
-- Retargeting closes the old menu through Game.RunAfterTick, because Close is also reachable
-- from Tick and detaching a child of Ui.Root while Widget.TickOuter is iterating Children
-- would mutate the collection mid-iteration. So for one frame BOTH menus are attached and an
-- immediate check reads '2:2' — which is what this assertion reported before it was moved
-- here. The new menu is the later sibling, so it draws on top and takes input first; the old
-- one is gone by the next tick, well before the shutter.
local function menuListsRows(label, expectedRows)
	local expectedState = "1:" .. tostring(expectedRows)
	local state = Test.GetUnloadMenuState()
	if state ~= expectedState then
		Test.Fail(label .. ": unload menu state is '" .. tostring(state) .. "', expected '" ..
			expectedState .. "' (menus attached : class rows listed)")
		return false
	end

	return true
end

WorldLoaded = function()
	Camera.Position = SmallTransport.CenterPosition

	Trigger.AfterDelay(DateTime.Seconds(SettleSeconds), function()
		if not menuOpensOn(SmallTransport, "small", SmallPassengers) then
			return
		end

		Trigger.AfterDelay(DateTime.Seconds(1), function()
			if not menuListsRows("small", 2) then
				return
			end

			TestHarness.Screenshot("01-menu-small-3pax",
				"expects: a floating menu, NOT docked to the screen edge, headed 'BRADLEY 3/6', " ..
				"with exactly 2 rows - 'Rifleman x2' and 'AT Specialist x1' - each carrying an " ..
				"ALL chip and a visible count. No per-man rows, no rally buttons, no scrollbar")

			Trigger.AfterDelay(DateTime.Seconds(1), function()
				Camera.Position = BigTransport.CenterPosition

				Trigger.AfterDelay(DateTime.Seconds(1), function()
					if not menuOpensOn(BigTransport, "full", FullPassengers) then
						return
					end

					Trigger.AfterDelay(DateTime.Seconds(1), function()
						if not menuListsRows("full", 9) then
							return
						end

						TestHarness.Screenshot("02-menu-full-36pax",
							"expects: the same menu headed 'CHINOOK 36/36' with 9 class rows - " ..
							"Rifleman x10, Automatic Rifleman x6, AT Specialist x5, Grenadier x4, " ..
							"Medic x3, Team Leader x2, Mortar x2, Combat Engineer x2, Sniper x2, " ..
							"every count legible. The whole list must sit on screen")

						Trigger.AfterDelay(DateTime.Seconds(1), function()
							Test.Pass("captured the unload menu at " .. tostring(SmallPassengers) ..
								" and " .. tostring(FullPassengers) .. " passengers")
						end)
					end)
				end)
			end)
		end)
	end)
end
