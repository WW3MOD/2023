-- SHARED BODY for the DEFCON 2 hold-fire garrison pair:
--
--     tools/autotest/scenarios/test-defcon2-holdfire-garrison            (Escalation, opens at 2 -- PASS)
--     tools/autotest/scenarios/test-defcon2-holdfire-garrison-skirmish   (Skirmish             -- FAIL)
--
-- READ SITE 5 of 6 -- GarrisonManager.ScanForTarget. It is the garrison's SECOND, INDEPENDENT
-- scanner: it picks its own target per port and AttackGarrisoned fires at PortState.CurrentTarget
-- directly, so none of AutoTarget's guards can see it and no unit-vs-unit scenario can reach it. All
-- four autonomous garrison paths -- the empty-port deploy, PromoteFromShelter, UpdatePortTarget's
-- re-target and TriggerAmbushDeploy -- reach a target only through that one method.
--
-- THE SHAPE OF THE RUN.
--
--   SETTLE     baselines and the setup controls. Baselines are taken BEFORE the riflemen are even
--              clicked into the house, so a garrison that starts shooting during the walk-in is
--              caught too rather than silently becoming the new zero.
--   LOADING    four riflemen walk in under a real EnterTransport click. The house flips to Russia on
--              the first entry (DynamicOwnership).
--   HOLD       HOLD_TICKS in which NO port may be manned and nothing may fire. An empty port
--              deploys only when ScanForTarget returns a valid target and the same target survives
--              TargetConfirmTicks (10) across TargetScanInterval (8) -- so ~30 ticks is all the
--              control arm needs, and 300 is ten times that.
--   ORDER      the house is CLICKED onto the humvee. AttackGarrisoned forwards RequestedTarget to
--              GarrisonManager.SetForceTarget, which deploys a shelter soldier to every port whose
--              arc contains the target and marks it PlayerOverride -- the path that deliberately
--              returns before ScanForTarget is ever called. A port must man and a rifle must fire.
--
-- THE OBSERVABLE THAT MATTERS MOST IS "IS A PORT MANNED", not damage. GarrisonManager.DeployToPort
-- does SetPosition(soldier, self.Location), so a manned port is a state the scenario can read
-- directly through Test.IsAtGarrisonPort, it is upstream of every shot, and it is the specific thing
-- ScanForTarget gates. Ammunition and the humvee's HP are carried alongside it as independent
-- confirmations, not as the primary.
--
-- WHY THE LEVEL IS READ AND NOT ASSERTED TO BE 2: this body runs in both arms. Only 2 (the hold) and
-- 0 (Skirmish) are acceptable openings and the level must stay where it opened. 3 is the CEASE-FIRE
-- rung, where a different rule silences ordered fire as well and phase 1 would pass for the wrong
-- reason; 1 is open war.

local TicksPerSecond = TestHarness.TicksPerSecond

local HOLD_FIRE_LEVEL = 2
local SKIRMISH_LEVEL = 0

local GARRISON_SIZE = 4     -- well under Cargo MaxWeight 10: slack, and no entry congestion
local SETTLE_TICKS = 15
local LOAD_TICKS = 400      -- ~3 cells at Mobile Speed 25 is ~125 ticks; this is threefold margin
local HOLD_TICKS = 300      -- ten times the ~30 ticks an autonomous deploy needs
local ORDER_TICKS = 250     -- SetForceTarget deploys on the tick it runs; this is the rifle's turn

local DEADLINE_TICKS = 1500
local DEADLINE_SECONDS = DEADLINE_TICKS / TicksPerSecond

local HOUSE_W, HOUSE_H = 2, 2   -- v01 is 2x2; Location is its top-left cell

