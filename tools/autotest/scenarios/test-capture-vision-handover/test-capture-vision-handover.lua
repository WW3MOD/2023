-- AUTO TEST: does a building's vision follow the building when it changes hands?
--
-- THE DEFECT. AffectsMapLayer (the base class under Vision/Radar/CounterBatteryRadar/
-- CreatesShroud) implements no INotifyOwnerChanged, and buildings change hands through
-- Actor.ChangeOwnerInPlaceSync, which deliberately skips World.Remove/Add. The per-player
-- vision sources live in MapLayers.sources keyed by the TRAIT INSTANCE, which an owner change
-- does not disturb, so on a stationary building none of the trait's other triggers fire:
-- AddedToWorld/RemovedFromWorld are skipped by the in-place path, the building never moves,
-- and ITick early-returns unless Range or the disabled state changed. The snapshot of "who is
-- allied to the owner" taken at AddedToWorld therefore stands for the rest of the match.
-- Consequence: the OLD owner keeps the vision and the NEW owner never gets any.
--
-- WHY THE GARRISON PATH. GarrisonManager.cs:260 flips a Neutral building to the entering
-- soldier's player through the same in-place call, and it predates the capture sites that
-- share the bug. It is also the everyday symptom rather than the edge case — "garrisoning a
-- building gives you none of its vision" is what a player would actually report, where an
-- engineer capture is a once-a-match event.
--
-- WHY NEUTRAL IS A LEGITIMATE STALE HOLDER, which is the one design choice here worth
-- defending. On unfixed code a building only ever feeds the player who owned it at
-- AddedToWorld, so the LEAK half is observable only for a WORLD-START owner — and garrison
-- can never make a player one, because GarrisonManager.cs:259 claims only from Neutral.
-- Neutral IS a world-start owner, holds a real Player.MapLayers, and the resolver does not
-- branch on player identity anywhere: MapLayers.Tick (:226-289), AddSource (:320) and
-- RemoveSource all treat every player alike. So the number Phase 2 reads off Neutral is the
-- number an enemy player would show in the same position. Demonstrating the leak against a
-- HUMAN opponent needs a capture, not a garrison, and is deliberately left to a second
-- scenario.
--
-- WHAT THE NUMBERS MEAN, and this is the question this scenario exists to settle. Fog on and
-- Explored off (rules.yaml), so Test.GetVisibility returns the raw ResolvedVisibility band:
--     0  shrouded — never seen
--     1  EXPLORED ONLY. MapLayers.cs:255-256 floors any explored cell to 1 whether or not a
--        live source sits on it, and MapLayers.cs:592-597 records that 1 therefore means
--        "seen at the weakest band" and "nobody is looking" interchangeably — it can never
--        carry detection.
--     3  LIVE vision from Vision@PROBE (Strength: 3).
-- The difference between a stale holder reading 3 and reading 1 is the difference between a
-- competitive-integrity bug (a live enemy-detecting sensor deep in territory you took) and a
-- cosmetic one (stale explored ground, which every player has everywhere anyway). Every
-- verdict below prints all four readings so the answer is legible from the log whichever way
-- the run goes.
--
-- VERDICTS:
--   PASS  — vision handed over: the captor gained LIVE vision and the previous owner dropped
--           to explored-only, and the non-building owner-change path did not throw.
--   FAIL  — one of those limbs broke. The message names which and prints every reading.
--   SKIP  — the scenario never built the world it describes (geometry wrong, garrison never
--           formed, soldier left in the world). Always a setup or harness fault, never a
--           finding about vision. Read the message and fix the scenario.

-- The cell the whole scenario is about: ~2 cells north of the Fort's footprint, inside
-- Vision@PROBE's 4c0 and outside every other source on the map. Chosen so that it stays
-- inside the radius under EITHER building-centre convention (with or without a 2x2
-- LocalCenterOffset the distance is ~2.3-2.6 cells against a 4.0 cell range).
local ProbeCell = CPos.New(31, 14)

