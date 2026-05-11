-- DEMO: drive the four artillery units around. Watch the turret while moving
-- (locked to forward, body-aligned) vs stopped (free to swing toward the
-- decoy t90s on the right side of the map).
--
-- The decoy t90s are pinned to HoldFire so they can't kill the artillery
-- before you get to look at the turret behavior. Decoys are only there to
-- give the artillery a reason to want to aim — without them, AutoTarget
-- never even tries to rotate the turret and you can't tell the difference.

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Mlrs, Grad, Tos, Decoy2)
	TestHarness.Select(Paladin)

	Decoy1.Stance = "HoldFire"
	Decoy2.Stance = "HoldFire"
	Decoy3.Stance = "HoldFire"
end
