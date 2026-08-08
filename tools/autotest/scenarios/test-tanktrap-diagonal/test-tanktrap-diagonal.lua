-- AUTO TEST: a vehicle must not drive through the corner two tank traps share.
--
-- Two pockets, seven traps each, identical except for the shape of the opening.
--
--   Squeeze pocket        Control pocket
--   19 20 21              29 30 31
--   #  #  .   15          #  .  #   15
--   #  G  #   16          #  G  #   16
--   #  #  #   17          #  #  #   17
--
-- The squeeze pocket's goal G(20,16) touches open ground only at the corner it
-- shares with 21,15 — getting in means passing between the traps at 20,15 and
-- 21,16, which is the defect. The control pocket's goal G(30,16) has a real
-- orthogonal opening at 30,15 and must stay reachable; if it doesn't, the
-- scenario itself is broken and the squeeze result means nothing.

local DeadlineSeconds = 18

local SqueezeGoal = { x = 20, y = 16 }
local ControlGoal = { x = 30, y = 16 }

local function isAt(actor, goal)
	if not actor or actor.IsDead then
		return false
	end

	local loc = actor.Location
	return loc.X == goal.x and loc.Y == goal.y
end

WorldLoaded = function()
	TestHarness.FocusBetween(Squeezer, Control)
	TestHarness.Select(Squeezer)

	Squeezer.Move(CPos.New(SqueezeGoal.x, SqueezeGoal.y))
	Control.Move(CPos.New(ControlGoal.x, ControlGoal.y))

	local deadlineTicks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	local elapsed = 0
	local controlArrived = false

	local poll
	poll = function()
		if Squeezer.IsDead or Control.IsDead then
			Test.Fail("a test vehicle died; nothing on this map should be shooting")
			return
		end

		if isAt(Squeezer, SqueezeGoal) then
			Test.Fail("squeezer reached 20,16 — a vehicle drove diagonally between the tank traps at 20,15 and 21,16")
			return
		end

		if isAt(Control, ControlGoal) then
			controlArrived = true
		end

		elapsed = elapsed + 1
		if elapsed >= deadlineTicks then
			if controlArrived then
				Test.Pass()
			else
				Test.Fail("control vehicle never reached 30,16 through its orthogonal opening — " ..
					"the scenario is broken, so the squeeze result is not meaningful")
			end

			return
		end

		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
