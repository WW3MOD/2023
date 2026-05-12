-- AUTO TEST: Capture rule fixes (260512).
--   TECN     captures neutrals + enemy-owned
--   Soldier  captures enemy-owned ONLY (was: also neutrals — fixed)
--   Engineer captures nothing (no Captures trait by design)

WorldLoaded = function()
	TestHarness.FocusBetween(Tecn, Soldier, Engineer, NeutralOilb, EnemyOilb)

	-- 1. TECN must be able to capture neutral OILB.
	if not Tecn.CanCapture(NeutralOilb) then
		Test.Fail("Tecn.CanCapture(NeutralOilb) was false — expected true")
		return
	end

	-- 2. TECN must be able to capture enemy-owned OILB.
	if not Tecn.CanCapture(EnemyOilb) then
		Test.Fail("Tecn.CanCapture(EnemyOilb) was false — expected true")
		return
	end

	-- 3. Soldier (rifleman) must NOT be able to capture neutral OILB.
	if Soldier.CanCapture(NeutralOilb) then
		Test.Fail("Soldier.CanCapture(NeutralOilb) was true — expected false (soldiers can't take neutrals)")
		return
	end

	-- 4. Soldier must be able to capture enemy-owned OILB.
	if not Soldier.CanCapture(EnemyOilb) then
		Test.Fail("Soldier.CanCapture(EnemyOilb) was false — expected true (soldier takes by force from enemy)")
		return
	end

	-- 5. Engineer must NOT be able to capture anything (no Captures trait).
	if Engineer.CanCapture(NeutralOilb) then
		Test.Fail("Engineer.CanCapture(NeutralOilb) was true — engineers don't capture in WW3MOD")
		return
	end
	if Engineer.CanCapture(EnemyOilb) then
		Test.Fail("Engineer.CanCapture(EnemyOilb) was true — engineers don't capture in WW3MOD")
		return
	end

	Test.Pass()
end
