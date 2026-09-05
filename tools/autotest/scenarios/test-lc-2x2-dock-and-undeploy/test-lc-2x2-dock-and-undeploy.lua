-- AUTO TEST — the 2x2 Logistics Centre: geometry, the crane dock, and force-move undeploy.
--
-- Three phases in one run, in this order, each with its own tick budget and its own diagnosis on
-- timeout. The camera moves with the phase so the screenshots are of the thing being asserted.
--
--   D (deploy)   An LCCV deploys. The Centre that appears must be TWO cells square: its Location is
--                the LCCV's cell + (-1,-1) and its CenterPosition is that cell's centre + (512,512).
--                Those two numbers together ARE Dimensions 2,2 — BuildingInfo.CenterOffset is
--                (CenterOfCell(D) - CenterOfCell(1,1)) / 2, so a 3x3 would read +1024 and a 2x2
--                reads +512. There is no Lua binding for a footprint, and this is exact rather than
--                a proxy for one.
--
--   K (dock)     A dry, damaged abrams is sent to the pre-placed Centre and must end up ON THE DOCK
--                CELL — the bottom-left of the footprint, under the crane — and be both repaired and
--                rearmed there. This is the phase that fails if ResupplyDock is missing or its
--                offset is wrong: Resupply's arrival test for this errand is WDist.Zero, and the
--                2x2's own centre is a cell corner nothing can stand on, so without the dock the
--                tank walks to the depot, never satisfies the test, and re-plans forever.
--
--   U (undeploy) Force-move (Ctrl) on ground undeploys the second Centre into an LCCV carrying its
--                supply and its health percentage, which then drives to the clicked cell. A PLAIN
--                click must NOT undeploy — it is still the rally-point gesture.
--
-- WHY THE MAKE DURATION IS TIMED ON THE UNDEPLOY LEG AND NOT THE DEPLOY LEG. On deploy the building
-- actor exists IMMEDIATELY; what takes the make animation's length is `build-incomplete` being held
-- (WithMakeAnimation.Forward, called from INotifyCreated.Created), and no Lua binding can observe a
-- condition. On undeploy the animation runs BEFORE the actor changes — Transform.Tick calls
-- makeAnimation.Reverse and only transforms in its callback — so ticks-from-order-to-lccv IS the
-- animation's length. It is the same `make` sequence played backwards, so timing it there measures
-- the deploy duration too. At Tick 60 that is 32 frames * 60 / 40 = 48 game ticks; the floor below
-- is set at 44, comfortably above the 32 the removed default would give and below the real 48.

local TPS = TestHarness.TicksPerSecond
local BUDGET = 60                      -- harness seconds for all three phases

-- Cells, mirroring map.yaml. Keep these and map.yaml in step.
local LCCV_CELL     = CPos.New(16, 8)
local DEPLOY_TL     = CPos.New(15, 7)  -- LCCV_CELL + Transforms.Offset (-1,-1)
local DEPOT_TL      = CPos.New(34, 16)
local DOCK_CELL     = CPos.New(34, 17) -- DEPOT_TL + (0,1): bottom-left, under the crane
local UNDEPLOY_TL   = CPos.New(50, 24)
local RESPAWN_CELL  = CPos.New(51, 25) -- UNDEPLOY_TL + Transforms.Offset (1,1)
local MOVE_TARGET   = CPos.New(58, 28)

-- Half a cell. A 2x2 building's centre sits this far down-right of its top-left cell's centre.
local HALF_CELL = 512

local MAKE_TICKS_FLOOR = 44            -- see the header: real value 48, old default 32

local DEPLOY_BUDGET   = 6 * TPS
local DOCK_BUDGET     = 30 * TPS
local UNDEPLOY_BUDGET = 20 * TPS

local phase = "deploy"
local phaseTicks = 0
local ticks = 0

-- Filled in as the run progresses so the timeout notes can report what actually happened.
local deployedLc = nil
local undeployTick = nil
local undeployedLccv = nil
local lcSupplyAtOrder, lcHealthPctAtOrder = nil, nil
local tankHpOnArrival, tankAmmoOnArrival = nil, nil
local tankDockedTick = nil
local plainClickOrder, forceClickOrder, forceCursor = nil, nil, nil
local plainClickTick = nil

