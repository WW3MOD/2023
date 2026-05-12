-- DEMO: v2 CaptureCoordinatorBotModule visible behavior
--
-- LAYOUT
--   USA-bot (v2, america)    @ (6,16)  ← left SR
--   Russia-bot (normal, russia) @ (58,16) ← right SR
--   BIO   (150 $/tick)       @ (32,10)
--   FCOM  (100 $/tick)       @ (32,22)
--   OILB1 (50 $/tick)        @ (22,16) ← closer to v2
--   OILB2 (50 $/tick)        @ (42,16) ← closer to normal
--
-- WHAT TO WATCH
--   1. PRIORITY — v2 should target BIO (highest income) before OILB1
--      even though OILB1 is closer, because the income weight dominates
--      until distance gets large.
--   2. ESCORT — when v2 dispatches an engineer, 1-2 nearby idle infantry
--      should move along with it (attack-move). Normal AI's engineer
--      walks alone.
--   3. DEFENSE — once v2 captures, if Russia army threatens the captured
--      building, watch idle USA infantry move toward the structure.
--
-- TIP
--   Press space to pause. Camera starts in the middle for full view.

WorldLoaded = function()
	-- Frame the camera across the contested neutral capturables.
	TestHarness.FocusBetween(NeutralBio, NeutralFcom, NeutralOilb1, NeutralOilb2)
end
