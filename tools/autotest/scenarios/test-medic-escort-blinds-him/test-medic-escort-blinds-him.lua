-- AUTO TEST: a medic escorting a HEALTHY squadmate still goes to the man
-- bleeding out 10 cells away. The escort must not be able to anchor him.
--
-- NAME IS HISTORICAL. This began life as a characterization test asserting the
-- escort DID blind him, and its header said to invert rather than relax it once
-- the behaviour was fixed. That happened; this is the inverted form. The
-- directory keeps its name so the WORKSPACE/DISCOVERIES.md trail still resolves.
--
-- The mechanism it guards. Two radii, and the FOLLOW layer decides where the
-- HEAL layer is standing. HealerAutoTarget.SearchRange is 8c0 (infantry.yaml
-- :2216) — the notice radius, measured from wherever the medic happens to be.
-- AutoFollowAlly.SearchRange is 20c0 (:2249) and is what actually moves him.
-- With the follow layer ranking allies by distance alone, a healthy man two
-- cells away parked the medic at FollowDistance and the casualty at ten cells
-- was never a CANDIDATE for the heal scan at all — not outranked, invisible.
-- AutoFollowAlly.FindNearestAlly now puts anyone its healer would treat in a
-- strictly higher tier than any healthy ally, so the medic walks to the
-- casualty and the notice radius follows him there.
--
-- The escort is deliberately kept ALIVE for the whole run. That is the point:
-- his presence must be irrelevant, so the test asserts treatment happens with
-- him still standing there, rather than only after he is removed.
--
-- PITFALL: TestHarness.TicksPerSecond is 25, but the mod runs at Timestep 60 --
-- 16.67 ticks/second. A "second" passed to AssertWithin is therefore 1.5 real
-- seconds. Budget in ticks and convert.
--
-- PITFALL: do not assert on pulse cadence or step size. The heal is being
-- reshaped from 10 HP/1.5s to 20 HP/3.0s at identical HP/s, so everything here
-- is a duration or a direction of travel.
local TotalBudgetTicks = 1200 -- ~72 real seconds
local StrandedStartPercent = 40

local elapsed = 0
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

		-- The escort standing there unhurt IS the experiment. If something kills
		-- him the medic is freed by accident and a later pass would mean nothing.
		if Escort.IsDead then
			return "fail: the healthy escort died, so this run no longer tests"
				.. " whether he could anchor the medic"
		end

		elapsed = elapsed + 1

		if Stranded.IsDead then
			return "fail: the stranded man bled out untreated after " .. elapsed
				.. " ticks, with the medic still standing by his healthy escort"
		end

		if Stranded.Health > strandedBaseline then
			return true
		end

		if elapsed >= TotalBudgetTicks then
			return "fail: a healthy escort still outranks a casualty — the medic spent "
				.. TotalBudgetTicks .. " ticks beside an unhurt man 2 cells away while a"
				.. " casualty 10 cells away went untreated, falling from " .. StrandedStartPercent
				.. "% to " .. percent(Stranded) .. "%. The follow layer picked the nearest ally"
				.. " without asking who needed treating, and parked the medic where his 8-cell"
				.. " notice radius could not reach the wounded man"
		end

		return false
	end, "escort-precedence assertion did not resolve within " .. TotalBudgetTicks .. " ticks")
end
