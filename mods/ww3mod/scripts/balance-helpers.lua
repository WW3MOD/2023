-- Shared helpers for test-balance-* scenarios.
-- A balance test sets up two teams, lets them fight to the death, and reports
-- the outcome (winner, time-to-kill, surviving HP) as a Pass note. Stalemates
-- count as Fail because we want to know if a duel can't resolve in reasonable
-- time at the configured range.

BalanceHarness = {}

-- Sum HP across a list of actors. Dead actors contribute 0.
function BalanceHarness.TotalHP(team)
	local total = 0
	for _, a in ipairs(team) do
		if a and not a.IsDead then
			total = total + a.Health
		end
	end
	return total
end

-- Sum MaxHealth across the original team list (used as the denominator for HP%).
function BalanceHarness.TotalMaxHP(team)
	local total = 0
	for _, a in ipairs(team) do
		if a then
			total = total + a.MaxHealth
		end
	end
	return total
end

-- Count survivors.
function BalanceHarness.LiveCount(team)
	local n = 0
	for _, a in ipairs(team) do
		if a and not a.IsDead then n = n + 1 end
	end
	return n
end

-- Run a duel between two teams. `nameA`/`nameB` are display strings, `teamA`
-- /`teamB` are arrays of actors that have ALREADY been spawned and ordered to
-- engage. Polls every tick; when one side is annihilated, Pass with a one-line
-- summary that the runner surfaces in the verdict. If `deadlineSeconds`
-- elapses with both sides alive, Fail with the partial damage state.
function BalanceHarness.RunDuel(nameA, teamA, nameB, teamB, deadlineSeconds)
	local startTotalA = BalanceHarness.TotalMaxHP(teamA)
	local startTotalB = BalanceHarness.TotalMaxHP(teamB)
	local deadlineTicks = math.floor(deadlineSeconds * TestHarness.TicksPerSecond)
	local elapsed = 0
	local tps = TestHarness.TicksPerSecond

	local tick
	tick = function()
		elapsed = elapsed + 1
		local liveA = BalanceHarness.LiveCount(teamA)
		local liveB = BalanceHarness.LiveCount(teamB)

		if liveA == 0 and liveB == 0 then
			Test.Pass(string.format(
				"DRAW (mutual kill) at t=%.1fs", elapsed / tps))
			return
		end
		if liveA == 0 then
			local hp = BalanceHarness.TotalHP(teamB)
			Test.Pass(string.format(
				"WINNER=%s | ttk=%.1fs | survivors=%d/%d | hp=%d/%d (%.0f%%)",
				nameB, elapsed / tps, liveB, #teamB, hp, startTotalB,
				100 * hp / startTotalB))
			return
		end
		if liveB == 0 then
			local hp = BalanceHarness.TotalHP(teamA)
			Test.Pass(string.format(
				"WINNER=%s | ttk=%.1fs | survivors=%d/%d | hp=%d/%d (%.0f%%)",
				nameA, elapsed / tps, liveA, #teamA, hp, startTotalA,
				100 * hp / startTotalA))
			return
		end

		if elapsed >= deadlineTicks then
			local hpA = BalanceHarness.TotalHP(teamA)
			local hpB = BalanceHarness.TotalHP(teamB)
			Test.Fail(string.format(
				"STALEMATE at deadline %ds | %s: %d/%d alive %d/%d hp (%.0f%%) | %s: %d/%d alive %d/%d hp (%.0f%%)",
				deadlineSeconds,
				nameA, liveA, #teamA, hpA, startTotalA, 100 * hpA / math.max(1, startTotalA),
				nameB, liveB, #teamB, hpB, startTotalB, 100 * hpB / math.max(1, startTotalB)))
			return
		end

		Trigger.AfterDelay(1, tick)
	end
	Trigger.AfterDelay(1, tick)
end

-- Issue a force-attack from each unit in `attackers` against the nearest live
-- unit in `targets`. Useful when both sides should be actively engaging from
-- t=0 instead of waiting for autotarget scan intervals.
function BalanceHarness.ForceEngage(attackers, targets)
	for _, a in ipairs(attackers) do
		if a and not a.IsDead then
			-- Pick first live target — for symmetric duels, near enough.
			for _, t in ipairs(targets) do
				if t and not t.IsDead then
					a.Attack(t, true, true)
					break
				end
			end
		end
	end
end
