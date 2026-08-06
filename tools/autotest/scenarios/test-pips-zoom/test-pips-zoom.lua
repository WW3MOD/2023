-- AUTO TEST: pips must render at every zoom level.
--
-- A Bradley (cargo pips + two ammo pip rows) is loaded with four riflemen and
-- selected, then the camera is stepped through the full zoom range and a
-- screenshot taken at each stop. Every shot must show the pip rows above the
-- Bradley; upstream's decoration cull hid them below MinZoom, which in WW3MOD
-- is the middle of the ordinary player zoom range (min zoom is unlocked for
-- everyone, not just spectators).
--
-- Beats:
--   01-zoom-in      — 4.0x MinZoom (fully zoomed in)
--   02-zoom-default — 1.0x MinZoom (default zoom)
--   03-zoom-half    — 0.5x MinZoom (where pips used to vanish)
--   04-zoom-out     — 0.25x MinZoom (fully zoomed out)

local boarded = false
local captureStarted = false

-- One capture per beat, each on its own delay: the screenshot reads the last
-- rendered frame, so the zoom change needs a render tick to land before the
-- grab or the PNG shows the previous zoom level.
local beats = {
	{ scale = 4.0, label = "01-zoom-in", note = "fully zoomed in" },
	{ scale = 1.0, label = "02-zoom-default", note = "default zoom" },
	{ scale = 0.5, label = "03-zoom-half", note = "half zoomed out — pips used to vanish here" },
	{ scale = 0.25, label = "04-zoom-out", note = "fully zoomed out" },
}

local function CaptureBeat(i)
	if i > #beats then
		Trigger.AfterDelay(25, function()
			Test.Pass("captured pips at 4.0x / 1.0x / 0.5x / 0.25x MinZoom")
		end)

		return
	end

	local beat = beats[i]
	local actual = Test.SetZoom(beat.scale)
	TestHarness.FocusBetween(Transport)

	Trigger.AfterDelay(15, function()
		TestHarness.Screenshot(beat.label, string.format(
			"expects: cargo pips (4 filled) and ammo pips visible above the selected Bradley — %s, zoom = %.2fx MinZoom",
			beat.note, actual))

		Trigger.AfterDelay(10, function() CaptureBeat(i + 1) end)
	end)
end

WorldLoaded = function()
	for _, rifleman in ipairs({ Rifle1, Rifle2, Rifle3, Rifle4 }) do
		Test.IssueEnterTransport(rifleman, Transport)
	end

	Trigger.AfterDelay(25 * 20, function()
		if not boarded then
			Test.Fail("riflemen did not board the Bradley within 20s")
		end
	end)
end

Tick = function()
	if captureStarted or Transport.IsDead or Transport.PassengerCount < 4 then
		return
	end

	boarded = true
	captureStarted = true
	TestHarness.Select(Transport)
	CaptureBeat(1)
end
