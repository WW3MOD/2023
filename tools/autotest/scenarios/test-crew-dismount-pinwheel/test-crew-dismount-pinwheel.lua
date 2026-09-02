-- CAPTURE INSTRUMENT: what a rear dismount LOOKS LIKE, on four hull facings at once.
--
-- =====================================================================================
-- THIS GRADES NOTHING. IT IS NOT A GUARD. The guard is test-crew-rear-dismount.
-- =====================================================================================
-- The terminal verdict is Test.Skip, for the same reason test-minimap-stance-shades' and
-- test-fog-darkness-ruler's are: no clause here is a pass/fail statement about the mod. The output
-- is six PNGs plus the readout below, and a human decides. A Test.Pass would contribute a green to
-- every run-batch.sh --all tally whether or not the pixels showed anything, which is the exact
-- mislabelling corrected across this suite on 2026-09-01.
--
-- WHY IT EXISTS. WORKSPACE/MILESTONE-260901.md grades item 3 (rear dismount, 3ce18d71) as ASSERTED
-- and says so in as many words: "No frame was read for this one." test-crew-rear-dismount queries
-- three crew members' Location and grades the half-plane they landed in. That is a strong claim
-- about geometry and a claim about NOTHING ELSE — it cannot see whether the men render, whether
-- they are where the engine says they are, or whether any of it is legible to a player. The
-- milestone is careful about this distinction elsewhere (4a2844e7 is explicit that a PASS there
-- certifies a node chain and not that a line renders), and this scenario is what closes it here.
--
-- =====================================================================================
-- WHY FOUR HULLS. The single-hull frame would have been nearly worthless.
-- =====================================================================================
-- Before 3ce18d71 the exit direction was `w.SharedRandom.Next(8)` — a uniform compass roll with no
-- reference to hull facing at all. For ONE north-facing hull, a uniform roll still keeps a given man
-- out of the front three cells 5 times in 8, so a single frame of three men would have looked
-- correct roughly one run in four. A reader shown that frame could not have told the fix from the
-- bug.
--
-- What a uniform roll cannot produce is CORRELATION. Four hulls carry the four cardinal facings, so
-- the claim the pixels have to support is not "the men are behind the tank" but "the empty arc
-- points the way the hull points, four times, in four different directions". Under the old roll,
-- even the weak form of that — no man in any hull's front three-cell arc, twelve men — is
-- (5/8)^12 = 5.5e-5.
--
-- It also kills a second alternative the single-hull frame could not: a facing-BLIND fan, e.g. one
-- that always sent men south/east/west regardless of orientation. That would look right on the
-- north hull and would show up here as four IDENTICAL triads instead of four rotated ones.
--
-- =====================================================================================
-- THE SHAPE THE READER MUST EXPECT — get this wrong and a correct frame grades as a failure
-- =====================================================================================
-- DismountGeometry.FanOffsets is { 0, +256, -256, +128, -128 } and +-256 is exactly +-90 degrees.
-- A THREE-man crew therefore consumes only the first three: one man STRICTLY ASTERN and two EXACTLY
-- ABEAM. It is not an arc of three men behind the hull; it is a T. For a hull pointing up the
-- screen that is men at 6, 3 and 9 o'clock with 12 empty. The +-90 bound is deliberate and
-- documented in DismountGeometry.cs ("A wider fan would send the tail of a full transport out PAST
-- the hull's shoulders and around its nose").
--
-- =====================================================================================
-- WHAT THE PRINTED READOUT IS FOR, AND WHAT IT IS NOT FOR
-- =====================================================================================
-- The Lua below measures each man's cell offset from his own hull and prints it. That readout is
-- NOT the verification — it is the same kind of state query test-crew-rear-dismount already grades,
-- and treating it as the answer would launder an ASSERTED result into a SEEN one, which is the one
-- thing this scenario exists not to do. Its job is to let a reader detect DISAGREEMENT between the
-- frame and the engine: if the printout says a man is two cells south and the picture shows nobody
-- south, then the picture is not a picture of this state and neither claim survives. Deliberately
-- there is no "all twelve are astern" summary line, because that line would be read as a verdict.
--
-- FACING IS COUNTERCLOCKWISE (0 N, 256 WEST, 512 S, 768 EAST) and MapGrid is Rectangular, so map
-- north is straight up on screen. Both are load-bearing for every direction word in the capture
-- notes; map.yaml carries the full derivation.
--
-- THE MEN IN THESE FRAMES ARE LYING DOWN, AND THAT IS CORRECT. ^CrewMember inherits ^CamoSoldier
-- and therefore InfantryStates, whose ProneCondition includes `!moving` (infantry.yaml:316) — an
-- idle infantryman in this mod goes prone, deliberately (crew.yaml:5-10, "Without this they stood
-- upright while idle ... felt completely different from other soldiers"). Every man here is idle at
-- the shutter by construction, so every man is prone. It is not a wound, not a death, and not
-- suppression. It is stated in the capture notes because a reader who does not know it will read
-- twelve prone soldiers as twelve casualties and conclude the bail-out killed the crew.

local Hulls = {
	{
		key = "north", actor = HullNorth, facing = 0,
		nose = "UP", noseClock = "12", label = "NORTH (nose UP)",
		expect = "one man BELOW (6 o'clock), one RIGHT (3), one LEFT (9); NOTHING ABOVE",
	},
	{
		key = "east", actor = HullEast, facing = 768,
		nose = "RIGHT", noseClock = "3", label = "EAST (nose RIGHT)",
		expect = "one man LEFT (9 o'clock), one ABOVE (12), one BELOW (6); NOTHING RIGHT",
	},
	{
		key = "south", actor = HullSouth, facing = 512,
		nose = "DOWN", noseClock = "6", label = "SOUTH (nose DOWN)",
		expect = "one man ABOVE (12 o'clock), one LEFT (9), one RIGHT (3); NOTHING BELOW",
	},
	{
		key = "west", actor = HullWest, facing = 256,
		nose = "LEFT", noseClock = "9", label = "WEST (nose LEFT)",
		expect = "one man RIGHT (3 o'clock), one ABOVE (12), one BELOW (6); NOTHING LEFT",
	},
}

local CrewTypes = { "crew.commander.america", "crew.gunner.america", "crew.driver.america" }
local CrewPerHull = 3

-- Camera. The four hulls span cells 17..47 across and 8..26 down, so the midpoint is 32,17.
local WideCameraX = 32
local WideCameraY = 17
local WideZoom = 1.4
local HullZoom = 2.6

-- A man is attributed to the nearest hull. Hulls are 24 cells apart in X and 12 in Y and the fan
-- reaches 3, so a correct attribution has the owner within 4 and every other hull beyond 8. Both
-- bounds are CHECKED at measure time rather than trusted, because an attribution that silently
-- picked the wrong hull would relabel the whole readout without looking wrong.
local OwnRadius = 4
local ForeignRadius = 8

-- Absolute tick schedule. Fixed delays rather than a settle poll, following
-- test-minimap-stance-shades: with AutoEvacuateOnEject off (rules.yaml) the crew go idle on their
-- fan cells and STAY there for the rest of the run, so there is no moment to race and a generous
-- constant is strictly safer than a predicate. The instrument check at SettleTick is what confirms
-- the delay was in fact long enough; it does not have to be what times the shot.
--
-- Budget: worst-case ejection is PostStopDelay 20 + 15 = 35 ticks to the first man, then
-- EjectionDelay 30 + 15 = 45 to each of the next two, so the third leaves at ~125. His walk is at
-- most 3 cells at Mobile Speed 25 (infantry.yaml:47), i.e. 1024/25 = 41 ticks per cell = ~123. So
-- ~250 ticks worst case and 500 is a clean 2x.
local ReferenceShotTick = 60
local DamageTick = 120
local SettleTick = DamageTick + 500
local Gap = 40

local setupNotes = {}

local function Note(fmt, ...)
	setupNotes[#setupNotes + 1] = string.format(fmt, ...)
end

local function LiveCrew()
	local all = {}
	for _, h in ipairs(Hulls) do
		if h.actor ~= nil and not h.actor.IsDead then
			for _, t in ipairs(CrewTypes) do
				for _, a in ipairs(h.actor.Owner.GetActorsByType(t)) do
					if not a.IsDead and a.IsInWorld then
						all[#all + 1] = a
					end
				end
			end

			-- Every hull shares one owner, so one pass over that owner's actors is the whole
			-- population. Break out rather than counting it four times.
			break
		end
	end

	return all
end

-- Confirm the hulls resolved and that each is actually pointing where map.yaml claims. This is the
-- check that lets the capture notes name a direction: the reader is told "the top-left hull points
-- up", and that sentence is only worth anything if something read the engine's own IFacing rather
-- than the author's intention. A mismatch invalidates the frame outright, so it is a hard skip.
local function CheckHulls()
	local ok = true

	for _, h in ipairs(Hulls) do
		if h.actor == nil or h.actor.IsDead then
			Note("hull %s did not resolve", h.key)
			ok = false
		else
			local actual = h.actor.Facing.Angle
			print(string.format("[pinwheel] hull %-5s at %d,%d facing=%d (authored %d) nose points %s",
				h.key, h.actor.Location.X, h.actor.Location.Y, actual, h.facing, h.nose))
			if actual ~= h.facing then
				Note("hull %s reports facing %d, not the authored %d — every direction word in the "
					.. "capture notes for this hull is wrong", h.key, actual, h.facing)
				ok = false
			end
		end
	end

	return ok
end

-- Measure and print each man's cell offset from his own hull. See the header for why this is a
-- cross-check on the image rather than the answer.
local function MeasureAndPrint()
	local crew = LiveCrew()
	print(string.format("[pinwheel] live crew=%d of %d", #crew, #Hulls * CrewPerHull))

	if #crew ~= #Hulls * CrewPerHull then
		Note("%d crew are in the world, expected %d — at least one hull's triad is incomplete and "
			.. "its gap in the frame may be a missing man rather than an exit arc",
			#crew, #Hulls * CrewPerHull)
	end

	local counts = {}
	for _, h in ipairs(Hulls) do counts[h.key] = 0 end

	for _, c in ipairs(crew) do
		local loc = c.Location
		local best, bestDist, secondDist = nil, 9999, 9999

		for _, h in ipairs(Hulls) do
			if h.actor ~= nil and not h.actor.IsDead then
				local hl = h.actor.Location
				local d = TestHarness.CellDrift(hl.X, hl.Y, loc.X, loc.Y)
				if d < bestDist then
					secondDist = bestDist
					best, bestDist = h, d
				elseif d < secondDist then
					secondDist = d
				end
			end
		end

		if best == nil then
			Note("a crew member at %d,%d could not be attributed to any hull", loc.X, loc.Y)
		elseif bestDist > OwnRadius or secondDist < ForeignRadius then
			Note("crew member at %d,%d is %d cells from hull %s and %d from the next — outside the "
				.. "%d/%d attribution margin, so the offsets printed for these hulls cannot be trusted",
				loc.X, loc.Y, bestDist, best.key, secondDist, OwnRadius, ForeignRadius)
		else
			counts[best.key] = counts[best.key] + 1
			local hl = best.actor.Location
			print(string.format("[pinwheel]   %-26s hull %-5s offset %+d,%+d (dx east+, dy south+)",
				c.Type, best.key, loc.X - hl.X, loc.Y - hl.Y))
		end

		if not c.IsIdle then
			Note("a crew member at %d,%d is still moving at the shutter — he is photographed "
				.. "mid-leg, not on the cell the fan chose", loc.X, loc.Y)
		end
	end

	for _, h in ipairs(Hulls) do
		if counts[h.key] ~= CrewPerHull then
			Note("hull %s has %d attributed crew, not %d", h.key, counts[h.key], CrewPerHull)
		end
	end
end

local function SetCamera(x, y, zoom, what)
	Camera.Position = WPos.New(x * 1024, y * 1024, 0)
	local applied = Test.SetZoom(zoom)
	print(string.format("[pinwheel] camera %s -> cell %d,%d zoom requested %.2f applied %.2f",
		what, x, y, zoom, applied))
	if math.abs(applied - zoom) > 0.001 then
		Note("zoom for %s clamped to %.2f (asked %.2f) — the frame is a different scale than "
			.. "intended, which changes how much is in it but not what is where", what, applied, zoom)
	end
end

WorldLoaded = function()
	if not CheckHulls() then
		Test.Skip("SETUP FAULT, no capture worth reading: " .. table.concat(setupNotes, "; "))
		return
	end

	SetCamera(WideCameraX, WideCameraY, WideZoom, "wide")
	UserInterface.SetMissionText(
		"CREW DISMOUNT PINWHEEL - hull noses: top-left UP, top-right RIGHT, bottom-left DOWN, bottom-right LEFT")

	Trigger.AfterDelay(ReferenceShotTick, function()
		TestHarness.Screenshot("01-hulls-undamaged-reference",
			"REFERENCE FRAME, read this one first. expects: FOUR intact Abrams on empty grass in a "
			.. "2x2 arrangement, and NO infantry anywhere in the picture. The four hulls point in "
			.. "four different directions: TOP-LEFT points UP the screen, TOP-RIGHT points RIGHT, "
			.. "BOTTOM-LEFT points DOWN, BOTTOM-RIGHT points LEFT. Nothing is burning yet. This "
			.. "frame exists so the four facings are fixed from clean hulls before any smoke, and "
			.. "every later frame is read against it by POSITION in the 2x2, not by re-reading the "
			.. "sprite. If any hull points somewhere other than the four listed, stop — the rest of "
			.. "the captures answer nothing.")
	end)

	-- Damage every hull to ~40% HP: past EjectionDamageState (Heavy = HP < 50%) so all three crew
	-- bail. Same value as test-crew-rear-dismount, deliberately, so the graded scenario and this
	-- capture are of the same staged world. With ChangesHealth@CriticalDamage removed in rules.yaml
	-- the hulls hold at this HP instead of bleeding out and cooking the crew off.
	Trigger.AfterDelay(DamageTick, function()
		for _, h in ipairs(Hulls) do
			h.actor.Health = math.floor(h.actor.MaxHealth * 4 / 10)
		end

		print("[pinwheel] all four hulls dropped to ~40% HP; crew bail from here")
	end)

	Trigger.AfterDelay(SettleTick, function()
		MeasureAndPrint()
		SetCamera(WideCameraX, WideCameraY, WideZoom, "wide")
		UserInterface.SetMissionText(
			"READ THE GAP IN EACH TRIAD - it must point the same way that hull's nose pointed in frame 01")
	end)

	Trigger.AfterDelay(SettleTick + Gap, function()
		TestHarness.Screenshot("02-pinwheel-wide",
			"THE RESULT FRAME. Same four hulls as 01, now smoking, each with THREE small blue "
			.. "infantry standing 2-3 cells out. READ THE GAP, NOT THE MEN. Around each hull the "
			.. "three men occupy three of the four cardinal directions and leave exactly ONE EMPTY, "
			.. "and the empty one must match that hull's nose from frame 01: TOP-LEFT empty ABOVE, "
			.. "TOP-RIGHT empty to the RIGHT, BOTTOM-LEFT empty BELOW, BOTTOM-RIGHT empty to the "
			.. "LEFT. Four hulls, four different empty sides, each agreeing with its own nose. "
			.. "WHAT THE FAILURES LOOK LIKE: (a) all four triads IDENTICAL in shape = the exit "
			.. "bearing is not reading hull facing at all; (b) every empty side OPPOSITE the nose = "
			.. "the fan is inverted and the crew are walking out of the front armour; (c) men "
			.. "scattered at no consistent angle, or two men stacked on one cell = the pre-3ce18d71 "
			.. "uniform random compass roll. Do not expect the three men to be bunched behind the "
			.. "hull in an arc: only ONE is astern, the other two are exactly abeam. That T shape is "
			.. "correct and is explained in the per-hull frames. THE MEN ARE LYING DOWN — idle "
			.. "infantry go prone in this mod by design (infantry.yaml:316), so twelve prone "
			.. "soldiers is the healthy state here, not twelve casualties.")
	end)

	for i, h in ipairs(Hulls) do
		local moveAt = SettleTick + (2 * i) * Gap
		local shotAt = SettleTick + (2 * i + 1) * Gap

		Trigger.AfterDelay(moveAt, function()
			if h.actor == nil or h.actor.IsDead then
				Note("hull %s died before its close-up", h.key)
				return
			end

			SetCamera(h.actor.Location.X, h.actor.Location.Y, HullZoom, "hull " .. h.key)
			UserInterface.SetMissionText(string.format(
				"HULL %s - expect %s", h.label, h.expect))
		end)

		Trigger.AfterDelay(shotAt, function()
			TestHarness.Screenshot(string.format("%02d-hull-%s", 2 + i, h.key),
				string.format(
					"CLOSE-UP, one hull filling the frame. This Abrams' nose points %s. expects: %s. "
					.. "THE SHAPE IS A T, NOT AN ARC — the three-man fan is {astern, astern+90, "
					.. "astern-90}, so exactly one man is straight out the back and the other two are "
					.. "square abeam, level with the hull. Three men bunched behind it would be the "
					.. "WRONG shape for this code. Each man is 2-3 cells out; the distance is the only "
					.. "randomised quantity, so the three legs may differ in length and that is "
					.. "expected. All three men are PRONE — idle infantry lie down in this mod by "
					.. "design (infantry.yaml:316) — so look for three small prone blue figures, not "
					.. "three standing ones, and do not read lying down as hurt. Ignore the smoke "
					.. "plume on the hull cell itself. The on-screen mission text names the hull and "
					.. "the expectation for cross-checking.",
					h.nose, h.expect))
		end)
	end

	Trigger.AfterDelay(SettleTick + (2 * #Hulls + 2) * Gap, function()
		if #setupNotes > 0 then
			Test.Skip("captures taken, but SETUP IS SUSPECT: " .. table.concat(setupNotes, "; "))
		else
			Test.Skip("captures taken; four hulls hold their authored facings, twelve crew are out, "
				.. "idle, and each attributable to one hull. Frames 01/02 are the wide pair, 03-06 the "
				.. "per-hull close-ups. NOTHING HERE IS GRADED — the geometry guard is "
				.. "test-crew-rear-dismount; this run answers only whether it is visible.")
		end
	end)
end
