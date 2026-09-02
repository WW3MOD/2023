-- AUTO TEST: the mid-errand re-pick is LEASHED. A dry soldier whose destination stops being
-- affordable while he is walking to it must not set off across the map to the next affordable
-- host; he must give up, raise NeedsResupply and go home.
--
-- WHAT IS BEING PINNED, and why it needed pinning. SeekSupplyProvider.FindBest (:143-161) filters
-- candidate hosts on SupplyHuntMath.WithinCellBudget against AmmoPool.ResolveSeekLeash, measured
-- from the unit's CURRENT cell. That filter was added in 0dcbd235 and NOTHING TESTS IT:
-- test-dry-seeks-affordable-cache stays green with the whole conjunct reverted, and the
-- implementer said so in that scenario's own header (:44-49) and named this missing scenario.
-- Every other dispatcher bounds the walk it orders — AutoSeekSupplies.WithinBreakOffLeash, and
-- AmmoPool's Auto arm and evacuate detour via ResolveSeekLeash. The re-pick was the one that did
-- not, and it became routine the moment TargetValid tightened from "has any stock" to "can afford
-- a batch": the failing band widened from exactly-zero to 1..batchPrice-1, which for the mortar's
-- 40-supply batch is 39 cells wide.
--
-- WHY THE FAILURE MODE IS WORSE THAN IT LOOKS: a unit that walks off to a host it will take
-- minutes to reach is not visibly broken. It reads to a player as a unit that will not fight, i.e.
-- as balance, and AutoSeekSupplies' stall guard cannot catch it because the unit IS changing cells
-- and the progress test keeps resetting the counter.
--
-- THE EXPERIMENT, in one line: open the errand toward a cache the leash allows, take that cache's
-- affordability away mid-walk, and watch what the re-pick reaches for.
--
-- THE OBSERVABLE IS THE TARGET LINE, NOT THE DISTANCE, and that choice is the difference between a
-- gate and a decoration. Test.GetAutomaticTargetLineCells walks the activity chain and returns the
-- cells of nodes painted in AutomaticOrder.LineColor — for this unit that IS
-- SeekSupplyProvider.TargetLineNodes (:288-294), which yields Target.FromActor(currentTarget) while
-- hunting and Target.FromCell(origin) once `returning` is set. So the binding reports the
-- activity's own currentTarget field, one layer from FindBest's return value, on the tick it
-- changes. Distance cannot do that job: a mortar covers about one cell per 50 ticks (measured in
-- test-dry-seeks-affordable-cache), so an unleashed re-pick aimed 52 cells away would move him
-- only ~8 cells inside any sane window — a signal an ORDERING assertion could report as "he moved
-- toward it a bit" and a magnitude assertion would miss entirely. Distance is kept as a secondary
-- diagnostic and is never the verdict.
--
-- WHY THE RED SIGNAL IS ATTRIBUTABLE TO FindBest AND TO NOTHING ELSE. FarCache sits 52-75 cells
-- from every cell the mortar occupies, against a 30-cell budget in all three dispatchers (idle seek
-- 20, break-off arm 30, AmmoPool Auto arm ResolveSeekLeash 30). With FindBest leashed there is no
-- path in the codebase that can name FarCache; with its leash reverted there is exactly one. A
-- target line pointing at FarCache therefore cannot have come from anywhere else.
--
-- AND WHY THE GREEN SIGNAL IS ATTRIBUTABLE. `returning` is set only in BeginReturn, which has two
-- callers: the currentTarget == null path (FindBest found nothing — the outcome under test) and
-- ErrandIsPointless(). The second is excluded by MEASUREMENT rather than by argument: it needs
-- either all pools full or SelfAssignedErrandIsOver, both of which require ammunition to have
-- arrived, and the run asserts the mortar's ammo is 0 on every single poll. That assertion is not
-- housekeeping — it is what earns the right to read a home-bound target line as "FindBest returned
-- null".
--
-- WHAT WOULD MAKE THIS PASS WHILE PROVING NOTHING. One shape, and it is the obvious one: if the
-- errand never opens, or the re-pick never fires, the mortar simply never goes near FarCache and
-- every assertion above is satisfied by a unit standing still. Three gates exist for that and all
-- three must be TRUE before the verdict is trusted — the target line was seen pointing at NearCache
-- (the errand existed and was aimed where we think), the mortar moved at least
-- MinCellsEastBeforeDrain cells east of his origin (he actually set off, and is well clear of the
-- 5c0 push aura so the re-pick is a real choice), and after the drain the target line CHANGED to a
-- cell that is neither cache (BeginReturn ran, so FindBest was consulted and answered null). A run
-- missing any of them reports SETUP INVALID and never reports a pass.
--
-- EXECUTION MARKER. Every verdict begins with `leash/` and carries `obs<n>` and `arm<n>`; WorldLoaded
-- prints `[leash] loaded` to lua.log before touching anything, and the drain prints `[leash] drained`.
-- A Lua that aborts at load also writes status:fail, so without these a never-executed run is
-- indistinguishable from a real RED. A fail verdict not beginning with `leash/` is VOID: check
-- lua.log is non-empty and that map.yaml still ends with its `Rules: rules.yaml` line.

