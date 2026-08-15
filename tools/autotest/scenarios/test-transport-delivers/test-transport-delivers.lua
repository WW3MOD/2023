-- ############################################################################################
-- THIS INSTRUMENT IS NOT YET VALID. DO NOT TRUST A GREEN FROM IT UNTIL THE BELOW IS RESOLVED.
--
-- Measured 2026-08-15 (run 260815_125124_p67532, DefaultCash 0, carriers-total=1 so the placed
-- bradley at 8,18 is provably the only carrier and therefore provably the `BotCarrier` global):
-- the module's own log recorded `depart carrier=bradley aboard=1` at tick 1015 and `aboard=2` at
-- 2515, while THIS predicate reported peakPax=0 and everCarried=0 for the whole run. Both readings
-- cannot be right. The engine reads Cargo.PassengerCount and Lua reads cargo.Passengers.Count() on
-- the same trait, the closure demonstrably ran (it produced this file's failure string at the
-- deadline), and the carrier was neither dead nor duplicated.
--
-- So the predicate is not observing the actor it names, and the cause is NOT yet identified. The
-- leading suspect is the file-scope `local Squad = { BotRifle1, ... }` capture below: map-actor
-- globals may not be bound when the chunk is first executed, which would leave Squad a table of
-- nils with #Squad == 0 — that explains everCarried=0 exactly. It does NOT explain peakPax=0, which
-- reads BotCarrier directly inside the closure, so there is at least one more thing wrong.
--
-- NEXT STEP, cheap and decisive: the engine now emits `[exp-transport] delivered ... pax=N` at the
-- Unloading -> Returning edge, so completed deliveries are provable from debug.log WITHOUT this
-- predicate. Use that as the primary evidence, and repair this file by (a) moving the Squad/Control
-- capture inside WorldLoaded and (b) printing BotCarrier.PassengerCount once per second to lua.log
-- to see what it actually returns.
-- ############################################################################################
--
-- AUTO TEST — a ground transport COMPLETES A DELIVERY before contact.
--
-- The first delivery this project will have observed. Everything before today measured the module
-- failing earlier in the chain: it could not resolve a drop cell at all, so it never created a task;
-- then it created tasks but tore them down every 250 ticks; then it held a task but the run died on
-- an unrelated tank-death guard while the passenger was one cell short.
--
-- WHAT COUNTS AS A DELIVERY, and why it is phrased this way. Three things must all hold, and the
-- third is what makes it a DELIVERY rather than a boarding:
--   (a) CARRIED   — a rifleman was observed OUT OF WORLD, which on this bot only happens inside a
--                   Cargo. A rifleman that walks is never out of world, so no amount of walking can
--                   satisfy this.
--   (b) RETURNED  — that same rifleman is back in the world.
--   (c) MOVED     — it is now at least DeliveredCells from where it started. It was carried
--                   somewhere, not picked up and set back down.
--
-- ATTRIBUTION, per the ownership-window rule banked in AUTOTEST.md on 2026-08-15. A per-actor
-- observable is exclusive to the mechanism under test only while that mechanism owns the actor, so:
--   * the measurement LATCHES AT THE FIRST DELIVERY and is frozen thereafter. A later load of the
--     same carrier by the ordinary frontline path cannot retroactively satisfy it.
--   * there is NO CONTACT in this scenario — no combat units exist at all — so no frontline can
--     form, the frontline branch of PickDropOffCell can never produce a cell, and the pre-contact
--     staging branch is the only path that could have delivered anyone. `via=staged-empty-frontline`
--     in debug.log corroborates that independently of this predicate.
--
-- THE CONTROL IS IN THE SAME RUN. Russia-bot is Bot: stable, whose @poi twin holds
-- DeliverBeforeContact at false; USA-bot is Bot: experimental, where it is true. Identical carrier
-- and identical five passengers on both sides, so the profile is the only difference. The stable
-- side must NOT deliver. If it does, the hold is not doing what its config claims and this fails.
--
-- RED: set DeliverBeforeContact false on MountedTransportBotModule@experimental in
-- mods/ww3mod/rules/ai/ai.yaml — the experimental side then resolves no drop cell, no task is ever
-- created, nobody is ever carried, and this times out with everCarried = 0.

local DeadlineSeconds = 180   -- generous: a passenger walks ~43 ticks/cell and must then ride out
local DeliveredCells = 10     -- how far from its start a returned passenger must be to count

local Squad = { BotRifle1, BotRifle2, BotRifle3, BotRifle4, BotRifle5 }
local Control = { CtlRifle1, CtlRifle2, CtlRifle3, CtlRifle4, CtlRifle5 }

