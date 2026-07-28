-- AUTO TEST (PIPELINE item 8, Stage 3 — OBS-D coverage debt): the DETECTION spring (trigger 1).
--
-- Stage-3's spring table fires on the FIRST of five triggers (AmbushTactics.EvaluateSpring), and trigger 1
-- "Detected" has the top precedence: the instant a group member becomes visible to the target's owner the
-- ambush must commit its alpha strike rather than wait for a nicer score (the AT-suppression trap). The
-- shipped test-ambush-{convoy,enemy-stops,fast-convoy} scenarios all exercise the SCORE triggers (3/4) and
-- deliberately keep the ambusher UNSEEN, so the detection path has no scenario. This closes that gap.
--
-- Isolation — trigger 1 is the only trigger that can fire here:
--   2 Damaged   : the detector is forced to HoldFire, so it never shoots the ambusher.
--   3 / 4 Score : AmbushMin/HighSpringThreshold are set unreachably high in rules.yaml, so no kill-zone
--                 score can ever satisfy the degrading / saturation triggers.
--   5 Overrun   : the detector's advance stops ~12c out, far outside the 3c stand-off, so it never overruns.
-- With 2-5 impossible, a spring can ONLY be a detection spring. Geometry (rules.yaml): the ambusher's
-- Detectable.Vision is 5, so it is detected only when the enemy closes inside 19c (^StandardVision strength
-- >= 5). The detector spawns 22c away (strength 4 ⇒ UNSEEN ⇒ the ambusher HOLDS FIRE), then drives inward
-- across the 19c band, where detection flips true and the ambush springs. "Held fire before detection" is
-- therefore built into the setup (undetected until 19c) and is proven complementarily by the RED run below.
--
-- RED baseline for the manager: comment out the single Detector.Move line marked GREEN below. The detector
-- then stays parked at 22c, never detected, and — with triggers 2-5 all impossible — the ambush never
-- springs and the test times out. (NOTE: the gate is intentionally NOT the RED lever here. Unlike the score
-- triggers, the detection spring also exists on the ungated stock path (AutoTarget.cs: `if (isSpotted ...`),
-- so gate-off would STILL spring on detection and could not serve as a timeout baseline. The detector's
-- advance is the lever that toggles the mechanism under test.)

local Deadline = 20

WorldLoaded = function()
	TestHarness.FocusBetween(Ambusher, Detector)
	TestHarness.Select(Ambusher)

	Ambusher.Stance = "Ambush"
	Ambusher.GrantCondition("enable-ambush-tactics")   -- opt-in seam: ExternalCondition@ambushtactics

	-- The detector reveals the ambusher (vision) but must never fire on it, so trigger 2 (Damaged) can
	-- never preempt the detection spring.
	Detector.Stance = "HoldFire"

	local startAmmo = Ambusher.AmmoCount("primary-ammo")

	-- GREEN: drive the detector from its 22c spawn to ~12c out. It crosses the 19c detection band en route,
	-- at which point the ambusher becomes detected and springs. Stop point (12c) stays well outside the 3c
	-- overrun stand-off. Comment THIS line out for the RED baseline (detector holds at 22c ⇒ never detected).
	Detector.Move(CPos.New(20, 22))

	-- Deadline (20s) is comfortably longer than the few seconds the detector needs to cross into the 19c
	-- band, so "fired within deadline" means "sprang once detected".
	TestHarness.AssertWithin(Deadline, function()
		if Ambusher.IsDead then return "fail: ambusher died before springing" end
		return Ambusher.AmmoCount("primary-ammo") < startAmmo
	end, "AT ambush did not spring within " .. Deadline .. "s of the detector entering detection range")
end
