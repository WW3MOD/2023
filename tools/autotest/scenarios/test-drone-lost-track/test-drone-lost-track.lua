--[[
  TEST: does an @experimental drone operator aim its drone at where an enemy was
  last seen, rather than at the stalest ground it could otherwise explore?

  WHAT THIS MEASURES, AND WHY IT IS A TRACE AND NOT A BOOLEAN.
  The question is a PREFERENCE, so "did a drone launch" cannot answer it — a drone
  launches either way. What separates the two hypotheses is WHERE it goes, so the
  observable is a sampled position trace of the drone actor, reduced to how much of
  its flight it spent near the vanish cell.

  Two things that look like reasonable observables and are not:

  1. IsIdle. USELESS FOR AIRCRAFT. An airframe with nothing to do is running
     FlyIdle/Hover, which IS an activity, so a parked drone never reads idle. Any
     gate written on it latches false forever.
  2. A min/max distance pair. Cannot distinguish "flew over once on the way
     somewhere else" from "flew there and stayed", which is exactly the difference
     between the two hypotheses. Hence SAMPLES, and a fraction-of-time statistic
     over them.

  ARMS. This scenario is the TREATMENT. The control is the same scenario with
  IntelSampleInterval raised beyond the run length in ai.yaml, which leaves the
  contact table empty and every candidate scoring intelSquares 0 — the pre-change
  behaviour. The control is EXPECTED TO FAIL here, and that failure is the RED.
  Do not "fix" a control-arm failure.

  Budgeted in TICKS throughout. TestHarness.TicksPerSecond says 25 and the real
  rate is 16.667 (CLAUDE.md), so any seconds conversion in this file would be
  wrong by 1.5x. The helper's own header tells new scenarios to budget in ticks.
]]

-- ===== Geometry. See map.yaml for why each distance is what it is. =====
local OperatorCell = { X = 18, Y = 45 }
local VanishCell = { X = 47, Y = 54 }

-- The scout stands ON the vanish cell's neighbour so the enemy is unambiguously
-- inside its vision, then dies to create the lost-observer condition.
local ScoutCell = { X = 47, Y = 53 }

-- A hover cell counts as "on the contact" within this many cells of the vanish
-- cell. The closest candidate the leash allows is 8 cells from V (30 - 22; run 8
-- measured bestintelcell=39,51 at exactly 8), and grid
-- candidates are spaced 2 cells apart, so 18 admits the genuine hunt cells while
-- still excluding the whole western half of the hover disc.
local NearVanishCells = 18

-- ===== Schedule, in ticks =====
--
-- THE KILL MUST LAND BEFORE t150 OR THIS SCENARIO CANNOT TEST WHAT IT CLAIMS TO.
--
-- Measured, run 5/6, not reasoned: the module's first evaluation is at t200 (fixed
-- ReevaluateInterval), and IntelSquares (DroneTaskingMath.cs:186) returns the
-- CURRENTLY-OBSERVED tier — areaSquares, 60 — for any contact whose age is <=
-- FreshSightingTicks (50, ai.yaml:955). Killing the scout at 175 left the contact 25
-- ticks old at the only evaluation that matters, so the lost-track ramp under test was
-- never once read. Both arms launched at 33,29, which is 28 cells from the vanish cell
-- — the exact rim of DroneVisionCells — where IntelFalloff discounts 60 squares to
--   60 * (28 - 28 + 1) / (28 + 1) = 2
-- and the treatment logged intel=2 against reveal=307. The lost tier at that age would
-- have logged 8, and at a real hunt cell 154. The 2 was not the term failing; it was
-- the wrong tier being asked, from the rim.
--
-- Two constraints, and they nearly collide:
--   age at t200 must EXCEED FreshSightingTicks 50  =>  KillScoutTick <= 149
--   scout must hold the contact >= 4 BeliefStore passes (25 ticks each) at full
--     confidence                                   =>  KillScoutTick - SpawnTick >= 100
-- 50/149 leaves a 99-tick window: 3.96 passes, one tick short. Hence SpawnTick moves
-- too. 25/140 gives a 115-tick window (4.6 passes) and age 60 at t200, ten ticks clear
-- of the boundary — and the error is ONE-SIDED, because nothing else observes the truk
-- until the wandering e3.america arrives around t1250, so LastSeenTick can only be
-- earlier than the kill, never later. Earlier means older means deeper into the lost
-- tier, which is the safe direction.
--
-- DO NOT INSTEAD LOWER FreshSightingTicks IN ai.yaml. That is retuning the mechanism to
-- make its own test pass, and it is the same move as widening NearVanishCells after
-- seeing a miss.
local SpawnTick = 25          -- let the world settle before anything is created
local KillScoutTick = 140     -- see the block above: < 150, and >= 4 passes after SpawnTick
local SampleEveryTicks = 15
local DeadlineTicks = 1800    -- ~108s at the real 16.667 t/s: first evaluation (200) + FireDelay (50)
                              -- + the 60s loiter, with slack. Only the FIRST launch is being judged.
