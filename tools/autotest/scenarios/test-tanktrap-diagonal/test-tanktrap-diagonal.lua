-- AUTO TEST: a full-cell vehicle must not drive through the corner two tank
-- traps share, while a subcell-capable soldier still may.
--
-- Three pockets, seven traps each, identical except for the opening.
--
--   Squeeze (vehicle)     Control (vehicle)     Infantry
--   19 20 21              29 30 31              39 40 41
--   #  #  .   15          #  .  #   15          #  #  .   15
--   #  G  #   16          #  G  #   16          #  G  #   16
--   #  #  #   17          #  #  #   17          #  #  #   17
--
-- Squeeze and Infantry are the same shape: G touches open ground only at the
-- corner it shares with the cell above-right, so getting in means passing
-- between two traps. The rule keys on whether the locomotor shares cells, so
-- the bradley is denied and the rifleman is not.
--
-- Control has a real orthogonal opening and must stay reachable. If it doesn't,
-- the scenario is broken and neither of the other two results means anything.

local DeadlineSeconds = 25

local SqueezeGoal = { x = 20, y = 16 }
local ControlGoal = { x = 30, y = 16 }
local InfantryGoal = { x = 40, y = 16 }

local function isAt(actor, goal)
	if not actor or actor.IsDead then
		return false
	end

	local loc = actor.Location
	return loc.X == goal.x and loc.Y == goal.y
end

WorldLoaded = function()
	TestHarness.FocusBetween(Squeezer, Control, Grunt)
	TestHarness.Select(Squeezer)

	Squeezer.Move(CPos.New(SqueezeGoal.x, SqueezeGoal.y))
	Control.Move(CPos.New(ControlGoal.x, ControlGoal.y))
	Grunt.Move(CPos.New(InfantryGoal.x, InfantryGoal.y))

	local deadlineTicks = math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)
	local elapsed = 0
	local controlArrived = false
	local gruntArrived = false

	local poll
	poll = function()
		if Squeezer.IsDead or Control.IsDead or Grunt.IsDead then
			Test.Fail("a test unit died; nothing on this map should be shooting")
			return
		end

		if isAt(Squeezer, SqueezeGoal) then
			Test.Fail("squeezer reached 20,16 — a vehicle drove diagonally between the tank traps at 20,15 and 21,16")
			return
		end

		if isAt(Control, ControlGoal) then
			controlArrived = true
		end

		if isAt(Grunt, InfantryGoal) then
			gruntArrived = true
		end

		elapsed = elapsed + 1
		if elapsed >= deadlineTicks then
			if not controlArrived then
				Test.Fail("control vehicle never reached 30,16 through its orthogonal opening — " ..
					"the scenario is broken, so the other results are not meaningful")
			elseif not gruntArrived then
				Test.Fail("rifleman never reached 40,16 — the squeeze rule has widened to subcell movers, " ..
					"which is a deliberate exemption and should not have changed")
			else
				Test.Pass()
			end

			return
		end

		Trigger.AfterDelay(1, poll)
	end

	Trigger.AfterDelay(1, poll)
end
