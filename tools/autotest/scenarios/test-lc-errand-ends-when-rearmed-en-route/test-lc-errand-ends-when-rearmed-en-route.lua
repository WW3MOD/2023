-- AUTO TEST: the LOGISTICS CENTRE half of the same report the sibling scenario covers.
--
-- The report, from live play: "I saw a soldier that was on its way to resupply when a supply truck
-- drove by to rearm him, but even with full ammo he kept moving towards the supply actor it was
-- heading for before. When soldiers are on an automatically assigned return to rearm order, and
-- they find ammo along the way, they should stop and return to the fight, or at least become idle.
-- But best would be if they go back to where they came from."
--
-- test-errand-ends-when-rearmed-en-route fixed this for a TRUCK/CACHE destination, which runs on
-- SeekSupplyProvider. Infantry name BOTH hosts (`RearmActors: truk, logisticscenter`) and
-- ChooseResupplier takes whichever is nearer, so in a real match the very same report lands on the
-- other branch whenever the depot is closer -- and that branch was untouched.
--
-- THE MECHANISM, and it is a different one from the sibling's. AmmoPool.AutoRearm sends a unit to a
-- SupplyProvider WITHOUT a docking gate via SeekSupplyProvider, and everything else via the stock
-- Resupply activity (AmmoPool.cs:349/373). The LC sets DockedCondition: unit.docked, so it takes
-- the second branch. Resupply decides ONCE, in its constructor, what it is going for:
--
--     var cannotRearmAtHost = rearmable == null || !RearmActors.Contains(host) ||
--                             rearmable.RearmableAmmoPools.All(p => p.HasFullAmmo);
--     if (!cannotRearmAtHost) activeResupplyTypes |= ResupplyType.Rearm;      (Resupply.cs:85-87)
--
-- and then never asks again while walking: Tick returns early on
-- `activeResupplyTypes != 0 && !isCloseEnough` (:139) for the whole approach. The set is frozen, so
-- a man topped up by a passer-by is still carrying the reason he set off with.
--
-- Geometry, so the verdict is unambiguous rather than a stop-tolerance argument:
--   x=34  Hunter's origin
--   x=29  served by the Cache (4c0 aura from 26,17 covers the line for x in [23,29])
--   x=22  FAIL line -- seven cells past the top-up, only reachable by continuing the errand
--   x~9   the Depot, where the errand would end up
-- Pass needs him back at x>=32, i.e. home within MoveTo's 2-cell HomeNearEnough.
--
-- The FAIL line is also what keeps the Depot's OWN aura out of the measurement: the LC grants
-- replenish-soldiers within 4c0 (structures.yaml:381-384), which drives the soldier's ReloadAmmoPool
-- trickle. That only reaches x<=12 or so, well past the point this test has already called it.
--
-- There is no enemy anywhere on this map on purpose, matching the sibling: entangling this
-- measurement with the ammo-aware SmartMoveActivity interrupt would mean a regression there could
-- only show up here as a confusing red. Silencing by fire stance is not available either
-- (AUTOTEST.md gotcha 7).

local DeadlineSeconds = 50
local FailLine = 22 -- unambiguously still marching to the original target
local HomeLine = 32 -- origin 34, less MoveTo's 2-cell HomeNearEnough

local wasRearmed = false

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Cache)
	TestHarness.Select(Hunter)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died" end
		if Depot.IsDead then return "fail: the errand's target depot died" end

		local ammo = Hunter.AmmoCount("primary-ammo")
		local x = Hunter.Location.X

		-- He starts dry at x=34 and the Cache's aura stops at x=29, so any ammo at all proves he
		-- set off, walked, and was topped up by the provider he was NOT walking to.
		if ammo > 0 then wasRearmed = true end

		if x <= FailLine then
			if not wasRearmed then
				-- Not the bug: he walked the whole aura and nobody served him, so the scenario is
				-- staging nothing. Say so rather than reporting a pass/fail about the errand.
				return "fail: SETUP -- reached x=" .. x .. " with 0 rounds; the passing cache never "
					.. "served him, so this run measures nothing about abandoning an errand"
			end

			return "fail: rearmed en route (" .. ammo .. " rounds) and still walking to the "
				.. "logistics centre -- reached x=" .. x
				.. ", errand outlived the reason for it"
		end

		return wasRearmed and x >= HomeLine
	end, "The rearmed soldier never got back to where the LC errand took him from")
end
