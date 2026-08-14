-- AUTO TEST: Capture rule fixes (260512; intent updated 2026-08-14).
--   TECN     captures neutrals + enemy-owned — the only unit that ever GAINS a building
--   Soldier  can target enemy-owned ONLY (was: also neutrals — fixed)
--   Engineer captures nothing (no Captures trait by design)
--
-- NOTE ON WHAT THIS FILE DOES AND DOESN'T COVER. Since 2026-08-14 a soldier entering
-- an enemy building CLEARS it — the building goes Neutral and the soldier walks out
-- alive — rather than taking ownership. `CanCapture` resolves to CaptureManager.CanTarget,
-- which reads capture TYPES x relationships and never inspects the effect, so every
-- assertion below is unchanged by that rule and case 4 still means what it says:
-- a soldier may target an enemy building. It does NOT mean the soldier gains it.
-- Nothing here asserts the clear EFFECT (owner becomes Neutral, soldier survives);
-- that would need a live capture and an owner check, and is not covered anywhere yet.

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

	-- 3. Soldier (rifleman) must NOT be able to target neutral OILB.
	if Soldier.CanCapture(NeutralOilb) then
		Test.Fail("Soldier.CanCapture(NeutralOilb) was true — expected false (soldiers can't touch neutrals)")
		return
	end

	-- 4. Soldier must be able to target enemy-owned OILB (to clear it, not to own it).
	if not Soldier.CanCapture(EnemyOilb) then
		Test.Fail("Soldier.CanCapture(EnemyOilb) was false — expected true (soldier can clear an enemy building)")
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
