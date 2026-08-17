-- AUTO TEST: stage the two frames that show whether a garrisoned soldier's
-- suppression is legible to the player who owns him.
--
-- Suppression is fully simulated on garrisoned soldiers — AttackGarrisoned fires
-- the soldier's OWN Armament, which reads its burst/burst-wait/inaccuracy
-- modifiers off the soldier (Armament.cs:253-258), so the ^SuppressionEffects
-- ladder in infantry.yaml already degrades garrison fire. Until now none of it
-- was drawn: the building's pip grid had damage/class/ammo rows and no
-- suppression row, and the soldier's own ^SuppressionPips carry
-- RequiresSelection: true while he is a 40%-alpha ghost on the building's cell.
--
-- Beats:
--   01-unsuppressed — three riflemen manning tower ports, suppression 0.
--                     The new row draws nothing at level 0, and three occupants
--                     fit one slot row, so this frame is also what the pre-change
--                     build renders. It is the "before" of the pair.
--   02-suppressed   — same frame with 45 suppression granted to each. Below the
--                     recall threshold (60) on purpose, so the soldiers stay at
--                     their ports and the port rows stay populated.
--
-- The verdict here only covers the staging (soldiers really reached ports, and
-- nothing died). Whether the pips and panel text are correct is judged by
-- reading the two PNGs — see DOCS/recipes/SCREENSHOT.md.

-- Below GarrisonManager's SuppressionRecallThreshold (60) so nobody is recalled
-- mid-capture. Lands in tier 5 of 10, whose pip is mid-orange (#E79228) — clearly
-- separated in hue from tier 1's pale yellow, which matters because the ten pip
-- frames are the same chevron and differ only in colour.
local SuppressionToGrant = 45

local Squad = nil
local HouseSquad = nil

-- Returns atPorts, inShelter, outside, dead.
--
-- DeployToPort does SetPosition(soldier, self.Location) — a port soldier occupies the
-- BUILDING's own cell while in-world. A soldier in shelter has been removed from the
-- world entirely. A soldier still walking is in-world on some other cell.
--
-- Count all four states separately and report them. Two earlier runs of this scenario
-- were lost to a gate that collapsed them into one number: "0 soldiers here" reads
-- identically whether they are walking, in shelter, or dead, and the runs could not be
-- told apart afterwards. A failing test must say which state it actually found.
local function GarrisonCensus(squad, building)
	local atPorts, inShelter, outside, dead = 0, 0, 0, 0
	for _, s in ipairs(squad) do
		if s.IsDead then
			dead = dead + 1
		elseif not s.IsInWorld then
			inShelter = inShelter + 1
		elseif s.Location.X == building.Location.X and s.Location.Y == building.Location.Y then
			atPorts = atPorts + 1
		else
			outside = outside + 1
		end
	end

	return atPorts, inShelter, outside, dead
end

local function CensusText(label, squad, building)
	local atPorts, inShelter, outside, dead = GarrisonCensus(squad, building)
	return label .. ": " .. atPorts .. " at ports, " .. inShelter .. " in shelter, " ..
		outside .. " still outside, " .. dead .. " dead"
end

WorldLoaded = function()
	Squad = { R1, R2, R3 }
	HouseSquad = { H1, H2, H3, H4, H5, H6 }

	TestHarness.FocusBetween(Tower, House)
	Test.SetZoom(2)

	for _, s in ipairs(Squad) do
		s.EnterTransport(Tower)
	end

	-- The house squad never enters the verdict. Its only job is to put a six-occupant
	-- (two-slot-row) pip grid in the same frame, so the capture shows whether the taller
	-- slot crowds the building sprite. Shelter occupants render pips too, so this works
	-- even if none of them are ever deployed to a port.
	for _, s in ipairs(HouseSquad) do
		s.EnterTransport(House)
	end

	-- Selecting the tower both raises the GARRISON_PANEL and switches the pip grid
	-- to its selected scale/alpha, so one frame carries both readouts.
	Trigger.AfterDelay(225, function()
		TestHarness.Select(Tower)
	end)

	Trigger.AfterDelay(250, function()
		-- Fail only if nobody got INSIDE. Port deployment needs a confirmed target and
		-- is the fragile half; the pip grid and the panel's shelter rows render for
		-- shelter occupants regardless, so a shelter-only garrison is still worth
		-- photographing. Never fail in a way that produces no pictures.
		local atPorts, inShelter = GarrisonCensus(Squad, Tower)
		if atPorts + inShelter == 0 then
			Test.Fail("nobody got inside the tower within 10s — " ..
				CensusText("tower squad", Squad, Tower) .. "; " ..
				CensusText("house squad", HouseSquad, House))
			return
		end

		TestHarness.Screenshot("01-unsuppressed",
			"expects: guard tower selected, pip grid beneath it showing class + ammo rows and " ..
			"NO suppression pip; garrison panel bottom-right listing ports with '% cover'; " ..
			"civilian building to the left carrying a six-slot pip grid, also with no suppression pip")
	end)

	Trigger.AfterDelay(275, function()
		for _, s in ipairs(Squad) do
			if not s.IsDead then
				for _ = 1, SuppressionToGrant do
					s.GrantCondition("suppressed")
				end
			end
		end

		for _, s in ipairs(HouseSquad) do
			if not s.IsDead then
				for _ = 1, SuppressionToGrant do
					s.GrantCondition("suppressed")
				end
			end
		end
	end)

	-- Well clear of the grant so the render path has seen the new condition count.
	Trigger.AfterDelay(300, function()
		TestHarness.Screenshot("02-suppressed",
			"expects: same frame, now with a suppression pip as the bottom row of each " ..
			"occupied pip slot on BOTH buildings, and the garrison panel port rows reading " ..
			"'SUPP 45' where they read '% cover' in 01. Check the civilian building's two-row " ..
			"grid does not ride up over its roof")
	end)

	-- Test.Screenshot is ASYNC: the pixels are read at the end of the NEXT RenderTick
	-- (Game.cs:926-930). Test.Pass begins teardown, so it counts as a state change and
	-- needs its own delay after the last capture, exactly like a world mutation would.
	Trigger.AfterDelay(325, function()
		Test.Pass(CensusText("tower", Squad, Tower) .. "; " ..
			CensusText("house", HouseSquad, House))
	end)
end
