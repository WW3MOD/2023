-- AUTO TEST: can a combat engineer with FULL C4 ever refill his rifle again?
--
-- THE REGRESSION. Before 9e46f141, LOGISTICSCENTER carried a bare
-- ProximityExternalCondition@ReplenishSoldiers that granted replenish-soldiers to everyone within
-- 4c0 unconditionally, and every soldier's ReloadAmmoPool is gated on that condition. That grant is
-- deleted (structures.yaml:455-468). The condition now comes from SupplyProvider alone, and only to
-- the single client it has SELECTED (SyncTargetCondition, SupplyProvider.cs:939-967).
--
-- Selection runs through AcceptClient (SupplyProvider.cs:743-758), which reads demand out of
-- Rearmable.RearmableAmmoPools — the pools named in Rearmable.AmmoPools (Rearmable.cs:44). The
-- engineer lists secondary-ammo ALONE (infantry.yaml, ^E6), so an engineer with full C4 presents
-- NoDemand however empty his rifle is; he is never selected, never granted the condition, and
-- ReloadAmmoPool@1 (infantry.yaml:1878-1881) never runs. His rifle never refills again.
--
-- WHY THE C4 MUST BE FULL, and why this is easy to miss by hand. While the engineer is being served
-- for his CHARGES he holds replenish-soldiers, and the rifle trickles as a side effect. So the bug
-- is invisible in every state except the one he is normally in.
--
-- THE VERDICT IS AMMUNITION, NOT POSITION. Nothing moves an undocked ground unit away from a
-- Logistics Centre, and a distance-based verdict on this branch already scored a wedge as healthier
-- than its repair (see test-who-pays-for-a-rearm:158-170). Both subjects stand still all run; the
-- only thing asked of them is whether rounds arrive.
--
-- THE MEASURED-SOMETHING GUARD is the eastern control, not a comment. An AR at an identical stocked
-- Centre at the identical offset, differing only in that HIS primary-ammo is listed in his
-- Rearmable. If he gains nothing the delivery chain did not run at all and the engineer's zero is
-- uninformative, so the run reports "measured nothing" rather than confirming the bug.

local EvalTicks = 600
local SnapshotEvery = 50

local FullLoad = 2250          -- LOGISTICSCENTER TotalSupply, structures.yaml:475
local AuraCells = 4            -- AuraRange 4c0, structures.yaml:503
local EngineerRifleFull = 100  -- ^E6 AmmoPool@1 Ammo, infantry.yaml:1873
local EngineerC4Full = 3       -- ^E6 AmmoPool@2 Ammo, infantry.yaml:1902

local pollCount = 0

local function chebyshev(a, b)
	local dx = a.X - b.X
	local dy = a.Y - b.Y
	if dx < 0 then dx = -dx end
	if dy < 0 then dy = -dy end
	return dx > dy and dx or dy
end

-- DISTANCE TO THE BUILDING'S CENTRE, not to its Location corner, and the difference is the whole
-- reason the first run of this scenario measured nothing. LOGISTICSCENTER is 3x3 (structures.yaml
-- `Dimensions: 3,3`) and SupplyProvider.InAuraRange compares against the provider's CenterPosition
-- (SupplyProvider.cs:1399-1402), so a subject 3 cells from the Location corner is between 2.12 and
-- 4.74 cells from the centre depending on WHICH SIDE it stands on. The corner distance reads 3 in
-- both cases — a number that looks correct and is measuring the wrong thing, which is exactly the
-- false-guard shape AUTOTEST.md warns about. Location + (1,1) is the centre cell of a 3x3.
local function distToCentre(unit, depot)
	local c = depot.Location
	return chebyshev(unit.Location, { X = c.X + 1, Y = c.Y + 1 })
end

