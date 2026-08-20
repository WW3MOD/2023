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

-- Cells east of Ghost, walked from far to near. `band` is the ^StandardVision strength that
-- reaches that separation and is asserted separately from detection, so a mis-sited rung
-- reports as a SETUP fault instead of as a verdict on the fix.
--
-- Band membership is EXACT, not rounded. MapLayers.ProjectedCellsInRange enumerates a
-- generous integer radius but then filters on squared WDist: `dist <= maxLimit && dist >
-- minLimit` (:303-305). So a cell at exactly N cells is in the band whose MinRange < N*1024
-- <= Range. Bands from defaults.yaml:47-84 -> B10 covers 1-4, B9 5-7, B8 8-10, B7 11-13,
-- B2 26-28, B1 29-32. Band edges (4, 7, 10, 28) are not sampled: AUTOTEST.md gotcha #8.
local Ladder = {
	{ cells = 30, band = 1,  detected = false, why = "band 1 (28c-32c), strength 1 -- far below tier 9" },
	{ cells = 9,  band = 8,  detected = false, why = "band 8 (7c-10c), strength 8 -- one short of tier 9" },
	{ cells = 5,  band = 9,  detected = true,  why = "band 9 (4c-7c), strength 9 -- MATCHES tier 9, and a match now reveals. This is the rung that catches a revert to a strict compare: 9 > 9 is false, 9 >= 9 is true" },
	{ cells = 2,  band = 10, detected = true,  why = "band 10 (0c-4c), strength 10 -- standing on top of it" },
}

WorldLoaded = function()
	local russia = Player.GetPlayer("Russia")
	local ghostCell = CPos.New(GhostX, GhostY)

	-- Forest shadow is subtracted from the OBSERVER's strength before it is stamped
	-- (MapLayers.AddSource), so a single tree on this row would move every rung of the
	-- ladder without moving anything the scenario claims. Assert the line is bare rather
	-- than trusting the copied map.bin.
	for x = GhostX, GhostX + 30 do
		local d = Test.GetDensity(CPos.New(x, GhostY))
		if d ~= 0 then
			Test.Fail("terrain density is " .. tostring(d) .. " at cell " .. tostring(x) ..
				"," .. tostring(GhostY) .. " -- ground shadow attenuates the observer's " ..
				"strength along the sightline, so none of the band distances below hold")
			return
		end
	end

	-- PITFALL, and it cost this scenario its first run. Detectable.CurrentVisibility is an
	-- auto-property written ONLY inside ITick.Tick (Detectable.cs:115), and WorldLoaded runs
	-- from World.LoadComplete BEFORE any actor has ticked -- so reading the tier here returns
	-- the uninitialised 0, not a computed level. 0 is not a value the clamp can produce at
	-- all: ClampConcealment returns 1 for anything below 1. Every tier read must therefore
	-- sit behind a delay. Nothing about the assertion changed; only when it is taken.
	local tierName = function(t)
		if t == 0 then
			return "0 (UNINITIALISED -- the actor has not ticked yet; this is a scenario " ..
				"timing fault, not a concealment value, because ClampConcealment cannot return 0)"
		end

		if t < 0 then
			return tostring(t) .. " (no Detectable trait on Ghost at all)"
		end

		return tostring(t)
	end

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
				Test.Fail("Ghost drifted to tier " .. tierName(nowTier) .. " at the " ..
					tostring(entry.cells) .. "-cell rung -- something moved it, made it " ..
					"fire, or changed its cover, so the remaining rungs mean nothing")
				return
			end

			local detected = Test.IsDetectedBy(Ghost, russia)
			local strength = Test.GetVisibility(russia, ghostCell)

			print("[detect] observer at " .. tostring(entry.cells) .. " cells: detected=" ..
				tostring(detected) .. " russianStrengthAtGhostCell=" .. tostring(strength) ..
				" expectedBand=" .. tostring(entry.band) ..
				" expectedDetected=" .. tostring(entry.detected))

			-- Separated from the verdict on purpose: if the rung is at the wrong distance, or
			-- some other Russian actor is contributing vision to Ghost's cell, that is a fault
			-- in this scenario's staging and must not be reported as a fault in detection.
			if strength ~= entry.band then
				Test.Fail("SETUP INVALID: with the observer " .. tostring(entry.cells) ..
					" cells east, Russia's resolved strength at Ghost's cell is " ..
					tostring(strength) .. ", expected band " .. tostring(entry.band) ..
					". The rung is not sampling the band it claims, so its detection " ..
					"expectation is meaningless. Either the distance is wrong or another " ..
					"Russian actor is stamping vision on that cell.")
				return
			end

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

	-- Three seconds is ~75-180 ticks, so Detectable has ticked many times by here and
	-- CurrentVisibility is a real level. Taking the premise check inside this delay rather
	-- than at WorldLoaded is the ONLY thing that changed after the first run: the asserted
	-- value is still 9, because 9 is the ceiling the fix installs. Before the ceiling landed
	-- this same pinned unit read 10, which is exactly what the check is for.
	Trigger.AfterDelay(DateTime.Seconds(3), function()
		local tier = Test.GetVisibilityLevel(Ghost)
		if tier ~= ExpectedTier then
			Test.Fail("Ghost is on concealment tier " .. tierName(tier) .. ", expected " ..
				tostring(ExpectedTier) .. ". rules.yaml pins Detectable.Vision at 12, which " ..
				"is above anything the modifier stack can compose, so this value IS the " ..
				"clamp ceiling. Tier 10 means the ceiling is gone and a unit can sit on the " ..
				"top vision band again -- the exact state this scenario exists to prevent")
			return
		end

		print("[detect] Ghost pinned at concealment tier " .. tostring(tier) ..
			" (ceiling); walking a Russian observer 30 -> 2 cells")

		step(1)
	end)
end
