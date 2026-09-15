-- AUTO TEST: a human unit on FireAtWill auto-acquires a garrisoned house.
--
-- Closes the gap the bot scenario states in its own Watch: that one proves an ORDERED non-force
-- attack reaches a garrisoned building, but it runs at DEFCON 2 and DEFCON 2 holds autotargeting for
-- everyone, so auto-acquisition was untested. Here nothing holds fire, and the probe is given NO
-- ORDER at any point — anything it does it decided by itself, which is the entire claim.
--
-- WHY NOT HP. A manned port soldier stands on the BUILDING'S OWN CELL (DeployToPort does
-- SetPosition(soldier, self.Location)), so splash aimed at him moves the building's HP — and warhead
-- damage never consults RequiresForceFire, which is an AttackBase targeting gate rather than a
-- damage-layer one. An HP assertion would pass on a build that still had the flag. The observable is
-- the AUTOMATIC-coloured target line (Test.GetAutomaticTargetLineCells), which AttackBase paints only
-- when AutoTarget.IsAutoAcquiredSource(source) holds (AttackBase.cs:786-787).
--
-- NO PORT MAY EVER DEPLOY, or that cell stops being unambiguous. THREE independent guards, after the
-- first version relied on one and could not say whether it had held:
--   1. rules.yaml holds the HOUSE's own AutoTarget at HoldFire. GarrisonManager reads the BUILDING's
--      stance, not the soldiers' (GarrisonManager.cs:796, :865, :1316), and returns before every
--      autonomous deploy path. ^CivBuilding ships no AutoTarget at all, so this trait is added by the
--      scenario — AttackGarrisoned is an AttackFollow and therefore an AttackBase, so it has one to
--      attach to.
--   2. the range gap: ScanForTarget derives an empty port's radius from the SHELTER soldiers'
--      armaments (5.56mm.E3 = 10c0) while TankRound.Abrams reaches 25c0, so the probe at sixteen
--      cells is outside the garrison's envelope and inside its own.
--   3. Test.IsAtGarrisonPort is re-read EVERY TICK and fails the run loudly if either of the above
--      ever stops holding.
--
-- EXPECTED RED with RequiresForceFire restored on Targetable@WhenGarrisoned:
-- AutoTarget.ChooseTarget calls ab.ChooseArmamentsForTarget(target, false) on every candidate and
-- `continue`s when it comes back empty (AutoTarget.cs:1502-1509), which for a force-fire-only target
-- it always does (AttackBase.cs:442). The house is never scored at all — the "never selected" half of
-- the finding — so the target line is EMPTY rather than pointed elsewhere, and the verdict reads
-- "the abrams never auto-acquired the house ... automatic target line: (empty), activity: (idle)".

local HOUSE_W, HOUSE_H = 2, 2      -- v01 is 2x2; Location is its top-left cell
local GARRISON_SIZE = 4            -- well under Cargo MaxWeight 10: slack, and no entry congestion
local GARRISON_TICKS = 350         -- walk in; nothing is measured during it
local ACQUIRE_SECONDS = 30         -- AutoTarget rescans on an interval; many times over

Garrison = {}
Probe = nil

-- THE CENSUS, and every line of it corrects the one this replaces.
--
-- `Actor.IsDead` is `Disposed || health.IsDead` (Actor.cs:76) — purely health and disposal — so a man
-- in a Cargo hold is alive, not disposed, and reads IsDead == FALSE. The first version counted
-- `s.IsDead` under a label reading "not in world", printed 0 while nine men were demonstrably aboard,
-- and produced a verdict that contradicted itself (run 260915_181523). `IsInWorld` IS false for a
-- passenger and that direction is sound: in-world-and-not-loaded really does mean standing outside.
--
-- So ask the TRAITS, not the actor: Test.IsLoadedInto reads Cargo.Passengers, Test.IsAtGarrisonPort
-- reads GarrisonManager.PortStates, and both are true regardless of either flag. The branch ORDER is
-- load-bearing — a port soldier is in-world and not loaded, so he satisfies the "outside" test too
-- and has to be claimed before it.
local function Tally()
	local loaded, ports, outside, dead = 0, 0, 0, 0
	for _, s in ipairs(Garrison) do
		if s.IsDead then dead = dead + 1
		elseif Test.IsLoadedInto(s, House) then loaded = loaded + 1
		elseif Test.IsAtGarrisonPort(s, House) then ports = ports + 1
		elseif s.IsInWorld then outside = outside + 1 end
	end

	return loaded, ports, outside, dead
end

local function InHouseFootprint(c)
	return c.X >= House.Location.X and c.X < House.Location.X + HOUSE_W
		and c.Y >= House.Location.Y and c.Y < House.Location.Y + HOUSE_H
end

