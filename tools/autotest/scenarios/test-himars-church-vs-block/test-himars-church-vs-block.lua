-- AUTO TEST: the central claim of the 2026-09-15 garrison retune, end to end.
--
-- THE CLAIM. "A HIMARS missile into a wooden church is a pretty bad day for the church"
-- (user ruling) while a concrete apartment block holds. The retune had to carry that in HP
-- rather than in armour type, because HIMARS' 36000 Warhead@Target and both tank rounds have
-- no Versus table at all -- only the 7000 Warhead@Shockwave discriminates, so the whole armour
-- vocabulary is worth 13% of one hit. This test is what makes that a measurement instead of
-- arithmetic on a warhead file.
--
-- THE NUMBERS BEING PINNED (WORKSPACE/audit/260915-garrison-tuning-table.md):
--   one HIMARS on Light   = 36000 + 2500 + 7000*80%  = 44100
--   one HIMARS on Concrete= 36000 + 2500 + 7000*25%  = 40250
--   V01      38000 HP Light    -> 44100 > 38000, so it bottoms out
--   RUSHOUSE 120000 HP Concrete-> 40250, so ~79750 left, about 66%
--
-- IT BOTTOMS OUT AT 1, NOT 0. GarrisonManager.Indestructible defaults true
-- (GarrisonManager.cs:85) and nothing overrides it on any garrisonable actor, so IDamageFloor
-- clamps both of these at one hit point forever. "Rubbled", not "destroyed".
--
-- WHY THE HEALTH IS LATCHED. HIMARS BurstWait is 250 ticks, and the mod runs at Timestep 60 --
-- 16.67 ticks/s, not the 25/s that a dozen comments in this tree still assume -- so a launcher
-- left alone fires again FIFTEEN seconds later (this line said ten until 2026-09-21) and a poll
-- that happens to run after the second salvo would see the block at ~39500 and fail a correct
-- build. Both readings are taken on the FIRST tick at which each target has taken any damage,
-- and held.
--
-- WHAT THE LATCH ACTUALLY CATCHES, which is not the full 44100. Of HIMARSExplosion's three
-- warheads only two land on the impact tick: Warhead@Target 36000 and Warhead@Spread_impact 2500.
-- Warhead@Shockwave carries StartDelay 2 (weapons-explosions.yaml:603) and so arrives a few ticks
-- later, by which time this latch has already fired. Expect the readings to be 38500 of damage,
-- not 44100 -- church floored to 1, block at 81500. Both thresholds below are set wide enough
-- that it does not matter which side of the shockwave the latch lands on.

-- THE TWO HONEST REDs. Each half of the bar has its own sabotage, and neither is a timing knob.
--
--   TIMING HALF -- "the order must not be issued before the world's first frame-end drain":
--   move the two LauncherB/LauncherA.Attack calls out of the tick poller and back into
--   WorldLoaded, their original call site. Expect FAIL "no impact within 1000 ticks -- church
--   38000/38000, block 120000/120000 ... probe: t1 B[(idle) ammo=2] A[(idle) ammo=2]".
--   `OrderAtTick = 1` is NOT a RED for this: run 260921_175314 passed with it. See the note at the
--   order site.
--
--   DAMAGE HALF -- "one HIMARS rubbles the church and leaves the block over half": add to this
--   scenario's rules.yaml, separated by a blank line, with the key UPPERCASE to match
--   civilian.yaml:302 (top-level MiniYaml merges case-SENSITIVELY, so `v01:` would silently
--   override nothing and the run would come back green):
--       V01:
--           Health:
--               HP: 120000
--   Expect FAIL "one HIMARS did NOT rubble the church: church 81500/120000, block 81500/120000 --
--   expected the 1 HP Indestructible floor ...".
--
-- A RED THAT DOES NOT WORK, recorded so nobody spends a run on it: raising the church's Armor
-- changes nothing. HIMARSExplosion's Warhead@Target carries no Versus table, so 36000 of the 38500
-- that lands on the impact tick is armour-blind -- which is the whole reason this retune had to be
-- carried in HP, and the whole reason this scenario exists.

local ChurchStart, BlockStart
local ChurchSeen, BlockSeen = nil, nil
local Reported = false

-- Horizontal centre-to-centre separation in whole cells, rounded. Same idiom as
-- test-sam-intercepts-iskander.lua:65. Only ever used to build a failure message.
local function CellsBetween(a, b)
	if not a or not b or a.IsDead or b.IsDead then
		return "?"
	end

	local pa, pb = a.CenterPosition, b.CenterPosition
	local dx, dy = pa.X - pb.X, pa.Y - pb.Y
	return tostring(math.floor(math.sqrt(dx * dx + dy * dy) / 1024 + 0.5))
