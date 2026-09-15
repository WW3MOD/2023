-- AUTO TEST: a non-force attack can damage a garrisoned building.
--
-- The audit finding, in one line: a garrisoned building carried RequiresForceFire, no bot module in
-- the mod ever force-fires an ACTOR, and shelter occupants can only be hurt through damage to the
-- building -- so against the AI a sheltering soldier could not be harmed at all and the
-- GarrisonProtection curve never degraded. Layout, the DEFCON-2 instrument and why it is there live
-- in map.yaml and description.txt.
--
-- TWO HALVES, BOTH REQUIRED, and they are two different ways into the same gate:
--   A  the BOT's own named "Attack" order against ChurchA  (forceAttack: false)
--   B  a PLAYER's ordinary attack order against ChurchB    (forceAttack: false)
-- Each latches the moment its building's HP first falls below max, so a shell that arrives late from
-- the wrong army cannot retroactively score a half that was already decided.
--
-- EXPECTED RED with RequiresForceFire restored on Targetable@WhenGarrisoned, and it is specific
-- rather than a generic timeout: the bot still SELECTS ChurchA -- NearestEngageableEnemy filters on
-- relationship, visibility and EstimatePercentDamage and knows nothing about force-fire
-- (PoiOffensiveBotModule.cs:4745-4765) -- and still queues the order; the Attack activity then finds
-- no armament for it (AttackBase.cs:442) and nothing is ever fired. Same for the player's order. So
-- the RED reads "ChurchA is untouched at 75000/75000 HP after 100s ... ChurchB likewise", with the
-- fixture census confirming both were garrisoned, Russia-owned and holding all their men in shelter
-- the whole time -- i.e. the buildings were loaded and hostile and simply could not be shot.

local HOLD_FIRE_LEVEL = 2

-- Long enough for ten men to walk one or two cells and load. Nothing is measured during it.
local GARRISON_TICKS = 350

-- Generous on purpose: the bot's first evaluation is LocalRandom.Next(0, 100) ticks in, it then has
-- to form an axis against the enemy Supply Route, recruit the armour onto it and drive thirty-odd
-- cells east to ChurchA. None of that is what is being measured, so the budget must never be the
-- thing that decides the verdict. Half B resolves within a second or two of its order.
local RUN_SECONDS = 100

-- Well under Cargo MaxWeight 10. The first version used all ten and one man never got in
-- (run 260915_181523): ten simultaneous entries into a hold with zero slack is a staging race, and
-- the size was never part of the claim -- what matters is that the men are in the SHELTER.
local GARRISON_A_SIZE = 4
local GARRISON_B_SIZE = 3

BotTanks = {}
GarrisonA = {}
GarrisonB = {}

local maxA, maxB
local damagedA, damagedB = false, false

local function CountAlive(squad)
	local n = 0
	for _, a in ipairs(squad) do
		if not a.IsDead then n = n + 1 end
	end

	return n
end

-- THE CENSUS, and every line of it corrects the one this replaces.
--
-- `Actor.IsDead` is `Disposed || health.IsDead` (Actor.cs:76) -- purely health and disposal -- so a
-- man in a Cargo hold is alive, not disposed, and reads IsDead == FALSE. The first version counted
-- `s.IsDead` under a label reading "not in world", printed 0 while nine men were demonstrably
-- aboard, and produced a verdict that contradicted itself (run 260915_181523). `IsInWorld` IS false
-- for a passenger and that direction is sound: in-world-and-not-loaded really does mean standing
-- outside.
--
-- So ask the TRAITS, not the actor: Test.IsLoadedInto reads Cargo.Passengers, Test.IsAtGarrisonPort
-- reads GarrisonManager.PortStates, and both are true regardless of either flag. The branch ORDER is
-- load-bearing -- a port soldier is in-world and not loaded, so he satisfies the "outside" test too
-- and has to be claimed before it.
local function Tally(church, squad)
	local loaded, ports, outside, dead = 0, 0, 0, 0
	for _, s in ipairs(squad) do
		if s.IsDead then dead = dead + 1
		elseif Test.IsLoadedInto(s, church) then loaded = loaded + 1
		elseif Test.IsAtGarrisonPort(s, church) then ports = ports + 1
		elseif s.IsInWorld then outside = outside + 1 end
	end

	return loaded, ports, outside, dead