-- Sentry's own cell, for the Phase 3 guard limb.
local SentryCell = CPos.New(10, 27)

local SettleSeconds = 2     -- s for a frame-end owner change plus the MapLayers resolver pass
local GarrisonWithin = 25   -- s for one rifleman to walk ~6 cells and claim the church
local FlipWithin = 5        -- s for a ChangeOwner frame-end task to land

local Live = 2              -- ResolvedVisibility >= 2 is live vision; 1 is merely explored

local usa, russia, neutral

local function Vis(player, cell)
	if player == nil then
		return -1
	end

	return Test.GetVisibility(player, cell)
end

-- Every verdict carries every reading, so the log answers the live-vs-explored question even
-- on a run that fails for some other reason.
local function Cell(c)
	return "(" .. c.X .. "," .. c.Y .. ")"
end

local function Readings()
	return "[probe " .. Cell(ProbeCell) .. ": USA=" .. Vis(usa, ProbeCell) ..
		" Neutral=" .. Vis(neutral, ProbeCell) ..
		"] [sentry " .. Cell(SentryCell) .. ": USA=" .. Vis(usa, SentryCell) ..
		" Russia=" .. Vis(russia, SentryCell) ..
		"] [Fort owner " .. (Fort.Owner ~= nil and Fort.Owner.InternalName or "<none>") ..
		", Trooper in world " .. tostring(Trooper.IsInWorld) .. "]"
end

-- Poll until `predicate` holds. Deliberately NOT TestHarness.AssertWithin: that calls
-- Test.Pass() the moment its predicate is true, which would end the run at the first phase.
-- Only the final assertion may use AssertWithin.
local function WaitUntil(seconds, predicate, onReady, onTimeout)
	local remaining = math.floor(seconds * TestHarness.TicksPerSecond)
	local check
	check = function()
		if predicate() then
			onReady()
			return
		end

		remaining = remaining - 1
		if remaining <= 0 then
			onTimeout()
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

local function After(seconds, fn)
	Trigger.AfterDelay(math.floor(seconds * TestHarness.TicksPerSecond), fn)
end

