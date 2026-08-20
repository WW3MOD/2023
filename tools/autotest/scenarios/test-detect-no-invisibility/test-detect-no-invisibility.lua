-- ASSERTION SCENARIO: no unit is undetectable at point-blank range.
--
-- =====================================================================================
-- WHAT THIS PINS
-- =====================================================================================
-- Concealment and observer vision strength are composed independently and then resolved
-- against each other on ONE shared ladder. Two things used to make the top of that ladder
-- unwinnable:
--
--   * concealment was clamped to [1, 10], the same 10 the strongest ^StandardVision band
--     carries, so a unit could sit exactly ON the best observer in the game; and
--   * reveal was a STRICT comparison, so 10 > 10 is false and that unit was invisible at
--     every range, to everything, including an enemy standing next to it.
--
-- The fix reserves the top level for observers (concealment now ceilings at 9) and makes
-- reveal non-strict. Ghost is pinned above the ceiling by rules.yaml, so it lands on 9
-- whatever the live modifier stack does, and the enemy ladder below is a property of the
-- ^StandardVision bands alone.
--
-- =====================================================================================
-- HOW TO READ A FAILURE
-- =====================================================================================
-- This scenario catches a revert of EITHER half of the fix, and says which:
--
--   tier 10 at the premise check      -> the concealment ceiling is gone
--   detected only inside 4 cells      -> the ceiling holds but reveal is strict again
--   detected at 30 cells              -> something else is revealing Ghost; read the
--                                        printed Russian strength at Ghost's cell before
--                                        blaming detection
--
-- The negative rungs are not padding. "Detected at 2 cells" passes just as happily if
-- every unit on the map became permanently visible, and that is the failure mode a
-- non-strict comparison invites, so the run has to show detection switching OFF as well
-- as on.

local GhostX = 32
local GhostY = 16

-- Ghost composes above the clamp, so it must land exactly on the ceiling.
local ExpectedTier = 9

-- Cells east of Ghost, walked from far to near. Each entry names the ^StandardVision band
-- that reaches it (defaults.yaml) and what that band's strength does against tier 9.
-- Band edges (4c, 7c, 10c, 28c) are deliberately not sampled: AUTOTEST.md gotcha #8 is
-- that an assertion sitting on a band edge flips between runs of an identical scenario.
local Ladder = {
	{ cells = 30, detected = false, why = "band 1 (28c-32c), strength 1 -- far below tier 9" },
	{ cells = 9,  detected = false, why = "band 8 (7c-10c), strength 8 -- one short of tier 9" },
	{ cells = 5,  detected = true,  why = "band 9 (4c-7c), strength 9 -- MATCHES tier 9, and a match now reveals" },
	{ cells = 2,  detected = true,  why = "band 10 (0c-4c), strength 10 -- standing on top of it" },
}

WorldLoaded = function()
	local russia = Player.GetPlayer("Russia")
	local ghostCell = CPos.New(GhostX, GhostY)

	-- Forest shadow is subtracted from the OBSERVER's strength before it is stamped
	-- (MapLayers.AddSource), so a single tree on this row would move every rung of the
	-- ladder without moving anything the scenario claims. Assert the line is bare rather
	-- than trusting the copied map.bin.
	for x = GhostX, GhostX + 31 do
		local d = Test.GetDensity(CPos.New(x, GhostY))
		if d ~= 0 then
			Test.Fail("terrain density is " .. tostring(d) .. " at cell " .. tostring(x) ..
				"," .. tostring(GhostY) .. " -- ground shadow attenuates the observer's " ..
				"strength along the sightline, so none of the band distances below hold")
			return
		end
	end

	local tier = Test.GetVisibilityLevel(Ghost)
	if tier ~= ExpectedTier then
		Test.Fail("Ghost is on concealment tier " .. tostring(tier) .. ", expected " ..
			tostring(ExpectedTier) .. ". rules.yaml pins Detectable.Vision at 12, which is " ..
			"above anything the modifier stack can compose, so this value IS the clamp " ..
			"ceiling. Tier 10 means the ceiling is gone and a unit can sit on the top " ..
			"vision band again -- the exact state this scenario exists to prevent")
		return
	end

	print("[detect] Ghost pinned at concealment tier " .. tostring(tier) ..
		" (ceiling); walking a Russian observer 30 -> 2 cells")

	local step
	step = function(index)
		local entry = Ladder[index]
		if entry == nil then
			Test.Pass("tier " .. tostring(ExpectedTier) .. " Ghost: undetected at 30c and 9c, " ..
				"detected at 5c and 2c -- reveal boundary sits at band 9 as intended, and a " ..
				"maximally concealed unit is no longer invisible at point-blank range")
			return
		end

		Watcher.Teleport(CPos.New(GhostX + entry.cells, GhostY))

		-- Settle: the teleport has to reach the ActorMap, and the vision source has to be
		-- removed and re-added before ResolvedVisibility reflects the new position.
		Trigger.AfterDelay(DateTime.Seconds(2), function()
			if Ghost.IsDead or Watcher.IsDead then
				Test.Fail("a unit died during the ladder -- both sides are HoldFire and " ..
					"Watcher is NoAutoTarget in rules.yaml, so nothing should be shooting")
				return
			end

			-- The premise, re-checked every rung: firing, moving or digging in would move
			-- Ghost off the ceiling and silently invalidate the rest of the ladder.
			local nowTier = Test.GetVisibilityLevel(Ghost)
			if nowTier ~= ExpectedTier then
				Test.Fail("Ghost drifted to tier " .. tostring(nowTier) .. " at the " ..
					tostring(entry.cells) .. "-cell rung -- something moved it, made it " ..
					"fire, or changed its cover, so the remaining rungs mean nothing")
				return
			end

			local detected = Test.IsDetectedBy(Ghost, russia)
			local strength = Test.GetVisibility(russia, ghostCell)

			print("[detect] observer at " .. tostring(entry.cells) .. " cells: detected=" ..
				tostring(detected) .. " russianStrengthAtGhostCell=" .. tostring(strength) ..
				" expected=" .. tostring(entry.detected))

			if detected ~= entry.detected then
				Test.Fail("with the Russian observer " .. tostring(entry.cells) ..
					" cells east, Ghost (concealment tier " .. tostring(nowTier) ..
					") was " .. (detected and "DETECTED" or "NOT detected") ..
					" -- expected " .. (entry.detected and "DETECTED" or "NOT detected") ..
					": " .. entry.why .. ". Russia's resolved strength at Ghost's cell was " ..
					tostring(strength))
				return
			end

			step(index + 1)
		end)
	end

	Trigger.AfterDelay(DateTime.Seconds(3), function()
		step(1)
	end)
end
