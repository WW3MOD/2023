-- Phase 3 — StancePositioningExecutor opt-out (option b, stance-decoupled).
--
-- Both zones reproduce the geometry that makes a Defensive AR relocate to cover (see
-- test-stance-positioning). The two units here are opted out and must NOT move:
--   HoldUnit  — EngagementStance HoldPosition (map init): the executor declines by stance.
--   DeployUnit — Defensive, but the `deployed` condition is granted from Lua: the executor declines
--                because a deployed unit's placement is deliberate (S2).
--
-- Enablement is the real Phase-3 path (USA is human ⇒ GrantConditionOnHumanOwner grants
-- enable-tactical-positioning at spawn); we also grant explicitly for timing robustness. Assertion:
-- neither unit ever leaves its spawn cell during the hold window ⇒ zero executor Move orders. If the
-- opt-out regressed, a Defensive/enabled unit in this geometry would walk to the y=17 cover edge.

WorldLoaded = function()
	TestHarness.FocusBetween(HoldUnit, DeployUnit)
	TestHarness.Select(HoldUnit)

	local holdHome = { X = HoldUnit.Location.X, Y = HoldUnit.Location.Y }
	local deployHome = { X = DeployUnit.Location.X, Y = DeployUnit.Location.Y }

	-- Executor auto-enables on human-owned units via GrantConditionOnHumanOwner (Phase 3), so no
	-- explicit grant is needed. HoldFire silences shots (no suppression gate, no AutoTarget chase).
	for _, u in ipairs({ HoldUnit, DeployUnit }) do
		if not u.IsDead then u.Stance = "HoldFire" end
	end
	for _, e in ipairs({ EnemyA, EnemyB }) do
		if not e.IsDead then e.Stance = "HoldFire" end
	end

	-- Deploy opt-out: grant the `deployed` condition the executor watches. (The AR isn't running the
	-- deploy activity; granting the condition is exactly what the executor gates on.)
	if not DeployUnit.IsDead then DeployUnit.GrantCondition("deployed") end

	local HOLD = 25 * 25            -- 25s of enforced stillness (executor cadence is 30 ticks)
	local shot = false
	local elapsed = 0

	local poll
	poll = function()
		elapsed = elapsed + 1

		if HoldUnit.IsDead then Test.Fail("HoldUnit died"); return end
		if DeployUnit.IsDead then Test.Fail("DeployUnit died"); return end

		local h = HoldUnit.Location
		if h.X ~= holdHome.X or h.Y ~= holdHome.Y then
			Test.Fail("HoldPosition unit was repositioned to " .. h.X .. "," .. h.Y ..
				" (opt-out regressed)")
			return
		end

		local d = DeployUnit.Location
		if d.X ~= deployHome.X or d.Y ~= deployHome.Y then
			Test.Fail("Deployed unit was repositioned to " .. d.X .. "," .. d.Y ..
				" (opt-out regressed)")
			return
		end

		if not shot and elapsed == math.floor(HOLD / 2) then
			Test.Screenshot("optout-both-held",
				"expects: HoldPosition AR at 13,19 and deployed AR at 40,19 — neither at a cover edge")
			shot = true
		end

		if elapsed >= HOLD then
			Test.Pass()
			return
		end

		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
