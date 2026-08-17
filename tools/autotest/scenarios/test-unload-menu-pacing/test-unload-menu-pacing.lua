-- AUTO TEST — measure what the unload menu's ALL chip actually costs on a helicopter.
--
-- THE SUSPICION, from reading UnloadCargo. A class row sends one existing
-- UnloadCargoPassenger order per man, which is what keeps the design free of any new
-- order or protocol bump. But each of those becomes its OWN UnloadCargo activity, and
-- for an aircraft OnFirstRun queues a Land and sets takeOffAfterUnload when it was
-- airborne; with a specific passenger, unloadAll is false, so the activity exits after
-- ONE man via Wait(AfterUnloadDelay) then TakeOff. Chinooks set AfterUnloadDelay: 40
-- and never zero BeforeUnloadDelay, so they inherit the engine's 8. If that reading is
-- right, four men cost four land/take-off cycles instead of one.
--
-- THE MEASUREMENT. Two identical Chinooks, four Riflemen each. One is emptied through
-- the menu's ALL chip — the real widget click, so the real orders. The other gets a
-- single bulk Unload, which runs one UnloadCargo with unloadAll true and loops inside
-- itself. Both are timed from the same tick to the tick their hold goes empty. Same
-- aircraft, same men, same start: the only variable is the path, so the ratio is the
-- finding.
--
-- This test does NOT assert that one is faster — that would only restate the code. It
-- asserts both paths EMPTY THE HOLD, which is the part that must not regress, and
-- reports both tick counts in the verdict so the cost is a measured number rather than
-- an inference from reading the activity.

local Carried = 4
local SettleSeconds = 3
local ProbeSeconds = 40
local TimeoutSeconds = 90

local counting = false
local ticks = 0
local menuTicks, bulkTicks, probeTicks, probeStart

-- Declared at file scope, not inside WorldLoaded: ScriptContext caches
-- runtime.Globals["Tick"] once when the script loads (ScriptContext.cs:242), so a Tick
-- assigned later never runs. There is no Trigger.OnTick in this API.
Tick = function()
	if not counting then
		return
	end

	ticks = ticks + 1

	if not MenuChinook.IsDead and menuTicks == nil and probeStart == nil
		and MenuChinook.PassengerCount == 0 then
		menuTicks = ticks
	end

	if not MenuChinook.IsDead and probeStart ~= nil and probeTicks == nil
		and MenuChinook.PassengerCount == 0 then
		probeTicks = ticks - probeStart
	end

	if bulkTicks == nil and not BulkChinook.IsDead and BulkChinook.PassengerCount == 0 then
		bulkTicks = ticks
	end
end

-- If the menu arm stalls, the interesting question is WHY, and the two candidate answers
-- want opposite fixes. Either the per-man order path is broken, or that particular cell
-- simply cannot be unloaded onto — CanUnload gates on terrain type, aircraft.CanLand and
-- adjacent-cell entry, all of which are properties of where the transport is standing, and
-- the two arms stand in different places. So the stall is probed by giving the SAME stuck
-- transport a bulk Unload. If it then empties, the cell was always fine and the per-man
-- path is at fault; if it stays stuck, the cell is the confound and this comparison is void.
local function probeStalledMenuArm()
	if menuTicks ~= nil or MenuChinook.IsDead then
		return
	end

	probeStart = ticks
	Test.IssueDeploy(MenuChinook)
end

local function finish()
	if probeStart ~= nil then
		if probeTicks ~= nil then
			Test.Fail(string.format(
				"a single UnloadCargoPassenger order does not drop anyone from an aircraft. One row " ..
				"click left all %d men aboard after %ds, and a bulk Unload issued to that SAME " ..
				"Chinook at that SAME cell then emptied it in %d ticks — so the cell was always " ..
				"unloadable and the per-man order path itself is what fails. This is the order the " ..
				"old EJECT button sent too, so it predates the menu. Reference bulk arm: %s ticks.",
				Carried, ProbeSeconds, probeTicks, tostring(bulkTicks)))
		else
			Test.Fail(string.format(
				"inconclusive: the menu arm stalled with %d aboard, but a bulk Unload on that same " ..
				"Chinook did not empty it either, so its cell is the confound rather than the order " ..
				"path. Reference bulk arm at the other cell: %s ticks. Re-site the menu Chinook.",
				MenuChinook.PassengerCount, tostring(bulkTicks)))
		end

		return
	end

	if menuTicks == nil or bulkTicks == nil then
		local function state(name, done, transport)
			if done ~= nil then
				return name .. " emptied in " .. tostring(done) .. " ticks"
			end

			return name .. " STUCK with " .. tostring(transport.PassengerCount) .. " still aboard"
		end

		Test.Fail("timed out after " .. tostring(TimeoutSeconds) .. "s — " ..
			state("menu ALL", menuTicks, MenuChinook) .. "; " ..
			state("bulk Unload", bulkTicks, BulkChinook))
		return
	end

	-- Both emptied, which is the assertion. The ratio is the reportable finding: a menu
	-- path close to the bulk path means the per-man activities coalesce; a large multiple
	-- means every man is paying for his own landing.
	Test.Pass(string.format(
		"both paths emptied a %d-man Chinook. Menu ALL (one UnloadCargoPassenger per man): %d " ..
		"ticks. Bulk Unload (one activity): %d ticks. Menu costs %.1fx the bulk path — %d extra " ..
		"ticks for the same %d men, which is the per-man land/take-off cycle made visible.",
		Carried, menuTicks, bulkTicks, menuTicks / bulkTicks, menuTicks - bulkTicks, Carried))
end

WorldLoaded = function()
	Camera.Position = MenuChinook.CenterPosition

	Trigger.AfterDelay(DateTime.Seconds(SettleSeconds), function()
		if MenuChinook.PassengerCount ~= Carried or BulkChinook.PassengerCount ~= Carried then
			Test.Fail("expected " .. tostring(Carried) .. " aboard each Chinook, got " ..
				tostring(MenuChinook.PassengerCount) .. " and " .. tostring(BulkChinook.PassengerCount))
			return
		end

		-- Menu arm: open on the real hotkey, then hit ALL on the single class row.
		TestHarness.Select(MenuChinook)
		if not Test.PressHotkey("UnloadMenu") then
			Test.Fail("the unload menu did not open on the Chinook")
			return
		end

		Trigger.AfterDelay(DateTime.Seconds(1), function()
			local state = Test.GetUnloadMenuState()
			if state ~= "1:1" then
				Test.Fail("expected one menu listing one class row, got '" .. tostring(state) .. "'")
				return
			end

			counting = true

			if not Test.ClickUnloadMenuRow(0, true) then
				Test.Fail("could not click the ALL chip on the Rifleman row")
				return
			end

			-- Bulk arm: Cargo's deploy order is "Unload", the whole-hold path.
			Test.IssueDeploy(BulkChinook)

			Trigger.AfterDelay(DateTime.Seconds(ProbeSeconds), probeStalledMenuArm)
			Trigger.AfterDelay(DateTime.Seconds(TimeoutSeconds), finish)
		end)
	end)
end
