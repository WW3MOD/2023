-- DEMO -- the DEFCON 3 dividing line, standing on a DIAGONAL, derived rather than authored.
-- Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json.
--
-- WHY THIS EXISTS, 2026-09-10. The wall was switched on across all ten shipped maps today, and
-- switching it on as specified would have shipped a border that still divided nothing: at the
-- shipped `HalfWidth: 512` every derived line LEAKED, on every ground locomotor, on all ten maps
-- -- 131 map/locomotor combinations. A one-cell band seals an AXIS-ALIGNED line and nothing else;
-- on a diagonal the banded cells touch only at their corners and an 8-connected step walks
-- straight between them.
--
-- Nothing caught that because the feature's one worked example (`test-defcon-wall`) authors a
-- VERTICAL line -- the single angle at which 512 is correct. The audit that found it is
-- arithmetic (`tools/nav-guard/defcon_wall_audit.py`), and arithmetic is not the engine. This
-- capture is the engine.
--
-- HalfWidth is 1024 now. The floor is derived, not tuned: two 8-adjacent cells' perpendicular
-- distances to a line differ by at most sqrt(2) cells, so a straddling pair always has a member
-- within sqrt(2)/2 = 724 world units of it.
--
-- ---- WHY THIS MAP ---------------------------------------------------------------------------
-- woodland-warfare-ww3, spawns at 1,4 and 96,93, so the derived bisector is steeply DIAGONAL and
-- passes through the map's midpoint at 48,48. A vertical line would not exercise the thing that
-- was broken, which is exactly how the bug survived until now.
--
-- The line is derived here, not authored: `DeriveFromSpawns: True` in rules.yaml, the same path a
-- real match takes. Nothing about this scenario hand-places the wall.
--
-- ---- WHAT THE FRAMES HAVE TO SHOW -----------------------------------------------------------
-- One continuous band, two cells thick, with NO gap and no staircase hole where it crosses open
-- ground -- and a vehicle ordered across it that does not cross. Corner-only contacts, or a unit
-- on the far side, is the failure this demo exists to detect.
--
-- Cells 40,40 (USA side) and 58,58 (Russia side) are both Clear, read out of map.bin with
-- tools/nav-guard/modload.py rather than picked by eye. The midpoint 48,48 is Clear too, so the
-- band there is drawn on open ground where a hole would be visible rather than hidden in trees.

local Midpoint = { X = 48, Y = 48 }
local Start = { X = 40, Y = 40 }
local Across = { X = 58, Y = 58 }

local USA
local Gun

WorldLoaded = function()
	USA = Player.GetPlayer("USA")

	Gun = Actor.Create("abrams", true, {
		Owner = USA,
		Location = CPos.New(Start.X, Start.Y),
		Facing = Angle.SouthEast,
	})

	-- Centred on the derived midpoint so the band runs corner to corner through the frame.
	Camera.Position = WPos.New(Midpoint.X * 1024 + 512, Midpoint.Y * 1024 + 512, 0)
	Camera.Zoom = math.min(2, Camera.MaxZoom)

	TestHarness.Select(Gun)

	TestHarness.ScreenshotAfter(120 / TestHarness.TicksPerSecond, "01-band-diagonal",
		"THE FRAME THIS DEMO EXISTS FOR. The derived line at DEFCON 3, drawn diagonally through " ..
		"cell 48,48 on open ground. expects: ONE CONTINUOUS BAND, two cells thick, running corner " ..
		"to corner with no gap and no staircase hole. FAIL if the banded cells touch only at their " ..
		"corners anywhere along it -- that is the leak that HalfWidth 512 produced on every " ..
		"diagonal, and it is invisible on a vertical line, which is why it survived.")

	-- The crossing attempt. The order is issued; the wall is what refuses it.
	Trigger.AfterDelay(140, function()
		if not Gun.IsDead then
			Gun.Move(CPos.New(Across.X, Across.Y))
		end
	end)

	TestHarness.ScreenshotAfter(560 / TestHarness.TicksPerSecond, "02-refused-crossing",
		"THE CROSSING, ~420 ticks after the move order. expects: the Abrams stopped on ITS OWN " ..
		"side of the band, or still holding at its start cell -- the pathfinder has no route " ..
		"across. FAIL if it is on the far side, or standing inside the band: either means the " ..
		"band did not seal at this angle. NOT a failure: the unit sitting still at 40,40 having " ..
		"never moved, which is what a refused path looks like when no detour exists.")

	TestHarness.ScreenshotAfter(900 / TestHarness.TicksPerSecond, "03-still-refused",
		"The same, ~340 ticks later, to catch a unit that found a long way round rather than " ..
		"being stopped. expects: still on its own side. A detour along the map edge would show " ..
		"here and would mean the line does not reach the map bounds -- which is a different " ..
		"defect from a leak in the middle, and worth telling apart.")
end
