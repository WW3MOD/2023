-- AUTO TEST: emptying a garrisoned building's SHELTER while its FIRING PORTS are still manned must
-- not hand the building to Neutral.
--
-- Audit item: WORKSPACE/audit/260915-civ-garrison-audit.md §4, "two independent revert-to-neutral
-- implementations on the same actors, and they do not agree".
--
-- THE DEFECT. All four garrison families set CargoInfo.Neutral, and its implementation in
-- UnloadCargo tested `cargo.PassengerCount == 0`. PassengerCount is the Cargo HOLD, not the
-- building: GarrisonManager.DeployToPort calls cargo.Unload on a man who mans a port, so a port
-- soldier is NOT a passenger. Emptying the shelter therefore satisfied that condition with men still
-- inside and still shooting, and the frame-end task handed the house to Neutral underneath them.
--
-- The other implementation, GarrisonManager.CheckOwnershipAfterExit, walks PortStates AND the
-- shelter list and gets it right — and it runs SYNCHRONOUSLY inside the same cargo.Unload (via
-- INotifyPassengerExited) that UnloadCargo's frame-end task then ran after. So the correct answer
-- was already on record and the Cargo flip overwrote it. The fix is a veto, not a second opinion:
-- IOverridesCargoNeutralRevert, which GarrisonManager answers true to whenever
-- CheckOwnershipAfterExit would act.
--
-- WHY THIS GESTURE AND NOT "UNLOAD ALL", which is what the item was written against. "Unload All"
-- CANNOT reach the defect, and finding that out is half of what this scenario is for. The "Unload"
-- order is resolved by BOTH traits on the actor: GarrisonManager.ResolveOrder clears every port
-- SYNCHRONOUSLY (GarrisonManager.cs:1561-1592) while Cargo.ResolveOrder only QUEUES the UnloadCargo
-- activity (Cargo.cs:459), which cannot run before the next tick. The ports are therefore always
-- already empty by the time the flip is evaluated, and the flip's answer is right by accident.
--
-- The path that DOES reach it is the class-grouped unload menu: CargoUnloadMenuLogic issues
-- `UnloadCargoPassenger` per man (CargoUnloadMenuLogic.cs:241), Cargo.ResolveOrder turns each into
-- its own UnloadCargo activity (Cargo.cs:467), and nothing in that chain touches a port. It is live
-- in this mod (chrome/ingame-player.yaml:6, hotkey UnloadMenu = J) and its candidate filter asks
-- only for a non-empty Cargo owned by the local player (CargoUnloadMenuLogic.cs:90-107) — it does
-- NOT exclude garrison buildings. So this is a real gesture a real player can make, and
-- Test.ClickUnloadMenuRow drives the actual click handlers rather than faking the order.
--
-- VERDICTS:
--   PASS  — the shelter emptied, at least one port stayed manned, the house stayed USA, AND it
--           still reverted to Neutral once the ports were cleared. Both halves are required: a fix
--           that merely stopped the building EVER reverting would pass the first half alone.
--   FAIL "HANDED TO NEUTRAL UNDER ITS OWN GARRISON" — the regression signal. This is what the code
--           did before the veto.
--   FAIL "NEVER REVERTED"  — the veto is too wide: the building kept an owner with nobody inside.
--   SKIP  — the scenario never built the state it describes (nobody garrisoned, the ports never
--           filled, the menu never opened). Named individually so a skip says which.
--
-- THE PRECONDITION THAT MAKES ANY OF IT MEASURABLE, learned the expensive way in run 260921_152208,
-- where the deliberate sabotage of the fix PASSED with text identical to the real thing: ALL EIGHT
-- PORTS MUST BE MANNED BEFORE THE SHELTER IS DRAINED. The hold has two exits and only one of them
-- arms the defect — UnloadCargo enqueues the frame-end revert (UnloadCargo.cs:235-252), while
-- GarrisonManager.DeployToPort calls cargo.Unload straight from its own tick with no activity and no
-- task (GarrisonManager.cs:441). While one port is still free the hold drains into it, reaches zero
-- having armed nothing, and the run reports "shelter 0, ports 8" having measured its own absence.
-- Full reasoning over WaitForEveryPortToBeManned.

