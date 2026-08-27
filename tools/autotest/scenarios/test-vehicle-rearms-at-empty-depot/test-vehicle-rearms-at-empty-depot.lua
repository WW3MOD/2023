-- AUTO TEST: does a Logistics Centre holding NOTHING still rearm a vehicle?
--
-- THE CLAIM, which was derived from three code sites and is settled here by observation instead.
-- Resupply.cs:131 sets its rearm branch from Rearmable.RearmActors membership alone; the rearm
-- itself is Rearmable.RearmTick (Resupply.cs:301 -> Rearmable.cs:57-78), which calls GiveAmmo and
-- consults no SupplyProvider; and nothing downstream charges for it, because SupplyProvider
-- implements neither INotifyResupply nor INotifyDockHost — the only two hooks Resupply fires at the
-- host, whose sole engine-wide implementers are two render traits. If that reading is right, a tank
-- refills at a depot holding zero and the depot's supply never moves.
--
-- WHY THE ERRAND IS ISSUED BY HAND. Every ordinary route to that activity — the Resupply order,
-- AmmoPool.AutoRearmIfDry, AutoSeekSupplies — runs ChooseResupplier first, and that filters
-- candidates on CurrentSupply > 0. Against a zeroed depot they all decline to send the tank, so the
-- run would measure the DISPATCHER'S FILTER and never reach the question. Test.IssueResupplyAt
-- exists to put the unit at the depot with no chooser in the way. Keeping those two things apart is
-- the correction that made this test necessary: a drained depot refusing to be CHOSEN as a
-- destination is not the same fact as a drained depot being unable to SERVE.
--
-- BOTH HALVES ARE ASSERTED. Ammunition arriving is only half the claim; the other half is that the
-- depot pays nothing for it. A run where ammo climbs and supply falls would refute the finding just
-- as thoroughly as one where no ammo arrives, and it fails here with its own message rather than
-- passing quietly.

local DeadlineSeconds = 60
local AmmoPoolName = "primary-ammo"

local pollCount = 0
local supplyEverMoved = false
local peakAmmo = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Tank, Depot)
	TestHarness.Select(Tank)

	Test.SetSupply(Depot, 0)

	local load = Test.GetSupply(Depot)
	if load ~= 0 then
		Test.Fail(string.format(
			"setup failed: the depot holds %d supply rather than 0, so a rearm here proves nothing " ..
			"about an EMPTY depot", load))
		return
	end

	local startingAmmo = Tank.AmmoCount(AmmoPoolName)
	if startingAmmo ~= 0 then
		Test.Fail(string.format(
			"setup failed: the tank starts with %d rounds, so ammunition appearing later is not " ..
			"necessarily ammunition it was GIVEN", startingAmmo))
		return
	end

	-- The errand itself, with no host selection involved: the depot is named outright.
	Test.IssueResupplyAt(Tank, Depot)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Tank.IsDead then return "fail: the tank died before reaching the depot" end
		if Depot.IsDead then return "fail: the depot died or despawned" end

		local ammo = Tank.AmmoCount(AmmoPoolName)
		local supply = Test.GetSupply(Depot)

		if ammo > peakAmmo then peakAmmo = ammo end
		if supply ~= 0 then supplyEverMoved = true end

		pollCount = pollCount + 1
		if pollCount % 50 == 0 then
			-- Live numbers belong here, never in the failure string, which Lua evaluates eagerly at
			-- registration and would report the starting values forever.
			print(string.format("[free-rearm] poll=%d ammo=%d peak=%d supply=%d",
				pollCount, ammo, peakAmmo, supply))
		end

		-- The refutation branch. SetSupply clamps to [0, TotalSupply] and nothing on this map can
		-- add supply, so any movement at all means the rearm is metered after all and the whole
		-- finding is wrong.
		if supplyEverMoved then
			return string.format(
				"fail: the depot's supply moved to %d, so the rearm IS metered and the free-rearm " ..
				"reading is refuted", supply)
		end

		return ammo > 0
	end, "The tank never gained a round at a Logistics Centre holding zero supply. Either the " ..
		"docking rearm is metered after all, or a vehicle cannot reach the dock-tight WDist.Zero " ..
		"tolerance at a building and the path is unreachable in practice -- the poll trace in " ..
		"lua.log separates those two, since an unreachable dock leaves the tank short of the depot.")
end
