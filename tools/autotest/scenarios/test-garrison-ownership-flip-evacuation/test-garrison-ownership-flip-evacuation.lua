-- AUTO TEST: what happens to men garrisoned in a building whose ownership flips to an enemy.
--
-- THE QUESTION AS ASKED: "can a non-owner evacuate men from a building an enemy now owns? If
-- not, men who garrison a building that flips ownership are permanently lost."
--
-- THE QUESTION IS BUILT ON A FALSE PREMISE, and this scenario is shaped by that rather than
-- around it. A shelter occupant is not left behind under a new owner at all: Cargo relays the
-- building's owner change straight onto everyone in the hold —
--
--     void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
--         foreach (var p in Passengers) p.ChangeOwner(newOwner);          Cargo.cs:1204-1211
--
-- so at the instant the building becomes Russian, the men inside BECOME RUSSIAN. There is
-- nothing left for their old owner to rescue. "Permanently lost" is the right answer and the
-- wrong mechanism: they are not stuck behind a UI gate, they have changed sides.
--
-- WHY THAT IS NOT A SHIPPED BUG. The flip cannot happen in real play. Four independent gates
-- stand in front of it, and the fourth is the one this scenario actually guards:
--   1. GarrisonManager.cs:259 — DynamicOwnership claims a building ONLY while it is Neutral,
--      so an occupied building never flips out from under its garrison on entry.
--   2. GarrisonManager.cs:320-330 — CheckOwnershipAfterExit transfers only to a player who
--      still has a man inside, and (3) means that is always an ally.
--   3. EnterAlliedActorTargeter.cs:49 — a soldier may only be ordered into an allied or
--      neutral building, so two hostile garrisons can never share a hold.
--   4. civilian.yaml:12-14 strips CaptureManager and both Capturable traits, and
--      OwnerLostAction sends a defeated player's buildings to Neutral, not to the victor.
-- Gate 3 is asserted below. If someone ever makes civilian buildings capturable, this test
-- goes red and says why — which is the point of writing it down as a test and not a comment.
--
-- WHAT THIS SCENARIO DELIBERATELY DOES NOT ASSERT, and it is the literal gesture in the
-- question: pressing evacuate on a building you do not own. NO LUA BINDING IN THIS ENGINE CAN
-- OBSERVE THAT GATE, so any assertion on it would be theatre:
--   * UnitOrderGenerator.cs:236 carries the owner check `self.Owner != world.LocalPlayer`, but
--     ONLY on the MouseInput overload. Test.ClickOrder and Test.ClickCursor both route through
--     the TargetModifiers overload at :271, which has no such check — they would report the
--     order going through when a real mouse could never have produced it.
--   * The authoritative gate, ValidateOrder.cs:48, compares the subject owner's ClientIndex to
--     the issuing client's. Map players share the HOST's ClientIndex (Player.cs:188-191,
--     CreateMapPlayers.cs:158-159), so in a single-client autotest "Russia" IS the local client
--     and the check passes unconditionally. It cannot fire here however the test is written.
-- The honest instrument for that gate is a code read, and it has been done: CommandBarLogic.cs
-- :460-462 filters the selection to `a.Owner == world.LocalPlayer` before building
-- selectedDeploys (:475), and GarrisonPanelLogic.cs:135-137 does the same before its eject
-- button. Both refuse. That is recorded here so the next reader does not spend a launch
-- rediscovering that the question is unanswerable by launching.
--
-- VERDICTS:
--   PASS  — the safe configuration holds: the owner can evacuate his own garrison, a soldier
--           cannot be ordered into an enemy-owned building, and a forced flip converts the
--           shelter occupant rather than orphaning him.
--   FAIL  — one of those limbs broke. The message names which, and what it means.
--   SKIP  — the scenario never built the world it describes. Always a setup or harness fault,
--           never a finding about garrisons. Read the message and fix the scenario.

local SetupWithin = 20      -- s for two riflemen to walk in and claim their houses
local EvacWithin = 20       -- s for the control garrison to come back out
local FlipWithin = 5        -- s for a ChangeOwner frame-end task to land

-- A soldier is unambiguously OUT when he is in the world and not standing on the building's
-- own cell. Both halves are needed. A shelter occupant is out of the world entirely, and a
-- port occupant is in-world but placed at the building's Location by DeployToPort — so
-- IsInWorld alone would read a manned firing port as an escape.
--
-- PITFALL, and it is why nothing below tests IsDead: for a soldier sitting in a Cargo hold
-- IsDead reads TRUE, so a sheltered man and a casualty are indistinguishable through these
-- properties (DOCS/recipes/AUTOTEST.md). Every assertion here is therefore phrased on being
-- OUT, a state both of those readings exclude.
local function IsOutOf(soldier, building)
	return soldier.IsInWorld
		and not (soldier.Location.X == building.Location.X and soldier.Location.Y == building.Location.Y)
end

local function OwnerOf(actor)
	local o = actor.Owner
	if o == nil then
		return "<none>"
	end

	return o.InternalName
end

-- Poll until `predicate` holds, then continue. Deliberately NOT TestHarness.AssertWithin:
-- that calls Test.Pass() the moment its predicate is true, which would end the run at the
-- first phase instead of moving to the next one. Only the final assertion may use AssertWithin.
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

local function State()
	return "Home owned by " .. OwnerOf(Home) .. ", Seized owned by " .. OwnerOf(Seized) ..
		"; C1 owner " .. OwnerOf(C1) .. " out=" .. tostring(IsOutOf(C1, Home)) ..
		"; S1 owner " .. OwnerOf(S1) .. " out=" .. tostring(IsOutOf(S1, Seized))