local GarrisonWithin = 40   -- s for ten riflemen to walk one cell and board
-- 60, not 40: this now waits for ALL EIGHT ports rather than the first one, which is eight target
-- confirmations and eight deploys instead of one. All eight are reachable — the four baits sit one
-- per diagonal and the eight ports share those four yaws two apiece (civilian.yaml:133-183), a
-- second port on the same diagonal takes the same bait under a score PENALTY rather than an
-- exclusion (GarrisonManager.cs:1227-1228), and selection has no positive threshold to fail
-- (:1102, :1131). The ceiling only costs time on a run that was going to skip anyway.
local PortsWithin = 60      -- s for the house to confirm its baits and fill every port
local DrainWithin = 30      -- s for the unload menu's orders to empty the hold
local RevertWithin = 30     -- s for the building to hand itself back once genuinely empty
local HoldWatch = 0.6       -- s to keep watching ownership after the hold hits zero

-- EVERY port, not one. ^CivBuilding declares exactly eight (civilian.yaml:133-183, names
-- northeast1/2, southeast1/2, southwest1/2, northwest1/2), and the drain MUST NOT START until all
-- eight are occupied. See the note over WaitForEveryPortToBeManned: while a port is free, the hold
-- empties into it rather than through UnloadCargo, and the defect is never armed.
local AllPorts = 8

local Men = nil

local function OwnerOf(actor)
	local o = actor.Owner
	if o == nil then
		return "<none>"
	end

	return o.InternalName
end

-- A man is "inside" in either capacity. Test.IsLoadedInto reads Cargo's passenger list;
-- Test.IsAtGarrisonPort reads GarrisonManager's port states. Neither alone is sufficient and the
-- difference is the entire subject of this scenario, so both are always asked together.
--
-- Nothing else works. A Cargo passenger is out of world AND reads IsDead == true, so IsInWorld calls
-- him gone, IsDead calls him a casualty, and Test.ConditionCount returns 0 for any actor failing
-- either check — it cannot see a passenger at all. (Learned in run 260915_175947, recorded in
-- test-garrison-hostile-cogarrison.lua.)
local function InShelter(soldier)
	return Test.IsLoadedInto(soldier, House)
end

local function AtPort(soldier)
	return Test.IsAtGarrisonPort(soldier, House)
end

local function CountShelter()
	local n = 0
	for _, m in ipairs(Men) do
		if InShelter(m) then
			n = n + 1
		end
	end

	return n
end

local function CountPorts()
	local n = 0
	for _, m in ipairs(Men) do
		if AtPort(m) then
			n = n + 1
		end
	end

	return n
end

local function State()
	return "House owner " .. OwnerOf(House) ..
		"; shelter=" .. CountShelter() ..
		"; ports=" .. CountPorts()
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