local function cellsEqual(a, b) return a.X == b.X and a.Y == b.Y end

local function findByType(cell, radius, actorType)
	local found = Map.ActorsInCircle(Map.CenterOfCell(cell), WDist.FromCells(radius), function(a)
		return a.Type == actorType
	end)
	return found[1]
end

local function healthPct(a)
	return math.floor(a.Health * 100 / a.MaxHealth)
end

local function ammoOf(a)
	-- AmmoCount is a METHOD taking a POOL NAME, and the abrams' only rearmable pool is
	-- "primary-ammo" (Ammo 40, ReloadCount 5). Passing the wrong name throws a LuaException rather
	-- than returning zero, so a rename would surface as a script error, not a silent false pass.
	return a.AmmoCount("primary-ammo")
end

-- ---------------------------------------------------------------- phase D: deploy

local function tickDeploy()
	if deployedLc == nil then
		deployedLc = findByType(DEPLOY_TL, 3, "logisticscenter")
		if deployedLc == nil then
			if phaseTicks >= DEPLOY_BUDGET then
				return string.format(
					"fail: D(deploy) — the LCCV at %d,%d never produced a logisticscenter within %d " ..
					"ticks. Nothing about 2x2 is under test yet; the deploy path itself is broken, or " ..
					"the footprint no longer fits on bare ground (Transforms.CanDeploy -> " ..
					"CanPlaceBuilding). Its activity chain: %s",
					LCCV_CELL.X, LCCV_CELL.Y, DEPLOY_BUDGET,
					LccvDeploy.IsDead and "<dead>" or Test.ActivityChain(LccvDeploy))
			end
			return false
		end

		-- Geometry, checked the moment it exists.
		if not cellsEqual(deployedLc.Location, DEPLOY_TL) then
			return string.format(
				"fail: D(deploy) — the Centre's top-left is %d,%d, expected %d,%d. Transforms.Offset " ..
				"on LCCV is -1,-1 and Transform spawns at self.Location + Offset, so this says the " ..
				"offset changed. If it did, LOGISTICSCENTER's inverse Transforms.Offset (1,1) is now " ..
				"wrong too and undeploy will put the truck on the wrong cell",
				deployedLc.Location.X, deployedLc.Location.Y, DEPLOY_TL.X, DEPLOY_TL.Y)
		end

		local want = Map.CenterOfCell(DEPLOY_TL)
		local got = deployedLc.CenterPosition
		local dx, dy = got.X - want.X, got.Y - want.Y
		if dx ~= HALF_CELL or dy ~= HALF_CELL then
			return string.format(
				"fail: D(deploy) — the Centre's centre is offset %d,%d from its top-left cell's " ..
				"centre, expected %d,%d. BuildingInfo.CenterOffset is (CenterOfCell(Dimensions) - " ..
				"CenterOfCell(1,1))/2, so %d,%d means Dimensions 3,3 (the pre-2026-09-05 footprint) " ..
				"and anything else means Dimensions or LocalCenterOffset was changed. The dock offset " ..
				"(-512,512) and the HitShape are both measured from this point, so they are wrong too",
				dx, dy, HALF_CELL, HALF_CELL, 2 * HALF_CELL, 2 * HALF_CELL)
		end

		TestHarness.Screenshot("2-idle-2x2",
			"expects: the deployed Logistics Centre, two cells square, art filling its footprint " ..
			"with no overhang; crane at the bottom-left, still")
		phase = "dock"
		phaseTicks = 0
		TestHarness.FocusBetween(Depot, Tank)
		TestHarness.Select(Tank)
		-- Damage it here rather than at WorldLoaded so the repair is unambiguously part of THIS
		-- errand, and send it. IssueResupplyAt names the host and mirrors AmmoPool.AutoRearm's
		-- docking branch exactly, including the dock-tight WDist.Zero tolerance that is the whole
		-- difficulty at an even-dimensioned building.
		Tank.Health = math.floor(Tank.MaxHealth / 2)
		tankHpOnArrival = nil
		Test.IssueResupplyAt(Tank, Depot)
		return false
	end

	return false
end

-- ---------------------------------------------------------------- phase K: dock + crane

