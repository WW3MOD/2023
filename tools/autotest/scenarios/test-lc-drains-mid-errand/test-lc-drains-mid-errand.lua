-- AUTO TEST: a Logistics Centre that runs dry UNDER a unit already walking to it must be abandoned,
-- and the unit must reach one that can actually serve it.
--
-- THE REPORT, from live play: "I noticed that units go to the LC even if it is empty, and they just
-- wait there." USER RULING, in two halves:
--   1. "Empty LC should count as no LC, as far as auto-rearming goes. Units evacuate instead if
--      there is no LC WITH SUPPLIES."
--   2. "If the supplies runs out, any unit queued to rearm there should cancel that order, and go
--      somewhere else to rearm or evacuate or whatever their stance makes them do."
--
-- This scenario is half 2, which is the one that produces the reported symptom: a unit that has
-- already committed cannot be rescued by filtering at selection time, and every unit inbound when
-- the last supplies went is stranded by a selection-only fix.
--
-- THE MECHANISM, and why the LC half specifically. AmmoPool.AutoRearm sends a unit to a
-- SupplyProvider WITHOUT a docking gate via SeekSupplyProvider, and everything else via the stock
-- Resupply activity. The LC sets DockedCondition: unit.docked, so it takes the second branch — and
-- the two branches did not agree:
--
--   * SeekSupplyProvider re-asks AmmoPool.HostCanAffordSomethingWeNeed EVERY tick of the approach
--     (its TargetValid), and SupplyHuntMath.NextState answers !providerUsable with Returning. The
--     truck/cache half has therefore handled this since the affordability work landed.
--   * Resupply re-asked only "am I still dry?" (SelfAssignedErrandIsOver). It never asked whether the
--     depot could still serve, so the unit walked the whole way to a Centre that had emptied behind
--     it, arrived, was correctly refused by Rearmable.RearmTick (which skips a pool the provider
--     cannot pay for, Rearmable.cs:106), and ended the activity standing at a useless building.
--
-- The idle re-decision then kept it there: AutoRearmIfDry's hopelessness test asked
-- AnyRearmHostWithinLeash, which swept hosts that EXIST with stock ignored, so the drained Centre it
-- was parked at counted as a reason to wait. That is HoldAndFlag, whose only payoff is NeedsResupply
-- — a flag whose sole reader drives to the flagged UNIT and can never resupply a depot. Hence "they
-- just wait there", indefinitely.
--
-- WHY THE DRAIN IS TO 5 AND NOT TO 0. AR's pool is SupplyValue 10 (infantry.yaml), so 1..9 is the
-- band where a depot is STOCKED and yet cannot serve anybody. AmmoPool.RearmCandidates filters on
-- CurrentSupply > 0 upstream of everything else, so a depot drained to ZERO drops out of candidacy by
-- that pre-existing coarse filter and this scenario would go green without testing the new predicate
-- at all. 5 is inside the band: it survives the coarse filter and must be rejected on affordability.
-- A fix written against "empty means zero" leaves this run RED, which is the point.
--
-- GEOMETRY, so the verdict is not a stop-tolerance argument:
--   (34,16)  Hunter's origin, dry
--   (26,12)  NearDepot   -- euclidean ~8.94, so ChooseAffordableResupplier picks it first
--   (18,22)  FarDepot    -- euclidean ~17.09, second choice, inside the 30-cell dry leash
--   x <= 32  the drain trigger: two cells of real travel, and still ~7 cells short of NearDepot, so
--            the Hunter is nowhere near either depot's 4c0 infantry aura when the supply vanishes
--
-- There is no enemy anywhere on this map on purpose, matching both sibling LC scenarios: entangling
-- this with the ammo-aware SmartMoveActivity interrupt would mean a regression there could only
-- surface here as a confusing red.
--
-- PASS: the Hunter ends up holding rounds (AmmoCount > 0) while standing within 5 cells of FarDepot.
-- Ammo alone would very nearly do — NearDepot is unaffordable and physically cannot serve him, so any
-- rounds at all imply the alternative served him — but requiring the POSITION as well is what makes
-- the verdict say "went somewhere else" rather than merely "got ammo somehow".
--
-- FAIL (pre-fix): he keeps walking to NearDepot and stops there. Called explicitly the moment he gets
-- within 3 cells of it carrying nothing, rather than left to time out, so the red names the symptom.

local DeadlineSeconds = 120
local AmmoPoolName = "primary-ammo"

local StockedLoad = 200      -- comfortably many 10-supply batches
local UnaffordableLoad = 5   -- stocked, and below AR's batch price of 10

