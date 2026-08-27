-- REGRESSION GUARD: restoring strict FrozenUnderFog visibility must not cost the
-- default game its buildings at t=0.
--
-- =====================================================================================
-- WHAT THIS EXISTS FOR
-- =====================================================================================
-- 12a9b91b (2026-05-03, "QUICK FIX 260503") short-circuited FrozenUnderFog.IsVisible to
-- an unconditional `return true`. It was not vandalism: it was reverting 2d7603bf, which
-- had made the same method strict, on the report that map-placed buildings then rendered
-- invisible at game start. The stated mechanism was a first-frame race --
-- FrozenActor.Visible initialises to true (FrozenActorLayer.cs:68) and FrozenUnderFog.cs
-- inverts it into FrozenState.IsVisible, so a building reads not-visible until the first
-- UpdateVisibility pass runs.
--
-- That race was never reproduced. This scenario is the reproduction attempt, kept
-- permanently: if the race is real, restoring IsVisibleInner trips rung A below. If it
-- is not -- if the May report conflated "correctly hidden under shroud" with a bug,
-- which is plausible because the same commit ALSO turned shroud off -- then rung B is a
-- standing guard that the quick fix can never be reintroduced without a red test.
--
-- =====================================================================================
-- WHY THE GRACE PERIOD IS ~ZERO, AND WHY THAT IS THE WHOLE DESIGN
-- =====================================================================================
-- The defect under test is transient by description: "hide on first render before any
-- sight pass runs". A scenario that waits 40 ticks and then asks "is it visible?" passes
-- against that defect and measures nothing. So rung A samples from tick 1 and records
-- the FIRST tick at which the building reports visible, rather than sampling once after
-- a settle.
--
-- One tick of latency is expected and harmless -- FrozenUnderFog defers its initial
-- state to a frame-end task (FrozenUnderFog.cs:79) and UpdateVisibility runs on
-- FrozenActorLayer's own tick, and nothing is drawn before the first tick anyway. So the
-- bar is set at VisibleByTick, not at tick 1, and the measured flip tick is printed
-- either way. A flip at 1 or 2 means the race exists and is benign; never flipping is
-- the May symptom.
--
-- =====================================================================================
-- WHAT EACH RUNG PROVES, AND WHY BOTH ARE NEEDED
-- =====================================================================================
-- Rung A (NearBox, actively watched, cell visibility > 1): must be visible almost
-- immediately. This is the regression guard on the fix.
--
-- Rung B (FarBox, explored but unwatched, cell visibility exactly 1): must NOT report
-- the live actor. Under fog the player is entitled to a REMEMBERED image -- the frozen
-- actor, carrying stale HP and owner -- and not the real one. This is the rung that goes
-- red if anyone reintroduces the short-circuit, and it does so under the SHIPPED default
-- lobby config rather than the Explored-OFF config its sibling scenario uses.
--
-- Each rung is the other's control. Rung A alone passes if everything became visible,
-- which is precisely the bug. Rung B alone passes if buildings were vetoed wholesale,
-- which is precisely the regression. They are asserted against the same actor type on
-- the same map.
--
-- =====================================================================================
-- WHAT IS ASSERTED, AND THE ONE THING THAT CANNOT BE
-- =====================================================================================
-- Test.IsDetectedBy is Actor.CanBeViewedByPlayer, which for a building IS
-- FrozenUnderFog.IsVisible -- the same call IRenderModifier.ModifyRender gates the
-- sprite on (FrozenUnderFog.cs:182). So it is the closest available probe to "did the
-- building render", and here it is the verdict rather than a diagnostic. That is the
-- opposite of its role in test-unscouted-building-hidden, where the short-circuit made
-- it unable to discriminate; with the short-circuit gone it is the honest question.
--
-- What CANNOT be asserted from Lua is the other half of rung B: that the player still
-- sees the remembered image. There is no frozen-actor hook in TestGlobal, so nothing
-- here can distinguish "correctly showing a frozen image" from "showing nothing at all".
-- Rung B pins that the LIVE actor is hidden; that the frozen one is drawn in its place
-- is a PLAYTEST.

