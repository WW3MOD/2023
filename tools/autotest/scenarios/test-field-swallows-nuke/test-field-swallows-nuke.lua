-- AUTO TEST (screenshot): a nuclear scar must cross a crop field.
--
-- WHAT THE VERDICT IS AND IS NOT. This scenario grades ONLY that it ran to its captures with its
-- witnesses alive and the field bed under the burst. The picture is the product, and the judgement
-- on it is the manager's -- the three PASS criteria and the three FAIL modes are written out
-- verbatim in README.md. A Test.Pass here means "the frames are worth looking at", never "the
-- feature works".
--
-- WHY IT IS A test- AND NOT A demo-. A demo stages and stops with no verdict (DEMO.md). This one has
-- real setup controls that can fail -- the field bed, the witnesses' survival, the detonation
-- actually having happened -- and those must red a batch rather than be discovered by a human
-- noticing a blank screenshot three days later.
--
-- TICKS ARE RAW. Timestep is 60 ms, so 16.67 ticks per second -- NOT the 25 that
-- TestHarness.TicksPerSecond carries (a harness convention for AssertWithin budgets, documented in
-- test-helpers.lua, and not the tick rate). Every schedule constant below is in ticks.
--
-- THE CAPTURE TIMES ARE NOT THE ONES THE BRIEF ASKED FOR, and the arithmetic is why. The brief said
-- "~10 ticks after detonation". At detonation+10 the ONLY scar on the ground is Warhead@Scar1Core,
-- a two-cell disc (Delay 0); ScarCrater is Delay 13, ScarChar 33, ScarBurn 54 and ScarRim 68, so the
-- disc is not finished until +68. Worse, Warhead@Flash is a FlashPaletteEffect with Duration 30, so
-- a frame at +10 is a WHITE SCREEN with a dot in it. The first capture is therefore at +140: past
-- the last band, past the flash, past the screen shake (Warhead@Shake3 runs to +142 at the
-- epicentre but shake moves the viewport, it does not wash it), and smudges are permanent so there
-- is no upper bound to worry about.
--
-- MAP.ActorsInCircle RETURNS NOTHING WHEN CALLED FROM WorldLoaded. It resolves to ActorMap's
-- POSITION BINS (ActorMap.cs:649) and map-placed actors only enter those bins via
-- ActorMap.TickFunction (:478), which runs from ITick -- i.e. the first world tick, AFTER
-- WorldLoaded returns. Correct code, correct arguments, empty answer, no error. Every query below is
-- therefore made from inside the polling predicate, behind a grace window. Same trap, same remedy,
-- as test-field-swallows-shell and test-field-crate-drop.

local TicksPerSecond = TestHarness.TicksPerSecond

local GZ = CPos.New(33, 16)             -- centre of the 11x11 patch, and the probe's own cell
local SEAM = CPos.New(38, 16)           -- last field cell on the row; 39 and 40 are bare, same band
local WITNESS_VIEW = CPos.New(39, 15)   -- between the two humvees

local SETUP_FROM = 20                   -- position bins are filled well before this
local SETUP_DEADLINE = 50
local GRANT_TICK = 60
local DETONATE_TICK = 70                -- conditions are applied in Actor.Tick's update pass, so
                                        -- granting and killing on the same tick races the enable
local CONFIRM_TICK = DETONATE_TICK + 20

local SHOT1_TICK = DETONATE_TICK + 140  -- whole disc, everything settled
local SHOT2_TICK = DETONATE_TICK + 180  -- the field/bare seam
local SHOT3_TICK = DETONATE_TICK + 220  -- the two witnesses
local FRAME_LEAD = 6                    -- camera is moved this many ticks before each capture
local DONE_TICK = SHOT3_TICK + 30       -- captures are flushed before Test.Pass exits

-- A 5-cell disc around ground zero is 81 cells, every one of them field. The floor sits well under
-- that while still catching a patch that stopped spawning.
local FIELD_PROBE_RADIUS = WDist.FromCells(5)
local MIN_FIELDS_UNDER_BURST = 60

