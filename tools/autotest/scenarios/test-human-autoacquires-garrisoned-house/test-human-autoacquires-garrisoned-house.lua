-- AUTO TEST: a human unit on FireAtWill auto-acquires a garrisoned house.
--
-- This closes the gap the sibling scenario (test-bot-damages-garrisoned-building) states in its own
-- Watch: that one proves an ORDERED non-force attack reaches a garrisoned building, but it runs at
-- DEFCON 2 -- the lever that keeps its garrison in shelter -- and DEFCON 2 holds autotargeting for
-- everyone, so auto-acquisition was untested. Layout and the range-gap instrument are in map.yaml.
--
-- NO ORDER IS EVER ISSUED TO THE PROBE. It is created, and that is all. Anything it does afterwards
-- it decided by itself, which is the entire claim.
--
-- WHY NOT HP. A manned port soldier stands on the BUILDING'S OWN CELL (DeployToPort does
-- SetPosition(soldier, self.Location)), so splash aimed at him moves the building's HP -- and
-- warhead damage never consults RequiresForceFire, which is an AttackBase targeting gate rather than
-- a damage-layer one. An HP assertion would pass on a build that still had the flag. Two independent
-- guards replace it: the observable is the AUTOMATIC-coloured target line, and the ten men are
-- re-counted every tick so a deployed port can never be mistaken for the building.
--
-- EXPECTED RED with RequiresForceFire restored on Targetable@WhenGarrisoned:
-- AutoTarget.ChooseTarget calls ab.ChooseArmamentsForTarget(target, false) on every candidate and
-- `continue`s when it comes back empty (AutoTarget.cs:1502-1509), which for a force-fire-only target
-- it always does (AttackBase.cs:442). The house is therefore never even scored -- this is the
-- "never selected" half of the finding, as opposed to the sibling's "selected then dropped". The run
-- reports "the abrams never auto-acquired the house ... automatic target line: (empty), activity:
-- (idle)", with the house confirmed Russia-owned, at full HP and holding all ten men.

local HOUSE_W, HOUSE_H = 2, 2      -- v01 is 2x2; Location is its top-left cell
local GARRISON_SIZE = 10           -- Cargo MaxWeight is 10
local GARRISON_TICKS = 350         -- walk in; nothing is measured during it
local ACQUIRE_SECONDS = 30         -- AutoTarget rescans on an interval; this is many times over

Garrison = {}
Probe = nil

local function InHouseFootprint(c)
	return c.X >= House.Location.X and c.X < House.Location.X + HOUSE_W
		and c.Y >= House.Location.Y and c.Y < House.Location.Y + HOUSE_H
end

-- How many of the ten are standing in the world. MUST stay zero: a port soldier occupies the
-- house's own cell, so one deployed man would make the target line ambiguous and this test would be
-- measuring nothing. PassengerCount is the positive half of the same question and is checked beside
-- it -- IsDead reads true for a Cargo occupant and cannot tell shelter from casualty on its own.
local function InWorldCount()
	local n = 0
	for _, s in ipairs(Garrison) do
		if not s.IsDead and s.IsInWorld then n = n + 1 end
	end

	return n
end

local function LineCells()
	if Probe == nil or Probe.IsDead then
		return "(probe gone)"
	end

	local cells = Test.GetAutomaticTargetLineCells(Probe)
	if #cells == 0 then
		return "(empty)"
	end

	local parts = {}
	for i, c in ipairs(cells) do
		parts[i] = c.X .. "," .. c.Y
	end

	return table.concat(parts, " ")
end

