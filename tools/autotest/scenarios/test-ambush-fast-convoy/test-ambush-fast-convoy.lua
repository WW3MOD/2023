-- AUTO TEST (PIPELINE item 8, Stage 3): the "fast convoy passes before checks complete" degenerate case
-- (design §3.6). A fast btr column races across the ambush kill-zone. The state machine must still spring
-- BEFORE the column clears — via saturation at peak density (4) or, as the column starts leaving, the
-- best-strike-degrading trigger (3) whose K-tick exit PREDICTION fires without waiting for the target to
-- actually leave range. Proves a quick pass is not missed by the 25-tick sample cadence.
--
-- RED baseline for the manager: comment out GrantCondition below → stock ambush, undetected fast column
-- passes without a spring, test times out.

local Deadline = 14

WorldLoaded = function()
	TestHarness.FocusBetween(Ambusher, Lead)
	TestHarness.Select(Ambusher)

	Ambusher.Stance = "Ambush"
	Ambusher.GrantCondition("enable-ambush-tactics")

	local startAmmo = Ambusher.AmmoCount("primary-ammo")

	Lead.Move(CPos.New(60, 14))
	C2.Move(CPos.New(60, 14))
	C3.Move(CPos.New(60, 14))
	C4.Move(CPos.New(60, 14))

	TestHarness.AssertWithin(Deadline, function()
		if Ambusher.IsDead then return "fail: ambusher died before springing" end
		return Ambusher.AmmoCount("primary-ammo") < startAmmo
	end, "AT ambush did not spring on the fast convoy within " .. Deadline .. "s")
end