local DEADLINE_TICKS = 600
local DEADLINE_SECONDS = DEADLINE_TICKS / TicksPerSecond

WorldLoaded = function()
	local ticks = 0
	local granted = false
	local killed = false
	local fieldsUnderBurst = 0
	local effects0 = 0
	local shots = {}

	local isField = function(a) return a.Type == "v14" end

	local function Look(c, zoom)
		Camera.Position = WPos.New(c.X * 1024 + 512, c.Y * 1024 + 512, 0)
		if zoom ~= nil then
			Camera.Zoom = zoom
		end
	end

	local function FieldsAt(c)
		-- A quarter-cell radius, so only a field actor whose centre IS this cell qualifies.
		-- ActorsInCircle keeps actors whose CENTRE is within r (WorldUtils.cs:83-84).
		return #Map.ActorsInCircle(Map.CenterOfCell(c), WDist.New(256), isField)
	end

	local function Census()
		return string.format(
			"t=%d | fields under burst %d | probe %s | witnessField %d,%d hp %s | "
			.. "witnessBare %d,%d hp %s | impacts %d->%d | shots %d",
			ticks, fieldsUnderBurst, Probe.IsDead and "dead(detonated)" or "alive",
			WitnessField.Location.X, WitnessField.Location.Y,
			WitnessField.IsDead and "DEAD" or (WitnessField.Health .. "/" .. WitnessField.MaxHealth),
			WitnessBare.Location.X, WitnessBare.Location.Y,
			WitnessBare.IsDead and "DEAD" or (WitnessBare.Health .. "/" .. WitnessBare.MaxHealth),
			effects0, Test.GetImpactEffectCount(), #shots)
	end

	Look(GZ)

	TestHarness.AssertWithin(DEADLINE_SECONDS, function()
		ticks = ticks + 1

		-- ===== SETUP CONTROLS. Without these a run in which the patch never spawned looks exactly
		-- ===== like a run in which it did: three screenshots of a scar on bare ground, and a green.
		if ticks == SETUP_FROM then
			fieldsUnderBurst = #Map.ActorsInCircle(Map.CenterOfCell(GZ), FIELD_PROBE_RADIUS, isField)
			effects0 = Test.GetImpactEffectCount()
		end

		if ticks == SETUP_DEADLINE then
			if fieldsUnderBurst < MIN_FIELDS_UNDER_BURST then
				return string.format(
					"fail: SETUP -- only %d field actors within 5 cells of ground zero (need >=%d). "
					.. "The blast would land on bare ground and the captures would show nothing "
					.. "about GroundCoverOverlay at all. Fix map.yaml before trusting any frame. %s",
					fieldsUnderBurst, MIN_FIELDS_UNDER_BURST, Census())
			end

			-- The two witnesses are only a comparison if they are standing on what they are supposed
			-- to be standing on. WitnessField must share its cell with a crop field (the cell would
			-- otherwise have taken the overlay, and the vehicle is what must stop it); WitnessBare
			-- must not (it is the bare-ground reference).
			local onField = FieldsAt(WitnessField.Location)
			local onBare = FieldsAt(WitnessBare.Location)
			if onField ~= 1 or onBare ~= 0 then
				return string.format(
					"fail: SETUP -- WitnessField at %d,%d sits on %d field actor(s) (want 1) and "
					.. "WitnessBare at %d,%d sits on %d (want 0). The 'a scar never covers a unit' "
					.. "criterion needs a vehicle on a cell that WOULD have been overlaid, and the "
					.. "darkness comparison needs one that would not. %s",
					WitnessField.Location.X, WitnessField.Location.Y, onField,
					WitnessBare.Location.X, WitnessBare.Location.Y, onBare, Census())
			end

			if FieldsAt(SEAM) ~= 1 or FieldsAt(CPos.New(SEAM.X + 1, SEAM.Y)) ~= 0 then
				return "fail: SETUP -- the seam at " .. SEAM.X .. "," .. SEAM.Y .. " is not a "
					.. "field-to-bare boundary, so the second capture has nothing to compare. "
					.. Census()
			end

			print("[field-nuke] setup confirmed. " .. Census())
		end

		-- ===== DETONATION, on a tick this scenario chooses. =====
		if ticks == GRANT_TICK and not granted then
			granted = true
			if Probe.IsDead then
				return "fail: the probe died before it could be armed, so no detonation is coming. "
					.. Census()
			end

			Probe.GrantCondition("nuke-probe-tactical")
		end

		if ticks == DETONATE_TICK and not killed then
			killed = true
			if Probe.IsDead then
				return "fail: the probe died between arming and detonation. " .. Census()
			end

			Probe.Kill()
		end

		if ticks == CONFIRM_TICK then
			if not Probe.IsDead then
				return "fail: the probe is still alive twenty ticks after Kill(), so the Explodes "
					.. "payload never fired. " .. Census()
			end

			if Test.GetImpactEffectCount() <= effects0 then
				return "fail: the probe died but Test.GetImpactEffectCount did not move ("
					.. effects0 .. "), so no warhead impact passed the validity gates. The Atomic "
					.. "payload did not go off -- check that the ExternalCondition merged onto "
					.. "`bradley` and that the Explodes@TacticalNuke gate names the same condition. "
					.. Census()
			end

			print("[field-nuke] detonated. " .. Census())
		end

		-- ===== THE CAPTURES. Camera first, then the shot a few ticks later. =====
		if ticks == SHOT1_TICK - FRAME_LEAD then Look(GZ, Camera.MinZoom) end
		if ticks == SHOT1_TICK then
			shots[#shots + 1] = TestHarness.Screenshot("disc-wide",
				"expects: the whole scar disc, ~25 cells across, with the 11x11 crop patch centred "
				.. "in it. PASS = the disc is CONTINUOUS across the patch and the same darkness "
				.. "over field cells as over the bare scarred ground around them. FAIL = a bright "
				.. "green block inside the dark disc.")
		end

		if ticks == SHOT2_TICK - FRAME_LEAD then Look(SEAM, 2) end
		if ticks == SHOT2_TICK then
			shots[#shots + 1] = TestHarness.Screenshot("seam-closeup",
				"expects: the field/bare boundary on row y=16. x38 is the last field cell and x39 "
				.. "and x40 are bare; all three are in the SAME ScarChar band (buckets 5, 6, 7) and "
				.. "must read as the same darkness. FAIL = the field side visibly DARKER than the "
				.. "bare side (double composite at the fringe -- ^CivField has RenderSprites.Scale "
				.. "1.15, larger than its own cell), or visibly lighter.")
		end

		if ticks == SHOT3_TICK - FRAME_LEAD then Look(WITNESS_VIEW, 2) end
		if ticks == SHOT3_TICK then
			shots[#shots + 1] = TestHarness.Screenshot("witness-vehicles",
				"expects: two humvees, one standing ON a field cell inside the disc (36,14) and one "
				.. "on bare scarred ground (42,16). PASS = both vehicle sprites are CLEAN -- no "
				.. "scar pixel on top of either -- while the ground around both is scarred. FAIL = "
				.. "any scar drawn over a vehicle sprite.")
		end

		if ticks < DONE_TICK then
			return false
		end

		if WitnessField.IsDead or WitnessBare.IsDead then
			return "fail: a witness vehicle died. Their HP is raised in rules.yaml precisely so they "
				.. "cannot -- a husk is not Passable.GroundCover, so its cell is excluded from the "
				.. "overlay for a second, unrelated reason and the 'a scar never covers a unit' "
				.. "criterion stops being testable in these frames. " .. Census()
		end

		if #shots < 3 then
			return "fail: only " .. #shots .. " of 3 captures were taken. " .. Census()
		end

		print("[field-nuke] PASS -- three captures taken, judge them against README.md. " .. Census())
		return true
	end, function()
		return "fail: the predicate never reached a verdict inside its backstop deadline, which "
			.. "means it stopped advancing rather than that any stage overran. " .. Census()
	end)
end
