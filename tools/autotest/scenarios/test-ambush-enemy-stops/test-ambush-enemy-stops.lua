-- AUTO TEST (PIPELINE item 8, Stage 3): the "enemy stops in the kill-zone" degenerate case (design §3.6).
-- Two t90 drive into the ambush kill-zone and HALT. The worthwhile score never decreases, so the
-- best-strike-degrading trigger (3) can never fire — but the score stays above HighSpringThreshold and the
-- saturation trigger (4) springs the ambush after it is sustained. Proves the state machine does not stall
-- forever waiting for a departure that never comes.
--
-- RED baseline for the manager: comment out GrantCondition below. With the gate off (stock ambush) the
-- parked-but-undetected tanks never trigger a spring and the test times out.

local Deadline = 20

WorldLoaded = function()
	TestHarness.FocusBetween(Ambusher, Tank1)
	TestHarness.Select(Ambusher)

	Ambusher.Stance = "Ambush"
	Ambusher.GrantCondition("enable-ambush-tactics")

	local startAmmo = Ambusher.AmmoCount("primary-ammo")

	-- Drive both tanks to a stop just inside the kill-zone (~8-9 cells from the ambush) and leave them.
	Tank1.Move(CPos.New(30, 14))
	Tank2.Move(CPos.New(34, 14))

	TestHarness.AssertWithin(Deadline, function()
		if Ambusher.IsDead then return "fail: ambusher died before springing" end
		return Ambusher.AmmoCount("primary-ammo") < startAmmo
	end, "AT ambush did not spring on the halted tanks within " .. Deadline .. "s")
end
