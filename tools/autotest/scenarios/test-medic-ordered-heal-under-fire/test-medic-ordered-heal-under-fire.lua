-- AUTO TEST: a medic the player explicitly orders onto a bleeding man must
-- actually treat him — while he is under fire as well as while he is not.
--
-- The user's report: "a medic right next to a soldier that was bleeding out, I
-- gave an explicit order to heal that soldier, and the medic just laid down next
-- to him and did nothing."
--
-- Two things about that report are easy to get wrong and both are staged here.
--
-- 1. The ORDER ROUTING. A right-click on a wounded ally does not produce an
--    Attack order: AttendAlly's targeter outranks AttackBase's (priority 7 vs 6),
--    so the click is routed to AttendAlly and the medic ends up in an
--    AttackMoveActivity wrapped around a Follow. Every earlier medic test named
--    the order it expected (Medic.Attack / Test.IssueAttendAlly) and so never
--    exercised the contest. Test.ClickOrder resolves the real chain, and the
--    order it picked is reported in the verdict either way.
--
-- 2. WHICH SIDE OF THE SUPPRESSION CONDITION FAILS. "The medic did not heal" is
--    the same sentence whether the heal path is broken outright or only broken
--    while suppressed, so a single pair cannot tell them apart. The control pair
--    is identical except that nobody is shooting at it.
--
-- "Laid down" is not by itself evidence of suppression, incidentally:
-- ProneCondition includes `!moving`, so any infantryman who has stopped is prone.

local DeadlineSeconds = 24
local DiagnoseAtSeconds = 18
local WoundedFraction = 40

-- Suppression decays 1 per 5 ticks, so 25 ticks sheds exactly 5. Replacing 4 of
-- those holds the pinned medic between ~76 and the 100 cap for the whole run: a
-- man under sustained fire, not one who was shot at once and can be waited out.
-- PITFALL: do NOT top up by the full 5. ExternalCondition refuses a grant once
-- permanent tokens reach TotalCap and the Lua binding raises a FATAL error rather
-- than ignoring it, so an over-grant aborts the test instead of saturating.
local TopUpEveryTicks = 25
local TopUpAmount = 4

local pinnedOrder, calmOrder
local pinnedBaseline, calmBaseline

local function suppress(actor, amount)
	if actor.IsDead then
		return
	end

	for _ = 1, amount do
		actor.GrantCondition("suppressed")
	end
end

WorldLoaded = function()
	TestHarness.FocusBetween(MedicPinned, WoundedPinned)
	TestHarness.Select(MedicPinned)

	WoundedPinned.Health = math.floor(WoundedPinned.MaxHealth * WoundedFraction / 100)
	WoundedCalm.Health = math.floor(WoundedCalm.MaxHealth * WoundedFraction / 100)

	pinnedBaseline = WoundedPinned.Health
	calmBaseline = WoundedCalm.Health

	suppress(MedicPinned, 100)

	local topUp
	topUp = function()
		suppress(MedicPinned, TopUpAmount)
		Trigger.AfterDelay(TopUpEveryTicks, topUp)
	end

	Trigger.AfterDelay(TopUpEveryTicks, topUp)

	-- The player's right-click, routed exactly as the UI routes it.
	pinnedOrder = Test.ClickOrder(MedicPinned, WoundedPinned) or "<refused>"
	calmOrder = Test.ClickOrder(MedicCalm, WoundedCalm) or "<refused>"

	local elapsed = 0
	local diagnoseAt = DiagnoseAtSeconds * TestHarness.TicksPerSecond

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if MedicPinned.IsDead or MedicCalm.IsDead then
			return "fail: a medic died"
		end

		if WoundedPinned.IsDead or WoundedCalm.IsDead then
			return "fail: a patient died"
		end

		local pinnedHealed = WoundedPinned.Health > pinnedBaseline
		local calmHealed = WoundedCalm.Health > calmBaseline

		if pinnedHealed and calmHealed then
			return true
		end

		elapsed = elapsed + 1
		if elapsed < diagnoseAt then
			return false
		end

		return "fail: under fire the ordered medic healed "
			.. (pinnedHealed and "OK" or "NOTHING")
			.. " (" .. pinnedBaseline .. " -> " .. WoundedPinned.Health
			.. ", click issued '" .. pinnedOrder .. "', medic idle=" .. tostring(MedicPinned.IsIdle) .. "); "
			.. "control medic healed "
			.. (calmHealed and "OK" or "NOTHING")
			.. " (" .. calmBaseline .. " -> " .. WoundedCalm.Health
			.. ", click issued '" .. calmOrder .. "', medic idle=" .. tostring(MedicCalm.IsIdle) .. ")"
	end, "ordered medics did not both heal their patients within " .. DeadlineSeconds .. "s")
end
