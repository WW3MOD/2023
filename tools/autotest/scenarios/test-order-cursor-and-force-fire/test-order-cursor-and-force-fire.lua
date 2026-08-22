-- AUTO TEST: a click a unit cannot carry out must still leave the player a cursor, and force-fire
-- must still reach the ground under an enemy the firing unit cannot target.
--
-- The rule this pins, in the user's words: "We should still see the default order, it is only when
-- we give a specific order that only some can carry out that the ones that cannot carry it out will
-- get no order." So the suppression is PER-SELECTION. While one unit is attacking, the others that
-- cannot are left alone; when NOTHING selected can attack there is no specific order to hold anyone
-- to, and the click -- and the cursor that previews it -- is the default order for everybody.
--
-- WHY NOT Test.ClickOrder. That is the per-UNIT layer: it resolves one actor in isolation and so
-- cannot see the selection rule at all, which is the half that was broken. ClickOrderGroup goes
-- through UnitOrderGenerator.OrdersForSelection, the same method the mouse uses.
--
-- SIX OBSERVABLES, taken in one tick so no unit has moved or died between them:
--   1. mixed selection      -- Abrams "Attack", AA specialist nothing. The previous fix's win; it
--                              must survive untouched.
--   2. mixed cursor         -- must equal the cursor the Abrams gives ALONE, i.e. the attack cursor.
--   3. nothing can attack   -- AA specialist alone gets "Move", and a non-empty cursor. Under the
--                              per-unit rule this was no order and a bare pointer.
--   4. the reported unit    -- Iskander alone, plain click on a tank: "Move" and a cursor. Its
--                              armament is force-fire-only, so it refuses every plain actor click by
--                              design, and that refusal is what erased the cursor.
--   5. force-fire ground    -- Iskander, Ctrl+Alt on a HELICOPTER it can never target: "ForceAttack"
--                              at the cell underneath. This is the one the terrain retry was
--                              skipping, and no amount of cursor work fixes it.
--   6. the normal case      -- Abrams alone on the tank: still "Attack". Proof the fix did not buy
--                              any of the above by loosening ordinary targeting.
--
-- Cursors are read BEFORE any order is issued: ClickCursor issues nothing, but the orders
-- ClickOrderGroup issues would change what a later hover resolves to.

local DeadlineSeconds = 20

local r = { done = false }

local function shown(v)
	if v == nil then
		return "<nil>"
	end

	if v == "" then
		return "<none>"
	end

	return v
end

