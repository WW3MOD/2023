-- AUTO TEST: a garrison in the rubble still takes losses.
--
-- The defect, in one line: at the 1 HP Indestructible clamp GarrisonManager returns a damage
-- modifier of 0, Health applies modifiers before raising INotifyDamage, and GarrisonProtection.Damaged
-- therefore saw ZERO and forwarded nothing -- so a rubbled garrison became immune, having been at its
-- most exposed one hit point earlier. Layout, the 20 HP instrument and the ejection rule are in
-- map.yaml.
--
-- THREE THINGS ARE ASSERTED AND ALL THREE ARE REQUIRED:
--   1. the church reaches the clamp with its garrison INTACT (the fixture's own precondition, and
--      what makes every later loss attributable to the rubble path);
--   2. the occupant count then FALLS under continued fire;
--   3. nobody is ever ejected or deployed -- the ruling is to price the bad state, not to force the
--      player out of it, so a man appearing in the world is a failure however he got there.
--
-- WHY PassengerCount IS A HONEST COUNT OF THE LIVING. A dead shelter occupant is removed from the
-- hold rather than left in it: Passenger.Killed calls Cargo.Unload on its transport
-- (Passenger.cs:269-277), which removes it from `cargo` and revokes one `loaded` token. That is also
-- why this terminates -- when the last man dies `loaded` goes, ownership reverts to Neutral and the
-- probe stops engaging of its own accord.
--
-- EXPECTED RED on a build without the fix: the church still reaches the clamp and the census still
-- reads ten men, and then nothing happens for the rest of the run. The verdict names that exactly --
-- "the garrison has taken no losses since the church hit its 1 HP clamp Ns ago" -- rather than a
-- generic timeout, because the clamp latch proves the fire was landing.

local HOUSE_W, HOUSE_H = 2, 2
local GARRISON_SIZE = 10
local GARRISON_TICKS = 350
local BLEED_SECONDS = 40

Garrison = {}
Probe = nil

local clampTick = nil
local countAtClamp = nil
local observed = 0

local function InWorldCount()
	local n = 0
	for _, s in ipairs(Garrison) do
		if not s.IsDead and s.IsInWorld then n = n + 1 end
	end

	return n
end

local function Census()
	return string.format(
		"house %d/%d HP, owner %s, %d in shelter, %d men standing in the world | probe: %s",
		House.Health, House.MaxHealth, House.Owner.Name, House.PassengerCount, InWorldCount(),
		Probe ~= nil and not Probe.IsDead and Test.ActivityChain(Probe) or "(gone)")
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	for i = 1, GARRISON_SIZE do
		Garrison[i] = Actor.Create("e1", true, {
			Owner = Russia,
			Location = CPos.New(42 + ((i - 1) % 5), 31 + math.floor((i - 1) / 5)),
		})
	end

	for _, s in ipairs(Garrison) do s.EnterTransport(House) end

	TestHarness.FocusBetween(House)

	Trigger.AfterDelay(GARRISON_TICKS, function()
		if House.Owner.Name ~= "Russia" then
			Test.Fail("the house never changed hands, so it is not a hostile garrison -- " .. Census())
			return
		end

		if House.PassengerCount ~= GARRISON_SIZE then
			Test.Fail(string.format(
				"the house holds %d men in shelter, expected all %d, before a shot was fired -- %s",
				House.PassengerCount, GARRISON_SIZE, Census()))
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

			-- LIMB 3, every tick. Covers a port deployment AND an emergency bail; either one would
			-- mean the men left the shelter rather than being priced for staying in it.
			if InWorldCount() > 0 then
				return "a garrison soldier is standing in the world. Nobody may be ejected from the " ..
					"rubble and no port may deploy (the probe sits outside the garrison's 10c0), so " ..
					"this is either an emergency bail that should be off or a port deploy that " ..
					"should have been impossible -- " .. Census()
			end

			-- LIMB 1: latch the clamp, with the census taken at that instant.
			if clampTick == nil then
				if House.Health <= 1 then
					clampTick = observed
					countAtClamp = House.PassengerCount
					if countAtClamp ~= GARRISON_SIZE then
						return string.format(
							"the church reached its 1 HP clamp with only %d of %d men left, so losses " ..
							"after this point cannot be attributed to the rubble path. The 20 HP " ..
							"instrument is meant to clamp on the first shell while forwarding nothing " ..
							"(19 damage at protection 30 is 13, under MinPassThrough 15) -- %s",
							countAtClamp, GARRISON_SIZE, Census())
					end
				end

				return false
			end

			-- LIMB 2: the church is rubble and the fire is still landing. Men must die.
			if House.PassengerCount < countAtClamp then
				return true
			end

			return false
		end, function()
			if clampTick == nil then
				return string.format(
					"the church never reached its 1 HP clamp in %ds, so the rubble path was never " ..
					"exercised and this run says nothing about it. Suspect the probe (it is given no " ..
					"order and must auto-acquire the house on FireAtWill) rather than " ..
					"GarrisonProtection -- %s", BLEED_SECONDS, Census())
			end

			return string.format(
				"the garrison has taken no losses in the %d ticks since the church hit its 1 HP clamp, " ..
				"still holding all %d men. The clamp latch proves the shells were landing, so this is " ..
				"the defect exactly: at the clamp GarrisonManager's damage modifier zeroes every hit " ..
				"and GarrisonProtection.Damaged returns on `incomingDamage <= 0` without forwarding " ..
				"anything -- the rubble is the safest place on the curve instead of the most exposed. " ..
				"%s", observed - clampTick, countAtClamp, Census())
		end)
	end)
end