local VisibleByTick = 8
local LateReadTick = 40

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")
	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve both players")
		return
	end

	if Scout == nil or NearBox == nil or FarBox == nil then
		Test.Fail("SETUP: map actors Scout/NearBox/FarBox did not all resolve")
		return
	end

	TestHarness.FocusBetween(Scout, NearBox)
	TestHarness.Select(Scout)

	local ticks = 0
	local nearFirstVisibleTick = -1
	local nearVisAtFirstSight = -1

	-- AssertWithin's failure string is concatenated EAGERLY at registration, so it can
	-- only report pre-run values. Every live number is therefore printed to lua.log or
	-- returned inside a "fail:" string, which IS evaluated at failure time.
	TestHarness.AssertWithin(20, function()
		ticks = ticks + 1

		if Scout.IsDead or NearBox.IsDead or FarBox.IsDead then
			return "fail: SETUP -- an actor died; nothing here should be able to shoot"
		end

		-- Sample the near building EVERY tick from the first one. This is the part that
		-- makes the scenario able to see a first-frame race at all.
		if nearFirstVisibleTick < 0 and Test.IsDetectedBy(NearBox, usa) then
			nearFirstVisibleTick = ticks
			nearVisAtFirstSight = Test.GetVisibility(usa, NearBox.Location)
			print(string.format(
				"[building-at-spawn] NearBox first reported visible on tick %d (cell vis %d)",
				nearFirstVisibleTick, nearVisAtFirstSight))
		end

		if ticks < LateReadTick then return false end

		local nCell = NearBox.Location
		local fCell = FarBox.Location
		local nVis = Test.GetVisibility(usa, nCell)
		local fVis = Test.GetVisibility(usa, fCell)
		local nDetected = Test.IsDetectedBy(NearBox, usa)
		local fDetected = Test.IsDetectedBy(FarBox, usa)

		print(string.format(
			"[building-at-spawn] near cell=%d:%d vis=%d detected=%s firstVisibleTick=%d | " ..
			"far cell=%d:%d vis=%d detected=%s",
			nCell.X, nCell.Y, nVis, tostring(nDetected), nearFirstVisibleTick,
			fCell.X, fCell.Y, fVis, tostring(fDetected)))

		-- ---- setup controls: these decide whether the run is a verdict at all ----
		if fVis ~= 1 then
			return "fail: SETUP -- the far building's cell reads visibility " .. fVis ..
				", not 1. This scenario needs 'explored, nobody looking'. 0 means Explored " ..
				"Map did not take (check ExploredMapCheckboxEnabled/Locked); >1 means some " ..
				"USA actor's sight band reaches 58,16 and the remembered-image rung is inert"
		end

		if nVis <= 1 then
			return "fail: SETUP -- the near building's cell reads visibility " .. nVis ..
				", so the scout is not actually watching it and rung A cannot say anything " ..
				"about buildings being visible at spawn"
		end

		-- ---- rung A: the t=0 regression guard ----
		if nearFirstVisibleTick < 0 then
			return "fail: a map-placed enemy building under active observation (cell " ..
				"visibility " .. nVis .. ") NEVER reported visible in " .. ticks .. " ticks. " ..
				"This is the symptom 12a9b91b described and the reason the quick fix was " ..
				"applied: FrozenActor.Visible initialises true and FrozenState.IsVisible " ..
				"inverts it, so the building stays hidden if no UpdateVisibility pass ever " ..
				"flips it. The strict-visibility restoration is NOT safe as it stands"
		end

		if nearFirstVisibleTick > VisibleByTick then
			return "fail: a map-placed enemy building under active observation took " ..
				nearFirstVisibleTick .. " ticks to become visible (budget " .. VisibleByTick ..
				"). Not the total blackout 12a9b91b reported, but a real startup delay in " ..
				"which the player sees an empty cell where a building is standing"
		end

		-- ---- rung B: the standing guard against the quick fix returning ----
		if fDetected then
			return "fail: an enemy building on explored-but-unwatched ground (cell " ..
				"visibility 1) reports the LIVE actor as visible. Under fog the player is " ..
				"entitled to a remembered image, not live state -- live damage, live " ..
				"death, and newly-built structures appearing the instant they are placed. " ..
				"If FrozenUnderFog.IsVisible has been short-circuited to `return true` " ..
				"again, this is that. See test-unscouted-building-hidden for the same " ..
				"defect in its Explored-OFF form"
		end

		if not nDetected then
			return "fail: CONTROL -- the near building was visible on tick " ..
				nearFirstVisibleTick .. " but is NOT visible now at tick " .. ticks ..
				". Visibility is being lost after startup while the scout is still " ..
				"watching, which rung B would otherwise report as a clean pass"
		end

		return true
	end, "building-at-spawn check never completed within 20s")
end
