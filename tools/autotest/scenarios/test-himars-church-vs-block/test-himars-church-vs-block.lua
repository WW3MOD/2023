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

	if not okM and ProbeError == nil then
		ProbeError = tostring(missiles)
		print("HIMARSPROBE binding-error missiles: " .. ProbeError)
	end

	local sig = "B[" .. (okB and chainB or "?") .. " ammo=" .. (okAmB and tostring(ammoB) or "?") ..
		"] A[" .. (okA and chainA or "?") .. " ammo=" .. (okAmA and tostring(ammoA) or "?") ..
		"] missiles=" .. (okM and tostring(missiles) or "?")

	if sig ~= LastSig then
		LastSig = sig
		local line = "t" .. tick .. " " .. sig
		print("HIMARSPROBE " .. line)
		if #Trace < 5 then
			Trace[#Trace + 1] = line
		end
	end
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

	-- allowMove FALSE, and that makes the map's spawn geometry load-bearing rather than merely
	-- convenient: a launcher that repositions changes the impact geometry between the two lanes,
	-- but a launcher that CANNOT reposition also cannot fix a bad spawn.
	--
	-- CITE THE RIGHT ACTIVITY. A HIMARS is AttackTurreted, which derives from AttackFollow, while
	-- AttackFrontal is its SIBLING under AttackBase -- so this unit never runs
	-- Activities/Attack.cs and the `tooClose`/`needsToMove` lines there do not apply to it. (An
	-- earlier version of this comment cited them; corrected 2026-09-21.) The equivalent gate is
	-- AttackFollow.cs:517, which requires in-MaxRange AND out-of-MinRange AND clear LOS; failing it
	-- falls to :528, where `move == null` -- exactly what allowMove=false produces at :404 -- ends
	-- the activity on its first tick. Both launchers are ~24 cells out, clearing the 16c0 minimum by
	-- 7.5; see the geometry block in map.yaml before moving any of the four.
	LauncherB.Attack(Church, false, true)
	LauncherA.Attack(Block, false, true)

	local ticks = 0

	Trigger.OnTick(function()
		ticks = ticks + 1
		Probe(ticks)

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
		end
	end)

	-- Generous: the missile has to fly ~24 cells. Fails with the census rather than timing out
	-- silently, so a run that produced no impact says which lane was quiet.
	--
	-- THE CENSUS NOW NAMES THE RANGE TOO. The first ever run of this scenario reported only
	-- "church 38000/38000, block 120000/120000", which is the signature of a launcher that never
	-- fired at all -- but says nothing about why, and the why was a spawn 10 cells from its target
	-- against MinRange 16c0. Printing each lane's separation turns that diagnosis into a read.
	TestHarness.AssertWithin(40, function() return Reported end, function()
		return "no impact within 40s — church " ..
			(Church.IsDead and "DEAD" or tostring(Church.Health)) .. "/" .. ChurchStart ..
			", block " .. (Block.IsDead and "DEAD" or tostring(Block.Health)) .. "/" .. BlockStart ..
			" — ranges: LauncherB->Church " .. CellsBetween(LauncherB, Church) ..
			"c, LauncherA->Block " .. CellsBetween(LauncherA, Block) ..
			"c (HIMARSTargeter needs >16c and <50c; allowMove is false so these cannot change)" ..
			" — probe: " .. TraceText()
	end)
end

function Verdict()
	local detail = "church " .. tostring(ChurchSeen) .. "/" .. ChurchStart ..
		", block " .. tostring(BlockSeen) .. "/" .. BlockStart

	TestHarness.Screenshot("01-after-one-salvo",
		"expects: the church at its rubble state (heavy damage overlay, burning), the apartment " ..
		"block visibly damaged but standing")

	Trigger.AfterDelay(25, function()
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
