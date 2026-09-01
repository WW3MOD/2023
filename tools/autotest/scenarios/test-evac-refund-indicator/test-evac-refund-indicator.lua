-- CAPTURE INSTRUMENT: the evacuation refund tick, "+$N" and "+$0", in a single frame.
--
-- =====================================================================================
-- THIS GRADES NOTHING. IT IS NOT A GUARD. The terminal verdict is Test.Skip.
-- =====================================================================================
-- Every runtime check below is a guard on the INSTRUMENT -- did the premise hold, was
-- the frame taken at a moment worth looking at -- and none of them is a pass/fail
-- statement about the mod. The only evidence about the indicator is the PNG, and a
-- human reads it. A Test.Pass would contribute a green to every run-batch.sh --all
-- tally whether or not the text ever rendered, which is the exact mislabelling
-- corrected across this suite on 2026-09-01.
--
-- WHAT IT IS FOR. adfb0f2f clamped the refund tick's position into Map.Bounds and gave
-- it FloatingText's ignoreVisibility flag, because the indicator was suppressed for
-- every evacuation that SUCCEEDED: a completed evacuation ends out of bounds by
-- construction (RotateToEdge drags a ground unit GroundOffMapCells = 2 past the
-- boundary before selling) and both of FloatingText's gates resolve position through
-- MapLayers, which reports anything out of bounds as hidden -- IsExplored returns false
-- when !map.Contains (MapLayers.cs:504-505) and IsVisible returns map.Contains outright
-- with fog off (:576-577). The rise was also lengthened from 30 ticks to 75, i.e. 1.8s
-- to 4.5s at Timestep 60. None of that has ever been looked at in a running game.
--
-- =====================================================================================
-- THE RUN IS WORTHLESS WITHOUT Test.KeepRenderPlayer=true, AND NOT FOR THE USUAL REASON
-- =====================================================================================
--     AUTOTEST_EXTRA_ARGS="Test.KeepRenderPlayer=true" \
--       ./tools/autotest/run-test.sh --size 1600x900 test-evac-refund-indicator
--
-- The usual reason a scenario needs the flag is that a null RenderPlayer makes fog
-- disappear so the frame stops being a picture of what a player sees. That applies here
-- too, but it is the SECOND reason and the weaker one. The first is that a null
-- RenderPlayer makes half the fix UNFALSIFIABLE:
--
--   * FloatingText's own gate is `!ignoreVisibility && (FogObscures || ShroudObscures)`
--     (FloatingText.cs:66), and World.FogObscures/ShroudObscures short-circuit to false
--     the moment RenderPlayer is null (World.cs:109-115). So with the flag missing, the
--     text draws whether or not ignoreVisibility is passed at all -- and the visibility
--     bypass, which is one of the two things adfb0f2f changed, cannot be observed to be
--     working or to have regressed.
--   * DoSell's own `self.Owner.IsAlliedWith(self.World.RenderPlayer)` gate is likewise
--     vacuous: Player.RelationshipWith returns Ally for a null argument outright
--     (Player.cs:254-255), so the gate opens for anyone.
--
-- A run without the flag therefore photographs a code path that CANNOT FAIL, and comes
-- back with a perfectly plausible picture of two refund ticks. That is the one failure
-- mode here that would be read as evidence.
--
-- SO THE RUN CHECKS FOR ITSELF, and refuses to hand up a verdict when the flag is
-- missing. FarBox is a Russia pillbox at 60,30 with all ten Vision layers stripped,
-- ~55 cells from either subject and past ^StandardVision's 32-cell outermost rung, on a
-- map with Explored OFF -- so USA has never looked at it. Test.IsMouseTargetable asks
-- MouseTargetVisibility.IsRevealed, whose actorIsVisible term is !World.FogObscures(a):
-- false under a live RenderPlayer, true under a null one, and the whole expression is
-- ANDed with it. Targetable therefore means null RenderPlayer and nothing else. This is
-- the same probe test-unscouted-building-hidden already relies on.
--
-- =====================================================================================
-- WHAT THE FRAME CAN SEPARATE, WHICH IS THE POINT OF STAGING IT AT THE WEST EDGE
-- =====================================================================================
-- Bounds are 1,1,64,32 so Map.Bounds is Left=1 Right=65 (exclusive), and both subjects
-- exit WEST -- derived in map.yaml from Map.ChooseClosestMatchingEdgeCell over the
-- perimeter of Bounds, where 1,14 and 1,19 win by more than 2x over any other edge. The
-- camera is centred at cell 8 so that roughly ten cells of off-map black sit to the LEFT
-- of column x=1 in frame. That gives three visibly different outcomes rather than two:
--
--   * text on column x=1, just inside the boundary  -> the clamp works. This is the fix.
--   * text ~2 cells out in the black void, x = -1   -> the clamp is gone but the
--                                                      visibility bypass is holding.
--   * no text at all                                -> suppressed, i.e. the original bug
--                                                      (or the sale never happened, which
--                                                      the verdict below rules out
--                                                      separately by reading the cash).
--
-- "Absent" and "drawn where nobody can see it" are different failures with different
-- fixes, and a capture that could not tell them apart would be worth very little.
--
-- AND THE VERDICT SEPARATES "no text" FROM "no sale". The run records the USA cash delta
-- at the tick each subject is observed dead. If the picture shows nothing but the
-- verdict says the engine credited money, the sale happened and the INDICATOR is what
-- is broken. If the verdict shows no credit either, nothing evacuated and the frame is
-- not about the indicator at all.
--
-- =====================================================================================
-- READ THE FULL-RESOLUTION PNG. DO NOT DOWNSCALE IT.
-- =====================================================================================
-- SCREENSHOT.md's standing advice is to shrink a capture to ~1280px before Read, and for
-- this one that advice is wrong: the payload is four to six glyphs of the TinyBold font,
-- and "+$0" versus "+$1000" versus nothing is exactly the small-text discrimination that
-- survives least well through a resize. Zoom is pinned at 2.0 to help, and the applied
-- value is logged rather than assumed.

