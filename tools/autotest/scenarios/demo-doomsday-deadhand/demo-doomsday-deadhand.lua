-- DEMO -- the FINAL EXCHANGE. No AssertWithin, no Test.Pass, no Test.Fail. It ENDS on Test.Skip
-- once the captures have flushed, which is the shape demo-danger-overlay and demo-territory-overlay
-- use: a `skip` verdict is not a claim that anything passed, it is the run saying "I finished, the
-- frames are on disk, a person decides". Without it run-test.sh has no verdict to read and kills
-- the game at its 300 s watchdog -- which is exactly what the first two runs of this demo did,
-- producing an empty lua.log and no PNGs.
--
-- WHAT THIS FILE EXISTS TO SHOW, and it is the one thing the demo could not show before: a warhead
-- a PLAYER placed inside the fifteen-second window, flying alongside the ones Dead Hand places for
-- the side that placed nothing. USA fires its B83; Russia does not, and Dead Hand aims for Russia.
-- Both halves of the user's ruling -- "either way the outcome is the same" -- are on screen at once.
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
--   CUT   the B83's dead air, 500 ticks shipped -> 260. SpawnActorEffect holds the actor out of
--         the world and renders NOTHING while it waits (SpawnActorEffect.cs:44-49), so those
--         ticks are a beacon and an empty sky. 260 is the smallest value that still lands the
--         player's warhead AFTER the Dead Hand salvo, which is the ordering the demo is for.
--   CUT   LeadInTicks 25 -> 10, AnnihilationDelayTicks 60 -> 30, ResolutionDelayTicks 20 -> 15.
--   KEPT  FinalExchangeWindowTicks at the shipped 250. It is the SUBJECT. A demo that showed a
--         six-second "fifteen seconds" would be demonstrating a deadline nobody ships.
--   KEPT  OutlierToCityPauseTicks 40. It carries the user's intent about the salvo's rhythm.
--
--     tick   what
--       70   B83 state printed BEFORE the window. Expect "hidden" -- both game-enders are event
--            tier and no faction provides powers.event, so the bin is empty. The control.
--       80   TIME LIMIT EXPIRES -> BeginFinalExchange. Band appears at 0:15.
--       85   B83 state printed again. Expect "ready". That flip IS the feature.
--       90   FRAME 01 -- band at ~0:15, B83 cameo present.
--       95   USA places its B83 on the eastern city. Expect "issued".
--      250   FRAME 02 -- band at ~0:05, USA's beacon planted.
--      330   WINDOW CLOSES. Dead Hand places the rest.
--      336   FRAME 03 -- "DEAD HAND ACTIVATED." + "Dead Hand placed for: Russia."
--  384-432   Dead Hand salvo: four derricks 2 ticks apart, the 40-tick pause, then one warhead
--            per city (WarheadsPerCity is 1 since the 2026-09-07 retune).
--      392   FRAME 04 -- derrick impacts down, city missiles in the air.
--      487   USA's B83 arrives, LAST. 95 + 260 + 0 PreLaunchTicks + ~132 flight; B83Missile has
--            Acceleration 0 and Speed 700 so EstimateArcTicks is hDist/speed, and hDist is the
--            standoff, sqrt(66^2+34^2) + 16 = ~90 cells.
--      490   FRAME 05 -- the player's own 1.2 Mt going off after the machine's salvo.
--      517   ANNIHILATION, held 30 ticks past the B83 rather than past the salvo. Without the
--            hold it would have run at ~462, while the player's warhead was still in the air.
--      532   Resolution.
--      545   Test.Skip. Verdict SKIP, exit 2, five PNGs on disk.

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
	-- lets frame 01 show the B83 cameo appearing rather than take its arrival on trust.
	-- UsaA is the map actor id from map.yaml; map actors are exposed to Lua under their id.
	TestHarness.Select(UsaA)

	Media.DisplayMessage("Final exchange demo: the clock runs out, both sides get 15 s to place "
		.. "their own game-enders, Dead Hand places the rest.", "DEAD HAND")

	-- THE CONTROL. Outside the exchange the bin must be EMPTY: both game-enders carry
	-- `Prerequisites: powers.event`, provided by no faction and by no lobby option here, so
	-- SupportPowerInstance.Permitted is false however many conditions are granted. Its appearance
	-- fifteen ticks later is only evidence because of this line.
	Trigger.AfterDelay(WindowOpensTick - 10, function()
		Media.DisplayMessage("Before the window, B83 reads: "
			.. Test.GetSupportPowerState(USA, "B83Strike"), "DEAD HAND")
	end)

	Trigger.AfterDelay(WindowOpensTick + 5, function()
		Media.DisplayMessage("Window open. B83 now reads: "
			.. Test.GetSupportPowerState(USA, "B83Strike")
			.. " -- watch the countdown band.", "DEAD HAND")
	end)

	Frame(WindowOpensTick + 10, "01-window-open",
		"THE FRAME THE FEATURE LIVES OR DIES ON. ~10 ticks into the window. expects: a full-width "
		.. "band about a third down the screen reading FINAL EXCHANGE / PLACE YOUR WARHEADS with a "
		.. "clock at or just under 0:15, its borders reaching BOTH screen edges; and a B83 cameo "
		.. "now present in the support-power bin that was empty ten ticks earlier. FAIL if the band "
		.. "is absent (the window never opened), if it reads as a dialog box with visible ends, or "
		.. "if the bin is still empty (the power was never armed -- that is the tier or the "
		.. "magazine, not the condition; see SupportPowerInstance.MakeReady).")

	-- USA places. Russia deliberately does not.
	Trigger.AfterDelay(WindowOpensTick + 15, function()
		PlacementStatus = Test.ActivateSupportPower(USA, "B83Strike", CPos.New(AimPoint.X, AimPoint.Y))
		Media.DisplayMessage("USA places its B83 on the eastern city: " .. PlacementStatus, "DEAD HAND")
	end)

	LateFrame(250, "02-band-five-seconds", function()
		return "~80 ticks left, so the clock should read about 0:05 and the digits should have "
			.. "turned RED (UrgentSeconds is 5). expects: the band still up and counting, and "
			.. "USA's beacon planted on the eastern city at 49,22 with its own clock sweeping. "
			.. "FAIL if the beacon is absent -- the placement order did not take. The order "
			.. "returned: " .. PlacementStatus
	end)

	Frame(336, "03-dead-hand-places",
		"Six ticks after the window shut. expects: the countdown band GONE, and two system lines "
		.. "in the transients panel -- 'DEAD HAND ACTIVATED. Incoming.' immediately followed by "
		.. "'Dead Hand placed for: Russia.' That second line is the placed-vs-placed-for split, "
		.. "and Russia must be the only name in it. FAIL if USA is also named: that would mean the "
		.. "placement at tick 95 was not recorded.")

	Frame(392, "04-deadhand-salvo",
		"The machine's own salvo. expects: the four derrick impacts down or going off (they land "
		.. "384/386/388/390, two ticks apart), near-vertical re-entry trails, and the two city "
		.. "missiles already in the air during the 40-tick pause. ONE warhead per city, not two -- "
		.. "WarheadsPerCity has been 1 since the 2026-09-07 retune. There is NO third wave: the "
		.. "fill pass is gone.")

	LateFrame(490, "05-player-warhead-arrives", function()
		return "USA's own 1.2 Mt, arriving AFTER the machine's salvo has finished. expects: a "
			.. "single very large detonation centred on the eastern city, on a map the Dead Hand "
			.. "salvo has already cratered. This frame is also the resolution-timing evidence: the "
			.. "annihilation sweep has NOT run yet at this tick, and would have run at ~462 without "
			.. "the hold. FAIL if the map is already swept flat and nothing arrives -- that is "
			.. "ExtendScheduleForImpact not firing. The order returned: " .. PlacementStatus
	end)

	-- Clean exit (Skip, not Pass/Fail) after the resolution at ~532 and after the last capture has
	-- flushed. ExitWhenCapturesFlushed retries until the PNGs are on disk, so this cannot truncate
	-- frame 05.
	Trigger.AfterDelay(545, function()
		Test.Skip("eyeball: final exchange -- window at 0:15 and 0:05, Dead Hand split, salvo, "
			.. "player-placed B83 last. Placement order returned: " .. PlacementStatus)
	end)
end
