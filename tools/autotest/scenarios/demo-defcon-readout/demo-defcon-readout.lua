-- DEMO -- the DEFCON readout, driven through all three levels on a shipped map.
-- Nothing here is asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json.
--
-- WHY THIS EXISTS, 2026-09-10. The user launched a DEFCON Escalation match and reported seeing
-- none of the indicators that were designed for it. They were right: the mode changed two major
-- rules and put nothing on screen. The readout was built the same day and NOTHING about how it
-- renders has been seen -- the worker that wrote it cannot launch the game. This scenario exists
-- to produce the frames that settle it.
--
-- ---- WHAT HAS TO HAPPEN, AND WHY IT IS AWKWARD --------------------------------------------
-- The readout has four distinct states and reaching the last two requires a KILL:
--
--   DEFCON 3   a clock counting down, and the rule line "the border is closed"
--   DEFCON 2   NO clock -- an em dash where it was -- plus "FIRST KILL ENDS THIS PHASE"
--   DEFCON 1   reached only when an actor dies to enemy action (DefconCasualtyObserver)
--   the gate   a nuclear release countdown that starts at DEFCON 1
--
-- So the scenario has to stage a killing, at a known place, at a predictable time, at DEFCON 2 --
-- where autonomous fire is SUPPRESSED by the very rule being demonstrated. An ORDERED attack
-- still fires (that is the whole design: every shot is one you order), which is what this does.
--
-- The victim is a TRUCK, not a tank. A tank duel takes an unpredictable number of ticks and the
-- frame after the kill has to be timed off it; an unarmed soft target dies quickly and once.
--
-- ---- TIMING -------------------------------------------------------------------------------
-- rules.yaml compresses all three paces to 300 ticks and the nuclear gate to 1200. At the 60 ms
-- timestep that is 16.67 ticks/s -- NOT 25 -- so 300 ticks = 18.0 s and 1200 = 72.0 s.
--
--     t=200   DEFCON 3, mid-phase, roughly 6 s left on the clock
--     t=300   DEFCON 3 -> 2. Banner holds ~66 ticks (4 s), so 300..366 is the window
--     t=360   attack ordered
--     kill    DEFCON 2 -> 1. Timed off Trigger.OnKilled rather than guessed
--
-- THE TWO FRAMES AFTER THE KILL HANG OFF OnKilled, NOT off a tick number, because the exact
-- kill tick is the one thing here that cannot be predicted. Guessing it is how a capture comes
-- back showing the state before or after the thing it was supposed to catch.

local Ground = { X = 49, Y = 10 }

