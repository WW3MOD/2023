-- SHARED BODY for the §B6 signature-moment pair:
--
--     tools/autotest/scenarios/test-escalation-banner-combined   (t90 dies to the FIRST round -- PASS)
--     tools/autotest/scenarios/test-escalation-banner-separate   (t90 survives three -- PASS)
--
-- WHAT IS MEASURED. DEFCON 2 is the phase the whole mode exists to dramatise and it ends on the
-- first casualty, so in a real match it is SHORT: 98 ticks (5.9 s) in run 260920_010605_p1901, and
-- ONE TICK in run 260915_012829. The transition banner is held for 66 ticks and the widget kept a
-- single shownLevel that the next edge OVERWROTE, so what the player actually got was `OPEN WAR`
-- alone -- a banner and a half. §B6 of the 2026-09-19 escalation review; the review rules out both
-- alternatives (queueing would announce "the border is open" while the war is already on; holding
-- longer has the same defect from the other end).
--
-- THE RULE THIS PINS, in one sentence: two edges inside one hold window are ONE banner naming both,
-- and two edges outside it are two banners. Both halves are asserted in both arms -- the arm only
-- declares which branch IT is supposed to take, and a run that takes the other one says so by name.
--
-- WHY THE ARMS DIFFER BY A HIT POINT TOTAL AND NOTHING ELSE. The whole sequence is identical in
-- both directories: the no-rush clock runs out, the abrams is clicked onto the t90 on that same
-- tick, and the t90 dies. What changes is HOW LONG the t90 takes to die, and that is one number in
-- rules.yaml -- Health.HP. TankRound.Abrams lands ~23000 per round and its BurstWait is 130 ticks,
-- so a t90 that dies to round one dies ~20 ticks after the edge (INSIDE the 66-tick window) and one
-- that needs three rounds dies ~260 ticks after it (well OUTSIDE). No Lua constant, no clock and no
-- geometry differs between the arms; `diff -r` them before believing any result.
--
-- THE OBSERVABLE IS THE MODEL, NOT THE WIDGET, AND THAT COST A RUN TO LEARN. Run 260921_165856
-- asserted the widget's own counter on the tick DEFCON reached 2 and read `raised=0`. The widget
-- had not ticked yet -- and the reason is structural rather than a one-frame lag:
--
--     Ui.Tick DOES run under --hidden (Game.cs:786-790 puts it in the LOGIC tick, not the render
--     path), but it runs on Ui.Timestep = 40 ms of WALL CLOCK (Widget.cs:30). The world runs on a
--     different clock entirely (OrderManager.cs:187), and a --hidden autotest sets
--     Graphics.CapFramerate=false so the sim FREE-RUNS. The world therefore advances many ticks
--     between two UI ticks, and a DEFCON 2 that lasts 98 ticks -- or one tick -- can pass ENTIRELY
--     between them.
--
-- So no widget-derived count is a sound observable in an autotest, at any tolerance: it is a fact
-- about one client's sampling rather than about the match. Test.DefconEdgesCombine is computed from
-- DefconEscalation.LevelReachedTick, written in synced code on the tick each level is first reached,
-- and is what this scenario asserts. Test.DefconBannerState is still printed in every census --
-- knowing what the client actually drew is worth having when a run goes wrong -- but nothing fails
-- on it.
--
-- WHAT A GREEN THEREFORE DOES NOT PROVE: that the band on screen is the combined one. That half is
-- pinned by NUnit (DefconReadoutTest, the §B6 block) and by eye. See README.md.

-- WHICH BRANCH THE DIRECTORY RUNNING THIS BODY IS BUILT FOR. Declared HERE, with a name that
-- cannot be mistaken for a real arm, and overwritten by each scenario's own two-line arm.lua --
-- which map.yaml lists AFTER this file, so the overwrite has happened long before WorldLoaded runs.
--
-- DECLARED IN THE SHARED BODY RATHER THAN ONLY IN THE ARMS, and that is not merely tidiness:
-- lua-gate analyses a shared lib on its own, so a global that only ever appears in a scenario-folder
-- file reads to it as an index into nil and it says so. Owning the shape here also means a
-- directory that forgets its arm.lua fails at SETUP with a named reason instead of a nil index.
BannerArm = { name = "undeclared" }

local TicksPerSecond = TestHarness.TicksPerSecond

local CEASE_FIRE_LEVEL = 3
local HOLD_FIRE_LEVEL = 2
local OPEN_WAR_LEVEL = 1

