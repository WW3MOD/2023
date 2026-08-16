-- DEMO: helicopter weapons (260815)
--
-- Four questions, one map. Nothing here asserts anything — you are the
-- instrument. Pause (space), speed up, and press End to restart.
--
-- LAYOUT
--   rows 3, 7    SECTION 1 — do the littlebird's missiles work, and how far?
--   rows 11-20   SECTION 2 — minigun vs infantry at 2 / 4 / 6 / 8 cells
--   rows 24-31   SECTION 3 — air-to-air, three lanes, with a reference
--
-- WHAT TO LOOK AT
--
-- SECTION 1 (top left). Two lanes, both firing Hellfires at a T-90.
--   The littlebird (row 3) is 17 cells from its target; the Apache (row 7) is
--   23. Both should launch and both should hurt — roughly half a T-90 per
--   missile. Before this change the littlebird's rack was effectively dead.
--   Select each helicopter and compare the CYAN missile range circle: the
--   littlebird's is now 5 cells tighter than the Apache's (20c0 vs 25c0).
--   The littlebird only carries 2 missiles and cannot reload away from a
--   helipad, so its lane goes quiet quickly. That is intended.
--
-- SECTION 2 (middle left). Four littlebirds strafing infantry at 2, 4, 6 and
--   8 cells. This is the "are the miniguns accurate enough" question. Watch
--   where the tracers land relative to the squad, and how fast each squad
--   dies. Note the helicopters will drift to their own preferred standoff
--   once engaged — the starting distances are the opening range, not a
--   fixed one. Infantry respawn so you can watch it repeatedly.
--
-- SECTION 3 (bottom, the important one). The anti-air ceiling.
--   Lane 1 (row 24): two littlebirds vs an Mi-28. They can only bring their
--     miniguns — order one to attack and you will see the missiles decline;
--     they are no longer allowed to target helicopters at all. Expect this
--     to take a long time, and expect the littlebirds to RUN OUT OF AMMO
--     before the Mi-28 dies. That is the ceiling working.
--   Lane 2 (row 28): Hind vs Apache, two-way. The Hind can now shoot back at
--     a helicopter, but the Apache should win this comfortably.
--   Lane 3 (row 31): Stryker SHORAD vs Mi-28 — the REFERENCE. This is what a
--     purpose-built anti-air unit does. It should be over almost immediately.
--     Compare lane 1 against lane 3: that gap IS the design.
--
-- If lane 1 or lane 2 feels too strong or too weak, say so — those are the
-- numbers meant to be argued with.

local TicksPerSecond = TestHarness.TicksPerSecond

-- ------------------------------------------------------------------
-- Helpers
-- ------------------------------------------------------------------

-- Re-issue a normal (NOT forced) attack order every few seconds. Normal
-- orders are deliberate: a forced order would bypass the ValidTargets gate
-- and hide the fact that the littlebird's missiles now refuse air targets.
local function keepAttacking(getAttacker, getTarget)
	local function tick()
		local a, t = getAttacker(), getTarget()
		if a and not a.IsDead and a.IsInWorld and t and not t.IsDead and t.IsInWorld then
			a.Attack(t, true, false)
		end
		Trigger.AfterDelay(3 * TicksPerSecond, tick)
	end
	Trigger.AfterDelay(TicksPerSecond, tick)
end

-- Respawn `actor` in place when killed, keeping the handle current.
local function respawnInPlace(slot, actorType, owner, location, facing, delaySec)
	local current = slot.actor
	if not current or current.IsDead then return end
	Trigger.OnKilled(current, function()
		Trigger.AfterDelay(math.floor((delaySec or 4) * TicksPerSecond), function()
			slot.actor = Actor.Create(actorType, true, {
				Owner = owner,
				Location = location,
				Facing = Angle.New(facing or 0),
			})
			respawnInPlace(slot, actorType, owner, location, facing, delaySec)
		end)
	end)
end

local function slotOf(actor) return { actor = actor } end
local function getter(slot) return function() return slot.actor end end

