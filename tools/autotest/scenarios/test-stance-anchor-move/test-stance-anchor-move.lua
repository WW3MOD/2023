-- Phase 3 — StancePositioningExecutor anchor lifecycle (B1) + arrival tolerance (S3).
--
-- B1 (stale anchor). The Rifle spawns at zone A (13,19), south of a treeline, enemy sighted south.
-- The executor relocates it to the zone-A south cover edge (y=17) and anchors there. The test then
-- issues a scripted player Move 27 cells EAST to zone B (40,19). With the FIX, the executor detects
-- the unit is now outside the leash of the zone-A anchor, invalidates that anchor, and re-anchors to
-- zone B — so it NEVER walks the Rifle back toward zone A. With the bug it targets a cover edge within
-- leash of the fossil anchor A and drags the Rifle ~27 cells back west. Fail signature: Rifle comes
-- back within 6 cells of the zone-A anchor.
--
-- S3 (arrival tolerance). Two ARs (PairL, PairR) sit south of a third treeline with one enemy. Each
-- relocates to a cover edge (y=26) and must then HOLD its exact cell — no oscillation. The 1-cell
-- arrival tolerance is what prevents a shoved unit from mis-reading the bump as a player interrupt and
-- churning; this asserts the steady-state no-oscillation invariant for a two-unit cover contest.
--
-- Enablement is the real Phase-3 path: USA is human, so GrantConditionOnHumanOwner grants
-- enable-tactical-positioning at spawn. We also grant it explicitly (idempotent) so the test is robust
-- to grant-timing. The units-under-test stay FireAtWill — the executor declines anything below it
-- (StancePositioningExecutor.cs:318) — and combat is silenced from the ENEMY side instead: the t90s are
-- HoldFire and are made non-auto-targetable in rules.yaml.

local A = { X = 13, Y = 19 }   -- zone-A anchor (Rifle spawn)
local B = { X = 40, Y = 19 }   -- zone-B relocation target

local function dist(loc, p) return math.abs(loc.X - p.X) + math.abs(loc.Y - p.Y) end

