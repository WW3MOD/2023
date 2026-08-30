--[[
   TEST: an evacuating launcher passes a poor depot to reach an affordable one.

   THE RULING THIS DEFENDS (user, 2026-08-21): "they should still go to any nearby resupply first if
   it is available, otherwise they evacuate". A unit on the Evacuate disposition that goes dry is
   entitled to a detour before it leaves. The defect was that the arm chose its detour target with
   ChooseResupplier -- the nearest host holding ANY stock -- and only THEN asked whether that host
   could pay for a batch. A near-but-poor depot therefore SHADOWED a farther affordable one: the
   affordability question was put to the wrong actor, answered no, and the launcher fell through to
   EvacuateForRefund and left the map permanently, with a depot that would have served it sitting
   half the leash away.

   WHAT THIS SCENARIO MEASURES, stated as the thing that can fail: the launcher must end up holding
   a round that FarRichDepot paid for. Not merely "it rearmed" -- see the trap below.

   THE TRAP, and why the verdict reads depot supply rather than just ammunition. The mechanism under
   test is the comparison `provider.CurrentSupply >= p.Info.SupplyValue`
   (AmmoPool.HostCanAffordSomethingWeNeed). If SupplyValue ever resolved to 0 through inheritance --
   which has happened in this repo, where a pin degraded an assertion to `x > 0` and passed happily
   with the bug sabotaged back in -- then EVERY depot becomes affordable, the filter is a no-op, the
   nearest wins, and the launcher rearms at NearPoorDepot. A verdict of `ammo > 0` would call that a
   pass. So the pass condition names FarRichDepot's supply falling, and a separate early-fail fires
   the moment NearPoorDepot's supply falls. Between them the two arms distinguish "chose correctly"
   from "the arithmetic stopped discriminating".

   `Launcher`, `NearPoorDepot` and `FarRichDepot` are the actor keys from map.yaml, which OpenRA
   exposes to map scripts as globals; they are not declared in this file.
]]

-- Held at 50 against m270's SupplyValue 70: stocked enough to be a candidate (RearmCandidates
-- filters on CurrentSupply > 0), too poor to pay for a batch. The 1..69 band is the mechanism.
local NearLoad = 50
-- A full Logistics Centre (TotalSupply 2250), set explicitly rather than assumed so the scenario
-- does not silently change meaning if that default is ever retuned.
local FarLoad = 2250
local AmmoPoolName = "primary-ammo"

-- One cell south. Re-triggers the becoming-idle transition AFTER the supplies above are in place,
-- so the decision under test is taken against the staged numbers rather than against the depots'
-- default full load at tick 0. Chosen so no distance changes: from 30,17 the depots are still 4 and
-- 8 cells away (chessboard) and the exit anchor still 25.
local PrimeCell = CPos.New(30, 17)

local DeadlineSeconds = 60

-- SELF-COUNTED DEADLINE, one second inside AssertWithin's own, and it exists for a specific reason:
-- AssertWithin's timeoutReason is an ordinary string ARGUMENT, so a string.format built at call time
-- freezes every number in it at its t=0 value. A timeout reported that way would state "0 rounds,
-- depot at 2250" no matter what actually happened. Failing from inside the predicate instead is the
-- only way to get live numbers into result.json, which is the artefact the verdict is read from.
local DeadlineTicks = math.floor((DeadlineSeconds - 1) * TestHarness.TicksPerSecond)

local pollCount = 0
local peakAmmo = 0
local minFarSupply = FarLoad

