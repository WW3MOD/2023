-- DEMO: WGM suite — show off all recent wire-guided / Hellfire work in one
-- map. Press End to restart. Use pause + speed to read the action.
--
-- LAYOUT (top → bottom):
--   row 3   Lane 1: 0 trees on line — baseline
--   row 6   Lane 2: 1 tree           — free under FreeLineDensity
--   row 9   Lane 3: 2 trees          — small per-shot miss chance
--   row 12  Lane 4: 3 trees          — meaningful miss chance
--   row 15  Lane 5: 4-tree wall + patrolling t90 — moving target through trees
--   row 18  Lane 6: operator retargeting — primary t90 dies first, missile
--           swings to one of two backup t90s (closer = preferred). Crews
--           are leveled-up so retargeting is near-instant; rookie crews
--           would take ~2 s.
--   rows 22-32: ongoing 6v6 skirmish with respawns. USA (Bradleys + Abrams +
--           M113) on the left, Russia (T-90s + BMP-2s) on the right. All on
--           FireAtWill. When a unit dies it respawns at its starting cell
--           after 3 s so the brawl never empties out. Lets you sanity-check
--           how the changes feel inside a more "real game" mix instead of
--           only in the controlled lanes.
--
-- All targets in the controlled lanes also respawn so each lane keeps
-- exercising the mechanic. Bradleys do too (skirmish zone may push waste
-- targeting onto the controlled lanes; respawning blunts that).

local TicksPerSecond = TestHarness.TicksPerSecond

-- --------------------------------------------------------------------
-- Helpers
-- --------------------------------------------------------------------

local function spawnReplacement(actorType, owner, location, facing, delaySec, onSpawn)
	local delayTicks = math.floor((delaySec or 3) * TicksPerSecond)
	Trigger.AfterDelay(delayTicks, function()
		local fresh = Actor.Create(actorType, true, {
			Owner = owner,
			Location = location,
			Facing = WAngle.New(facing or 0),
		})
		if onSpawn then onSpawn(fresh) end
	end)
end

-- Watch an actor and respawn it at the same place when it dies. Recursive on
-- the new actor so the loop is permanent.
local function respawnLoop(actor, actorType, owner, location, facing, delaySec, onSpawn)
	if onSpawn then onSpawn(actor) end
	Trigger.OnKilled(actor, function()
		spawnReplacement(actorType, owner, location, facing, delaySec, function(fresh)
			respawnLoop(fresh, actorType, owner, location, facing, delaySec, onSpawn)
		end)
	end)
end

-- Force-attack target on whichever Bradley is currently alive in `getBradley()`.
-- Re-issues the attack order every second so respawns / target deaths /
-- mid-flight retargets don't leave the Bradley idle.
local function attackForever(getBradley, getTarget)
	local function rearm()
		local b = getBradley()
		local t = getTarget()
		if b and not b.IsDead and t and not t.IsDead then
			b.Attack(t, false, false)
		end
	end
	local function tick()
		rearm()
		Trigger.AfterDelay(TicksPerSecond, tick)
	end
	tick()
end