-- PHASE 4 — the other half of the fix. A veto that simply stopped the building reverting would pass
-- phase 3 and be a worse bug than the one it replaced: a civilian house permanently annexed by
-- whoever garrisoned it once. Clear the ports and require the revert to happen.
local function DoesItStillRevertWhenGenuinelyEmpty()
	TestHarness.Select(House)

	-- "Unload" is the order that clears the ports (GarrisonManager.cs:1560-1592). Reaching it from
	-- HERE takes the mouse gesture, not the Deploy hotkey, and that distinction is the whole reason
	-- the first run of this scenario never returned a verdict.
	--
	-- THE DEPLOY KEY CANNOT ISSUE THIS ORDER IN THIS EXACT STATE. The command bar's Deploy path does
	-- not walk IIssueOrder at all: it collects TraitsImplementing<IIssueDeployOrder> and issues only
	-- where CanIssueDeployOrder is true (CommandBarLogic.cs:607-619). The ONLY IIssueDeployOrder on
	-- this actor is Cargo, and its gate is `!IsEmpty()` (Cargo.cs:427) — which phase 2 has just
	-- falsified by construction. GarrisonManager implements that interface nowhere. So the button is
	-- disabled, no order is built, and the ports stay manned until the clock runs out. (Run
	-- 260921_150014 skipped exactly here: "8 still manned after 30s".)
	--
	-- AND Test.PressHotkey CANNOT SEE THAT HAPPEN. ButtonWidget.HandleKeyPress returns true for any
	-- matching key whether or not the button is disabled (ButtonWidget.cs:169); IsDisabled gates
	-- OnKeyPress alone (:160), falling through to ClickDisabledSound. A disabled Deploy button
	-- therefore CONSUMES the press and reports "consumed", so the old `if not PressHotkey` guard read
	-- green on precisely the failure it was written to catch. Test.IssueDeploy is no escape either:
	-- it re-applies the same CanIssueDeployOrder gate (TestGlobal.cs:1244).
	--
	-- The MOUSE path is wired for this state on purpose. GarrisonManager yields its own
	-- DeployOrderTargeter("Unload") exactly WHEN the cargo is empty and occupants remain
	-- (GarrisonManager.cs:1464-1476, added by bc35eb98 "allow Unload when only port soldiers remain"),
	-- complementing Cargo's, which yields only while NOT empty (Cargo.cs:390-411) — the two are
	-- mutually exclusive and together cover both states. ClickOrder walks that chain in descending
	-- OrderPriority and issues whatever wins, which is the routing a player clicking the selected
	-- house actually gets, so this is a real gesture and not a staged order.
	local issued = Test.ClickOrder(House, House)
	if issued ~= "Unload" then
		Test.Skip("clicking the garrisoned house resolved to " .. tostring(issued) .. ", not " ..
			"\"Unload\", so the ports were never ordered clear and the revert half went unmeasured. " ..
			"The keep-ownership half PASSED and that finding stands. GarrisonManager offers the " ..
			"Unload targeter only while `cargo.IsEmpty() && HasAnyOccupants` " ..
			"(GarrisonManager.cs:1464-1476); check that gate, and that no higher-priority targeter on " ..
			"the building now wins a self-click. " .. State())
		return
	end

	WaitUntil(RevertWithin,
		function() return CountPorts() == 0 and OwnerOf(House) == "Neutral" end,
		function()
			Test.Pass("OWNERSHIP HELD WHILE MANNED, RELEASED WHEN EMPTY. The shelter was emptied " ..
				"through the real unload menu while firing ports were still manned and the house " ..
				"stayed USA — the Cargo hold hitting zero no longer decides ownership on an actor " ..
				"whose men are at the loopholes. Clearing the ports then handed it back to Neutral, " ..
				"so the veto is scoped to the disagreement and has not simply switched the revert " ..
				"off. " .. State())
		end,
		function()
			if CountPorts() > 0 then
				-- Distinct from the refusal above: the Unload order was ACCEPTED and routed, and the
				-- ports still did not clear. That narrows it to the RESOLVE side, where the clearing
				-- loop is unconditional today (GarrisonManager.cs:1564-1577).
				Test.Skip("the house accepted an \"Unload\" order and the ports still never cleared (" ..
					CountPorts() .. " still manned after " .. RevertWithin .. "s), so the revert half " ..
					"could not be asked. The keep-ownership half PASSED and that finding stands. The " ..
					"order was issued and routed, so this is the RESOLVE side: check that " ..
					"GarrisonManager's \"Unload\" case still walks PortStates and clears " ..
					"DeployedSoldier unconditionally (GarrisonManager.cs:1564-1577). " .. State())
				return
			end

			Test.Fail("NEVER REVERTED. Every port is clear and the shelter is empty, yet the house " ..
				"is still owned by " .. OwnerOf(House) .. " after " .. RevertWithin .. "s. The veto " ..
				"is too wide: IOverridesCargoNeutralRevert suppressed CargoInfo.Neutral's flip on an " ..
				"actor whose own CheckOwnershipAfterExit then did not run or did not revert either, " ..
				"so NOTHING hands the building back. That is a permanent silent annexation of a " ..
				"civilian house, and it is worse than the bug the veto was added to fix. Check that " ..
				"GarrisonManager.ResolveOrder(\"Unload\") still calls CheckOwnershipAfterExit after " ..
				"clearing the ports. " .. State())
		end)