local PayerRow = 14
local PauperRow = 19
local StartX = 6

-- Map.Bounds as Rectangle: Left/Top inclusive, Right/Bottom EXCLUSIVE. Mirrored here
-- only so the run can PRINT the cell it expects the text on; the engine's own clamp is
-- EvacRefundTextMath.ClampToBounds and nothing below feeds back into it. Legitimate to
-- do in CPos space because this is a rectangular TEMPERAT map, where the MPos the engine
-- clamps in and the CPos read here are the same numbers.
local BoundsLeft, BoundsTop = 1, 1
local BoundsRight, BoundsBottom = 65, 33

local FarBoxCell = { X = 60, Y = 30 }

-- Centre far enough west that column x=1 sits well inside the frame with about ten cells
-- of off-map black beyond it -- the region where an UNCLAMPED tick would land.
local CameraCellX = 8
local CameraCellY = 16
local TargetZoom = 2.0

local PremiseTicks = 15

-- SELECT AND PRESS ARE ON DIFFERENT TICKS, DELIBERATELY. The command bar caches its
-- evacuateDisabled state against a selection hash, so a press issued in the same closure
-- as the selection can read the PREVIOUS selection's state and be rejected. Split by
-- three ticks here; test-evac-queued-after-waypoints carries the same split and calls it
-- load-bearing.
local SelectPayerTick = 25
local PressPayerTick = 28

-- Twenty ticks after Payer, not simultaneously. Two reasons, both load-bearing: cash
-- deltas read per-tick would merge into one unattributable number if both sales landed
-- on the same tick, and two texts spawned together would be indistinguishable in the
-- frame if either one failed to draw. Twenty ticks is also comfortably inside
-- EvacRefundTextMath.TickLifetime = 75, so the older text is still alive at the shot.
local SelectPauperTick = 45
local PressPauperTick = 48

local DeadlineTicks = 600

