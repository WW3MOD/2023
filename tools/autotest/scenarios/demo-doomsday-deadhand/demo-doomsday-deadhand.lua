-- DEMO -- the FINAL EXCHANGE. No AssertWithin, no Test.Pass, no Test.Fail. It ENDS on Test.Skip
-- once the captures have flushed, which is the shape demo-danger-overlay and demo-territory-overlay
-- use: a `skip` verdict is not a claim that anything passed, it is the run saying "I finished, the
-- frames are on disk, a person decides". Without it run-test.sh has no verdict to read and kills
-- the game at its 300 s watchdog -- which is exactly what the first two runs of this demo did,
-- producing an empty lua.log and no PNGs.
--
-- WHAT THIS FILE EXISTS TO SHOW: a warhead a PLAYER placed inside the fifteen-second window,
-- landing in the SAME cascade as the ones the machine fires for the side that placed nothing. USA
-- fires its Trident; Russia does not, and Russia's own Sarmat is fired for it at the enemy. Both halves
-- of the user's ruling -- "either way the outcome is the same" -- are on screen at once.
--
-- RETIMED 2026-09-20 WITH THE REDESIGN. Dead Hand -- a map-wide, side-blind salvo with its own
-- lead-in and wave structure -- is gone, and the three knobs this demo used to compress
-- (LeadInTicks, OutlierToCityPauseTicks, WithinWaveTicks) went with it. There is now ONE cascade
-- anchored at the window's close plus FinalExchangeFlightTicks, so what this scenario compresses
-- instead is that one number and the two game-enders' MissileDelay. The subject is unchanged and
-- the window is still at its shipped 250.
--
-- THE ORDER GOES THROUGH THE REAL PATH. Test.ActivateSupportPower issues exactly the order
-- SelectGenericPowerTarget emits on a left-click (TestGlobal.cs:1658-1677), so what is exercised is
-- the support power a human would have clicked, not a private entry point. If the window did not
-- actually grant and arm the power, this returns 'not-ready:0' or 'hidden' and NOTHING FLIES from
-- USA -- which is the demo failing visibly rather than silently, and is why the status string is
-- printed on screen AND carried into every screenshot note from that point on.
--
-- ---- TICKS, NOT SECONDS ------------------------------------------------------------------
-- Timestep is 60 ms, so 16.67 ticks/s -- NOT the 25 that TestHarness.TicksPerSecond carries (a
-- deliberately-preserved harness convention for AssertWithin and ScreenshotAfter budgets,
-- documented in test-helpers.lua, and not the tick rate). Every number below is RAW TICKS, and the
-- ones handed to ScreenshotAfter are divided by TicksPerSecond to undo its conversion -- the same
-- `ticks / TestHarness.TicksPerSecond` form demo-defcon-readout uses.
--
-- ---- THE SCHEDULE, AND WHY IT IS AS SHORT AS IT IS ---------------------------------------
-- Two runs were CPU-starved by sibling builds and reached under 100 ticks in 300 s. That is not
-- this scenario's bug and nothing here can fix it, but the run is now ~535 ticks end to end where
-- it was ~780. What was cut and what was NOT:
--
--   CUT   the pre-window skirmish, 250 ticks -> 80. It staged a score difference for a
--         frozen-score verdict that a TestMode session cannot show anyway (see the caveat in
--         description.txt), so it was paying 170 ticks for something invisible.
--   CUT   the dead air on BOTH game-enders, 500 ticks shipped -> 120. SpawnActorEffect holds the
--         actor out of the world and renders NOTHING while it waits (SpawnActorEffect.cs:44-49),
--         so those ticks are a beacon and an empty sky. Both, not just the Trident: Russia's Sarmat
--         is fired for it at the close and carries the same 500.
--   CUT   FinalExchangeFlightTicks 800 -> 260, which is what the shortened MissileDelay makes
--         correct rather than merely convenient: it must cover MissileDelay plus the slowest
--         game-ender's flight, and on this 64x32 map b83missile crosses the standoff in ~128
--         ticks. 120 + 128 = 248, so 260.
--   CUT   AnnihilationDelayTicks 60 -> 30, ResolutionDelayTicks 20 -> 15.
--   KEPT  FinalExchangeWindowTicks at the shipped 250. It is the SUBJECT. A demo that showed a
--         six-second "fifteen seconds" would be demonstrating a deadline nobody ships.
--   KEPT  ImpactSpacingTicks at the shipped 15. It is the rhythm of the cascade and is the other
--         thing worth looking at here.
--
--     tick   what
--       70   Trident state printed BEFORE the window. Expect "hidden" -- both game-enders are event
--            tier and no faction provides powers.event, so the bin is empty. The control.
--            THE PACKAGE HERE IS TWO WARHEADS PER SIDE. 64x32 is 2048 playable cells, which
--            rounds to 1 at CellsPerImpact 2400 and is lifted to MinPackage 2 -- so this demo
--            also shows the floor doing its work. Four warheads total, on four cascade slots.
--
--       80   TIME LIMIT EXPIRES -> BeginFinalExchange. Band appears at 0:15.
--       85   Trident state printed again. Expect "ready". That flip IS the feature.
--       90   FRAME 01 -- band at ~0:15, Trident cameo present.
--       95   USA places its Trident. Expect "issued". TWO aim points, on a ring around the click.
--      250   FRAME 02 -- band at ~0:05, USA's beacons planted.
--      330   WINDOW CLOSES. Russia's own Sarmat is fired FOR it, at USA's half of the map.
--      336   FRAME 03 -- "The packages are in the air." + "Fired automatically for: Russia."
--      590   THE ANCHOR: 330 + FinalExchangeFlightTicks 260. USA slot 0.
--      605   USA slot 1.
--      592   FRAME 04 -- the first of USA's two going off; Russia's still inbound.
--      620   Russia slot 2.   635   Russia slot 3, the last impact of the cascade.
--      637   FRAME 05 -- all four craters, USA's on the east and Russia's reply on USA's ground.
--      665   ANNIHILATION, 30 ticks past the LAST impact of the cascade rather than past any one
--            warhead. Nothing lands before the window shuts and nothing lands after this.
--      680   Resolution.
--      700   Test.Skip. Verdict SKIP, exit 2, five PNGs on disk.