end

-- The verdict text for phase 3's failure, lifted out so the watch below reads as a loop.
local function ReportHandedToNeutral(ports)
	Test.Fail("HANDED TO NEUTRAL UNDER ITS OWN GARRISON. The shelter emptied and the house " ..
		"became " .. OwnerOf(House) .. " while " .. ports .. " firing port(s) were still manned " ..
		"— its men are shooting out of a building that no longer belongs to the player who put " ..
		"them there. This is CargoInfo.Neutral's flip in UnloadCargo firing on " ..
		"`cargo.PassengerCount == 0`, which counts the Cargo HOLD only: DeployToPort removes a " ..
		"port soldier from the hold, so zero there is not evidence the building is empty. The " ..
		"fix is the IOverridesCargoNeutralRevert veto — check GarrisonManager still implements " ..
		"it, that UnloadCargo still calls GarrisonOwnershipMath.MayRevertHoldToNeutral, and that " ..
		"the guard has not been narrowed. " .. State())
end

-- PHASE 3 — THE MEASUREMENT. The hold is empty, ports are manned. Who owns the house?
--
-- WATCHED, NOT SAMPLED. The flip under test is a FRAME-END TASK, and this callback is one too:
-- Trigger.AfterDelay schedules a DelayedAction effect (TriggerGlobal.cs:77) whose tick re-queues the
-- body to the frame end (DelayedAction.cs:32). Both therefore land in the same drain loop
-- (World.cs:520-521), and the flip is enqueued from the ACTOR tick (:505) while this is enqueued
-- from the EFFECT tick (:510) — so the queue is FIFO-ordered flip-then-us and a single sample here
-- does see it. That ordering is derived from reading, not observed, and it is the kind of thing a
-- future engine change can invert silently. Watching for a beat costs nothing and does not depend on
-- being right about it: any tick in the window where the house is not USA while a port is manned is
-- the defect, whenever it lands.
local function DidItKeepItsOwner()
	local remaining = math.floor(HoldWatch * TestHarness.TicksPerSecond)
	local watch

	watch = function()
		local ports = CountPorts()

		if ports > 0 and OwnerOf(House) ~= "USA" then
			ReportHandedToNeutral(ports)
			return
		end

		remaining = remaining - 1
		if remaining <= 0 then
			-- Ports emptying during the watch is not the defect and not a pass: with nobody at a
			-- loophole the revert is CORRECT, so there is no disagreement left to measure.
			if ports == 0 then
				Test.Skip("every firing port emptied during the " .. HoldWatch .. "s ownership " ..
					"watch, so the hold-versus-building disagreement stopped existing before it " ..
					"could be measured and the house's owner proves nothing either way. Most likely " ..
					"the garrison recalled or un-manned its ports (bait killed, out of ammo, or " ..
					"target lost). " .. State())
				return
			end

			DoesItStillRevertWhenGenuinelyEmpty()
			return
		end

		Trigger.AfterDelay(1, watch)
	end

	-- Sample immediately first: on the derived ordering the flip has already run, earlier in this
	-- same frame-end drain.
	watch()
end

