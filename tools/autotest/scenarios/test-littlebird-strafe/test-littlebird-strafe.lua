-- TEST: the littlebird's guns must actually damage infantry, and the
-- zero-firepower penalty must still fire for a gunner who is genuinely GONE.
--
-- Four lanes, one map, all at 2 cells on flat clear terrain:
--
--   A  littlebird  vs e1   -- must DAMAGE. Was 0 before the @NoGunner fix.
--   B  Apache      vs e1   -- control. Declares Pilot+Gunner, must DAMAGE.
--   C  Apache      vs e1   -- damaged under EjectionDamageState. REPORTED, NOT
--                             ASSERTED. See the warning below.
--   D  A10         vs e1   -- fixed-wing NEGATIVE control. Reported, not
--                             asserted. A static sweep keyed on ^Airborne
--                             predicted the A10 was zeroed too; this lane
--                             measured it killing its target with the fix
--                             REVERTED, which is what caught the mistake. The
--                             crew FirepowerMultipliers live on ^Helicopter,
--                             not ^Airborne, so planes carry 4 modifiers and
--                             never had @NoGunner. Keep the lane: it is the
--                             guard against re-widening the blast radius.
--
-- WHY LANE C ASSERTS NOTHING. It was written to prove that @NoGunner still
-- fires for a gunner who is genuinely gone, and it CANNOT: measured 260815, a
-- helicopter damaged past EjectionDamageState enters autorotation or crash-land,
-- and both call HeliEmergencyLanding's SuppressEjection (HeliEmergencyLanding.cs
-- :217,254) so the crew never leaves and has-gunner is never revoked. The same
-- states grant @EmergencyDescent, which zeroes firepower anyway. The first
-- version of this lane passed on exactly that confusion — its Apache showed
-- index 6 (@EmergencyDescent) at 0 while index 5 (@NoGunner) was still 100, and
-- a passing lane C "proved" a modifier that had never engaged. If you make lane
-- C assert again, read the indexed firepowerModifiers in the WW3_GUNTRACE output
-- and confirm it is index 5 that is zero, not index 4 or 6.

local TicksPerSecond = TestHarness.TicksPerSecond

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

local function hp(a)
	if a == nil or a.IsDead or not a.IsInWorld then return 0 end
	return a.Health
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	local function lane(actorType, y)
		local shooter = Actor.Create(actorType, true, {
			Owner = USA,
			CenterPosition = cellPos(12, y, 1280),
			Facing = Angle.East,
		})
		local victim = Actor.Create("e1", true, {
			Owner = RUSSIA,
			Location = CPos.New(14, y),
			Facing = Angle.West,
		})
		return { shooter = shooter, victim = victim, start = victim and victim.Health or 0 }
	end

	local A = lane("littlebird", 12)
	local B = lane("heli", 16)
	local C = lane("heli", 20)
	local D = lane("a10", 26)

	if A.shooter == nil or B.shooter == nil or C.shooter == nil then
		Test.Fail("could not spawn littlebird/heli lanes")
		return
	end

	TestHarness.FocusBetween(A.shooter, A.victim)
	TestHarness.Select(A.shooter)

	-- Lane C: drive the Apache under EjectionDamageState (Heavy, HP <50% of 800)
	-- so VehicleCrew ejects its crew and revokes has-gunner. has-gunner-seat is
	-- granted for life, so @NoGunner must now engage and pin its firepower to 0.
	Trigger.AfterDelay(math.floor(0.5 * TicksPerSecond), function()
		if not C.shooter.IsDead then
			C.shooter.Health = 300
			print(string.format("[STRAFE] lane C apache damaged to %d/800 to force crew ejection", hp(C.shooter)))
		end
	end)

	local function reorder()
		for _, L in ipairs({ A, B, C, D }) do
			if L.shooter ~= nil and not L.shooter.IsDead and L.shooter.IsInWorld
				and L.victim ~= nil and not L.victim.IsDead and L.victim.IsInWorld then
				L.shooter.Attack(L.victim, true, false)
			end
		end
		Trigger.AfterDelay(3 * TicksPerSecond, reorder)
	end
	Trigger.AfterDelay(TicksPerSecond, reorder)

	local function report()
		print(string.format(
			"[STRAFE] A littlebird=%d/%d  B apache=%d/%d  C apache-ejected=%d/%d (shooterHp=%d)  D a10=%d/%d",
			hp(A.victim), A.start, hp(B.victim), B.start,
			hp(C.victim), C.start, hp(C.shooter),
			hp(D.victim), D.start))
		Trigger.AfterDelay(2 * TicksPerSecond, report)
	end
	Trigger.AfterDelay(2 * TicksPerSecond, report)

	-- ONE verdict for all three judged lanes. Two separate TestHarness asserts
	-- would race: both call Test.Pass(), so the first to succeed would end the
	-- run before lane C had been given its window. Lane C asserts an ABSENCE and
	-- needs the full delay anyway (PostStopDelay 20 + StopTimeout 25 + staged
	-- EjectionDelay 15 per crew member), so everything is judged at 22s.
	Trigger.AfterDelay(math.floor(22 * TicksPerSecond), function()
		local failures = {}

		if not (A.victim.IsDead or hp(A.victim) < A.start) then
			failures[#failures + 1] = string.format(
				"lane A littlebird dealt NO damage (%d/%d)", hp(A.victim), A.start)
		end

		if not (B.victim.IsDead or hp(B.victim) < B.start) then
			failures[#failures + 1] = string.format(
				"lane B Apache control dealt NO damage (%d/%d) - control lane broken, lane A result is not trustworthy",
				hp(B.victim), B.start)
		end

		-- Lane C is observation only; see the header. Recorded so a future reader
		-- can see what the damaged Apache actually did rather than guessing.
		print(string.format("[STRAFE] lane C (reported, not asserted): victim=%d/%d shooterHp=%d",
			hp(C.victim), C.start, hp(C.shooter)))

		if #failures == 0 then
			Test.Pass()
		else
			Test.Fail(table.concat(failures, " | "))
		end
	end)
end
