-- AUTO TEST: can two HOSTILE players hold one garrisonable building at the same time, and if so can
-- the one who does not own it get his men back out?
--
-- Audit item #5 (WORKSPACE/audit/260915-civ-garrison-audit.md), and the run the user selected on
-- 2026-09-01 as Q3.
--
-- WHY IT IS REACHABLE AT ALL. The relationship gate appears EXACTLY ONCE in the entry chain, at
-- targeting time: EnterAlliedActorTargeter.CanTargetActor admits an allied OR NEUTRAL owner
-- (EnterAlliedActorTargeter.cs:49-54). A neutral house is therefore a legal target for BOTH players
-- simultaneously. Nothing downstream re-asks:
--   Passenger.ResolveOrder      — liveness, CanEnter, IsCorrectCargoType   (Passenger.cs:216-239)
--   RideTransport.OnEnterComplete — identity, CanLoad                      (RideTransport.cs:69-87)
--   Cargo.CanLoad               — LoadingBlocked, filters, space           (Cargo.cs:522-533)
-- The ICargoCanLoadFilter extension point that WOULD be the natural home for such a check has
-- exactly one implementor mod-wide, SupplyProvider, and it is not on a civilian building.
--
-- So the race is: both men are ordered in while the house is neutral, the near one arrives and flips
-- it to his owner (GarrisonManager.cs:263-267, and deliberately with updateGeneration: false so
-- in-flight enter orders are NOT invalidated), and the far one walks into a building that now
-- belongs to his enemy.
--
-- WHAT THIS SCENARIO CAN AND CANNOT ANSWER, stated up front because the second half of the question
-- is genuinely unobservable here and pretending otherwise would be theatre:
--
--   OBSERVABLE — whether the second player's man LOADS, but ONLY through Test.IsLoadedInto, which
--   reads Cargo's passenger list directly. Everything else lies: a Cargo passenger is out of world
--   AND reads IsDead == true, so IsInWorld calls him gone, IsDead calls him a casualty, and
--   Test.ConditionCount returns 0 for any actor failing either check — which is why the condition
--   Passenger grants precisely BECAUSE he boarded cannot be read through it. Run 260915_175947
--   learned this the expensive way; see the note above IsLoaded below.
--
--   NOT OBSERVABLE — the non-owner pressing evacuate on a building he does not own. The owner check
--   lives at UnitOrderGenerator.cs:236 on the MouseInput overload ONLY; Test.ClickOrder and
--   Test.ClickCursor route through the TargetModifiers overload at :271, which has no such check.
--   The authoritative gate, ValidateOrder.cs:48, compares the subject owner's ClientIndex to the
--   issuing client's — and map players all share the HOST's ClientIndex (Player.cs:188-191,
--   CreateMapPlayers.cs:158-159), so in a single-client autotest "Russia" IS the local client and the
--   check passes unconditionally. It cannot fire here however the test is written. The honest
--   instrument for that gate is a code read, and it has been done: CommandBarLogic.cs:460-462 and
--   GarrisonPanelLogic.cs:135-137 both filter the selection to `a.Owner == world.LocalPlayer` before
--   offering any eject. Both refuse. (Same finding, same citations, as
--   test-garrison-ownership-flip-evacuation — repeated so this file stands alone.)
--
--   WHAT IS MEASURED INSTEAD, and it decides the user's question at the layer that can be measured:
--   whether the SIM will release a hostile passenger at all. Phase 4 has the OWNER unload, and reads
--   whether the enemy's man comes out with the rest. If he does, nothing is welded in and "men
--   permanently lost" is a UI-layer problem with a cheap fix. If he does not, the trap is in the sim
--   and it is a real bug.
--
-- VERDICTS, and every stage has a named one — a refusal anywhere is a PASS, because a refusal is the
-- safe answer to this question:
--   PASS + note "REFUSED AT TARGETING"  — the second player was never offered the order.
--   PASS + note "REFUSED AT LOAD"       — he was offered it, walked over, and the hold rejected him.
--   PASS + note "CO-GARRISON REACHED, MEN RELEASABLE" — the oddity exists but nobody is trapped.
--   FAIL "MEN PERMANENTLY LOST"         — he got in and the sim will not give him back.
--   FAIL "THE ENTRY GATE IS NOT HOLDING AT ALL" — the control probe was admitted into a building
--                                          already owned by his enemy, which is a bigger finding
--                                          than the race and would make the race reading moot.
--   SKIP                                — the scenario never built the world it describes.

local SetupWithin = 25      -- s for the adjacent man to walk one cell and claim the house
local RaceWithin = 30       -- s for the far man to finish his walk and resolve one way or the other
local EvacWithin = 20       -- s for an unload to put men back on the ground

local function OwnerOf(actor)
	local o = actor.Owner
	if o == nil then
		return "<none>"
	end

	return o.InternalName