WorldLoaded = function()
	Test.SetSupply(NearPoorDepot, NearLoad)
	Test.SetSupply(FarRichDepot, FarLoad)

	-- SETUP VERIFICATION. Each of these is a precondition the verdict silently depends on, and each
	-- has a failure mode that would otherwise masquerade as a result rather than a broken stage.
	local near0 = Test.GetSupply(NearPoorDepot)
	local far0 = Test.GetSupply(FarRichDepot)
	if near0 ~= NearLoad or far0 ~= FarLoad then
		Test.Fail(string.format(
			"setup failed: depots hold %d (near) and %d (far), not the %d and %d this test needs. " ..
			"Without that gap there is nothing for affordability to discriminate between.",
			near0, far0, NearLoad, FarLoad))
		return
	end

	-- An InitialAmmo that failed to apply leaves the launcher armed and never dry, so it never enters
	-- AutoRearmIfDry at all and the scenario would time out blaming code it never reached.
	local ammo0 = Launcher.AmmoCount(AmmoPoolName)
	if ammo0 ~= 0 then
		Test.Fail(string.format(
			"setup failed: the launcher holds %d round(s) at t=0, not 0. The InitialAmmo: 0 override " ..
			"did not apply, so it is not dry and never reaches the evacuate arm.", ammo0))
		return
	end

	TestHarness.FocusBetween(Launcher, FarRichDepot)
	TestHarness.Select(Launcher)

	Launcher.Move(PrimeCell)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		pollCount = pollCount + 1

		local near = Test.GetSupply(NearPoorDepot)
		local far = Test.GetSupply(FarRichDepot)
		if far < minFarSupply then minFarSupply = far end

		-- THE PRE-FIX OUTCOME. RotateToEdge disposes the actor at the map edge, and nothing on this map
		-- can shoot it, so IsDead can only mean it evacuated. Both depot loads are reported because they
		-- are what make the failure attributable: near still at 50 proves it took nothing on the way
		-- out, far still at 2250 proves the affordable depot was never consulted.
		if Launcher.IsDead then
			return string.format(
				"fail: the launcher LEFT THE MAP -- it evacuated for a refund rather than detouring. " ..
				"At departure the poor depot 4 cells away held %d (below SupplyValue 70, correctly " ..
				"unaffordable) and the affordable depot 8 cells away held %d, untouched. The arm chose " ..
				"its detour target by proximity and only then asked about affordability, so the far " ..
				"depot was never a candidate.", near, far)
		end

		local ammo = Launcher.AmmoCount(AmmoPoolName)
		if ammo > peakAmmo then peakAmmo = ammo end

		-- THE TRAP DETECTOR, not a redundant restatement of the pass condition. The poor depot cannot
		-- pay for a batch while SupplyValue is 70; if its supply drops, the launcher docked THERE, which
		-- means the affordability comparison stopped discriminating. Catching that here rather than
		-- letting `ammo > 0` absorb it is the whole reason this scenario reads supply and not ammunition
		-- alone.
		if near < NearLoad then
			return string.format(
				"fail: the launcher rearmed at the POOR depot -- its supply fell from %d to %d while the " ..
				"launcher gained %d round(s). A depot holding less than SupplyValue 70 must not be able " ..
				"to serve an m270, so the affordability comparison is not discriminating at all. Suspect " ..
				"SupplyValue resolving to 0 through inheritance before suspecting the selection code.",
				NearLoad, near, ammo)
		end

		if pollCount % 50 == 0 then
			print(string.format(
				"[evac-afford] poll=%d ammo=%d peak=%d near=%d far=%d minFar=%d",
				pollCount, ammo, peakAmmo, near, far, minFarSupply))
		end

		if pollCount >= DeadlineTicks then
			return string.format(
				"fail: the launcher neither rearmed nor left within %ds. Still on the map holding %d " ..
				"round(s) (peak %d), poor depot at %d, affordable depot at %d of %d. Neither of the two " ..
				"things a dry unit may do happened -- that is a stall, not the shadowing defect this " ..
				"test targets, so read it as a broken stage or a changed dispatch path before reading " ..
				"it as this bug returning.",
				DeadlineSeconds, ammo, peakAmmo, near, minFarSupply, FarLoad)
		end

		-- THE PASS. Both conjuncts are load-bearing: the ammunition proves the launcher was actually
		-- served, and the far depot's supply falling proves THAT depot is what served it.
		return ammo > 0 and far < FarLoad
	end, "the launcher neither rearmed nor left, and the scenario's own deadline did not fire first " ..
		"-- treat this as a harness fault rather than a verdict.")
end