-- DO NOT RAISE IT TO "LET THE SECOND SORTIE ARRIVE". Run 5/6 both logged a second launch
-- ORDER at t1600 aimed at 39,53 — 8 cells from the vanish cell — and it is tempting to read
-- that as the mechanism working and the deadline cutting it off. It is not. The CONTROL,
-- with records=0 and intel=0, chose the SAME cell with the SAME reveal=252: that target is
-- the revealed-area argmax and the intel term did not pick it. Extending the deadline so
-- the flight lands would therefore raise `near` in BOTH arms, and a big enough second hover
-- would turn a clean double-FAIL into a double-PASS — which is exactly the "control passes
-- with intel=0" outcome CONTROL-ARM.md says to report and stop on. It would not reveal a
-- suppressed signal; it would manufacture one.
-- (No drone ever flew that order anyway: the first sortie docked just after t1545 and
-- RearmTicks 150 left ~100 ticks to run when the armament fired at t1650, so GetLaunchable
-- returned null and the shot was wasted — the next BurstWait 200 lands at t1850, past here.)

local scout, enemy, operator
local samples = {}
local nearCount = 0
local minDist = 9999
local firstDroneTick = -1
local setupFaults = {}
local elapsed = 0

-- The first contaminating friendly seen AFTER the guard window closed. Recorded and
-- printed rather than acted on; the guard block in tick() carries the reasoning.
local lateIntruder = nil
local lateIntruderTick = -1
local samplesAfterLate = 0