end

-- ---------------------------------------------------------------------------------------------
-- WHY THIS PROBE EXISTS. Two runs (2026-09-21) produced the identical census -- both targets at
-- EXACTLY full HP -- first with the launchers 10 cells out (inside HIMARSTargeter's MinRange 16c0)
-- and then at 24 cells, inside the band. So the range was real but not the whole story, and the
-- second run proved that reading the HP alone cannot distinguish the two questions that matter:
--
--   (1) did the attack ACTIVITY survive, or did it end on its first tick?
--   (2) did the armament ever FIRE -- i.e. is this "no shot" or "a shot that put no missile
--       into the world"?
--
-- Nothing in debug.log answers either. WW3_GUNTRACE=1 only instruments Armament.CheckFire
-- (Armament.cs:500-526), which is never reached when the refusal is one rung above it in
-- AttackFollow.Tick's `IsAiming` conjunction (:213-215) or in the activity itself.
--
-- The three readings below settle both questions between them, and Test.ActivityChain exists for
-- precisely this purpose -- its own [Desc] says inferring this by reading code "has already
-- produced one confident wrong answer" (TestGlobal.cs:1195-1200).
--
--   ActivityChain "(idle)"                   -> the AttackActivity RETURNED TRUE and ended. Look at
--                                               AttackFollow.cs:442 (AmmoPool.CannotFight), :447
--                                               (RequestedTarget invalidated), :508 (target hidden
--                                               with no usable last-seen position) or :528
--                                               (maxRange zero / below minRange, with move == null
--                                               because allowMove is false).
--   ActivityChain names an attack activity    -> the activity SURVIVED, so range, LOS and target
--     for the whole window                      validity all passed (:517-524) and the refusal is
--                                               in the firing gate: CanAimAtTarget (:112-128),
--                                               ReadyToEngage -> AttackTurreted.CanAttack (turret
--                                               facing + AttackBase.CanAttack's SetupTicks 100),
--                                               DefconFireDiscipline.Permits, or Armament.CanFire.
--                                               THIS is the case where WW3_GUNTRACE=1 pays off.
--   ammo 2 -> 1                               -> the armament FIRED. Everything above is innocent
--                                               and the fault is downstream: MissileSpawnerMaster's
--                                               INotifyAttack.Attacking hook found no launchable
--                                               slave (:98-100), or the missile flew and its
--                                               warheads did nothing.
--   ammo stays 2 AND missiles stays 0        -> no shot was ever taken. Combined with the chain
--                                               reading above, that names the rung.
--   missiles goes 0 -> 1                     -> the rocket exists; the remaining suspects are
--                                               flight and warhead, not the launcher.
--
-- Sampled every tick but RECORDED ONLY ON CHANGE, so the trace is a handful of lines rather than
-- a thousand. Full trace goes to lua.log via print(); the first few lines also ride into the
-- failure message, because a zero-byte lua.log is exactly what the last two runs produced and the
-- verdict has to carry its own evidence.
local Trace = {}
local LastSig = nil
local ProbeError = nil

-- EVERY probe read goes through pcall, and every one is wrapped in a CLOSURE rather than passed as
-- `pcall(obj.Method, obj, ...)`. OpenRA exposes actor properties as already-bound closures — the
-- scenario's own `LauncherB.Attack(Church, false, true)` is a dot call with no self — so handing
-- pcall an extra leading argument would push the real one off the end. A probe that is meant to
-- explain a silent failure must not become a second one: if a binding shape here is wrong the
-- reading degrades to "?" and names itself in lua.log, instead of killing the OnTick trigger and
-- costing the run the verdict it exists to produce.
local function Probe(tick)
	local okB, chainB = pcall(function() return Test.ActivityChain(LauncherB) end)
	local okA, chainA = pcall(function() return Test.ActivityChain(LauncherA) end)
	-- Pool name must be "primary-ammo" (vehicles-america.yaml:1144). AmmoCount THROWS on an
	-- unknown pool (AmmoPoolProperties.cs:38), so a typo here would surface in lua.log, not hide.
	local okAmB, ammoB = pcall(function() return LauncherB.AmmoCount("primary-ammo") end)
	local okAmA, ammoA = pcall(function() return LauncherA.AmmoCount("primary-ammo") end)
	local okM, missiles = pcall(function() return #LauncherB.Owner.GetActorsByType("himarsmissile") end)
	-- THE READING THAT WILL SETTLE RUN 3'S HYPOTHESIS -- it has not settled it yet.
	-- Test.IsDetectedBy goes straight through Actor.CanBeViewedByPlayer (TestGlobal.cs:1771-1776),
	-- so it is the engine's own answer to "can USA see the church", which is what decides
	-- `targetIsHiddenActor` in the attack activity. false-early-then-true confirms the viewability
	-- mechanism; true throughout refutes it and the order comment above needs rewriting.
	local okV, visC = pcall(function() return Test.IsDetectedBy(Church, LauncherB.Owner) end)

	if not okM and ProbeError == nil then
		ProbeError = tostring(missiles)
		print("HIMARSPROBE binding-error missiles: " .. ProbeError)
	end

	local sig = "B[" .. (okB and chainB or "?") .. " ammo=" .. (okAmB and tostring(ammoB) or "?") ..
		"] A[" .. (okA and chainA or "?") .. " ammo=" .. (okAmA and tostring(ammoA) or "?") ..
		"] missiles=" .. (okM and tostring(missiles) or "?") ..
		" churchVisible=" .. (okV and tostring(visC) or "?")

	if sig ~= LastSig then
		LastSig = sig
		local line = "t" .. tick .. " " .. sig
		print("HIMARSPROBE " .. line)
		if #Trace < 5 then
			Trace[#Trace + 1] = line
		end
	end
end

-- Dumped twice -- once at tick 1 and once at the tick the order is issued -- so the two can be
-- compared. Long, so it goes to lua.log only and never into result.json.
-- NOTE THE ARGUMENT ORDER: TargetableReport is (target, byActor) — target FIRST
-- (TestGlobal.cs:921). Reversed, it would report the LAUNCHER's targetability and read as a
-- perfectly plausible answer to a question nobody asked.
local function Snapshot(label)
	local ok, report = pcall(function() return Test.TargetableReport(Church, LauncherB) end)
	print("HIMARSPROBE " .. label .. " church-targetable: " .. (ok and report or "?"))

	local ok2, canB = pcall(function() return LauncherB.CanTarget(Church) end)
	local ok3, canA = pcall(function() return LauncherA.CanTarget(Block) end)
	print("HIMARSPROBE " .. label .. " canTarget B->Church=" .. (ok2 and tostring(canB) or "?") ..
		" A->Block=" .. (ok3 and tostring(canA) or "?"))
end

-- LIVE health, for the watchdog. Distinct from the LATCHED reading Verdict asserts on: if nothing
-- ever hit, there is no latch to report and the live numbers are the whole story.
local function Census()
	return "church " .. (Church.IsDead and "DEAD" or tostring(Church.Health)) .. "/" .. tostring(ChurchStart) ..
		", block " .. (Block.IsDead and "DEAD" or tostring(Block.Health)) .. "/" .. tostring(BlockStart)
end

local function TraceText()
	if #Trace == 0 then
		return "probe recorded nothing"
	end

	return table.concat(Trace, " ;; ")
end

WorldLoaded = function()
	ChurchStart = Church.Health
	BlockStart = Block.Health

	TestHarness.FocusBetween(Church, Block)
	Test.SetZoom(1)

	-- DO NOT ORDER FROM WorldLoaded. This is the one scenario-side difference between this file and
	-- the launcher scenario that has been passing all along, and it is why the order dies.
	--
	-- WHAT IS MEASURED. Runs 2 and 3 both ended with
	-- `HIMARSPROBE t1 B[(idle) ammo=2] A[(idle) ammo=2] missiles=0` as the ONLY probe line and zero
	-- [GUNTRACE] lines. Both launchers were already IDLE at the first Lua tick, ammo untouched: the
	-- attack activity died before tick 1 and no armament was ever consulted. Geometry cannot explain
	-- it -- runs 1 (10 cells, inside MinRange) and 2 (24 cells, inside the band) failed identically.
	--
	-- WHAT IS ESTABLISHED. The pre-explored map is applied as a FRAME-END TASK, not inline:
	-- `if (ExploreMapEnabled) self.World.AddFrameEndTask(w => ExploreAll());` (MapLayers.cs:200-201).
	-- Lua's WorldLoaded runs BEFORE that task drains, so the world genuinely is not settled here.
	-- test-sam-intercepts-iskander orders from a tick poller at `FireAtTick = 20`, commented "let the
	-- world settle before ordering" (:48), and never from WorldLoaded. This file is copying that.
	--
	-- WHAT IS NOW OBSERVED, not inferred. Run 260921_174914 (GREEN) logged
	-- `t1 ... churchVisible=false` then `t2 ... churchVisible=true`: the deferral is exactly ONE
	-- TICK wide, and Test.IsDetectedBy goes straight through Actor.CanBeViewedByPlayer
	-- (TestGlobal.cs:1771-1776). So the church genuinely is unviewable while WorldLoaded runs, which
	-- is what makes AttackActivity's constructor skip recording `lastVisibleTarget`
	-- (AttackFollow.cs:414-417) and the first Tick end the activity at `:508` -- "target is hidden
	-- or dead, and we don't have a fallback position to move towards" -- on tick one, silently. An
	-- earlier version of this comment flagged that chain as suspected-but-unproven; it is proven now.
	--
	-- THE DISCRIMINATOR IS THE CALL SITE, NOT THE LENGTH OF THE DELAY, and this matters because the
	-- obvious RED is wrong. Run 260921_175314 set `OrderAtTick = 1` and PASSED: at t1 the trace
	-- reads `B[AttackActivity]` WITH `churchVisible=false`, and the activity survived anyway. An
	-- order issued from OnTick at tick N has its first activity tick in tick N+1, by which time the
	-- frame-end queue has drained -- so ANY tick >= 1 is safe, and only WorldLoaded, which lands
	-- before tick 1's activity pass, is not. `OrderAtTick = 20` is kept purely as harmless margin,
	-- copied from test-sam-intercepts-iskander.lua:48; 1 would do. Do NOT use a smaller OrderAtTick
	-- as a RED for this -- see THE TWO HONEST REDs at the top of this file.
	--
	-- WHY NO LOG EVER SAID SO. `CombatProperties.Attack` does warn about an unviewable target, but
	-- that branch is gated on `!HasTraitInfo<FrozenUnderFogInfo>()` (CombatProperties.cs:97) and
	-- these buildings carry FrozenUnderFog (structures.yaml:80, via ^BasicBuilding) -- so for exactly
	-- these targets the warning is suppressed and lua.log stayed empty while the order died.
	--
	-- NOT A RULES REGRESSION, AND THE CHURCH IS HIMARS-ABLE IN PLAY. A player cannot issue an order
	-- before the first frame-end task has drained; only a script can. The tuning branch's Targetable
	-- blocks are fine: ^CivBuilding carries Ground and no RequiresForceFire (civilian.yaml:17-19,
	-- and the note at :45-72 explains why that flag was deliberately deleted). The
	-- `RequiresForceFire: true` at civilian.yaml:287 belongs to ^Bridge, not to any civilian
	-- building. 58 of 317 scenarios issue an order from WorldLoaded and 30 of those use .Attack(,
	-- so most get away with it -- presumably because their targets sit inside the attacker's own
	-- vision bubble, which is live from world setup. A 24-cell indirect-fire shot does not.
	local OrderAtTick = 20
	local ordered = false

	-- NO AssertWithin HERE, AND THAT IS THE POINT. This scenario used
	-- `TestHarness.AssertWithin(40, function() return Reported end, ...)` until 2026-09-21, and it
	-- made both of that day's GREEN runs worthless: AssertWithin calls `Test.Pass()` ITSELF the
	-- moment its predicate returns true (test-helpers.lua:91-93), with NO note, and that is a
	-- TERMINAL verdict that exits the game. `Reported` is set in the latch above, 2 + 25 = 27 ticks
	-- before Verdict's assertions would run -- so AssertWithin won the race every time, Verdict
	-- NEVER EXECUTED, and the runs passed without ever evaluating either half of the bar. The
	-- evidence is in the run dirs of 260921_174914 and 260921_175315: `"notes":""`, no
	-- `screenshots` key, and no PNG on disk. A test that cannot fail is not a passing test.
	--
	-- THE RULE: a predicate handed to AssertWithin must NEVER become true in a scenario that has
	-- its own assertions. AssertWithin is safe only as a pure watchdog whose predicate is always
	-- false, or in a scenario whose ONLY question is "did X happen in time". Here the deadline is
	-- open-coded instead, so Verdict is the single verdict authority.
	--
	-- THE BUDGET, IN TICKS, because that is the unit the engine uses. 1000 ticks is 60 s at
	-- Timestep 60 (16.67 ticks/s) -- deliberately the same number AssertWithin(40) resolved to via
	-- TestHarness.TicksPerSecond = 25, so the deadline did not silently move with this change.
	-- Against it: order at t20, then SetupTicks 100 + up to ~52 ticks of turret travel at TurnSpeed
	-- 10 + AimingDelay 40, then ~120 ticks of flight. Measured in run 260921_174914: ammo left the
	-- tube at t177 and the last missile left the world at t281, so impact is ~t280 and there is
	-- roughly 3.5x headroom. Do not trim this to the measured margin.
	local DeadlineTicks = 1000

	-- allowMove FALSE: a launcher that repositions changes the impact geometry between the two
	-- lanes, so the map's spawn separation is part of the assertion. Both are ~24 cells out,
	-- clearing HIMARSTargeter's 16c0 minimum by 7.5 and well inside its 50c0 maximum; the gate is
	-- AttackFollow.cs:517 and the give-up is :528. See the geometry block in map.yaml.
	local ticks = 0

	Trigger.OnTick(function()
		ticks = ticks + 1

		if not ordered and ticks >= OrderAtTick then
			ordered = true
			Snapshot("t" .. ticks .. " at-order")
			LauncherB.Attack(Church, false, true)
			LauncherA.Attack(Block, false, true)
		end

		Probe(ticks)

		if ticks == 1 then
			Snapshot("t1 pre-order")
		end

		if Reported then
			return
		end

		if ChurchSeen == nil and not Church.IsDead and Church.Health < ChurchStart then
			ChurchSeen = Church.Health
		end

		if BlockSeen == nil and not Block.IsDead and Block.Health < BlockStart then
			BlockSeen = Block.Health
		end

		if ChurchSeen ~= nil and BlockSeen ~= nil then
			Reported = true
			Trigger.AfterDelay(2, Verdict)
			return
		end

		-- The watchdog, and the ONLY other terminal verdict in this file. Carries the census, both
		-- separations and the probe trace, because "both targets at exactly full HP" is the shared
		-- signature of at least four unrelated causes and it took three runs to learn that health
		-- alone cannot tell them apart.
		if ticks >= DeadlineTicks then
			Reported = true
			local census = Census()
			print("HIMARSCENSUS timeout " .. census)
			Test.Fail("no impact within " .. DeadlineTicks .. " ticks — " .. census ..
				" — ranges: LauncherB->Church " .. CellsBetween(LauncherB, Church) ..
				"c, LauncherA->Block " .. CellsBetween(LauncherA, Block) ..
				"c (HIMARSTargeter needs >16c and <50c; allowMove is false so these cannot change)" ..
				" — probe: " .. TraceText())
		end
	end)

end

function Verdict()
	-- THE LATCHED reading, taken on the first tick each target took any damage (see the header for
	-- why it is latched and why it reads 38500 of damage rather than 44100).
	local detail = "church " .. tostring(ChurchSeen) .. "/" .. tostring(ChurchStart) ..
		", block " .. tostring(BlockSeen) .. "/" .. tostring(BlockStart)

	-- TO lua.log FIRST, BEFORE ANY VERDICT CAN EXIT THE GAME. The two HP readings ARE the bar, and
	-- for two runs on 2026-09-21 they existed only inside a Lua local that nothing ever printed:
	-- result.json carried `"notes":""` and lua.log carried no census at all, so a PASS said nothing
	-- about what it had measured. Recording it here means the numbers survive regardless of which
	-- branch below fires, and regardless of whether a future edit reintroduces a competing verdict.
	print("HIMARSCENSUS " .. detail)

	TestHarness.Screenshot("01-after-one-salvo",
		"expects: the church at its rubble state (heavy damage overlay, burning), the apartment " ..
		"block visibly damaged but standing")

	Trigger.AfterDelay(25, function()
		-- Defensive: Verdict is only scheduled once both latches are set, so this cannot fire
		-- today. It is here because the failure this file just had was a verdict path that was
		-- never reached, and a nil arriving at the comparison below would raise a Lua error whose
		-- only symptom is a watchdog timeout with a misleading message.
		if ChurchSeen == nil or BlockSeen == nil then
			Test.Fail("verdict reached with an incomplete census: " .. detail)
			return
		end

		if ChurchSeen > 1000 then
			Test.Fail("one HIMARS did NOT rubble the church: " .. detail ..
				" — expected the 1 HP Indestructible floor: 38000 HP Light against 38500 on the impact" ..
				" tick alone (36000 Warhead@Target + 2500 Warhead@Spread_impact), 44100 once the" ..
				" shockwave lands")
			return
		end

		if BlockSeen < math.floor(BlockStart / 2) then
			Test.Fail("one HIMARS took more than half the apartment block: " .. detail ..
				" — expected about 81500 of 120000 left: the latch reads the impact tick (38500)," ..
				" and 79750 if it catches the delayed shockwave too (40250 on Concrete)")
			return
		end

		Test.Pass(detail)
	end)
end
