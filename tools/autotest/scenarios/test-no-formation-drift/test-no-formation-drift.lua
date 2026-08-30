-- REGRESSION GUARD — formation drift is off for human players (2026-08-30).
--
-- Proves a NEGATIVE, so most of this file is about making a vacuous or a FALSE result impossible.
-- A unit that stands still because it never had a slot would pass while proving nothing; a unit
-- reported as "drifting" because a displacement had not landed yet would fail while proving nothing.
-- Earlier revisions of this test hit both, so the guards below are the point of the file.
--
-- SEQUENCE
--   1. Force Cohesion to Loose. Tight is the vanilla opt-out: CohesionMoveModifier CLEARS the slot
--      for a Tight human instead of assigning one (CohesionMoveModifier.cs:1085), which would remove
--      the mechanism under test.
--   2. One grouped Move via Test.GroupMove. It must be the ORDER path: only that runs
--      CohesionMoveModifier and assigns slots. Actor.Move queues an activity and bypasses it
--      entirely (TestGlobal.cs:880-884).
--   3. Wait for a slot to actually exist (Test.GetCohesionSlot). Rules out "nothing was armed".
--   4. Stop(), then displace the unit with Teleport, then WAIT FOR THE TELEPORT TO LAND. Teleport
--      QUEUES a SimpleTeleport activity (GeneralProperties.cs:114) — it is not instant — so sampling
--      Location on the next tick reads the OLD cell and looks exactly like a unit walking away.
--   5. Confirm the landing cell is genuinely off the slot, then watch it.
--
-- WHY Stop() AND NOT AN IDLE POLL: with the leash ON (the RED state) a unit resting off its slot
-- re-queues a Move from TickIdle, and TickIdle only runs on a tick that BEGAN idle (Actor.cs:331),
-- so a Lua sample essentially never observes IsIdle. Stop() clears the queue and does NOT clear the
-- slot record — only the Tight branch does — so the leash stays armed across it.
--
-- THE WINDOW MUST STAY INSIDE THE LEASH. The slot expires ForgetAfterTicks (750) after the order
-- tick; past that a unit holds for an unrelated reason. The watch ends before then and the test
-- FAILS rather than passes if the schedule slips.

-- DEST IS THE SQUAD'S OWN CENTRE, NOT A DISTANT CELL. The order exists to make
-- CohesionMoveModifier assign slots, not to make anyone walk far: ordering the squad across the map
-- meant a unit was still travelling when the test tried to displace it, and Teleport QUEUES a
-- SimpleTeleport (GeneralProperties.cs:114) that therefore sat behind the running Move and had not
-- executed by the deadline. Ordering the squad onto itself assigns real slots while keeping the
-- journey to a cell or two, so the queue is empty by the time we displace anyone.
local DEST = { X = 21, Y = 19 }        -- centre of the three spawn cells
local PARK = { X = 45, Y = 19 }        -- where the player "parks" the displaced unit. On the y=19
                                       -- row, which the supply routes at x=5 and x=60 flank, so it
                                       -- is open ground.
local FORGET_AFTER = 750               -- CohesionSlotMemoryInfo.ForgetAfterTicks
local SLOT_DEADLINE = 120              -- ticks allowed for the order to be processed and a slot set
local SETTLE_TICKS = 60                -- ticks after slot assignment before we touch the unit
local STOP_LEAD = 5                    -- ticks between cancelling activities and displacing
local LAND_WINDOW = 200                -- ticks allowed for the displacement to take effect
local WATCH_UNTIL = 700                -- last tick we observe; < FORGET_AFTER so the leash is live

