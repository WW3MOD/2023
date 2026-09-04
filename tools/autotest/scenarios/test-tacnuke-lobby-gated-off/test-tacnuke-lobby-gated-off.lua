-- ASSERTING AUTOTEST — at the shipped default, is the tactical nuke really not there?
--
-- WHAT IS UNDER TEST: that a game nobody configured does not hand its players a nuclear weapon.
-- The proposal ships this power lobby-gated and OFF (§9.4), which is a claim about DEFAULT state —
-- the sort of claim that is true the day it is written and quietly false after a renamed option id,
-- an inverted GrantWhenOptionDisabled, or a PauseOnCondition swapped in for a RequiresCondition.
-- None of those produce an error anywhere.
--
-- WHY THIS IS NOT A TIMEOUT TEST. "The nuke never fired" would pass on a broken mod, a missing
-- SupportPowerManager, or a Player actor that never loaded player.yaml. Two things make it a
-- positive reading instead:
--
--   1. Test.GetSupportPowerState reads SupportPowerInstance.Disabled — the exact predicate
--      SupportPowersWidget filters its icon list on (SupportPowersWidget.cs:136) — and returns
--      'hidden' rather than 'not-ready'. That distinction matters because a disabled power is STILL
--      a key in SupportPowerManager.Powers: ActorAdded registers every SupportPower trait on the
--      actor, disabled ones included (SupportPowerManager.cs:58-74), so ActivateSupportPower cannot
--      tell "the host switched this off" from "this is still charging".
--      It returns ONE BARE TOKEN. The bin listing comes from Test.GetSupportPowerBin, separately.
--      The first version of the binding appended " (bin: ...)" to every state, which made the
--      comparison on line ~100 below unsatisfiable and failed this scenario at 0d0dcfbb while the
--      SHIPPED BEHAVIOUR WAS ALREADY CORRECT. Do not merge the two readings again.
--   2. THE KINZHAL IS THE POSITIVE CONTROL, read on the same player in the same tick. It is not
--      lobby-gated, so it must be present. One power visible and the other not is a claim about the
--      GATE; two powers invisible is a claim about the mod being broken, and the verdict says which.
--
-- AND THE ORDER PATH IS EXERCISED TOO. Reading the bin proves the icon is not drawn; issuing the
-- order proves the power cannot be fired anyway by something that bypasses the UI. Both are checked,
-- because a hidden-but-fireable power would be worse than a visible one.
--
-- IF THIS GOES RED, THE FIX IS NEVER TO RELAX IT. Re-read the polarity note on
-- GrantConditionOnLobbyOption@tacnuke in player.yaml first: the trait falls back to
-- OptionOrDefault(Option, !GrantWhenOptionDisabled), so only the "grant a DISABLING condition when
-- the option is off" form survives the option not being registered at all.

local NukeKey = "TacNukeStrike"
local ControlKey = "KinzhalStrike"
local TargetX, TargetY = 40, 17

-- Long enough that a slow TechTree pass cannot be mistaken for a closed gate. The Kinzhal control
-- needs its `Prerequisites: player.russia` satisfied, and that runs on its own schedule — so the
-- run watches until the CONTROL comes up, then reads both, rather than sampling one early tick.
local ObserveTicks = 200

local tick = 0
local Russia
local nukeState = "never-read"
local controlState = "never-read"
local bin = "never-read"
local nukeOrderStatus = "never-called"
local controlReadyTick = nil
local finished = false

-- "Would the bin draw this?" spelled once. Test.GetSupportPowerState returns a bare token, and the
-- two drawn states are `ready` and `charging:<n>` — the second carries a value, so it is matched by
-- prefix. Every other token ('hidden', 'absent', 'no-manager') means no icon.
local function isDrawn(state)
	return state == "ready" or string.sub(state, 1, 9) == "charging:"
end