-- ------------------------------------------------------------------

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local Russia = Player.GetPlayer("Russia")

	-- Frame everything; the user pans to each section themselves.
	TestHarness.FocusBetween(S1LB, S2D, S3MI28, S3SHORAD)
	TestHarness.Select(S1LB)

	-- ---------------- SECTION 1: missiles ----------------
	local s1lb, s1lbT = slotOf(S1LB), slotOf(S1LBTarget)
	local s1ap, s1apT = slotOf(S1AP), slotOf(S1APTarget)
	respawnInPlace(s1lb, "littlebird", USA, CPos.New(4, 3), 192, 6)
	respawnInPlace(s1lbT, "t90", Russia, CPos.New(21, 3), 704, 6)
	respawnInPlace(s1ap, "heli", USA, CPos.New(4, 7), 192, 6)
	respawnInPlace(s1apT, "t90", Russia, CPos.New(27, 7), 704, 6)
	keepAttacking(getter(s1lb), getter(s1lbT))
	keepAttacking(getter(s1ap), getter(s1apT))

	-- ---------------- SECTION 2: minigun vs distance ----------------
	local lanes = {
		{ S2A, "littlebird", CPos.New(4, 11), { { S2ATgt1, CPos.New(6, 11) }, { S2ATgt2, CPos.New(6, 12) } } },
		{ S2B, "littlebird", CPos.New(4, 14), { { S2BTgt1, CPos.New(8, 14) }, { S2BTgt2, CPos.New(8, 15) } } },
		{ S2C, "littlebird", CPos.New(4, 17), { { S2CTgt1, CPos.New(10, 17) }, { S2CTgt2, CPos.New(10, 18) } } },
		{ S2D, "littlebird", CPos.New(4, 20), { { S2DTgt1, CPos.New(12, 20) }, { S2DTgt2, CPos.New(12, 21) } } },
	}
	for _, lane in ipairs(lanes) do
		local heliSlot = slotOf(lane[1])
		respawnInPlace(heliSlot, lane[2], USA, lane[3], 192, 6)
		for _, t in ipairs(lane[4]) do
			local tgtSlot = slotOf(t[1])
			respawnInPlace(tgtSlot, "e1", Russia, t[2], 0, 4)
			keepAttacking(getter(heliSlot), getter(tgtSlot))
		end
	end

	-- ---------------- SECTION 3: air-to-air ceiling ----------------
	-- Lane 1: littlebird miniguns vs Mi-28 (expect slow; expect them to go dry)
	local lb1, lb2, mi28 = slotOf(S3LB1), slotOf(S3LB2), slotOf(S3MI28)
	respawnInPlace(lb1, "littlebird", USA, CPos.New(34, 24), 192, 8)
	respawnInPlace(lb2, "littlebird", USA, CPos.New(34, 26), 192, 8)
	respawnInPlace(mi28, "mi28", Russia, CPos.New(46, 25), 704, 8)
	keepAttacking(getter(lb1), getter(mi28))
	keepAttacking(getter(lb2), getter(mi28))

	-- Lane 2: Hind vs Apache, two-way. The Hind has no AutoTarget inheritance
	-- of its own (aircraft-russia.yaml:91 is commented out), so without an
	-- explicit order it would simply sit there — hence keepAttacking here.
	local hind, apache = slotOf(S3HIND), slotOf(S3APACHE)
	respawnInPlace(hind, "hind", Russia, CPos.New(34, 28), 192, 8)
	respawnInPlace(apache, "heli", USA, CPos.New(46, 28), 704, 8)
	keepAttacking(getter(hind), getter(apache))
	keepAttacking(getter(apache), getter(hind))

	-- Lane 3: the reference — a dedicated AA vehicle doing the same job.
	local shorad, mi28b = slotOf(S3SHORAD), slotOf(S3MI28B)
	respawnInPlace(shorad, "strykershorad", USA, CPos.New(34, 31), 192, 8)
	respawnInPlace(mi28b, "mi28", Russia, CPos.New(46, 31), 704, 8)
	keepAttacking(getter(shorad), getter(mi28b))
end
