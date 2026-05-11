-- TEST: Artillery turret rotation
-- Description lives in description.txt (read by the test runner, shown in
-- the TEST MODE panel). Lua only handles staging.

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)
	TestHarness.Select(Paladin)
end
