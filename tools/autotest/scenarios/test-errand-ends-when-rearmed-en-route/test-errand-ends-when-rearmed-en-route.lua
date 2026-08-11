-- AUTO TEST: an automatically-assigned errand that has become pointless must end.
--
-- The report, from live play: "I saw a soldier that was on its way to resupply when a supply truck
-- drove by to rearm him, but even with full ammo he kept moving towards the supply actor it was
-- heading for before."
--
-- Nothing here issues an order. The Hunter starts with an empty pool, so AutoSeekSupplies'
-- ReturnWhenEmpty tick (or AmmoPool's own INotifyBecomingIdle -- either dispatcher queues the same
-- activity) picks the only actor his RearmActors list names, the Supply truck 28 cells west, and
-- queues SeekSupplyProvider. Five cells into that walk he crosses the Cache's push aura and is
-- handed 250 rounds by a source he never asked for and was never walking to.
--
-- The mechanism, which is NOT the recorded moveQueued latch (that one wedges a unit standing
-- STILL; this report is a unit that keeps WALKING): SeekSupplyProvider never set
-- ChildHasPriority = false, and Activity.TickOuter runs
--     lastRun = TickChild(self) && (finishing || Tick(self))          (Activity.cs:112)
-- so the parent's Tick is skipped for as long as a child is alive. Every re-evaluation the
-- activity contains -- the rearm-complete bail, the retarget -- has only ever run once the move
-- had already finished. And the bail it does contain asks for EVERY pool full, which a passing
-- provider handing over one batch does not satisfy anyway.
--
-- Geometry, so the verdict is unambiguous rather than a stop-tolerance argument:
--   x=34  Hunter's origin
--   x=29  served by the Cache (4c0 aura from 26,17 covers the line for x in [23,29])
--   x=22  FAIL line -- seven cells past the top-up, only reachable by continuing the errand
--   x=11  where the errand would end up, in the Supply truck's 5c0 aura
-- Pass needs him back at x>=32, i.e. home within MoveTo's 2-cell HomeNearEnough.
--
-- There is no enemy anywhere on this map on purpose. The recently-fixed SmartMoveActivity
-- interrupt is ammo-aware now, but entangling this measurement with it would mean a regression
-- there could only show up here as a confusing red. Silencing by fire stance is not available
-- either (AUTOTEST.md gotcha 7).

local DeadlineSeconds = 50
local ServedLine = 29 -- east edge of the Cache's aura along the walking line
local FailLine = 22 -- unambiguously still marching to the original target
local HomeLine = 32 -- origin 34, less MoveTo's 2-cell HomeNearEnough

local wasRearmed = false

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Cache)
	TestHarness.Select(Hunter)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: Hunter died" end
		if Supply.IsDead then return "fail: the errand's target truck died" end

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
				.. "original supply truck -- reached x=" .. x
				.. ", errand outlived the reason for it"
		end

		return wasRearmed and x >= HomeLine
	end, "The rearmed soldier never got back to where the errand took him from")
end
