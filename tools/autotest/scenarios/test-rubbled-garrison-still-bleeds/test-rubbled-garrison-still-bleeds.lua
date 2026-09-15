-- AUTO TEST: a garrison in the rubble still takes losses.
--
-- GarrisonManager.Indestructible clamps a garrison at 1 HP by returning a damage modifier of 0 once
-- it is there (GarrisonManager.cs:1451-1468), and Health applies the modifiers BEFORE raising
-- INotifyDamage (Health.cs:177-215) — so GarrisonProtection.Damaged saw a damage of ZERO and
-- forwarded nothing. A rubbled garrison was immune: safer than the same garrison one hit point
-- earlier, safer than an intact one, and terminally so, because the building cannot be destroyed
-- either. Men in the rubble could not be killed by any weapon in the game.
--
-- THE HOUSE IS GIVEN 20 HP IN rules.yaml, which is absurd for a church and is the instrument. The
-- first shell takes it straight to the clamp and forwards NOTHING on the way (19 damage at
-- RubbleProtection 30 is a 13-point share, under MinPassThrough 15), so the garrison is provably
-- untouched at the moment of clamping and every loss after that came through the path under test.
-- At the shipped 75000 HP the men would all be dead long before the clamp — the pre-clamp curve
-- works and always did — and this case would never be reached at all.
--
-- NOBODY MAY BE EJECTED, and nobody may man a port. The ruling is to price the bad state, not to
-- force the player out of it (user, 2026-09-02: "don't force them out, let the damage curve do the
-- work"), so a soldier appearing in the world is a failure however he got there. THREE independent
-- guards, after the first version relied on one and could not say whether it had held:
--   1. rules.yaml holds the HOUSE's own AutoTarget at HoldFire. GarrisonManager reads the BUILDING's
--      stance, not the soldiers' (GarrisonManager.cs:796, :865, :1316), and returns before every
--      autonomous deploy path. ^CivBuilding ships no AutoTarget at all, so this trait is added by the
--      scenario — AttackGarrisoned is an AttackFollow and therefore an AttackBase, so it has one to
--      attach to.
--   2. the range gap: ScanForTarget derives an empty port's radius from the SHELTER soldiers'
--      armaments (5.56mm.E3 = 10c0) while TankRound.Abrams reaches 25c0, so the probe at sixteen
--      cells is outside the garrison's envelope and inside its own.
--   3. Test.IsAtGarrisonPort is re-read EVERY TICK, which also catches an emergency bail.
--
-- THE OBSERVABLE IS THE LOADED COUNT, read through Test.IsLoadedInto. A dead shelter occupant is
-- removed from the hold rather than left in it: Passenger.Killed calls Cargo.Unload on its transport
-- (Passenger.cs:269-277), which removes it from `cargo` and revokes one `loaded` token. That is also
-- why this terminates on its own — when the last man dies `loaded` goes, ownership reverts to
-- Neutral and the probe stops engaging.
--
-- EXPECTED RED on a build without the fix: the church still reaches the clamp and the census still
-- reads every man aboard, and then nothing happens for the rest of the run. The verdict names that
-- exactly — "the garrison has taken no losses since the church hit its 1 HP clamp" — rather than a
-- generic timeout, because the clamp latch proves the fire was landing.

local HOUSE_W, HOUSE_H = 2, 2      -- v01 is 2x2; Location is its top-left cell
local GARRISON_SIZE = 4            -- well under Cargo MaxWeight 10: slack, and no entry congestion
local GARRISON_TICKS = 350         -- walk in; nothing is measured during it
local BLEED_SECONDS = 40           -- reach the clamp, then kill at least one man through it

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


local clampTick = nil
local loadedAtClamp = nil
local observed = 0

local function Census()
	local loaded, ports, outside, dead = Tally()
	return string.format(
		"house %d/%d HP, owner %s | of %d men: %d in shelter, %d at ports, %d outside, %d dead | "
		.. "probe: %s",
		House.Health, House.MaxHealth, House.Owner.Name, GARRISON_SIZE, loaded, ports, outside, dead,
		Probe == nil and "(not created yet)" or (Probe.IsDead and "(dead)" or Test.ActivityChain(Probe)))
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	for i = 1, GARRISON_SIZE do
		Garrison[i] = Actor.Create("e1", true, {
			Owner = Russia,
			Location = CPos.New(42 + (i - 1), 31),
		})
	end

	-- STAGING GOES THROUGH THE CLICK, not through Mobile — see the sibling scenario's note.
	local refused = 0
	for _, s in ipairs(Garrison) do
		if Test.ClickOrder(s, House) ~= "EnterTransport" then refused = refused + 1 end
	end

	if refused > 0 then
		Test.Fail(string.format(
			"%d of %d riflemen were not even OFFERED an EnterTransport order on a neutral house, so "
			.. "nothing could be staged.", refused, GARRISON_SIZE))
		return
	end

	TestHarness.FocusBetween(House)

	Trigger.AfterDelay(GARRISON_TICKS, function()
		local loaded, ports, outside, dead = Tally()

		if House.Owner.Name ~= "Russia" then
			Test.Fail("the house never changed hands, so it is not a hostile garrison -- " .. Census())
			return
		end

		if ports > 0 then
			Test.Fail(string.format(
				"%d man/men manned a firing port before a shot was fired. The house's AutoTarget is "
				.. "held at HoldFire in rules.yaml and GarrisonManager reads the BUILDING's stance "
				.. "(:796/:865/:1316), so if this fires the stance gate is not what it reads as. %s",
				ports, Census()))
			return
		end

		if loaded ~= GARRISON_SIZE then
			Test.Fail(string.format(
				"only %d of %d men reached the shelter (%d still outside, %d dead) -- staging, not "
				.. "the subject. %s", loaded, GARRISON_SIZE, outside, dead, Census()))
			return
		end

		if House.Health ~= House.MaxHealth then
			Test.Fail("the house was damaged before the probe existed -- " .. Census())
			return
		end

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

		TestHarness.AssertWithin(BLEED_SECONDS, function()
			observed = observed + 1

			local nowLoaded, atPorts, outInOpen = Tally()

			-- NOBODY LEAVES. Covers a port deploy and an emergency bail alike; either would mean the
			-- men escaped the rubble rather than being priced for staying in it.
			if atPorts > 0 or outInOpen > 0 then
				return "a garrison soldier is standing in the world. Nobody may be ejected from the "
					.. "rubble and no port may deploy -- " .. Census()
			end

			-- LIMB 1: latch the clamp, with the census taken at that instant.
			if clampTick == nil then
				if House.Health <= 1 then
					clampTick = observed
					loadedAtClamp = nowLoaded
					if loadedAtClamp ~= GARRISON_SIZE then
						return string.format(
							"the church reached its 1 HP clamp with only %d of %d men still aboard, "
							.. "so losses after this point cannot be attributed to the rubble path. "
							.. "The 20 HP instrument is meant to clamp on the first shell while "
							.. "forwarding nothing (19 damage at protection 30 is 13, under "
							.. "MinPassThrough 15) -- %s", loadedAtClamp, GARRISON_SIZE, Census())
					end
				end

				return false
			end

			-- LIMB 2: the church is rubble and the fire is still landing. Men must die.
			if nowLoaded < loadedAtClamp then
				return true
			end

			return false
		end, function()
			if clampTick == nil then
				return string.format(
					"the church never reached its 1 HP clamp in %ds, so the rubble path was never "
					.. "exercised and this run says nothing about it. Suspect the probe -- it is "
					.. "given no order and must auto-acquire the house on FireAtWill -- rather than "
					.. "GarrisonProtection. %s", BLEED_SECONDS, Census())
			end

			return string.format(
				"the garrison has taken no losses in the %d ticks since the church hit its 1 HP "
				.. "clamp, still holding all %d men. The clamp latch proves the shells were landing, "
				.. "so this is the defect exactly: at the clamp GarrisonManager's damage modifier "
				.. "zeroes every hit and GarrisonProtection.Damaged returns on `incomingDamage <= 0` "
				.. "without forwarding anything -- the rubble is the safest place on the curve "
				.. "instead of the most exposed. %s", observed - clampTick, loadedAtClamp, Census())
		end)
	end)
end
