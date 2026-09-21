-- AUTO TEST: a garrisoned emplacement inside a nuke's vaporize radius is DESTROYED, not left alive.
--
-- Rationale, the Vaporizable spin, why a GTWR and why the witness exists are all in map.yaml. This
-- file garrisons the tower, buys and fires the high-yield strike at its cell, and reads two actors.
--
-- THE ASSERTION IS IsDead, NEVER HP, and the distinction is the whole point. Under the defect the
-- tower is at 1 HP -- it took the Kill and had it floored -- so a test watching for "HP fell" sees
-- it fall from 15000 to 1 and calls that success while the actor is alive, invisible, still
-- targetable and being Kill()ed every tick. `IsDead` is also safe to read after disposal:
-- BaseActorProperties carries [ExposedForDestroyedActors] (GeneralProperties.cs:23), unlike
-- HealthProperties, which is why every Health read below is guarded by an IsDead test first.
--
-- EXPECTED RED with the pre-72a3aba2 floor (one applied unconditionally, before
-- Health.IgnoresDamageFloor existed):
--   "the blast landed -- the witness four cells from ground zero is dead -- but the garrisoned
--    guard tower is ALIVE at 1/15000 HP."
-- The witness clause is what separates that from "the strike never arrived", which reads
--   "the strike never reached ground zero: the witness ... is still alive".

local GARRISON_SIZE = 3        -- GTWR Cargo MaxWeight is 6
local GARRISON_TICKS = 350     -- walk in; nothing is measured during it
local VERDICT_SECONDS = 60     -- MissileDelay 100 + flight + Vaporize Duration 30, with slack

local Proxy = "power.highyieldnuke"
local PowerKey = "HighYieldNukeStrike"
local GroundZero = { X = 50, Y = 30 }

Garrison = {}

local tick = 0
local fired = false
local fireTick = nil
local lastPowerStatus = "not-tried"

local function Alive(a)
	return not a.IsDead
end

local function Hp(a)
	-- HealthProperties is NOT exposed for destroyed actors, so this must never be reached for one.
	if a.IsDead then
		return "dead"
	end

	return a.Health .. "/" .. a.MaxHealth
end

local function Tally()
	local loaded, ports, outside, dead = 0, 0, 0, 0
	for _, s in ipairs(Garrison) do
		if s.IsDead then dead = dead + 1
		elseif Test.IsLoadedInto(s, Tower) then loaded = loaded + 1
		elseif Test.IsAtGarrisonPort(s, Tower) then ports = ports + 1
		elseif s.IsInWorld then outside = outside + 1 end
	end

	return loaded, ports, outside, dead
end

local function Census()
	local loaded, ports, outside, dead = Tally()
	return string.format(
		"tower %s%s | witness %s | of %d men: %d in shelter, %d at ports, %d outside, %d dead | "
		.. "t=%d, fired=%s, power=%s",
		Hp(Tower), Tower.IsDead and "" or (", owner " .. Tower.Owner.Name), Hp(Witness),
		GARRISON_SIZE, loaded, ports, outside, dead,
		tick, fireTick == nil and "no" or ("t=" .. fireTick), lastPowerStatus)
end

-- Buy a shot if the magazine is empty, then fire once. Stateless and idempotent, so it is safe to
-- call every tick; see the header of TestHarness.EnsurePower.
local function BuyAndFire()
	if fired then
		return
	end

	local USA = Player.GetPlayer("USA")
	local ready, status = TestHarness.EnsurePower(USA, Proxy, PowerKey, tick)
	lastPowerStatus = status

	if not ready then
		return
	end

	local result = Test.ActivateSupportPower(USA, PowerKey, CPos.New(GroundZero.X, GroundZero.Y))
	if result ~= "issued" then
		lastPowerStatus = "activate:" .. result
		return
	end

	fired = true
	fireTick = tick
end