local function tickDock()
	if Tank.IsDead then
		return "fail: K(dock) — the tank died on the way to the Centre; this run measured nothing"
	end

	if not cellsEqual(Depot.Location, DEPOT_TL) then
		return string.format(
			"fail: K(dock) — the pre-placed Centre's top-left is %d,%d but map.yaml puts it at %d,%d, " ..
			"so DOCK_CELL is computed from the wrong corner and this phase is measuring nothing. " ..
			"Keep the constants at the top of this file in step with map.yaml",
			Depot.Location.X, Depot.Location.Y, DEPOT_TL.X, DEPOT_TL.Y)
	end

	local onDock = cellsEqual(Tank.Location, DOCK_CELL)

	if tankDockedTick == nil and onDock then
		tankDockedTick = ticks
		tankHpOnArrival = Tank.Health
		tankAmmoOnArrival = ammoOf(Tank)
		-- One second in, so the crane has been running and the repair/rearm is visibly under way
		-- rather than caught on its first frame.
		TestHarness.ScreenshotAfter(1, "3-docked-crane",
			"expects: the abrams parked ON the bottom-left cell of the Centre, under the crane, and " ..
			"the crane arm MID-MOTION (compare with 2-idle-2x2, where it is still). A tank sitting " ..
			"one cell short, or beside the building, is the dock offset being wrong")
	end

	if tankDockedTick ~= nil then
		local gainedHp = Tank.Health > tankHpOnArrival
		local gainedAmmo = ammoOf(Tank) > tankAmmoOnArrival
		if gainedHp and gainedAmmo then
			phase = "undeploy"
			phaseTicks = 0
			TestHarness.FocusBetween(UndeployDepot, UndeployDepot)
			TestHarness.Select(UndeployDepot)
			return false
		end
	end

	if phaseTicks >= DOCK_BUDGET then
		if not onDock and tankDockedTick == nil then
			return string.format(
				"fail: K(dock) — THE DEFECT THIS PHASE EXISTS FOR. The tank is at %d,%d after %d " ..
				"ticks and never stood on the dock cell %d,%d. Resupply sends this errand to " ..
				"CellContaining(host.CenterPosition + ResupplyDock.Offset) and tests arrival by cell " ..
				"equality against the same point, so either LOGISTICSCENTER has no ResupplyDock (in " ..
				"which case it is being sent to a 2x2's centre, a cell CORNER no ground unit can " ..
				"occupy, and will re-plan forever) or the offset does not name a footprint cell that " ..
				"is passable and stoppable — '=' is, '+' and 'x' are not. Its activity chain: %s",
				Tank.Location.X, Tank.Location.Y, phaseTicks, DOCK_CELL.X, DOCK_CELL.Y,
				Test.ActivityChain(Tank))
		end

		return string.format(
			"fail: K(dock) — the tank DID reach the dock cell %d,%d (at tick %d) but was not served " ..
			"there within %d ticks: hp %d -> %d, ammo %d -> %d, depot supply %d. Arrival is fine, so " ..
			"this is the SERVICE half: RepairsUnits/SupplyProvider paused (build-incomplete or " ..
			"disabled), the depot drained, or the player out of cash for the repair. Expect the crane " ..
			"in screenshot 3-docked-crane to be STILL if this is what failed",
			DOCK_CELL.X, DOCK_CELL.Y, tankDockedTick, DOCK_BUDGET,
			tankHpOnArrival, Tank.Health, tankAmmoOnArrival, ammoOf(Tank), Test.GetSupply(Depot))
	end

	return false
end

-- ---------------------------------------------------------------- phase U: undeploy

