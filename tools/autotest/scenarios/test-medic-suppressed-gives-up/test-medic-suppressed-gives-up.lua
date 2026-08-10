-- AUTO TEST: a medic who is ordered to heal someone he cannot currently treat
-- must give up and stay available, not aim at him forever.
--
-- The order is legitimate: the soldier is genuinely wounded, so he carries the
-- `damaged` condition and therefore the `Heal` target type, and the order is
-- accepted. What fails is the firing: PauseOnCondition: suppressed >= 10 pauses
-- the heal Armament, ChooseArmamentsForTarget does not filter paused armaments,
-- and Armament.CanFire then declines silently every tick while the Attack
-- activity keeps reporting Attacking. To the player that is indistinguishable
-- from the order having been refused — the medic simply stands there.
--
-- Suppression decays by 1 every 5 ticks (5/second), so 100 keeps the armament
-- paused (>= 10) for roughly 18 seconds. The assert lands at 8.

local AssertAtSeconds = 8
local Suppression = 100
local WoundedFraction = 40

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Wounded)
	TestHarness.Select(Medic)

	Wounded.Health = math.floor(Wounded.MaxHealth * WoundedFraction / 100)

	for _ = 1, Suppression do
		Medic.GrantCondition("suppressed")
	end

	-- The player's right-click: a direct heal order on a visibly wounded man.
	Medic.Attack(Wounded, true, false)

	TestHarness.AssertAfter(AssertAtSeconds, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		return Medic.IsIdle
	end, "medic was still locked in an attack activity he cannot fire — he never gave up")
end
