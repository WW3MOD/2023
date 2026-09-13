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
--
-- ---- THE SECOND HALF, ADDED 2026-09-13 WITH THE LEDGER ------------------------------------
-- Frames 01-05 were the strip. 06-09 are the NUCLEAR LEDGER and the ARMED moment, and they are
-- the reason this file now ends on Test.Skip (see below).
--
--     K+1200  the release gate opens: both sides hold 1 kt permanently
--     K+1215  FRAME 06 -- the NUCLEAR RELEASE banner, and both ledger rows lit at 1 kt
--     K+1260  RUSSIA fires a 1 kt at empty ground in the far corner
--     K+1290  FRAME 07 -- the ARMED banner, and USA's row carrying a 20 kt window
--     K+1700  FRAME 08 -- mid-window: the banner gone, the window clock visibly down
--     K+2260  the window lapses
--     K+2300  FRAME 09 -- the 20 kt box dark again and the expiry line in the transients panel
--     K+2400  Test.Skip
--
-- ---- WHY RUSSIA FIRES AND NOT USA, WHICH IS NOT WHAT YOU WOULD EXPECT ----------------------
-- ANY LAUNCH ARMS THE OTHER SIDE -- that is the whole rule -- so the ARMED banner appears on the
-- screen of whoever was SHOT AT, never the firer's. This map makes USA the playable slot, so USA
-- is the local player and USA's screen is the only one a capture can see. A demo where USA fired
-- would therefore produce the banner on a screen nobody is looking at, and the capture would come
-- back showing a transient line and no banner at all.
--
-- So Russia takes the shot and USA is the victim. That also makes frame 08 the more interesting
-- one: the two rows are ASYMMETRIC -- USA holding 1 kt with a 20 kt grant ticking down, Russia
-- holding 1 kt and nothing else -- which is the exact position the ledger exists to make readable,
-- and the one a single-row readout could not show at all.
--
-- ---- IT NOW ENDS, WHICH IT DID NOT ---------------------------------------------------------
-- This file had NO terminal call. Under run-test.sh that means no verdict is ever written, the
-- 300 s watchdog kills the game, and the run is reported as a timeout whatever the frames show --
-- the same failure demo-doomsday-deadhand's first two runs hit. Test.Skip is the fix and is the
-- right verdict for a demo: a `skip` is not a claim that anything passed, it is the run saying
-- "I finished, the frames are on disk, a person decides".

local Ground = { X = 49, Y = 10 }

-- GROUND ZERO FOR RUSSIA'S 1 kt, chosen to be FAR FROM THE CAMERA rather than dramatic. The camera
-- sits over 49,10 at zoom 2 and the readout is what these frames are about; a fireball in shot
-- would light the panel differently frame to frame and would put the blast wave on the Abrams whose
-- selection is what populates the support-power column above the readout. 80,60 is inside the map's
-- 1,1,96,80 bounds and about 66 cells away.
local AimPoint = { X = 80, Y = 60 }

-- Russia's 1 kt, by OrderName -- SupportPowerManager keys its dictionary on it. 9M729, 1000 t,
-- which NuclearReleaseLadder.RungForYield puts in the Kiloton band.
local RU_1KT = "Ru9M729Strike"

-- 1 minute at the mod's 60 ms timestep, matching rules.yaml's RetaliationWindowDefault: 1.
-- TICKS, NOT TestHarness SECONDS: that helper counts 25 ticks to the second against a mod running
-- at 16.67, and every boundary in the second half is a tick count the engine computed from minutes.
local WINDOW_TICKS = 1000

local USA
local Russia
local Gun
local Victim