WorldLoaded = function()
	TestHarness.FocusBetween(Launcher, Enemy)

	-- Nothing acquires on its own, so every order recorded below is attributable to its click.
	-- HoldFire does not gag an explicit order: AttackBase.ResolveOrder does not consult stance.
	Enemy.Stance = "HoldFire"
	EnemyAir.Stance = "HoldFire"
	Gunner.Stance = "HoldFire"
	Rejector.Stance = "HoldFire"
	Launcher.Stance = "HoldFire"

	Trigger.AfterDelay(50, function()
		r.mixedCursor = Test.ClickCursor({ Gunner, Rejector }, Enemy)
		r.gunnerCursor = Test.ClickCursor({ Gunner }, Enemy)
		r.rejectorCursor = Test.ClickCursor({ Rejector }, Enemy)
		r.launcherCursor = Test.ClickCursor({ Launcher }, Enemy)
		r.forceFireCursor = Test.ClickCursor({ Launcher }, EnemyAir, "CtrlAlt")

		local mixed = Test.ClickOrderGroup({ Gunner, Rejector }, Enemy)
		r.gunnerInMixed = mixed[1]
		r.rejectorInMixed = mixed[2]

		r.rejectorAlone = Test.ClickOrderGroup({ Rejector }, Enemy)[1]
		r.launcherAlone = Test.ClickOrderGroup({ Launcher }, Enemy)[1]
		r.forceFire = Test.ClickOrderGroup({ Launcher }, EnemyAir, "CtrlAlt")[1]
		r.gunnerAlone = Test.ClickOrderGroup({ Gunner }, Enemy)[1]

		r.done = true
	end)

	-- Live values go to lua.log, never into a failure string: AssertWithin's third argument is
	-- evaluated once at registration, so anything interpolated there reports its pre-run value
	-- (AUTOTEST.md §Two Lua traps).
	local ticks = 0
	Trigger.AfterDelay(1, function()
		local report
		report = function()
			ticks = ticks + 1
			if ticks % 25 == 0 then
				print("[order-cursor] t=" .. ticks
					.. " gunnerInMixed=" .. shown(r.gunnerInMixed)
					.. " rejectorInMixed=" .. shown(r.rejectorInMixed)
					.. " rejectorAlone=" .. shown(r.rejectorAlone)
					.. " launcherAlone=" .. shown(r.launcherAlone)
					.. " forceFire=" .. shown(r.forceFire)
					.. " gunnerAlone=" .. shown(r.gunnerAlone)
					.. " | cursors mixed=" .. shown(r.mixedCursor)
					.. " gunner=" .. shown(r.gunnerCursor)
					.. " rejector=" .. shown(r.rejectorCursor)
					.. " launcher=" .. shown(r.launcherCursor)
					.. " forceFire=" .. shown(r.forceFireCursor))
			end

			Trigger.AfterDelay(1, report)
		end

		report()
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Gunner.IsDead or Rejector.IsDead or Launcher.IsDead or Enemy.IsDead or EnemyAir.IsDead then
			return "fail: a unit died before the clicks were resolved -- something is shooting, and the stances above should have stopped it"
		end

		if not r.done then
			return false
		end

		-- 1. the previous fix's win, unchanged.
		if r.gunnerInMixed ~= "Attack" then
			return "fail: mixed selection -- the Abrams got '" .. shown(r.gunnerInMixed)
				.. "' instead of Attack; the order that CAN be carried out was lost"
		end

		if r.rejectorInMixed ~= "" then
			return "fail: mixed selection -- the AA specialist got '" .. shown(r.rejectorInMixed)
				.. "'; while another selected unit is attacking, one that cannot must get NO order rather than a walk into the target"
		end

		-- 2. and the cursor names that attack, not something else.
		if r.mixedCursor == "" or r.mixedCursor ~= r.gunnerCursor then
			return "fail: mixed selection showed cursor '" .. shown(r.mixedCursor)
				.. "' but the Abrams alone shows '" .. shown(r.gunnerCursor)
				.. "' -- the cursor must name the order the click will actually produce"
		end

		-- 3. nothing in the selection can attack: the default order, and a cursor for it.
		if r.rejectorAlone ~= "Move" then
			return "fail: the AA specialist ALONE got '" .. shown(r.rejectorAlone)
				.. "' instead of Move; with nothing selected that can attack there is no specific order to withhold"
		end

		if r.rejectorCursor == "" then
			return "fail: hovering the tank with only the AA specialist selected gave NO cursor at all -- the default order must still be previewed"
		end

		if r.rejectorCursor == r.gunnerCursor then
			return "fail: the AA specialist previewed the ATTACK cursor '" .. shown(r.rejectorCursor)
				.. "' over a tank it can never shoot; a cursor that promises an attack it will not deliver is worse than the bare pointer it replaced"
		end

		-- 4. the reported unit, on the click the user actually made.
		if r.launcherAlone ~= "Move" then
			return "fail: the Iskander alone got '" .. shown(r.launcherAlone)
				.. "' instead of Move on a plain click; its armament is force-fire-only, so a plain click is a move request"
		end

		if r.launcherCursor == "" then
			return "fail: hovering an enemy tank with the Iskander selected gave NO cursor at all -- this is the reported bug"
		end

		if r.launcherCursor == r.gunnerCursor then
			return "fail: the Iskander previewed the ATTACK cursor '" .. shown(r.launcherCursor)
				.. "' over a tank a plain click will only MOVE it toward; the cursor has to name the order the click produces"
		end

		-- 5. the half no cursor work can reach: force-fire at the ground under an untargetable enemy.
		if r.forceFire ~= "ForceAttack" then
			return "fail: force-attack-ground under a helicopter the Iskander cannot target got '" .. shown(r.forceFire)
				.. "' instead of ForceAttack; the terrain retry is the only route to that cell and it is being skipped"
		end

		if r.forceFireCursor == "" then
			return "fail: force-attack-ground under an untargetable enemy showed no cursor, so the player cannot tell the shot is available"
		end

		-- 6. the ordinary case, untouched.
		if r.gunnerAlone ~= "Attack" then
			return "fail: the Abrams ALONE got '" .. shown(r.gunnerAlone)
				.. "' instead of Attack -- ordinary targeting regressed"
		end

		return true
	end, "the clicks never resolved -- see the [order-cursor] lines in lua.log for which observable is still unset")
end