WorldLoaded = function()
	TestHarness.FocusBetween(Rifle, EnemyB)
	TestHarness.Select(Rifle)

	-- Executor auto-enables on human-owned units via GrantConditionOnHumanOwner (Phase 3), so no
	-- explicit grant is needed. The ARs must be FireAtWill: the executor relinquishes management of any
	-- unit below FireAtWill (the deliberate Ambush/HoldFire opt-out), so silencing them by fire stance —
	-- as this test used to — switches the trait under test off entirely.
	local combatants = { Rifle, PairL, PairR }
	for _, u in ipairs(combatants) do
		if not u.IsDead then u.Stance = "FireAtWill" end
	end
	for _, e in ipairs({ EnemyA, EnemyB, EnemyS }) do
		if not e.IsDead then e.Stance = "HoldFire" end
	end

	-- Deadlines (25 ticks/sec). Infantry Speed is 25 (~41 ticks/cell), so the 27-cell relocation
	-- takes ~1100+ ticks — the relocate window must be generous. Run at --speed 8 so this is a few
	-- seconds of real time.
	local ARRIVE_A_DEADLINE = 25 * 30
	local RELOCATE_DEADLINE = 25 * 80   -- 27 cells at ~41 ticks/cell ≈ 1100 ticks + margin
	local B1_HOLD = 25 * 12             -- ticks the Rifle must stay near B without returning to A
	local S3_ARRIVE_DEADLINE = 25 * 35
	local S3_HOLD = 250                 -- ticks each pair unit must hold its cover cell
	local HARD_TIMEOUT = 25 * 200

	-- B1 state machine: reach-A -> relocate -> arrive-B -> hold-near-B.
	local b1phase = "reachA"
	local b1_moveIssued = false
	local b1_bWait = 0
	local b1_holdCount = 0
	local b1_done = false
	local b1_shot = false

	-- S3 per-unit state.
	local s3 = {}
	for _, u in ipairs({ { a = PairL, k = "L" }, { a = PairR, k = "R" } }) do
		s3[u.k] = { actor = u.a, arrived = false, rest = nil, hold = 0, done = false }
	end

	local elapsed = 0

	local function stepB1()
		if b1_done then return end
		if Rifle.IsDead then Test.Fail("B1: Rifle died"); return end
		local loc = Rifle.Location

		if b1phase == "reachA" then
			-- zone-A south cover edge is y==17, inside the 4-cell leash of A.
			if loc.Y == 17 and dist(loc, A) <= 4 then
				b1phase = "relocate"
			elseif elapsed >= ARRIVE_A_DEADLINE then
				Test.Fail("B1: Rifle never reached zone-A cover edge; at " .. loc.X .. "," .. loc.Y)
			end
		elseif b1phase == "relocate" then
			if not b1_moveIssued then
				Rifle.Move(CPos.New(B.X, B.Y))
				b1_moveIssued = true
			end
			-- Reached zone B once we're within the leash of B (the executor may pull the Rifle from
			-- the (40,19) drop cell to the (40,17) cover edge — both are within 5 of B, both far from A).
			if dist(loc, B) <= 5 then
				b1phase = "holdB"
			elseif elapsed >= ARRIVE_A_DEADLINE + RELOCATE_DEADLINE then
				Test.Fail("B1: Rifle never reached zone B after relocate; at " .. loc.X .. "," .. loc.Y)
			end
		elseif b1phase == "holdB" then
			-- THE B1 assertion: the Rifle must never walk back toward the abandoned zone-A anchor.
			if dist(loc, A) <= 6 then
				Test.Fail("B1: Rifle walked back toward stale zone-A anchor; at " .. loc.X .. "," .. loc.Y)
				return
			end
			if not b1_shot and dist(loc, B) <= 4 and loc.Y == 17 then
				-- Bonus positive signal: it re-targeted a cover edge within leash of the NEW anchor.
				Test.Screenshot("re-anchored-zone-b",
					"expects: Rifle at a zone-B cover edge (y=17, x~37..43), NOT back west at zone A")
				b1_shot = true
			end
			b1_holdCount = b1_holdCount + 1
			if b1_holdCount >= B1_HOLD then
				b1_done = true
			end
		end
	end

	local function stepS3()
		for _, st in pairs(s3) do
			if not st.done then
				if st.actor.IsDead then Test.Fail("S3: a pair unit died"); return end
				local loc = st.actor.Location
				if not st.arrived then
					-- zone-S3 south cover edge is y==26 (treeline y=25).
					if loc.Y == 26 then
						st.arrived = true
						st.rest = { X = loc.X, Y = loc.Y }
					elseif elapsed >= S3_ARRIVE_DEADLINE then
						Test.Fail("S3: a pair unit never reached cover; at " .. loc.X .. "," .. loc.Y)
						return
					end
				else
					if loc.X ~= st.rest.X or loc.Y ~= st.rest.Y then
						Test.Fail("S3: pair unit oscillated off " .. st.rest.X .. "," .. st.rest.Y ..
							" to " .. loc.X .. "," .. loc.Y)
						return
					end
					st.hold = st.hold + 1
					if st.hold >= S3_HOLD then st.done = true end
				end
			end
		end
	end

	local poll
	poll = function()
		elapsed = elapsed + 1

		stepB1()
		stepS3()

		if b1_done and s3.L.done and s3.R.done then
			Test.Pass()
			return
		end

		if elapsed >= HARD_TIMEOUT then
			Test.Fail("hard timeout; b1phase=" .. b1phase .. " b1hold=" .. b1_holdCount ..
				" s3L=" .. tostring(s3.L.done) .. " s3R=" .. tostring(s3.R.done))
			return
		end

		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