end

-- INSTRUMENT, corrected after run 260915_175947 measured nothing. The first version asked
-- Test.ConditionCount(soldier, "disable-experience"), the condition Passenger grants on load. That
-- binding opens with
--   if (!TestMode.IsActive || actor == null || actor.IsDead || !actor.IsInWorld) return 0;
-- and a Cargo passenger is out of world AND reads IsDead == true -- so it returns 0 for exactly the
-- state it was being used to detect. The run proved it: the house DID flip to USA, which only
-- happens when a man boards, while UsMan still read loaded=false. A guaranteed false negative.
--
-- Test.IsLoadedInto reads Cargo's own passenger list and is true regardless of either flag.
local function IsLoaded(soldier)
	return Test.IsLoadedInto(soldier, House)
end

-- IsInWorld is sound in THIS direction: a man in a hold is out of world, so in-world-and-not-loaded
-- really does mean standing outside. It is only the inverse that lies.
local function IsOutInTheOpen(soldier)
	return soldier.IsInWorld and not IsLoaded(soldier)
end

local function State()
	return "House owner " .. OwnerOf(House) ..
		"; UsMan loaded=" .. tostring(IsLoaded(UsMan)) ..
		"; RuMan loaded=" .. tostring(IsLoaded(RuMan)) ..
		" inWorld=" .. tostring(RuMan.IsInWorld) ..
		"; RuProbe inWorld=" .. tostring(RuProbe.IsInWorld)
end

local function WaitUntil(seconds, predicate, onReady, onTimeout)
	local remaining = math.floor(seconds * TestHarness.TicksPerSecond)
	local check
	check = function()
		if predicate() then
			onReady()
			return
		end

		remaining = remaining - 1
		if remaining <= 0 then
			onTimeout()
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

-- PHASE 4 — the releasability question, at the only layer this harness can see.
local function CanTheHostileOccupantBeReleased()
	TestHarness.Select(House)
	if Test.GetSelectedCount() ~= 1 then
		Test.Skip("could not select the garrisoned house (selected count " ..
			Test.GetSelectedCount() .. "), so no unload could be staged and the releasability half " ..
			"of the question was never asked. The co-garrison itself WAS reached — that finding " ..
			"stands. Harness fault. " .. State())
		return
	end

	-- Deploy is what carries Unload for a garrison: both Cargo and GarrisonManager implement
	-- IIssueDeployOrder, and CommandBarLogic.PerformDeployOrderOnSelection is what a player's F
	-- press reaches. Issued by the OWNER, deliberately — see the header: the non-owner's own
	-- gesture cannot be observed in a single-client autotest, so this asks the answerable question
	-- (will the sim hand a hostile passenger back) rather than faking the unanswerable one.
	if not Test.PressHotkey("Deploy") then
		Test.Skip("no widget consumed the Deploy hotkey, so the unload was never delivered. Check " ..
			"that 'Deploy' is still bound and that the command bar is present in this scenario's " ..
			"chrome. The co-garrison finding stands; only releasability went unmeasured. " .. State())
		return
	end

	WaitUntil(EvacWithin,
		function() return IsOutInTheOpen(RuMan) end,
		function()
			Test.Pass("CO-GARRISON REACHED, MEN RELEASABLE. Two hostile players held one building at " ..
				"once — the neutral window in EnterAlliedActorTargeter.cs:49-54 let the second man be " ..
				"ordered in, and nothing downstream re-checked once the first took ownership. But the " ..
				"sim does NOT weld him in: an unload put the enemy's man back on the ground with the " ..
				"rest. So 'men permanently lost' is a UI-layer problem, not a sim one — the refusal is " ..
				"CommandBarLogic.cs:460-462 and GarrisonPanelLogic.cs:135-137 filtering the selection " ..
				"to the local player, which this harness cannot exercise (see header). That makes the " ..
				"fix cheap and the severity lower than the audit assumed. " .. State())
		end,
		function()
			Test.Fail("MEN PERMANENTLY LOST. The enemy's man was still in the hold " .. EvacWithin ..
				"s after an unload that the OWNER issued, so the trap is in the sim and not merely in " ..
				"the UI: there is no gesture available to anybody that returns him. Audit item #5 " ..
				"escalates from an oddity to a real bug, and the ICargoCanLoadFilter fix that stops " ..
				"the entry becomes the priority rather than a tidy-up. Check Cargo.Unload for a " ..
				"passenger-owner assumption, and whether a hostile passenger is being skipped rather " ..
				"than placed. " .. State())
		end)
end

