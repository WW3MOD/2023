-- AUTO TEST: Kill() still kills an indestructible garrison.
--
-- Rationale, the Vaporizable spin and why a PBOX is the subject are all in map.yaml. This file stages
-- three riflemen into the pillbox, proves it is genuinely garrisoned and at full health, then calls
-- Kill() on it and requires it to die.
--
-- THE ASSERTION IS IsDead, NOT HP, and that distinction is the whole regression. With the floor
-- wrongly honoured on a Kill the pillbox sits at 1 HP: a test watching for "HP fell" would see it
-- fall from full to 1 and call that success, while the actor is still alive, still shooting and
-- still being Kill()ed every tick by Vaporizable. Only IsDead separates "reduced to rubble" from
-- "destroyed", which is exactly the pair this floor is meant to keep apart.
--
-- EXPECTED RED if IDamageFloor is ever applied to an ignoreModifiers hit:
-- "the garrisoned pillbox survived a direct Kill() ... 1/HP, still alive". Note the HP in that
-- verdict: 1, not full. That is the signature -- the Kill was received and then floored, rather than
-- refused outright.

local GARRISON_SIZE = 3        -- PBOX Cargo MaxWeight is 4
local GARRISON_TICKS = 350
local DEATH_SECONDS = 5        -- Kill is synchronous; this is slack, not a budget

Garrison = {}

local function Tally()
	local loaded, ports, outside, dead = 0, 0, 0, 0
	for _, s in ipairs(Garrison) do
		if s.IsDead then dead = dead + 1
		elseif Test.IsLoadedInto(s, Box) then loaded = loaded + 1
		elseif Test.IsAtGarrisonPort(s, Box) then ports = ports + 1
		elseif s.IsInWorld then outside = outside + 1 end
	end

	return loaded, ports, outside, dead
end

local function Census()
	local loaded, ports, outside, dead = Tally()
	if Box.IsDead then
		return string.format("pillbox DEAD | of %d men: %d in shelter, %d at ports, %d outside, %d dead",
			GARRISON_SIZE, loaded, ports, outside, dead)
	end

	return string.format(
		"pillbox %d/%d HP, still alive, owner %s | of %d men: %d in shelter, %d at ports, %d outside, %d dead",
		Box.Health, Box.MaxHealth, Box.Owner.Name, GARRISON_SIZE, loaded, ports, outside, dead)
end

WorldLoaded = function()
	local Russia = Player.GetPlayer("Russia")

	for i = 1, GARRISON_SIZE do
		Garrison[i] = Actor.Create("e1", true, {
			Owner = Russia,
			Location = CPos.New(42 + (i - 1), 31),
		})
	end

	-- Staging through the click rather than Mobile — see test-human-autoacquires-garrisoned-house.
	local refused = 0
	for _, s in ipairs(Garrison) do
		if Test.ClickOrder(s, Box) ~= "EnterTransport" then refused = refused + 1 end
	end

	if refused > 0 then
		Test.Fail(string.format(
			"%d of %d riflemen were not offered an EnterTransport order on a neutral pillbox, so "
			.. "nothing could be staged.", refused, GARRISON_SIZE))
		return
	end

	TestHarness.FocusBetween(Box)
	TestHarness.Select(Box)

	Trigger.AfterDelay(GARRISON_TICKS, function()
		local loaded, ports = Tally()

		-- THE PRECONDITION IS THE POINT: an EMPTY pillbox would die on Kill whatever the floor does,
		-- because GarrisonManager's floor is unconditional but the interesting state is the loaded
		-- one that a player sees. Measure that it really is garrisoned before killing it.
		if Box.Owner.Name ~= "Russia" then
			Test.Fail("the pillbox never changed hands, so it is not a hostile garrison and the "
				.. "`loaded` condition may not be granted either -- " .. Census())
			return
		end

		if ports > 0 then
			Test.Fail(string.format(
				"%d man/men manned a firing port. rules.yaml holds PBOX's AutoTarget at HoldFire and "
				.. "there is no enemy on this map at all, so if this fires the stance gate is not "
				.. "what it reads as -- %s", ports, Census()))
			return
		end

		if loaded ~= GARRISON_SIZE then
			Test.Fail(string.format(
				"only %d of %d men reached the shelter -- staging, not the subject. %s",
				loaded, GARRISON_SIZE, Census()))
			return
		end

		if Box.Health ~= Box.MaxHealth then
			Test.Fail("the pillbox was damaged before the Kill, so a later death could be the damage "
				.. "rather than the Kill -- nothing on this map should be shooting. " .. Census())
			return
		end

		if Box.IsDead then
			Test.Fail("the pillbox was already dead before the Kill -- " .. Census())
			return
		end

		-- THE GESTURE UNDER TEST.
		Box.Kill()

		TestHarness.AssertWithin(DEATH_SECONDS, function()
			if Box.IsDead then
				return true
			end

			return false
		end, function()
			return string.format(
				"the garrisoned pillbox survived a direct Kill(). %s | Health.Kill is "
				.. "InflictDamage(MaxHP, ignoreModifiers: true), and IDamageFloor must not apply to "
				.. "it: the IDamageModifier the floor replaced lived inside "
				.. "`if (!ignoreModifiers && damage.Value > 0)` and never saw a Kill. An HP of 1 above "
				.. "means the Kill WAS received and then floored -- the actor is now unkillable, "
				.. "INotifyKilled will never fire, and Vaporizable.ITick will call Kill on it every "
				.. "tick for the rest of the match behind a self.IsDead guard it can no longer satisfy.",
				Census())
		end)
	end)
end