WorldLoaded = function()
	local Garrison = { Man1, Man2, Man3, Man4 }

	TestHarness.FocusBetween(House, Probe)
	TestHarness.Select(Probe)

	local ticks = 0
	local phase = "settle"

	local openingLevel = nil
	local ammo0 = {}
	local probeHp0 = 0
	local effects0 = 0
	local holdStart = 0
	local orderIssuedTick = 0

	local function Ammo(a)
		return a.AmmoCount("primary-ammo")
	end

	-- Ask the TRAITS, not the actor flags, and mind the branch ORDER. `Actor.IsDead` is
	-- `Disposed || health.IsDead` (Actor.cs:76), so a man in a Cargo hold is alive and reads
	-- IsDead == false; `IsInWorld` IS false for a passenger. A PORT soldier, though, is in-world
	-- and not loaded, so he satisfies the "outside" test too and has to be claimed before it.
	local function Tally()
		local loaded, ports, outside, dead = 0, 0, 0, 0
		for _, s in ipairs(Garrison) do
			if s.IsDead then dead = dead + 1
			elseif Test.IsLoadedInto(s, House) then loaded = loaded + 1
			elseif Test.IsAtGarrisonPort(s, House) then ports = ports + 1
			elseif s.IsInWorld then outside = outside + 1 end
		end

		return loaded, ports, outside, dead
	end

	local function AmmoTrace()
		local parts = {}
		for i, s in ipairs(Garrison) do
			if s.IsDead then
				parts[i] = "dead"
			else
				parts[i] = ammo0[i] .. "->" .. Ammo(s)
			end
		end

		return table.concat(parts, " ")
	end

	-- Live, never interpolated into the AssertWithin timeout argument: that string is built once at
	-- registration and would report tick-0 values forever (AUTOTEST.md, "The failure message is
	-- evaluated EAGERLY"). Called from inside the predicate and from the timeout FUNCTION.
	local function Census()
		local loaded, ports, outside, dead = Tally()
		return string.format(
			"t=%d phase=%s defcon=%d(opened %s) | house %d,%d owner %s hp %d/%d | men: %d shelter "
			.. "%d ports %d outside %d dead | ammo %s | probe hp %d/%d act %s | impacts %d->%d",
			ticks, phase, Test.DefconLevel(), tostring(openingLevel),
			House.Location.X, House.Location.Y, House.Owner.Name, House.Health, House.MaxHealth,
			loaded, ports, outside, dead, AmmoTrace(),
			Probe.IsDead and 0 or Probe.Health, Probe.MaxHealth,
			Probe.IsDead and "(dead)" or Test.ActivityChain(Probe),
			effects0, Test.GetImpactEffectCount())
	end

	-- nil while nothing has fired, else a "fail: ..." string naming the first observable that moved.
	local function ShotFired(who)
		local _, ports = Tally()
		if ports > 0 then
			return "fail: " .. who .. " -- " .. ports .. " rifleman/men manned a firing port with no "
				.. "order given. A port is manned only through GarrisonManager.ScanForTarget "
				.. "returning a valid target, which is read site 5 and which the hold is supposed to "
				.. "refuse. " .. Census()
		end

		for i, s in ipairs(Garrison) do
			if not s.IsDead and Ammo(s) < ammo0[i] then
				return "fail: " .. who .. " -- rifleman " .. i .. " fired: primary-ammo "
					.. ammo0[i] .. " -> " .. Ammo(s) .. ". " .. Census()
			end
		end

		if not Probe.IsDead and Probe.Health < probeHp0 then
			return "fail: " .. who .. " -- the humvee took damage, so something on this map fired at "
				.. "it. It is the only USA actor in the garrison's envelope and it is held at "
				.. "HoldFire, so the garrison is the only candidate. " .. Census()
		end

		if Test.GetImpactEffectCount() > effects0 then
			return "fail: " .. who .. " -- a warhead detonated somewhere on the map. "
				.. "Test.GetImpactEffectCount rose " .. effects0 .. " -> "
				.. Test.GetImpactEffectCount() .. ". " .. Census()
		end

		return nil
	end

	TestHarness.AssertWithin(DEADLINE_SECONDS, function()
		ticks = ticks + 1

		if Probe.IsDead then
			return "fail: the humvee died. Its HP is raised fiftyfold in rules.yaml precisely so it "
				.. "cannot, and a death here also drops DEFCON 2 to 1 and un-holds every gun on the "
				.. "map. " .. Census()
		end

		-- ========== SETTLE ==========
		if phase == "settle" then
			if ticks < SETTLE_TICKS then
				return false
			end

			local level = Test.DefconLevel()
			if level ~= HOLD_FIRE_LEVEL and level ~= SKIRMISH_LEVEL then
				return string.format(
					"fail: SETUP -- the match opened at DEFCON %d. Only %d (the hold-fire rung, this "
					.. "directory) and %d (Skirmish, the -skirmish twin) are arms of this pair. 3 is "
					.. "the cease-fire rung, where a different rule silences ordered fire as well.",
					level, HOLD_FIRE_LEVEL, SKIRMISH_LEVEL)
			end

			openingLevel = level

			if House.Owner.Name ~= "Neutral" then
				return "fail: SETUP -- the house is owned by " .. House.Owner.Name .. " at tick "
					.. ticks .. ". GarrisonManager claims a building only while it is Neutral, so "
					.. "nothing can garrison it from any other start state. " .. Census()
			end

			local dx = House.Location.X - Probe.Location.X
			local dy = House.Location.Y - Probe.Location.Y
			local sep = math.sqrt(dx * dx + dy * dy)
			if sep > 9 then
				return string.format(
					"fail: SETUP -- the humvee is %.1f cells from the house. An EMPTY port derives "
					.. "its search radius from the SHELTER soldiers' armaments and 5.56mm.E3 is "
					.. "Range: 10c0, so a probe outside that envelope is one ScanForTarget would "
					.. "refuse anyway and a silent garrison would prove nothing. %s", sep, Census())
			end

			probeHp0 = Probe.Health
			effects0 = Test.GetImpactEffectCount()

			local refused = 0
			for i, s in ipairs(Garrison) do
				ammo0[i] = Ammo(s)
				if ammo0[i] <= 0 then
					return "fail: SETUP -- rifleman " .. i .. " opened with no ammunition, so an "
						.. "unchanged ammo count proves nothing about him. " .. Census()
				end

				-- STAGING GOES THROUGH THE CLICK. MobileProperties.EnterTransport queues a
				-- RideTransport activity directly; Test.ClickOrder issues a real EnterTransport
				-- order through Passenger.ResolveOrder, which is what a player's click does. The
				-- returned order string is a free setup assertion.
				if Test.ClickOrder(s, House) ~= "EnterTransport" then
					refused = refused + 1
				end
			end

			if refused > 0 then
				return "fail: SETUP -- " .. refused .. " of " .. GARRISON_SIZE .. " riflemen were "
					.. "not even OFFERED an EnterTransport order on a neutral house, so nothing "
					.. "could be staged. This is the targeter refusing, not the garrison -- look at "
					.. "EnterAlliedActorTargeter before reading it as a garrison finding. " .. Census()
			end

			print("[defcon2-garrison] settled, riflemen clicked in. " .. Census())
			phase = "loading"
			return false
		end

		if Test.DefconLevel() ~= openingLevel then
			return string.format(
				"fail: DEFCON moved %d -> %d during the run, so the phase the rest of this scenario "
				.. "asserts about is no longer the phase it is running in. %s",
				openingLevel, Test.DefconLevel(), Census())
		end

		-- The zero-fire assertion runs from SETTLE onward, through the walk-in as well as the hold
		-- window: a garrison that opens up while the last man is still outside has broken the same
		-- rule, and taking baselines after loading would have made that the new zero.
		if phase ~= "order" then
			local fired = ShotFired(phase == "loading" and "WALK-IN" or "HOLD WINDOW")
			if fired ~= nil then
				return fired
			end
		end

		-- ========== LOADING ==========
		if phase == "loading" then
			local loaded, _, outside, dead = Tally()

			if dead > 0 then
				return "fail: " .. dead .. " rifleman/men died on the way in. Nothing on this map "
					.. "should be able to do that. " .. Census()
			end

			if loaded == GARRISON_SIZE and House.Owner.Name == "Russia" then
				-- The building must be a live targeting pair with the probe, or "it did not shoot"
				-- is worth nothing. GetTargetOrder walks the same IIssueOrder/IOrderTargeter chain
				-- the mouse cursor walks and issues nothing.
				local offered = Test.GetTargetOrder(House, Probe)
				if offered ~= "Attack" then
					return "fail: SETUP -- a right-click from the garrisoned house onto the humvee "
						.. "resolves to " .. tostring(offered) .. ", not Attack. The two are not a "
						.. "live targeting pair, so neither the hold window nor phase 2 means "
						.. "anything. " .. Census()
				end

				holdStart = ticks
				print("[defcon2-garrison] garrison formed. " .. Census())
				phase = "hold"
				return false
			end

			if ticks >= SETTLE_TICKS + LOAD_TICKS then
				return "fail: STAGING -- only " .. loaded .. " of " .. GARRISON_SIZE .. " riflemen "
					.. "reached the shelter in " .. LOAD_TICKS .. " ticks (" .. outside
					.. " still outside) or the house never flipped to Russia. Every man was offered "
					.. "and issued an EnterTransport click at settle. " .. Census()
			end

			return false
		end

		-- ========== HOLD ==========
		if phase == "hold" then
			local loaded = Tally()
			if loaded ~= GARRISON_SIZE then
				return "fail: HOLD WINDOW -- the shelter lost a man mid-window (" .. loaded .. " of "
					.. GARRISON_SIZE .. "), so the garrison under test is no longer the one that was "
					.. "formed. " .. Census()
			end

			if ticks % 50 == 0 then
				print("[defcon2-garrison] hold. " .. Census())
			end

			if ticks < holdStart + HOLD_TICKS then
				return false
			end

			-- THE ORDER. Through the real click resolver rather than through House.Attack, so the
			-- returned OrderString is itself an assertion and a refusal is named rather than silent.
			local issued = Test.ClickOrder(House, Probe)
			if issued ~= "Attack" then
				return "fail: the click from the house onto the humvee produced " .. tostring(issued)
					.. " rather than Attack. DEFCON 2 is the rung that PERMITS an ordered shot; only "
					.. "the DEFCON 3 cease-fire refuses the order itself. " .. Census()
			end

			orderIssuedTick = ticks
			print("[defcon2-garrison] hold complete, house clicked onto the humvee. " .. Census())
			phase = "order"
			return false
		end

		-- ========== ORDER: the garrison must man a port and fire ==========
		local _, ports = Tally()
		local firedCount = 0
		for i, s in ipairs(Garrison) do
			if not s.IsDead and Ammo(s) < ammo0[i] then
				firedCount = firedCount + 1
			end
		end

		if ports > 0 and (firedCount > 0 or Probe.Health < probeHp0) then
			print("[defcon2-garrison] PASS. " .. Census())
			return true
		end

		if ticks >= orderIssuedTick + ORDER_TICKS then
			return string.format(
				"fail: the ORDERED attack produced nothing in %d ticks: %d port(s) manned, %d "
				.. "rifleman/men having fired, humvee at %d/%d. SetForceTarget deploys a shelter "
				.. "soldier to every port whose arc contains the target ON THE TICK IT RUNS, and "
				.. "that path deliberately returns before ScanForTarget is consulted -- so if this "
				.. "is the failure, the hold is over-broad and is refusing fire a player asked for, "
				.. "which is a worse defect than the one this scenario was written for. %s",
				ORDER_TICKS, ports, firedCount, Probe.Health, Probe.MaxHealth, Census())
		end

		return false
	end, function()
		return "fail: the predicate never reached a verdict inside its backstop deadline, which means "
			.. "it stopped advancing rather than that any phase overran -- every phase owns its own "
			.. "budget and its own reason. " .. Census()
	end)
end
