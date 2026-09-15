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

local GARRISON_A_SIZE = 10   -- MaxWeight is 10 and there are 8 ports; at DEFCON 2 none of them deploy
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

local function Occupancy(label, church, squad)
	local dead = 0
	for _, s in ipairs(squad) do
		-- PITFALL: IsDead reads TRUE for a soldier sitting in a Cargo hold, so this counts
		-- "in shelter OR dead" and cannot separate them (DOCS/recipes/AUTOTEST.md). That is fine
		-- here because PassengerCount below answers the same question properly; this is only ever
		-- printed beside it.
		if s.IsDead then dead = dead + 1 end
	end

	return string.format("%s at %d,%d: %d/%d HP, owner %s, %d in shelter, %d of %d men not in world",
		label, church.Location.X, church.Location.Y, church.Health, church.MaxHealth,
		church.Owner.Name, church.PassengerCount, dead, #squad)
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

	for _, s in ipairs(GarrisonA) do s.EnterTransport(ChurchA) end
	for _, s in ipairs(GarrisonB) do s.EnterTransport(ChurchB) end

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

			if church.PassengerCount ~= wanted then
				return string.format(
					"%s holds %d men in shelter, expected all %d. Either somebody manned a port -- "
					.. "which would let splash decide this test -- or men were lost on the way in. "
					.. "%s", label, church.PassengerCount, wanted, Occupancy(label, church, squad))
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