local function LineCells()
	if Probe == nil then
		return "(probe not created yet)"
	end

	if Probe.IsDead then
		return "(probe dead)"
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
	local loaded, ports, outside, dead = Tally()
	return string.format(
		"house at %d,%d: %d/%d HP, owner %s | of %d men: %d in shelter, %d at ports, %d outside, "
		.. "%d dead | automatic target line: %s | probe activity: %s",
		House.Location.X, House.Location.Y, House.Health, House.MaxHealth, House.Owner.Name,
		GARRISON_SIZE, loaded, ports, outside, dead, LineCells(),
		Probe == nil and "(not created yet)" or (Probe.IsDead and "(dead)" or Test.ActivityChain(Probe)))
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	-- East of the house, clear of the cell the probe will later occupy.
	for i = 1, GARRISON_SIZE do
		Garrison[i] = Actor.Create("e1", true, {
			Owner = Russia,
			Location = CPos.New(42 + (i - 1), 31),
		})
	end

	-- STAGING GOES THROUGH THE CLICK, not through Mobile. MobileProperties.EnterTransport queues a
	-- RideTransport activity directly; Test.ClickOrder issues a real EnterTransport order through
	-- Passenger.ResolveOrder, which is what a player's click does. The first version used the former
	-- and two of ten men never arrived (runs 260915_181651, _181810). The returned order string is a
	-- free setup assertion: a staging failure becomes a named verdict here instead of an unexplained
	-- shortfall fourteen seconds later.
	local refused = 0
	for _, s in ipairs(Garrison) do
		if Test.ClickOrder(s, House) ~= "EnterTransport" then refused = refused + 1 end
	end

	if refused > 0 then
		Test.Fail(string.format(
			"%d of %d riflemen were not even OFFERED an EnterTransport order on a neutral house, so "
			.. "nothing could be staged. This is the targeter refusing, not the garrison — look at "
			.. "EnterAlliedActorTargeter before reading it as a garrison finding.", refused, GARRISON_SIZE))
		return
	end

	TestHarness.FocusBetween(House)

	-- PRE-ASSERTION: nothing may be holding fire. Test.DefconLevel is DefconEscalationState.NoLevel
	-- (0) in an ordinary Skirmish; at the hold-fire level AutoTarget refuses to pick a target at all
	-- and a RED here would be the phase rather than Targetable@WhenGarrisoned.
	Trigger.AfterDelay(1, function()
		local level = Test.DefconLevel()
		if level ~= 0 then
			Test.Fail(string.format(
				"this scenario must run as an ordinary Skirmish with nothing holding fire, but "
				.. "Test.DefconLevel reads %d", level))
		end
	end)

	Trigger.AfterDelay(GARRISON_TICKS, function()
		local loaded, ports, outside, dead = Tally()

		if House.Owner.Name ~= "Russia" then
			Test.Fail("the house never changed hands, so it is not a hostile garrison and the "
				.. "`loaded` condition that enables Targetable@WhenGarrisoned may not be granted "
				.. "either -- " .. Census())
			return
		end

		if ports > 0 then
			Test.Fail(string.format(
				"%d man/men manned a firing port. A port soldier stands on the house's OWN CELL, so "
				.. "the target line would no longer be attributable. All three guards were supposed "
				.. "to prevent this -- the house's AutoTarget is held at HoldFire in rules.yaml "
				.. "(GarrisonManager reads the BUILDING's stance at :796/:865/:1316) and no enemy is "
				.. "within the garrison's 10c0. If this fires, the stance gate is not what it reads "
				.. "as. %s", ports, Census()))
			return
		end

		if loaded ~= GARRISON_SIZE then
			Test.Fail(string.format(
				"only %d of %d men reached the shelter (%d still outside, %d dead) -- staging, not "
				.. "the subject. Every man was offered and issued an EnterTransport click at tick 0. "
				.. "%s", loaded, GARRISON_SIZE, outside, dead, Census()))
			return
		end

		if House.Health ~= House.MaxHealth then
			Test.Fail("the house was already damaged before the probe existed -- " .. Census())
			return
		end

		-- THE PROBE, created only now: while the riflemen were walking in there was no abrams on the
		-- map that could have acquired one of them in the open. Sixteen cells west of the house.
		-- No order is given to it, now or ever.
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
				return "the probe died before it acquired anything. Nothing on this map should be "
					.. "able to do that: the garrison is held at HoldFire and is sixteen cells away "
					.. "in any case -- " .. Census()
			end

			-- GUARD 3, every tick rather than once.
			local _, atPorts, outInOpen = Tally()
			if atPorts > 0 or outInOpen > 0 then
				return "a garrison soldier left the shelter mid-run, so the house's cell is no "
					.. "longer unambiguous and the target line cannot be attributed -- " .. Census()
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
