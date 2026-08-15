-- TEST: does the littlebird's 7.62mm.Minigun take ANY health off infantry?
--
-- One littlebird, one e1, 2 cells apart, flat clear terrain, no cover, no
-- other actors. The user's report is that section 2 of demo-heli-weapons
-- "shoots and shoots" and kills nothing at 2/4/6/8 cells. This strips the
-- question to its smallest form: at the CLOSEST of those ranges, does the
-- victim's health drop at all within 20 seconds?
--
-- RED means the gun deals literally zero. That is the point of the test —
-- it is written to fail against the behaviour being diagnosed.

local TicksPerSecond = TestHarness.TicksPerSecond

local function cellPos(cx, cy, altitude)
	return WPos.New(cx * 1024 + 512, cy * 1024 + 512, altitude or 0)
end

WorldLoaded = function()
	local USA = Player.GetPlayer("USA")
	local RUSSIA = Player.GetPlayer("Russia")
	if USA == nil or RUSSIA == nil then
		Test.Fail("required players not found")
		return
	end

	local lb = Actor.Create("littlebird", true, {
		Owner = USA,
		CenterPosition = cellPos(12, 17, 1280),
		Facing = Angle.East,
	})
	local grunt = Actor.Create("e1", true, {
		Owner = RUSSIA,
		Location = CPos.New(14, 17),
		Facing = Angle.West,
	})

	-- CONTROL lane. The Apache declares a Gunner crew slot; the littlebird
	-- declares only a Pilot. Both inherit ^Airborne's
	-- FirepowerMultiplier@NoGunner (Modifier: 0, RequiresCondition:
	-- !has-gunner). If the control's infantry dies and the littlebird's does
	-- not, the crew slot is the whole difference — nothing about the weapon.
	local ap = Actor.Create("heli", true, {
		Owner = USA,
		CenterPosition = cellPos(12, 24, 1280),
		Facing = Angle.East,
	})
	local ctrlGrunt = Actor.Create("e1", true, {
		Owner = RUSSIA,
		Location = CPos.New(14, 24),
		Facing = Angle.West,
	})

	if lb == nil or grunt == nil or ap == nil or ctrlGrunt == nil then
		Test.Fail("could not spawn littlebird/heli/e1")
		return
	end

	TestHarness.FocusBetween(lb, grunt)
	TestHarness.Select(lb)

	local startHp = grunt.Health
	local ctrlStartHp = ctrlGrunt.Health
	print(string.format("[STRAFE] start hp=%d ctrlStartHp=%d", startHp, ctrlStartHp))

	-- Normal (not forced) attack order, re-issued: identical to how
	-- demo-heli-weapons section 2 drives its lanes, so the measurement
	-- matches what the user is watching.
	local function reorder()
		if not lb.IsDead and lb.IsInWorld and not grunt.IsDead and grunt.IsInWorld then
			lb.Attack(grunt, true, false)
		end
		if not ap.IsDead and ap.IsInWorld and not ctrlGrunt.IsDead and ctrlGrunt.IsInWorld then
			ap.Attack(ctrlGrunt, true, false)
		end
		Trigger.AfterDelay(3 * TicksPerSecond, reorder)
	end
	Trigger.AfterDelay(TicksPerSecond, reorder)

	local function report()
		local hp = grunt.IsDead and 0 or grunt.Health
		local chp = ctrlGrunt.IsDead and 0 or ctrlGrunt.Health
		print(string.format("[STRAFE] littlebird=%d/%d  control(apache)=%d/%d", hp, startHp, chp, ctrlStartHp))
		Trigger.AfterDelay(2 * TicksPerSecond, report)
	end
	Trigger.AfterDelay(2 * TicksPerSecond, report)

	TestHarness.AssertWithin(20, function()
		return grunt.IsDead or grunt.Health < startHp
	end, "littlebird minigun took zero health off e1 in 20s at 2 cells")
end
