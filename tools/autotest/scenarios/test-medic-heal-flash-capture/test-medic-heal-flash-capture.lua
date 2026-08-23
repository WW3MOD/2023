-- AUTO TEST: photograph the white heal flash, and confirm the frames are worth
-- looking at.
--
-- WithHealFlash is now the ONLY channel that tells a player treatment is
-- happening. The treatment pip and GrantConditionOnHealed were removed, the Heal
-- weapon's Report is commented out (weapons-other.yaml:352), and MedicVoice has
-- no heal line. Its Brightness of 1.4 (defaults.yaml:39) is a 40% multiply
-- toward white, and that file records the value has never been seen on screen.
-- This puts a frame in front of a human.
--
-- Catching it is the hard part: Count 6 at Interval 1 is six consecutive ticks,
-- ~360ms at Timestep 60. So capture is triggered by the EVENT, not a clock — the
-- tick the patient's health rises is the impact tick, which is the tick
-- FlashTarget begins tinting. Two frames are taken inside the window (capture is
-- async, so the second covers a frame of lag) and one well outside it,
-- mid-BurstWait, as a control.
--
-- Compare flash-01/flash-02 against control-01: same two men, same camera,
-- differing only by whether the tint is up. Without the control there is nothing
-- to judge "faint" or "too strong" against.
--
-- RUN THIS AT 1x. At --speed 8 the six-tick window passes in ~45ms of wall clock
-- and an async capture lands after it every time.
--
-- The assertion is deliberately weak and deliberately present: the screenshots
-- are only evidence if treatment actually occurred, so this passes once the
-- frames are away AND the patient has visibly gained health. It is a guard on
-- the capture, not a claim about the flash — no automated check here can tell
-- whether 1.4 looks right. That judgement needs eyes on the PNGs.
local PatientStartPercent = 45
local BudgetTicks = 900

local pulses = 0
local lastHealth = 0
local startHealth = 0
local pendingControl = -1
local capturesDone = 0

WorldLoaded = function()
	TestHarness.FocusBetween(Medic, Patient)

	Patient.Health = math.floor(Patient.MaxHealth * PatientStartPercent / 100)
	lastHealth = Patient.Health
	startHealth = Patient.Health

	local elapsed = 0

	TestHarness.AssertWithin(BudgetTicks / TestHarness.TicksPerSecond, function()
		elapsed = elapsed + 1

		if Medic.IsDead or Patient.IsDead then
			return "fail: an actor died before the capture completed"
		end

		-- Control frame: 25 ticks after the photographed impact is halfway
		-- through the 50-tick BurstWait, so it is guaranteed outside any flash.
		if pendingControl > 0 then
			pendingControl = pendingControl - 1
			if pendingControl == 0 then
				TestHarness.Screenshot("control-01",
					"expects: NO tint — same two men mid-gap between pulses, for comparison against flash-01")
				capturesDone = capturesDone + 1
			end
		end

		if Patient.Health > lastHealth then
			lastHealth = Patient.Health
			pulses = pulses + 1

			-- Photograph the second pulse: the first coincides with acquisition
			-- and turning, so the medic may still be settling into place.
			if pulses == 2 then
				TestHarness.Screenshot("flash-01",
					"expects: the patient lifted toward white — WithHealFlash Brightness 1.4, impact tick")
				capturesDone = capturesDone + 1

				Trigger.AfterDelay(2, function()
					TestHarness.Screenshot("flash-02",
						"expects: same tint, 2 ticks into the 6-tick window — insurance against capture lag")
				end)

				pendingControl = 25
			end
		end

		-- Both the impact frame and the control are away, and healing is
		-- demonstrably happening. The PNGs are now worth a human looking at.
		if capturesDone >= 2 and Patient.Health > startHealth then
			return true
		end

		if elapsed >= BudgetTicks then
			return "fail: capture never completed — " .. pulses .. " pulses seen, patient at "
				.. math.floor(Patient.Health * 100 / Patient.MaxHealth) .. "% (from "
				.. PatientStartPercent .. "). No treatment means no flash to photograph"
		end

		return false
	end, "heal-flash capture did not resolve within " .. BudgetTicks .. " ticks")
end
