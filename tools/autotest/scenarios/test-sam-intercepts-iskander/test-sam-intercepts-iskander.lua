-- ASSERTING AUTOTEST — does a buildable SAM shoot an Iskander missile out of the sky?
--
-- WHAT IS UNDER TEST. On 2026-09-05 the SAM's Buildable.Prerequisites went from `~disabled` to
-- `supplyroute, ~techlevel.medium`. Recon 260905-intercepting-ballistic-munitions.md §2 Option A
-- argues that this single flip IS the feature: the Iskander's in-flight munition is a real world
-- actor carrying `TargetTypes: ICBM` at both altitude bands, six weapons already list ICBM, and
-- all five hosts were shelved. This run checks both halves of that claim.
--
-- CHECK 0 IS NOT DECORATION. A map-placed SAM bypasses Buildable entirely, so every other check
-- in this file would pass just as happily with the prerequisite reverted to `~disabled`.
-- Player.HasPrerequisites is the only assertion here that can see the change actually made. It
-- strips `~` internally (TechTree.cs:68-69), so the un-prefixed forms below are correct.
--
-- WHY "THE ABRAMS SURVIVED" IS NOT SUFFICIENT ON ITS OWN, and why the last-cell reading exists.
-- The victim can also survive because the launcher never fired, because the missile never left
-- the rail, or because it flew somewhere else entirely. Each of those is a different bug and each
-- gets its own check below, in the order they occur, so a failure names its own cause.
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The CRAM and the AGUN. Both stay `~disabled` on purpose — the CRAM misses AND fires with an
--     empty magazine (WORKSPACE/bugs/discovered.md, 2026-09-05). Recon §5.
--   * Whether bullets lead their target. Contested in the recon (§6) and irrelevant here: the SAM
--     fires a homing Missile projectile, which re-solves the intercept every tick and never leads.
--   * The Kinzhal. Different criterion, different scenario — test-sam-vs-kinzhal.
--
-- ONE HAZARD RULED OUT RATHER THAN GUARDED AGAINST. MissileSpawnerMaster invalidates the slave
-- entry at launch (se.Actor = null, MissileSpawnerMaster.cs:126-129) and both slave-disposal loops
-- skip invalid entries (BaseSpawnerMaster.cs:217, :286). So an in-flight IskanderMissile is NOT
-- killed if its launcher dies or evacuates, and a disappearing missile cannot be blamed on the
-- launcher going away. Do not add a launcher-liveness guard on the assumption that it can.
--
-- EXPECTED GEOMETRY, derived in map.yaml and restated so a failure is readable without it:
-- launcher 8,17 · SAM 48,10 · victim 48,17. Ring entry at x=13.7, ~34 cells of exposure,
-- ~89-98 ticks against a 23-28 tick acquire-aim-fire chain.

local SamPrereqs = { "supplyroute", "techlevel.medium" }
local MissileType = "iskandermissile"
local TargetX, TargetY = 48, 17

-- The missile must die at least this far short of the aim point for the run to read as an
-- interception rather than an arrival. IskanderExplosion's widest warhead is ShockwaveDamage
-- MaxRadius 4c0, so anything at or inside 4 cells could have killed the Abrams by splash and is
-- not a clean read either way. 6 gives that a margin and is still small against the ~34 cells of
-- ring the missile has to survive.
local MinShortfall = 6

local ObserveTicks = 700   -- 80 pre-launch + ~156 flight + ample slack, at Timestep 60.
local FireAtTick = 20      -- let the world settle before ordering; the SAM needs no warm-up.

local tick = 0
local USA, Russia
local prereqOk = false
local ordered = false
local firstSeenTick = nil
local firstCell = nil
local lastCell = nil
local lastSeenTick = nil
local goneTick = nil
local everMoved = false
local finished = false

local function cellDist(ax, ay, bx, by)
	local dx = ax - bx
	local dy = ay - by
	return math.floor(math.sqrt(dx * dx + dy * dy) + 0.5)
end

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

local function pollTick()
	tick = tick + 1

	if tick == FireAtTick and not ordered then
		Launcher.Attack(Victim, true, false)
		ordered = true
		return
	end

	local missiles = Russia.GetActorsByType(MissileType)
	if #missiles > 0 then
		local c = missiles[1].Location
		if firstCell == nil then
			firstCell = { X = c.X, Y = c.Y }
			firstSeenTick = tick
		end

		if lastCell ~= nil and (lastCell.X ~= c.X or lastCell.Y ~= c.Y) then
			everMoved = true
		end

		lastCell = { X = c.X, Y = c.Y }
		lastSeenTick = tick
	elseif firstSeenTick ~= nil and goneTick == nil then
		-- The missile existed and no longer does: intercepted, or arrived.
		goneTick = tick
	end
end

