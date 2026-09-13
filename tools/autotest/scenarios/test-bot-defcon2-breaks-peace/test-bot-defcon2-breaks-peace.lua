-- TEST: the bot breaks the DEFCON 2 cease-fire with its ground army.
--
-- Layout and intent live in description.txt. This file places both armies, confirms the match really
-- opened in the cease-fire phase, and then waits for the one event that can only be produced by a
-- shot the bot ORDERED.
--
-- THE PRE-ASSERTION IS NOT CEREMONY. If the mode or the opening phase failed to apply, the match is
-- an ordinary Skirmish where autotargeting was never held at all -- and the kill assertion below
-- would then pass for entirely the wrong reason, reporting a fix that had not run. Test.DefconLevel
-- returns DefconEscalationState.NoLevel (0) in Skirmish, which is what separates the two.

local TicksPerSecond = TestHarness.TicksPerSecond

local HOLD_FIRE_LEVEL = 2
local OPEN_WAR_LEVEL = 1

-- Generous: the bot's first evaluation is LocalRandom.Next(0, 100) ticks in, it then has to form an
-- axis against the enemy Supply Route, recruit the armour onto it and close to weapon range. None of
-- that is what is being measured, so the budget should never be the thing that decides the verdict.
local RUN_SECONDS = 90

BotTanks = {}
EnemyTanks = {}

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

	local enemyPlacements = {
		{ 16, 26 }, { 16, 28 }, { 16, 30 },
		{ 18, 27 }, { 18, 29 }, { 17, 28 },
	}

	for _, p in ipairs(enemyPlacements) do
		EnemyTanks[#EnemyTanks + 1] = Actor.Create("t90", true, {
			Owner = Russia,
			Location = CPos.New(p[1], p[2]),
			Facing = Angle.West,
		})
	end

	TestHarness.FocusBetween(BotTanks[1], EnemyTanks[1])
	TestHarness.Select(BotTanks[1])

	-- THE PRE-ASSERTION, read one tick in so every World trait has had its first tick.
	Trigger.AfterDelay(1, function()
		local level = Test.DefconLevel()
		if level ~= HOLD_FIRE_LEVEL then
			Test.Fail(string.format(
				"match did not open in the cease-fire phase: DEFCON reads %d, expected %d "
				.. "(0 means the Escalation mode never applied and this is an ordinary Skirmish, "
				.. "in which nothing was ever holding fire and the rest of this test proves nothing)",
				level, HOLD_FIRE_LEVEL))
		end
	end)

	local function enemyLosses()
		local dead = 0
		for _, t in ipairs(EnemyTanks) do
			if t.IsDead then
				dead = dead + 1
			end
		end

		return dead
	end

	-- THE VERDICT. Both halves are required and they fail differently on purpose: a casualty with the
	-- level still at 2 would mean the kill did not qualify (DefconCasualtyObserver.IsQualifyingCasualty
	-- rejects friendly fire and neutral victims), while the level moving with no casualty is not a
	-- state this mode can reach and would mean the readout is lying.
	TestHarness.AssertWithin(RUN_SECONDS, function()
		local dead = enemyLosses()
		local level = Test.DefconLevel()

		if dead > 0 and level == OPEN_WAR_LEVEL then
			return true
		end

		if dead > 0 and level == HOLD_FIRE_LEVEL then
			return string.format(
				"%d enemy tank(s) destroyed but DEFCON is still %d -- the casualty did not qualify, "
				.. "so the 2 -> 1 trigger never fired", dead, level)
		end

		return false
	end, function()
		return string.format(
			"no enemy tank was destroyed in %ds and DEFCON is still %d. The bot's ground army held "
			.. "fire through the whole cease-fire: every order it issues is an AttackMove, which "
			.. "AutoTarget.IsAutoAcquiredSource classes as autonomous and the phase refuses. The air "
			.. "paths are removed in rules.yaml, so nothing else could have broken the peace either.",
			RUN_SECONDS, Test.DefconLevel())
	end)
end
