-- ASSERTING AUTOTEST — the Kinzhal is shot AT and gets through anyway.
--
-- WHAT IS UNDER TEST. On 2026-09-05 KinzhalMissile's Targetable@Ground/@Airborne went from
-- `TargetTypes: Hypersonic` to `ICBM`, on the user's ruling that the Kinzhal "should be almost
-- impossible to catch, but it could be possible to try to shoot at it, just that the interceptor
-- mostly misses". This run checks that BOTH halves of that sentence are true at once.
--
-- THE ACCEPTANCE TARGET IS A BAND WITH TWO OPPOSITE FAILURE MODES. Zero launches fails just as
-- surely as a kill:
--   * SAM never fires   -> FAIL. Silence reads to a player as a broken defence, not an outmatched
--                          one. It is the failure mode the recon predicts for the CRAM (§3.3) and
--                          the reason the CRAM was deliberately left `~disabled`.
--   * Kinzhal destroyed -> FAIL. The strike is meant to land.
--   * Fires and misses  -> PASS. That is the whole design.
--
-- HOW "DID IT FIRE" IS OBSERVED, because the obvious routes do not work. Interceptors are
-- projectiles and IProjectile : IEffect (WeaponInfo.cs:71), so they never appear in
-- GetActorsByType. AttackTurreted engages through AttackFollow.RequestedTarget in Tick and queues
-- no activity, so Test.ActivityChain reads "(idle)" on a firing SAM and
-- TestHarness.HoldsAttackActivity cannot see it either. The scenario's rules.yaml therefore hangs
-- a GrantConditionOnAttack probe on the SAM: it fires from INotifyAttack.Attacking, so the
-- condition means an interceptor actually left the rail. It latches (RevokeDelay 9000) so a
-- once-per-tick poller cannot miss a single-tick event.
--
-- WHY THIS SITING IS THE DEFENCE'S ABSOLUTE CEILING, and why a never-fired verdict is decisive:
-- the SAM sits ON the flight line at mid-map, so the missile is inside its 35c0 ring from its first
-- tick in the world until impact: the WHOLE FLIGHT, ~29 ticks, against a 20-28 tick acquire-aim-fire
-- chain. That is 1-9 ticks of live fire and no siting on this map yields more, so if it cannot fire
-- HERE it cannot fire anywhere. (The recon's "~12 ticks in the best siting" assumed a 36-tick ring
-- transit needing a full 70-cell diameter; a 64-cell playfield cannot supply it.)
--
-- WHAT THIS DELIBERATELY DOES NOT MEASURE:
--   * The GBU-57 and the tac nuke. Still `Penetrator` and `Hypersonic` on purpose — separate
--     design decisions, and the user ruled only on the Kinzhal. Recon §3.4.
--   * Hit PROBABILITY. One run is one sample of a system with real RNG (Inaccuracy 400). The
--     recon's criterion is "the Kinzhal reaches its aim point in at least 8 of 10", which needs
--     ten runs. A single green here is consistent with the design, not proof of the rate.
--   * The SAM against the SLOW munitions — that is test-sam-intercepts-iskander, where the same
--     interceptor is expected to connect reliably.

local OrderKey = "KinzhalStrike"
local TargetX, TargetY = 62, 17
local MissileType = "kinzhalmissile"
local FiredCondition = "sam-fired"

local ObserveTicks = 500   -- 150-tick MissileDelay + ~30 of flight + generous slack.

local tick = 0
local Russia, USA
local orderStatus = "never-called"
local orderTick = nil
local firstSeenTick = nil
local firstCell = nil
local lastCell = nil
local goneTick = nil
local samFiredTick = nil
local impactTick = nil
local finished = false

local function n(v)
	if v == nil then
		return "none"
	end

	return tostring(v)
end

local function pollTick()
	tick = tick + 1

	-- Retry until the power reports ready rather than assuming it is armed on tick 1:
	-- SupportPowerInstance.Active is only set inside Tick(), and the TechTree pass that satisfies
	-- `Prerequisites: player.russia` runs on its own schedule. The last status string is kept
	-- either way, so "never fired" always says WHY.
	if orderTick == nil then
		orderStatus = Test.ActivateSupportPower(Russia, OrderKey, CPos.New(TargetX, TargetY))
		if orderStatus == "issued" then
			orderTick = tick
		end

		return
	end

	if samFiredTick == nil and not Defender.IsDead
		and Test.ConditionCount(Defender, FiredCondition) > 0 then
		samFiredTick = tick
	end

	local missiles = Russia.GetActorsByType(MissileType)
	if #missiles > 0 then
		local c = missiles[1].Location
		if firstCell == nil then
			firstCell = { X = c.X, Y = c.Y }
			firstSeenTick = tick
		end

		lastCell = { X = c.X, Y = c.Y }
	elseif firstSeenTick ~= nil and goneTick == nil then
		goneTick = tick
	end

	if impactTick == nil and Victim.IsDead then
		impactTick = tick
	end
end