-- PHASE 3 — did the far man actually get in?
local function DidTheRaceComplete()
	-- The previous version also waited on `RuMan.IsDead and not RuMan.IsInWorld` as a "he died"
	-- branch. That IS the loaded state, so it fired the moment he boarded and reported the run as
	-- having measured nothing. There is no honest death test available here: a corpse and a
	-- passenger are indistinguishable through actor properties. Nothing can damage him instead --
	-- every unit on the map is HoldFire (rules.yaml) -- so loaded-or-still-walking are the only two
	-- states, and the timeout arm below distinguishes them.
	WaitUntil(RaceWithin,
		function() return IsLoaded(RuMan) end,
		function()
			CanTheHostileOccupantBeReleased()
		end,
		function()
			-- The SAFE outcome, and the one the audit hoped for. He was offered the order, walked
			-- over, and did not end up inside.
			if IsOutInTheOpen(RuMan) then
				Test.Pass("REFUSED AT LOAD. The neutral window let a hostile soldier be ORDERED into " ..
					"the house — Test.ClickOrder returned EnterTransport for him while it was still " ..
					"neutral — but he never entered the hold after the building flipped to his enemy. " ..
					"Something in the load path is re-checking after all: look at Cargo.CanLoad's " ..
					"ICargoCanLoadFilter list (Cargo.cs:527-530) and at RideTransport.OnEnterComplete " ..
					"(:78-82), since the audit found no relationship test in either. Whatever it is, " ..
					"hostile co-garrison is not reachable and audit item #5 can be closed. " .. State())
				return
			end

			Test.Skip("RuMan neither loaded nor ended up standing outside within " .. RaceWithin ..
				"s — he is presumably still walking, or stuck. The race was never resolved either " ..
				"way. Raise RaceWithin, or check that he can path to the house. " .. State())
		end)
end

-- PHASE 2 — the control. An enemy-owned building must be refused outright, which is what makes the
-- race specifically about the NEUTRAL window rather than about the gate being broken generally.
local function ProbeTheGateAfterTheFlip()
	local issued = Test.ClickOrder(RuProbe, House)

	if issued == "EnterTransport" then
		Test.Fail("THE ENTRY GATE IS NOT HOLDING AT ALL. A Russian rifleman was offered " ..
			"EnterTransport against a house already owned by USA, so EnterAlliedActorTargeter's " ..
			"allied-or-neutral test (EnterAlliedActorTargeter.cs:49-54) is not refusing enemies. That " ..
			"is a larger finding than the co-garrison race this scenario was written for, and it " ..
			"makes the race reading moot: entry would not need a neutral window at all. " .. State())
		return
	end

	DidTheRaceComplete()
end

WorldLoaded = function()
	TestHarness.FocusBetween(House, RuMan)

	if OwnerOf(House) ~= "Neutral" then
		Test.Skip("the house did not start Neutral (owner " .. OwnerOf(House) .. "), so the neutral " ..
			"window this scenario depends on never existed. Check map.yaml. " .. State())
		return
	end

	-- BOTH ORDERS ON THE SAME TICK, THROUGH THE REAL TARGETER. Test.ClickOrder walks the same
	-- IIssueOrder/IOrderTargeter pipeline the UI cursor resolver walks, so the allied-or-neutral
	-- test really is consulted here — unlike Test.IssueEnterTransport, which would issue the order
	-- directly and stage a situation the game might never permit.
	local us = Test.ClickOrder(UsMan, House)
	local ru = Test.ClickOrder(RuMan, House)

	if us ~= "EnterTransport" then
		Test.Skip("the USA rifleman was not offered EnterTransport against a NEUTRAL house (got '" ..
			tostring(us) .. "'), so the scenario could not even stage the ordinary case. Nothing " ..
			"about hostile co-garrison was measured. " .. State())
		return
	end

	if ru ~= "EnterTransport" then
		Test.Pass("REFUSED AT TARGETING. The second, hostile player was never offered the order " ..
			"against the neutral house (got '" .. tostring(ru) .. "'), so the race described in the " ..
			"audit cannot start and hostile co-garrison is unreachable. Note this contradicts " ..
			"EnterAlliedActorTargeter.cs:49-54 as read on 2026-09-15, which admits neutral owners — " ..
			"so if this is the verdict, something ELSE is refusing and it is worth finding out what, " ..
			"because whatever it is also governs ordinary allied garrisoning. " .. State())
		return
	end

	-- Ownership IS the setup proof for the first man: DynamicOwnership flips a neutral building to
	-- the entering player, so "House reads USA" states unambiguously that UsMan is inside, and it is
	-- not confounded by the IsDead ambiguity.
	WaitUntil(SetupWithin,
		function() return OwnerOf(House) == "USA" end,
		ProbeTheGateAfterTheFlip,
		function()
			Test.Skip("the house never became USA-owned within " .. SetupWithin .. "s, so the first " ..
				"man never got in and there was no flip for the second to race. Either he could not " ..
				"path one cell, or DynamicOwnership stopped claiming neutral buildings on entry " ..
				"(GarrisonManager.cs:263-267). " .. State())
		end)
end
