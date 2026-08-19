-- CAPTURE SCENARIO: the untyped radar circle still draws independently.
--
-- The concealment-gauge commit split WithRangeCircle.RenderRangeCircle in two: circles
-- declaring a Type now route through RangeCircleGrouping (which dims arcs falling inside
-- an equal-radius peer), while untyped ones keep the original independent path. The radar
-- circle is untyped, so nothing about it should have changed.
--
-- Two selected MSAR sixteen cells apart, both deployed. Their 42-cell circles overlap
-- almost entirely, which is the arrangement in which grouped and ungrouped look different.
--
-- NOT A VERDICT ON APPEARANCE. The state assertions here are only that both radars really
-- deployed and both are really selected — if either were false the frame would show one
-- circle, or none, and that would be a fact about the scenario rather than the renderer.
--
-- See rules.yaml for the one deviation from shipped config (RequireShift) and its limits.

WorldLoaded = function()
	print("[radar] zoom = " .. tostring(Test.SetZoom(1)) .. "x MinZoom")

	Test.IssueDeploy(Radar1)
	Test.IssueDeploy(Radar2)

	TestHarness.FocusBetween(Radar1, Radar2)

	-- Deploy runs a make-animation and grants `deployed` at the end of it; the circle's
	-- RequiresCondition keys off that, so give it room before asserting or photographing.
	Trigger.AfterDelay(DateTime.Seconds(6), function()
		if Radar1.IsDead or Radar2.IsDead then
			Test.Fail("a radar died before the capture")
			return
		end

		Test.SelectActors({ Radar1, Radar2 })
	end)

	Trigger.AfterDelay(DateTime.Seconds(8), function()
		local selected = Test.GetSelectedCount()
		if selected ~= 2 then
			Test.Fail("selection is " .. tostring(selected) .. " actors, not 2 — one circle " ..
				"in the frame would look like a merge that never happened")
			return
		end

		TestHarness.Screenshot("01-two-radars-selected",
			"expects: TWO selected radar vehicles sixteen cells apart, each inside its own " ..
			"complete black 42-cell circle. " ..
			"CORRECT = two full circles, both drawn at the same weight all the way round, " ..
			"visibly CROSSING each other at two intersection points. That crossing is the " ..
			"point: untyped circles are supposed to ignore each other. " ..
			"BROKEN = the arcs where the circles overlap are faded to a quarter alpha, or " ..
			"the pair reads as one merged outline — that is the grouped look leaking onto " ..
			"a circle that declares no Type. " ..
			"ALSO BROKEN, differently = only one circle, or none, which would mean the " ..
			"untyped branch stopped yielding a renderable at all. " ..
			"Both vehicles should be shown in their deployed sprite, not their driving one.")
	end)

	Trigger.AfterDelay(DateTime.Seconds(10), function()
		Test.Pass("captured two deployed radars with overlapping 42-cell circles")
	end)
end
