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
-- DEADLINES ARE IN TICKS, NOT HARNESS SECONDS, PER AUTOTEST.md. TestHarness.TicksPerSecond is 25
-- while the mod runs at 16.67 ticks/s, so every "N seconds" window in this suite is really N x 1.5
-- and any duration reported in seconds is overstated by half again. Trigger.AfterDelay counts real
-- ticks and is immune to both that constant and to whatever game speed a run happens to use.

local EvalTicks = 1500        -- ~90s of real time; the whole sequence should finish inside ~400.
local SnapshotEvery = 100

local StageHealthPct = 75
local BleedOutBelowPct = 50   -- ChangesHealth@CriticalDamage StartIfBelow, vehicles.yaml:186
local RepairRangeCells = 1    -- Repair weapon Range: 1c0, weapons-other.yaml:370

local pollCount = 0
local startHealth = 0

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

	-- Self-rescheduling snapshot; there is no Trigger.OnTick in this engine. Live counters go HERE and
	-- never into a failure string built at registration time, which Lua would evaluate eagerly and
	-- which would therefore report the starting values forever.
	local function snapshot()
		pollCount = pollCount + SnapshotEvery
		print(string.format(
			"[eng-op] tick=%d | casualty hp=%d/%d (start %d) | engineer dist=%d c4=%d | decoys %s",
			pollCount,
			Casualty.IsDead and -1 or Casualty.Health, maxHealth, startHealth,
			engineerDistance(),
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
		local summary = string.format(
			"casualty hp=%d/%d (staged %d, delta %d) | engineer dist=%d (started %d) c4=%d",
			hp, maxHealth, startHealth, hp - startHealth, distance, startDistance,
			Engineer.AmmoCount("secondary-ammo"))

		print("[eng-op] RESULT " .. summary)

		if hp > startHealth then
			Test.Pass()
			return
		end

		-- The two failures that share this one symptom, named so the next reader does not have to
		-- guess which of them happened. `dist` separates them outright: the engineer either never
		-- left, or arrived and did not heal.
		if distance > RepairRangeCells then
			Test.Fail(string.format(
				"the engineer NEVER WALKED: the casualty gained nothing and he is still %d cells away " ..
				"(Repair reaches %d). He was not employed at all — the module did not order him, or " ..
				"it did not recognise him. Check that map.yaml places `e6.america` and not a bare " ..
				"`e6`: EngineerOperatorBotModuleInfo.OperatorActorTypes names the faction-suffixed " ..
				"types, and a bare e6 stands still exactly like a broken module. %s",
				distance, RepairRangeCells, summary))
			return
		end

		Test.Fail(string.format(
			"the engineer ARRIVED AND DID NOT HEAL: he is %d cells from the casualty, inside the " ..
			"%d-cell Repair range, and the casualty gained nothing. The employment fired but the " ..
			"armament did not — suspect the auto-target chain (AutoTargetPriority@Repair, " ..
			"Armament@Repair's `PauseOnCondition: suppressed >= 10`, or the casualty not presenting " ..
			"Targetable@VehicleRepair) rather than the tasking module. %s",
			distance, RepairRangeCells, summary))
	end)
end
