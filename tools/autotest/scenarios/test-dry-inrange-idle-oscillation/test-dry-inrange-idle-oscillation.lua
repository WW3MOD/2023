-- AUTO TEST / MEASUREMENT: the one case on this branch nobody had observed.
--
-- A dry rifleman standing INSIDE his own weapon range of a live target, with no
-- resupplier anywhere on the map. Every other scenario here halts its dry unit OUTSIDE
-- weapon range, where AutoTarget's in-range armament filter finds nothing and the unit
-- simply rests. In range, it keeps re-issuing the attack that AmmoPool.CannotFight keeps
-- ending, so the unit cycles idle -> busy -> idle instead of latching idle.
--
-- The claim under test is that the cycle is BOUNDED AND CHEAP, not pathological. It should
-- be paced by AutoTarget's scan interval (SharedRandom.Next(16, 32) for WW3MOD infantry,
-- AutoTarget.cs:934-937), so the man should read idle for the great majority of ticks and
-- busy for roughly the single tick each re-issued attack survives.
--
-- Verdict, deliberately asymmetric: FAIL only on the pathological shape — busy most of the
-- time, which would mean the guard is not actually freeing him. A unit that latches idle
-- and never cycles at all also passes; that would be better, not worse, and is recorded in
-- the note rather than being failed as "did not reproduce". The measured numbers go into
-- the verdict note either way, because the number IS the deliverable here.

local SettleTicks = 50 -- let the first scan/queue happen before sampling
local SampleTicks = 250 -- 10s of samples at 25 ticks/s

local idleTicks = 0
local busyTicks = 0
local transitions = 0
local wasIdle = nil
local sampled = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Target)
	TestHarness.Select(Hunter)

	-- The target is a prop: it must survive, and must not shoot the subject of the
	-- measurement out from under it.
	Target.Stance = "HoldFire"

	Trigger.AfterDelay(SettleTicks, function()
		local sample
		sample = function()
			if Hunter.IsDead then
				Test.Fail("Hunter died during measurement")
				return
			end

			local isIdle = Hunter.IsIdle
			if isIdle then idleTicks = idleTicks + 1 else busyTicks = busyTicks + 1 end
			if wasIdle ~= nil and isIdle ~= wasIdle then transitions = transitions + 1 end
			wasIdle = isIdle
			sampled = sampled + 1

			if sampled < SampleTicks then
				Trigger.AfterDelay(1, sample)
				return
			end

			local pct = math.floor(idleTicks * 100 / SampleTicks)
			local note = "idle " .. idleTicks .. "/" .. SampleTicks .. " ticks (" .. pct
				.. "%), busy " .. busyTicks .. ", transitions " .. transitions
				.. ", ammo " .. Hunter.AmmoCount("primary-ammo")

			-- Pathological = busy most of the time. That would mean the dry man is still
			-- effectively holding an attack order and the guard bought nothing.
			if pct < 50 then
				Test.Fail("dry in-range unit is busy most of the time: " .. note)
				return
			end

			Test.Pass(note)
		end

		sample()
	end)
end