-- Ticks throughout, never TestHarness seconds. TestHarness.TicksPerSecond is 25 against a mod
-- Timestep of 60 (16.67 tps), so every "seconds" window in this suite is 1.5x what it claims;
-- polling with Trigger.AfterDelay(1, ...) is immune to that and to game speed (AUTOTEST.md).
local ArmDeadlineTicks = 1200
local ObserveTicks = 400

-- He starts 23 cells from NearCache, so 3 cells east is 20 from it — four times the 5c0 push aura,
-- and unambiguously "walking" rather than "settling into the cell he spawned in".
local MinCellsEastBeforeDrain = 3

-- 45: affordable against the mortar's 40-supply batch, and it survives paying (RemoveBelowSupply 1).
local AffordableLoad = 45
-- 39: ONE SHORT. Stocked, and unable to serve. The boundary is the measurement — a run cannot pass
-- by being generous, because the cache is rejected on price alone and by a single unit of supply.
local PoorLoad = 39
local FarLoad = 200

-- How much closing on FarCache counts as "set off across the map", for the secondary distance
-- check. 6 cells is ~300 ticks of mortar walking, far more than drift or a repath wobble.
local FarApproachSlackCells = 6

local AmmoPoolName = "primary-ammo"

local S = {
	armTicks = 0,
	observed = 0,
	drained = false,
	sawNearLine = false,
	sawFarLine = false,
	sawReturnLine = false,
	returnLineCell = "-",
	maxEast = 0,
	eastAtDrain = -1,
	minDistFar = 99999,
	startDistFar = -1,
	distFarAtDrain = -1,
	peakAmmo = 0,
	nearSupplyMin = 99999,
	nearSupplyMax = -1,
	ticksToRepick = -1,
	faults = {},
}

local originCell, nearCell, farCell

local function addFault(s)
	table.insert(S.faults, s)
end

