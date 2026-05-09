-- DEMO: Burn arena.
--
-- Top half (Demo player, NonCombatant): one of every crewed vehicle, on a
-- scripted bleed from full HP to 0 over ~10 seconds. The bleed itself is
-- Lua; the fire overlays + cookoff that result are pure game behaviour.
-- Every script action is announced via Media.DisplayMessage with a [LUA]
-- prefix so it's never ambiguous whether the player just saw scripted or
-- emergent behaviour.
--
-- Bottom half (USA vs Russia): 4 USA vs 4 Russia combat vehicles, default
-- stances, no Lua tampering. Real combat — what burning looks like in a
-- live game.
--
-- The script does NOT call Test.Pass(). The window stays open until you
-- close it manually.

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

local function announce(msg)
	Media.DisplayMessage("[LUA] " .. msg, "BURN ARENA")
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	local DEMO = Player.GetPlayer("Demo")

	if USA == nil or RUSSIA == nil or DEMO == nil then
		announce("Required players (USA, Russia, Demo) not found — aborting demo")
		return
	end

	Camera.Position = cellPos(32, 16, 0)

	-- ---------------------------------------------------------------------
	-- Top half: bleed-side. One of every crewed vehicle, owned by Demo
	-- (NonCombatant) so combat-zone AI doesn't engage them.
	-- ---------------------------------------------------------------------
	local bleed = {}

	local row1 = { "humvee", "m113", "bradley", "abrams", "m109", "m270", "strykershorad" }
	local row2 = { "btr", "bmp2", "t90", "giatsint", "grad", "tos", "tunguska" }

	for i, t in ipairs(row1) do
		local x = 4 + (i - 1) * 8
		local v = Actor.Create(t, true, {
			Owner = DEMO,
			Location = CPos.New(x, 6),
			Facing = Angle.South,
		})
		table.insert(bleed, v)
	end

	for i, t in ipairs(row2) do
		local x = 4 + (i - 1) * 8
		local v = Actor.Create(t, true, {
			Owner = DEMO,
			Location = CPos.New(x, 10),
			Facing = Angle.South,
		})
		table.insert(bleed, v)
	end

	announce("Spawned 14 vehicles (one of each type) on Demo player at top of map.")

	-- ---------------------------------------------------------------------
	-- Bottom half: 4v4 combat zone. Default ^AutoTarget stance is FireAtWill.
	-- These are NOT touched by Lua — they fight naturally.
	-- ---------------------------------------------------------------------
	Actor.Create("abrams",  true, { Owner = USA,    Location = CPos.New(12, 22), Facing = Angle.South })
	Actor.Create("bradley", true, { Owner = USA,    Location = CPos.New(20, 22), Facing = Angle.South })
	Actor.Create("abrams",  true, { Owner = USA,    Location = CPos.New(28, 22), Facing = Angle.South })
	Actor.Create("bradley", true, { Owner = USA,    Location = CPos.New(36, 22), Facing = Angle.South })

	Actor.Create("t90",  true, { Owner = RUSSIA, Location = CPos.New(12, 28), Facing = Angle.North })
	Actor.Create("bmp2", true, { Owner = RUSSIA, Location = CPos.New(20, 28), Facing = Angle.North })
	Actor.Create("t90",  true, { Owner = RUSSIA, Location = CPos.New(28, 28), Facing = Angle.North })
	Actor.Create("bmp2", true, { Owner = RUSSIA, Location = CPos.New(36, 28), Facing = Angle.North })

	announce("Spawned 4 USA + 4 Russia in bottom half — default stances, no Lua intervention.")

	-- ---------------------------------------------------------------------
	-- Bleed: -2% MaxHP every 5 ticks (= 0.2s) for 50 steps = ~10 seconds.
	-- ---------------------------------------------------------------------
	Trigger.AfterDelay(sec(2), function()
		announce("Starting 10-second bleed on top row (-2% MaxHP every 0.2s).")
		local step = 0
		local function tickBleed()
			step = step + 1
			if step > 50 then
				announce("Bleed cycle complete. Anything still on screen is post-bleed game behaviour.")
				return
			end

			-- Milestones every 25% of the way through the ramp so the player
			-- sees what stage the fire SHOULD be at.
			if step == 1 then
				announce("HP=98%. Stage 1 fire about to ignite as HP crosses 50%.")
			elseif step == 25 then
				announce("HP=50%. First fire stack should be visible right now.")
			elseif step == 38 then
				announce("HP≈25%. Mid-ramp — should be ~5 stacks of fire.")
			elseif step == 50 then
				announce("HP=0%. Final detonation.")
			end

			for _, v in ipairs(bleed) do
				if not v.IsDead and v.MaxHealth > 0 then
					local pct = 100 - step * 2
					v.Health = math.floor(v.MaxHealth * pct / 100)
				end
			end
			Trigger.AfterDelay(5, tickBleed)
		end
		tickBleed()
	end)

	-- No Test.Pass — window stays open. Close it manually when done.
end