-- Ground zero for USA's own warhead: CITY EAST, which is also one of the two clusters Dead Hand
-- aims at. Deliberately the same kind of target the machine would pick, so the two placements are
-- comparable rather than one of them being obviously a demo artifact.
local AimPoint = { X = 49, Y = 22 }

-- WorldTick at which the Time Limit expires and the window opens. Mirrors TimeLimitTicks in
-- rules.yaml; the two must move together.
local WindowOpensTick = 80

-- Carried into the later screenshot notes so a frame that came back empty says WHY on its own,
-- without anyone having to correlate it against lua.log.
local PlacementStatus = "not attempted"

-- ScreenshotAfter takes SECONDS and converts with TicksPerSecond, so a tick count is divided by it
-- to undo that -- the `ticks / TestHarness.TicksPerSecond` form demo-defcon-readout uses. Exact for
-- integer ticks: the helper's own math.floor(seconds * TicksPerSecond) round-trips.
local function Frame(tick, label, note)
	TestHarness.ScreenshotAfter(tick / TestHarness.TicksPerSecond, label, note)
end

-- The same thing for a frame whose note has to be built AT CAPTURE TIME rather than at
-- registration time. Frame() above takes a plain string, which is composed when WorldLoaded runs --
-- i.e. before the placement has happened -- so a note quoting PlacementStatus through it would
-- freeze the word "not attempted" into the JSON forever and quietly lie about the run. Deferring
-- the composition is the same shape demo-defcon-readout uses for its two post-kill frames.
local function LateFrame(tick, label, noteFn)
	Trigger.AfterDelay(tick, function()
		TestHarness.Screenshot(label, noteFn())
	end)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	Russia = Player.GetPlayer("Russia")

	-- Between the central skirmish (25-29,17-19) and CITY EAST (45-53,20-25), so the placed
	-- beacon and its impact are both in frame. The western city and two of the four derricks are
	-- deliberately off-frame: the salvo is spread over the whole 64x32 playfield and no single
	-- camera holds all of it. The countdown band is full-width and is unaffected either way.
	Camera.Position = WPos.New(40 * 1024 + 512, 19 * 1024 + 512, 0)

	-- Pre-selected so the support-power bin is on screen BEFORE the window opens, which is what
	-- lets frame 01 show the Trident cameo appearing rather than take its arrival on trust.
	-- UsaA is the map actor id from map.yaml; map actors are exposed to Lua under their id.
	TestHarness.Select(UsaA)

	Media.DisplayMessage("Final exchange demo: the clock runs out, both sides get 15 s to place "
		.. "their own game-enders, and an unplaced package fires at the enemy anyway.", "FINAL EXCHANGE")

	-- THE CONTROL. Outside the exchange the bin must be EMPTY: both game-enders carry
	-- `Prerequisites: powers.event`, provided by no faction and by no lobby option here, so
	-- SupportPowerInstance.Permitted is false however many conditions are granted. Its appearance
	-- fifteen ticks later is only evidence because of this line.
	Trigger.AfterDelay(WindowOpensTick - 10, function()
		Media.DisplayMessage("Before the window, Trident reads: "
			.. Test.GetSupportPowerState(USA, "TridentStrike"), "DEAD HAND")
	end)

	Trigger.AfterDelay(WindowOpensTick + 5, function()
		Media.DisplayMessage("Window open. Trident now reads: "
			.. Test.GetSupportPowerState(USA, "TridentStrike")
			.. " -- watch the countdown band.", "DEAD HAND")
		-- THE BIN ITSELF, PRINTED, because frame 01 cannot show it: the right-hand panel opens on
		-- the infantry tab and there is no Test verb that selects another. This line is what makes
		-- that frame's "is the power actually in the bin" question answerable from the capture.
		Media.DisplayMessage("Support bin draws: " .. Test.GetSupportPowerBin(USA), "DEAD HAND")
	end)

	Frame(WindowOpensTick + 10, "01-window-open",
		"THE FRAME THE FEATURE LIVES OR DIES ON. ~10 ticks into the window. expects: a full-width "
		.. "band about a third down the screen reading FINAL EXCHANGE / PLACE YOUR STRIKE PACKAGE "
		.. "-- UNPLACED FIRES AT THE ENEMY, with a clock at or just under 0:15 and its borders "
		.. "reaching BOTH screen edges; and a Trident cameo "
		.. "now present in the support-power bin. "
		.. "READ THE PRINTED BIN CONTENTS, NOT THE GRID, AND THIS NOTE USED TO GET IT WRONG. The "
		.. "right-hand panel opens on the INFANTRY tab, not the nuclear one, so its grid is empty "
		.. "in a correct run and the old wording (`FAIL if the bin is still empty`) misread that as "
		.. "a failure. There is no Test verb that selects a production tab -- nothing in "
		.. "TestGlobal resolves one -- so the bin is read through Test.GetSupportPowerBin() and "
		.. "printed into the transients panel instead, where it is visible in this frame whatever "
		.. "tab is showing. FAIL if the band is absent (the window never opened), if it reads as a "
		.. "dialog box with visible ends, or if the printed bin does NOT contain TridentStrike (the "
		.. "power was never armed -- that is the tier or the magazine, not the condition; see "
		.. "SupportPowerInstance.MakeReady).")

	-- USA places. Russia deliberately does not.
	Trigger.AfterDelay(WindowOpensTick + 15, function()
		-- A SINGLE-TARGET ORDER, which is what this binding issues and what a bot issues. The
		-- power is a game-ender, so MissileStrikePower asks DoomsdayStrike for the package size
		-- (2 here) and lays the second bomb on the AimPointFallbackSpread ring around this click.
		-- That path is only exercised because the binding cannot drive placement mode.
		PlacementStatus = Test.ActivateSupportPower(USA, "TridentStrike", CPos.New(AimPoint.X, AimPoint.Y))
		Media.DisplayMessage("USA places its Trident on the eastern city: " .. PlacementStatus, "FINAL EXCHANGE")
	end)

	LateFrame(250, "02-band-five-seconds", function()
		return "~80 ticks left, so the clock should read about 0:05 and the digits should have "
			.. "turned RED (UrgentSeconds is 5). expects: the band still up and counting, and "
			.. "USA's beacons planted around the eastern city at 49,22 -- TWO of them, one per "
			.. "warhead of the package -- with their clocks sweeping. FAIL if no beacon is "
			.. "present: the placement order did not take. The order returned: " .. PlacementStatus
	end)

	Frame(336, "03-packages-away",
		"Six ticks after the window shut. expects: the countdown band GONE, and two system lines "
		.. "in the transients panel -- 'The packages are in the air.' immediately followed by "
		.. "'Fired automatically for: Russia.' That second line is the placed-vs-fired-for split, "
		.. "and Russia must be the only name in it. FAIL if USA is also named: that would mean the "
		.. "placement at tick 95 was not recorded.")

	Frame(592, "04-cascade-opens",
		"THE ANCHOR TICK, plus two. expects: USA's first 455 kt going off around the eastern city, "
		.. "and NOTHING having gone off before it -- the whole point of the cascade is that no "
		.. "warhead of the exchange lands until the window has shut and the flight has been flown. "
		.. "Russia's two should still be inbound, arriving 28 and 43 ticks later. FAIL if the map "
		.. "is already cratered at this tick: something is flying on its own schedule.")

	-- ==== THE FIREBALL FRAME, ADDED 2026-09-20. NOBODY HAS LOOKED AT A 455 kt DETONATION ====
	-- Run 260920_155623 captured 592 (everything still inbound) and 638 (a full-screen whiteout),
	-- so the W88's own fireball and its terrain wash appeared in NEITHER. 615 is chosen to sit
	-- after USA's two impacts at 590 and 605 and before Russia's at 620, with the 590 flash
	-- (FlashPaletteEffect Duration: 30) already decaying -- the one window in the whole run where
	-- a fireball is on screen and the screen is not white.
	--
	-- IT ALSO SETTLES A TIMING QUESTION. At 592, three ticks past a reserved impact of 590, the
	-- missile was visibly still in the air with its beacon still drawn -- while 638, three ticks
	-- past the 635 impact, was a whiteout. Either the flight overruns
	-- BallisticMissileFly.EstimateArcTicks (which the cascade solves its launch delay backwards
	-- from, so an overrun lands the warhead late) or the capture lands earlier than its label. If
	-- THIS frame shows two craters, the impacts happened between 593 and 615 and it is an overrun
	-- of roughly twenty ticks; if it shows them still inbound, the estimate is badly wrong and the
	-- cascade's arithmetic needs re-checking against a real flight rather than an estimate.
	Frame(615, "05-fireballs",
		"THE ONLY FRAME IN THE RUN THAT SHOWS A 455 kt DETONATION WITHOUT A WHITEOUT OVER IT. "
		.. "expects: USA's two fireballs over the eastern city, the 590 one already fading and the "
		.. "605 one at or near full, with the terrain wash visible around them and Russia's two "
		.. "warheads NOT yet landed (they are due at 620 and 635). "
		.. "WHAT TO JUDGE, because this is the first look anybody has had at it: the fireball "
		.. "sprite scale. NukeW88's Fireball.ScalePercent is 585 and it is the ONE number in that "
		.. "weapon block that could not be tied to a stated scaling law -- it was interpolated on "
		.. "the exponent the shipped 100 kt / 750 kt pair implies (0.214), and the fireball light "
		.. "generator READS it to size its radii. Too large or too small here means that number is "
		.. "wrong and the Light block is wrong with it. Compare against the 750 kt Sarmat RV, which "
		.. "sits at 651. "
		.. "ALSO: whether any impact has happened at all. If the map is still clean, see the note "
		.. "above this frame -- the flight is overrunning its estimate.")

	-- CAMERA CAVEAT, and it is worth stating rather than discovering from a frame: the camera sits
	-- at 40,19 to hold USA's own aim point at 49,22, while Russia's package is aimed at USA's HALF
	-- of the map -- west of the derived bisector between 26,18 and 56,13, i.e. left of about x=41.
	-- Russia's two craters may therefore sit at or past the western frame edge. That is the
	-- targeting working; if the frame needs all four in shot, move the camera west rather than
	-- moving the aim point east.
	LateFrame(637, "06-flash-stack", function()
		return "Two ticks after the LAST slot of the cascade, and IT IS A FULL WHITEOUT BY DESIGN "
			.. "-- that is what to judge here, not craters. Four FlashPaletteEffects of Duration 30 "
			.. "are overlapping (590, 605, 620, 635), so the terrain is washed out and only health "
			.. "bars and the minimap read. Run 260920_155623 captured exactly this and its note "
			.. "asked for four craters, which this frame can never show. "
			.. "expects: a white screen with unit bars still visible, i.e. the annihilation sweep "
			.. "has NOT run yet (it is 30 ticks out). FAIL if the screen is NOT washed out: the "
			.. "flash stack is not firing, and at four nuclear detonations inside 45 ticks it must. "
			.. "The craters are frame 07. The order returned: " .. PlacementStatus
	end)

	-- ==== THE AFTERMATH, ADDED 2026-09-20, AND IT IS THE FRAME 06 WAS ALWAYS MEANT TO BE ====
	-- 685 is past the last flash (635 + Duration 30 = 665), past the annihilation at 665 and past
	-- the resolution at 680. It is the only tick in the run at which the map is both unwashed and
	-- final.
	LateFrame(685, "07-aftermath", function()
		return "The ending, after the flashes have faded and the annihilation sweep has run. "
			.. "expects: FOUR craters -- USA's two around the eastern city at 49,22, and Russia's "
			.. "two on USA's HALF of the map, which is the whole of what replaced the side-blind "
			.. "Dead Hand salvo -- and nothing left standing, because Annihilate killed every actor "
			.. "on the map at tick 665. "
			.. "FAIL if any of Russia's craters is on RUSSIA's own ground: the enemy-half "
			.. "classification is wrong, which on a scenario is usually the HomeLocation trap (see "
			.. "DoomsdayStrike.AnchorOf). "
			.. "KNOWN AND EXPECTED: one of Russia's two aim points is the far NW map corner. The "
			.. "camera is at 40,19 to hold USA's aim point, so that crater is off-frame to the "
			.. "upper left and its absence here is NOT a failure. Why a corner gets a warhead at "
			.. "all is FinalExchangeTargeting's padding tier and is reported separately; it is a "
			.. "property of this map's emptiness plus the Sarmat's 30-cell AimPointRadius, not of "
			.. "this frame. The order returned: " .. PlacementStatus
	end)

	-- Clean exit (Skip, not Pass/Fail) after the resolution at ~532 and after the last capture has
	-- flushed. ExitWhenCapturesFlushed retries until the PNGs are on disk, so this cannot truncate
	-- frame 05.
	Trigger.AfterDelay(730, function()
		Test.Skip("eyeball: final exchange -- window at 0:15 and 0:05, the placed-vs-fired-for "
			.. "split, and one four-warhead cascade with nothing landing before the window shut. "
			.. "doomsday{" .. Test.DoomsdayState() .. "} usa{" .. Test.FinalExchangePackage(USA)
			.. "} rus{" .. Test.FinalExchangePackage(Russia)
			.. "}. Placement order returned: " .. PlacementStatus)
	end)
end
