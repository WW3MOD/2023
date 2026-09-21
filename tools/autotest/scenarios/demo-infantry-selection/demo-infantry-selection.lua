-- DEMO -- WHAT A SELECTED RIFLEMAN ACTUALLY LOOKS LIKE, and what one corner bracket would add.
--
-- Nothing here asserts. There is no AssertWithin, no Test.Pass and no Test.Fail: the user is
-- picking between two pictures, and a verdict would be this file claiming to have made that
-- choice for him (DEMO.md). It ENDS ON Test.Skip, which is the corpus convention for a demo
-- that must run unattended -- demo-nuke-arsenal, demo-doomsday-deadhand, demo-defcon-readout
-- and demo-command-bar-reflow all do the same. Skip writes a verdict file, flushes the pending
-- captures through ExitWhenCapturesFlushed (TestGlobal.cs:55-60) and exits 2, instead of
-- sitting on the watchdog until it times out with the PNGs unflushed.
--
-- READ THE SKIP REASON BEFORE READING THE FRAMES. It carries `selected=N`. If N is not 9 the
-- run is a NO-RESULT, not a finding: the selection never applied, every frame is a picture of
-- an unselected army, and arm A and arm B will look identical for a reason that has nothing to
-- do with ShowNever.
--
-- THAT GUARD HAS ALREADY EARNED ITS KEEP. The first run of this demo (260922_005703, at
-- 86f86980) came back `selected = 1 (want 9)` with four healthy non-black PNGs -- four frames
-- that looked like evidence and were not. The cause was in the engine, not here:
-- Test.SelectActors passed `isClick: true` to Selection.Combine, whose very first branch is
-- `newSelection.Take(1)` (Selection.cs:96-99), so the binding replaced the selection with the
-- FIRST actor of the array and dropped the other eight -- while its own [Desc] promised ALL of
-- them. Fixed in the same branch (TestGlobal.cs, `false, false`). The lesson generalises past
-- this scenario: a capture that photographs a STATE must assert the state it photographs,
-- because a wrong state and a right one produce equally convincing pictures.
--
-- CAPTURE TIMING. Test.Screenshot ARMS a grab that samples at the end of the NEXT RenderTick
-- (SCREENSHOT.md), so a camera move on the following line would be photographed under the
-- previous label. Every capture below therefore owns its own delay with quiet either side, and
-- the first one sits well clear of WorldLoaded -- a shot fired before any frame has rendered
-- lands blank. The tell for a blank frame is FILE SIZE, not the image: ~59 KB is a black frame,
-- a real one is megabytes.
--
-- THE FRAMES
--   A-current          arm A close up. SHIPPED rules. Top row of four is selected, bottom row
--                      of two is not. Question: is there ANY mark on the top row that is not
--                      on the bottom row?
--   B-bracket          arm B close up, same framing, same sprite. SelectionDecorations
--                      ShowNever: false. Expect four white 1px L-shaped corner brackets
--                      (SelectionBoxAnnotationRenderable.cs:44-55, 4px arms) around each of the
--                      top four and none around the bottom two.
--   C-side-by-side     both arms and both tanks in one frame at a normal playing zoom. This is
--                      the frame the decision should actually be made on: a bracket that reads
--                      at 4x MinZoom and vanishes at 2x is not a fix.
--   D-vehicle-control  one selected abrams beside one unselected abrams, shipped rules on both.
--                      Settles whether the vehicle bracket is selection-gated. architecture.md
--                      currently says it is not; SelectionDecorations.cs:75 reached from
--                      SelectionDecorationsBase.cs:110 says it is.

local Zoom = { Close = 4, Wide = 2 }

local applied = { close = 0, wide = 0 }
local selectedAtCapture = -1

