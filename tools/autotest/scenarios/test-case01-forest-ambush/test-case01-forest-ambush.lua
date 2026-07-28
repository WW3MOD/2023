-- CASE-01 — Forest ambush measurement scenario (pipeline item 22).
--
-- Defenders (USA, HUMAN, Ambush stance, enable-ambush-tactics granted) get ONE group Move
-- order into the south cover patch of the kill-clearing map; item-21 (CohesionMoveModifier
-- ambush branch) re-seats each formation slot onto the most concealed cell, then they HOLD.
-- Attackers (Russia, scripted) attack-move south through the concealment wall and across the
-- open clearing; the concealed defenders (undetectable, in DensityModifiesDamage cover) spring
-- and cut down the exposed attackers who mostly can't return effective fire. See map.yaml for
-- the wall / clearing / cover-patch geometry and why simpler groves failed.
--
-- Measurement: cost-weighted losses (e3 = 100cr both factions, verified from ^E3 Valued.Cost).
-- The verdict note carries machine-parseable metrics; the debug.log gets the same via print().
--
-- CALIBRATION MODE: this test PASSES whenever the sim resolves and losses are captured — it does
-- NOT fail on the provisional >=1:3 ratio (the bar is not ratified yet). Once ratified, replace
-- the `Test.Pass(note)` at finish with a ratio gate (see the FINISH block).
--
-- Item-21 gate verification: applyAmbushConcealment fires iff (subject human) && (stance==Ambush)
-- && (cohesion!=Tight) at order time (CohesionMoveModifier.cs:1145). All three are set below BEFORE
-- the single group Move, so the branch provably executes; we additionally log each defender's
-- assigned cohesion slot + density window to show the slots landed in concealment.

local DEF_COST = 100   -- e3.america Valued.Cost
local ATT_COST = 100   -- e3.russia Valued.Cost (same ^E3 base)

local SETTLE_TICKS   = 250            -- ~10s for the squad to walk in and item-21 to reseat
local MEASURE_SECS   = 90             -- combat deadline after the attackers launch
local TPS            = TestHarness.TicksPerSecond

local Defenders = { D1, D2, D3, D4, D5 }
local Attackers = { A1, A2, A3, A4, A5 }

local function liveCount(team)
	local n = 0
	for _, a in ipairs(team) do if a and not a.IsDead then n = n + 1 end end
	return n
end

-- 5x5 density window around a cell (uses the same DensityLayer item-21 scores on).
local function densityWindow(cell)
	local sum = 0
	for dx = -2, 2 do
		for dy = -2, 2 do
			sum = sum + Test.GetDensity(CPos.New(cell.X + dx, cell.Y + dy))
		end
	end
	return sum
end

local function totalAmmo(team)
	local n = 0
	for _, a in ipairs(team) do
		if a and not a.IsDead then n = n + a.AmmoCount("primary-ammo") end
	end
	return n
end


WorldLoaded = function()
	local russia = Player.GetPlayer("Russia")
	local refinedCount = 0
	local defStartAmmo = 0

	TestHarness.FocusBetween(D3, A3)
	TestHarness.Select(D3)

	-- Configure the defenders BEFORE the order so the item-21 ambush branch is armed:
	-- Ambush stance (hold-fire until sprung + gates the concealment refinement) and Loose
	-- cohesion (!= Tight). Grant enable-ambush-tactics so the Stage-3 spring machine runs.
	for _, d in ipairs(Defenders) do
		if not d.IsDead then
			d.Stance = "Ambush"
			d.GrantCondition("enable-ambush-tactics")
			Test.SetCohesion(d, "Loose")
		end
	end

	-- The SINGLE group order. Test.GroupMove routes through the real order pipeline so
	-- IModifyGroupOrder (CohesionMoveModifier) fires — Move (not AttackMove) so Ambush units
	-- don't halt-before-contact (Stage 2 only touches attack/auto-move).
	Test.GroupMove(Defenders, CPos.New(32, 20), "Move")

	print("[case01] order issued: 5 defenders Ambush/Loose, group Move -> (32,20)")

	Trigger.AfterDelay(SETTLE_TICKS, function()
		-- Snapshot concealment seating (item-21 verification + hidden-until-close premise).
		local refined = 0
		for i, d in ipairs(Defenders) do
			if not d.IsDead then
				local loc = d.Location
				local slot = Test.GetCohesionSlot(d)
				local dens = densityWindow(loc)
				if dens > 0 then refined = refined + 1 end
				print(string.format(
					"[case01] D%d settled=(%d,%d) slot=(%d,%d) densWin=%d visFromRussia=%d",
					i, loc.X, loc.Y, slot.X, slot.Y, dens, Test.GetVisibility(russia, loc)))
			end
		end
		print(string.format("[case01] refined(seated-in-cover)=%d/5", refined))
		refinedCount = refined
		defStartAmmo = totalAmmo(Defenders)

		Test.Screenshot("settled-in-grove",
			"expects: 5 USA defenders concealed inside the tree grove, holding in Ambush")

		-- Launch the scripted attackers: each attack-moves straight down its column, through
		-- the grove, to a cell south of the defenders. Deterministic (harness seed only).
		for _, a in ipairs(Attackers) do
			if not a.IsDead then a.AttackMove(CPos.New(a.Location.X, 28)) end
		end
		print("[case01] attackers launched: attack-move south through grove")

		-- Measurement poll.
		local deadlineTicks = math.floor(MEASURE_SECS * TPS)
		local elapsed = 0
		local poll
		poll = function()
			elapsed = elapsed + 1
			local liveDef = liveCount(Defenders)
			local liveAtt = liveCount(Attackers)
			local resolved = (liveDef == 0) or (liveAtt == 0)

			if resolved or elapsed >= deadlineTicks then
				local defDead = (#Defenders - liveDef)
				local attDead = (#Attackers - liveAtt)
				local defLoss = defDead * DEF_COST
				local attLoss = attDead * ATT_COST
				local ratio
				if defLoss > 0 then ratio = attLoss / defLoss else ratio = attLoss end  -- attacker loss per 1 defender loss
				local sprang = (totalAmmo(Defenders) < defStartAmmo) or (attDead > 0)

				local note = string.format(
					"defLoss=%d attLoss=%d ratio=%.2f survDef=%d/5 survAtt=%d/5 sprang=%s refined=%d/5 resolved=%s t=%.1fs",
					defLoss, attLoss, ratio, liveDef, liveAtt,
					tostring(sprang), refinedCount,
					tostring(resolved), elapsed / TPS)

				print("[case01] RESULT " .. note)

				-- CALIBRATION: capture-mode pass. After ratification, replace with a ratio gate, e.g.:
				--   if defLoss == 0 or (attLoss / defLoss) >= RATIFIED_RATIO then Test.Pass(note)
				--   else Test.Fail(note) end
				Test.Pass(note)
				return
			end

			Trigger.AfterDelay(1, poll)
		end
		Trigger.AfterDelay(1, poll)
	end)
end
