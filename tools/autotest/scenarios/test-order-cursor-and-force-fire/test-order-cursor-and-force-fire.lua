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
-- 2026-08-22: the user ruled that these launchers take a plain right-click like anything else
-- ("Right-click attacks, bots included. They still start on hold-fire, so they dont waste their
-- missiles unless we deliberately change their attack stance"), so `RequiresForceFire` came off the
-- iskander and HIMARS armaments. Observable 4 below is therefore INVERTED from what it pinned on the
-- 22nd: the Iskander now answers a plain click with "Attack" and the attack cursor. The half of it
-- that still holds -- and is still worth pinning -- is that the cursor must AGREE with the order.
-- Observable 7 is new and covers the case that ruling has just made reachable for the first time.
--
-- SEVEN OBSERVABLES, taken in one tick so no unit has moved or died between them:
--   1. mixed selection      -- Abrams "Attack", AA specialist nothing. The previous fix's win; it
--                              must survive untouched.
--   2. mixed cursor         -- must equal the cursor the Abrams gives ALONE, i.e. the attack cursor.
--   3. nothing can attack   -- AA specialist alone gets "Move", and a non-empty cursor. Under the
--                              per-unit rule this was no order and a bare pointer.
--   4. the launcher         -- Iskander alone, plain click on a tank 20 cells off: "Attack", and the
--                              SAME cursor the Abrams shows. This is the ruling's whole point.
--   5. force-fire ground    -- Iskander, Ctrl+Alt on a HELICOPTER it can never target: "ForceAttack"
--                              at the cell underneath. This is the one the terrain retry was
--                              skipping, and no amount of cursor work fixes it.
--   6. the normal case      -- Abrams alone on the tank: still "Attack". Proof the fix did not buy
--                              any of the above by loosening ordinary targeting.
--   7. inside MinRange      -- Iskander on a tank 5 cells off, well inside IskanderTargeter's
--                              MinRange 16c0: still "Attack", still the attack cursor. The targeter
--                              tests MaxRange only (AttackBase.cs:785); the minimum is enforced by
--                              the attack ACTIVITY, which pathfinds back out to the annulus
--                              (MoveWithinRange.cs:62) before firing. A refusal here -- no order, or
--                              a bare pointer -- would mean the player gets no feedback at all on a
--                              click the engine is in fact willing to carry out.
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
	-- Sits 5 cells from the Launcher, so it is the one enemy close enough to open up on its own and
	-- kill something before the clicks resolve. Gagged like the rest.
	CloseEnemy.Stance = "HoldFire"

	Trigger.AfterDelay(50, function()
		r.mixedCursor = Test.ClickCursor({ Gunner, Rejector }, Enemy)
		r.gunnerCursor = Test.ClickCursor({ Gunner }, Enemy)
		r.rejectorCursor = Test.ClickCursor({ Rejector }, Enemy)
		r.launcherCursor = Test.ClickCursor({ Launcher }, Enemy)
		r.forceFireCursor = Test.ClickCursor({ Launcher }, EnemyAir, "CtrlAlt")
		r.launcherCloseCursor = Test.ClickCursor({ Launcher }, CloseEnemy)

		local mixed = Test.ClickOrderGroup({ Gunner, Rejector }, Enemy)
		r.gunnerInMixed = mixed[1]
		r.rejectorInMixed = mixed[2]

		r.rejectorAlone = Test.ClickOrderGroup({ Rejector }, Enemy)[1]
		r.launcherAlone = Test.ClickOrderGroup({ Launcher }, Enemy)[1]
		r.forceFire = Test.ClickOrderGroup({ Launcher }, EnemyAir, "CtrlAlt")[1]
		r.gunnerAlone = Test.ClickOrderGroup({ Gunner }, Enemy)[1]
		-- Issued last: this is the one order that sends a unit driving (backwards, out to min range),
		-- so leaving it until the others are recorded keeps every reading above taken from a still map.
		r.launcherClose = Test.ClickOrderGroup({ Launcher }, CloseEnemy)[1]

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
					.. " launcherClose=" .. shown(r.launcherClose)
					.. " | cursors mixed=" .. shown(r.mixedCursor)
					.. " gunner=" .. shown(r.gunnerCursor)
					.. " rejector=" .. shown(r.rejectorCursor)
					.. " launcher=" .. shown(r.launcherCursor)
					.. " forceFire=" .. shown(r.forceFireCursor)
					.. " launcherClose=" .. shown(r.launcherCloseCursor))
			end

			Trigger.AfterDelay(1, report)
		end

		report()
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Gunner.IsDead or Rejector.IsDead or Launcher.IsDead or Enemy.IsDead or EnemyAir.IsDead or CloseEnemy.IsDead then
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

		-- 4. the launcher, on the click the user actually made.
		if r.launcherAlone ~= "Attack" then
			return "fail: the Iskander alone got '" .. shown(r.launcherAlone)
				.. "' instead of Attack on a plain right-click; RequiresForceFire is off this armament, so a plain click must attack like any other unit"
		end

		if r.launcherCursor == "" then
			return "fail: hovering an enemy tank with the Iskander selected gave NO cursor at all -- this is the reported bug"
		end

		if r.launcherCursor ~= r.gunnerCursor then
			return "fail: the Iskander previewed '" .. shown(r.launcherCursor)
				.. "' over a tank it will now ATTACK, but the Abrams shows '" .. shown(r.gunnerCursor)
				.. "'; the same click produces the same order, so it has to preview the same cursor"
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

		-- 7. inside the minimum range, reachable by a plain click for the first time.
		if r.launcherClose ~= "Attack" then
			return "fail: the Iskander clicked on a tank 5 cells away -- INSIDE its MinRange 16c0 -- got '"
				.. shown(r.launcherClose)
				.. "' instead of Attack; the targeter tests MaxRange only, and the attack activity backs out to the annulus, so the order must be accepted rather than refused at click time"
		end

		if r.launcherCloseCursor ~= r.gunnerCursor then
			return "fail: hovering a tank INSIDE the Iskander's minimum range previewed '"
				.. shown(r.launcherCloseCursor) .. "' but the order the click produces is Attack (cursor '"
				.. shown(r.gunnerCursor)
				.. "'); a click the engine will carry out must not be previewed as something else"
		end

		return true
	end, "the clicks never resolved -- see the [order-cursor] lines in lua.log for which observable is still unset")
end