-- The capture is ONE FRAME LATE -- Test.Screenshot arms a capture and the pixels are
-- sampled at the end of the NEXT RenderTick -- so it gets its own delay after the last
-- sale, and the verdict gets another after it. Fifteen ticks also lets the newer text
-- rise clear of the ground clutter without letting the older one (35 ticks old by then,
-- ~1.2 cells up) climb out of frame.
local ShotDelayTicks = 15
local VerdictDelayTicks = 25

local usa
local tick = 0
local prevCash = 0
local setupNotes = {}

local payerLastCell, pauperLastCell
local payerRec, pauperRec

local function Clamp(v, lo, hi)
	if v < lo then return lo end
	if v > hi then return hi end
	return v
end

-- Where EvacRefundTextMath.ClampToBounds would put a tick spawned at `cell`. Right and
-- Bottom are exclusive, so the last legal cell is one less than each.
local function PredictTextCell(cell)
	if cell == nil then return nil end
	return {
		X = Clamp(cell.X, BoundsLeft, BoundsRight - 1),
		Y = Clamp(cell.Y, BoundsTop, BoundsBottom - 1),
	}
end

local function CellStr(cell)
	if cell == nil then return "(unknown)" end
	return cell.X .. "," .. cell.Y
end

-- Test.IsMouseTargetable and Test.GetVisibility together decide whether this run is
-- allowed to produce a verdict at all. Returns a reason string when the premise is
-- broken, or nil when it holds.
local function PremiseFault()
	if FarBox == nil or FarBox.IsDead then
		return "map actor FarBox did not resolve (or is dead), so the run cannot tell a live "
			.. "RenderPlayer from a null one and the frame cannot be trusted"
	end

	local vis = Test.GetVisibility(usa, CPos.New(FarBoxCell.X, FarBoxCell.Y))
	if vis ~= 0 then
		return "FarBox's cell reads visibility " .. vis .. ", not 0 -- USA can see it, so its "
			.. "mouse-targetability no longer isolates World.RenderPlayer and the "
			.. "KeepRenderPlayer probe below is void. Something on this map is emitting vision "
			.. "toward 60,30, or ExploredMapCheckboxEnabled did not stay false"
	end

	if Test.IsMouseTargetable(FarBox) then
		return "FarBox is mouse-targetable from never-scouted ground at visibility 0, which "
			.. "means World.RenderPlayer is NULL -- the run was launched WITHOUT "
			.. "AUTOTEST_EXTRA_ARGS=\"Test.KeepRenderPlayer=true\". FloatingText's visibility "
			.. "gate and DoSell's IsAlliedWith(RenderPlayer) gate are both vacuous in that "
			.. "state, so any refund tick in the resulting frame proves nothing. Re-run with "
			.. "the flag"
	end

	return nil
end

local function TakeShot()
	TestHarness.Screenshot("evac-refund-indicator",
		"expects: TWO floating refund ticks near the WEST map boundary, one on row 14 "
		.. "(Payer, a positive amount) and one five cells below on row 19 (Pauper, which "
		.. "MUST read \"+$0\" and must not be missing). Both should sit just INSIDE the "
		.. "boundary on column x=1, i.e. at the very left edge of the lit map, NOT out in "
		.. "the black void to the left of it -- a tick in the void is an unclamped position "
		.. "and a tick that is absent is the original suppression bug. The row-14 text is "
		.. "~20 ticks older so it will have drifted slightly higher; that is the rise rate "
		.. "working, not a fault. Most of the map should be BLACK unexplored shroud with a "
		.. "lit corridor along each subject's westward path -- a uniformly lit map means "
		.. "RenderPlayer was null and the capture is void. Read this PNG at full "
		.. "resolution; do not downscale it, the payload is a few glyphs of small text.")
end