WorldLoaded = function()
	TestHarness.FocusBetween(Engineer, EngineerDepot)
	TestHarness.Select(Engineer)

	Test.SetSupply(EngineerDepot, FullLoad)
	Test.SetSupply(ControlDepot, FullLoad)

	if Test.GetSupply(EngineerDepot) ~= FullLoad or Test.GetSupply(ControlDepot) ~= FullLoad then
		Test.Fail(string.format(
			"setup failed: depots read %d and %d, want %d each — neither could pay for a rearm, so " ..
			"nothing that follows is a statement about selection",
			Test.GetSupply(EngineerDepot), Test.GetSupply(ControlDepot), FullLoad))
		return
	end

	-- THE SETUP ASSERTION THAT MATTERS. An engineer whose C4 is not full is being served for the
	-- charges, holds replenish-soldiers for that reason, and trickles rifle rounds as a side effect
	-- — so his rifle filling would prove nothing about the defect. Checked, never assumed.
	local startRifle = Engineer.AmmoCount("primary-ammo")
	local startC4 = Engineer.AmmoCount("secondary-ammo")
	if startRifle ~= 0 or startC4 ~= EngineerC4Full then
		Test.Fail(string.format(
			"setup failed: engineer starts rifle=%d (want 0) c4=%d (want %d). A non-full C4 pool " ..
			"makes him a legitimate client for his charges, which grants the condition and refills " ..
			"the rifle incidentally — the exact shape that hides this bug",
			startRifle, startC4, EngineerC4Full))
		return
	end

	if Control.AmmoCount("primary-ammo") ~= 0 then
		Test.Fail(string.format(
			"setup failed: the control rifleman starts with %d rounds rather than 0, so rounds in " ..
			"his pool at the deadline would not prove the depot served anyone",
			Control.AmmoCount("primary-ammo")))
		return
	end

	-- Self-rescheduling snapshot. There is NO Trigger.OnTick in this engine, so a periodic trace has
	-- to be a delay that re-arms itself. Live counters go HERE and never into an AssertWithin failure
	-- string, which Lua evaluates eagerly at registration and which would therefore report the
	-- starting values forever.
	local function snapshot()
		pollCount = pollCount + SnapshotEvery
		print(string.format(
			"[eng-rifle] tick=%d | engineer rifle=%d c4=%d dist=%d depot=%d | control rifle=%d dist=%d depot=%d",
			pollCount,
			Engineer.IsDead and -1 or Engineer.AmmoCount("primary-ammo"),
			Engineer.IsDead and -1 or Engineer.AmmoCount("secondary-ammo"),
			Engineer.IsDead and -1 or distToCentre(Engineer, EngineerDepot),
			Test.GetSupply(EngineerDepot),
			Control.IsDead and -1 or Control.AmmoCount("primary-ammo"),
			Control.IsDead and -1 or distToCentre(Control, ControlDepot),
			Test.GetSupply(ControlDepot)))

		if pollCount < EvalTicks then
			Trigger.AfterDelay(SnapshotEvery, snapshot)
		end
	end

	Trigger.AfterDelay(SnapshotEvery, snapshot)

	Trigger.AfterDelay(EvalTicks, function()
		if Engineer.IsDead or Control.IsDead then
			Test.Fail("a subject left the world before evaluation, so nothing was measured")
			return
		end

		local rifle = Engineer.AmmoCount("primary-ammo")
		local c4 = Engineer.AmmoCount("secondary-ammo")
		local engDist = distToCentre(Engineer, EngineerDepot)
		local engDepot = Test.GetSupply(EngineerDepot)

		local ctrl = Control.AmmoCount("primary-ammo")
		local ctrlDist = distToCentre(Control, ControlDepot)

		local summary = string.format(
			"engineer rifle=%d/%d c4=%d dist=%d, his depot holds %d of %d (spent %d) | control " ..
			"rifle=%d dist=%d",
			rifle, EngineerRifleFull, c4, engDist, engDepot, FullLoad, FullLoad - engDepot,
			ctrl, ctrlDist)

		print("[eng-rifle] RESULT " .. summary)

		-- Guards first. Each turns an apparent answer into "measured nothing".
		if engDist > AuraCells then
			Test.Fail(string.format(
				"measured nothing: the engineer is %d cells from his Centre, outside the %d-cell " ..
				"aura, so an unrefilled rifle says nothing about selection. %s",
				engDist, AuraCells, summary))
			return
		end

		if engDepot <= 0 then
			Test.Fail(string.format(
				"measured nothing: the engineer's Centre is drained (%d), so AcceptClient would " ..
				"return Unaffordable and decline him for a reason unrelated to the pool list. %s",
				engDepot, summary))
			return
		end

		-- THE MEASURED-SOMETHING GUARD. The control differs from the subject in exactly one way:
		-- his pool is listed in his Rearmable. If HE was not served, the aura arm did not run in
		-- this world at all and the engineer's zero is not evidence of anything.
		if ctrl == 0 then
			Test.Fail(string.format(
				"measured nothing: the CONTROL rifleman gained nothing either, at a stocked Centre " ..
				"%d cells away with his pool correctly listed. The delivery chain never ran in this " ..
				"run, so the engineer's empty rifle is a staging failure and not the defect. %s",
				ctrlDist, summary))
			return
		end

		if rifle > 0 then
			Test.Pass()
			return
		end

		Test.Fail(
			"the engineer's rifle gained NOTHING beside a stocked Centre while the control rifleman " ..
			"beside an identical one was served, so the delivery chain works and the engineer alone " ..
			"is invisible to it. His primary-ammo pool is absent from Rearmable.AmmoPools " ..
			"(infantry.yaml, ^E6), so AcceptClient reads NoDemand from his full C4, he is never " ..
			"selected, he is never granted replenish-soldiers, and ReloadAmmoPool@1 never runs. " ..
			summary)
	end)
end
