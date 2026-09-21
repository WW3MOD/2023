-- ASSERTION SCENARIO: the negative arm. With "Pre-captured Structures" left at its shipped
-- default, every neutral capturable structure is still Neutral once the match is running.
--
-- =====================================================================================
-- WHY THIS ARM EXISTS AND WHY IT IS NOT REDUNDANT
-- =====================================================================================
-- The default of this option is OFF, and that default is load-bearing rather than cosmetic: a
-- Skirmish nobody touched must be the match it was before the option existed. A feature that
-- works when switched on and ALSO fires when switched off is not half-working, it is a silent
-- change to every existing game -- and the positive arm cannot see that, because it turns the
-- option on. This is the only place the shipped default is observed.
--
-- The map geometry is identical to test-precaptured-structures (three derricks at 545%, 3.6%
-- and 458% margins from two hand-placed Supply Routes), so the two runs differ in exactly one
-- input: whether rules.yaml carries the option. That is what makes the pair evidence about the
-- option rather than about the map.
--
-- Note what this does NOT assert. `PreCapturedStructures.WorldLoaded` returns before touching
-- the actor list, the rules or the map when the option is off, and nothing observable from Lua
-- can tell "returned early" from "ran and decided nobody was near enough". Three Neutral
-- derricks at 545% margins rules out the second reading on this map -- the trait cannot both
-- have run and left a derrick 7.5 cells from a Supply Route alone -- so this is a real
-- observation of the early return, just an indirect one.
--
-- =====================================================================================
-- HOW THIS ARM GOES VACUOUS
-- =====================================================================================
-- If the actors fail to resolve, or the SRs are missing, everything is trivially Neutral and
-- the run is green for the wrong reason. Both Supply Routes are therefore asserted present and
-- correctly owned BEFORE the verdict is read: without them there are no anchors, so "nothing
-- was captured" would be true of a broken map as well as of a working default.

local Grace = 30
local Ticks = 0

WorldLoaded = function()
	local usa = Player.GetPlayer("USA")
	local russia = Player.GetPlayer("Russia")

	print("[precaptured-off] USA=" .. tostring(usa ~= nil) .. " Russia=" .. tostring(russia ~= nil))

	if usa == nil or russia == nil then
		Test.Fail("SETUP: could not resolve players USA / Russia. See the sibling scenario's guard for " ..
			"the mechanism: an empty lobby slot yields no Player object, so only ONE side of this map " ..
			"may carry `Playable: True`. Russia must stay a map player")
		return
	end

	if NearUSA == nil or Middle == nil or NearRussia == nil then
		Test.Fail("SETUP: map actors NearUSA/Middle/NearRussia did not all resolve")
		return
	end

	if UsaSR == nil or RussiaSR == nil then
		Test.Fail("SETUP: the two SUPPLYROUTE actors did not resolve. Without anchors every " ..
			"derrick stays Neutral no matter what the option says, and this arm would be vacuous")
		return
	end

	TestHarness.FocusBetween(NearUSA, NearRussia)

	TestHarness.AssertWithin(30, function()
		Ticks = Ticks + 1
		if Ticks < Grace then return false end

		if UsaSR.IsDead or RussiaSR.IsDead then
			return "fail: SETUP -- a Supply Route died, so the anchors this arm depends on are gone"
		end

		if UsaSR.Owner.InternalName ~= "USA" or RussiaSR.Owner.InternalName ~= "Russia" then
			return "fail: SETUP -- the Supply Routes are owned by '" .. UsaSR.Owner.InternalName ..
				"' and '" .. RussiaSR.Owner.InternalName .. "', not USA and Russia. Both anchors must " ..
				"be real and correctly owned or 'nothing was captured' proves nothing"
		end

		local owners = {
			{ name = "NearUSA", at = "11,15", owner = NearUSA.Owner.InternalName, margin = "545%" },
			{ name = "Middle", at = "31,15", owner = Middle.Owner.InternalName, margin = "3.6%" },
			{ name = "NearRussia", at = "51,15", owner = NearRussia.Owner.InternalName, margin = "458%" },
		}

		print(string.format("[precaptured-off] tick=%d NearUSA=%s Middle=%s NearRussia=%s",
			Ticks, owners[1].owner, owners[2].owner, owners[3].owner))

		for _, d in ipairs(owners) do
			if d.owner ~= "Neutral" then
				return "fail: with the option at its shipped default the derrick " .. d.name .. " at " ..
					d.at .. " is owned by '" .. d.owner .. "'. PreCapturedStructuresInfo.CheckboxEnabled " ..
					"must default to false: every existing Skirmish and every saved lobby preset that " ..
					"predates this option relies on it, and an ON default changes all of them at once " ..
					"without anyone choosing it. Check the field, and check that this scenario's " ..
					"rules.yaml still has no PreCapturedStructures block at all -- the absence IS the " ..
					"test, so an added `CheckboxEnabled: false` here would hide exactly this regression"
			end
		end

		return true
	end, "pre-captured default-off check never completed within 30s")
end