local function Verdict()
	local parts = {}

	parts[#parts + 1] = "Payer: " .. (payerRec ~= nil
		and ("sold at tick " .. payerRec.tick .. ", USA cash +" .. payerRec.delta
			.. ", last in-world cell " .. CellStr(payerLastCell)
			.. ", text predicted at " .. CellStr(PredictTextCell(payerLastCell)))
		or "NEVER SOLD")

	parts[#parts + 1] = "Pauper: " .. (pauperRec ~= nil
		and ("sold at tick " .. pauperRec.tick .. ", USA cash +" .. pauperRec.delta
			.. ", last in-world cell " .. CellStr(pauperLastCell)
			.. ", text predicted at " .. CellStr(PredictTextCell(pauperLastCell)))
		or "NEVER SOLD")

	if payerRec ~= nil and pauperRec ~= nil then
		local gap = pauperRec.tick - payerRec.tick
		if gap < 0 then gap = -gap end
		-- The cash delta is read against the previous poll tick, so two sales landing on ONE
		-- tick produce a single combined figure that then gets attributed to both subjects.
		-- The 20-tick stagger exists to prevent this; if it happened anyway, neither per-unit
		-- number above means anything and the two texts will be at the same height in frame.
		if gap == 0 then
			setupNotes[#setupNotes + 1] = "both sales were observed on the SAME poll tick, so the "
				.. "two cash deltas reported here are one combined credit counted twice and "
				.. "neither per-unit figure can be trusted"
		elseif gap >= 75 then
			setupNotes[#setupNotes + 1] = "the two sales were " .. gap .. " ticks apart, which is "
				.. "at or past EvacRefundTextMath.TickLifetime (75) -- the FIRST text had already "
				.. "expired when the shot was armed, so its absence in the frame is the "
				.. "instrument's fault and NOT evidence about the indicator"
		elseif gap >= 45 then
			setupNotes[#setupNotes + 1] = "the two sales were " .. gap .. " ticks apart, so by the "
				.. "shot the older text had risen roughly " .. string.format("%.1f", (gap + ShotDelayTicks) * 34 / 1024)
				.. " cells and may be well above its boundary cell"
		end
	end

	if pauperRec ~= nil and pauperRec.delta ~= 0 then
		setupNotes[#setupNotes + 1] = "Pauper's refund was " .. pauperRec.delta .. ", not 0 -- the "
			.. "Payload: 0 override in rules.yaml did not take, so the zero-refund arm is not "
			.. "actually testing a zero and a \"+$0\" is not what should be in the frame"
	end

	if payerRec ~= nil and payerRec.delta <= 0 then
		setupNotes[#setupNotes + 1] = "Payer's refund was " .. payerRec.delta .. ", so the frame has "
			.. "no non-zero arm to compare the zero against"
	end

	local body = table.concat(parts, "; ")
	if #setupNotes > 0 then
		Test.Skip("capture taken, but SETUP IS SUSPECT: " .. table.concat(setupNotes, "; ")
			.. ". Readings were -- " .. body)
	else
		Test.Skip("capture taken; both subjects evacuated and the engine credited the refunds "
			.. "recorded here, so anything missing from the PNG is the INDICATOR and not the sale. "
			.. body)
	end
end

local function Poll()
	tick = tick + 1

	if not Payer.IsDead then
		payerLastCell = { X = Payer.Location.X, Y = Payer.Location.Y }
	elseif payerRec == nil then
		payerRec = { tick = tick, delta = usa.Cash - prevCash }
	end

	if not Pauper.IsDead then
		pauperLastCell = { X = Pauper.Location.X, Y = Pauper.Location.Y }
	elseif pauperRec == nil then
		pauperRec = { tick = tick, delta = usa.Cash - prevCash }
	end

	prevCash = usa.Cash

	if payerRec ~= nil and pauperRec ~= nil then
		Trigger.AfterDelay(ShotDelayTicks, TakeShot)
		Trigger.AfterDelay(ShotDelayTicks + VerdictDelayTicks, Verdict)
		return
	end

	if tick >= DeadlineTicks then
		local stuck = {}
		if not Payer.IsDead then
			stuck[#stuck + 1] = "Payer at " .. CellStr(payerLastCell)
				.. " running " .. Test.ActivityChain(Payer)
		end
		if not Pauper.IsDead then
			stuck[#stuck + 1] = "Pauper at " .. CellStr(pauperLastCell)
				.. " running " .. Test.ActivityChain(Pauper)
		end

		Test.Skip("NO CAPTURE: a subject never evacuated within " .. DeadlineTicks
			.. " ticks, so there was never a refund tick to photograph. " .. table.concat(stuck, "; ")
			.. ". An activity chain naming RotateToEdge means the order was accepted and the drive "
			.. "stalled; anything else means the Evacuate hotkey never reached DeliversCash")
		return
	end

	Trigger.AfterDelay(1, Poll)
end

WorldLoaded = function()
	usa = Player.GetPlayer("USA")
	if usa == nil then
		Test.Skip("SETUP FAULT: could not resolve the USA player")
		return
	end

	if Payer == nil or Pauper == nil then
		Test.Skip("SETUP FAULT: map actors Payer and/or Pauper did not resolve, so there is "
			.. "nothing to evacuate")
		return
	end

	Camera.Position = WPos.New(CameraCellX * 1024, CameraCellY * 1024, 0)

	-- SetZoom returns what was actually APPLIED, which is not necessarily what was asked
	-- for. The reader needs the applied value to convert cells to pixels, so it is logged.
	local appliedZoom = Test.SetZoom(TargetZoom)
	print(string.format("[evac-refund] camera at cell %d,%d; zoom requested %.2f applied %.2f",
		CameraCellX, CameraCellY, TargetZoom, appliedZoom))

	if math.abs(appliedZoom - TargetZoom) > 0.001 then
		setupNotes[#setupNotes + 1] = string.format(
			"zoom was clamped to %.2f (asked %.2f), so one cell is %.1f px, not 48, and the "
			.. "pixel geometry in description.txt does not apply",
			appliedZoom, TargetZoom, 24 * appliedZoom)
	end

	UserInterface.SetMissionText(
		"EVAC REFUND: two tanks leave the west edge 20 ticks apart. Both must leave a "
		.. "floating refund tick just inside the boundary; the lower one must read +$0.")

	prevCash = usa.Cash
	print("[evac-refund] USA cash at start: " .. prevCash)

	-- Vision resolves on the MapLayers tick, so the probe is not read in WorldLoaded --
	-- that would report the pre-tick state and could accuse a correctly-configured run.
	Trigger.AfterDelay(PremiseTicks, function()
		local fault = PremiseFault()
		if fault ~= nil then
			Test.Skip("NO CAPTURE, PREMISE BROKEN: " .. fault)
			return
		end

		print("[evac-refund] premise OK: FarBox at 60,30 is at visibility 0 and is NOT "
			.. "mouse-targetable, so World.RenderPlayer is live")

		Trigger.AfterDelay(SelectPayerTick - PremiseTicks, function()
			Test.SelectActors({ Payer })
		end)

		Trigger.AfterDelay(PressPayerTick - PremiseTicks, function()
			local consumed = Test.PressHotkey("Evacuate", false)
			print("[evac-refund] Payer E consumed=" .. tostring(consumed)
				.. " selection=" .. Test.GetSelectedCount())
			if not consumed then
				setupNotes[#setupNotes + 1] = "the Evacuate hotkey was not consumed for Payer, so "
					.. "its evacuation was never ordered"
			end
		end)

		Trigger.AfterDelay(SelectPauperTick - PremiseTicks, function()
			Test.SelectActors({ Pauper })
		end)

		Trigger.AfterDelay(PressPauperTick - PremiseTicks, function()
			local consumed = Test.PressHotkey("Evacuate", false)
			print("[evac-refund] Pauper E consumed=" .. tostring(consumed)
				.. " selection=" .. Test.GetSelectedCount())
			if not consumed then
				setupNotes[#setupNotes + 1] = "the Evacuate hotkey was not consumed for Pauper, so "
					.. "its evacuation was never ordered"
			end
		end)

		Trigger.AfterDelay(PressPayerTick - PremiseTicks, Poll)
	end)
end