local function Census()
	return string.format(
		"house at %d,%d: %d/%d HP, owner %s, %d in shelter, %d men standing in the world | "
		.. "automatic target line: %s | activity: %s",
		House.Location.X, House.Location.Y, House.Health, House.MaxHealth, House.Owner.Name,
		House.PassengerCount, InWorldCount(), LineCells(),
		Probe ~= nil and not Probe.IsDead and Test.ActivityChain(Probe) or "(probe gone)")
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	-- East of the house, so they never cross the cell the abrams will later occupy.
	for i = 1, GARRISON_SIZE do
		Garrison[i] = Actor.Create("e1", true, {
			Owner = Russia,
			Location = CPos.New(42 + ((i - 1) % 5), 31 + math.floor((i - 1) / 5)),
		})
	end

	for _, s in ipairs(Garrison) do s.EnterTransport(House) end

	TestHarness.FocusBetween(House)

	-- PRE-ASSERTION: nothing may be holding fire. Test.DefconLevel is DefconEscalationState.NoLevel
	-- (0) in an ordinary Skirmish; any other value means a DefconEscalation slipped in, and at level
	-- 2 the abrams' autotargeting would be held by the phase rather than by anything this test is
	-- about -- a RED that looks exactly like the finding and is not.
	Trigger.AfterDelay(1, function()
		local level = Test.DefconLevel()
		if level ~= 0 then
			Test.Fail(string.format(
				"this scenario must run as an ordinary Skirmish with nothing holding fire, but "
				.. "Test.DefconLevel reads %d. At the hold-fire level AutoTarget refuses to pick a "
				.. "target at all, so a RED here would be the phase and not Targetable@WhenGarrisoned",
				level))
		end
	end)

	Trigger.AfterDelay(GARRISON_TICKS, function()
		-- THE FIXTURE GATE. Each limb fails with its own text so a broken fixture is never read as
		-- the finding.
		if House.Owner.Name ~= "Russia" then
			Test.Fail("the house never changed hands, so it is not a hostile garrison and the "
				.. "`loaded` condition that enables Targetable@WhenGarrisoned may not be granted "
				.. "either -- " .. Census())
			return
		end

		if House.PassengerCount ~= GARRISON_SIZE then
			Test.Fail(string.format(
				"the house holds %d men in shelter, expected all %d -- either a port deployed, "
				.. "which would make the target line ambiguous, or men were lost walking in. %s",
				House.PassengerCount, GARRISON_SIZE, Census()))
			return
		end

		if House.Health ~= House.MaxHealth then
			Test.Fail("the house was already damaged before the probe existed -- " .. Census())
			return
		end

		-- THE PROBE, created only now: while the riflemen were walking in there was no abrams on the
		-- map that could have acquired one of them in the open. Sixteen cells west of the house --
		-- inside TankRound.Abrams' 25c0, outside the garrison's 10c0. No order is given to it.
		Probe = Actor.Create("abrams", true, {
			Owner = USA,
			Location = CPos.New(24, 28),
			Facing = Angle.East,
		})

		if Probe == nil then
			Test.Fail("could not create the probe abrams -- " .. Census())
			return
		end

		TestHarness.FocusBetween(Probe, House)
		TestHarness.Select(Probe)

		TestHarness.AssertWithin(ACQUIRE_SECONDS, function()
			if Probe.IsDead then
				return "the probe died before it acquired anything, which nothing on this map "
					.. "should be able to do -- " .. Census()
			end

			-- THE AMBIGUITY GUARD, checked every tick rather than once. If a port ever deploys, the
			-- man stands on the house's own cell and a target line naming that cell stops meaning
			-- "the building". Fail loudly instead of scoring it.
			if InWorldCount() > 0 then
				return "a garrison soldier deployed to a port, so the house's cell is no longer "
					.. "unambiguous and the target line cannot be attributed. The range gap that "
					.. "was supposed to prevent this (garrison 10c0 vs probe at 16 cells) did not "
					.. "hold -- " .. Census()
			end

			for _, c in ipairs(Test.GetAutomaticTargetLineCells(Probe)) do
				if InHouseFootprint(c) then
					return true
				end
			end

			return false
		end, function()
			return string.format(
				"the abrams never auto-acquired the house within %ds, having been given no order at "
				.. "any point. %s | With RequiresForceFire on Targetable@WhenGarrisoned this is the "
				.. "expected state and the target line is EMPTY rather than pointed elsewhere: "
				.. "ChooseTarget drops the house at its own ChooseArmamentsForTarget call before the "
				.. "candidate is ever scored, so the unit has nothing to engage and stays idle.",
				ACQUIRE_SECONDS, Census())
		end)
	end)
end
