-- AUTO TEST: a medic escorting a healthy squadmate ignores a man bleeding out
-- 10 cells away — and picks him up promptly the moment the escort is gone.
--
-- Two radii, one decision. HealerAutoTarget.SearchRange is 8c0 (infantry.yaml
-- :2216) and is the only thing that makes a casualty visible to the medic at
-- all. AutoFollowAlly.SearchRange is 20c0 (:2249), and FindNearestAlly
-- (AutoFollowAlly.cs:191-207) picks the nearest ally WITHOUT consulting health.
-- A healthy man closer than a wounded one therefore wins the medic's attention
-- and parks him at FollowDistance, from where the wounded man is out of sight.
--
-- The escort is destroyed at the halfway mark. That turns an observation into a
-- controlled experiment: distance to Stranded is unchanged across that moment,
-- so if treatment starts only after Escort disappears, the escort was the cause.
--
-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 —
-- 16.67 ticks/second. A "second" passed to AssertWithin is therefore 1.5 real
-- seconds. Budget in ticks and convert.
--
-- PITFALL: do not assert on pulse cadence or step size. The heal is being
-- reshaped from 10 HP/1.5s to 20 HP/3.0s at identical HP/s, so everything here
-- is a duration or a direction of travel.
local TotalBudgetTicks = 1200 -- ~72 real seconds
local BlindPhaseTicks = 400 -- ~24 real seconds with the escort alive
local StrandedStartPercent = 40

local elapsed = 0
local escortRemoved = false
local removedAtTick = 0
local strandedBaseline = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Stranded)

	Stranded.Health = math.floor(Stranded.MaxHealth * StrandedStartPercent / 100)
	strandedBaseline = Stranded.Health

	local function percent(actor)
		if actor.IsDead then
			return -1
		end

		return math.floor(actor.Health * 100 / actor.MaxHealth)
	end

	TestHarness.AssertWithin(TotalBudgetTicks / TestHarness.TicksPerSecond, function()
		if Medic.IsDead then
			return "fail: medic died"
		end

		elapsed = elapsed + 1

		-- Phase 1: escort alive. Stranded is bleeding and must not be treated —
		-- his health can only fall. If it RISES here the premise is wrong and the
		-- medic sees further than his configured notice radius; say so loudly
		-- rather than passing on a coincidence.
		if not escortRemoved then
			if Stranded.IsDead then
				return "fail: the stranded man bled out before the experiment could run"
			end

			if Stranded.Health > strandedBaseline then
				return "fail: premise wrong — the medic treated a casualty 10 cells away WITHOUT"
					.. " losing his escort, so the 8-cell notice radius is not what gates him"
			end

			if elapsed >= BlindPhaseTicks then
				Escort.Destroy()
				escortRemoved = true
				removedAtTick = elapsed
			end

			return false
		end

		-- Phase 2: nothing about Stranded changed except that the healthy man
		-- next to the medic is gone. He is now the nearest ally, so the follow
		-- layer walks the medic over and the notice radius does the rest.
		local sinceRemoval = elapsed - removedAtTick

		if Stranded.IsDead then
			return "fail: the stranded man bled out " .. sinceRemoval
				.. " ticks after the escort was removed, still untreated"
		end

		if Stranded.Health > strandedBaseline then
			return true
		end

		if elapsed >= TotalBudgetTicks then
			return "fail: the stranded man was never treated at all — " .. BlindPhaseTicks
				.. " ticks with an escort and " .. sinceRemoval .. " ticks without one."
				.. " He is at " .. percent(Stranded) .. "% (wounded to " .. StrandedStartPercent
				.. "). Removing the escort did not free the medic, so the cause is not the escort"
		end

		return false
	end, "escort-blindness assertion did not resolve within " .. TotalBudgetTicks .. " ticks")
end