WorldLoaded = function()
	local squad = { SquadA, SquadB, SquadC }
	local subject = SquadA

	TestHarness.FocusBetween(SquadA, SquadC)
	TestHarness.Select(SquadA)

	for _, u in ipairs(squad) do
		if not u.IsDead then
			Test.SetCohesion(u, "Loose")
			u.Stance = "FireAtWill"
		end
	end

	Test.GroupMove(squad, CPos.New(DEST.X, DEST.Y))

	local phase = "await-slot"
	local elapsed = 0
	local slot = nil
	local slotTick = nil
	local origin = nil
	local parkedAt = nil
	local park = nil
	local shot = false

	local poll
	poll = function()
		elapsed = elapsed + 1

		if subject.IsDead then
			Test.Fail("precondition lost: the subject died at tick " .. elapsed)
			return
		end

		if phase == "await-slot" then
			local s = Test.GetCohesionSlot(subject)
			if s.X ~= 0 or s.Y ~= 0 then
				slot = { X = s.X, Y = s.Y }
				slotTick = elapsed
				phase = "settle"
			elseif elapsed >= SLOT_DEADLINE then
				Test.Fail("precondition lost: no formation slot was assigned within " .. SLOT_DEADLINE ..
					" ticks (Cohesion may have been Tight, which CLEARS the slot instead of assigning " ..
					"one) — a stationary unit would prove nothing")
				return
			end

		elseif phase == "settle" then
			-- Stop() first and displace a few ticks later. Stop clears the short walk to the slot so
			-- the SimpleTeleport is not queued behind it; the gap lets the cancellation take effect.
			-- Stop does NOT clear the slot record (only the Tight branch does), so the leash survives.
			if elapsed == slotTick + SETTLE_TICKS then
				subject.Stop()
			elseif elapsed >= slotTick + SETTLE_TICKS + STOP_LEAD then
				-- Record where the unit stood BEFORE the displacement. "It is now at the park cell" is
				-- only evidence that the displacement executed if it was somewhere else to begin with;
				-- without this, a unit that happened to already be parked would satisfy the landing
				-- check trivially and the run would look like a clean setup that never moved anything.
				origin = { X = subject.Location.X, Y = subject.Location.Y }
				parkedAt = elapsed
				subject.Teleport(CPos.New(PARK.X, PARK.Y))
				phase = "await-landing"
			end

		elseif phase == "await-landing" then
			local loc = subject.Location
			if loc.X == PARK.X and loc.Y == PARK.Y then
				if origin.X == PARK.X and origin.Y == PARK.Y then
					Test.Fail("precondition lost: the subject was already standing on the park cell " ..
						PARK.X .. "," .. PARK.Y .. " before the displacement, so nothing was moved and " ..
						"the landing check proves nothing")
					return
				end

				-- The displacement is only meaningful if it actually took the unit OFF its slot; if it
				-- landed on the slot there would be nothing to walk back to and the watch would be
				-- vacuous (TryReturnToSlot returns early when Location == assignedSlot).
				if math.abs(loc.X - slot.X) + math.abs(loc.Y - slot.Y) < 3 then
					Test.Fail("precondition lost: the park cell " .. loc.X .. "," .. loc.Y ..
						" is within 3 cells of the formation slot " .. slot.X .. "," .. slot.Y ..
						" — the unit is effectively already home and could not drift")
					return
				end

				park = { X = loc.X, Y = loc.Y }
				phase = "watch"
			elseif elapsed >= parkedAt + LAND_WINDOW then
				Test.Fail("precondition lost: the displacement had not landed " .. LAND_WINDOW ..
					" ticks after it was queued; unit is at " .. loc.X .. "," .. loc.Y ..
					" not the park cell " .. PARK.X .. "," .. PARK.Y ..
					" (SimpleTeleport is queued, so something is still running ahead of it)")
				return
			end

		else
			local loc = subject.Location
			if loc.X ~= park.X or loc.Y ~= park.Y then
				Test.Fail("formation drift is ACTIVE: the parked AR left " .. park.X .. "," .. park.Y ..
					" for " .. loc.X .. "," .. loc.Y .. " at tick " .. elapsed ..
					" heading for its formation slot " .. slot.X .. "," .. slot.Y ..
					" — a human-owned unit must not walk back to its formation slot")
				return
			end

			-- If the slot expired mid-watch, everything after this point is a unit holding still for
			-- an unrelated reason, so the observation would no longer prove anything.
			if elapsed >= FORGET_AFTER then
				Test.Fail("test window slipped past ForgetAfterTicks (" .. FORGET_AFTER ..
					"); the observation ran outside the leash and proves nothing")
				return
			end

			if not shot and elapsed >= WATCH_UNTIL - 60 then
				Test.Screenshot("parked-unit-stayed",
					"expects: one AR alone at the park cell to the east, NOT rejoining the two-unit " ..
					"formation to the west")
				shot = true
			end

			if elapsed >= WATCH_UNTIL then
				Test.Pass()
				return
			end
		end

		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
