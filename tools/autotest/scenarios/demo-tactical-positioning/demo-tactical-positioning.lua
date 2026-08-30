-- DEMO: Phase-3 StancePositioningExecutor. Load it, watch, close when done (End restarts).
-- Use pause / speed to read the small repositioning steps. NO verdict — this is a demo.
--
-- LAYOUT
--   ZONE A (left, top, rows 6..12)   COVER-SEEK. Three Defensive ARs at y=9, a treeline at y=6, an
--     enemy tank sighted at y=12. On load the ARs walk NORTH to the south (threat-facing) cover edge
--     at y=7 and hold hull-down. Watch them shuffle up into the trees at the start.
--   ZONE B (right, rows 13..31)      THREAT RESPONSE. Three Defensive ARs at y=16 with NO enemy in
--     range — they sit idle in the open. A scripted enemy tank (ProbeB) drives up from the south;
--     once it is close enough to register on the threat field the ARs take the y=14 cover edge facing
--     it. The probe then pulls back and the ARs are reset to the open, so the approach→reposition
--     repeats on a loop.
--   ZONE C (left, bottom, rows 24..30) OPT-OUT FREEZE. Same treeline+enemy geometry as zone A, but
--     the group is opted out of tactical positioning: HoldC0..2 are on HoldPosition and DeployC
--     carries the `deployed` condition. None of them ever reposition, even with the enemy sighted.
--
-- Everything is HoldFire (no shots ⇒ nothing dies ⇒ no suppression gate, no AutoTarget chase), so the
-- ONLY thing that moves an idle unit is the executor. Enablement is SCENARIO-LOCAL as of 2026-08-30:
-- the shipped mod no longer grants enable-tactical-positioning to human-owned units, so this demo's
-- rules.yaml re-adds the token to the executor's gate AND the granter. This demo therefore shows a
-- behaviour human players NO LONGER GET in a normal game — it documents the bot-side layer.

local TPS = TestHarness.TicksPerSecond

local function holdFire(actor)
	if actor and not actor.IsDead then actor.Stance = "HoldFire" end
end

WorldLoaded = function()
	-- Frame the left column (zones A + C) with zone B at the right; the user pans/zooms from here.
	TestHarness.FocusBetween(SeekA1, WatchB1, HoldC1)
	TestHarness.Select(SeekA1)

	UserInterface.SetMissionText(
		"Tactical positioning demo — LEFT/top: units seek cover · RIGHT: units react to an approaching probe · LEFT/bottom: HoldPosition + deployed stay put")

	-- Silence every combatant so the only motion is the executor's repositioning.
	local usaUnits = { SeekA0, SeekA1, SeekA2, WatchB0, WatchB1, WatchB2, HoldC0, HoldC1, HoldC2, DeployC }
	for _, u in ipairs(usaUnits) do holdFire(u) end
	for _, e in ipairs({ EnemyA, EnemyC, ProbeB }) do holdFire(e) end

	-- Zone C deploy opt-out: grant the `deployed` condition the executor watches (stands in for a real
	-- deploy — the executor gates on the condition, not the deploy activity).
	if not DeployC.IsDead then DeployC.GrantCondition("deployed") end

	-- ================================================================
	-- Zone B probe loop: approach (sighted → ARs take cover) then retreat (reset ARs to the open).
	-- ================================================================
	local watchers = { WatchB0, WatchB1, WatchB2 }
	local watcherHome = {}
	for i, w in ipairs(watchers) do
		watcherHome[i] = { X = w.Location.X, Y = w.Location.Y }
	end

	local ENGAGE = CPos.New(47, 19)   -- probe drives to here (≈3 cells south of the ARs)
	local RETREAT = CPos.New(47, 31)  -- probe pulls back off the threat field

	local function moveProbe(dest)
		if not ProbeB.IsDead then ProbeB.Move(dest) end
	end

	local function resetWatchers()
		for i, w in ipairs(watchers) do
			if not w.IsDead then w.Move(CPos.New(watcherHome[i].X, watcherHome[i].Y)) end
		end
	end

	local cycle
	cycle = function()
		Media.DisplayMessage("Zone B: no threat in range — watchers idle in the open.", "DEMO")
		moveProbe(ENGAGE)

		Trigger.AfterDelay(10 * TPS, function()
			Media.DisplayMessage("Zone B: probe sighted — watchers pull back to the treeline edge.", "DEMO")
		end)

		Trigger.AfterDelay(20 * TPS, function()
			Media.DisplayMessage("Zone B: probe withdraws.", "DEMO")
			moveProbe(RETREAT)
		end)

		-- Give the probe time to clear the threat field, then push the ARs back into the open so the
		-- approach→reposition can play again.
		Trigger.AfterDelay(28 * TPS, resetWatchers)

		Trigger.AfterDelay(34 * TPS, cycle)
	end

	Trigger.AfterDelay(2 * TPS, cycle)
end