-- DefconReadoutModel.BannerHoldTicks(60) = 4000 / 60 = 66. NOT 100: read at 25 ticks per second
-- that would be six seconds, which is the 1.5x error this repo has made at eleven sites. Note that
-- TestHarness.TicksPerSecond is 25 and is used for DEADLINES ONLY below, where over-estimating is
-- harmless -- it must never be used to derive this number.
local BANNER_HOLD_TICKS = 66

local ORDER_DELAY_TICKS = 4  -- ticks to wait after the 3 -> 2 edge before clicking; see below
local SETTLE_TICKS = 15      -- every World trait has ticked; DefconEscalation's clock is running
local CLOCK_TICKS = 150      -- DefconEscalation.NoRushTicksOverride in both rules.yaml
local CLOCK_SLACK = 200      -- budget over the clock before "the 3 -> 2 edge never happened"
local KILL_TICKS = 600       -- budget for the ordered shots to finish the t90 in EITHER arm
local AFTER_TICKS = 30       -- let the widget tick past the edge before the final read

local DEADLINE_TICKS = 1500
local DEADLINE_SECONDS = DEADLINE_TICKS / TicksPerSecond

WorldLoaded = function()
	TestHarness.FocusBetween(Probe, Contact)
	TestHarness.Select(Probe)

	local ticks = 0
	local phase = "settle"

	local twoTick, oneTick, orderTick = -1, -1, -1

	local function Census()
		return string.format(
			"t=%d phase=%s defcon=%d combines=%s | at2=%d at1=%d | banner %s (diagnostic) | "
			.. "casualty %s | sampled two=%d one=%d | t90 hp %s/%s | abrams act %s",
			ticks, phase, Test.DefconLevel(), Test.DefconEdgesCombine(),
			Test.DefconLevelReachedTick(2), Test.DefconLevelReachedTick(1),
			Test.DefconBannerState(), Test.DefconFirstCasualty(), twoTick, oneTick,
			Contact.IsDead and "(dead)" or tostring(Contact.Health),
			Contact.IsDead and "(dead)" or tostring(Contact.MaxHealth),
			Probe.IsDead and "(dead)" or Test.ActivityChain(Probe))
	end

	TestHarness.AssertWithin(DEADLINE_SECONDS, function()
		ticks = ticks + 1

		if Probe.IsDead then
			return "fail: SETUP -- the abrams died. It is the only thing on this map that fires, and "
				.. "nothing here should be able to kill it. " .. Census()
		end

		-- ========== SETTLE: the controls that make the rest mean anything ==========
		if phase == "settle" then
			if ticks < SETTLE_TICKS then
				return false
			end

			-- THE ARM MUST DECLARE ITSELF. Each directory ships a two-line arm.lua; a missing one
			-- means the scenario is running a shared body with no expectation and would "pass"
			-- whichever branch the match happened to take.
			if BannerArm.expectCombines == nil or BannerArm.name == "undeclared" then
				return "fail: SETUP -- this directory never overwrote BannerArm, so the shared body is "
					.. "running with no expectation and would report PASS on either branch. "
					.. "Each arm ships a two-line arm.lua and map.yaml must list it in "
					.. "LuaScript.Scripts AFTER defcon-banner-combine-lib.lua. " .. Census()
			end

			-- THE MODEL MUST BE THERE. "absent" means the world actor carries no DefconEscalation at
			-- all, so nothing below can be measured. The WIDGET is deliberately not required: it is
			-- diagnostic here, and a run that cannot see it still produces a real verdict.
			if Test.DefconEdgesCombine() == "absent" then
				return "fail: SETUP -- Test.DefconEdgesCombine is \"absent\": this world carries no "
					.. "DefconEscalation, so there are no edges to measure. " .. Census()
			end

			local level = Test.DefconLevel()
			if level ~= CEASE_FIRE_LEVEL then
				return string.format(
					"fail: SETUP -- the match opened at DEFCON %d, not %d. This pair needs the 3 -> 2 "
					.. "edge AND the 2 -> 1 edge; a match opening at 2 has only one of them and can "
					.. "never combine anything. %s", level, CEASE_FIRE_LEVEL, Census())
			end

			if Test.DefconEdgesCombine() ~= "pending" then
				return "fail: SETUP -- the match already has both edges recorded at DEFCON 3 ("
					.. Test.DefconEdgesCombine() .. "), which means it did not open where this pair "
					.. "needs it to. " .. Census()
			end

			print("[banner-" .. BannerArm.name .. "] settled. " .. Census())
			phase = "clock"
			return false
		end

		-- ========== CLOCK: wait out the no-rush period, then order the shot ==========
		if phase == "clock" then
			if Test.DefconLevel() == HOLD_FIRE_LEVEL then
				twoTick = ticks
				print("[banner-" .. BannerArm.name .. "] DEFCON 2 reached. " .. Census())
				phase = "order"
				return false
			end

			if ticks > SETTLE_TICKS + CLOCK_TICKS + CLOCK_SLACK then
				return string.format(
					"fail: the no-rush clock never expired. DefconEscalation.NoRushTicksOverride is "
					.. "%d in rules.yaml and %d ticks have passed at DEFCON %d. Nothing below this "
					.. "point ran. %s", CLOCK_TICKS, ticks, Test.DefconLevel(), Census())
			end

			return false
		end

		-- ========== ORDER: a few ticks after the edge, click the abrams onto the t90 ==========
		-- NOT ON THE TRANSITION TICK ITSELF. DefconEscalation.CeaseAutonomousFireEverywhere runs on
		-- exactly that tick and cancels every autonomous engagement on the map -- including the
		-- abrams' own, because at DEFCON 3 DefconHoldsFire is FALSE and autotarget acquires normally
		-- (only Armament.CanFire is gated there). Run 260921_165856 caught the abrams mid
		-- `AttackActivity(cancelling)` on that tick. Issuing into a cancelling activity is not known
		-- to be unsafe, but there is no reason to find out: four ticks is nothing against a 66-tick
		-- window and it puts the order somewhere unambiguous.
		if phase == "order" then
			if ticks < twoTick + ORDER_DELAY_TICKS then
				return false
			end

			-- Through the real click resolver rather than Actor.Attack, so the returned OrderString is
			-- itself an assertion: DEFCON 3 refuses the click outright and DEFCON 2 must not, because
			-- the hold-fire rule is about provenance and an order is provenance.
			local issued = Test.ClickOrder(Probe, Contact)
			if issued ~= "Attack" then
				return "fail: the click onto the t90 produced " .. tostring(issued) .. " rather than "
					.. "Attack, " .. ORDER_DELAY_TICKS .. " ticks after DEFCON reached 2. Only the "
					.. "DEFCON 3 cease-fire refuses the order itself, so either the level moved back "
					.. "or the two are no longer a targeting pair. " .. Census()
			end

			orderTick = ticks
			print("[banner-" .. BannerArm.name .. "] order issued. " .. Census())
			phase = "kill"
			return false
		end

		-- ========== KILL: the ordered shots finish the t90, which ends DEFCON 2 ==========
		if phase == "kill" then
			if Test.DefconLevel() == OPEN_WAR_LEVEL then
				oneTick = ticks
				print("[banner-" .. BannerArm.name .. "] DEFCON 1. " .. Census())
				phase = "verdict"
				return false
			end

			if ticks > orderTick + KILL_TICKS then
				return string.format(
					"fail: the t90 never died. %d ticks after an accepted Attack order it is on %s hp "
					.. "and the level is still %d, so neither banner branch was ever reached. If the "
					.. "abrams is not firing at all, that is the DEFCON 2 hold refusing an ORDERED "
					.. "shot, which is a worse defect than the one this scenario was written for. %s",
					KILL_TICKS, Contact.IsDead and "0" or tostring(Contact.Health),
					Test.DefconLevel(), Census())
			end

			return false
		end

		-- ========== VERDICT: one band, or two, and it must match the gap ==========
		if ticks < oneTick + AFTER_TICKS then
			return false
		end

		-- THE TWO EDGE TICKS THE ENGINE RECORDED, not the ticks this poller happened to sample them
		-- on. Test.DefconLevelReachedTick's own [Desc] is about exactly this hazard: a phase can be
		-- ONE TICK long, and no sampling interval is safe against an edge.
		local at2 = Test.DefconLevelReachedTick(2)
		local at1 = Test.DefconLevelReachedTick(1)
		if at2 < 0 or at1 < 0 then
			return string.format(
				"fail: the engine recorded no tick for DEFCON %d, so the gap this verdict rests on "
				.. "cannot be computed. This is DefconEscalation.RecordLevel failing, not the banner. "
				.. "%s", at2 < 0 and 2 or 1, Census())
		end

		local gap = at1 - at2
		local insideWindow = gap < BANNER_HOLD_TICKS
		local combines = Test.DefconEdgesCombine()

		-- THE INVARIANT, ASSERTED IN BOTH ARMS, AND IT IS A CROSS-CHECK RATHER THAN A TAUTOLOGY:
		-- `combines` comes from the engine applying DefconReadoutModel.CombinesWithPrevious to the
		-- two recorded ticks with BannerHoldTicks(GameSpeed.Timestep), and `insideWindow` is this
		-- script doing the same arithmetic against a hardcoded 66. They agree only if the engine's
		-- hold really is 66 ticks at this game speed -- i.e. if the 1.5x tick-rate error this repo
		-- has made at eleven sites is NOT live in BannerHoldTicks.
		if insideWindow and combines ~= "yes" then
			return string.format(
				"fail: §B6 -- the two edges landed %d ticks apart (DEFCON 2 at %d, DEFCON 1 at %d), "
				.. "INSIDE the %d-tick hold window, so the player should have had ONE banner naming "
				.. "both -- but the engine says combines=%s. This is the shipped defect: the 2 -> 1 "
				.. "edge overwrote the DEFCON 2 banner and the player saw only OPEN WAR. %s",
				gap, at2, at1, BANNER_HOLD_TICKS, combines, Census())
		end

		if not insideWindow and combines ~= "no" then
			return string.format(
				"fail: §B6 -- the two edges landed %d ticks apart (DEFCON 2 at %d, DEFCON 1 at %d), "
				.. "OUTSIDE the %d-tick hold window, so they must NOT be combined -- but the engine "
				.. "says combines=%s. Combining edges this far apart would put a sentence about a "
				.. "finished phase on screen while the war is already on, which is the 'queueing' "
				.. "option the review rejects. %s", gap, at2, at1, BANNER_HOLD_TICKS, combines, Census())
		end

		-- THE ARM MUST HAVE TAKEN THE BRANCH IT WAS BUILT FOR. Without this, an arm whose t90 hp
		-- drifted would quietly measure the other branch and still report PASS -- two directories
		-- both testing the same half, which looks exactly like a clean pair.
		if combines ~= BannerArm.expectCombines then
			return string.format(
				"fail: ARM DRIFT -- this directory is the '%s' arm and expects combines=%s, but the "
				.. "run produced combines=%s with a %d-tick gap (DEFCON 2 at %d, DEFCON 1 at %d). The "
				.. "rule was obeyed; it is the STAGING that moved, so the pair no longer covers both "
				.. "branches. Check the t90's Health.HP in rules.yaml -- it is the only quantity that "
				.. "differs between the two arms. %s", BannerArm.name, BannerArm.expectCombines,
				combines, gap, at2, at1, Census())
		end

		-- WHO CHOSE -- the other half of §B6. The event-log line is client-side text this script
		-- cannot read, but it is built from exactly these four strings, so asserting them asserts
		-- the line's content. A "none" here means DefconCasualtyObserver never reported the kill and
		-- the level moved by some other route entirely.
		local casualty = Test.DefconFirstCasualty()
		if casualty == "none" then
			return "fail: §B6 -- DEFCON reached 1 but no first casualty was recorded, so the "
				.. "event-log line naming who fired has nothing to print. " .. Census()
		end

		if not string.match(casualty, "attacker=USA") or not string.match(casualty, "weapon=abrams") then
			return "fail: §B6 -- the first casualty does not name the abrams that fired it: "
				.. casualty .. ". " .. Census()
		end

		-- THE VICTIM'S OWNER, NOT ITS TYPE. The t90 carries VehicleCrew with
		-- CrewDamageThresholdPercent 8, so a crewman ejected and then killed by the same burst is a
		-- qualifying casualty too and would arrive here as `victim=crew.gunner.russia`. That is the
		-- rule working, not a failure -- what §B6 asks this line to prove is WHO CHOSE, and the
		-- attacker pair above is that. The owner is still pinned so a self-inflicted or third-party
		-- death cannot satisfy it.
		if not string.match(casualty, "owner=Russia") then
			return "fail: §B6 -- the first casualty was not one of Russia's: " .. casualty
				.. ". Only enemy action ends DEFCON 2. " .. Census()
		end

		print("[banner-" .. BannerArm.name .. "] PASS. " .. Census())
		return true
	end, function()
		return "fail: the predicate never reached a verdict inside its backstop deadline, which means "
			.. "it stopped advancing rather than that any phase overran -- every phase owns its own "
			.. "budget and its own reason. " .. Census()
	end)
end
