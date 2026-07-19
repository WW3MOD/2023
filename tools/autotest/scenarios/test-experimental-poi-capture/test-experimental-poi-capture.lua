-- FOCUSED IN-GAME CAPTURE ASSERTION. POI-strategy Phase 2 (PART A).
--
-- A single close, safe, high-value derrick (CloseBio) is the unambiguous top
-- PoiMap pick. The experimental bot's pre-placed escorted TECN should walk ~8c and
-- capture it well within the window. Asserts:
--   * capture COMPLETES: CloseBio ends up owned by the experimental bot.
-- Capture completing on a reachable, uncontested target is the observable proxy
-- for "no order-thrash": a thrashed/stolen capture order never arrives. The
-- exact commit-count no-thrash invariant is covered deterministically by
-- PoiGoalGuardTest (NUnit); the live [experimental-capture] log (commitN) corroborates.

WorldLoaded = function()
	local usa = Player.GetPlayer("USA-bot")

	TestHarness.FocusBetween(CloseBio, BotTecn)

	TestHarness.AssertWithin(35, function()
		if CloseBio.IsDead then return "fail: derrick destroyed before capture" end
		if CloseBio.Owner == usa then return true end
		return false
	end, "experimental bot did not capture the close safe derrick within 35s — capture execution stalled")
end