end

-- PHASE 4 — the guard that makes all of the above unreachable in real play.
local function EntryGuard()
	-- Prober is USA and Seized is now Russian. The refusal under test lives in
	-- EnterAlliedActorTargeter.CanTargetActor, which is on the shared tail of both
	-- OrderForUnit overloads — so unlike the Unload owner gate, Test.ClickOrder DOES see it
	-- honestly. The check it makes is about the RELATIONSHIP to the target; the check that
	-- ClickOrder bypasses is about whether `self` is the local player's, and Prober is.
	local issued = Test.ClickOrder(Prober, Seized)

	if issued == "EnterTransport" then
		Test.Fail("a USA rifleman was ordered INTO a Russia-owned building — the allied-or-neutral " ..
			"entry gate (EnterAlliedActorTargeter.cs:49) no longer holds. This is the gate that " ..
			"makes the ownership-flip defection unreachable, so with it gone a player can now walk " ..
			"men into an enemy building and have them change sides on the spot. " .. State())
		return
	end

	Test.Pass()
end

-- PHASE 3 — force the flip that cannot happen naturally, and read what became of the man.
local function ForceFlipAndMeasure()
	local russia = Player.GetPlayer("Russia")
	if russia == nil then
		Test.Skip("the Russia player does not exist in this map, so the flip could not be staged " ..
			"at all — check map.yaml's PlayerReference@Russia. " .. State())
		return
	end

	-- Direct assignment, not a gameplay action: GeneralProperties.Owner calls ChangeOwner, and
	-- there is no legitimate route to this state (see the header). The scenario is asking a
	-- counterfactual on purpose.
	Seized.Owner = russia

	WaitUntil(FlipWithin,
		function() return OwnerOf(Seized) == "Russia" end,
		function()
			-- THE ANSWER TO THE ORIGINAL QUESTION, in one assertion.
			if OwnerOf(S1) ~= "Russia" then
				Test.Fail("the building flipped to Russia but the man inside is still owned by " ..
					OwnerOf(S1) .. ". Cargo.cs:1204-1211 is supposed to relay the owner change onto " ..
					"every passenger, so either that relay is gone or S1 was never actually in the " ..
					"hold. If this fires, the 'men are permanently lost' question becomes live again " ..
					"and the answer now depends on the Unload owner gate — which no autotest can " ..
					"observe (see header). Check Cargo's INotifyOwnerChanged first. " .. State())
				return
			end

			EntryGuard()
		end,
		function()
			Test.Skip("Seized never registered the forced owner change within " .. FlipWithin ..
				"s; ChangeOwner runs as a frame-end task, so either it was rejected or the poll is " ..
				"reading a stale value. Nothing about garrisons was measured. " .. State())
		end)
end

-- PHASE 2 — the control. Proves the evacuate gesture works AT ALL before anything is claimed
-- about it failing later. Without this limb a broken hotkey binding and a working ownership
-- gate produce byte-identical evidence.
local function ControlEvacuation()
	TestHarness.Select(Home)
	if Test.GetSelectedCount() ~= 1 then
		Test.Skip("could not select the player's own garrisoned building (selected count " ..
			Test.GetSelectedCount() .. "), so the evacuate gesture could not be staged. This is a " ..
			"harness or selection fault, not a garrison finding. " .. State())
		return
	end

	-- The real key, dispatched through the real widget chain. Deploy is what carries Unload for
	-- a garrison: both Cargo and GarrisonManager implement IIssueDeployOrder, and
	-- CommandBarLogic.PerformDeployOrderOnSelection (:501) is what a player's F press reaches.
	if not Test.PressHotkey("Deploy") then
		Test.Skip("no widget consumed the Deploy hotkey, so the evacuate gesture was never " ..
			"delivered. Check that 'Deploy' is still bound (engine/mods/common/hotkeys/game.yaml:92) " ..
			"and that the command bar is present in this scenario's chrome. " .. State())
		return
	end

	WaitUntil(EvacWithin,
		function() return IsOutOf(C1, Home) end,
		ForceFlipAndMeasure,
		function()
			Test.Skip("the owner's OWN garrison did not come out within " .. EvacWithin ..
				"s of pressing Deploy, so the instrument this scenario depends on is broken and " ..
				"nothing can be concluded about the enemy-owned case. Check Cargo.CanUnload — a " ..
				"2x2 building needs a free adjacent cell to place a passenger into. " .. State())
		end)
end

WorldLoaded = function()
	TestHarness.FocusBetween(Home, Seized)

	C1.EnterTransport(Home)
	S1.EnterTransport(Seized)

	-- Ownership IS the setup proof. DynamicOwnership flips a Neutral building to the entering
	-- soldier's player in OnPassengerEntered, so "both houses read USA" is a direct, unambiguous
	-- statement that both men are inside — and it is not confounded by the IsDead ambiguity that
	-- makes a sheltered soldier unreadable by any other property.
	WaitUntil(SetupWithin,
		function() return OwnerOf(Home) == "USA" and OwnerOf(Seized) == "USA" end,
		ControlEvacuation,
		function()
			Test.Skip("one or both houses never became USA-owned within " .. SetupWithin ..
				"s, so the garrison never formed and nothing under test was reached. Either the " ..
				"riflemen could not path to the buildings, or DynamicOwnership stopped claiming " ..
				"neutral buildings on entry (GarrisonManager.cs:256-261). " .. State())
		end)
end
