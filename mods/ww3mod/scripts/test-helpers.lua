-- WW3MOD developer test harness — shared Lua helpers.
-- Loaded by test rules.yaml via `LuaScript: Scripts: test-helpers.lua, <test>.lua`.
-- Idle when the harness isn't active; safe to leave referenced from regular maps.

TestHarness = {}

-- Center the camera on the geometric midpoint of the given actors.
-- Usage: TestHarness.FocusBetween(Paladin, Target)
--        TestHarness.FocusBetween(actorA, actorB, actorC)
function TestHarness.FocusBetween(...)
	local actors = { ... }
	local sumX, sumY, count = 0, 0, 0
	for _, a in ipairs(actors) do
		if a and not a.IsDead then
			local pos = a.CenterPosition
			sumX = sumX + pos.X
			sumY = sumY + pos.Y
			count = count + 1
		end
	end
	if count > 0 then
		Camera.Position = WPos.New(math.floor(sumX / count), math.floor(sumY / count), 0)
	end
end

-- Pre-select the unit-under-test so the player doesn't have to click first.
-- Usage: TestHarness.Select(Paladin)
function TestHarness.Select(actor)
	if actor and not actor.IsDead then
		UserInterface.Select(actor)
	end
end