-- What Test.ActivateSupportPower returned, carried into the frame notes so a capture that shows no
-- banner says WHY on its own caption rather than sending a reader to lua.log.
local FireStatus = "not-attempted"

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
					"DEFCON strip below it, a visible gap to the support-power column, and BOTH " ..
					"LEDGER ROWS present with every box DARK. The rows are labelled YOU and ENEMY, " ..
					"in that order, top to bottom. FAIL if the blocks overlap, if the strip moved " ..
					"from where frames 01-03 had it, if any box is lit before release, or if only " ..
					"one row is drawn.")
			end)

			-- ---- THE RELEASE GATE OPENS ------------------------------------------------------
			Trigger.AfterDelay(1215, function()
				TestHarness.Screenshot("06-release-banner",
					"THE GATE OPENING, ~15 ticks into a ~66-tick hold. expects: a full-width band " ..
					"in the NUCLEAR BLUE -- not the DEFCON red -- reading NUCLEAR RELEASE over " ..
					"'1 kt available to both sides', its borders reaching both screen edges and " ..
					"the two lines centred as a block. Underneath, the ledger's FIRST box is now " ..
					"lit on BOTH rows and the other four are dark on both. FAIL if the band is " ..
					"red (it would mean the DEFCON palette leaked into a nuclear event), if it " ..
					"reads as a dialog with visible ends, or if the rows disagree about 1 kt -- " ..
					"release is simultaneous and symmetric and the two rows must match exactly.")
			end)

			-- RUSSIA FIRES, NOT USA. See the header: the ARMED banner appears on the screen of
			-- whoever was shot at, and USA is the only screen a capture can see.
			Trigger.AfterDelay(1260, function()
				FireStatus = Test.ActivateSupportPower(Russia, RU_1KT, CPos.New(AimPoint.X, AimPoint.Y))
			end)

			-- ---- USA IS ARMED ----------------------------------------------------------------
			-- 30 ticks after the shot. The grant crosses a world trait, a player trait and a
			-- condition before the banner can see it, and NuclearExchangeInfo.GrantRetryTicks is 30.
			Trigger.AfterDelay(1290, function()
				TestHarness.Screenshot("07-armed-banner",
					"THE FRAME THIS WHOLE SECOND HALF EXISTS FOR. USA has just been shot at. " ..
					"expects: a nuclear-blue full-width band reading ARMED over '20 kt available " ..
					"for 1:00 - reply or hold', with a real EM DASH between the clock and 'reply' " ..
					"rather than a missing-glyph box. In the ledger below, the YOU row's 20 kt box " ..
					"is drawn in the grant style -- a red-brown fill with a PULSING border, " ..
					"visibly different from the lit blue 1 kt box beside it -- and carries its own " ..
					"m:ss under the label. The ENEMY row still shows 1 kt lit and nothing else. " ..
					"FAIL if there is no banner (the edge detection never fired), if the YOU row's " ..
					"20 kt box looks identical to its 1 kt box (Window collapsed into Held, and " ..
					"the player cannot tell a grant from a holding), or if both rows changed -- " ..
					"firing arms the OTHER side and must leave the firer's row alone. " ..
					"Fire order returned: " .. FireStatus)
			end)

			-- ---- MID-WINDOW ------------------------------------------------------------------
			Trigger.AfterDelay(1700, function()
				TestHarness.Screenshot("08-ledger-asymmetric",
					"THE LEDGER ALONE, banner long gone, ~440 ticks (~26 s) into a 1000-tick " ..
					"window. expects: the two rows ASYMMETRIC and readable as such at a glance -- " ..
					"YOU with 1 kt lit and 20 kt pulsing on a clock now reading roughly 0:34, " ..
					"ENEMY with 1 kt lit and four dark boxes. The nuclear block's top-right value " ..
					"slot should read '20 kt FOR m:ss' and its number must AGREE with the small " ..
					"one in the box. FAIL if the two clocks disagree, if the window clock has not " ..
					"visibly fallen since frame 07, or if the box labels have shifted position " ..
					"between a box that has a clock and one that does not.")
			end)

			-- ---- THE GRANT LAPSES UNUSED -----------------------------------------------------
			-- The window opened at ~1260 and runs WINDOW_TICKS; 40 ticks of slack past it.
			Trigger.AfterDelay(1260 + WINDOW_TICKS + 40, function()
				TestHarness.Screenshot("09-grant-expired",
					"AFTER THE LAPSE. expects: the YOU row's 20 kt box DARK again and matching the " ..
					"ENEMY row exactly, the value slot back to plain '1 kt', the foot line back to " ..
					"the no-window wording, and the line '20 kt grant expired.' in the transients " ..
					"panel at the bottom-left. FAIL if the box is still lit or still pulsing -- " ..
					"the permission is revoked at lapse and a box that stayed on would be offering " ..
					"a shot the condition layer has already taken back. A MISSING TRANSIENT LINE " ..
					"is a softer fault: the panel holds its lines for a limited time, so check the " ..
					"tick gap before calling it.")
			end)

			-- Clean exit, after the last capture has had time to flush. Skip and not Pass: nothing
			-- here is asserted and a person decides what the frames show.
			Trigger.AfterDelay(1260 + WINDOW_TICKS + 140, function()
				Test.Skip("eyeball: the nuclear ledger -- release lights both rows, Russia's 1 kt " ..
					"arms USA with a 20 kt window, the window runs down and lapses. Fire order " ..
					"returned: " .. FireStatus)
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