local USA
local Russia
local Gun
local Victim

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	Russia = Player.GetPlayer("Russia")

	-- An 11x7 block of Clear cells centred on 49,10, read out of map.bin with
	-- tools/nav-guard/modload.py rather than picked by eye. Both units are inside it, five
	-- cells apart, so neither spawn can land on water, rock or a cliff.
	Gun = Actor.Create("abrams", true, {
		Owner = USA,
		Location = CPos.New(Ground.X - 3, Ground.Y),
		Facing = Angle.East,
	})

	-- Terrain under the bottom-left corner, which is where the readout docks. A camera over
	-- water or off the playfield would put the panel on black and say nothing about contrast.
	Camera.Position = WPos.New(Ground.X * 1024 + 512, Ground.Y * 1024 + 512, 0)
	Camera.Zoom = math.min(2, Camera.MaxZoom)

	-- Selected so the support-power column is populated: the nuclear block is drawn ABOVE that
	-- column and the gap between them is one of the things the frames are meant to check.
	TestHarness.Select(Gun)

	TestHarness.ScreenshotAfter(200 / TestHarness.TicksPerSecond, "01-defcon3-clock",
		"DEFCON 3, mid-phase. expects: the strip bottom-left reading DEFCON 3 / POSITIONING / " ..
		"m:ss to DEFCON 2, the countdown fitting its right-aligned slot, the progress pip " ..
		"visibly PART-filled, and the rule line 'The border is closed. Neither side may cross " ..
		"it.' on one or two lines inside the panel. FAIL if the panel is absent, clipped at its " ..
		"bounds, or drawn over the support-power column above it.")

	TestHarness.ScreenshotAfter(312 / TestHarness.TicksPerSecond, "02-banner-3-to-2",
		"THE 3->2 BANNER, ~12 ticks into a ~66-tick hold. expects: a full-width band about a " ..
		"third down the screen, its borders reaching BOTH screen edges, two lines centred as a " ..
		"block, the second naming the CAUSE. FAIL if it reads as a dialog box with visible ends, " ..
		"or if the two lines are centred independently of each other.")

	TestHarness.ScreenshotAfter(345 / TestHarness.TicksPerSecond, "03-defcon2-emdash",
		"THE FRAME THAT MATTERS MOST. DEFCON 2, banner gone. expects: the clock slot holds a " ..
		"visible EM DASH -- not blank, not a missing-glyph box -- because that emptied slot is " ..
		"what teaches the player this rung ends on an event rather than a clock. The rule line " ..
		"'Your units will not fire on their own. Every shot is one you order.' should wrap to " ..
		"TWO lines, and the pulsing FIRST KILL ENDS THIS PHASE box must sit fully inside the " ..
		"panel's bottom edge. FAIL on a three-line wrap or a clipped trigger box -- the fix for " ..
		"either is one constant, Height: 170 in both chrome files.")

	-- THE VICTIM IS SPAWNED LATE, and the first run of this demo is why. It used to be created in
	-- WorldLoaded alongside the Abrams -- and the Abrams killed it at TICK 28, because autonomous
	-- fire is only suppressed at DEFCON 2 and the match opens at DEFCON 3, where autotargeting
	-- works normally. The kill therefore happened at the wrong level: DefconCasualtyObserver only
	-- escalates from 2, so the match never reached DEFCON 1, and both frames hanging off OnKilled
	-- fired ~400 ticks early against a DEFCON 2 screen. The capture came back with five files and
	-- two of them showing the wrong state.
	--
	-- Spawning at t=320 puts the truck on the map AFTER the 3 -> 2 transition at t=300, so the only
	-- shot ever fired at it is the ordered one below.
	Trigger.AfterDelay(320, function()
		Victim = Actor.Create("truk", true, {
			Owner = Russia,
			Location = CPos.New(Ground.X + 2, Ground.Y),
			Facing = Angle.West,
		})

		-- Both post-kill frames hang off OnKilled rather than a tick, because the kill tick is the
		-- one thing in this schedule that cannot be predicted.
		Trigger.OnKilled(Victim, function()
			Trigger.AfterDelay(15, function()
				TestHarness.Screenshot("04-banner-2-to-1",
					"THE 2->1 BANNER, ~15 ticks after the kill that caused it. expects: the band " ..
					"full-width, reading DEFCON 1, second line naming the cause -- a life taken, " ..
					"autonomous fire released. FAIL if no banner: that would mean the casualty " ..
					"observer did not see an ordered kill.")
			end)

			Trigger.AfterDelay(420, function()
				TestHarness.Screenshot("05-defcon1-gate",
					"DEFCON 1 with the nuclear gate still SHUT. expects: TWO blocks stacked -- the " ..
					"blue-accented nuclear block above reading RELEASE IN m:ss and counting, the " ..
					"DEFCON strip below it, a visible gap to the support-power column, and the six " ..
					"rungs legible at 10 px. FAIL if the blocks overlap, if the strip moved from " ..
					"where frames 01-03 had it, or if the rung text cannot be read at all.")
			end)
		end)

		-- Ordered, not automatic: at DEFCON 2 nothing fires on its own, which is the rule being
		-- demonstrated. forceAttack is true so the order cannot be refused on stance grounds.
		Trigger.AfterDelay(40, function()
			if not Gun.IsDead and not Victim.IsDead then
				Gun.Attack(Victim, true, true)
			end
		end)
	end)
end
