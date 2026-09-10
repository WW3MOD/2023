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
		"THE FRAME THIS DEMO EXISTS FOR, AND ITS SUBJECT HAS CHANGED. The first run of this capture " ..
		"showed ordinary woodland: the wall sealed the map and drew NOTHING, because writing " ..
		"Map.CustomTerrain changes what the pathfinder reads and not what the terrain renderer " ..
		"draws. DefconWall now draws the border itself. expects: an amber line running corner to " ..
		"corner through cell 48,48, with perpendicular hatch strokes every 3 cells along it and a " ..
		"translucent warm fill over the two-cell band it sits in. The line must STOP at the map " ..
		"edge, not continue into the black margin. FAIL if the field is unmarked (the original " ..
		"defect), if the fill has a gap or a staircase hole where it crosses open ground, or if it " ..
		"reads as terrain -- a river or a road -- rather than as a rule.")

	-- The crossing attempt.
	--
	-- Test.IssueMoveOrder, NOT Gun.Move, and the first run of this capture is why. MobileProperties.Move
	-- queues a Move ACTIVITY directly and never touches the order path -- its own PITFALL comment
	-- says so, and says a scenario testing anything in the order path wants Test.IssueMoveOrder.
	-- The refusal this frame exists to photograph lives in order validation, so a scripted Move
	-- could never trigger it: frame 02 came back blank and the blankness was the rig's fault, not
	-- the feature's. The unit still failed to cross either way, because the pathfinder has no
	-- route -- which is exactly the silent disobedience the notification was added to end.
	Trigger.AfterDelay(140, function()
		if not Gun.IsDead then
			Test.IssueMoveOrder(Gun, CPos.New(Across.X, Across.Y))
		end
	end)

	TestHarness.ScreenshotAfter(160 / TestHarness.TicksPerSecond, "02-refusal-said-out-loud",
		"~20 ticks after the move order, to catch the TRANSIENT notification line. Ordering a unit " ..
		"to a legal cell beyond the border used to be accepted in silence and the unit then sat " ..
		"still, so a player saw a tank disobey and was told nothing. expects: the line \"The border " ..
		"is closed at DEFCON 3 - that order would cross it.\" in the notification area. FAIL if " ..
		"nothing is printed. It is transient, so if this frame lands after it has faded, say so " ..
		"rather than calling it absent -- the next run can move the tick earlier.")

	TestHarness.ScreenshotAfter(560 / TestHarness.TicksPerSecond, "03-refused-crossing",
		"THE CROSSING, ~420 ticks after the move order. expects: the Abrams stopped on ITS OWN " ..
		"side of the band, or still holding at its start cell -- the order is now refused outright " ..
		"rather than accepted and left unpathable. FAIL if it is on the far side, or standing " ..
		"inside the band: either means the band did not seal at this angle. NOT a failure: the " ..
		"unit sitting still at 40,40 having never moved.")

	TestHarness.ScreenshotAfter(900 / TestHarness.TicksPerSecond, "04-still-refused",
		"The same, ~340 ticks later, to catch a unit that found a long way round rather than " ..
		"being stopped. expects: still on its own side. A detour along the map edge would show " ..
		"here and would mean the line does not reach the map bounds -- which is a different " ..
		"defect from a leak in the middle, and worth telling apart.")

	-- ZOOMED OUT, which is the requirement the band fill alone cannot answer. A marking that is
	-- legible at zoom 2 and invisible at zoom 1 has not solved "obvious at a glance at any zoom",
	-- and one that turns the map into a stripe has broken "does not make the map ugly".
	Trigger.AfterDelay(1000, function()
		Camera.Zoom = 1
	end)

	TestHarness.ScreenshotAfter(1040 / TestHarness.TicksPerSecond, "05-zoomed-out",
		"The same border at zoom 1, roughly half the magnification of the frames above. expects: " ..
		"the line still obviously locates the border at a glance -- its width is in PIXELS, so it " ..
		"should not thin out with distance -- and the hatching still reads as border notation. " ..
		"FAIL if the border has become hard to find, or if the band fill has swamped the map and " ..
		"units near it are hard to pick out.")
end