local function tickUndeploy()
	-- Step 1: the PLAIN click, issued for real, and then a pause to let it act if it is going to.
	if plainClickOrder == nil then
		-- Resolved through the real targeter chain, not by naming an order. Test.IssueMove would skip
		-- the OrderPriority contest and pass while a player's actual click routed somewhere else —
		-- and it sends the string "ForceMove", which is Mobile's and which this building ignores.
		plainClickOrder = Test.ClickOrderAtCell(UndeployDepot, MOVE_TARGET, "") or "<refused>"
		if plainClickOrder == "UndeployMove" then
			return "fail: U(undeploy) — a PLAIN click on ground with the Centre selected produced " ..
				"\"UndeployMove\". RequiresForceMove is what should make TransformsIntoMobile decline " ..
				"without Ctrl; without it every ordinary click on the map dismantles the building. " ..
				"(The expected answer is \"SetRallyPoint\" — RallyPoint is OrderPriority 0 and picks " ..
				"the click up once TransformsIntoMobile has declined it.)"
		end
		plainClickTick = ticks
		return false
	end

	-- Give the plain click a clear run at doing damage before the real gesture is issued.
	if forceClickOrder == nil and ticks - plainClickTick < 15 then
		return false
	end

	if undeployTick == nil then
		if UndeployDepot.IsDead then
			return string.format(
				"fail: U(undeploy) — the Centre is gone 15 ticks after a PLAIN click on ground, which " ..
				"resolved to %q. Whatever that order was, it undeployed the building; only Ctrl+click " ..
				"is supposed to",
				plainClickOrder)
		end

		forceCursor = Test.ClickCursorAtCell({ UndeployDepot }, MOVE_TARGET, "Ctrl")
		forceClickOrder = Test.ClickOrderAtCell(UndeployDepot, MOVE_TARGET, "Ctrl")
		if forceClickOrder ~= "UndeployMove" then
			return string.format(
				"fail: U(undeploy) — Ctrl+click on ground produced %q, expected \"UndeployMove\" " ..
				"(cursor was %q). The order string is deliberately NOT \"Move\": bot modules build " ..
				"new Order(\"Move\", actor, ...) by hand and TransformsIntoMobile.ResolveOrder does " ..
				"not consult RequiresForceMove, so a shared name would let a bot undeploy a Centre. " ..
				"If this says \"Move\", TransformsIntoMobile.OrderName was dropped from the rules",
				tostring(forceClickOrder), tostring(forceCursor))
		end

		undeployTick = ticks
		lcSupplyAtOrder = Test.GetSupply(UndeployDepot)
		lcHealthPctAtOrder = healthPct(UndeployDepot)
		TestHarness.ScreenshotAfter(1, "4-undeploy-mid-reverse",
			"expects: the Centre PART-WAY through its make animation played backwards — a partly " ..
			"dismantled building, not a finished one and not an empty cell")
		return false
	end

	if undeployedLccv == nil then
		undeployedLccv = findByType(RESPAWN_CELL, 2, "lccv")
		if undeployedLccv == nil then
			if phaseTicks >= UNDEPLOY_BUDGET then
				return string.format(
					"fail: U(undeploy) — %d ticks after the accepted UndeployMove order there is still " ..
					"no lccv near %d,%d, and the Centre is %s. Transform plays the make animation " ..
					"backwards first and only then swaps the actor, so a Centre still standing means " ..
					"the animation never finished (or Transforms is paused: disabled / " ..
					"build-incomplete / being-captured / being-demolished)",
					phaseTicks, RESPAWN_CELL.X, RESPAWN_CELL.Y,
					UndeployDepot.IsDead and "gone" or "still standing")
			end
			return false
		end

		local elapsed = ticks - undeployTick
		if elapsed < MAKE_TICKS_FLOOR then
			return string.format(
				"fail: U(undeploy) — the undeploy took only %d ticks; the make animation is configured " ..
				"for 48 (32 factmake frames at sequence Tick 60, and Animation.Tick advances by a " ..
				"FIXED 40 ms per game tick regardless of the mod's 60 ms Timestep, so ticks = " ..
				"32 * Tick / 40). Anything at or near 32 means the Tick line was removed from the " ..
				"`make` sequence and the animation is back on the 40 ms default — deploy is that fast " ..
				"too, since both legs play this one sequence",
				elapsed)
		end

		if not cellsEqual(undeployedLccv.Location, RESPAWN_CELL) then
			return string.format(
				"fail: U(undeploy) — the lccv appeared at %d,%d, expected %d,%d. LOGISTICSCENTER's " ..
				"Transforms.Offset must be the exact inverse of LCCV's (-1,-1 in, 1,1 out) so a " ..
				"deploy-then-undeploy round trip leaves the truck where it started. Note the expected " ..
				"cell is the footprint's bottom-RIGHT and deliberately not the dock cell, which may " ..
				"have a vehicle parked on it",
				undeployedLccv.Location.X, undeployedLccv.Location.Y, RESPAWN_CELL.X, RESPAWN_CELL.Y)
		end

		local carried = Test.GetSupply(undeployedLccv)
		if carried ~= lcSupplyAtOrder then
			return string.format(
				"fail: U(undeploy) — the Centre held %d supply at the moment of the order and the lccv " ..
				"carries %d. SupplyProvider.ITransformActorInitModifier is supposed to hand the " ..
				"remainder across in BOTH directions (it is what makes a half-spent truck deploy into " ..
				"a half-spent Centre); a full 2250 here means the new actor fell back to TotalSupply " ..
				"and the player was handed free supply for undeploying",
				lcSupplyAtOrder, carried)
		end

		local pct = healthPct(undeployedLccv)
		if math.abs(pct - lcHealthPctAtOrder) > 1 then
			return string.format(
				"fail: U(undeploy) — the Centre was at %d%% health and the lccv is at %d%%. Transform " ..
				"carries health as a PERCENTAGE (HealthInit), so a damaged Centre must not undeploy " ..
				"into a fresh truck",
				lcHealthPctAtOrder, pct)
		end

		TestHarness.Screenshot("5-undeployed-lccv",
			"expects: an LCCV standing on the cell the Centre's bottom-right used to be, driving off " ..
			"toward 58,28 — and no building left behind")
		return false
	end

	if undeployedLccv.IsDead then
		return "fail: U(undeploy) — the undeployed lccv died before reaching the clicked cell"
	end

	if cellsEqual(undeployedLccv.Location, MOVE_TARGET) then
		return true
	end

	if phaseTicks >= UNDEPLOY_BUDGET then
		return string.format(
			"fail: U(undeploy) — the lccv exists but is at %d,%d rather than the clicked cell %d,%d " ..
			"after %d ticks. The Move was queued onto the transformed actor by " ..
			"IssueOrderAfterTransform, so this is the handover half failing, not the undeploy: the " ..
			"truck should drive off by itself with no second order. Its activity chain: %s",
			undeployedLccv.Location.X, undeployedLccv.Location.Y, MOVE_TARGET.X, MOVE_TARGET.Y,
			phaseTicks, Test.ActivityChain(undeployedLccv))
	end

	return false