local function finish()
	local lastX = lastCell ~= nil and lastCell.X or -1
	local lastY = lastCell ~= nil and lastCell.Y or -1
	local shortfall = lastCell ~= nil and cellDist(lastX, lastY, TargetX, TargetY) or -1
	local victimState = Victim.IsDead and "DEAD" or (Victim.Health .. "hp")
	local samState = Defender.IsDead and "DEAD" or (Defender.Health .. "hp")

	local summary = "prereqs=" .. tostring(prereqOk)
		.. " | missile first=" .. (firstCell ~= nil and (firstCell.X .. "," .. firstCell.Y) or "none")
		.. "@t" .. n(firstSeenTick)
		.. " last=" .. (lastCell ~= nil and (lastX .. "," .. lastY) or "none") .. "@t" .. n(lastSeenTick)
		.. " gone@t" .. n(goneTick) .. " moved=" .. tostring(everMoved)
		.. " | aim=" .. TargetX .. "," .. TargetY .. " shortfall=" .. shortfall .. "c"
		.. " (needs >= " .. MinShortfall .. ")"
		.. " | victim " .. victimState .. " sam " .. samState
		.. " | observed=" .. tick .. "t"

	-- 0. AVAILABILITY. The prerequisite flip itself, and the only check here that a map-placed SAM
	-- cannot fake. `supplyroute` comes from the SR's bare ProvidesPrerequisite@BuildingName
	-- (structures.yaml:372) resolving to the lowercased actor name; `techlevel.medium` comes from
	-- ProvidesTechPrerequisite@Medium (player.yaml:446-449), always granted because
	-- TechLevelDropdownVisible is false and MapOptions defaults TechLevel to `unrestricted`.
	if not prereqOk then
		Test.Fail("USA cannot satisfy the SAM's shipped Buildable prerequisites ("
			.. table.concat(SamPrereqs, ", ") .. "), so no player could build one and the"
			.. " interception below — if it happened at all — happened with a structure that is"
			.. " not obtainable in a real match. Either the prerequisite was reverted to"
			.. " ~disabled, or a token it names stopped being provided. || " .. summary)
		return
	end

	-- 1. Did the launcher fire? An Iskander that never launched measures nothing.
	if firstSeenTick == nil then
		Test.Fail("the iskander never put an " .. MissileType .. " into the world after an Attack"
			.. " order at tick " .. FireAtTick .. ". The armament fires IskanderTargeter, a dummy"
			.. " InstantHit; the payload is MissileSpawnerMaster spawning the actor"
			.. " (vehicles-russia.yaml:1101). No actor means the launch path broke, not the"
			.. " interception. || " .. summary)
		return
	end

	-- 2. Did it actually fly? IskanderMissile sits erect ON THE LAUNCHER for 80 ticks
	-- (LaunchRiseTicks 60 + PostErectionWaitTicks 20, BallisticMissile.cs:85) before the motor
	-- lights. A missile that appeared and never moved was destroyed on the rail 40 cells from the
	-- SAM — outside its 35c0 ring — which is a different event from an interception.
	if not everMoved then
		Test.Fail("the " .. MissileType .. " appeared but never left its launch cell, so nothing"
			.. " was intercepted in flight. It spends 80 ticks erect on the launcher before the"
			.. " motor lights; dying inside that window is not this test's subject. || " .. summary)
		return
	end

	-- 3. THE VERDICT PROPER. Did it stop short of where it was aimed?
	if goneTick == nil then
		Test.Fail("the " .. MissileType .. " was still in the world after " .. tick .. " ticks."
			.. " Expected ~156 ticks of flight over 40 cells plus 80 of pre-launch. Either the"
			.. " flight stalled or the observation window is too short. || " .. summary)
		return
	end

	if shortfall < MinShortfall then
		Test.Fail("the missile reached " .. shortfall .. " cells of its aim point (needs >= "
			.. MinShortfall .. " to read as an interception), so the SAM did not stop it. Every"
			.. " gate except availability was already open before this change: check that the SAM"
			.. " still has line of sight, that ^AutoTargetAirICBM still lists ICBM"
			.. " (defaults.yaml:747-750), and that SurfaceToAirMissile still lists ICBM in BOTH"
			.. " its ValidTargets and its Warhead@Spread. || " .. summary)
		return
	end

	-- 4. The corroborating read. With a ~62,800-damage warhead against a 28,000hp Abrams, arrival
	-- means death — so a live victim and a short-stopping missile are the same fact seen twice. If
	-- these two ever disagree the geometry moved and the run is unreadable.
	if Victim.IsDead then
		Test.Fail("contradictory result: the missile vanished " .. shortfall .. " cells short of"
			.. " the aim point, yet the Abrams is dead. The intercept fireball should not reach it"
			.. " at that distance (ShockwaveDamage MaxRadius 4c0). The geometry in map.yaml no"
			.. " longer matches the warhead and this run cannot be trusted either way."
			.. " || " .. summary)
		return
	end

	Test.Pass("the SAM is buildable and shot the Iskander missile down " .. shortfall
		.. " cells short of its aim point; the Abrams survived. || " .. summary)
end

local function step()
	pollTick()

	if finished then
		return
	end

	if goneTick ~= nil then
		-- A few settling ticks after the missile leaves the world, so Victim.IsDead has resolved
		-- through the frame-end task that applies the warhead.
		if tick < goneTick + 3 then
			return
		end

		finished = true
		finish()
		return
	end

	if tick >= ObserveTicks then
		finished = true
		finish()
	end
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	Russia = Player.GetPlayer("Russia")

	prereqOk = USA.HasPrerequisites(SamPrereqs)

	Trigger.OnTick(step)
end
