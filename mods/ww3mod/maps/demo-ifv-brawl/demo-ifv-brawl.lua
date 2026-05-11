-- DEMO: 14v14 Bradley-vs-BMP brawl to showcase the slow WGM turn rate
-- (HorizontalRateOfTurn 60 → 8). When a target dies mid-flight the in-flight
-- missile swings to a new enemy — the curve should now span several cells
-- instead of snapping at a near-straight angle.
--
-- LAYOUT
--   USA (left):     2 cols × 7 rows (cols 22, 26)   = 14 Bradleys, facing east
--   Russia (right): 2 cols × 7 rows (cols 38, 42)   = 14 BMP-2s,   facing west
--   Gap: 12-20 cells — all pairings inside WGM range (25c0).
--
-- BEHAVIOR
--   - All crews start max veterancy so retargeting is near-instant (no 2 s
--     rookie delay obscuring the turn-rate visual).
--   - WGM BurstWait shortened in weapons.yaml so missiles are continuously
--     in the air; targets die fast → retargets happen often.
--   - Dead units respawn in place after ~5 s with fresh ammo and full vet.
--   - InitialEngagementStance: Defensive keeps them facing off in place
--     rather than charging — needed so missile flight times are long
--     enough for the curve to be visible.
--
-- TIP
--   Press space to pause and study an in-flight missile's path. Press End
--   to restart.

local TicksPerSecond = TestHarness.TicksPerSecond

-- --------------------------------------------------------------------
-- Helpers
-- --------------------------------------------------------------------

local function setupActor(actor)
	if actor and not actor.IsDead and actor.GiveLevels then
		actor.GiveLevels(5, true)
	end
end

local function spawnReplacement(actorType, owner, location, facing, delaySec, onSpawn)
	local delayTicks = math.floor((delaySec or 5) * TicksPerSecond)
	Trigger.AfterDelay(delayTicks, function()
		local fresh = Actor.Create(actorType, true, {
			Owner = owner,
			Location = location,
			Facing = WAngle.New(facing or 0),
		})
		if onSpawn then onSpawn(fresh) end
	end)
end

local function respawnLoop(actor, actorType, owner, location, facing, delaySec)
	setupActor(actor)
	Trigger.OnKilled(actor, function()
		spawnReplacement(actorType, owner, location, facing, delaySec, function(fresh)
			respawnLoop(fresh, actorType, owner, location, facing, delaySec)
		end)
	end)
end

-- --------------------------------------------------------------------
-- WorldLoaded
-- --------------------------------------------------------------------

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	-- Frame the camera on the middle of the battle.
	TestHarness.FocusBetween(U1, U14, R1, R14)
	TestHarness.Select(U7)

	-- USA side: 14 Bradleys
	local usaUnits = {
		{ U1,  22,  4 }, { U2,  22,  8 }, { U3,  22, 12 }, { U4,  22, 16 },
		{ U5,  22, 20 }, { U6,  22, 24 }, { U7,  22, 28 },
		{ U8,  26,  4 }, { U9,  26,  8 }, { U10, 26, 12 }, { U11, 26, 16 },
		{ U12, 26, 20 }, { U13, 26, 24 }, { U14, 26, 28 },
	}
	for _, e in ipairs(usaUnits) do
		respawnLoop(e[1], "bradley", USA, CPos.New(e[2], e[3]), 768, 5)
	end

	-- Russia side: 14 BMP-2s
	local russiaUnits = {
		{ R1,  38,  4 }, { R2,  38,  8 }, { R3,  38, 12 }, { R4,  38, 16 },
		{ R5,  38, 20 }, { R6,  38, 24 }, { R7,  38, 28 },
		{ R8,  42,  4 }, { R9,  42,  8 }, { R10, 42, 12 }, { R11, 42, 16 },
		{ R12, 42, 20 }, { R13, 42, 24 }, { R14, 42, 28 },
	}
	for _, e in ipairs(russiaUnits) do
		respawnLoop(e[1], "bmp2", Russia, CPos.New(e[2], e[3]), 256, 5)
	end
end