-- PHASE 2 — drain the shelter through the real player gesture, leaving the ports alone.
local function EmptyTheShelterWithoutTouchingThePorts()
	TestHarness.Select(House)
	if Test.GetSelectedCount() ~= 1 then
		Test.Skip("could not select the garrisoned house (selected count " ..
			Test.GetSelectedCount() .. "), so the unload menu could not be opened and nothing was " ..
			"measured. Harness fault. " .. State())
		return
	end

	if not Test.PressHotkey("UnloadMenu") then
		Test.Skip("no widget consumed the UnloadMenu hotkey, so the class-grouped unload menu never " ..
			"opened. Check hotkeys.yaml still binds UnloadMenu (J) and that CargoUnloadMenuLogic is " ..
			"still in ingame-player.yaml's Logic list. " .. State())
		return
	end

	-- The ALL chip on row 0 drops the whole class, i.e. every rifleman in the HOLD. It issues one
	-- UnloadCargoPassenger per man and touches no port — which is exactly the asymmetry the defect
	-- lives in. Row 0 is the only row: every man aboard is an e1.
	if not Test.ClickUnloadMenuRow(0, true) then
		Test.Skip("the unload menu opened but row 0 could not be clicked, so no man was ordered out " ..
			"and nothing was measured. Either CLASS_LIST is empty (the hold had already drained) or " ..
			"the row carries no CLASS_ALL button. " .. State())
		return
	end

	WaitUntil(DrainWithin,
		function() return CountShelter() == 0 end,
		function()
			-- THE PRECONDITION THE WHOLE RESULT RESTS ON. If the ports emptied along with the
			-- shelter there is no disagreement left to measure and a green result would be
			-- meaningless — so this is a SKIP with its cause named, never a pass.
			if CountPorts() == 0 then
				Test.Skip("the shelter drained but every port emptied with it, so the state this " ..
					"scenario measures — hold at zero, ports still manned — never existed and the " ..
					"ownership reading proves nothing either way. Most likely the garrison recalled " ..
					"or un-manned its ports (bait killed, out of ammo, or target lost). " .. State())
				return
			end

			DidItKeepItsOwner()
		end,
		function()
			Test.Skip("the shelter still held " .. CountShelter() .. " men " .. DrainWithin ..
				"s after the unload menu's ALL chip was clicked, so the hold never reached zero and " ..
				"the flip's condition was never satisfied. Check that UnloadCargoPassenger still " ..
				"reaches Cargo.ResolveOrder and that the men are not boxed in with no exit cell. " ..
				State())
		end)
end

-- PHASE 1b — WAIT FOR ALL EIGHT PORTS, not for one. This is the precondition the whole measurement
-- rests on, and requiring only one is why run 260921_152208 could not tell the fix from its own
-- sabotage: with the veto forced off it PASSED, with the identical text.
--
-- THE HOLD HAS TWO EXITS AND ONLY ONE OF THEM ARMS THE DEFECT. The defect lives in a frame-end task
-- that UnloadCargo enqueues after it unloads somebody (UnloadCargo.cs:235-252); the task re-reads
-- cargo.PassengerCount when it runs and reverts the building if the hold is empty. But a man also
-- leaves the hold by being DEPLOYED TO A PORT: GarrisonManager.DeployToPort calls cargo.Unload
-- directly (GarrisonManager.cs:441) from its own ITick, with no UnloadCargo activity anywhere in the
-- chain and therefore NO SUCH TASK. So if the last man to leave the hold leaves through a port, the
-- hold reaches zero and nothing was ever armed to notice.
--
-- That is exactly what "one port is enough" produced. The old predicate fired on the FIRST port
-- manned, while seven were still empty and ten men were still converging, so phase 2 clicked the ALL
-- chip into a hold that GarrisonManager was concurrently draining into those seven free ports. The
-- run reached "shelter 0, ports 8" either way and measured nothing.
--
-- With all eight occupied there is nowhere left to deploy, so every remaining man can only leave
-- through UnloadCargo, and the last one's frame-end task reads PassengerCount == 0 with eight ports
-- still manned — which is the disagreement this scenario exists to measure. The map is sized for
-- precisely this steady state: ten men, eight ports, two left over in the shelter.
local function WaitForEveryPortToBeManned()
	WaitUntil(PortsWithin,
		function() return CountPorts() >= AllPorts and CountShelter() > 0 end,
		EmptyTheShelterWithoutTouchingThePorts,
		function()
			if CountPorts() == 0 then
				Test.Skip("no firing port was ever manned within " .. PortsWithin .. "s, so the " ..
					"hold-versus-building disagreement could not be staged. GarrisonManager only " ..
					"deploys to a port with a CONFIRMED, IN-ARC, IN-RANGE target its armament is " ..
					"valid against. The four baits are riflemen (a rifle cannot target a building " ..
					"at all: ^5.56mm ValidTargets is Infantry, Vehicle, AirLight) placed one per " ..
					"diagonal at 8.49 cells, inside 5.56mm.E3's 10c0. If this fires, check the port " ..
					"Cone reading and TargetConfirmTicks. " .. State())
				return
			end

			if CountShelter() == 0 then
				Test.Skip("ports are manned (" .. CountPorts() .. ") but the shelter is empty, so " ..
					"there was nothing left to unload and the measurement could not be staged. All " ..
					"ten men deployed to ports at once. " .. State())
				return
			end

			Test.Skip("only " .. CountPorts() .. " of " .. AllPorts .. " ports filled within " ..
				PortsWithin .. "s (shelter " .. CountShelter() .. "), so a FREE PORT REMAINED and " ..
				"the hold would have drained into it instead of through UnloadCargo — which arms " ..
				"nothing, so the measurement would have been meaningless rather than wrong. This is " ..
				"a deliberate skip, not a timeout to widen: raising PortsWithin only helps if the " ..
				"ports are still filling. If one port never fills at all, the bait for that diagonal " ..
				"is the thing to check (map.yaml BaitNE/SE/SW/NW), not this clock. " .. State())
		end)