local function cellDist(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	-- Integer Euclidean, matching CVec.Length on the engine side so the numbers in
	-- the summary are comparable with DroneVisionCells and the falloff radius.
	return math.floor(math.sqrt((dx * dx) + (dy * dy)))
end

-- The drone is a separate actor from its operator, spawned by CarrierMaster on
-- launch. Found by type rather than held as a reference because it is destroyed
-- and respawned across sorties.
local function findDrone(usa)
	local found = nil
	for _, a in ipairs(usa.GetActorsByType("quadcopterdrone")) do
		if not a.IsDead and a.IsInWorld then
			found = a
			break
		end
	end

	return found
end

-- THE CONFOUND GUARD. A friendly within vision of the vanish cell makes that cell
-- currently-visible, and BeliefStore removes any contact whose cell is visible
-- (ResolveUnobserved, :263). If the bot builds something that wanders east, the
-- memory under test is erased by the bot's own economy and the run would report a
-- clean-looking negative for entirely the wrong reason. Named explicitly so that
-- cannot happen silently. The drone itself is exempt: going there is the point.
-- THIS FUNCTION ONLY DETECTS. Whether a detection voids the run is the caller's
-- decision and depends on WHEN it fires — see the guard block in tick().
-- ASK THE SPATIAL QUESTION SPATIALLY. The obvious form — walk Player.GetActors()
-- and compare each actor's Location — ABORTS THE WHOLE SCRIPT, and did: that
-- collection includes the PLAYER ACTOR, which defines no Location, and reading a
-- property an actor does not define THROWS rather than returning nil, so an
-- `a.Location ~= nil` guard never gets the chance to fire. Map.ActorsInCircle only
-- ever returns world actors with positions, which makes the entire class of error
-- unreachable instead of merely guarded. (Its own documented pitfall — returns
-- nothing when called from WorldLoaded — does not apply: this runs only from the
-- tick, long after load.)
local function contaminatingFriendly(usa)
	local near = Map.ActorsInCircle(
		Map.CenterOfCell(CPos.New(VanishCell.X, VanishCell.Y)),
		WDist.FromCells(28),
		function(a)
			-- The drone is exempt: going there is the entire point.
			--
			-- The scout needs no exemption and deliberately gets none. This guard first
			-- runs the tick AFTER the scripted kill, so `not a.IsDead` already excludes
			-- it. An `a ~= scout` term would have been an identity comparison between two
			-- script actor wrappers — which nothing in the engine implements (__eq/Equals
			-- are absent) and which no existing scenario performs; every comparison in the
			-- corpus is against nil. Unverified semantics buying nothing.
			return a.Owner == usa
				and not a.IsDead
				and a.Type ~= "quadcopterdrone"
				and a.Type ~= "supplyroute"
		end)

	if #near > 0 then
		-- REPORT WHERE AND HOW FAR, not just what. The first firing of this guard named
		-- only the type ("friendly 'dr.america' gained vision"), which was true but sent
		-- me to the engine's debug.log to find out whether that was the placed operator
		-- or a reinforcement, and what had moved it. The answer was one line —
		-- `[defence] assign unit=21@18,45 -> 39,51 reason=contested-line` — that the
		-- verdict could have carried itself.
		local a = near[1]
		return string.format("%s at %d:%d (%dc from the vanish cell)",
			a.Type, a.Location.X, a.Location.Y, cellDist(a.Location, VanishCell))
	end

	return nil
end

local function summary()
	local pct = 0
	if #samples > 0 then
		pct = math.floor((nearCount * 100) / #samples)
	end

	local trace = ""
	for i, s in ipairs(samples) do
		if i > 1 then trace = trace .. " " end
		trace = trace .. s.X .. ":" .. s.Y
	end

	-- ALWAYS PRINTED, INCLUDING WHEN NOTHING HAPPENED. A silent narrowing is how the next
	-- reader mistakes an ignored confound for a clean run; "lateintruder=none" is the
	-- positive statement that the guard looked and saw nothing after the window closed.
	local late = "none"
	if lateIntruder ~= nil then
		late = string.format("t%d %s (ignored: after the launch decision; %d of %d samples follow it)",
			lateIntruderTick, lateIntruder, samplesAfterLate, #samples)
	end

	return string.format(
		"samples=%d near(<=%dc of %d:%d)=%d (%d%%) mindist=%d firstdrone=t%d op=%d:%d "
		.. "lateintruder=%s || trace: %s",
		#samples, NearVanishCells, VanishCell.X, VanishCell.Y, nearCount, pct, minDist,
		firstDroneTick, OperatorCell.X, OperatorCell.Y, late, trace)
end

local function finish()
	-- SECOND HALF OF THE EXECUTION PROOF — see the WorldLoaded marker. Its presence
	-- means the script reached a verdict under its own power rather than being cut
	-- short mid-tick, which is exactly what happened on the first attempt at this
	-- run and was only caught by reading the verdict's wording.
	Test.Screenshot("99-verdict-reached", "scenario reached finish() and is about to emit its own verdict")

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary())
		return
	end

	-- "No drone ever appeared" is NOT evidence about targeting preference, and
	-- must never be reported as though it were. It is the wiring/cost-gate class of
	-- failure that has hit this module twice, and it wants a different investigation
	-- entirely: a non-empty [drone] counter log means suspect the wiring, an empty
	-- one means the cost gate skipped the tick again.
	if firstDroneTick < 0 then
		Test.Fail("NO DRONE EVER LAUNCHED — this says nothing about targeting. Check the "
			.. "[drone] lines in debug.log: counters present => wiring; log empty => cost gate. || " .. summary())
		return
	end

	if #samples < 5 then
		Test.Fail("drone seen but only " .. #samples .. " samples — too few to call a preference || " .. summary())
		return
	end

	-- The preference criterion. A drone that merely passed overhead on its way
	-- somewhere else cannot clear a majority-of-samples bar.
	--
	-- THE NEGATIVE IS BY MERIT, AND IT STAYS A FAIL. This branch briefly reported SKIP so a
	-- by-design negative could not turn the batch permanently red. That problem is real, but
	-- main solved it a better way while this work sat unmerged: `expected-status` (added
	-- 575e48c8, hardened 7da74c4a) declares the by-merit outcome in a FILE beside the scenario
	-- and grades the run against it, so the batch goes green without the verdict having to lie.
	-- This scenario's declaration is `fail`, so FAIL here is GREEN and a PASS is loudly RED as
	-- "the premise moved" — see tools/autotest/expected-status.sh.
	--
	-- DO NOT RE-FLIP THIS TO Test.Skip. Under a `fail` declaration, SKIP grades RED
	-- ("declared fail, skips instead" in that file's selftest), which is the precise disease
	-- both mechanisms exist to prevent. The declaration and the verdict have to agree, and the
	-- declaration is the half that is meant to move: delete the file in the same commit as
	-- whatever makes the operator actually prefer the contact.
	--
	-- WHAT A DECLARATION CANNOT TELL YOU, and it is the same gap either mechanism leaves: a NEW
	-- targeting regression that moves the drone off the contact for some reason unrelated to
	-- term weight reads exactly like the recorded outcome. The tell is in the summary rather
	-- than the verdict — the treatment reached mindist=25 against the control's 27, so a
	-- treatment run whose mindist regresses to 27+ has lost the effect even though the verdict
	-- is unchanged.
	if (nearCount * 2) >= #samples then
		Test.Pass("drone spent the majority of its flight on the lost-track contact || " .. summary())
	else
		Test.Fail("drone did NOT prefer the lost-track contact — EXPECTED ON MERIT, see this "
			.. "scenario's expected-status file. The term demonstrably moves the chosen cell "
			.. "toward the contact but does not override the best exploration alternative in "
			.. "this deliberately hard geometry (contact 30 cells out against a 22-cell leash, "
			.. "so the best hunt cell sits at maximum falloff). HOW SHORT IS CURRENTLY UNKNOWN: "
			.. "the only measurement predates 1e0226b9, which replaced the drone's rectangular "
			.. "revealed-area query with the vision disc and so changed both `reveal` and "
			.. "`bestintelreveal` — the numerator of the multiplier. Re-measure with "
			.. "./tools/autotest/run-test.sh test-drone-lost-track and read bestintel/"
			.. "bestintelreveal off the [drone] launch line in RUN_DIR/debug.log. || " .. summary())
	end
end

local tick
tick = function()
	elapsed = elapsed + 1

	local usa = Player.GetPlayer("USA-bot")
	if usa == nil then
		Test.Fail("USA-bot player not found")
		return
	end

	if elapsed == SpawnTick then
		-- UNARMED ON PURPOSE. An armed enemy would stamp the danger fields from its own
		-- believed contact, and the drone's hover cell is gated on AIR danger
		-- (MaxAirDanger 100) — a btr inherits ^AutoTargetHMG, and WeaponThreatensAir
		-- keys on ValidTargets containing Helicopter, so an HMG can refuse the very
		-- hunt cell this test is trying to observe. That failure would look exactly
		-- like "the feature does not work". truk has no Armament at all, so it
		-- contributes zero to both channels, and it still carries Health (via ^Vehicle,
		-- HP 10000) which BeliefStore requires before it will record a contact at all.
		-- It is Mobile, so it decays as a mobile contact — the tier under test.
		enemy = Actor.Create("truk", true, {
			Owner = Player.GetPlayer("Russia-bot"),
			Location = CPos.New(VanishCell.X, VanishCell.Y)
		})

		scout = Actor.Create("e1.america", true, {
			Owner = usa,
			Location = CPos.New(ScoutCell.X, ScoutCell.Y)
		})

		if enemy == nil or enemy.IsDead then
			setupFaults[#setupFaults + 1] = "enemy did not spawn at the vanish cell"
		end

		if scout == nil or scout.IsDead then
			setupFaults[#setupFaults + 1] = "scout did not spawn"
		end
	end

	-- Kill the OBSERVER, not the target. An enemy that merely leaves our vision is
	-- verified-clear at once; only losing the observer leaves a decaying memory.
	if elapsed == KillScoutTick then
		if scout ~= nil and not scout.IsDead then
			scout.Kill()
		else
			setupFaults[#setupFaults + 1] = "scout was already dead before the planned kill"
		end

		if enemy == nil or enemy.IsDead then
			setupFaults[#setupFaults + 1] = "enemy died before the observation window"
		end
	end

	-- THE GUARD IS ARMED ONLY UNTIL THE DRONE EXISTS. AFTER THAT IT ONLY REPORTS.
	--
	-- Four slots were spent on this scenario and the last one died here: a technician
	-- the bot's CaptureCoordinator requested at t28 (priority, so NOT budget-gated —
	-- the map's DefaultCash: 0 does not stop it) walked out of the Supply Route toward
	-- the derrick at 38,53 and clipped this circle around t1200. The run was voided
	-- 1000 ticks after the only decision it was measuring.
	--
	-- The contamination is REAL and the 28-cell radius is exactly right, not merely
	-- conservative: ^StandardVision's outermost band (Vision@1, 28c0-32c0) carries
	-- strength 1, and MapLayers.IsVisible(cell, 1) compares STRICTLY (MapLayers.cs:579),
	-- so strength 1 does not verify — 28 cells is the true radius at which a standard
	-- ground unit makes the vanish cell visible and BeliefStore erases the contact.
	-- The tecn sat at 27. It really did erase it.
	--
	-- What it could not do is change the answer. The launch decision resolves inside a
	-- SINGLE tick: ChooseTargetCell reads the intel table, and the order is issued as one
	-- unqueued ForceAttack which CarrierMaster turns into a one-shot MoveTo (CarrierMaster.cs:190).
	-- The drone carries no Armament, so the retarget loop at :138 is unreachable, and
	-- TaskOperator early-returns for as long as a slave is launched. An airborne drone's
	-- destination is IMMUTABLE, so belief state erased at t1200 cannot steer a flight
	-- ordered at t200 — voiding on it fails the run for something that cannot affect it.
	--
	-- Hence: fail hard before the drone exists, record and continue after. The window
	-- closes on the OBSERVED drone rather than on a tick number on purpose — the launch
	-- is not guaranteed at t200 (an evaluation that finds CanLaunchNow false slips to
	-- t400, t600, ...), and a fixed-tick window would then close before the decision it
	-- exists to protect. firstDroneTick is set in the sampling block BELOW this one and
	-- only on sample ticks, so the guard stays armed for up to SampleEveryTicks after the
	-- drone actually appears. That lag is in the safe direction and is left alone.
	if elapsed > KillScoutTick then
		local intruder = contaminatingFriendly(usa)
		if intruder ~= nil then
			if firstDroneTick < 0 then
				-- "made currently-visible", NOT "erased". Run 6 settled which one it is: the truk never
				-- moves, so a friendly that reaches it RE-OBSERVES it and the record returns to the
				-- fresh tier (areaSquares) rather than being removed. Either way there is no lost
				-- contact left to decide on, so the void is right — but saying "erased" sent the last
				-- reader looking for a removal that had not happened.
				setupFaults[#setupFaults + 1] = "a friendly made the vanish cell currently-visible BEFORE "
					.. "any drone launched, so the contact was no longer lost when the launch decision "
					.. "read it: " .. intruder .. " at t" .. elapsed
				finish()
				return
			elseif lateIntruder == nil then
				lateIntruder = intruder
				lateIntruderTick = elapsed
			end
		end
	end

	if elapsed > KillScoutTick and (elapsed % SampleEveryTicks) == 0 then
		local drone = findDrone(usa)
		if drone ~= nil then
			if firstDroneTick < 0 then
				firstDroneTick = elapsed
			end

			local c = drone.Location
			local d = cellDist(c, VanishCell)
			samples[#samples + 1] = { X = c.X, Y = c.Y }
			if d <= NearVanishCells then
				nearCount = nearCount + 1
			end

			-- THE ONE NUMBER THAT WOULD FALSIFY THE NARROWING ABOVE, MEASURED RATHER THAN ASSUMED.
			-- Ignoring late contamination is only sound while every sample belongs to the sortie
			-- that was ordered before it. That holds today by ~50 ticks and nothing enforces it:
			-- ReturnAfter (1000) + the return flight + RearmTicks (150) + the ~300-tick docked
			-- window put the earliest possible SECOND launch at ~t1775, whose drone would spawn
			-- after DeadlineTicks. Lower any of those, or raise the deadline, and a second sortie
			-- targeted from erased belief starts feeding this same statistic. A nonzero count here
			-- is the tell, and it is printed whether or not the run passes.
			if lateIntruderTick >= 0 then
				samplesAfterLate = samplesAfterLate + 1
			end

			if d < minDist then
				minDist = d
			end
		end
	end

	if elapsed >= DeadlineTicks then
		finish()
		return
	end

	Trigger.AfterDelay(1, tick)
end

WorldLoaded = function()
	-- EXECUTION PROOF, AND IT IS THE FIRST STATEMENT ON PURPOSE.
	--
	-- A Lua abort and a real failure both arrive as status "fail", and the first
	-- attempt at this run reported one that looked exactly like the RED it was meant
	-- to produce. Reading the verdict WORDING caught it — the text was the engine's,
	-- not the scenario's — but that is a discipline, and a discipline is not a check.
	--
	-- Test.Screenshot records label, path and TICK into result.json's screenshots[]
	-- synchronously (TestMode.cs:294-308) even though the PNG itself lands async, so
	-- these two markers make the artefact answer the question by itself:
	--
	--   neither marker -> the script never loaded at all
	--   00 only        -> it loaded, then EITHER aborted mid-run (tonight's failure)
	--                     OR failed setup validation in WorldLoaded below. Those two
	--                     are still told apart by whose wording the verdict carries —
	--                     the setup failures are authored here, an abort is not.
	--   00 and 99      -> it reached a verdict through the sampling path under its own
	--                     power, so the status means what it says.
	--
	-- Placed above every actor lookup so that nothing which could throw runs first.
	Test.Screenshot("00-script-loaded", "scenario entered WorldLoaded; no actor has been queried yet")

	local usa = Player.GetPlayer("USA-bot")
	if usa == nil then
		Test.Fail("USA-bot player not found at load")
		return
	end

	local ops = usa.GetActorsByType("dr.america")
	if #ops == 0 then
		Test.Fail("SETUP INVALID: no drone operator (dr.america) on the map")
		return
	end

	operator = ops[1]

	-- Pin the premise the whole geometry rests on. If the operator's own 28-cell
	-- verifying bubble reaches the vanish cell, the contact is verified-clear the
	-- moment the scout dies and there is nothing left to remember — the test would
	-- then be measuring an empty table and reporting it as "no preference".
	local d = cellDist(operator.Location, VanishCell)
	if d <= 28 then
		Test.Fail(string.format(
			"SETUP INVALID: vanish cell is %d cells from the operator, inside its own 28-cell "
			.. "verifying radius — the contact can never become 'lost'", d))
		return
	end

	Trigger.AfterDelay(1, tick)
end
