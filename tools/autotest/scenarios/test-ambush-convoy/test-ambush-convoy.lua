-- AUTO TEST (PIPELINE item 8, Stage 3): a stationary AT-specialist ambush in Ambush stance, opted into
-- the widened-ambush gate, must SPRING on a convoy passing through its kill-zone BEFORE the column clears
-- — driven by the worthwhile-score saturation trigger (4), not by being spotted.
--
-- Setup: Ambusher (at.america, USA, Ambush stance, gate granted) at (32,22). A 4×t90 column starts at the
-- left and is Move-ordered straight across a lane at y=14, ~8 cells above the ambush. ATGM range is 20c so
-- the column is well within reach; the kill-zone is widened to 12c (rules.yaml) so the tanks score while
-- passing. The convoy is high-value, so the score sits above HighSpringThreshold and saturates within two
-- 25-tick samples → the ambush springs.
--
-- RED baseline for the manager: comment out the GrantCondition line below (gate off ⇒ stock ambush). The
-- ambusher should then NOT fire while the column passes undetected (ammo stays 3) and the test times out.
-- Unseeability is enforced by the Detectable.Vision: 9 override in rules.yaml (visible only inside 4c) —
-- stock detectability had the t90s spotting and killing the ambusher at the 8c gap (RED 260725). This also
-- guarantees GREEN cannot pass via the detection trigger (review OBS-1): only the score triggers can spring.

local Deadline = 20

WorldLoaded = function()
	TestHarness.FocusBetween(Ambusher, Lead)
	TestHarness.Select(Ambusher)

	Ambusher.Stance = "Ambush"
	Ambusher.GrantCondition("enable-ambush-tactics")   -- opt-in seam: ExternalCondition@ambushtactics

	local startAmmo = Ambusher.AmmoCount("primary-ammo")

	-- The convoy is the ENEMY driving through — a plain Move across the map.
	Lead.Move(CPos.New(60, 14))
	C2.Move(CPos.New(60, 14))
	C3.Move(CPos.New(60, 14))
	C4.Move(CPos.New(60, 14))

	-- Deadline (20s) is comfortably below the column's full transit time, so "fired within deadline"
	-- means "sprang before the column cleared".
	TestHarness.AssertWithin(Deadline, function()
		if Ambusher.IsDead then return "fail: ambusher died before springing" end
		return Ambusher.AmmoCount("primary-ammo") < startAmmo
	end, "AT ambush did not spring on the passing convoy within " .. Deadline .. "s")
end
