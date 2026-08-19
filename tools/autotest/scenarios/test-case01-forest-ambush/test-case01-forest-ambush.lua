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
-- VERDICT: this test CAN GO RED. It asserts, in order, (a) that the run actually built the ambush
-- it describes, and (b) Bar B — "every seed def = 0". See the VERDICT block at the bottom for why
-- Bar A's two MEAN clauses are NOT asserted here and where they are asserted instead.
-- (Capture-mode — an unconditional `Test.Pass` — from 2026-07-28 until 2026-08-19.)
--
-- Item-21 gate verification: applyAmbushConcealment fires iff (subject human) && (stance==Ambush)
-- && (cohesion!=Tight) at order time (CohesionMoveModifier.cs:1145). All three are set below BEFORE
-- the single group Move, so the branch provably executes; we additionally log each defender's
-- assigned cohesion slot + density window to show the slots landed in concealment.

local DEF_COST = 100   -- e3.america Valued.Cost
local ATT_COST = 100   -- e3.russia Valued.Cost (same ^E3 base)

-- Bar B, verbatim from the case (`WORKSPACE/cases/case-01-forest-ambush.md`, status log
-- 2026-07-29): "every seed def = 0". Cost-denominated here; 0 deaths = 0cr. This is the ONE
-- clause of the mined bar that a single seed can decide, so it is the one the per-run verdict
-- carries. Flip this constant only with the user's say-so — the bar is theirs.
local BAR_B_DEF_MAX_LOSS = 0

local SETTLE_TICKS   = 250            -- ~10s for the squad to walk in and item-21 to reseat
local MEASURE_SECS   = 90             -- combat deadline after the attackers launch
local TPS            = TestHarness.TicksPerSecond

local Defenders = { D1, D2, D3, D4, D5 }
local Attackers = { A1, A2, A3, A4, A5 }

-- Per-kill + return-fire instrumentation (item 260729 bar-mining §6). EXTRA LOGGING ONLY —
-- none of this feeds Test.Pass/Fail; the verdict semantics are unchanged. Kill events go to
-- debug.log via print() (the same per-unit channel as the settle snapshot); compact kill-curve
-- and defender-damage aggregates are additionally folded into the surviving verdict note so they
-- outlive an overwritten debug.log.
local launchTick   = nil          -- DateTime.GameTime captured when the attackers are launched
local firstAttKillT = nil         -- ticks-since-launch of the FIRST attacker death (burst onset)
local lastAttKillT  = nil         -- ticks-since-launch of the LAST attacker death (attrition tail)
local attKilled     = 0           -- attacker deaths seen via the kill hook (cross-check on liveCount)
local defDmg        = {}           -- per-defender cumulative damage taken (index matches Defenders)
local defDmgFirstT  = {}           -- ticks-since-launch of first damage on that defender, nil if none

