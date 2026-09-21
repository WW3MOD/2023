-- DEMO: what GARRISON_PANEL shows once it can report a full shelter and a narrow garrison.
--
-- Audit item #8 (WORKSPACE/audit/260915-civ-garrison-audit.md §5a) plus the dead space below the
-- port rows. No verdict: this is a demo and nothing here asserts. It exists so the two panel states
-- can be LOOKED at, because neither is arithmetic anybody should be trusted to eyeball off a diff.
--
-- TWO FRAMES, and they are deliberately independent so one failing to stage does not hide the other:
--
--   garrison-panel-shelter-overflow — v09 with all ten men in the SHELTER and no port manned.
--     WHAT TO LOOK FOR: rows 1-3 name three riflemen; the FOURTH row reads
--     "[S] +7 more (10/10 in shelter)". Before the fix that row named a fourth man and the panel
--     silently reported 4 of 10. All eight port rows read "(empty)", which is correct here — there
--     is no enemy within reach of this house, so nothing deploys.
--
--   garrison-panel-narrow-gap — a PBOX, two firing ports and four shelter slots, holding four men.
--     WHAT TO LOOK FOR: the shelter rows sit DIRECTLY under the second port row. Before the fix
--     they kept the eight-port Y, so this panel drew two port rows, roughly six rows of empty space,
--     and then the shelter. With four men in four slots no summary row appears — which is the point
--     of using the PBOX for this frame rather than the house: it isolates the re-seating from the
--     overflow row.
--
-- The panel is owner-only (GarrisonPanelLogic filters the selection to world.LocalPlayer), so both
-- buildings are USA-owned by the time they are selected — the house by DynamicOwnership claiming a
-- neutral building on entry, the pillbox from the start.

local BoardWithin = 40      -- s for riflemen to walk one cell and board
local SettleTicks = 25      -- one second of quiet before a capture, so the panel has refreshed

local HouseMen = nil
local BoxMen = nil

local function InsideCount(men, building)
	local n = 0
	for _, m in ipairs(men) do
		if Test.IsLoadedInto(m, building) or Test.IsAtGarrisonPort(m, building) then
			n = n + 1
		end
	end

	return n
end

local function WaitUntil(seconds, predicate, onReady)
	local remaining = math.floor(seconds * TestHarness.TicksPerSecond)
	local check
	check = function()
		if predicate() then
			onReady()
			return
		end

		remaining = remaining - 1
		if remaining <= 0 then
			-- A demo does not fail. Capture whatever did stage and let the frame show it; a panel
			-- with six men in it still demonstrates the row, and a note beats a silent black screen.
			print("DEMO: timed out waiting to stage a frame — capturing the state as it is.")
			onReady()
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

local function CaptureNarrowGap()
	TestHarness.Select(Box)

	Trigger.AfterDelay(SettleTicks, function()
		TestHarness.Screenshot("garrison-panel-narrow-gap",
			"PBOX: 2 firing ports, 4 shelter slots, " .. InsideCount(BoxMen, Box) .. " men inside. " ..
			"The shelter rows should sit directly under PORT_LABEL_1 — not at the 8-port position " ..
			"with six rows of dead space above them. No summary row at 4-of-4.")
	end)
end

local function CaptureShelterOverflow()
	TestHarness.Select(House)

	Trigger.AfterDelay(SettleTicks, function()
		TestHarness.Screenshot("garrison-panel-shelter-overflow",
			"v09: MaxWeight 10, " .. InsideCount(HouseMen, House) .. " men inside, none at a port. " ..
			"RESERVE_LABEL_3 should read '[S] +7 more (10/10 in shelter)' rather than naming a " ..
			"fourth rifleman — the panel used to report 4 of 10 with nothing saying there were more.")

		-- Second frame after the first has been taken, so the two selections never overlap.
		Trigger.AfterDelay(SettleTicks, CaptureNarrowGap)
	end)
end

WorldLoaded = function()
	HouseMen = { Man0, Man1, Man2, Man3, Man4, Man5, Man6, Man7, Man8, Man9 }
	BoxMen = { BoxMan0, BoxMan1, BoxMan2, BoxMan3 }

	TestHarness.FocusBetween(House, Man0)

	-- Through the real targeter, so the demo cannot stage an entry the game would refuse.
	for _, m in ipairs(HouseMen) do
		Test.ClickOrder(m, House)
	end

	for _, m in ipairs(BoxMen) do
		Test.ClickOrder(m, Box)
	end

	WaitUntil(BoardWithin,
		function()
			return InsideCount(HouseMen, House) >= 10 and InsideCount(BoxMen, Box) >= 4
		end,
		CaptureShelterOverflow)
end
