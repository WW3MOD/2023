-- DEMO: Burn arena.
--
-- Top half (Demo player, NonCombatant):
--   - 14 vehicles (one of every crewed type) damaged to 49% HP. The single
--     Lua hit is the only scripted action; the production-rules
--     ChangesHealth@CriticalDamage (-1% MaxHP / 5 ticks once HP < 50%) is
--     what carries them down to 0 from there. So the fire ramp progresses
--     at game-natural pace and the final inferno tier (stacks 9-10) lingers
--     for several seconds before cookoff.
--
--   - 1 helicopter on the ground (spawned at altitude 0 — sits, doesn't
--     autorotate). Lua-tick its HP since there's no natural bleed for a
--     non-crash-disabled aircraft on the ground; this lets the player
--     watch every fire stage on a stationary heli.
--
--   - 1 helicopter airborne. Single Lua hit to 49% HP triggers
--     HeliEmergencyLanding's autorotation; from there the descent + burn +
--     cookoff is all production behaviour.
--
-- Bottom half: 4 USA vs 4 Russia, default stances, no Lua intervention —
-- this is what burning looks like in real combat.
--
-- Every Lua action is announced with a [LUA] prefix. No Test.Pass — the
-- window stays open until you close it manually.

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
		announce("Required players not found")
		return
	end

	Camera.Position = cellPos(32, 16, 0)

	-- ---------------------------------------------------------------------
	-- Top half: bleed-side vehicles. One of every crewed vehicle type.
	-- ---------------------------------------------------------------------
	local bleedVehicles = {}
	local row1 = { "humvee", "m113", "bradley", "abrams", "m109", "m270", "strykershorad" }
	local row2 = { "btr", "bmp2", "t90", "giatsint", "grad", "tos", "tunguska" }

	for i, t in ipairs(row1) do
		table.insert(bleedVehicles, Actor.Create(t, true, {
			Owner = DEMO, Location = CPos.New(4 + (i - 1) * 8, 4), Facing = Angle.South,
		}))
	end
	for i, t in ipairs(row2) do
		table.insert(bleedVehicles, Actor.Create(t, true, {
			Owner = DEMO, Location = CPos.New(4 + (i - 1) * 8, 8), Facing = Angle.South,
		}))
	end
	announce("Spawned 14 vehicles (Demo player) at top of map.")

	-- ---------------------------------------------------------------------
	-- Helicopter row: one on ground, one airborne (will autorotate).
	-- ---------------------------------------------------------------------
	local groundHeli = Actor.Create("heli", true, {
		Owner = DEMO,
		CenterPosition = cellPos(12, 13, 0),  -- altitude 0 = sitting on ground
		Facing = Angle.East,
	})
	local airHeli = Actor.Create("hind", true, {
		Owner = DEMO,
		CenterPosition = cellPos(48, 13, 1280),  -- altitude 1280 = cruising
		Facing = Angle.West,
	})
	announce("Spawned 1 ground heli (Apache, west side) + 1 airborne heli (Hind, east side facing west).")

	-- ---------------------------------------------------------------------
	-- Combat zone: USA vs Russia, no Lua tampering.
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
	-- Apply the single 51% damage hit IMMEDIATELY (no settle delay) so the
	-- player doesn't have to wait. From here game-natural ChangesHealth
	-- carries the vehicles down to 0 over ~9-10 seconds.
	-- ---------------------------------------------------------------------
	announce("Single 51% damage hit applied to top-row vehicles + airborne heli — natural bleed takes over.")
	for _, v in ipairs(bleedVehicles) do
		if not v.IsDead and v.MaxHealth > 0 then
			v.Health = math.floor(v.MaxHealth * 49 / 100)
		end
	end
	if not airHeli.IsDead and airHeli.MaxHealth > 0 then
		airHeli.Health = math.floor(airHeli.MaxHealth * 49 / 100)
	end

	-- ---------------------------------------------------------------------
	-- Ground heli has no natural bleed (ChangesHealth@CrashBurn requires
	-- crash-disabled, which only fires after a safe-autorotation landing).
	-- Lua-tick its HP -2% / 5 ticks so the player sees every fire stage on
	-- a static, on-the-ground helicopter.
	-- ---------------------------------------------------------------------
	announce("Ground heli: starting Lua-driven 10-second bleed (no natural bleed available on grounded non-crash-disabled aircraft).")
	local step = 0
	local function tickGroundHeli()
		step = step + 1
		if step > 50 then return end
		if not groundHeli.IsDead and groundHeli.MaxHealth > 0 then
			local pct = 100 - step * 2
			groundHeli.Health = math.floor(groundHeli.MaxHealth * pct / 100)
		end
		Trigger.AfterDelay(5, tickGroundHeli)
	end
	tickGroundHeli()
end
