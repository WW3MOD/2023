-- AUTO TEST: a dry launcher with no depot evacuates, and the player order QUEUED behind its
-- last shot goes with it.
--
-- WHAT IS ACTUALLY BEING MEASURED, and why it needed a game rather than an argument: the Auto
-- arm reaches Evacuate from INotifyAttack.Attacking -- i.e. on the tick the last round leaves
-- the tube -- and EvacuateForRefund queues RotateToEdge with QueueActivity(false, ...), which
-- calls CancelActivity first. Activity.Cancel sets NextActivity = null when keepQueue is false
-- (Activity.cs:209-212), so it drops not only the running activity but everything queued behind
-- it. That chain was reasoned from source and never watched. A player order silently vanishing
-- on a unit's last shot is player-visible, so it gets seen once before it ships.
--
-- THE SHAPE: fire, then a queued move EAST. Evacuation goes WEST (RotateToEdge heads for the
-- owner's spawn area, staged at 4,4). So the two dispositions move the unit in opposite
-- directions and cannot be mistaken for one another:
--   * order dropped   -> unit never goes east, drives west, RotateToEdge disposes it -> IsDead
--   * order survived  -> unit walks east to x=20 and sits there, never disposed
--
-- WHY IsDead IS THE PASS CONDITION. Nothing on this map can shoot the Launcher (there are no
-- enemy units at all), and it is staged at full health so ChangesHealth@CriticalDamage never
-- starts. Actor.IsDead is `Disposed || health.IsDead` (Actor.cs:76), so with damage excluded it
-- can only mean RotateToEdge called self.Dispose (RotateToEdge.cs:407) -- that is, it evacuated.
-- IsDead and IsInWorld also sit on BaseActorProperties, which is [ExposedForDestroyedActors] and
-- makes no trait queries, so they stay callable after disposal. Location does NOT -- it reads
-- OccupiesSpace -- which is why every read of it below is guarded by an IsDead check first.
--
-- MinRange: the IskanderTargeter is Range 50c0 / MinRange 16c0 (weapons-missiles.yaml:382-383).
-- The target cell is 22 cells out: inside the maximum and outside the minimum. Put it closer
-- than 16 and the launcher silently declines to fire and the run dies of timeout instead.

-- Budget in TICKS and divide back through the harness constant. TestHarness.TicksPerSecond is
-- 25 while the mod runs at Timestep 60 = 16.67 ticks/second; the constant is deliberately wrong
-- and is pinned by AutotestTickRateTest.cs, so anything sized in "seconds" here would silently
-- mean something else. 1200 ticks is ~72 real seconds.
local function ticks(t) return t / TestHarness.TicksPerSecond end

local DeadlineTicks = 1200
local FireAtCell = CPos.New(32, 16)   -- 22 cells east of the launcher: within [16c0, 50c0]
local MoveToCell = CPos.New(20, 16)   -- the player's queued order: 10 cells EAST
local OrderRanLineX = 18              -- east of here, only the queued move can have put it there
local QueueOrderAfterTicks = 10       -- let the attack latch as the current activity first

local reachedX = 10

WorldLoaded = function()
	TestHarness.FocusBetween(Launcher, OwnSR)
	TestHarness.Select(Launcher)

	-- Force-fire at empty ground. allowMove = false: the target is already in range, and letting
	-- it reposition would muddy "which activity was cancelled". forceAttack = true is implied by
	-- AttackGround (CombatProperties.cs:109) and is required -- the iskander ships
	-- InitialStance: HoldFire.
	Launcher.AttackGround(FireAtCell, false, false)

	-- The player's order, queued BEHIND the attack. MobileProperties.Move uses the queueing
	-- QueueActivity overload (MobileProperties.cs:35), so this lands in the queue rather than
	-- replacing the attack -- which is the state the cancellation has to be observed against.
	Trigger.AfterDelay(QueueOrderAfterTicks, function()
		if not Launcher.IsDead then
			Launcher.Move(MoveToCell)
		end
	end)

	TestHarness.AssertWithin(ticks(DeadlineTicks), function()
		-- Disposal is checked FIRST and is the pass: with nothing able to damage this unit, the
		-- only route to IsDead is RotateToEdge disposing it at the map edge.
		if Launcher.IsDead then
			return true
		end

		-- Safe only while it is alive; Location is not exposed for destroyed actors.
		local x = Launcher.Location.X
		if x > reachedX then
			reachedX = x
		end

		if x >= OrderRanLineX then
			return "fail: launcher reached x=" .. x .. " -- it carried out the queued move order "
				.. "instead of evacuating, so the last-shot cancellation did NOT happen"
		end

		-- Timeout messages are built at AssertWithin CALL time, so the running maximum cannot be
		-- interpolated into one. Report it from inside the predicate instead, on the last tick
		-- before the deadline would fire.
		return false
	end, "Launcher never evacuated and never carried out the queued move either: it still held "
		.. "ammo, never fired, or fired and then stood still where it was")
end