-- --------------------------------------------------------------------
-- WorldLoaded
-- --------------------------------------------------------------------

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	TestHarness.FocusBetween(B0, T0, B5, T5a)
	TestHarness.Select(B3)

	-- =================================================================
	-- Controlled lanes 1-4 (rows 3, 6, 9, 12) — tree-density gate
	-- =================================================================

	local lanes = {
		{ b = B0, t = T0, bX = 3, bY = 3,  tX = 24, tY = 3,  bFacing = 768, tFacing = 256 },
		{ b = B1, t = T1, bX = 3, bY = 6,  tX = 24, tY = 6,  bFacing = 768, tFacing = 256 },
		{ b = B2, t = T2, bX = 3, bY = 9,  tX = 24, tY = 9,  bFacing = 768, tFacing = 256 },
		{ b = B3, t = T3, bX = 3, bY = 12, tX = 24, tY = 12, bFacing = 768, tFacing = 256 },
	}

	for _, L in ipairs(lanes) do
		-- Make the lane self-contained: each side uses local refs that
		-- the respawn closure updates.
		local state = { b = L.b, t = L.t }

		respawnLoop(state.b, "bradley", USA,
			CPos.New(L.bX, L.bY), L.bFacing, 4,
			function(actor) state.b = actor end)
		respawnLoop(state.t, "t90", Russia,
			CPos.New(L.tX, L.tY), L.tFacing, 4,
			function(actor)
				state.t = actor
				actor.Stance = "HoldFire"
			end)

		L.b.Stance = "HoldFire"  -- so it doesn't try to engage skirmish enemies
		state.t.Stance = "HoldFire"

		attackForever(function() return state.b end, function() return state.t end)
	end

	-- =================================================================
	-- Lane 5 (row 15) — moving target through tree wall
	-- =================================================================

	local lane5 = { b = B4, t = T4 }
	respawnLoop(lane5.b, "bradley", USA, CPos.New(3, 15), 768, 4, function(a) lane5.b = a end)
	respawnLoop(lane5.t, "t90", Russia, CPos.New(24, 15), 256, 4, function(a)
		lane5.t = a
		-- t90 paces along its row, crossing the tree wall at cols 14-20.
		-- Auto-attack disabled so it doesn't shred the Bradley.
		a.Stance = "HoldFire"
		a.Patrol({ CPos.New(10, 15), CPos.New(28, 15) }, true, 0)
	end)
	lane5.b.Stance = "HoldFire"
	attackForever(function() return lane5.b end, function() return lane5.t end)

	-- =================================================================
	-- Lane 6 (row 18) — operator retargeting
	-- =================================================================
	-- Give B5 max veterancy so retargeting is instant (vs the 2 s rookie
	-- delay). User can compare by lowering this in the Lua and restarting.
	if B5.GiveLevels then B5.GiveLevels(5, true) end

	local L6 = { b = B5, primary = T5a, backup1 = T5b, backup2 = T5c }
	respawnLoop(L6.b, "bradley", USA, CPos.New(3, 18), 768, 4, function(a)
		L6.b = a
		if a.GiveLevels then a.GiveLevels(5, true) end
	end)
	respawnLoop(L6.primary, "t90", Russia, CPos.New(24, 18), 256, 6, function(a)
		L6.primary = a
		a.Stance = "HoldFire"
	end)
	respawnLoop(L6.backup1, "t90", Russia, CPos.New(26, 17), 256, 6, function(a)
		L6.backup1 = a
		a.Stance = "HoldFire"
	end)
	respawnLoop(L6.backup2, "t90", Russia, CPos.New(26, 19), 256, 6, function(a)
		L6.backup2 = a
		a.Stance = "HoldFire"
	end)
	L6.b.Stance = "HoldFire"
	-- Primary t90 takes more punishment on respawn so it dies dramatically
	-- mid-flight and forces the operator-retarget code path.
	attackForever(function() return L6.b end,
		function()
			-- Always target the primary while it's alive; the missile in
			-- flight is the one that retargets (engine-side) when it dies.
			if L6.primary and not L6.primary.IsDead then return L6.primary end
			if L6.backup1 and not L6.backup1.IsDead then return L6.backup1 end
			return L6.backup2
		end)

	-- =================================================================
	-- Skirmish zone (rows 22-32) — ongoing 6v6 with respawns
	-- =================================================================

	-- Each entry is { handle, type, owner, x, y, facing }.
	local skirmish = {
		{ SkB1, "bradley", USA,    4,  24, 768 },
		{ SkB2, "bradley", USA,    4,  28, 768 },
		{ SkB3, "bradley", USA,    8,  26, 768 },
		{ SkA1, "abrams",  USA,    4,  30, 768 },
		{ SkH1, "m113",    USA,    8,  30, 768 },
		{ SkH2, "m113",    USA,    8,  22, 768 },
		{ SkT1, "t90",     Russia, 60, 24, 256 },
		{ SkT2, "t90",     Russia, 60, 28, 256 },
		{ SkT3, "t90",     Russia, 56, 26, 256 },
		{ SkB4, "bmp2",    Russia, 60, 30, 256 },
		{ SkB5, "bmp2",    Russia, 56, 30, 256 },
		{ SkB6, "bmp2",    Russia, 56, 22, 256 },
	}
	for _, e in ipairs(skirmish) do
		respawnLoop(e[1], e[2], e[3], CPos.New(e[4], e[5]), e[6], 5)
	end
end
