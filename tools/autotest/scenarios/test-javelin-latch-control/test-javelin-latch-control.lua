-- RED CONTROL for the 6.1 / 6.3 sweeps. This scenario exists because of a hole in them.
--
-- The audit's 6.1 survival fingerprint requires `flystraight_latches >= 1`. Across 472 shots the
-- two sweeps produced ZERO latches, so the fingerprint could not have matched no matter what the
-- Javelin did — a test that cannot go green is not evidence when it comes back negative
-- (DOCS/recipes/AUTOTEST.md, "a green run is not evidence unless something could have made it RED",
-- read in the other direction).
--
-- The one latching flight in the retained corpus launched from 12376 wdist. The sweeps engage at
-- 4-6 cells, chosen to buy the shallow terminal pitch of condition (B), and at that range the
-- missile reaches its target in about twenty ticks and fuses at closest approach every time. This
-- arm holds the rig identical and moves ONE thing — engagement range, to ~11.5 cells — to establish
-- whether the rig can express a latch at all.
--
--   latches here, none in the sweeps  -> the sweeps' negative is about the geometry, and is real
--   no latches anywhere               -> the rig cannot express the phenomenon and the sweeps say
--                                        nothing; the next move is the launcher, not Missile.cs
--
-- One column only: a lane needs >= 20 cells of clear downrange beyond the target, and a 12-cell
-- engagement plus that clearance does not fit twice across a 64-cell playfield.

local COLUMNS = { { launcher = 4, track = 16 } }
local TRIGGERS = { 0, 1000, 1500, 2000 }
local RunSeconds = 170
local MinMissiles = 40

local function reverse(lane)
	lane.moveDir = -lane.moveDir
	lane.target.Move(CPos.New(lane.trackX, lane.row + lane.half * lane.moveDir))
end

WorldLoaded = function()
	JavelinProbe.SetColumns(COLUMNS)
	if not JavelinProbe.Build(TRIGGERS, 3) then
		return
	end

	JavelinProbe.Drive(reverse, RunSeconds, MinMissiles)
end
