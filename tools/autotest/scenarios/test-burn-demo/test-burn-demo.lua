-- DEMO: staged burning ramp.
--
-- Three vehicles and one helicopter, all dropped to 50% HP. Production-rules
-- bleed (-1% MaxHP per 5 ticks once below 50%) does the rest, so the fire
-- overlays light up progressively from stack 1 (small smoulder at 50% HP)
-- through stack 4 (medium fire) to stack 8+ (full inferno) before the wreck
-- detonates via VehicleCookoff.
--
-- For the helicopter: damaged to 30% HP at altitude → autorotation triggers
-- → lands → ChangesHealth@CrashBurn drains it to 0 over ~12s while the fire
-- intensifies and finally explodes via Explodes: !airborne.
--
-- No assertions. Test.Pass() is called once everything has likely detonated
-- so the runner exits cleanly. The point of this scenario is to look at it.

local TICKS_PER_SEC = TestHarness.TicksPerSecond
local function sec(s) return math.floor(s * TICKS_PER_SEC) end

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("Required players not found")
		return
	end

	-- Camera centred on the action.
	Camera.Position = cellPos(28, 16, 0)

	-- Three vehicles spaced 8 cells apart so the fire layers don't blur into
	-- each other. One Western MBT, one Soviet MBT, one IFV — all on USA so
	-- ejected crew (if any) doesn't cross-fire and steal attention.
	local abrams = Actor.Create("abrams", true, {
		Owner = USA,
		Location = CPos.New(16, 16),
		Facing = Angle.South,
	})
	local t90 = Actor.Create("t90", true, {
		Owner = USA,
		Location = CPos.New(28, 16),
		Facing = Angle.South,
	})
	local bmp2 = Actor.Create("bmp2", true, {
		Owner = USA,
		Location = CPos.New(40, 16),
		Facing = Angle.South,
	})
	pcall(function() abrams.Stance = "HoldFire" end)
	pcall(function() t90.Stance = "HoldFire" end)
	pcall(function() bmp2.Stance = "HoldFire" end)

	-- One Apache hovering above the row at altitude 1280 (cruise).
	local heli = Actor.Create("heli", true, {
		Owner = USA,
		CenterPosition = cellPos(52, 16, 1280),
		Facing = Angle.West,
	})
	pcall(function() heli.Stance = "HoldFire" end)

	Media.DisplayMessage("DEMO start — vehicles at full HP, heli hovering",
		"BURN DEMO")

	-- T+2s: damage vehicles to 50% HP. The first onfire stack ignites
	-- immediately at exactly 50%; production-rules bleed at -1% per 5 ticks
	-- carries them down from there, so each stack threshold (1/4/8) lights up
	-- roughly every ~3s as HP slides 50→25→1%.
	Trigger.AfterDelay(sec(2), function()
		Media.DisplayMessage("Vehicles at 50% — first fire stack should ignite",
			"BURN DEMO")
		for _, v in ipairs({ abrams, t90, bmp2 }) do
			if not v.IsDead and v.MaxHealth > 0 then
				v.Health = math.floor(v.MaxHealth * 50 / 100)
			end
		end
	end)

	-- T+8s: heli takes a hit dropping it to 30% (Heavy state). HeliEmergencyLanding
	-- fires StartAutorotation, heli descends. By the time it touches down it'll
	-- already have ~5 stacks of fire (HP ramp = 50→1 mapped to stacks 1→10), and
	-- the ChangesHealth@CrashBurn keeps draining HP after the safe-land.
	Trigger.AfterDelay(sec(8), function()
		Media.DisplayMessage("Heli hit — 30% HP, autorotation, fire scaling",
			"BURN DEMO")
		if not heli.IsDead and heli.MaxHealth > 0 then
			heli.Health = math.floor(heli.MaxHealth * 30 / 100)
		end
	end)

	-- T+22s: vehicles should have detonated by now (10s bleed from 50% → 0%
	-- plus a little aim margin). Heli should be on the ground burning out.
	Trigger.AfterDelay(sec(22), function()
		Media.DisplayMessage("Bleed-out should be wrapping up", "BURN DEMO")
	end)

	-- T+32s: pretty much everything should be a smoking crater. Exit cleanly.
	Trigger.AfterDelay(sec(32), function()
		Media.DisplayMessage("=== Demo complete ===", "BURN DEMO")
		Test.Pass()
	end)
end