-- Ticks elapsed since the attackers launched (-1 before launch; deaths shouldn't occur pre-launch).
local function sinceLaunch()
	if launchTick == nil then return -1 end
	return DateTime.GameTime - launchTick
end

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

	-- Instrumentation hooks (extra logging only — do NOT affect the verdict). Register at
	-- WorldLoaded so damage/deaths across the whole scenario are caught; ticks are reported
	-- relative to attacker launch (sinceLaunch()), so pre-launch events read t=-1 (shouldn't occur).
	for i, d in ipairs(Defenders) do
		defDmg[i] = 0
		defDmgFirstT[i] = nil
		if d and not d.IsDead then
			local idx, utype = i, d.Type
			-- Return-fire signal: cumulative damage TAKEN by this defender. Distinguishes
			-- "attackers fired at / damaged defenders" from "attackers died blind" (§4 inference).
			Trigger.OnDamaged(d, function(self, attacker, damage)
				if damage and damage > 0 then
					defDmg[idx] = defDmg[idx] + damage
					if defDmgFirstT[idx] == nil then defDmgFirstT[idx] = sinceLaunch() end
				end
			end)
			Trigger.OnKilled(d, function(self, killer)
				local st = sinceLaunch()
				print(string.format(
					"[case01] KILL side=DEF type=%s cost=%d tick=%d t=%.1fs dmgTaken=%d",
					utype, DEF_COST, DateTime.GameTime, st / TPS, defDmg[idx]))
			end)
		end
	end
	for _, a in ipairs(Attackers) do
		if a and not a.IsDead then
			local utype = a.Type
			Trigger.OnKilled(a, function(self, killer)
				local st = sinceLaunch()
				attKilled = attKilled + 1
				if firstAttKillT == nil then firstAttKillT = st end
				lastAttKillT = st
				print(string.format(
					"[case01] KILL side=ATT type=%s cost=%d tick=%d t=%.1fs",
					utype, ATT_COST, DateTime.GameTime, st / TPS))
			end)
		end
	end

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
		launchTick = DateTime.GameTime   -- baseline for per-kill / first-damage tick deltas
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

				-- Aggregate the return-fire (defender damage-taken) signal + kill-curve into the
				-- surviving note. These are logging-only; they do not gate the verdict.
				local secs = function(t) if t == nil or t < 0 then return "-" end return string.format("%.1f", t / TPS) end
				local defDmgTotal, defDmgd = 0, 0
				for i = 1, #Defenders do
					defDmgTotal = defDmgTotal + (defDmg[i] or 0)
					if (defDmg[i] or 0) > 0 then defDmgd = defDmgd + 1 end
				end

				-- Per-defender damage-taken fingerprint to debug.log (mirrors the settle snapshot).
				for i = 1, #Defenders do
					print(string.format(
						"[case01] D%d dmgTaken=%d firstDmg=%ss alive=%s",
						i, defDmg[i] or 0, secs(defDmgFirstT[i]),
						tostring(Defenders[i] and not Defenders[i].IsDead)))
				end

				local note = string.format(
					"defLoss=%d attLoss=%d ratio=%.2f survDef=%d/5 survAtt=%d/5 sprang=%s refined=%d/5 resolved=%s t=%.1fs "
					.. "firstKill=%ss lastKill=%ss attKilled=%d defDmgd=%d/5 defDmgTot=%d",
					defLoss, attLoss, ratio, liveDef, liveAtt,
					tostring(sprang), refinedCount,
					tostring(resolved), elapsed / TPS,
					secs(firstAttKillT), secs(lastAttKillT), attKilled, defDmgd, defDmgTotal)

				print("[case01] RESULT " .. note)

				-- ---- VERDICT ----------------------------------------------------------------
				-- The bar has two levels, so the assertion does too.
				--
				--   Bar B  "every seed def = 0"                    -> per-seed. Asserted HERE.
				--   Bar A  "mean def <= 50cr AND mean att >= 300cr
				--           over >=6 seeds"                        -> a MEAN. Asserted by
				--          `tools/autotest/parse-case01-bar.py` over a >=6-run batch.
				--
				-- Bar A is deliberately NOT collapsed into a per-run test. A per-seed
				-- `attLoss >= 300` is a DIFFERENT and stricter bar than the one written: the
				-- 2026-07-28 batch the bar was mined from — the batch the user is being asked to
				-- ratify as GREEN — contains seed 5005 at attLoss=200 and two seeds at exactly
				-- 300. Gating each run on the mean would call that reference batch RED.
				--
				-- Setup validity is checked FIRST, because defLoss==0 is also what a world that
				-- never happened produces: if the attackers never launch, never close, or never
				-- engage, the defenders lose nothing and a bare Bar-B test reports PASS. That is
				-- the false-green shape in AUTOTEST.md ("a green run is not evidence unless
				-- something could have made it RED") — so the run must show it built the ambush
				-- before its zero is allowed to mean anything.
				local invalid = nil
				if refinedCount ~= #Defenders then
					invalid = string.format(
						"item-21 seated %d/%d defenders in cover (densWin>0), expected %d; "
						.. "the concealment mechanism under test did not run",
						refinedCount, #Defenders, #Defenders)
				elseif attDead < 1 then
					-- No other damage source exists on this map (the only other actors are the
					-- two supplyroutes, which are NoAutoTarget), so an attacker death is
					-- attributable to the defenders. Zero of them means no ambush occurred.
					-- Threshold 1 sits well below the 2026-07-28 per-seed floor of 2 kills, so
					-- it cannot flip a seed the mined bar counts as green.
					invalid = "no attacker died; the ambush never engaged, so defLoss=0 is vacuous"
				end

				if invalid ~= nil then
					Test.Fail("case01 SETUP-INVALID: " .. invalid .. " | " .. note)
					return
				end

				if defLoss <= BAR_B_DEF_MAX_LOSS then
					Test.Pass(string.format("case01 BAR-B PASS defLoss=%d | %s", defLoss, note))
				else
					Test.Fail(string.format(
						"case01 BAR-B FAIL: defenders lost %dcr (%d/%d killed, %d took damage), "
						.. "bar requires %dcr | %s",
						defLoss, defDead, #Defenders, defDmgd, BAR_B_DEF_MAX_LOSS, note))
				end
				return
			end

			Trigger.AfterDelay(1, poll)
		end
		Trigger.AfterDelay(1, poll)
	end)
end