end

local function Occupancy(label, church, squad)
	local loaded, ports, outside, dead = Tally(church, squad)
	return string.format("%s at %d,%d: %d/%d HP, owner %s | of %d men: %d in shelter, %d at ports, "
		.. "%d outside, %d dead",
		label, church.Location.X, church.Location.Y, church.Health, church.MaxHealth,
		church.Owner.Name, #squad, loaded, ports, outside, dead)
end

local function Spawn(owner, count, x, y)
	local made = {}
	for i = 1, count do
		made[i] = Actor.Create("e1", true, {
			Owner = owner,
			Location = CPos.New(x + ((i - 1) % 5), y + math.floor((i - 1) / 5)),
		})
	end

	return made
end

WorldLoaded = function()
	local USAbot = Player.GetPlayer("USA-bot")
	local Russia = Player.GetPlayer("Russia")

	local botPlacements = {
		{ 6, 26 }, { 6, 28 }, { 6, 30 },
		{ 8, 26 }, { 8, 28 }, { 8, 30 },
		{ 9, 27 }, { 9, 29 },
	}

	for _, p in ipairs(botPlacements) do
		BotTanks[#BotTanks + 1] = Actor.Create("abrams", true, {
			Owner = USAbot,
			Location = CPos.New(p[1], p[2]),
			Facing = Angle.East,
		})
	end

	-- Two cells clear of each church so nobody is standing on its footprint when it loads.
	GarrisonA = Spawn(Russia, GARRISON_A_SIZE, 39, 31)
	GarrisonB = Spawn(Russia, GARRISON_B_SIZE, 79, 13)

	-- STAGING GOES THROUGH THE CLICK, not through Mobile. MobileProperties.EnterTransport queues a
	-- RideTransport activity directly; Test.ClickOrder issues a real EnterTransport order through
	-- Passenger.ResolveOrder, which is what a player's click does. The first version used the former
	-- and a man never arrived. The returned order string is a free setup assertion: a staging failure
	-- becomes a named verdict at tick 0 instead of an unexplained shortfall fourteen seconds later.
	local refused = 0
	for _, s in ipairs(GarrisonA) do
		if Test.ClickOrder(s, ChurchA) ~= "EnterTransport" then refused = refused + 1 end
	end

	for _, s in ipairs(GarrisonB) do
		if Test.ClickOrder(s, ChurchB) ~= "EnterTransport" then refused = refused + 1 end
	end

	if refused > 0 then
		Test.Fail(string.format(
			"%d rifleman/men were not even OFFERED an EnterTransport order on a neutral church, so "
			.. "nothing could be staged. This is the targeter refusing, not the garrison.", refused))
		return
	end

	maxA = ChurchA.MaxHealth
	maxB = ChurchB.MaxHealth

	TestHarness.FocusBetween(BotTanks[1], ChurchA)
	TestHarness.Select(BotTanks[1])

	-- THE PHASE PRE-ASSERTION, read one tick in so every World trait has had its first tick. If the
	-- Escalation mode never applied this is an ordinary Skirmish, the garrison would deploy to its
	-- ports normally, and a SpreadDamage warhead aimed at a port man standing on the building's own
	-- cell would move the building's HP on a build that still had the flag -- scoring half A for a
	-- reason that has nothing to do with the fix. Test.DefconLevel returns 0 in Skirmish.
	Trigger.AfterDelay(1, function()
		local level = Test.DefconLevel()
		if level ~= HOLD_FIRE_LEVEL then
			Test.Fail(string.format(
				"match did not open in the cease-fire phase: DEFCON reads %d, expected %d. 0 means "
				.. "the Escalation mode never applied, the garrison will man its ports, and splash "
				.. "onto the building's own cell would then decide this test instead of the gate "
				.. "under test", level, HOLD_FIRE_LEVEL))
		end
	end)

	Trigger.AfterDelay(GARRISON_TICKS, function()
		-- THE FIXTURE GATE. Every one of these is a precondition of the verdict meaning anything,
		-- and each fails with its own text so a broken fixture can never be read as the finding.
		local function fixtureFault(label, church, squad, wanted)
			if church.IsDead then
				return label .. " is dead before anything was measured"
			end

			-- By NAME rather than by object identity: two ScriptPlayer handles for the same
			-- player are distinct userdata, so `~=` on them is not the question being asked.
			if church.Owner.Name ~= "Russia" then
				return string.format(
					"%s never changed hands, so it is not a hostile garrison and the `loaded` "
					.. "condition that enables Targetable@WhenGarrisoned may not be granted either "
					.. "-- %s", label, Occupancy(label, church, squad))
			end

			local loaded, ports, outside, dead = Tally(church, squad)

			if ports > 0 then
				return string.format(
					"%d of %s's men manned a firing port. A port soldier stands on the building's OWN "
					.. "CELL, so splash aimed at him would move the building's HP and score this test "
					.. "on a build that still had the flag. TWO guards were supposed to prevent it: "
					.. "the church's AutoTarget is held at HoldFire in rules.yaml (GarrisonManager "
					.. "reads the BUILDING's stance at :796/:865/:1316) and DEFCON 2 makes "
					.. "ScanForTarget return Invalid outright (:961). If this fires, say which. %s",
					ports, label, Occupancy(label, church, squad))
			end

			if loaded ~= wanted then
				return string.format(
					"only %d of %s's %d men reached the shelter (%d still outside, %d dead) -- "
					.. "staging, not the subject. Every man was offered and issued an EnterTransport "
					.. "click at tick 0. %s", loaded, label, wanted, outside, dead,
					Occupancy(label, church, squad))
			end

			if church.Health ~= church.MaxHealth then
				return string.format(
					"%s was already damaged before the measurement started, so neither half can be "
					.. "attributed -- %s", label, Occupancy(label, church, squad))
			end

			return nil
		end

		local fault = fixtureFault("ChurchA", ChurchA, GarrisonA, GARRISON_A_SIZE)
			or fixtureFault("ChurchB", ChurchB, GarrisonB, GARRISON_B_SIZE)
		if fault ~= nil then
			Test.Fail("fixture did not come up: " .. fault)
			return
		end

		if Rifle.IsDead then
			Test.Fail("the player's abrams died before it could be ordered to attack -- "
				.. Occupancy("ChurchB", ChurchB, GarrisonB))
			return
		end

		-- HALF B'S GESTURE: an ORDINARY attack order, forceAttack = false. That third argument is the
		-- entire point of this line -- passing true here would be a Ctrl+click, which has worked
		-- against a garrison since the flag was added and proves nothing.
		Rifle.Attack(ChurchB, true, false)

		TestHarness.AssertWithin(RUN_SECONDS, function()
			if ChurchA.Health < maxA then damagedA = true end
			if ChurchB.Health < maxB then damagedB = true end

			if damagedA and damagedB then
				return true
			end

			return false
		end, function()
			local missing
			if not damagedA and not damagedB then
				missing = "NEITHER half landed a shot"
			elseif not damagedA then
				missing = "the BOT's half never landed: the player's ordinary attack order did "
					.. "reach ChurchB, so the gate is open for a player-issued order but the bot "
					.. "never got a shot away -- suspect the bot's axis or its DEFCON-2 direct-fire "
					.. "pass, not Targetable@WhenGarrisoned"
			else
				missing = "the PLAYER's half never landed: the bot damaged ChurchA, so the gate is "
					.. "open, but the ordinary attack order on ChurchB produced no fire"
			end

			return string.format(
				"%s in %ds. %s | %s | DEFCON %d, %d bot tanks alive. With RequiresForceFire on "
				.. "Targetable@WhenGarrisoned this is the expected state: both orders are issued "
				.. "and both are then discarded by AttackBase.ChooseArmamentsForTarget, which hands "
				.. "back an empty armament set for any attack that is not force-fired.",
				missing, RUN_SECONDS,
				Occupancy("ChurchA", ChurchA, GarrisonA),
				Occupancy("ChurchB", ChurchB, GarrisonB),
				Test.DefconLevel(), CountAlive(BotTanks))
		end)
	end)
end
