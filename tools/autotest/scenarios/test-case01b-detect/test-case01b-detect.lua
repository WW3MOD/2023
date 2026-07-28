-- CASE-01B — Forest ambush, DETECT variant (fire-lane measurability).
--
-- Clones test-case01-forest-ambush's staging exactly: 5 USA defenders (HUMAN, Ambush stance,
-- enable-ambush-tactics granted, Loose cohesion) get ONE group Move order into the south cover
-- patch; item-21 (CohesionMoveModifier ambush branch) re-seats each formation slot onto the most
-- OMNIDIRECTIONALLY concealed cell, then they HOLD. 5 Russia attackers (scripted) attack-move
-- south through the wall and across the open clearing.
--
-- THE DIFFERENCE FROM case-01: a map-local rules override lowers the DEFENDERS' Detectable.Vision
-- to 1 (rules.yaml), so the attackers ACQUIRE them and the engagement actually resolves. The
-- calibration batch could never resolve the fight — concealed defenders were undetectable, so it
-- measured concealment, not fighting.
--
-- WHAT THIS MEASURES (WORKSPACE/recon/260729-firing-lane-seating.md): ground-shadow interposition
-- is direction-symmetric, so the tree density that hides a seated defender ALSO blocks its own
-- outgoing DMR fire (5.56mm.DMR, engine-default ClearSightThreshold 5). item-21 maximises
-- omnidirectional window density, which can bury a defender in a fire-blocked seat. Only the
-- defender's DETECTABILITY is raised here — its detection-of-attacker and firing-at-attacker are
-- left to the honest symmetric shadow byte — so a fire-blocked seat shows up as "sprang late /
-- never / few shots", not masked by the attacker being blind.
--
-- Per-defender fire-lane metrics land in the verdict `note` (aggregate) and debug.log (per unit):
--   * everFired  — did this defender's rifle ammo ever drop (it got at least one shot off)?
--   * ttfShot    — ticks/seconds from ATTACKER LAUNCH to that defender's first shot.
--   * shots      — cumulative rifle shots (monotonic: e3 has no replenish-soldiers here, so
--                  primary-ammo never reloads and each drop is a real shot).
--   * casualties — defenders killed / attackers killed, cost-weighted.
--
-- CALIBRATION MODE: this test PASSES on resolution or deadline once metrics are captured. The value
-- is the fire-lane metrics, not a pass/fail verdict (never Test.Pass-less — that would be a demo).

local DEF_COST = 100   -- e3.america Valued.Cost
local ATT_COST = 100   -- e3.russia Valued.Cost (same ^E3 base)

local SETTLE_TICKS = 250            -- ~10s for the squad to walk in and item-21 to reseat
local MEASURE_SECS = 90             -- combat deadline after the attackers launch
local TPS          = TestHarness.TicksPerSecond

local Defenders = { D1, D2, D3, D4, D5 }
local Attackers = { A1, A2, A3, A4, A5 }

-- Per-defender fire-lane state (indices match Defenders).
local defStart   = {}   -- primary-ammo at launch
local defLast    = {}   -- last observed primary-ammo (frozen on death)
local defShots   = {}   -- cumulative rifle shots
local defEver    = {}   -- ever fired?
local defTtf     = {}   -- ticks (since launch) of first shot, -1 if never

-- Attacker aggregate shot state (sanity: proves the DETECT override made attackers engage).
local attLast    = {}
local attShots   = {}

local function liveCount(team)
	local n = 0
	for _, a in ipairs(team) do if a and not a.IsDead then n = n + 1 end end
	return n
end

-- 5x5 density window around a cell (the same DensityLayer item-21 scores on).
local function densityWindow(cell)
	local sum = 0
	for dx = -2, 2 do
		for dy = -2, 2 do
			sum = sum + Test.GetDensity(CPos.New(cell.X + dx, cell.Y + dy))
		end
	end
	return sum
end

local function ammoOf(a)
	if a and not a.IsDead then return a.AmmoCount("primary-ammo") end
	return nil
end

