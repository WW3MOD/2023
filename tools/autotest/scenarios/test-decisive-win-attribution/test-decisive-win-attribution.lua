-- Regression: a decisive defeat must resolve to exactly ONE winner and ONE loser.
--
-- Bug (2026-07-30): spectating a 1v1, one bot was defeated and BOTH sides showed
-- "Lost" with no winner declared. Root cause: the explicit win-award lived only in
-- SupplyRouteContestation.ResolveTeamElimination (the contestation-elimination path).
-- Any OTHER defeat path (loss of required units, surrender, a near-simultaneous mutual
-- defeat) left the win to ConquestVictoryConditions.Tick's next-tick inference, which
-- no-ops once the survivor is itself marked Lost — so the survivor was never awarded and
-- the end screen mirrored WinState as "everyone Lost".
--
-- This drives a decisive end by defeating the enemy (the same MarkFailed path elimination
-- and surrender run through) and asserts the surviving combatant is awarded "Won". Pre-fix
-- the survivor stays "Undefined" (no winner) → RED; post-fix it is awarded → GREEN.

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")

	TestHarness.FocusBetween(OwnSR, OpponentSR)

	-- Simulate the losing side being defeated a moment into the match.
	Trigger.AfterDelay(25, function()
		local id = russia.AddPrimaryObjective("Hold the Supply Route")
		russia.MarkFailedObjective(id)
	end)

	TestHarness.AssertWithin(6, function()
		if russia.WinState ~= "Lost" then
			return false
		end

		-- The enemy is defeated; the survivor MUST now be the declared winner.
		if usa.WinState == "Lost" then
			return "fail: surviving side was also marked Lost (no winner declared)"
		end

		return usa.WinState == "Won"
	end, "enemy was defeated but the survivor was never awarded Won (both sides left non-Won)")
end
