-- AUTO TEST: a medic ordered to heal someone he cannot currently treat must give
-- up and stay available, not aim at him forever.
--
-- The order is legitimate: the soldier is genuinely wounded, so he carries the
-- `damaged` condition and therefore the `Heal` target type, and the order is
-- accepted. What fails is the firing. ChooseArmamentsForTarget filters DISABLED
-- armaments but not PAUSED ones, and a paused AttackBase makes DoAttack skip its
-- armaments wholesale, so the medic aims and Armament.CanFire then declines in
-- silence every tick while the Attack activity keeps reporting Attacking. It
-- never completes: he is stuck non-idle, which silences every idle-driven
-- behaviour he owns. AttackBaseInfo.AbandonWhenArmamentsPaused ends the activity
-- instead; ^MEDI opts in.
--
-- The pause driven here is `garrisoned-at-port`, which pauses ^MEDI's inherited
-- AttackFrontal. This test used to drive the same wedge through the heal
-- Armament's `PauseOnCondition: suppressed >= 10` and asserted that a SUPPRESSED
-- medic gives up. That gate has been removed: giving up and lying beside a
-- bleeding man come to the same thing for the man, who dies either way. A
-- suppressed medic now treats his patient (test-medic-ordered-heal-under-fire),
-- so suppression can no longer produce the wedge and can no longer test the guard.

local AssertAtSeconds = 8
local WoundedFraction = 40

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Wounded)
	TestHarness.Select(Medic)

	Wounded.Health = math.floor(Wounded.MaxHealth * WoundedFraction / 100)

	-- Pauses the medic's AttackFrontal. It pauses his Mobile too, which is
	-- immaterial: he starts adjacent, so this measures firing, not walking.
	Medic.GrantCondition("garrisoned-at-port")

	-- The player's right-click: a direct heal order on a visibly wounded man.
	Medic.Attack(Wounded, true, false)

	TestHarness.AssertAfter(AssertAtSeconds, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		return Medic.IsIdle
	end, "medic was still locked in an attack activity he cannot fire — he never gave up")
end
