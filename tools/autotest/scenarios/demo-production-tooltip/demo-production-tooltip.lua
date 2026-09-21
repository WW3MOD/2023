-- DEMO: the production tooltip, on the opaque panel, over the sidebar it overlaps.
--
-- No verdict. Nothing here asserts and there is deliberately no Test.Pass / Test.Fail — a demo
-- stages a thing to look at and stops (DEMO.md).
--
-- WHAT IS BEING LOOKED AT. `Background@PRODUCTION_TOOLTIP` drew on `dialog4`, whose centre tile
-- is black at ALPHA 159 of 255. TooltipContainerWidget.GetAnchoredPosition (:154) places this
-- panel at `anchor.X - tooltipWidth - AnchorGap` — to the LEFT of the sidebar, clamped only
-- vertically — so a tall tooltip lands across the sidebar's icon column and the unit portraits
-- underneath were legible THROUGH it, behind the tooltip's own text (bugs/discovered.md
-- 2026-08-30). It now draws on `tooltip-panel`: the same 6px dialog4 frame with a fully opaque
-- interior. THE GEOMETRY IS UNCHANGED AND IS NOT THE FIX — the panel still overlaps, it just no
-- longer transmits. A frame where the tooltip has moved off the sidebar is photographing
-- something else.
--
-- WHY THREE SUBJECTS. The overlap is a function of tooltip HEIGHT, and height is a function of
-- how many rows the actor contributes, so one subject cannot show the range:
--   * abrams    — one ammo pool, every actor-wide stat row. The tall panel.
--   * e3        — TWO pools (5.56mm DMR + RPG), which is the exact tooltip the 2026-08-30 report
--                 was written against ("the tall rifleman tooltip lands across the sidebar's
--                 icon column"). This is the repro frame.
--   * e1        — one pool, the shortest infantry panel. The control: if the interior is opaque
--                 here but not on the two above, the bug is in the tiling, not the tile.
--
-- HOVER IS ARMED, NOT APPLIED, AND THE CAPTURE SAMPLES A FRAME LATER STILL. Test.HoverProductionIcon
-- switches the sidebar to the queue that offers the type and sets the request; the palette applies
-- it from its own Tick, because the icon rectangles it needs only exist after RefreshIcons has run
-- a layout pass — and switching the queue is exactly what invalidates them. Then Test.Screenshot
-- ARMS a capture whose pixels are read at the end of the NEXT RenderTick (SCREENSHOT.md). Both
-- delays below are for that, and they are why nothing else may touch the world in between.
--
-- A BLANK FRAME IS TOLD BY FILE SIZE, NOT BY THE IMAGE: ~59 KB is a black frame, a real one is
-- megabytes. Check the bytes before believing any capture here.

-- Ticks, not seconds. TestHarness.ScreenshotAfter takes seconds and is avoided on purpose: the
-- gap that matters here is "one layout pass plus one render", which is a tick count.
local HoverSettleTicks = 30

local subjects = {
	{ type = "abrams", label = "01-abrams",
	  note = "expects: production tooltip open to the LEFT of the sidebar, overlapping it. "
	      .. "The panel interior is SOLID — no sidebar cameo, portrait or row frame is visible "
	      .. "through it anywhere behind the text. Heading reads TANK ROUND ABRAMS (word-split, "
	      .. "not the raw key TankRound.Abrams), then AMMO 40 rounds, a REFILL rate, and a "
	      .. "FULL REFILL total in amber. NO-RESULT if the sidebar is empty (no Supply Route "
	      .. "=> no enabled queue), if no panel is drawn at all (a Background: naming a missing "
	      .. "collection draws NOTHING, silently), or if the tooltip does not overlap the "
	      .. "sidebar — then it is not photographing the reported defect." },
	{ type = "e3", label = "02-rifleman-two-pools",
	  note = "expects: the ORIGINAL repro. Two weapon sections — 5.56MM DMR and RPG — each with "
	      .. "its own AMMO and REFILL rows, a wider gap where the weapons band ends and ARMOUR / "
	      .. "HEALTH / SPEED begin, and one FULL REFILL total. This is the tallest infantry "
	      .. "panel and reaches furthest down the sidebar's icon column; the interior must be "
	      .. "solid for its whole height, including the bottom rows farthest from the anchor. "
	      .. "NO-RESULT if only one weapon section is present (the hover landed on the wrong "
	      .. "actor) or if the panel is shorter than 01's." },
	{ type = "e1", label = "03-conscript-single-pool",
	  note = "expects: the shortest panel — one section, 5.56MM E3, AMMO 100 rounds, REFILL, and "
	      .. "a FULL REFILL total that is present even though this unit has only ONE pool (that "
	      .. "row was once gated on having two). Interior solid as above. NO-RESULT if the FULL "
	      .. "REFILL row is absent, or if the panel is so short it clears the sidebar entirely — "
	      .. "at that point it cannot show the overlap either way and 01/02 are the evidence." },
}

WorldLoaded = function()
	-- Centre on the ballast abrams so there is sidebar-adjacent map content behind the panel.
	-- FocusBetween is the only centring helper; passing the same actor twice centres on it.
	TestHarness.FocusBetween(Ballast, Ballast)

	-- One hover + one capture per subject, each pair fully settled before the next hover is
	-- armed. Interleaving them would re-point the palette while a capture was still waiting to
	-- sample, and the frame would carry the NEXT subject's tooltip under THIS subject's label —
	-- the mislabelling trap recorded in SCREENSHOT.md.
	local t = 0
	for _, s in ipairs(subjects) do
		t = t + HoverSettleTicks
		Trigger.AfterDelay(t, function()
			-- Logged rather than asserted: a demo has no verdict, and a false here is the one
			-- thing that makes the capture below meaningless rather than merely ugly. Read it
			-- back out of lua.log if a frame comes up with no tooltip.
			local armed = Test.HoverProductionIcon(s.type)
			print("demo-production-tooltip: hover " .. s.type .. " armed=" .. tostring(armed))
		end)

		t = t + HoverSettleTicks
		Trigger.AfterDelay(t, function()
			TestHarness.Screenshot(s.label, s.note)
		end)
	end
end