local function pollTick()
	tick = tick + 1

	nukeState = Test.GetSupportPowerState(Russia, NukeKey)
	controlState = Test.GetSupportPowerState(Russia, ControlKey)
	bin = Test.GetSupportPowerBin(Russia)

	-- The control coming up is the signal that the support power system has finished initialising,
	-- so both readings are taken against a settled world rather than a cold one. `charging:<n>` is
	-- the expected reading for the Kinzhal here: nothing in this scenario overrides its shipped
	-- 3000-tick interval, and it must NOT be overridden — the control has to be observed exactly as
	-- it ships. Charging is a live icon; hidden is not.
	if controlReadyTick == nil and isDrawn(controlState) then
		controlReadyTick = tick
	end
end

local function finish()
	-- Issued last, once, so the verdict can also report what the order path does. Expected
	-- 'not-ready:<n>' — the power exists as a key but never charges while its trait is disabled,
	-- because SupportPowerInstance.Tick resets remainingSubTicks and returns early
	-- (SupportPowerManager.cs:196-201).
	nukeOrderStatus = Test.ActivateSupportPower(Russia, NukeKey, CPos.New(TargetX, TargetY))

	local summary = "lobby=DEFAULT(no override) | nuke '" .. NukeKey .. "' state=" .. nukeState
		.. " order=" .. nukeOrderStatus
		.. " | control '" .. ControlKey .. "' state=" .. controlState
		.. " live@t" .. (controlReadyTick ~= nil and tostring(controlReadyTick) or "never")
		.. " | bin=[" .. bin .. "]"
		.. " | observed=" .. tick .. "t"

	-- 1. THE CONTROL, FIRST. Without it a broken mod passes this scenario.
	if controlReadyTick == nil then
		Test.Fail("the Kinzhal control never appeared in the power bin either (state '"
			.. controlState .. "'), so this run proves NOTHING about the nuke's lobby gate — a mod"
			.. " in which no power works at all would report the nuke as absent too. Fix the control"
			.. " before reading the assertion below. || " .. summary)
		return
	end

	-- 2. THE ASSERTION. 'hidden' is the expected reading: the trait is registered but disabled, so
	-- SupportPowersWidget does not draw it. 'absent' would also mean no icon, but it means the trait
	-- is not on the Player actor at all, which is a different (and wrong) way to be off — the power
	-- is meant to be one tickbox away, not deleted.
	if nukeState ~= "hidden" then
		Test.Fail("the tactical nuclear strike is not gated off at the shipped default: state '"
			.. nukeState .. "', where 'hidden' was required. A 'ready' or 'charging' reading means"
			.. " every unconfigured game now ships a nuke — check the option id 'tactical-nuke'"
			.. " matches on both sides, that PowersLobbyOptions.TacticalNukeCheckboxEnabled is still"
			.. " false, and above all that GrantConditionOnLobbyOption@tacnuke still reads"
			.. " `GrantWhenOptionDisabled: true` (that polarity is what makes an UNREGISTERED option"
			.. " fail safe; inverting it makes an absent option enable the power). 'absent' means the"
			.. " trait is missing from the Player actor entirely, which is off for the wrong reason."
			.. " || " .. summary)
		return
	end

	-- 3. And it cannot be fired past the UI either.
	if nukeOrderStatus == "issued" then
		Test.Fail("the nuke's icon is hidden but the ORDER still went through. A power that is"
			.. " invisible and fireable is worse than a visible one: SupportPowerInstance.Activate"
			.. " gates on Ready (SupportPowerManager.cs:245), so this means Disabled and Ready"
			.. " disagree. || " .. summary)
		return
	end

	Test.Pass("tactical nuke is hidden at the shipped default while the Kinzhal control is live. || "
		.. summary)
end

local function step()
	pollTick()

	if not finished and (controlReadyTick ~= nil or tick >= ObserveTicks) then
		finished = true
		finish()
		return
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	Russia = Player.GetPlayer("Russia")
	if Russia == nil then
		Test.Fail("Russia player not found")
		return
	end

	TestHarness.FocusBetween(OwnSR, Bystander)
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
