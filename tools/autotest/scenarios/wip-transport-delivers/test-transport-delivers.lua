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

-- CAPTURED INSIDE WorldLoaded, NOT HERE. Map-actor globals are not guaranteed to be bound when this
-- chunk is first executed, so a file-scope `{ BotRifle1, ... }` can be a table of five nils with
-- #Squad == 0 — every loop over it then silently does nothing and every count stays 0. That is the
-- suspected cause of the everCarried=0 reading on 2026-08-15 and it is a silent failure: no error,
-- no warning, just a predicate that examines an empty list forever.
local Squad, Control

local function CellDistance(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

WorldLoaded = function()
	TestHarness.FocusBetween(BotCarrier, OpponentSR)

	-- Bind here, where map-actor globals are guaranteed live.
	Squad = { BotRifle1, BotRifle2, BotRifle3, BotRifle4, BotRifle5 }
	Control = { CtlRifle1, CtlRifle2, CtlRifle3, CtlRifle4, CtlRifle5 }

	-- SELF-CHECK: refuse to run against an empty or short squad. A predicate that examines nothing
	-- reports a confident zero, which is indistinguishable from the behaviour being absent — the
	-- precise failure this scenario suffered. Fail loudly at tick 0 instead.
	if Squad == nil or #Squad ~= 5 or Control == nil or #Control ~= 5 then
		TestHarness.AssertWithin(1, function()
			return "fail: SETUP DID NOT BIND — #Squad=" .. tostring(Squad and #Squad)
				.. " #Control=" .. tostring(Control and #Control)
				.. ", expected 5 and 5. The predicate would have measured an empty list and reported "
				.. "zero for everything. Check the actor names in map.yaml against this file."
		end, "unreachable")
		return
	end

	-- Start positions, captured before anything moves.
	local start, ctlStart = {}, {}
	for i = 1, #Squad do start[i] = Squad[i].Location end
	for i = 1, #Control do ctlStart[i] = Control[i].Location end

	-- Periodic trace of what THIS predicate actually reads, so a disagreement with the module's own
	-- `[exp-transport] depart aboard=N` line names which side is wrong instead of leaving a
	-- contradiction. Goes to lua.log; a 0-byte lua.log means this never ran.
	local ticks = 0

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
		ticks = ticks + 1

		if not frozen then
			if not BotCarrier.IsDead and BotCarrier.PassengerCount > peakPax then
				peakPax = BotCarrier.PassengerCount
			end

			if not CtlCarrier.IsDead and CtlCarrier.PassengerCount > ctlPeakPax then
				ctlPeakPax = CtlCarrier.PassengerCount
			end
		end

		-- ~every 3s. Cheap, and it is what turns "the two readings disagree" into "here is what each
		-- side saw at the same moment".
		if ticks % 50 == 0 then
			local live = 0
			for i = 1, #Squad do
				if Squad[i] ~= nil and not Squad[i].IsDead and Squad[i].IsInWorld then live = live + 1 end
			end

			-- PER-MEMBER state, which the aggregate above cannot give. The open question this settles:
			-- the module logged three completed deliveries on 2026-08-15, yet no rifleman satisfied
			-- RETURNED + MOVED >= 10 cells, and the two candidate explanations need different fixes —
			-- the delivered riflemen died shortly after being set down (contact arrives ~tick 1300), or
			-- the moved clause is mis-measuring. `w` (in world) with a live `d` (cells from its start)
			-- says delivered-and-alive-but-short; `w=n` after a latch says it died; `d` never rising
			-- says it was never taken anywhere.
			--
			-- Location is read ONLY when in world. A carried passenger is out of world AND reports dead
			-- (the latch below depends on that), and reading position off an actor in that state is the
			-- class of Lua error that aborts a run with no measurement at all.
			local per = ""
			for i = 1, #Squad do
				local r = Squad[i]
				local inWorld = r ~= nil and r.IsInWorld
				local d = "-"
				if inWorld then d = tostring(CellDistance(r.Location, start[i])) end
				per = per .. " r" .. i .. "=" .. (inWorld and "w" or "n")
					.. "/c" .. (carried[i] and "1" or "0") .. "/d" .. d
			end

			print("[deliv] tick~" .. ticks
				.. " carrierDead=" .. tostring(BotCarrier.IsDead)
				.. " pax=" .. tostring(BotCarrier.IsDead and -1 or BotCarrier.PassengerCount)
				.. " peakPax=" .. peakPax
				.. " squadInWorld=" .. live .. "/" .. #Squad
				.. " everCarried=" .. everCarried
				.. " bestMoved=" .. bestMoved
				.. " |" .. per)
		end

		-- (a) CARRIED — monotonic latch on NOT-IN-WORLD ALONE.
		--
		-- DO NOT re-add `not r.IsDead` here. That is the obvious-looking guard and it silently breaks
		-- the latch: measured 2026-08-15, a run reported peakPax=2 (so carriage demonstrably happened)
		-- and squadInWorld=2/5, yet everCarried stayed 0 for the whole match — the latch never fired
		-- once. The only reading consistent with all three is that a passenger inside a Cargo also
		-- reports IsDead, so `not IsDead and not IsInWorld` is unsatisfiable for exactly the units this
		-- clause exists to catch. test-combined-arms-rendezvous carries the same idiom and is likely
		-- mis-counting for the same reason.
		--
		-- Being permissive here is safe because it cannot manufacture a pass on its own: a genuinely
		-- dead unit never returns to the world, so it can never satisfy the RETURNED and MOVED clauses
		-- below. The latch is evidence of boarding, and delivery still has to be earned separately.
		for i = 1, #Squad do
			local r = Squad[i]
			if r ~= nil and not r.IsInWorld and not carried[i] then
				carried[i] = true
				everCarried = everCarried + 1
			end
		end

		for i = 1, #Control do
			local r = Control[i]
			if r ~= nil and not r.IsInWorld and not ctlCarried[i] then
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
				if r ~= nil and carried[i] and r.IsInWorld then
					local moved = CellDistance(r.Location, start[i])
					if moved > bestMoved then bestMoved = moved end
					if moved >= DeliveredCells then delivered = delivered + 1 end
				end
			end

			if delivered >= 1 then
				frozen = true

				-- ARRIVE-TOGETHER — the user's actual complaint, measured at the one instant it is
				-- meaningful. A rifleman that RODE is set down at the drop cell with the carrier; one that
				-- WALKED is still however many cells short of it, and at ~43 ticks/cell that distance IS the
				-- "the infantry turn up minutes later" being reported. Printed PER MEMBER because the two
				-- populations ARE the finding — an average over them describes neither.
				--
				-- NOT attributable on its own, and it is not asked to be: five riflemen that all walked to
				-- the same anchor would read as clustered too. What makes the number mean something is that
				-- it is printed next to the per-member carried flag, and the latch above already required one
				-- of them to have been out of world. Read the two together, never the spread alone.
				local drop = nil
				for i = 1, #Squad do
					local r = Squad[i]
					if r ~= nil and carried[i] and r.IsInWorld and CellDistance(r.Location, start[i]) >= DeliveredCells then
						drop = r.Location
						break
					end
				end

				local spread = ""
				for i = 1, #Squad do
					local r = Squad[i]
					if r == nil or not r.IsInWorld then
						spread = spread .. " r" .. i .. "=aboard/dead"
					elseif drop ~= nil then
						spread = spread .. " r" .. i .. "=" .. CellDistance(r.Location, drop) .. "cells"
					end
				end

				print("[deliv] DELIVERED tick~" .. ticks
					.. " everCarried=" .. everCarried
					.. " peakPax=" .. peakPax
					.. " deliveredNow=" .. delivered
					.. " drop=" .. (drop and (drop.X .. "," .. drop.Y) or "?")
					.. " from-drop:" .. spread)

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
		.. "READ THE NUMBERS FROM lua.log, NOT FROM THIS LINE. The `[deliv] ...` trace above carries the "
		.. "live counters; this message cannot. Lua concatenates the third argument to AssertWithin "
		.. "EAGERLY, at registration, so any counter interpolated here reports its value BEFORE the "
		.. "predicate has run even once — always the initial zero. That is not a hypothesis: on "
		.. "2026-08-15 this line reported everCarried=0 peakPax=0 while the trace from inside the same "
		.. "closure, in the same run, read everCarried=3 peakPax=2. The zeros were an artefact of when "
		.. "the string was built, and they cost a run and a wrong diagnosis before that was spotted.")
end
