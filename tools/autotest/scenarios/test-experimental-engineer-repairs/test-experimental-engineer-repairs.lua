-- AUTO TEST: does the @experimental bot EMPLOY its combat engineer, or just park him?
--
-- THE GAP. The bot buys engineers — e6 sits at UnitsToBuild 20 / UnitTargetShares 8 / UnitLimits 2 on
-- both faction UnitBuilder twins — and then never uses one. ^E6 carries `AIUnitRole: Role: Logistics`
-- (infantry.yaml, ^E6) and every free-pool module sets `UseUnitRoles: true`, so the role filter
-- excludes him from assault, ambush, garrison and line duty alike. The only thing that ever moved an
-- engineer before EngineerOperatorBotModule@experimental was EngineerRouteOpenBotModule's bridge
-- trigger, which fires only when a repairable crossing happens to sit in the believed-weakest enemy
-- sector. Everywhere else he holds three C4 charges, a repair armament and a mine detector, at the
-- Supply Route, for the whole match.
--
-- THE VERDICT IS HIT POINTS, NOT POSITION, AND THAT IS THE POINT. Nothing else on this map can heal a
-- vehicle: no logisticscenter is placed (so `Repairable: RepairActors: logisticscenter` has no
-- provider), vehicles carry no RepairableBuilding anywhere in the mod, and the only healing weapon in
-- reach is Armament@Repair on the engineer himself. That weapon has Range: 1c0 — ONE cell
-- (weapons-other.yaml:368-370) — so a single point of health recovered PROVES he walked the four
-- cells and parked adjacent. A position check would be the weaker assertion, not the stronger one:
-- an engineer standing next to the casualty for an unrelated reason would satisfy it.
--
-- THE DISCRIMINATOR IS THE PAIR OF RIFLEMEN in the south-east corner. They put a live SCREEN anchor
-- on the map, and screen ranks below repair in EngineerTaskingMath.ChooseEmployment. A correct module
-- walks the engineer 4 cells WEST to the casualty; one whose priority order is inverted walks him
-- ~14 cells EAST to the screen centroid and the casualty never gains a point. Without them the run
-- would prove only that the engineer took the single job on offer.
--
-- WHY THE CASUALTY SITS AT 75% AND NOT 50%. ^EffectsWhenDamagedVehicles carries
-- `ChangesHealth@CriticalDamage` with `StartIfBelow: 50` and `PercentageStep: -1`
-- (vehicles.yaml:184-187): a vehicle staged below half BLEEDS OUT, and would race the repair in the
-- opposite direction at a comparable rate. At 75% the DamageState is Light or Medium — enough for
-- GrantConditionOnDamageState@Damaged to grant `damaged` and switch on Targetable@VehicleRepair, not
-- enough to bleed.
--
-- DEADLINES ARE IN TICKS, NOT HARNESS SECONDS, PER AUTOTEST.md. This file names no harness constant
-- at all and every window in it is a tick literal, which is why the 2026-09-22 correction of
-- TestHarness.TicksPerSecond (25 -> 16.667, the engine's real rate) moved nothing here. Kept as tick
-- literals deliberately: Trigger.AfterDelay counts real ticks and is immune both to that constant and
-- to whatever game speed a run happens to use.

local EvalTicks = 1500        -- ~90s of real time; the whole sequence should finish inside ~400.
local SnapshotEvery = 100

local StageHealthPct = 75
local BleedOutBelowPct = 50   -- ChangesHealth@CriticalDamage StartIfBelow, vehicles.yaml:186
local RepairRangeCells = 1    -- Repair weapon Range: 1c0, weapons-other.yaml:370

local pollCount = 0
local startHealth = 0
local stagedCasualtyCell = nil
local closestApproach = -1     -- best (smallest) sampled engineer->casualty distance; -1 until sampled

-- HOW FAR THE CASUALTY MAY DRIFT before this scenario is measuring something else entirely. Two cells:
-- large enough to absorb a pathfinder nudge, far smaller than the 10-38 cells a drafted casualty
-- covers inside the window (measured: 22,16 -> 33,16 by t300 in run 260922_063223).
local DraftedDriftCells = 2
local DraftCheckTick = 400     -- both recorded runs had an offensive axis ORDERED by t189.

local function chebyshev(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	return dx > dy and dx or dy
end

local function engineerDistance()
	if Engineer.IsDead or Casualty.IsDead then return -1 end
	return chebyshev(Engineer.Location, Casualty.Location)
end

WorldLoaded = function()
	TestHarness.FocusBetween(Engineer, Casualty)
	TestHarness.Select(Engineer)

	local maxHealth = Casualty.MaxHealth
	startHealth = math.floor(maxHealth * StageHealthPct / 100)
	Casualty.Health = startHealth

	-- THE SETUP IS NOT THE SETUP THAT RAN UNTIL IT IS READ BACK. The Health setter routes through
	-- InflictDamage, so armour modifiers, damage-type filters or a VehicleCrew interaction could all
	-- land the casualty somewhere other than where it was put. Asserting the SUBJECT rather than the
	-- config is what AUTOTEST.md means by checking the subject.
	local staged = Casualty.Health
	if staged >= maxHealth then
		Test.Fail(string.format(
			"setup failed: the casualty reads %d/%d after staging, i.e. undamaged. With no `damaged` " ..
			"condition it never presents Targetable@VehicleRepair, the repair armament has nothing to " ..
			"auto-target, and the module has no repair work to find — so a flat health line would be a " ..
			"staging failure and not a statement about employment",
			staged, maxHealth))
		return
	end

	if staged * 100 <= maxHealth * BleedOutBelowPct then
		Test.Fail(string.format(
			"setup failed: the casualty reads %d/%d, at or below the %d%% bleed-out threshold. " ..
			"ChangesHealth@CriticalDamage would then remove 1%% per step while the engineer adds 1%% " ..
			"per burst, and the run would measure the race between them rather than the employment",
			staged, maxHealth, BleedOutBelowPct))
		return
	end

	-- The screen anchor must actually exist or the discriminator is not in the run at all, and a pass
	-- would only show that repair beat NOTHING.
	if ScreenDecoyA.IsDead or ScreenDecoyB.IsDead then
		Test.Fail(
			"setup failed: a screen decoy is missing, so no screen anchor competes with the repair " ..
			"employment and a pass would not show that repair OUTRANKS screen — only that it was the " ..
			"one job available")
		return
	end

	local startDistance = engineerDistance()
	stagedCasualtyCell = Casualty.Location
	closestApproach = startDistance

	-- ── THE DRAFTED-CASUALTY GUARD, AND IT IS A PRECONDITION READ-BACK RATHER THAN A VERDICT ──
	--
	-- WHAT IT WATCHES FOR. A stock abrams resolves to UnitRole.MainBattle and PoiOffensiveBotModule's
	-- free pool accepts exactly MainBattle || IndirectFire (PoiOffensiveBotModule.cs:3230), so before
	-- the staged actors became re-roled clones the bot DRAFTED ITS OWN CASUALTY into an
	-- offensive axis and drove it at the enemy Supply Route. The engineer then chases a tank at
	-- infantry speed against a cell refreshed once per OrderSettleTicks (200) and the verdict turns on
	-- the RNG seed: run 260921_213228 passed because the casualty crossed ONTO his cell at t100, and
	-- run 260922_063223 failed because it left from 22,16 and reached 38 cells away. Same code, same
	-- mechanism, opposite verdicts.
	--
	-- WHY IT SKIPS AND DOES NOT FAIL. If this fires, the AIUnitRole override in rules.yaml did not
	-- reach the actor and NO STATEMENT ABOUT THE MODULE IS AVAILABLE from this run — the measurement
	-- apparatus is what broke. Failing here would file a module bug against a scenario fault. Same
	-- precedent as test-ambush-lane-share, which skips unless it reads its own floor back.
	--
	-- A BROKEN MAP-RULES LOAD IS SWALLOWED, which is exactly why a runtime read-back is the only
	-- honest check: Map.PostInit catches a map-rules load failure, logs `Failed to load rules` to
	-- debug.log and falls back to the tileset defaults (Map.cs:540-547). If that happens the clones
	-- `casualtyabrams` and `screendecoye3` do not exist at all; short of that, anything that puts the
	-- casualty back in a combat pool reverts this scenario to measuring a chase, silently and greenly.
	Trigger.AfterDelay(DraftCheckTick, function()
		if Casualty.IsDead then
			return      -- the deadline handler below reports this, and reports it better.
		end

		local drift = chebyshev(Casualty.Location, stagedCasualtyCell)
		if drift > DraftedDriftCells then
			Test.Skip(string.format(
				"apparatus fault, not a verdict: the casualty has moved %d cells from its staged cell " ..
				"(%d,%d -> %d,%d) by tick %d, so something recruited it and the engineer is chasing a " ..
				"moving vehicle rather than walking to a parked one. The `AIUnitRole: Role: Logistics` " ..
				"carried by this scenario's `casualtyabrams` clone did not reach the actor — check that " ..
				"map.yaml still stages the CLONE and not a stock `abrams`, and that the clone still " ..
				"carries the role (a map-rules load failure is SWALLOWED into a defaults fallback), " ..
				"then grep debug.log for `Failed to load rules`",
				drift, stagedCasualtyCell.X, stagedCasualtyCell.Y,
				Casualty.Location.X, Casualty.Location.Y, DraftCheckTick))
		end
	end)

	-- Self-rescheduling snapshot; there is no Trigger.OnTick in this engine. Live counters go HERE and
	-- never into a failure string built at registration time, which Lua would evaluate eagerly and
	-- which would therefore report the starting values forever.
	local function snapshot()
		pollCount = pollCount + SnapshotEvery

		-- CLOSEST SAMPLED APPROACH, because the FINAL distance cannot tell the two interesting
		-- outcomes apart. Run 260921_213228 ended at dist 21 and PASSED: the engineer had been at 0 at
		-- t100 and was left behind by a casualty that drove off. Run 260922_063223 ended at dist 24 and
		-- had never been closer than 2. A single end-of-run number reads those as the same run, and the
		-- old failure text — "the engineer NEVER WALKED" — was that conflation written down.
		-- SAMPLED, not continuous: a transit through repair range between two polls is invisible here,
		-- so this bounds the approach from above and the health delta stays the verdict.
		local distanceNow = engineerDistance()
		if distanceNow >= 0 and (closestApproach < 0 or distanceNow < closestApproach) then
			closestApproach = distanceNow
		end

		local casualtyCell = Casualty.IsDead and "dead" or string.format("%d,%d (drift %d)",
			Casualty.Location.X, Casualty.Location.Y,
			stagedCasualtyCell and chebyshev(Casualty.Location, stagedCasualtyCell) or -1)

		print(string.format(
			"[eng-op] tick=%d | casualty hp=%d/%d (start %d) cell=%s | engineer dist=%d (closest %d) " ..
			"c4=%d | decoys %s",
			pollCount,
			Casualty.IsDead and -1 or Casualty.Health, maxHealth, startHealth, casualtyCell,
			distanceNow, closestApproach,
			Engineer.IsDead and -1 or Engineer.AmmoCount("secondary-ammo"),
			(ScreenDecoyA.IsDead or ScreenDecoyB.IsDead) and "lost" or "alive"))

		if pollCount < EvalTicks then
			Trigger.AfterDelay(SnapshotEvery, snapshot)
		end
	end

	Trigger.AfterDelay(SnapshotEvery, snapshot)

	Trigger.AfterDelay(EvalTicks, function()
		if Engineer.IsDead then
			Test.Fail(
				"measured nothing: the engineer left the world before the deadline. Nothing on this " ..
				"map should be shooting at him, so this is a staging fault rather than a verdict")
			return
		end

		if Casualty.IsDead then
			Test.Fail(
				"measured nothing: the casualty left the world before the deadline, so no health " ..
				"trend could be read from it")
			return
		end

		local hp = Casualty.Health
		local distance = engineerDistance()
		local casualtyDrift = chebyshev(Casualty.Location, stagedCasualtyCell)
		local summary = string.format(
			"casualty hp=%d/%d (staged %d, delta %d) | engineer dist=%d (started %d, closest %d) " ..
			"c4=%d | casualty drift %d cells",
			hp, maxHealth, startHealth, hp - startHealth, distance, startDistance, closestApproach,
			Engineer.AmmoCount("secondary-ammo"), casualtyDrift)

		print("[eng-op] RESULT " .. summary)

		if hp > startHealth then
			Test.Pass(summary)
			return
		end

		-- THE THREE failures that share this one symptom, named so the next reader does not have to
		-- guess which happened. The discriminators are `closest` and the casualty's drift, NOT the
		-- final distance — which is the correction of 2026-09-22.
		--
		-- WHY THIS BRANCH SET GREW A THIRD ARM. It used to offer two ("never walked" / "arrived and
		-- did not heal") and chose between them on the FINAL distance alone, so a run in which the
		-- engineer walked correctly and the CASUALTY drove away was reported as "the engineer NEVER
		-- WALKED: ... He was not employed at all", pointing the reader at map.yaml's actor name. In run
		-- 260922_063223 that text was printed against a log showing the module ordering him every
		-- cycle and the casualty 38 cells downrange — a confident, specific, wrong diagnosis, and the
		-- triage it sent the next reader on is the cost this arm exists to avoid.
		if casualtyDrift > DraftedDriftCells then
			Test.Fail(string.format(
				"the CASUALTY LEFT: it drifted %d cells from where it was staged, so the engineer was " ..
				"chasing a moving vehicle. This is NOT a statement that he was unemployed — read the " ..
				"`[engineer] ... repair cell=` anchors in debug.log; they track the casualty. Either " ..
				"the casualtyabrams clone's AIUnitRole stopped excluding it from the combat pools (it is " ..
				"drafted after tick %d, past the read-back guard) or a module that ignores roles has " ..
				"recruited it. %s",
				casualtyDrift, DraftCheckTick, summary))
			return
		end

		if closestApproach > RepairRangeCells then
			Test.Fail(string.format(
				"the engineer NEVER CLOSED: his closest sampled approach was %d cells and Repair " ..
				"reaches %d, with the casualty parked throughout (drift %d). He was either not " ..
				"employed at all — check that map.yaml places `e6.america` and not a bare `e6`, since " ..
				"EngineerOperatorBotModuleInfo.OperatorActorTypes names the faction-suffixed types and " ..
				"a bare e6 stands still exactly like a broken module — or he was ordered ONTO the " ..
				"casualty's own cell, which he cannot enter, and stalled beside it: that was the " ..
				"shipped bug until EngineerOperatorBotModule.RepairParkAnchor, so check the " ..
				"`[engineer] ... repair cell=` anchor against the casualty's own cell first. %s",
				closestApproach, RepairRangeCells, casualtyDrift, summary))
			return
		end

		if distance > RepairRangeCells then
			Test.Fail(string.format(
				"the engineer REACHED THE CASUALTY AND LEFT AGAIN without healing it: closest approach " ..
				"%d cells, now %d away, casualty parked (drift %d). He was employed and he arrived, so " ..
				"suspect a re-task that pulled him off a completed park — the screen employment " ..
				"competing every ReevaluateInterval, or OrderSettleTicks expiring — rather than the " ..
				"armament. %s",
				closestApproach, distance, casualtyDrift, summary))
			return
		end

		Test.Fail(string.format(
			"the engineer ARRIVED AND DID NOT HEAL: he is %d cells from the casualty, inside the " ..
			"%d-cell Repair range, the casualty is parked (drift %d) and it gained nothing. The " ..
			"employment fired and the walk completed but the armament did not — suspect the " ..
			"auto-target chain (AutoTargetPriority@Repair, Armament@Repair's `PauseOnCondition: " ..
			"suppressed >= 10`, or the casualty not presenting Targetable@VehicleRepair) rather than " ..
			"the tasking module. %s",
			distance, RepairRangeCells, casualtyDrift, summary))
	end)
end