WorldLoaded = function()
	TestHarness.FocusBetween(ASel1, BSel4, TankSel, TankCtl)

	-- Infantry are a handful of pixels at MinZoom and the corner bracket is a 1px line with 4px
	-- arms; the first pass at this on a truck (test-lc-refill-gesture) came back unreadable as
	-- evidence. SetZoom's scale is a multiple of MinZoom and it RETURNS what was actually
	-- applied after clamping, which is not necessarily what was asked for -- so record it and
	-- put it in the skip reason rather than assuming 4 was honoured.
	applied.close = Test.SetZoom(Zoom.Close)
	print("[selection] close zoom = " .. tostring(applied.close) .. "x MinZoom")

	-- ---- The selection, made once and never touched again ---------------------------------
	-- UserInterface.Select takes a single actor and REPLACES the selection, so it cannot build
	-- a multi-unit one at all; Test.SelectActors is the only route to this state from a
	-- scenario. Nine actors: four from each arm plus TankSel. The two control rows and TankCtl
	-- are deliberately left out -- they are the whole experiment.
	--
	-- Requires the TestGlobal.cs `isClick: false` fix made in this branch. Before it, this line
	-- selected ASel1 and nothing else, which is why the count below is printed rather than
	-- assumed.
	Trigger.AfterDelay(DateTime.Seconds(2), function()
		Test.SelectActors({ ASel1, ASel2, ASel3, ASel4, BSel1, BSel2, BSel3, BSel4, TankSel })
		selectedAtCapture = Test.GetSelectedCount()
		print("[selection] selected = " .. tostring(selectedAtCapture) .. " (want 9)")
	end)

	-- ---- Frame A: the shipped state --------------------------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(4), function()
		TestHarness.FocusBetween(ASel1, ASel4, ACtl1, ACtl2)
	end)

	Trigger.AfterDelay(DateTime.Seconds(6), function()
		TestHarness.Screenshot("A-current",
			"SHIPPED RULES, arm A. Six identical riflemen: a TOP row of four that IS selected " ..
			"and a BOTTOM row of two that is NOT. " ..
			"THE QUESTION: is there any mark on the top row that is absent from the bottom row? " ..
			"NO CORNER BRACKETS ARE EXPECTED on either row -- ^Infantry sets ShowNever: true. " ..
			"What MAY be there is a small pip directly above each selected head: pip-selected " ..
			"from WithDecoration@Selected, RequiresSelection: true. It is authored at the exact " ..
			"same anchor (Position: Top, Margin: 0,6) as the e3_class pictogram every soldier " ..
			"already carries, with no z-order between them, so it may be drawn UNDER the class " ..
			"pip and be invisible. " ..
			"READ IT AS: (a) top and bottom rows visibly differ -> infantry DO have a selection " ..
			"mark and the audit's 'no feedback at all' is wrong; (b) the two rows are " ..
			"indistinguishable -> the mark exists in YAML and does not reach the screen, which " ..
			"is the user's report and a stronger finding than the one filed. " ..
			"Ignore the diamonds and stance glyphs above the heads -- those are on BOTH rows " ..
			"and are not selection-gated.")
	end)

	-- ---- Frame B: the candidate ------------------------------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(8), function()
		TestHarness.FocusBetween(BSel1, BSel4, BCtl1, BCtl2)
	end)

	Trigger.AfterDelay(DateTime.Seconds(10), function()
		TestHarness.Screenshot("B-bracket",
			"ARM B -- the candidate fix, same six-man staging, same sprite as arm A, one " ..
			"scenario-local line different: SelectionDecorations ShowNever: false. " ..
			"CORRECT = each of the TOP FOUR now carries four white L-shaped corner brackets, " ..
			"1px, 4px arms, at the corners of a box roughly half a cell wide and two thirds of " ..
			"a cell tall; the BOTTOM TWO carry none. " ..
			"BROKEN = brackets on the bottom row too (then something other than selection is " ..
			"drawing them), or no brackets anywhere (then the override did not take -- check " ..
			"the actor key case in rules.yaml before believing anything about the design). " ..
			"JUDGE, don't just confirm: does the bracket sit sensibly around a man-sized " ..
			"sprite, or is it so tight it reads as noise on the sprite itself? Does it collide " ..
			"with the pips already stacked over the head? Four riflemen shoulder to shoulder is " ..
			"the dense case on purpose -- if the brackets merge into a hedge at this spacing, " ..
			"that is the argument against arm B and it should be said out loud.")
	end)

	-- ---- Frame C: the frame the decision is actually made on ---------------------------------
	Trigger.AfterDelay(DateTime.Seconds(12), function()
		applied.wide = Test.SetZoom(Zoom.Wide)
		print("[selection] wide zoom = " .. tostring(applied.wide) .. "x MinZoom")
		TestHarness.FocusBetween(ASel1, BSel4, TankSel, TankCtl)
	end)

	Trigger.AfterDelay(DateTime.Seconds(15), function()
		TestHarness.Screenshot("C-side-by-side",
			"BOTH ARMS AND BOTH TANKS, one frame, at half the zoom of A and B -- closer to a " ..
			"zoom someone actually plays at. LEFT squad is shipped, RIGHT squad is the " ..
			"candidate, and the two rows under each are the unselected controls. " ..
			"THIS IS THE COMPARISON FRAME: at this distance, can you tell the left squad is " ..
			"selected at all, and can you tell the right one is? If the bracket survives the " ..
			"zoom-out and the pip does not, that is the case for arm B in one picture. If " ..
			"neither reads, neither option is finished and the answer is a third one.")
	end)

	-- ---- Frame D: the vehicle claim ---------------------------------------------------------
	Trigger.AfterDelay(DateTime.Seconds(17), function()
		applied.close = Test.SetZoom(Zoom.Close)
		TestHarness.FocusBetween(TankSel, TankCtl)
	end)

	Trigger.AfterDelay(DateTime.Seconds(20), function()
		TestHarness.Screenshot("D-vehicle-control",
			"TWO ABRAMS, SHIPPED RULES ON BOTH. The LEFT one is in the selection; the RIGHT " ..
			"one is not. Nothing else differs. " ..
			"CORRECT (and what the code says): white corner brackets on the LEFT tank only. " ..
			"IF BOTH TANKS CARRY BRACKETS, a curated claim is right and a code reading is " ..
			"wrong -- DOCS/reference/architecture.md states own vehicles draw their brackets on " ..
			"the unselected path 'so the bracket is not a selection indicator at all'. Say which " ..
			"you see; this frame is the only evidence either way and it is free here.")
	end)

	Trigger.AfterDelay(DateTime.Seconds(23), function()
		Test.Skip("eyeball: infantry selection mark, arm A (shipped) vs arm B (ShowNever: false)"
			.. " -- selected=" .. tostring(selectedAtCapture) .. " (want 9; anything else is a"
			.. " NO-RESULT, not a finding), closeZoom=" .. tostring(applied.close)
			.. "x MinZoom, wideZoom=" .. tostring(applied.wide) .. "x MinZoom")
	end)
end