end

-- PHASE 1 — garrison the house.
local function WaitForTheGarrison()
	-- Ownership IS the setup proof: DynamicOwnership flips a neutral building to the entering
	-- player, so "House reads USA" states unambiguously that men got in, and it is not confounded by
	-- the IsDead ambiguity a Cargo passenger carries.
	WaitUntil(GarrisonWithin,
		function() return OwnerOf(House) == "USA" and CountShelter() + CountPorts() >= 8 end,
		WaitForEveryPortToBeManned,
		function()
			if OwnerOf(House) ~= "USA" then
				Test.Skip("the house never became USA-owned within " .. GarrisonWithin .. "s, so " ..
					"nobody got in. Either the men could not path one cell, or DynamicOwnership " ..
					"stopped claiming neutral buildings on entry (GarrisonManager.cs:263-267). " ..
					State())
				return
			end

			-- Fewer than 8 inside is still workable as long as both compartments are occupied when
			-- phase 1b checks; fall through rather than skipping on a headcount.
			WaitForEveryPortToBeManned()
		end)
end

WorldLoaded = function()
	Men = { Man0, Man1, Man2, Man3, Man4, Man5, Man6, Man7, Man8, Man9 }

	TestHarness.FocusBetween(House, BaitSE)

	if OwnerOf(House) ~= "Neutral" then
		Test.Skip("the house did not start Neutral (owner " .. OwnerOf(House) .. "), so " ..
			"DynamicOwnership's claim-on-entry could not be used as the setup proof. Check " ..
			"map.yaml. " .. State())
		return
	end

	-- Through the REAL targeter, not Test.IssueEnterTransport: ClickOrder walks the same
	-- IIssueOrder/IOrderTargeter pipeline the UI cursor resolver walks, so a scenario cannot stage
	-- an entry the game would never permit. (soldier.EnterTransport moves nobody in scenarios.)
	local refused = 0
	for _, m in ipairs(Men) do
		if Test.ClickOrder(m, House) ~= "EnterTransport" then
			refused = refused + 1
		end
	end

	if refused > 0 then
		Test.Skip(refused .. " of 10 riflemen were not offered EnterTransport against a NEUTRAL " ..
			"house, so the ordinary garrisoning case could not even be staged and nothing about the " ..
			"ownership flip was measured. EnterAlliedActorTargeter admits allied OR neutral owners " ..
			"(EnterAlliedActorTargeter.cs:49-54); if it is refusing here, that gate has changed. " ..
			State())
		return
	end

	WaitForTheGarrison()
end