local function finish()
	local victimState = Victim.IsDead and "DEAD" or (Victim.Health .. "hp")
	local samState = Defender.IsDead and "DEAD" or (Defender.Health .. "hp")
	local flight = (firstSeenTick ~= nil and goneTick ~= nil) and (goneTick - firstSeenTick) or -1

	local summary = "order=" .. orderStatus .. "@t" .. n(orderTick)
		.. " | missile first=" .. (firstCell ~= nil and (firstCell.X .. "," .. firstCell.Y) or "none")
		.. "@t" .. n(firstSeenTick)
		.. " last=" .. (lastCell ~= nil and (lastCell.X .. "," .. lastCell.Y) or "none")
		.. " gone@t" .. n(goneTick) .. " flight=" .. flight .. "t"
		.. " | sam fired@t" .. n(samFiredTick)
		.. " | aim=" .. TargetX .. "," .. TargetY
		.. " victim " .. victimState .. "@t" .. n(impactTick)
		.. " sam " .. samState
		.. " | observed=" .. tick .. "t"

	-- 1. Did the order path reach the trait at all? Every non-issued status names its own cause:
	-- not-ready:<n>, unknown-power:<key> (have: ...), no-manager.
	if orderStatus ~= "issued" then
		Test.Fail("the Kinzhal power never fired, so neither half of the criterion was measured."
			.. " || " .. summary)
		return
	end

	-- 2. Did a missile actor reach the world? Nothing below means anything without it.
	if firstSeenTick == nil then
		Test.Fail("the order was accepted but no " .. MissileType .. " ever entered the world, so"
			.. " there was nothing to shoot at. This is a delivery failure, not an interception"
			.. " result — test-missile-strike-power is the scenario that diagnoses it."
			.. " || " .. summary)
		return
	end

	-- 3. SETUP GUARD. If the SAM died the run is unreadable in both directions: a dead SAM cannot
	-- launch, so "never fired" would be unattributable. The geometry puts it 30 cells from the aim
	-- point precisely so the strike cannot reach it.
	if Defender.IsDead then
		Test.Fail("the SAM was destroyed during the run, so neither half of the band can be read."
			.. " It sits 30 cells from the aim point specifically so the Kinzhal warhead cannot"
			.. " reach it; if that is no longer true the geometry in map.yaml has drifted from the"
			.. " warhead radius. || " .. summary)
		return
	end

	-- 4. THE "VISIBLY TRIES" HALF. Zero launches is a real failure, not a lesser pass — see the
	-- header. First knob if this fires: an explicit AimingDelay of 5-8 on the SAM's Armament,
	-- which buys 7-10 ticks of the 20-28 tick chain and is the cheapest lever there is. Recon §3.3
	-- lists the rest in the order to reach for them. NOTE that the margin here is 1-9 ticks by
	-- construction, so this check sitting near the edge is the expected state, not a defect.
	if samFiredTick == nil then
		Test.Fail("the SAM never launched an interceptor at the Kinzhal, in the best siting"
			.. " available to it (~29 ticks of exposure — the whole flight — against a 20-28 tick"
			.. " acquire-aim-fire chain). The user's criterion is that it must visibly TRY and mostly miss; silence"
			.. " reads as a broken defence instead of an outmatched one. Check that"
			.. " KinzhalMissile still carries TargetTypes: ICBM on BOTH Targetable traits, that"
			.. " ^AutoTargetAirICBM still lists ICBM, and that SurfaceToAirMissile still lists it"
			.. " in ValidTargets. || " .. summary)
		return
	end

	-- 5. THE "ALMOST NEVER HIT" HALF. The interceptor tops out at 800 against 2000 rising to a
	-- terminal 2400, so it cannot overtake and kills only on a head-on pass. A dead Abrams is the
	-- positive observable that the strike landed: ~62,800 damage against 28,000 HP.
	if not Victim.IsDead then
		Test.Fail("the Kinzhal did not reach its aim point — the Abrams is alive at " .. victimState
			.. " and the missile left the world at " .. (lastCell ~= nil and (lastCell.X .. ","
			.. lastCell.Y) or "none") .. ". The strike is meant to land: at Speed 2000 rising to"
			.. " a terminal 2400 the interceptor is 2.5x too slow to overtake, so a kill here"
			.. " means either the Kinzhal was slowed or the interceptor was sped up."
			.. " || " .. summary)
		return
	end

	Test.Pass("the SAM launched at the Kinzhal at t" .. samFiredTick .. " and the strike landed"
		.. " anyway — tried and missed, which is the criterion. NOTE: one run is one sample; the"
		.. " recon's band is 8 of 10 strikes getting through. || " .. summary)
end

local function step()
	pollTick()

	if finished then
		return
	end

	-- Settle a few ticks past impact so the SAM-fired probe and Victim.IsDead have both resolved
	-- through their frame-end tasks before the verdict is written.
	if impactTick ~= nil then
		if tick < impactTick + 5 then
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
	Russia = Player.GetPlayer("Russia")
	USA = Player.GetPlayer("USA")

	Trigger.OnTick(step)
end