-- PHASE 3 — the crash guard. Sentry is an ordinary in-world rifleman, so his owner change goes
-- through Actor.ChangeOwnerSync (GeneralProperties.cs:63 -> Actor.ChangeOwner), which removes
-- him from the world, fires INotifyOwnerChanged, and only then adds him back. The handler added
-- to AffectsMapLayer must early-return on !IsInWorld there: without that guard it would
-- AddSource into the emptied dictionary and the following AddedToWorld would add again WITHOUT
-- removing first (AffectsMapLayer.cs:188 vs UpdateCells' :167), hitting the duplicate-key throw
-- at MapLayers.cs:323. That is every infantry and vehicle in the game, so an unguarded build
-- does not fail this phase — it dies here with an unhandled InvalidOperationException.
--
-- RevealsMap (RevealsShroud.cs:54) implements the same interface with NO such guard and is
-- correct to, because it has no INotifyAddedToWorld/RemovedFromWorld and manages its cells via
-- TraitEnabled/TraitDisabled. Copying it into AffectsMapLayer is the way this ships broken,
-- which is why the guard has a test rather than a comment.
local function GuardLimb()
	local beforeUsa = Vis(usa, SentryCell)
	if beforeUsa < Live then
		Test.Skip("Sentry's owner never had live vision of his own cell before the flip (USA=" ..
			beforeUsa .. "), so the non-building owner-change path could not be exercised and the " ..
			"IsInWorld guard was not tested. Check that E1's Vision@SENTRY survived the rules " ..
			"override in rules.yaml. " .. Readings())
		return
	end

	Sentry.Owner = russia

	WaitUntil(FlipWithin,
		function() return Sentry.Owner ~= nil and Sentry.Owner.InternalName == "Russia" end,
		function()
			After(SettleSeconds, function()
				local afterRussia = Vis(russia, SentryCell)
				local afterUsa = Vis(usa, SentryCell)

				if afterRussia < Live then
					Test.Fail("a rifleman changed owner and his new owner got no vision of the cell " ..
						"he is standing on (Russia=" .. afterRussia .. ", expected >= " .. Live ..
						"). This is the ChangeOwnerSync path, which rebuilds vision through " ..
						"World.Remove/Add and was correct before this fix — so the IsInWorld guard in " ..
						"AffectsMapLayer.OnOwnerChanged is most likely suppressing a rebuild it should " ..
						"not, or firing when the actor is back in the world. " .. Readings())
					return
				end

				if afterUsa >= Live then
					Test.Fail("a rifleman changed owner but his PREVIOUS owner still has live vision " ..
						"of the cell he stands on (USA=" .. afterUsa .. ", expected <= 1 i.e. explored " ..
						"only). The mobile-actor path leaks the same way the building path does, which " ..
						"would mean World.Remove is no longer clearing the source. " .. Readings())
					return
				end

				Test.Pass()
			end)
		end,
		function()
			Test.Skip("Sentry never registered the forced owner change within " .. FlipWithin ..
				"s; ChangeOwner runs as a frame-end task, so either it was rejected or the poll reads " ..
				"a stale value. The building limbs above already passed. " .. Readings())
		end)
end

-- PHASE 2 — the measurement. Both halves matter and they are separate assertions: a fix could
-- plausibly deliver the hand-over without the removal, or the reverse.
local function Measure()
	-- A soldier deployed to a firing port is IN WORLD, standing at the building's Location,
	-- where his own 2c0 bubble could reach the probe cell and light it for USA no matter what
	-- the building's traits did. Refuse to measure in that state rather than report a green
	-- that a port soldier could have produced on his own.
	if Trooper.IsInWorld then
		Test.Skip("Trooper is in the world after the flip, so he was deployed to a firing port " ..
			"rather than left in the shelter, and his own vision could be lighting the probe cell. " ..
			"The reading would not be attributable to the building. GarrisonManager only deploys to " ..
			"a port once it has a confirmed target, so check that nothing hostile came into range " ..
			"and that the HoldFire stances in rules.yaml still apply. " .. Readings())
		return
	end

	local captor = Vis(usa, ProbeCell)
	local former = Vis(neutral, ProbeCell)

	-- THE GAP HALF. The everyday symptom, in one assertion.
	if captor < Live then
		Test.Fail("the building changed hands and its new owner got NO LIVE VISION from it: USA " ..
			"resolves " .. captor .. " at the probe cell, expected >= " .. Live .. " from " ..
			"Vision@PROBE (Strength: 3). 0 means the cell is still shrouded for the captor; 1 means " ..
			"explored ground with nothing watching it (MapLayers.cs:255-256, :592-597). This is " ..
			"AffectsMapLayer carrying no INotifyOwnerChanged: the building flipped through " ..
			"ChangeOwnerInPlaceSync, which skips World.Remove/Add, so the AddedToWorld snapshot that " ..
			"decided only Neutral may see is never recomputed. " .. Readings())
		return
	end

	-- THE LEAK HALF, and the reading that settles what the leak actually IS. `former` is the
	-- number: >= 2 says the previous owner kept a LIVE sensor (competitive integrity), 1 says
	-- it kept explored ground only (cosmetic).
	if former >= Live then
		Test.Fail("the building changed hands but its PREVIOUS owner still has LIVE vision through " ..
			"it: Neutral resolves " .. former .. " at the probe cell, expected <= 1 (explored only). " ..
			"This is the leak half of the same defect — MapLayers.sources is keyed by the trait " ..
			"instance and nothing removed the old entry, so the per-cell counters were never " ..
			"decremented (MapLayers.cs:398-430 is reached only from RemoveCellsFromPlayerMapLayer). " ..
			"A reading of " .. former .. " rather than 1 means the stale source is resolving ABOVE " ..
			"the explored floor, i.e. the old owner keeps a real sensor and not merely remembered " ..
			"terrain. " .. Readings())
		return
	end

	GuardLimb()
end

-- PHASE 1 — the flip, driven by the real garrison gesture.
--
-- PITFALL, and it is the reason this scenario does not use the idiom the neighbouring garrison
-- scenario uses: `Fort.Owner = usa` WOULD NOT TEST THIS BUG. The Lua Owner setter calls
-- Actor.ChangeOwner (GeneralProperties.cs:63), which routes to ChangeOwnerSync — the
-- World.Remove/Add path — and that hands vision over correctly with or without the fix. Only a
-- call that reaches ChangeOwnerInPlaceSync exhibits the defect, and the shipped routes to it
-- are GarrisonManager.cs:260/:324/:329, CaptureActor.cs:141, ProximityCapturable.cs:225 and
-- ProximityCapturableBase.cs:191. A scenario written with the setter would go green against
-- the live bug.
local function Garrison()
	Trooper.EnterTransport(Fort)

	WaitUntil(GarrisonWithin,
		function() return Fort.Owner ~= nil and Fort.Owner.InternalName == "USA" end,
		function() After(SettleSeconds, Measure) end,
		function()
			Test.Skip("the Fort never became USA-owned within " .. GarrisonWithin ..
				"s, so the ownership flip under test never happened and nothing about vision was " ..
				"measured. Either Trooper could not path to the church, or DynamicOwnership stopped " ..
				"claiming neutral buildings on entry (GarrisonManager.cs:256-261). " .. Readings())
		end)
end

-- PHASE 0 — prove the world is the world this scenario describes, BEFORE anything is claimed.
-- Both limbs of the real measurement are readings of a single cell, so if the geometry is off
-- by a cell or some other source lights that cell, every later assertion is meaningless. These
-- are Skips, not Fails: a broken baseline is a scenario fault, never a finding.
local function Baseline()
	local ownerSees = Vis(neutral, ProbeCell)
	local captorSees = Vis(usa, ProbeCell)

	if ownerSees < Live then
		Test.Skip("the Fort's own owner does not have live vision of the probe cell at start " ..
			"(Neutral=" .. ownerSees .. ", expected >= " .. Live .. "). The probe cell is outside " ..
			"Vision@PROBE's 4c0 radius, or the rules override did not attach. Move the probe cell " ..
			"closer to the Fort at 30,16 — nothing about the hand-over was measured. " .. Readings())
		return
	end

	if captorSees > 0 then
		Test.Skip("the probe cell is ALREADY visible to USA before the flip (USA=" .. captorSees ..
			", expected 0), so a post-flip reading could not be attributed to the building. Some " ..
			"other USA source reaches it — check that the Vision strips in rules.yaml applied to E1 " ..
			"and that no unit drifted north of the Fort. " .. Readings())
		return
	end

	Garrison()
end

WorldLoaded = function()
	usa = Player.GetPlayer("USA")
	russia = Player.GetPlayer("Russia")
	neutral = Player.GetPlayer("Neutral")

	if usa == nil or russia == nil or neutral == nil then
		Test.Skip("one of the three players named in map.yaml does not exist (USA=" ..
			tostring(usa ~= nil) .. " Russia=" .. tostring(russia ~= nil) .. " Neutral=" ..
			tostring(neutral ~= nil) .. "), so no reading could be taken.")
		return
	end

	TestHarness.FocusBetween(Fort, Trooper)

	-- Let the first MapLayers resolver pass run before reading any baseline: AddSource only
	-- marks cells touched, and ResolvedVisibility is written in MapLayers.Tick.
	After(SettleSeconds, Baseline)
end
