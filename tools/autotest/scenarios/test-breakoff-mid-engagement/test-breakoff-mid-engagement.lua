-- Does a turreted unit that is ALREADY FIRING stop when its target goes
-- critical mid-engagement?
--
-- This is the lane 70f3dc18 actually changed, and the one the suite did not
-- have. See map.yaml for why the sibling scenario does not cover it: it stages
-- the target already-critical, so it only ever exercises AutoTarget's
-- ACQUISITION filter, which predates the fix by a long way.
--
-- SHAPE OF THE RUN, per lane:
--   ACQUIRE  (AcquireTicks)  target healthy. Both lanes must fire. A lane that
--                            does not fire here never locked a target and its
--                            later silence means nothing -> SETUP INVALID.
--   [subject's target is set to 20% HP -> critical-damage is granted]
--   SETTLE   (SettleTicks)   ignored. Absorbs a shot already committed on the
--                            tick the condition lands.
--   MEASURE  (MeasureTicks)  the verdict. Subject must fire ZERO shots.
--                            Control must fire MORE THAN zero.
--
-- Everything is expressed in TICKS and polled with Trigger.AfterDelay(1, ...).
-- TestHarness.TicksPerSecond is 25 while the mod runs at 16.67 (AUTOTEST.md),
-- so any window written in seconds is silently 1.5x longer than it reads. Ticks
-- are immune to that and to whatever game speed a run happens to use.
--
-- WHO ELSE COULD MAKE THE SUBJECT GO QUIET? Every one of these is a guard
-- below, because each would produce the same silence as a working break-off:
--   ammo ran dry            -> AmmoPool raised to 4000 in rules.yaml, and end
--                              -of-run ammo is asserted > 0 on both lanes.
--   the target died         -> HP 1000000; target asserted alive at the end.
--   the shooter died        -> asserted alive at the end.
--   the shooter took heavy  -> tunguska's armaments carry
--   damage and its guns        `PauseOnCondition: ... || heavy-damage-attained`,
--   were PauseOnCondition'd    which silences it for a reason that has nothing to
--                              do with break-off. The t90s are put on HoldFire so
--                              they never shoot back, and the shooters' minimum
--                              health fraction is tracked and asserted at 100%.
--   the target left vision  -> fog is off (rules.yaml) and nothing moves.
--   the manipulation missed -> subject's target asserted < 25% at the end,
--                              control's asserted >= 25%.
--
-- The Lua Health setter routes through InflictDamage (HealthProperties.cs:33),
-- so the damage-state notifications fire normally and GrantConditionOnDamageState
-- really grants critical-damage. It is not a back-door poke past the trait.
--
-- NOTE ON WHY THE SUBJECT IS NEVER ORDERED TO ATTACK. AttackBase.BreakOffApplies
-- exempts AttackSource.Default, and the Lua Actor.Attack binding passes exactly
-- that (BreakOffScopeTest:67-68). An engagement staged with l.shooter.Attack(...)
-- would be exempt from the guard by design and the lane could never go green.
-- The engagement here is acquired by AutoTarget itself, which is
-- AttackSource.AutoTarget and in scope.

local AcquireTicks = 100
local SettleTicks = 12

-- MEASURE is the asserted window: subject must fire ZERO, control MORE than
-- zero. 40 ticks is sized off measured control cadence, not guessed — the 30mm
-- runs ~12 ticks of fire against ~18 idle, so 40 ticks contains at least one
-- full control burst and cannot come up empty by phase alignment.
local MeasureTicks = 40

-- WATCH is observed and REPORTED but deliberately NOT asserted. Run
-- 260827_010229 showed the subject silent from the tick the condition landed
-- through t41, then a single 7-tick burst at t42, then silence for the rest of
-- the run. That re-acquisition is real and is surfaced in the notes of every
-- run rather than tuned out of the window — shrinking MEASURE until the burst
-- fell outside it would have turned an unexplained behaviour into a green.
-- It is not asserted because it is not what 70f3dc18 claims to fix: the guard
-- is about dropping the target the tick it goes critical, and the numbers that
-- prove that live entirely inside MEASURE.
local WatchTicks = 160

local CriticalFraction = 20

local Lanes = {
	{ id = 0, subject = false },
	{ id = 1, subject = true },
}

local phase = "acquire"
local setupFaults = {}
local report = {}

local function cellStr(c)
	return c.X .. ":" .. c.Y
end

local function cellDist(a, b)
	local dx = math.abs(a.X - b.X)
	local dy = math.abs(a.Y - b.Y)
	if dx > dy then return dx end
	return dy
end

local function healthPct(a)
	if a.IsDead then return -1 end
	return math.floor(a.Health * 100 / a.MaxHealth)
end

local traceTick = 0

local function pollTick()
	-- Counts ticks since the condition landed, so a reported tick number is
	-- directly comparable across runs and across the RED/GREEN arms.
	if phase ~= "acquire" then
		traceTick = traceTick + 1
	end

	for _, l in ipairs(Lanes) do
		-- Hold the damage state fixed. This is the independent variable, and
		-- WW3MOD actively fights anything held under 50% (see rules.yaml). The
		-- explicit -ChangesHealth@CriticalDamage there removes the drain we know
		-- about; re-pinning every tick covers any we do not, including the
		-- shooter's own residual hits. Exactly floor(20%) never crosses the 25%
		-- Critical boundary, so the condition cannot flicker.
		if l.subject and l.pinHealth and not l.target.IsDead then
			local want = math.floor(l.target.MaxHealth * CriticalFraction / 100)
			if l.target.Health ~= want then
				l.target.Health = want
			end
		end

		if not l.shooter.IsDead then
			local ammo = l.shooter.AmmoCount("primary-ammo")
			local hp = healthPct(l.shooter)
			if hp >= 0 and hp < l.shooterMinHp then l.shooterMinHp = hp end

			local drift = cellDist(l.shooter.Location, l.startCell)
			if drift > l.maxDrift then l.maxDrift = drift end

			if phase == "acquire" then
				l.acquireShots = l.ammoAtStart - ammo
			elseif phase == "measure" then
				l.measureShots = l.ammoAtMeasureStart - ammo
			elseif phase == "watch" then
				local shots = l.ammoAtWatchStart - ammo
				if shots > 0 and l.firstWatchShotTick < 0 then
					l.firstWatchShotTick = traceTick
				end
				l.watchShots = shots
			end
		end
	end

	-- Live trace to lua.log. The failure STRING is evaluated eagerly at
	-- registration (AUTOTEST.md), so counters interpolated into it would report
	-- their initial values forever; a periodic print is the only way to see when
	-- firing actually stopped relative to when the condition landed.
	if phase ~= "acquire" then
		local line = "[breakoff] t" .. traceTick
		for _, l in ipairs(Lanes) do
			line = line .. " | L" .. l.id .. (l.subject and "SUBJ" or "CTRL")
				.. " ammo" .. (l.shooter.IsDead and -1 or l.shooter.AmmoCount("primary-ammo"))
				.. " tgthp" .. healthPct(l.target)
		end
		print(line)
	end
end

local function runPhase(ticks, onDone)
	local remaining = ticks
	local step
	step = function()
		pollTick()
		remaining = remaining - 1
		if remaining <= 0 then
			onDone()
		else
			Trigger.AfterDelay(1, step)
		end
	end
	Trigger.AfterDelay(1, step)
end

local function fault(s)
	table.insert(setupFaults, s)
end

local function finish()
	for _, l in ipairs(Lanes) do
		local name = "L" .. l.id .. (l.subject and "SUBJ" or "CTRL")

		if l.shooter.IsDead then fault(name .. " shooter died") end
		if l.target.IsDead then fault(name .. " target died") end

		local ammoLeft = l.shooter.IsDead and -1 or l.shooter.AmmoCount("primary-ammo")
		local tgtPct = healthPct(l.target)

		-- A shooter that emptied its pool is silent for a reason that has
		-- nothing to do with break-off, so the lane cannot be read.
		if ammoLeft == 0 then
			fault(name .. " shooter ran dry - silence is not attributable")
		end

		-- heavy-damage-attained is a PauseOnCondition on both of this unit's
		-- gun armaments. If the shooter took ANY damage the lane is suspect;
		-- the t90s are on HoldFire precisely so this cannot happen.
		if l.shooterMinHp < 100 then
			fault(name .. " shooter was damaged to hp" .. l.shooterMinHp
				.. "% - its guns may have been PauseOnCondition'd, not broken off")
		end

		-- The engagement must have STARTED. Without a shot in the acquire
		-- phase there was never a locked RequestedTarget to break off from,
		-- and the measured silence is just a unit that never engaged.
		if l.acquireShots <= 0 then
			fault(name .. " never fired while the target was healthy - no engagement to break off")
		end

		if not l.target.IsDead then
			if l.subject and tgtPct >= 25 then
				fault(name .. " target hp" .. tgtPct
					.. "% is NOT inside the <25% Critical band - critical-damage never applied")
			end
			if not l.subject and tgtPct < 25 then
				fault(name .. " control target fell to hp" .. tgtPct .. "% and went critical itself")
			end
		end

		table.insert(report, table.concat({
			name,
			"tgthp" .. tgtPct .. "%",
			"acq" .. l.acquireShots,
			"meas" .. l.measureShots,
			-- Observed, not asserted. See the WatchTicks note at the top.
			"watch" .. l.watchShots,
			"watch1st" .. l.firstWatchShotTick,
			"ammo" .. ammoLeft,
			"shooterhp" .. l.shooterMinHp .. "%",
			"drift" .. l.maxDrift,
			"cell" .. cellStr(l.startCell),
		}, " "))
	end

	local summary = table.concat(report, " | ")

	if #setupFaults > 0 then
		Test.Fail("SETUP INVALID: " .. table.concat(setupFaults, "; ") .. " || " .. summary)
		return
	end

	local control, subject
	for _, l in ipairs(Lanes) do
		if l.subject then subject = l else control = l end
	end

	-- FALSIFICATION CONTROL FIRST. If the healthy lane also stopped firing,
	-- the ammo observable is not moving during the measurement window at all
	-- and the subject's zero says nothing about break-off.
	if control.measureShots <= 0 then
		Test.Fail("CONTROL DID NOT FIRE during the measurement window - the observable is dead, "
			.. "so the subject's silence is not evidence || " .. summary)
		return
	end

	if subject.measureShots > 0 then
		Test.Fail("subject kept firing at a target that went critical MID-ENGAGEMENT "
			.. "(this is the pre-70f3dc18 behaviour) || " .. summary)
		return
	end

	Test.Pass(summary)
end

WorldLoaded = function()
	local Russia = Player.GetPlayer("Russia")
	if Russia == nil then
		Test.Fail("Russia player not found")
		return
	end

	local shooters = { S0, S1 }
	local targets = { T0, T1 }

	for i, l in ipairs(Lanes) do
		l.shooter = shooters[i]
		l.target = targets[i]
		if l.shooter == nil or l.shooter.IsDead then
			Test.Fail("shooter missing for lane " .. l.id)
			return
		end
		if l.target == nil or l.target.IsDead then
			Test.Fail("target missing for lane " .. l.id)
			return
		end

		-- Silence the ENEMY, never the unit under test (AUTOTEST.md gotcha 9).
		-- A t90 shooting back could drive the Tunguska to heavy damage, whose
		-- condition is a PauseOnCondition on both of its gun armaments — that
		-- would stop the firing for a reason that is not break-off at all.
		l.target.Stance = "HoldFire"

		l.startCell = l.shooter.Location
		l.ammoAtStart = l.shooter.AmmoCount("primary-ammo")
		l.acquireShots = 0
		l.measureShots = 0
		l.ammoAtMeasureStart = 0
		l.ammoAtWatchStart = 0
		l.watchShots = 0
		l.firstWatchShotTick = -1
		l.shooterMinHp = 100
		l.maxDrift = 0
	end

	TestHarness.FocusBetween(S1, T1)
	TestHarness.Select(S1)

	-- PHASE ACQUIRE — both targets healthy, autotarget engages on its own.
	runPhase(AcquireTicks, function()
		-- Drive ONLY the subject's target into DamageState.Critical, while the
		-- Tunguska is mid-engagement with a RequestedTarget already locked.
		local subject
		for _, l in ipairs(Lanes) do
			if l.subject then subject = l end
		end

		if not subject.target.IsDead then
			subject.target.Health = math.floor(subject.target.MaxHealth * CriticalFraction / 100)
			subject.pinHealth = true
		end

		phase = "settle"
		runPhase(SettleTicks, function()
			for _, l in ipairs(Lanes) do
				l.ammoAtMeasureStart = l.shooter.IsDead and 0
					or l.shooter.AmmoCount("primary-ammo")
			end

			phase = "measure"
			runPhase(MeasureTicks, function()
				for _, l in ipairs(Lanes) do
					l.ammoAtWatchStart = l.shooter.IsDead and 0
						or l.shooter.AmmoCount("primary-ammo")
				end

				phase = "watch"
				runPhase(WatchTicks, finish)
			end)
		end)
	end)
end