local function chessboard(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	if dx > dy then return dx end
	return dy
end

local function sameCell(a, b)
	return a.X == b.X and a.Y == b.Y
end

-- Classify this tick's automatic target-line cells. Returns nothing; folds into S.
local function readTargetLine()
	local cells = Test.GetAutomaticTargetLineCells(Gunner)
	for _, c in ipairs(cells) do
		if sameCell(c, nearCell) then
			S.sawNearLine = true
		elseif sameCell(c, farCell) then
			S.sawFarLine = true
		elseif S.drained then
			-- Neither cache. On this map the only other node SeekSupplyProvider can draw is
			-- Target.FromCell(origin) under `returning`, which is the BeginReturn signature.
			S.sawReturnLine = true
			if S.returnLineCell == "-" then
				S.returnLineCell = c.X .. "," .. c.Y
				S.ticksToRepick = S.observed
			end
		end
	end
end

local function summary()
	return table.concat({
		"leash/",
		"arm" .. S.armTicks,
		"obs" .. S.observed,
		"nearLine" .. tostring(S.sawNearLine),
		"FARLINE" .. tostring(S.sawFarLine),
		"returnLine" .. tostring(S.sawReturnLine) .. "@" .. S.returnLineCell,
		"ticksToRepick" .. S.ticksToRepick,
		"eastAtDrain" .. S.eastAtDrain,
		"maxEast" .. S.maxEast,
		"startDistFar" .. S.startDistFar,
		"distFarAtDrain" .. S.distFarAtDrain,
		"minDistFar" .. S.minDistFar,
		"peakAmmo" .. S.peakAmmo,
		"nearSupply" .. S.nearSupplyMin .. ".." .. S.nearSupplyMax,
	}, " ")
end

local function finish()
	local sum = summary()

	-- Setup faults that make the OUTCOME unreadable are collected here rather than asserted inline,
	-- so the verdict can put the leash result first when both are present.
	if not S.sawNearLine then
		addFault("the automatic target line never pointed at NearCache - the SeekSupplyProvider errand"
			.. " never opened, so no re-pick was ever consulted and the leash was never asked anything")
	end
	if S.peakAmmo > 0 then
		addFault("the mortar received " .. S.peakAmmo .. " rounds - a cache paid him, so ErrandIsPointless"
			.. " could end the errand on its own and a home-bound target line no longer implies FindBest"
			.. " returned null")
	end
	if S.nearSupplyMax > PoorLoad then
		addFault("NearCache held " .. S.nearSupplyMax .. " after the drain, above the " .. PoorLoad
			.. " it was set to - it never entered the stocked-but-unaffordable band, so TargetValid"
			.. " stayed true and the re-pick never fired")
	end
	if not S.sawReturnLine then
		addFault("the target line never left NearCache after the drain - the re-pick did not resolve"
			.. " within " .. ObserveTicks .. " ticks, so this run did not reach the decision under test")
	end

	-- LEASH VERDICT FIRST. In the RED arm the mortar also ends the run far from home and dry, which
	-- would otherwise surface as a setup fault and bury the mechanism.
	if S.sawFarLine then
		Test.Fail("LEASH DID NOT HOLD: the re-pick targeted FarCache at "
			.. farCell.X .. "," .. farCell.Y .. ", " .. S.distFarAtDrain
			.. " cells away against a 30-cell budget - FindBest returned a host outside"
			.. " AmmoPool.ResolveSeekLeash || " .. sum)
		return
	end

	if S.minDistFar <= S.startDistFar - FarApproachSlackCells then
		Test.Fail("LEASH DID NOT HOLD: the mortar closed to " .. S.minDistFar
			.. " cells of FarCache from " .. S.startDistFar
			.. " without its cell ever appearing on the target line - he is walking there by some"
			.. " path this scenario has not accounted for; read the [leash] trace before trusting"
			.. " either verdict || " .. sum)
		return
	end

	if #S.faults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(S.faults, "; ") .. " || " .. sum)
		return
	end

	Test.Pass(sum)
end

local function observePhase()
	local step
	step = function()
		if Gunner.IsDead then
			-- Out of world reads as dead, so this catches an evacuation departure too. Either is a
			-- different disposition and must not be read as "the leash held".
			Test.Fail("SETUP INVALID: the mortar left the world during the observation window -"
				.. " he died or evacuated for a refund, and this run measured neither the leash nor"
				.. " the re-pick || " .. summary())
			return
		end

		readTargetLine()

		local ammo = Gunner.AmmoCount(AmmoPoolName)
		if ammo > S.peakAmmo then S.peakAmmo = ammo end

		local near = Test.GetSupply(NearCache)
		if near < S.nearSupplyMin then S.nearSupplyMin = near end
		if near > S.nearSupplyMax then S.nearSupplyMax = near end

		local east = Gunner.Location.X - originCell.X
		if east > S.maxEast then S.maxEast = east end

		local d = chessboard(Gunner.Location, farCell)
		if d < S.minDistFar then S.minDistFar = d end

		S.observed = S.observed + 1

		if S.observed % 50 == 0 then
			-- Live counters go in a print, never interpolated into a failure string: Lua evaluates
			-- those eagerly at registration and would report the pre-run zeros for the whole run.
			print(string.format(
				"[leash] obs=%d cell=%d,%d east=%d dFar=%d ammo=%d nearSupply=%d farLine=%s returnLine=%s@%s",
				S.observed, Gunner.Location.X, Gunner.Location.Y, east, d, ammo, near,
				tostring(S.sawFarLine), tostring(S.sawReturnLine), S.returnLineCell))
		end

		if S.observed >= ObserveTicks then
			finish()
		else
			Trigger.AfterDelay(1, step)
		end
	end
	Trigger.AfterDelay(1, step)
end

-- Phase A: wait until the errand is demonstrably live and aimed at NearCache AND the mortar has
-- actually walked, then take NearCache's affordability away. Keying the drain on both facts is
-- what makes "mid-errand" true rather than assumed.
local function armPhase()
	local step
	step = function()
		if Gunner.IsDead then
			Test.Fail("SETUP INVALID: the mortar left the world before the drain - no errand was ever"
				.. " observed, so nothing about the re-pick was measured || " .. summary())
			return
		end

		S.armTicks = S.armTicks + 1
		readTargetLine()

		local ammo = Gunner.AmmoCount(AmmoPoolName)
		if ammo > S.peakAmmo then S.peakAmmo = ammo end

		local east = Gunner.Location.X - originCell.X
		if east > S.maxEast then S.maxEast = east end

		if S.sawNearLine and east >= MinCellsEastBeforeDrain then
			Test.SetSupply(NearCache, PoorLoad)

			-- SAME-TICK CONTROL. Read the load back on the tick the clock starts rather than only
			-- at the end: a leash result is meaningless if the cache was still affordable at the
			-- moment the re-pick was supposed to reject it.
			local back = Test.GetSupply(NearCache)
			if back ~= PoorLoad then
				Test.Fail("SETUP INVALID: NearCache reads " .. back .. " immediately after being set to "
					.. PoorLoad .. " - the drain did not take, TargetValid stays true and no re-pick"
					.. " will ever fire || " .. summary())
				return
			end

			S.drained = true
			S.eastAtDrain = east
			S.distFarAtDrain = chessboard(Gunner.Location, farCell)
			S.minDistFar = S.distFarAtDrain
			S.nearSupplyMin = back
			S.nearSupplyMax = back

			print(string.format(
				"[leash] drained armTicks=%d cell=%d,%d east=%d nearSupply=%d dFar=%d dNear=%d ammo=%d",
				S.armTicks, Gunner.Location.X, Gunner.Location.Y, east, back, S.distFarAtDrain,
				chessboard(Gunner.Location, nearCell), ammo))

			observePhase()
			return
		end

		if S.armTicks >= ArmDeadlineTicks then
			-- Name WHICH precondition was missing. "He never set off" and "he set off toward
			-- something else" are different bugs and only one of them is about this leash.
			local why
			if not S.sawNearLine then
				why = "the automatic target line never pointed at NearCache in " .. ArmDeadlineTicks
					.. " ticks - no SeekSupplyProvider errand was opened toward it. Check NearCache is"
					.. " still outside the idle seek's 20-cell leash and inside the break-off arm's 30"
			else
				why = "the mortar never got " .. MinCellsEastBeforeDrain .. " cells east of his origin in "
					.. ArmDeadlineTicks .. " ticks despite being aimed at NearCache - he is aimed but not"
					.. " walking, which is a movement bug and not a leash result"
			end
			Test.Fail("SETUP INVALID: " .. why .. " || " .. summary())
			return
		end

		Trigger.AfterDelay(1, step)
	end
	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	-- Printed before anything else can throw: this line in lua.log is the proof the script loaded.
	print("[leash] loaded armDeadline=" .. ArmDeadlineTicks .. " observe=" .. ObserveTicks
		.. " poorLoad=" .. PoorLoad .. " minEast=" .. MinCellsEastBeforeDrain)

	TestHarness.FocusBetween(Gunner, NearCache)
	TestHarness.Select(Gunner)

	originCell = Gunner.Location
	nearCell = NearCache.Location
	farCell = FarCache.Location

	Test.SetSupply(NearCache, AffordableLoad)
	Test.SetSupply(FarCache, FarLoad)

	-- Guard the guards. A silently failed binding would leave both caches on their shipped load,
	-- both affordable, and the run would measure nothing while still writing a verdict.
	local nearNow = Test.GetSupply(NearCache)
	local farNow = Test.GetSupply(FarCache)
	if nearNow ~= AffordableLoad or farNow ~= FarLoad then
		Test.Fail("leash/ setup failed: caches hold " .. nearNow .. " (near) and " .. farNow
			.. " (far), expected " .. AffordableLoad .. " and " .. FarLoad
			.. " - Test.SetSupply did not take and the run never staged its premise")
		return
	end

	local startingAmmo = Gunner.AmmoCount(AmmoPoolName)
	if startingAmmo ~= 0 then
		Test.Fail("leash/ setup failed: the mortar starts with " .. startingAmmo
			.. " rounds, so he is not dry, no dispatcher runs and no errand is ever opened")
		return
	end

	S.startDistFar = chessboard(originCell, farCell)
	S.minDistFar = S.startDistFar

	-- The premise this scenario rests on, asserted rather than assumed. ResolveSeekLeash returns
	-- DryRearmLeashCells (30) for a wholly dry unit; if FarCache were ever inside that, the leash
	-- would legitimately admit it and a green would mean nothing.
	if S.startDistFar <= 30 then
		Test.Fail("leash/ setup failed: FarCache is " .. S.startDistFar
			.. " cells away, inside the 30-cell DryRearmLeashCells budget - the leash is SUPPOSED to"
			.. " admit it there, so this geometry cannot test a refusal. Move FarCache east.")
		return
	end

	armPhase()
end