local DrainTriggerX = 32     -- two cells of real travel past the priming move
local ArrivedAtNear = 3      -- chebyshev cells: close enough to call it "he went there anyway"
local ArrivedAtFar = 5       -- chebyshev cells: inside FarDepot's 4c0 aura, plus a cell of slack

-- The priming move, and the scenario is worthless without it. AmmoPool's idle dispatcher hangs off
-- INotifyBecomingIdle, which Actor.Tick raises only on the !wasIdle -> IsIdle TRANSITION, and
-- `wasIdle` is recomputed from IsIdle at the top of every tick (Actor.cs:318). A unit placed on the
-- map with no activity is idle ALREADY on its first tick, never transitions, and never asks for
-- resupply even once. test-poor-depot-still-worth-the-trip shipped a first cut with exactly that
-- defect and its red was right for entirely the wrong reason.
local PrimeCell = CPos.New(33, 16)

local drained = false
local peakAmmo = 0

local function chebyshev(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	if dx > dy then return dx end
	return dy
end

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, NearDepot)
	TestHarness.Select(Hunter)

	Test.SetSupply(NearDepot, StockedLoad)
	Test.SetSupply(FarDepot, StockedLoad)

	-- Setup assertions, so a staging failure never reads as a verdict about the errand.
	if Test.GetSupply(NearDepot) ~= StockedLoad or Test.GetSupply(FarDepot) ~= StockedLoad then
		Test.Fail("setup failed: could not stock both depots, so the initial pick is not the one "
			.. "this scenario reasons about")
		return
	end

	if Hunter.AmmoCount(AmmoPoolName) ~= 0 then
		Test.Fail("setup failed: the Hunter is not dry, so AutoRearmIfDry will never dispatch him")
		return
	end

	Hunter.Move(PrimeCell)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died" end
		if NearDepot.IsDead then return "fail: NearDepot died -- that is the host-invalid path, "
			.. "which Resupply already handled and this scenario is not about" end
		if FarDepot.IsDead then return "fail: FarDepot died, so the alternative under test is gone" end

		local ammo = Hunter.AmmoCount(AmmoPoolName)
		if ammo > peakAmmo then peakAmmo = ammo end

		local here = Hunter.Location
		local toNear = chebyshev(here, NearDepot.Location)
		local toFar = chebyshev(here, FarDepot.Location)

		-- Pull the supply out from under him once he is genuinely under way. Deliberately AFTER the
		-- errand exists rather than at WorldLoaded: draining before dispatch would test selection
		-- (half 1), and the in-flight abandonment (half 2) is the harder half and the one that
		-- produces the reported symptom.
		if not drained and here.X <= DrainTriggerX then
			Test.SetSupply(NearDepot, UnaffordableLoad)
			drained = true

			if Test.GetSupply(NearDepot) ~= UnaffordableLoad then
				return "fail: SETUP -- could not drain NearDepot to " .. UnaffordableLoad
					.. "; it holds " .. Test.GetSupply(NearDepot)
					.. ", so nothing about abandoning an errand is being measured"
			end
		end

		if not drained then
			return false
		end

		-- THE REPORTED SYMPTOM, named explicitly. He was told the depot cannot serve him seven cells
		-- ago and walked to it regardless.
		if toNear <= ArrivedAtNear and ammo == 0 then
			return "fail: reached NearDepot (chebyshev " .. toNear .. ") with 0 rounds after it was "
				.. "drained to " .. UnaffordableLoad .. " -- the in-flight errand was never abandoned "
				.. "(peak ammo " .. peakAmmo .. "). This is the user's 'they go to the LC even if it "
				.. "is empty, and they just wait there'."
		end

		-- The other way the old behaviour shows: he abandoned or never arrived, but then stood still
		-- rather than re-deciding. Distinguished from the above so the two failures are not conflated.
		if ammo > 0 and toFar > ArrivedAtFar then
			return "fail: holding " .. ammo .. " rounds but " .. toFar .. " cells from FarDepot -- "
				.. "something other than the alternative depot served him, so this run does not "
				.. "measure 'goes somewhere else to rearm'"
		end

		return ammo > 0 and toFar <= ArrivedAtFar
	end, "The Hunter never abandoned the drained Logistics Centre and reached the stocked one. "
		.. "A timeout here (rather than one of the explicit fails above) means he neither arrived at "
		.. "NearDepot nor got anywhere near FarDepot -- look for him standing still mid-map, which is "
		.. "HoldAndFlag firing on a host that cannot serve him.")
end
