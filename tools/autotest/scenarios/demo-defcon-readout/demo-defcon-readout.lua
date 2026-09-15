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
-- ---- THE SECOND HALF, ADDED 2026-09-13 WITH THE LEDGER, RE-SCRIPTED 2026-09-15 FOR v2 --------
-- Frames 01-05 are the strip. 06-09 are the NUCLEAR LEDGER and the ESCALATED moment, and they are
-- the reason this file ends on Test.Skip (see below).
--
--     K+1200  the release gate opens: both sides reach level 1
--     K+1215  FRAME 06 -- the NUCLEAR RELEASE banner, both ledger rows lit at 1 kt, both READY
--     K+1260  RUSSIA fires a 1 kt at empty ground in the far corner
--     K+1290  FRAME 07 -- the ESCALATED banner, USA's row now lit to 20 kt, Russia's row dimmed
--             with its cooldown counting
--     K+1700  FRAME 08 -- the banner gone, Russia's clock visibly down, USA's still READY
--     K+1760  FRAME 08b -- the readout LIFTED clear of the cargo panel (added 2026-09-14)
--     K+2160  Russia's cooldown ends (rules.yaml compresses it to 900 ticks)
--     K+2300  FRAME 09 -- BOTH rows READY, and USA's 20 kt box STILL LIT
--     K+2400  Test.Skip
--
-- ---- WHAT CHANGED AT v2, FRAME BY FRAME, BECAUSE THE OLD CAPTIONS WERE ABOUT A DEAD MODEL ----
-- ONE SHOT, NOT TWO. This demo used to fire BOTH 1 kt warheads two ticks apart, because the
-- cooldown was per BAND and Russia held two powers in it (the 9M729 and, under `powers-sandbox`,
-- America's B61) -- so one shot left the band loaded and the Charging cell never appeared. A
-- cooldown is now per SIDE, so one shot silences everything Russia holds and the second launch
-- would be refused rather than merely redundant.
--
-- FRAME 07's SECOND ROW IS THE NEW PICTURE. Under v1 the interesting cell was USA's 20 kt drawn in
-- the GRANT style with a pulsing border and its own countdown. There is no grant: USA's 20 kt is
-- an ordinary lit box, permanently, and the asymmetry has moved to the CLOCK COLUMN -- USA READY,
-- Russia counting.
--
-- FRAME 09 IS THE RATCHET AND IS NOW THE MOST INFORMATIVE FRAME IN THE FILE. It used to show the
-- grant LAPSING: the 20 kt box going dark again and a "20 kt grant expired" line in the transients
-- panel. Under v2 the box MUST STILL BE LIT -- levels never fall -- and what has changed instead is
-- Russia's clock reading READY again. A frame showing that box dark is a level that fell, which is
-- the one thing this model does not do.
--
-- ---- WHY RUSSIA FIRES AND NOT USA, WHICH IS NOT WHAT YOU WOULD EXPECT ----------------------
-- ANY LAUNCH RAISES THE OTHER SIDE -- that is the whole rule -- so the ESCALATED banner appears on
-- the screen of whoever was SHOT AT, never the firer's. This map makes USA the playable slot, so
-- USA is the local player and USA's screen is the only one a capture can see. A demo where USA
-- fired would therefore produce the banner on a screen nobody is looking at, and the capture would
-- come back showing a transient line and no banner at all.
--
-- So Russia takes the shot and USA is the victim. That also makes frames 07 and 08 the interesting
-- ones: the two rows are ASYMMETRIC in BOTH dimensions the ledger has -- USA lit to 20 kt and
-- READY, Russia lit to 1 kt and reloading -- which is the exact position the ledger exists to make
-- readable, and the one a single-row readout could not show at all.
--
-- ---- THE THIRD HALF, ADDED 2026-09-14 WITH THE MOVE TO THE BOTTOM-RIGHT --------------------
-- The readout used to dock bottom-LEFT. It now docks bottom-RIGHT, which is a corner two panels
-- already use part-time: GARRISON_PANEL and CARGO_PANEL share one 228x240 rectangle at
-- X: WINDOW_WIDTH - 240, Y: WINDOW_HEIGHT - 260, and each is on screen only while the right kind of
-- thing is selected. DefconReadoutWidget now draws from whichever of them is visible, 5px above its
-- top edge, instead of from its own bottom. FRAME 08b is the only evidence that works.
--
-- IT HAS TO BE THE CARGO PANEL AND NOT THE GARRISON PANEL, and that is not a preference. The
-- garrison panel CANNOT BE MADE TO APPEAR AT ALL: GarrisonPanelLogic.cs:120 starts it hidden and
-- then drives its visibility from GARRISON_TICKER, a LogicTicker INSIDE it -- and Widget.cs:512-518
-- gates TickOuter on IsVisible(), so the ticker that would show the panel never runs. That is a
-- live bug in the garrison panel, recorded in WORKSPACE/bugs/discovered.md and NOT fixed here;
-- CargoPanelLogic.cs:148-150 carries a comment describing that exact chicken-and-egg as the reason
-- it uses an IsVisible delegate instead, so the cargo half works and is what this frame uses.
--
-- THE APC MUST BE CARRYING SOMEONE. CargoPanelLogic.cs:205-208 returns early on a transport with
-- neither passengers nor supply, so an empty APC shows no panel and the frame would prove nothing
-- while looking exactly like a pass. LoadPassenger teleports the rifleman aboard in one call.
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

-- ONE SHOT IS ENOUGH SINCE v2, AND IT WAS NOT BEFORE. This demo used to fire TWO 1 kt warheads two
-- ticks apart, and the reason is worth keeping because it explains why the pair is gone rather than
-- leaving a reader to wonder. Under `powers-sandbox` Russia holds BOTH warheads in the Kiloton band
-- (NuclearReleaseLadder's ceiling is 1000 t inclusive) --
--
--     MissileStrikePower@B61Low     300 t   cameo "0.3 KT"
--     MissileStrikePower@Ru9M729   1000 t   cameo "1 KT"
--
-- -- and v1's cooldown was per BAND, computed as the SMALLEST remaining across the side, so one
-- shot left the other warhead loaded, the band reported 0, and the Charging cell never appeared at
-- all. Frame 07 of the 2026-09-13 capture is the direct evidence: a "1 KT" cameo sitting in USA's
-- own column.
--
-- A cooldown is now per SIDE. One shot silences everything Russia holds, so the second launch is
-- not merely redundant -- NuclearExchangeState.ReportLaunch would REFUSE it, and the refusal would
-- land in debug.log as an alarm about a power that should not have been ready.
local RU_1KT = "Ru9M729Strike"   -- 1000 t, the band ceiling

-- rules.yaml compresses the 1 kt SIDE COOLDOWN to this from the shipped 5000 (5:00). TICKS, NOT
-- TestHarness SECONDS: that helper counts 25 ticks to the second against a mod running at 16.67.
local COOLDOWN_TICKS = 900

local USA
local Russia
local Gun
local Victim

-- THE LIFT PAIR, ADDED 2026-09-14 WITH THE MOVE TO THE BOTTOM-RIGHT. An APC and one rifleman to
-- ride in it, for the single frame that proves the readout gets out of CARGO_PANEL's way. Created
-- late and only for that frame -- see the block at K+1730.
local Apc
local Rider

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
					"lit on BOTH rows and the other four are dark on both, with BOTH rows' clock " ..
					"columns reading READY. FAIL if the band is " ..
					"red (it would mean the DEFCON palette leaked into a nuclear event), if it " ..
					"reads as a dialog with visible ends, or if the rows disagree in either the " ..
					"boxes or the clock -- release is simultaneous and symmetric and the two rows " ..
					"must match exactly.")
			end)

			-- RUSSIA FIRES, NOT USA. See the header: the ESCALATED banner appears on the screen of
			-- whoever was shot at, and USA is the only screen a capture can see.
			--
			-- ONE WARHEAD. The pair this used to fire was v1's per-band cooldown working around
			-- Russia holding two powers in the Kiloton band; a cooldown is now per SIDE and a second
			-- launch would be refused. See the header.
			Trigger.AfterDelay(1260, function()
				FireStatus = Test.ActivateSupportPower(Russia, RU_1KT, CPos.New(AimPoint.X, AimPoint.Y))
			end)

			-- ---- USA IS ARMED ----------------------------------------------------------------
			-- 30 ticks after the shot. The grant crosses a world trait, a player trait and a
			-- condition before the banner can see it, and NuclearExchangeInfo.GrantRetryTicks is 30.
			Trigger.AfterDelay(1290, function()
				TestHarness.Screenshot("07-escalated-banner",
					"THE FRAME THIS WHOLE SECOND HALF EXISTS FOR. USA has just been shot at. " ..
					"expects: a nuclear-blue full-width band reading ESCALATED over '20 kt now " ..
					"available' -- NO clock and NO 'reply or hold' in that line, because a level " ..
					"does not expire. In the ledger below, the YOU row now has TWO lit boxes " ..
					"(1 kt and 20 kt, identical in style -- there is no grant style any more) and " ..
					"its clock column reads READY. The ENEMY row has gone CHARGING: its 1 kt box " ..
					"still lit but DIMMER, and its clock column counting down from ~0:54. " ..
					"FAIL if there is no banner (the edge detection never fired); if the banner " ..
					"line carries a countdown (v1's copy is back); if the YOU row's clock is " ..
					"counting (firing raises the OTHER side and must leave the firer's ROW alone " ..
					"-- only the firer pays a cooldown, and the firer is Russia); or if only ONE " ..
					"box on the ENEMY row dimmed (a cooldown is side-wide, so every box on that " ..
					"row must dim together). " ..
					"Fire order returned: " .. FireStatus)
			end)

			-- ---- MID-WINDOW ------------------------------------------------------------------
			Trigger.AfterDelay(1700, function()
				TestHarness.Screenshot("08-ledger-asymmetric",
					"THE LEDGER ALONE, banner long gone, ~440 ticks (~26 s) into Russia's " ..
					"900-tick cooldown. expects: the two rows ASYMMETRIC IN BOTH DIMENSIONS and " ..
					"readable as such at a glance -- YOU with 1 kt and 20 kt lit and its clock " ..
					"reading READY, ENEMY with only 1 kt lit, DIMMED, and its clock counting " ..
					"roughly 0:28. That is the whole claim of the design: one glance says what " ..
					"each side HOLDS and whether it can USE it. The nuclear block's top-right " ..
					"value slot should read the viewer's own level, '20 kt', with no countdown " ..
					"beside it. FAIL if the ENEMY clock has not visibly fallen since frame 07, if " ..
					"the YOU clock is counting at all, if any box carries its own small clock " ..
					"(v1 drew one per box; v2 draws one per ROW), or if the value slot shows a " ..
					"countdown.")
			end)

			-- ---- THE READOUT GETS OUT OF THE CARGO PANEL'S WAY -------------------------------
			-- Everything for this frame is built and torn down between frames 08 and 09 so neither
			-- of them changes: the selection is handed back to the Abrams at K+1790, 510 ticks
			-- before frame 09 is taken.
			--
			-- Both cells are inside the 11x7 block of Clear terrain centred on 49,10 that the two
			-- combatants were placed from, so neither spawn can land on water, rock or a cliff.
			Trigger.AfterDelay(1730, function()
				Apc = Actor.Create("m113", true, {
					Owner = USA,
					Location = CPos.New(Ground.X - 3, Ground.Y + 2),
					Facing = Angle.East,
				})

				Rider = Actor.Create("E1.america", true, {
					Owner = USA,
					Location = CPos.New(Ground.X - 5, Ground.Y + 2),
					Facing = Angle.East,
				})
			end)

			-- Ten ticks later, because LoadPassenger throws if the passenger is not IDLE and a
			-- just-created actor is not idle on its first tick.
			Trigger.AfterDelay(1740, function()
				if Apc and not Apc.IsDead and Rider and not Rider.IsDead then
					Apc.LoadPassenger(Rider)
					TestHarness.Select(Apc)
				end
			end)

			Trigger.AfterDelay(1760, function()
				TestHarness.Screenshot("08b-readout-lift-over-cargo",
					"THE LIFT. A loaded APC is selected, so CARGO_PANEL is up in the bottom-right " ..
					"corner -- 228x240, its top edge at WINDOW_HEIGHT - 260. expects: the cargo " ..
					"panel drawn in that corner with one passenger listed, and the DEFCON readout " ..
					"sitting ENTIRELY ABOVE IT, its lowest pixel 5px clear of the panel's top " ..
					"edge, with the same two blocks and the same 352px width as frame 08. The " ..
					"whole stack should look like frame 08 shifted straight up by exactly 260px " ..
					"-- its draw bottom goes from WINDOW_HEIGHT - 5 to WINDOW_HEIGHT - 265 -- " ..
					"with nothing clipped off its top and no gap opened between the nuclear block " ..
					"and the strip. " ..
					"FAIL, and this is the failure the frame exists to catch, if the readout is " ..
					"still drawn at the bottom with the cargo panel ON TOP OF IT -- the panel is " ..
					"declared later in the chrome file and so wins the overdraw, which means a " ..
					"broken lift looks like a readout that has half vanished rather than like two " ..
					"things overlapping. FAIL ALSO if the readout lifted but left the corner, or " ..
					"if it lifted in frames where no panel is up: compare against frame 09, taken " ..
					"540 ticks later with the selection handed back, where it must be low again.")
			end)

			-- Selection handed back, so frame 09 is the frame it has always been.
			Trigger.AfterDelay(1790, function()
				if Gun and not Gun.IsDead then
					TestHarness.Select(Gun)
				end
			end)

			-- ---- THE COOLDOWN ENDS AND THE LEVEL DOES NOT ------------------------------------
			-- Russia fired at ~1260 and its cooldown runs COOLDOWN_TICKS; 140 ticks of slack past it.
			Trigger.AfterDelay(1260 + COOLDOWN_TICKS + 140, function()
				TestHarness.Screenshot("09-cooldown-ended",
					"THE RATCHET, AND THE MOST INFORMATIVE FRAME IN THE FILE. expects: the YOU " ..
					"row's 20 kt box STILL LIT -- levels never fall, so what USA gained by being " ..
					"shot at is USA's for the rest of the match -- the ENEMY row's 1 kt box " ..
					"RECOVERED to plain lit, and BOTH clock columns reading READY. The value slot " ..
					"still reads '20 kt' and the foot line is the ready wording ('Firing puts your " ..
					"whole team on cooldown and raises the enemy's level.'). A " ..
					"'Nuclear cooldown ended.' line should be in the transients panel at the " ..
					"bottom-left. " ..
					"FAIL, and this is the failure the frame exists to catch, if the YOU row's " ..
					"20 kt box has gone DARK: that is a level falling, which this model does not " ..
					"do, and it would be v1's one-shot window surviving under a new name. FAIL " ..
					"ALSO if either clock is still counting. A MISSING TRANSIENT LINE is a softer " ..
					"fault: the panel holds its lines for a limited time, so check the tick gap " ..
					"before calling it.")
			end)

			-- Clean exit, after the last capture has had time to flush. Skip and not Pass: nothing
			-- here is asserted and a person decides what the frames show.
			Trigger.AfterDelay(1260 + COOLDOWN_TICKS + 240, function()
				Test.Skip("eyeball: the nuclear ledger -- release lights both rows READY, Russia's " ..
					"1 kt raises USA to 20 kt permanently and puts Russia's whole row on one " ..
					"cooldown, the cooldown runs out and USA's new box stays lit. Fire order " ..
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