WorldLoaded = function()
	local russia = Player.GetPlayer("Russia")
	local refinedCount = 0

	TestHarness.FocusBetween(D3, A3)
	TestHarness.Select(D3)

	-- Arm the item-21 ambush branch BEFORE the order: Ambush stance (holds fire until sprung +
	-- gates the concealment refinement), Loose cohesion (!= Tight), enable-ambush-tactics (Stage-3
	-- spring machine). All three set here so the branch provably executes.
	for _, d in ipairs(Defenders) do
		if not d.IsDead then
			d.Stance = "Ambush"
			d.GrantCondition("enable-ambush-tactics")
			Test.SetCohesion(d, "Loose")
		end
	end

	-- The SINGLE group order. Test.GroupMove routes through the real order pipeline so
	-- IModifyGroupOrder (CohesionMoveModifier) fires — Move (not AttackMove) so Ambush units don't
	-- halt-before-contact.
	Test.GroupMove(Defenders, CPos.New(32, 20), "Move")
	print("[case01b] order issued: 5 defenders Ambush/Loose, group Move -> (32,20)")

	Trigger.AfterDelay(SETTLE_TICKS, function()
		-- Snapshot seating (item-21 verification) and initialise per-unit fire-lane state.
		local refined = 0
		for i, d in ipairs(Defenders) do
			if d and not d.IsDead then
				local loc  = d.Location
				local slot = Test.GetCohesionSlot(d)
				local dens = densityWindow(loc)
				if dens > 0 then refined = refined + 1 end
				local a0 = ammoOf(d) or 0
				defStart[i] = a0
				defLast[i]  = a0
				defShots[i] = 0
				defEver[i]  = false
				defTtf[i]   = -1
				print(string.format(
					"[case01b] D%d settled=(%d,%d) slot=(%d,%d) densWin=%d startAmmo=%d visFromRussia=%d",
					i, loc.X, loc.Y, slot.X, slot.Y, dens, a0, Test.GetVisibility(russia, loc)))
			else
				defStart[i] = 0; defLast[i] = 0; defShots[i] = 0; defEver[i] = false; defTtf[i] = -1
			end
		end
		refinedCount = refined
		print(string.format("[case01b] refined(seated-in-cover)=%d/5", refined))

		for i, a in ipairs(Attackers) do
			attLast[i]  = ammoOf(a) or 0
			attShots[i] = 0
		end

		Test.Screenshot("settled-in-grove",
			"expects: 5 USA defenders seated in the south cover patch, holding in Ambush")

		-- Launch the scripted attackers: each attack-moves straight down its column, through the
		-- wall and across the clearing, to a cell south of the defenders. Deterministic.
		for _, a in ipairs(Attackers) do
			if not a.IsDead then a.AttackMove(CPos.New(a.Location.X, 28)) end
		end
		print("[case01b] attackers launched: attack-move south through grove")

		-- Measurement poll (every tick; `elapsed` is ticks since attacker launch).
		local deadlineTicks = math.floor(MEASURE_SECS * TPS)
		local elapsed = 0
		local poll
		poll = function()
			elapsed = elapsed + 1

			-- Accumulate defender rifle shots (monotonic drops = shots fired).
			for i, d in ipairs(Defenders) do
				local a = ammoOf(d)
				if a ~= nil and defLast[i] ~= nil then
					if a < defLast[i] then
						defShots[i] = defShots[i] + (defLast[i] - a)
						if not defEver[i] then
							defEver[i] = true
							defTtf[i]  = elapsed
						end
					end
					defLast[i] = a
				end
			end
			-- Attacker aggregate shots (engagement sanity).
			for i, a in ipairs(Attackers) do
				local am = ammoOf(a)
				if am ~= nil and attLast[i] ~= nil then
					if am < attLast[i] then attShots[i] = attShots[i] + (attLast[i] - am) end
					attLast[i] = am
				end
			end

			local liveDef = liveCount(Defenders)
			local liveAtt = liveCount(Attackers)
			local resolved = (liveDef == 0) or (liveAtt == 0)

			if resolved or elapsed >= deadlineTicks then
				local defDead = (#Defenders - liveDef)
				local attDead = (#Attackers - liveAtt)
				local defLoss = defDead * DEF_COST
				local attLoss = attDead * ATT_COST

				-- Aggregate fire-lane metrics.
				local firedCount, totDefShots = 0, 0
				local ttfMin, ttfMax, ttfSum, ttfN = nil, nil, 0, 0
				for i = 1, #Defenders do
					totDefShots = totDefShots + (defShots[i] or 0)
					if defEver[i] then
						firedCount = firedCount + 1
						local t = defTtf[i]
						if ttfMin == nil or t < ttfMin then ttfMin = t end
						if ttfMax == nil or t > ttfMax then ttfMax = t end
						ttfSum = ttfSum + t; ttfN = ttfN + 1
					end
				end
				local totAttShots = 0
				for i = 1, #Attackers do totAttShots = totAttShots + (attShots[i] or 0) end

				local function secs(t) if t == nil then return "-" end return string.format("%.1f", t / TPS) end
				local ttfMean = (ttfN > 0) and (ttfSum / ttfN) or nil

				-- Per-defender detail to debug.log (the fire-lane fingerprint).
				for i = 1, #Defenders do
					print(string.format(
						"[case01b] D%d everFired=%s ttfShot=%s shots=%d alive=%s",
						i, tostring(defEver[i]), secs(defTtf[i] >= 0 and defTtf[i] or nil),
						defShots[i] or 0, tostring(Defenders[i] and not Defenders[i].IsDead)))
				end

				local note = string.format(
					"defFired=%d/5 ttfShot(min/mean/max)=%s/%s/%ss defShots=%d attShots=%d "
					.. "defKilled=%d/5 attKilled=%d/5 defLoss=%d attLoss=%d refined=%d/5 resolved=%s t=%.1fs",
					firedCount, secs(ttfMin), secs(ttfMean), secs(ttfMax),
					totDefShots, totAttShots,
					defDead, attDead, defLoss, attLoss, refinedCount,
					tostring(resolved), elapsed / TPS)

				print("[case01b] RESULT " .. note)

				-- CALIBRATION: capture-mode pass. The metrics are the deliverable, not a verdict.
				Test.Pass(note)
				return
			end

			Trigger.AfterDelay(1, poll)
		end
		Trigger.AfterDelay(1, poll)
	end)
end
