-- TEST: Artillery turret rotation
--
-- Goal: confirm a Paladin (m109) facing East rotates its turret to face
-- a target placed 7 cells South before firing — not firing locked-forward.
--
-- Bug under test: "Recent change forced artillery to stop before turning the
-- turret toward the enemy and firing. Paladin observed firing with turret
-- locked in forward position when it should have turned." (RELEASE_V1.md, 260508)
--
-- Player observes the Paladin's first firing cycle:
--   PASS (F1) — turret rotated South before muzzle flash
--   FAIL (F2) — turret stayed pointing East / fired locked-forward
--   SKIP (F3) — couldn't tell / repro failed (didn't fire)

WorldLoaded = function()
	Camera.Position = WPos.New(1024 * 33, 1024 * 20, 0)
	Media.DisplayMessage("Watch the Paladin's turret rotate from East to South before firing.", "Test")
	Media.DisplayMessage("F1 = Pass · F2 = Fail · F3 = Skip", "Controls")
end
