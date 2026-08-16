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
--
-- Cases 6-8 (added with the bot reclaim work) extend the same targeting assertions off the
-- money structures onto an AIRFIELD, covering both ends of the reclaim cycle:
--   6. TECN CAN capture a NEUTRALISED afld  ← the reclaim itself
--   7. Soldier CANNOT target a neutralised afld
--   8. Soldier CAN target an ENEMY afld     ← the eviction that CREATES a reclaim target
-- Both were assumed everywhere and asserted nowhere. They are still targeting assertions,
-- not effect assertions, and they exercise rules this branch does not modify — which is the
-- point: they are a tripwire under a precondition the bot's reclaim pass depends on, and a
-- `-Capturable@*` override landing on ^Building (as ^Defense already does for pbox/hbox/gtwr)
-- would break the feature silently in play while every pre-existing case stayed green.

WorldLoaded = function()
	TestHarness.FocusBetween(Tecn, Soldier, Engineer, NeutralOilb, EnemyOilb, NeutralAfld, EnemyAfld)

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

	-- 6. TECN must be able to capture a NEUTRALISED NON-INCOME structure. Every assertion
	--    above uses an oil derrick, so "a technician can take a cleared airfield back" was
	--    assumed rather than tested — and it is the precondition the bot's whole reclaim
	--    pass rests on. If ^BasicBuilding ever loses ^NeutralOrOccupiedCapturable, the bot
	--    would go on dispatching technicians at targets none of them can act on, which
	--    fails silently in play and loudly here.
	if not Tecn.CanCapture(NeutralAfld) then
		Test.Fail("Tecn.CanCapture(NeutralAfld) was false — a cleared airfield must be reclaimable by a technician")
		return
	end

	-- 7. Soldier must NOT be able to target it. Soldiers only ever CLEAR enemy buildings;
	--    an already-cleared one is Neutral and there is nothing left for them to do to it.
	if Soldier.CanCapture(NeutralAfld) then
		Test.Fail("Soldier.CanCapture(NeutralAfld) was true — expected false (soldiers can't touch neutrals)")
		return
	end

	-- 8. Soldier MUST be able to target an ENEMY airfield — this is the event that MANUFACTURES
	--    every reclaim target, and without it the bot's reclaim pass has an input that never
	--    arrives. Case 4 asserts the same rule for an oil derrick only; a `-Capturable@occupied`
	--    landing on ^Building (the way ^Defense already does it for pbox/hbox/gtwr) would break
	--    this while leaving case 4 green.
	if not Soldier.CanCapture(EnemyAfld) then
		Test.Fail("Soldier.CanCapture(EnemyAfld) was false — expected true (a soldier clears an enemy airfield to Neutral)")
		return
	end

	Test.Pass()
end
