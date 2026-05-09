-- DEMO compare: 10 burn-variant columns × 5 vehicle types each = 50 wrecks.
-- Each column shows the SAME burn config across 5 different vehicles, so
-- the player can compare how variant N looks across humvee / m113 / abrams /
-- bradley / m270.
--
-- 51% damage hit at T=0; from there production-rules ChangesHealth (-1%
-- MaxHP / 5 ticks once below 50%) carries everyone down at the same rate,
-- so all 50 race the SAME HP curve and the visual difference is purely
-- the burn config in rules.yaml.
--
-- No Test.Pass — window stays open. Close when done.

local function announce(msg)
	Media.DisplayMessage("[LUA] " .. msg, "BURN COMPARE")
end

WorldLoaded = function()
	announce("WorldLoaded fired.")
	local OWNER = Player.GetPlayer("Neutral")
	if OWNER == nil then
		announce("Neutral player missing — aborting.")
		return
	end

	-- Layout: 11 columns × 5 rows. Columns 5 cells apart so 11 columns fit
	-- in the 64-cell map width (last column at x=54).
	local cols = 11
	local vehicles = { "humvee", "m113", "abrams", "bradley", "m270" }
	local spawned = {}
	local failed = 0

	for col = 1, cols do
		for row = 1, #vehicles do
			local vtype = vehicles[row] .. ".v" .. col
			local x = 4 + (col - 1) * 5
			local y = 4 + (row - 1) * 5
			local v = Actor.Create(vtype, true, {
				Owner = OWNER,
				Location = CPos.New(x, y),
				Facing = Angle.South,
			})
			if v == nil then
				failed = failed + 1
			else
				table.insert(spawned, v)
			end
		end
	end

	announce("Spawn loop done. " .. #spawned .. " spawned, " .. failed .. " failed.")
	Camera.Position = WPos.New(32 * 1024 + 512, 14 * 1024 + 512, 0)

	-- Fill cargo on every cargo-capable vehicle with 2 riflemen so the user
	-- can verify passengers eject when the vehicle dies. Skip vehicles
	-- without Cargo (humvee/m113/btr/bmp2/bradley have it; m270 doesn't).
	local loaded = 0
	for _, v in ipairs(spawned) do
		for i = 1, 2 do
			-- addToWorld=false: passenger lives only inside cargo. If we
			-- pre-add them, World.Add will throw 'duplicate key' when the
			-- vehicle ejects them on death.
			local soldier = Actor.Create("e3.america", false, {
				Owner = OWNER,
				Location = v.Location,
				Facing = Angle.South,
			})
			if soldier ~= nil then
				local ok = pcall(function() v.LoadPassenger(soldier) end)
				if ok then
					loaded = loaded + 1
				else
					-- Vehicle had no Cargo trait — discard the unused actor.
					pcall(function() soldier.Destroy() end)
				end
			end
		end
	end
	announce("Loaded " .. loaded .. " riflemen into cargo-capable vehicles.")

	announce("Applying 51% damage to all 55 — natural bleed takes over.")
	for _, v in ipairs(spawned) do
		if not v.IsDead and v.MaxHealth > 0 then
			v.Health = math.floor(v.MaxHealth * 49 / 100)
		end
	end

	announce("V1..V5 are slower/less burning than V6 (the previous favourite, in the centre).")
	announce("V7..V11 are faster/more burning than V6, V11 ≈ production. Watch for the smoothest progression.")
end
