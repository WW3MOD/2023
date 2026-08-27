-- AUTO TEST: a dry unit that has already asked once, and been told "no", must ask AGAIN.
--
-- THE CLAIM. AmmoPool is not ITick. Its two dispatch sites are INotifyAttack.Attacking — which
-- cannot fire with an empty magazine — and INotifyBecomingIdle, which Actor.Tick:317-323 raises
-- only on the !wasIdle -> IsIdle TRANSITION. A unit that goes dry, falls idle, and then simply
-- stands there therefore asks for resupply exactly once and never re-evaluates. The one thing that
-- rescues a soldier from that is AutoSeekSupplies.ReturnWhenEmpty (infantry.yaml:251-253), whose
-- ITick arm re-asks every EmptyScanInterval. This scenario is that claim, staged: the first ask is
-- deliberately made to fail, and the run turns entirely on whether a second one ever happens.
--
-- WHY A LOGISTICS CENTRE AND NOT A TRUCK OR A CRATE. Only the Centre isolates the ITick arm. The
-- trait's OTHER periodic path, INotifyIdle, refuses any provider that declares a DockedCondition
-- (AutoSeekSupplies.CanServe) and the Centre declares unit.docked — so with a truck or a crate on
-- the map both arms could dispatch and a green would not say which one did. It is also the only
-- rearm host vehicles name at all, which is why this scenario is the one worth extending.
--
-- WHAT MAKES THIS FAILABLE. See rules.yaml: uncommenting ReturnWhenEmpty: false leaves every other
-- mechanism in place and must still produce a man at zero ammunition at the deadline.
--
-- THE TRAP THIS SCENARIO IS SHAPED AROUND. The Centre hands infantry TWO 4-cell grants of
-- replenish-soldiers: the supply-gated SupplyProvider aura arm, and a plain
-- ProximityExternalCondition that carries NO supply gate at all and enables the soldier's own free
-- ReloadAmmoPool trickle. Inside four cells a rifleman refills beside a completely empty depot.
-- The ten-cell separation in map.yaml exists to keep that out of the measurement; do not close it.

-- Ticks, not harness-seconds, so the wait is immune to TestHarness.TicksPerSecond (25) disagreeing
-- with the mod's real 16.67. Long enough that the man has had ~12 re-ask opportunities at
-- EmptyScanInterval 25 and been refused every one of them.
local RefillTicks = 300

-- Harness-seconds (x1.5 real). Covers the 300-tick dry window, one re-ask cadence, and a ten-cell
-- walk at roughly 41 ticks per cell, with room to spare: the trip is made promptly or not at all.
local DeadlineSeconds = 60

local FullLoad = 2250
local AmmoPoolName = "primary-ammo"

local refilled = false
local pollCount = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Hunter, Depot)
	TestHarness.Select(Hunter)

	-- Drained before the first tick, so the single dispatch AmmoPool gets from the becoming-idle
	-- transition finds no stocked host and leaves him standing. ChooseResupplier filters on
	-- CurrentSupply > 0, so zero is genuinely "no candidate", not "a poor candidate".
	Test.SetSupply(Depot, 0)

	-- Assert the setup rather than trusting it, in both directions: a depot that did not drain and
	-- a rifleman who did not start dry each produce a confident, meaningless verdict.
	local load = Test.GetSupply(Depot)
	if load ~= 0 then
		Test.Fail(string.format(
			"setup failed: the depot still holds %d supply, so the first ask would have succeeded and " ..
			"nothing about a RETRY would be measured", load))
		return
	end

	local startingAmmo = Hunter.AmmoCount(AmmoPoolName)
	if startingAmmo ~= 0 then
		Test.Fail(string.format(
			"setup failed: the rifleman starts with %d rounds, so he is not dry and never asks at all",
			startingAmmo))
		return
	end

	Trigger.AfterDelay(RefillTicks, function()
		Test.SetSupply(Depot, FullLoad)
		refilled = true
		print(string.format("[retry-test] refilled depot to %d at the %d-tick mark; ammo was %d",
			Test.GetSupply(Depot), RefillTicks, Hunter.AmmoCount(AmmoPoolName)))
	end)

	TestHarness.AssertWithin(DeadlineSeconds, function()
		if Hunter.IsDead then return "fail: the rifleman died before the depot was refilled" end
		if Depot.IsDead then return "fail: the depot died or despawned" end

		local ammo = Hunter.AmmoCount(AmmoPoolName)
		local supply = Test.GetSupply(Depot)

		pollCount = pollCount + 1
		if pollCount % 50 == 0 then
			-- Live numbers go here and NOT into the failure string, which Lua evaluates eagerly at
			-- registration and which would therefore report the starting values forever.
			print(string.format("[retry-test] poll=%d refilled=%s ammo=%d supply=%d",
				pollCount, tostring(refilled), ammo, supply))
		end

		if not refilled then
			-- The control half, checked live rather than assumed. Either of these means the man was
			-- served by something other than a dispatch to a stocked depot -- the free proximity
			-- trickle, most likely -- and the green half would then prove nothing.
			if ammo > 0 then
				return "fail: the rifleman rearmed while the depot held zero supply, so something " ..
					"other than the seek is feeding him and this scenario measures nothing"
			end

			if supply ~= 0 then
				return "fail: the depot regained supply on its own before the scheduled refill"
			end

			return false
		end

		-- Nothing has issued him an order at any point, and at ten cells he is outside every aura
		-- the Centre projects. Ammunition arriving can only mean he was dispatched and walked.
		return ammo > 0
	end, "The rifleman never rearmed after the depot was refilled: he asked once while it was empty, " ..
		"was refused, and never asked again. That is AutoSeekSupplies.ReturnWhenEmpty's ITick re-ask " ..
		"failing to fire -- with it off, this is exactly what the RED control in rules.yaml produces.")
end
