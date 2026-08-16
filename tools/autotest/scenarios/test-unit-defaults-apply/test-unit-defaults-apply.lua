-- test-unit-defaults-apply.lua
--
-- Proves that a per-TYPE stance preference reaches a newly-arrived unit through the ORDER path
-- (UnitDefaultsManager subscribing to World.ActorAdded), now that AutoTarget.Created no longer
-- reads the per-machine preference file.
--
-- WHAT WOULD MAKE THIS FAIL (the question the recipe says to ask of every green):
--   * Drop the ActorAdded subscription  -> SUBJECT never leaves its map default -> timeout FAIL.
--   * Apply the preference indiscriminately -> CONTROL also flips -> immediate FAIL.
--   * Setup silently not applied -> Test.SetUnitTypeFireStance returns false -> immediate FAIL.
--   * Baseline already equals the target stance -> nothing to detect -> immediate FAIL.
-- The CONTROL is what makes a single run sufficient: subject and control are read by the same
-- code, so a broken observable cannot produce "one changed, one did not".

SubjectType = "e3.russia"   -- gets a per-type default
ControlType = "e1.russia"   -- gets NO per-type default
TargetStance = "HoldFire"

Polls = 0
FlipPoll = -1
SubjectBaseline = nil
ControlBaseline = nil

WorldLoaded = function()
	local human = Player.GetPlayer("Human")
	if human == nil then
		Test.Fail("scenario error: no Human player — the hook is gated on LocalPlayer ownership")
		return
	end

	-- Setup must be asserted, not assumed: an empty default store would let the subject sit at
	-- its map stance forever and read as a mechanism failure.
	local applied = Test.SetUnitTypeFireStance(SubjectType, TargetStance)
	if not applied then
		Test.Fail("scenario error: SetUnitTypeFireStance did not take effect")
		return
	end

	-- Created AFTER WorldLoaded so ActorAdded fires with the subscription in place.
	Subject = Actor.Create(SubjectType, true, { Owner = human, Location = CPos.New(12, 17) })
	Control = Actor.Create(ControlType, true, { Owner = human, Location = CPos.New(14, 17) })

	TestHarness.FocusBetween(Subject, Control)

	-- NOTE: AssertWithin's failure string is evaluated EAGERLY at registration, so it must stay
	-- static. Live numbers go to lua.log via print().
	TestHarness.AssertWithin(10, function()
		Polls = Polls + 1

		if Subject.IsDead or Control.IsDead then
			return "fail: a unit died before the verdict"
		end

		local s = Subject.Stance
		local c = Control.Stance

		-- Non-vacuity: an unreadable stance must not be mistaken for an unchanged one.
		if s == nil or c == nil then
			return "fail: stance not readable — observable is broken, not the mechanism"
		end

		if SubjectBaseline == nil then
			SubjectBaseline = s
			ControlBaseline = c
			print(string.format("[defaults] baseline subject=%s control=%s target=%s", s, c, TargetStance))

			-- If the subject already starts at the target there is nothing to detect and a pass
			-- would mean nothing.
			if s == TargetStance then
				return "fail: subject baseline already equals the target stance"
			end
		end

		-- CONTROL: no per-type default was set for this type, so nothing may move it. If it
		-- flips, the preference is being applied indiscriminately rather than per type.
		if c ~= ControlBaseline then
			print(string.format("[defaults] CONTROL MOVED %s -> %s at poll %d", ControlBaseline, c, Polls))
			return "fail: control unit changed stance — preference applied indiscriminately"
		end

		if s == TargetStance then
			if FlipPoll < 0 then
				FlipPoll = Polls
				print(string.format("[defaults] subject %s -> %s after %d poll(s)", SubjectBaseline, s, FlipPoll))
				print(string.format("[defaults] control held at %s", c))
			end

			return true
		end

		return false
	end, "subject never received its per-type stance default (see lua.log [defaults] lines)")
end