local function CellDistance(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

WorldLoaded = function()
	TestHarness.FocusBetween(BotCarrier, OpponentSR)

	-- Start positions, captured before anything moves.
	local start, ctlStart = {}, {}
	for i = 1, #Squad do start[i] = Squad[i].Location end
	for i = 1, #Control do ctlStart[i] = Control[i].Location end

	local carried, ctlCarried = {}, {}
	local everCarried, ctlEverCarried = 0, 0
	local peakPax, ctlPeakPax = 0, 0
	local delivered = 0
	local bestMoved = 0
	local frozen = false        -- latched at the first delivery; see ATTRIBUTION above

	TestHarness.AssertWithin(DeadlineSeconds, function()
		-- Peak load, frozen once the first delivery lands so a later frontline-path load of the same
		-- carrier cannot inflate the number this run reports.
		-- IsDead is checked FIRST and always: a destroyed actor has no PassengerCount property at all
		-- and reading it is a fatal Lua error that aborts the run with no measurement (cost one run,
		-- 2026-08-15). Cargo on these carriers sets EjectOnDeath, so a carrier CAN die with passengers.
		if not frozen then
			if not BotCarrier.IsDead and BotCarrier.PassengerCount > peakPax then
				peakPax = BotCarrier.PassengerCount
			end

			if not CtlCarrier.IsDead and CtlCarrier.PassengerCount > ctlPeakPax then
				ctlPeakPax = CtlCarrier.PassengerCount
			end
		end

		-- (a) CARRIED — monotonic latch; out of world on this bot means inside a Cargo.
		for i = 1, #Squad do
			local r = Squad[i]
			if r ~= nil and not r.IsDead and not r.IsInWorld and not carried[i] then
				carried[i] = true
				everCarried = everCarried + 1
			end
		end

		for i = 1, #Control do
			local r = Control[i]
			if r ~= nil and not r.IsDead and not r.IsInWorld and not ctlCarried[i] then
				ctlCarried[i] = true
				ctlEverCarried = ctlEverCarried + 1
			end
		end

		-- THE CONTROL IS COUNTED HERE BUT JUDGED FROM THE LOG, deliberately.
		--
		-- The claim being controlled is narrow: the stable side must not deliver via the PRE-CONTACT
		-- path, because DeliverBeforeContact is held false on @poi. It is perfectly correct for the
		-- stable side to deliver later via the ordinary FRONTLINE path once contact forms, and Lua
		-- cannot tell the two apart — it sees only that a passenger vanished. Failing on this counter
		-- would therefore fail spuriously on correct behaviour whenever the run lives long enough for
		-- contact, which is precisely what a long-deadline scenario is built to do.
		--
		-- The authoritative signal is `[exp-transport] no-task ... cause=empty-frontline+fallback-disabled`
		-- for the stable player, and the `via=` field on any task it does create. Measured
		-- 2026-08-15: 21 fallback-disabled passes, and its single task came `via=frontline` at tick
		-- 1286, after contact — the hold verified by measurement rather than by config inspection.
		-- The count is carried into the failure message so a regression here is still visible.

		-- (b) RETURNED and (c) MOVED — and the carrier must be ALIVE when it happens.
		--
		-- The alive check is not defensive, it is part of the definition. Cargo sets EjectOnDeath, so a
		-- carrier destroyed mid-drive spills its passengers into the world far from where they boarded
		-- — which satisfies "carried, returned, moved" perfectly while being the opposite of a
		-- delivery. Requiring the carrier to still exist is what separates an unload from a wreck.
		if not frozen and not BotCarrier.IsDead then
			delivered = 0
			for i = 1, #Squad do
				local r = Squad[i]
				if r ~= nil and carried[i] and not r.IsDead and r.IsInWorld then
					local moved = CellDistance(r.Location, start[i])
					if moved > bestMoved then bestMoved = moved end
					if moved >= DeliveredCells then delivered = delivered + 1 end
				end
			end

			if delivered >= 1 then
				frozen = true
				return true
			end
		end

		-- A carrier lost before it ever delivered is an inconclusive run, not a negative result, and
		-- must say so rather than timing out with a bare zero.
		if BotCarrier.IsDead and not frozen and everCarried > 0 then
			return "fail: the carrier was destroyed after loading " .. everCarried
				.. " passenger(s) but before any of them was set down at least " .. DeliveredCells
				.. " cells from its start. INCONCLUSIVE about delivery — the load worked. Check the "
				.. "depart line in debug.log; if it departed Full, the drive was interrupted by combat "
				.. "and this scenario needs the two sides further apart or poorer."
		end

		return false
	end, "no completed delivery within " .. DeadlineSeconds .. "s. "
		.. "everCarried=" .. everCarried .. " (0 means nobody ever boarded — the transport never ran, "
		.. "so this run measured nothing about delivery); peakPax=" .. peakPax
		.. "; furthest a carried rifleman ended from its start=" .. bestMoved .. "/" .. DeliveredCells
		.. " cells (a positive everCarried with a small distance means it boarded and was set down "
		.. "where it started — loading works, the drive does not); stable-side carried="
		.. ctlEverCarried .. " (expected 0 before contact; check its via= in debug.log if non-zero, "
		.. "since a frontline-path delivery after contact is correct and not a hold violation).")
end