end

-- ---------------------------------------------------------------- driver

WorldLoaded = function()
	TestHarness.FocusBetween(LccvDeploy, LccvDeploy)
	TestHarness.Select(LccvDeploy)

	TestHarness.Screenshot("0-pre-deploy",
		"expects: the LCCV alone on 16,8, nothing built yet — the before shot for the half-cell " ..
		"question, since a 2x2's centre is the CORNER up-left of the truck's cell")

	-- The command bar's Deploy button, through IIssueDeployOrder, exactly as a player takes it.
	Test.IssueDeploy(LccvDeploy)

	-- Mid-make: roughly half of the configured 48 ticks.
	Trigger.AfterDelay(24, function()
		TestHarness.Screenshot("1-mid-make",
			"expects: the make animation part-built. JUDGE THE HALF-CELL JUMP HERE — the animation " ..
			"draws at the building's centre, which is the corner up-left of where the truck sprite " ..
			"was in 0-pre-deploy")
	end)

	TestHarness.AssertWithin(BUDGET, function()
		ticks = ticks + 1
		phaseTicks = phaseTicks + 1

		if phase == "deploy" then return tickDeploy() end
		if phase == "dock" then return tickDock() end
		return tickUndeploy()
	end, function()
		return string.format(
			"LC 2x2 assertions unresolved within %ds — stalled in phase %q at tick %d (phase tick " ..
			"%d). deployedLc=%s tankCell=%s dockedAt=%s undeployOrderTick=%s lccv=%s",
			BUDGET, phase, ticks, phaseTicks,
			deployedLc ~= nil and "yes" or "no",
			Tank.IsDead and "<dead>" or (Tank.Location.X .. "," .. Tank.Location.Y),
			tostring(tankDockedTick), tostring(undeployTick),
			undeployedLccv ~= nil and "yes" or "no")
	end)
end
