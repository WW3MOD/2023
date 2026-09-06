-- DEMO -- the six screen-shake profiles, fired in sequence at a fixed camera. Nothing here is
-- asserted: no AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer watches, and
-- closes the window when done.
--
-- WATCH THE CAMERA, NOT THE MAP. Every probe explosion is damage-free and visual-free; the only
-- thing that happens when one dies is that the viewport moves. The two Abrams south of the camera
-- are there purely to give the eye some hard straight edges to judge the motion against, which is
-- much harder over open grass.
--
-- LAYOUT, so this file can be read without map.yaml. The camera is pinned at cell 64,100 and never
-- moves -- distance falloff and propagation delay are both measured from the viewport centre, so
-- panning between stages would change the quantity being demonstrated. Six probes, distances from
-- that camera:
--
--     P1  e1  64,100    0 cells   shell impact
--     P2  e2  65,100    1 cell    building collapse
--     P3  e3  64,101    1 cell    heavy impact
--     P4  e4  65,101  1.4 cells   TACTICAL NUKE, all four stages
--     P5  at  63,60    40 cells   TACTICAL NUKE, the same four stages, far away
--     P6  sn  66,100    2 cells   HIGH-YIELD NUKE, all five stages
--
-- Every near probe is inside ReferenceDistance (4 cells), where the falloff curve is flat at 1.0,
-- so those five stages deliver their stated peak amplitude exactly and distance cannot confound a
-- comparison between two of them. The probes are infantry, and every cell is one that
-- demo-highyield-nuke already places an actor on -- twin-rivers has rivers and cliffs through it.
--
-- THE SCHEDULE, in raw ticks from WorldLoaded. Timestep is 60 ms, so 16.67 ticks per second -- NOT
-- the 25 that TestHarness.TicksPerSecond carries (a deliberately-preserved harness convention for
-- AssertWithin budgets, documented in test-helpers.lua, and not the tick rate). Every delay below
-- is therefore a raw tick count rather than a seconds conversion.
--
--     t=50     P1 shell fires.            2 px peak, 2.98 Hz, done by t=75.
--     t=150    P2 collapse fires.         2.8 px peak, 2.71 Hz, done by t=190.
--     t=270    P3 heavy impact fires.     8 px peak, 1.66 Hz, done by t=360.
--     t=420    P4 tactical nuke fires.   12.0 px peak, 1.30 Hz, done by t=698.
--     t=760    P5 tactical nuke, FAR.    ground wave arrives t=796; air blast arrives t=1072.
--     t=1450   P6 high-yield fires.      16.7 px peak, 1.11 Hz, done by t=2200.
--
-- P4 AND P5 ARE THE SAME WEAPON, and the pair is the thing this demo is really for. At the camera
-- it is an immediate 12 px hit. At 40 cells the ground wave takes 36 ticks (2.2 s) just to arrive
-- and lands at 1.9 px, a sixth of the amplitude, and then the air-blast stage -- which propagates
-- at 7.8 ticks/cell against the ground wave's 0.9 -- turns up 312 ticks (18.7 s) after detonation,
-- long after the ground has gone quiet. Under the model this replaced those two would have been
-- simultaneous, and in fact under that model neither would have shaken the screen at all: every
-- ShakeScreen warhead in the mod omitted Multiplier, which used to default to 0,0. See the note on
-- `Atomic` in weapons-superweapons.yaml.
--
-- STAGE 5 IS DELIBERATELY FAINT. Under 2 px is about what a nuclear detonation 6.4 km away has any
-- business doing to a camera, and the stage is about the two ARRIVALS and the size of the drop
-- rather than about the size of the shake. Do not read it as a bug.
--
-- THE NUMERIC COMPANION. Screen shake cannot be judged from a screenshot, so the shape of each of
-- these profiles is also printed as an amplitude-over-time trace by the NUnit fixture
-- ScreenShakeModelTest.TabulateShippedProfiles, sampled from the shipping model itself:
--     dotnet test engine/OpenRA.Test/OpenRA.Test.csproj -c Release \
--       --filter "FullyQualifiedName~ScreenShakeModelTest" -l "console;verbosity=detailed"
-- Run that to read the envelopes; run this to feel them.
--
-- Every Hz figure quoted here is the CENTRE of the per-event detune band. FrequencyJitterPercent is
-- 6, so each detonation lands within about +/-6% of it and no two are identical -- which is part of
-- why firing the same weapon twice does not produce the same shake.

local CameraCell = { X = 64, Y = 100 }

local Stages =
{
	{
		tick = 50,
		probe = 1,
		title = "1/6  SHELL IMPACT, at the camera",
		note = "2 px peak, 2.98 Hz, 1.5 s. The fastest shake the tick rate can render.",
	},
	{
		tick = 150,
		probe = 2,
		title = "2/6  BUILDING COLLAPSE, 1 cell",
		note = "2.8 px peak, 2.71 Hz, 2.4 s. This is the shipped ShakeOnDeath profile.",
	},
	{
		tick = 270,
		probe = 3,
		title = "3/6  HEAVY IMPACT, 1 cell",
		note = "8 px peak, 1.66 Hz, 5.4 s. Watch it hit in 3 ticks and then roll off.",
	},
	{
		tick = 420,
		probe = 4,
		title = "4/6  TACTICAL NUKE, 1.4 cells -- all four stages",
		note = "12 px peak, 1.30 Hz, 16.7 s: precursor, main shock, coda, then the air blast.",
	},
	{
		tick = 760,
		probe = 5,
		title = "5/6  TACTICAL NUKE, 40 cells -- SAME WEAPON AS STAGE 4",
		note = "Faint on purpose. Ground wave in 2.2 s at 1.9 px; the air blast follows 18.7 s later.",
	},
	{
		tick = 1450,
		probe = 6,
		title = "6/6  HIGH-YIELD NUKE, 2 cells -- all five stages",
		note = "16.7 px peak, 1.11 Hz, 45 s. Lower and longer than stage 4, not merely bigger.",
	},
}

-- Announced this many ticks before the probe dies, so the caption is on screen and read before the
-- camera starts moving rather than during it.
local AnnounceLead = 25

local tick = 0
local fired = 0

-- Filled in WorldLoaded. Actors named in map.yaml arrive as bare globals rather than through any
-- lookup table, so they are gathered into an indexable list once the world exists.
local probes = nil

local function step()
	tick = tick + 1

	local next = Stages[fired + 1]
	if next ~= nil then
		if tick == next.tick - AnnounceLead then
			Media.DisplayMessage(next.title, "SHAKE")
			Media.DisplayMessage(next.note, "SHAKE")
		elseif tick >= next.tick then
			fired = fired + 1
			local probe = probes[next.probe]
			-- Staging, not an assertion: if a probe is somehow already gone the demo carries on to
			-- the next stage rather than stopping, because a demo that halts tells the viewer less
			-- than one that skips.
			if probe ~= nil and probe.IsDead ~= true then
				probe.Kill()
			end
		end
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(CameraCell.X * 1024 + 512, CameraCell.Y * 1024 + 512, 0)

	probes = { Probe1, Probe2, Probe3, Probe4, Probe5, Probe6 }

	Media.DisplayMessage("Screen-shake profiles. Six stages, each announced before it fires.", "SHAKE")
	Media.DisplayMessage("The camera never moves on its own -- everything you see is the shake.", "SHAKE")

	Trigger.AfterDelay(1, step)
end
