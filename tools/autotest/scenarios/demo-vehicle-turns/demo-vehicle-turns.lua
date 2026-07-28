-- DEMO: vehicle turn feel (PIPELINE item 27, YAML-only). Load it, watch, close when done.
-- NO verdict — this is a demo (no Test.Pass / Test.Fail).
--
-- WHAT THIS SHOWS
--   Four USA lanes, each looping a horizontal serpentine of 90-degree corners. The tuning under test:
--     * ^Vehicle TurnSpeedLoss 1 -> 0  — vehicles no longer bleed speed inside a turning arc, so they
--       sweep the corner keeping pace instead of stuttering to a crawl and re-accelerating.
--     * raised Mobile TurnSpeed        — the hull swings onto the new heading sooner, so the pivot at
--       each corner is shorter and the column reads less robotic.
--   WATCH: the corners. Pre-tuning the same course stutter-pivots at every jog; post-tuning each lane
--   carves the zig-zag as continuous sweeps. Lane 1 (abrams x3) is the clearest read — a whole column.
--
-- HOW TO COMPARE BEFORE/AFTER: run this demo on this branch, then on main (or `git stash` the tuning
-- commit) and run it again. The course, speeds and Acceleration are unchanged — only the turn knobs
-- differ, so any difference you see at the corners is exactly this change.
--
-- Everything is HoldFire and there are no enemies, so the only motion on screen is the turn behaviour.

local TPS = TestHarness.TicksPerSecond

local function holdFire(actor)
	if actor and not actor.IsDead then actor.Stance = "HoldFire" end
end

-- Build a looping serpentine (east with south jogs, then mirrored back west) for a lane whose straight
-- run is on row `yb`, jogging `jog` cells south. The repeated 90-degree corners are the whole point.
local function makeCourse(yb, jog)
	local xs = { 6, 16, 26, 36, 46, 56 }
	local wp = {}
	local function add(x, y) wp[#wp + 1] = CPos.New(x, y) end

	-- East leg: alternate straight run / south jog / straight run ...
	for i = 1, #xs do
		local x = xs[i]
		if i % 2 == 1 then
			add(x, yb)
			add(x, yb + jog)
		else
			add(x, yb + jog)
			add(x, yb)
		end
	end
	-- West leg back to the start, mirrored so the return trip also corners.
	for i = #xs, 1, -1 do
		local x = xs[i]
		if i % 2 == 1 then
			add(x, yb + jog)
			add(x, yb)
		else
			add(x, yb)
			add(x, yb + jog)
		end
	end
	return wp
end

-- Drive `actor` around `course` forever: OnIdle only fires once the queue drains, so re-queuing the
-- whole loop there makes it repeat without stacking activities.
local function loopCourse(actor, course)
	if not actor or actor.IsDead then return end
	local function issue()
		if actor.IsDead then return end
		for _, cell in ipairs(course) do actor.Move(cell) end
	end
	Trigger.OnIdle(actor, issue)
end

WorldLoaded = function()
	TestHarness.FocusBetween(Mbt0, Truck0)
	TestHarness.Select(Mbt0)

	UserInterface.SetMissionText(
		"Vehicle turn-feel demo — each lane loops a 90-degree serpentine. Watch the corners: vehicles keep speed through the sweep. No verdict.")

	local column = { Mbt0, Mbt1, Mbt2 }
	for _, u in ipairs(column) do holdFire(u) end
	holdFire(Ifv0)
	holdFire(Scout0)
	holdFire(Truck0)

	-- Lane bands (base row -> south jog). Spacing leaves a clear gap between lanes so paths never cross.
	local mbtCourse = makeCourse(4, 4)
	for _, u in ipairs(column) do loopCourse(u, mbtCourse) end
	loopCourse(Ifv0, makeCourse(12, 4))
	loopCourse(Scout0, makeCourse(20, 4))
	loopCourse(Truck0, makeCourse(28, 3))

	Trigger.AfterDelay(1 * TPS, function()
		Media.DisplayMessage("Watch the corners — each lane sweeps the 90-degree jogs keeping speed.", "DEMO")
	end)
end
