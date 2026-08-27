-- AUTO TEST: a Logistics Centre holding NOTHING must rearm nobody, and must not TRAP the client.
--
-- RE-POINTED 2026-08-27. This scenario was written to prove the opposite — that the docking rearm
-- was free — and it did prove it, which is why the charging change exists. Its verdict is now
-- inverted and its second clause (the tank must undock and leave) is the one that earns its keep.
-- The original framing is kept below because it is the evidence the change rests on.
--
-- THE ORIGINAL CLAIM, derived from three code sites and then settled by observation.
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
local everDocked = false
local departed = false

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

		-- Chessboard distance to the depot, latched: "arrived then left" is the pass shape, so a
		-- bare distance test at the deadline could not tell it from "never set off".
		local dx = Tank.Location.X - Depot.Location.X
		local dy = Tank.Location.Y - Depot.Location.Y
		if dx < 0 then dx = -dx end
		if dy < 0 then dy = -dy end
		local dist = dx > dy and dx or dy
		if dist <= 2 then everDocked = true end
		if everDocked and dist > 2 then departed = true end

		pollCount = pollCount + 1
		if pollCount % 50 == 0 then
			-- Live numbers belong here, never in the failure string, which Lua evaluates eagerly at
			-- registration and would report the starting values forever.
			print(string.format("[free-rearm] poll=%d ammo=%d peak=%d supply=%d docked=%s departed=%s",
				pollCount, ammo, peakAmmo, supply, tostring(everDocked), tostring(departed)))
		end

		-- The refutation branch. SetSupply clamps to [0, TotalSupply] and nothing on this map can
		-- add supply, so any movement at all means the rearm is metered after all and the whole
		-- finding is wrong.
		if supplyEverMoved then
			return string.format(
				"fail: the depot's supply moved to %d, so the rearm IS metered and the free-rearm " ..
				"reading is refuted", supply)
		end

		-- RE-POINTED 2026-08-27, and the verdict is INVERTED from what this file originally asserted.
		-- It was written to prove the docking rearm was free, and it did: a dry abrams refilled here
		-- at a Centre holding ZERO with the depot's supply unmoved. That hole is now closed, so the
		-- correct expectation is the opposite one — nothing arrives — and the scenario is kept rather
		-- than deleted precisely because a green here would mean the free path had returned.
		--
		-- The second clause is the part that matters more. A depot that cannot pay must make the tank
		-- LEAVE, not wait: once docked, Rearmable.RearmTick returning true is the only exit
		-- (Resupply.cs:301), and a tank that stands there is combat-inert and withheld from every bot
		-- module by StarvingRecruitGate for the rest of the match.
		if ammo > 0 then
			return string.format(
				"fail: the tank gained %d round(s) at a Centre holding %d — the free docking rearm has " ..
				"returned", ammo, supply)
		end

		return departed
	end, "The tank never left the depot. It correctly received nothing from a Centre holding zero, " ..
		"then stayed docked instead of giving up — which is the wedge: combat-inert, IsSeekingRearm " ..
		"true, withheld from every bot module. RearmTick must report the errand DONE when the host " ..
		"cannot pay, rather than deferring forever.")
end