WorldLoaded = function()
	local Russia = Player.GetPlayer("Russia")

	-- South of the tower, clear of the witness.
	for i = 1, GARRISON_SIZE do
		Garrison[i] = Actor.Create("e1", true, {
			Owner = Russia,
			Location = CPos.New(49 + (i - 1), 34),
		})
	end

	-- Staging through the click rather than Mobile — see test-human-autoacquires-garrisoned-house.
	local refused = 0
	for _, s in ipairs(Garrison) do
		if Test.ClickOrder(s, Tower) ~= "EnterTransport" then refused = refused + 1 end
	end

	if refused > 0 then
		Test.Fail(string.format(
			"%d of %d riflemen were not offered an EnterTransport order on a neutral guard tower, "
			.. "so nothing could be staged.", refused, GARRISON_SIZE))
		return
	end

	TestHarness.FocusBetween(Tower, Witness)

	local step
	step = function()
		tick = tick + 1

		if tick >= GARRISON_TICKS then
			BuyAndFire()
		end

		Trigger.AfterDelay(1, step)
	end

	Trigger.AfterDelay(1, step)

	Trigger.AfterDelay(GARRISON_TICKS, function()
		local loaded, ports = Tally()

		-- THE PRECONDITION. An EMPTY tower would die here whatever the floor does; the state worth
		-- testing is the garrisoned one, and it has to be proved rather than assumed.
		if Tower.IsDead then
			Test.Fail("the tower was dead before the strike was even ordered -- " .. Census())
			return
		end

		if Tower.Owner.Name ~= "Russia" then
			Test.Fail("the tower never changed hands, so it is not a garrison -- " .. Census())
			return
		end

		if ports > 0 then
			Test.Fail(string.format(
				"%d man/men manned a firing port. rules.yaml holds GTWR's AutoTarget at HoldFire and "
				.. "there is no enemy unit on this map, so if this fires the stance gate is not what "
				.. "it reads as -- %s", ports, Census()))
			return
		end

		if loaded ~= GARRISON_SIZE then
			Test.Fail(string.format(
				"only %d of %d men reached the shelter -- staging, not the subject. %s",
				loaded, GARRISON_SIZE, Census()))
			return
		end

		if not Alive(Witness) then
			Test.Fail("the witness died before the strike, so it can no longer testify that the "
				.. "blast landed -- " .. Census())
			return
		end

		TestHarness.AssertWithin(VERDICT_SECONDS, function()
			-- THE CLAIM. Both dead is the pass: the witness proves the warhead arrived and the tower
			-- proves the floor did not eat its Kill.
			if Tower.IsDead and Witness.IsDead then
				return true
			end

			-- The one ordering that would be a genuine surprise rather than a failure of the subject:
			-- the floored actor dying while the unfloored control survives.
			if Tower.IsDead and not Witness.IsDead then
				return "the tower is destroyed but the witness four cells away is untouched, which "
					.. "is backwards -- the witness has no damage floor and takes the same Vaporize "
					.. "path. Something other than the strike killed the tower. " .. Census()
			end

			return false
		end, function()
			if not Witness.IsDead then
				return string.format(
					"the strike never reached ground zero: the witness four cells away is still "
					.. "alive after %ds, so nothing can be concluded about the tower. Suspect the "
					.. "purchase or the order rather than the damage floor -- the last power status "
					.. "was '%s'. %s", VERDICT_SECONDS, lastPowerStatus, Census())
			end

			return string.format(
				"THE BLAST LANDED -- the witness four cells from ground zero is dead -- but the "
				.. "garrisoned guard tower is ALIVE at %s. That HP is the signature: the Kill WAS "
				.. "received and then floored, so IDamageFloor is being applied to an ignoreModifiers "
				.. "hit. The tower is now unkillable and invisible (Vaporizable has already faded it "
				.. "and suppressed its remains), INotifyKilled will never fire, and Vaporizable.ITick "
				.. "will call Kill on it every tick for the rest of the match behind a self.IsDead "
				.. "guard it can no longer satisfy. %s", Hp(Tower), Census())
		end)
	end)
end
