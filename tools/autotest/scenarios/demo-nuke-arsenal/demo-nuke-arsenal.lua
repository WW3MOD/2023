-- DEMO -- the whole nuclear arsenal fired in ascending yield at one aim point. Nothing here is
-- asserted: no AssertWithin, no Test.Pass, no Test.Fail. It ends on Test.Skip, which is the
-- convention every captured demo in this tree uses (see demo-defcon-readout) so `run-test.sh` gets
-- a verdict file and exits instead of sitting on the watchdog. Test.Skip is neither a pass nor a
-- fail and asserts nothing.
--
-- ============================================================================================
-- WHY THIS FILE WAS RE-SCRIPTED (2026-09-21): TWO OF THE SIX COULD NOT FIRE
-- ============================================================================================
-- Since the nuclear powers were faction-tiered, USA CANNOT FIRE THE SARMAT OR THE TSAR BOMBA and
-- this demo -- the artifact used to show the nukes off -- silently skipped a third of its arsenal.
--
--   rules/player.yaml:238-239   MissileStrikePower@Sarmat     Prerequisites: powers.event, player.russia
--   rules/player.yaml:262-263   MissileStrikePower@TsarBomba  Prerequisites: powers.event, player.russia
--
-- `powers.event` is the part the sandbox checkbox supplies to everybody
-- (ProvidesPrerequisite@SandboxEvent, player.yaml:195-197, unfiltered). `player.russia` is NOT:
-- ProvidesPrerequisite@Russia carries `Factions: russia` (player.yaml:1168-1170) and nothing in the
-- sandbox block grants it. So an America seat is short one prerequisite on exactly those two.
--
-- AND THE FAILURE IS SILENT RATHER THAN LOUD, which is why it survived. The chain:
--   * SupportPowerProductionQueue filters BOTH AllItems and BuildableItems on
--     SupportPowerInstance.Purchasable (SupportPowerProductionQueue.cs:102-117), which is false
--     when the power's prerequisites are unmet -- so the cameo is ABSENT from the shop, not greyed.
--   * ProductionQueue.ResolveOrder drops a StartProduction for anything not in BuildableItems and
--     returns (ProductionQueue.cs:509-510). No error, no refund, no log line.
--   * Lua's Player.Build does not read that: it calls queue.ResolveOrder directly and returns true
--     regardless (ProductionProperties.cs:290-292). EnsurePower therefore reports 'buying' forever
--     rather than 'refused', which is the one status token that would have named the cause.
--   * The magazine stays empty, so Test.ActivateSupportPower returns 'not-ready:0' for the whole
--     ShotPatience budget and the shot is skipped.
--
-- THE FIX IS A SEAT, NOT A PREREQUISITE OVERRIDE. Each warhead is now fired from the nation that
-- owns it, which is also what the demo should have been showing all along:
--
--   1  B61-12 (0.3 kt)   USA      powers.america
--   2  B61-12 (50 kt)    USA      powers.america
--   3  W76-1 (100 kt)    USA      powers.america
--   4  RS-28 Sarmat      RUSSIA   powers.event + player.russia   <-- was unfireable
--   5  B83-1 (1.2 Mt)    USA      powers.event, no owner named   (America's bomb; see below)
--   6  Tsar Bomba        RUSSIA   powers.event + player.russia   <-- was unfireable
--
-- The B83 stays on the USA seat deliberately. `player.america` was REMOVED from it on 2026-09-20
-- (player.yaml:260-261) so that NuclearGameEnders.NamesAnOwner finds an empty owner list and
-- neither the final exchange nor the retaliation window can hand it to anybody -- it is event-tier
-- and unowned, so EITHER seat can fire it under sandbox. It is fired from USA because it is the
-- largest weapon in the US stockpile and this demo is also a tour of who holds what.
--
-- RUSSIA IS ALREADY ON THIS MAP AND IS ALREADY A COMBATANT. map.yaml seats it as a BARE MAP PLAYER
-- (no `Playable: True`), which is the shape demo-doomsday-deadhand had to be fixed INTO on
-- 2026-09-20: CreateMapPlayers builds a Player per non-playable map player unconditionally, and
-- one per lobby SLOT only when a client occupies it (CreateMapPlayers.cs:95-122). run-test.sh
-- launches ONE client, so a second `Playable: True` seat would get no Player at all. Nothing in
-- map.yaml's Players block needed to change.
--
-- CAN A SCRIPT ORDER A NON-PLAYABLE SEAT ABOUT? YES, AND BY TWO DIFFERENT ROUTES:
--   * BUYING bypasses the order path entirely -- Lua's Player.Build calls queue.ResolveOrder
--     directly (ProductionProperties.cs:290-292), so there is nothing for a validator to reject.
--   * FIRING goes through World.IssueOrder and IS validated, by ValidateOrder.OrderValidation. It
--     passes because a map player's ClientIndex is the ADMIN's: `world.LobbyInfo.Clients
--     .FirstOrDefault(c => c.IsAdmin)?.Index ?? 0` (Player.cs:222), and in a single-client autotest
--     run the one client is the admin (Server.cs:589 gives IsAdmin to the first to join). So
--     subjectClientId == clientId and the order resolves. demo-defcon-readout has fired a Russian
--     power from exactly this seat shape since 2026-09-15.
--
-- ============================================================================================
-- THE SCHEDULE IS NOW EVENT-DRIVEN, NOT A TICK TABLE
-- ============================================================================================
-- The old header carried an absolute tick table (order / impact / blast / wave-ends per shot) and
-- every number in it was stale, twice over:
--
--   * IT USED THE OLD FLIGHT LENGTH. engine b69681d2 replaced the map-edge spawn with an off-map
--     standoff: entry = aimPoint - unit(home -> salvoCentroid) * standoff, standoff = mapDiagonal +
--     ApproachMargin = 185363 + 16384 = 201747 (197.0 cells). map.yaml's own header records that
--     every tick below it moved and was never re-measured. Flight is now
--     EstimateArcTicks(standoff) = standoff / Speed for these missiles (all seven carry
--     `Acceleration: 0`, which is the branch that makes it exact -- BallisticMissileFly.cs:84-85).
--   * IT COUNTED A MissileDelay THAT IS NOT APPLIED. rules.yaml overrides MissileDelay to 60 on all
--     six, and under `powers-sandbox` -- which this scenario locks ON -- MissileStrikePower reads
--     `sandbox.SandboxRemovesLaunchDelay ? 0 : info.MissileDelay` (MissileStrikePower.cs:624-626),
--     and that field defaults TRUE (PowersLobbyOptions.cs:168). So the six overrides are INERT and
--     every impact was a further 60 ticks early.
--
-- Rather than re-derive a table that the next engine change will stale again, THE DEMO NOW WATCHES.
-- Shot N+1 is ordered a fixed settle after shot N's detonation is OBSERVED, and a detonation is
-- observed rather than predicted: the strike's missile actors are enumerated every tick with
-- Player.GetActorsByType, and the tick their count returns to zero after having been positive is
-- the tick the last warhead of that salvo hit the ground. That is the same event the fireball is,
-- read from the world instead of from arithmetic.
--
-- The settle per shot is 7 ticks per cell of blast radius -- the ratio the old table's own
-- wave-end column implies at every one of its six rows (4.5c/31t, 24.6c/172t, 31.0c/217t,
-- 60.7c/424t, 71.0c/496t, 246.1c/1722t; 7.0 at each) -- rounded up, plus a margin. The Tsar's full
-- 1722-tick wave is NOT waited out: it is the last shot, so the demo captures its fireball and ends.
--
-- ============================================================================================
-- LAYOUT, so this file can be read without map.yaml
-- ============================================================================================
-- Ground zero is cell 64,64, the exact centre of a 128x128 playfield. 142 target sites sit in
-- thirteen rings around it at 3, 7, 12, 18, 23, 28, 33, 40, 48, 57, 65, 74 and 82 cells. Each site
-- is the same mix (one structure, one vehicle, two infantry, two trees), so distance from the burst
-- is the only variable. USA's Supply Route is on the south edge at 64,118; Russia's is in the far
-- north-west at 10,10. Both survive every shot: their only target type is NoAutoTarget and no
-- arsenal warhead lists it.
--
-- THE APPROACH BEARINGS NOW DIFFER, and that is a visible consequence of the seat change rather
-- than a defect. The entry point is derived from the FIRER's home through the aim point, so shots
-- 1/2/3/5 still come due north up the centreline from 64,118, and shots 4 and 6 come in from the
-- north-west over Russia's own shoulder at 10,10. FLIGHT TIME IS UNAFFECTED: the standoff is a
-- fixed distance (the map diagonal plus ApproachMargin), not a distance to the firer.
--
-- TREES DO NOTHING AT ALL at any range -- every T##/TC0# actor carries ^TreeIndestructible
-- (DamageMultiplier: Modifier: 0, decoration.yaml:135-137). They are here for scale. Do not read
-- "the trees are still standing" as a weapon underperforming; read the structures and the infantry.
--
-- ============================================================================================
-- HOW MANY WARHEADS EACH SHOT FLIES IS MAP-DERIVED, AND TWO OF THEM ARE NOT ONE
-- ============================================================================================
-- AimPoints is deliberately ABSENT from @Sarmat, @TridentW88 and @B83 in nuclear-arsenal.yaml.
-- MissileStrikePower.AimPointsFor overrides it for any power NuclearGameEnders.Is() accepts, and
-- returns DoomsdayStrike.PackageSize -- round(playableCells / CellsPerImpact) clamped to [2,6].
-- This map's Bounds are 1,1,126,126 = 15876 playable cells, CellsPerImpact is 2400, so
-- (15876 + 1200) / 2400 = 7, clamped to 6. THE PACKAGE ON THIS MAP IS SIX.
--
--   shot 4  Sarmat    750 kt/RV, NuclearYieldTons 750000    -> game-ender -> SIX RVs
--   shot 5  B83-1     1.2 Mt,    NuclearYieldTons 1200000   -> game-ender -> SIX bombs
--   shot 6  Tsar      50 Mt,     NuclearYieldTons 50000000  -> ABOVE NuclearReleaseLadder
--                                .SandboxOnlyAboveTons (10 Mt), so NOT a game-ender, so AimPoints
--                                falls through to its YAML value: the default 1. ONE bomb.
--
-- THE B83 FIRING SIX IS NEW AND IS NOT WHAT THIS DEMO'S RING STORY ASSUMES (see ## Watch in the
-- hand-off). Test.ActivateSupportPower issues an order with no multi-aim payload, so
-- ResolveAimPoints builds the FALLBACK RING at info.AimPointRadius -- 30c0 for the Sarmat, 35c0
-- for the B83. Six 71-cell bursts on a 35-cell ring reach ~106 cells from ground zero, which is
-- past ring 82 and therefore past everything the Tsar Bomba was staged to be the first to reach.
-- The demo still fires and still observes all six; what it can no longer promise is that the LAST
-- ring standing belongs to the last weapon. The per-shot `warheads=` figure printed below is the
-- observed count, so a viewer reads the real number rather than this comment's.
--
-- ============================================================================================
-- WHAT IS WRITTEN WHERE
-- ============================================================================================
-- lua.log gets one line per event, greppable on a fixed prefix:
--   ARSENAL LAUNCH      n/6  <name>  tick=..  firer=..  order=issued
--   ARSENAL DETONATION  n/6  <name>  tick=..  warheads=..  flight=..  firer=..
--   ARSENAL NOT-FIRED / ARSENAL NOT-OBSERVED   with the diagnostic token that says why
-- SIX `ARSENAL DETONATION` lines is the demo working. Anything less names its own cause.
--
-- One screenshot per detonation, plus an opening frame and a closing one. The capture is taken a
-- short delay AFTER the detonation tick, scaled to the weapon, because the fireball needs frames to
-- reach its size -- a capture on the impact tick itself is a bright dot.
--
-- THE SIX WARHEADS ARE BOUGHT, NOT CHARGED. Every shipped support power carries
-- `RequiresPurchase: True`, which replaces the timer with a magazine: at zero banked shots the
-- power is Disabled, therefore not Ready, and Test.ActivateSupportPower returns 'not-ready:0'.
-- The chain is written out in the header of TestHarness.EnsurePower (mods/ww3mod/scripts/
-- test-helpers.lua). rules.yaml supplies 250000 credits TO EVERY PLAYER (the block is on `Player:`,
-- so Russia is funded too), ticks the sandbox lobby option that provides `powers.event`, and cuts
-- each proxy's BuildDuration to 5 ticks -- which under sandbox is itself moot, because
-- SandboxRemovesPurchaseDelay makes GetBuildTime return 0 and a purchase complete in one tick.

local GroundZero = { X = 64, Y = 64 }

-- WHO FIRES WHAT, and every field is read by the state machine below.
--   order    the power's OrderName -- SupportPowerManager keys its dictionary on this, NOT on the
--            trait name or the @suffix.
--   proxy    the buy-tab actor whose ProvidesSupportPowerCharge banks a shot on that power.
--   missile  MissileStrikePower.MissileActor, lowercase exactly as the YAML writes it. This is the
--            actor enumerated to observe the detonation.
--   seat     "USA" or "RUSSIA" -- which player holds the prerequisites. See the header.
--   blast    blast radius in cells, used ONLY to size the settle and the capture delay.
--   settle   ticks to let the blast wave finish before the next shot is ordered: ceil(7 * blast)
--            rounded up to a round number. 7 ticks per cell is the ratio the shipped wave-end
--            table implies at all six rows.
local SHOTS = {
	{
		order = "B61LowStrike", proxy = "power.b61low", missile = "b61lowmissile",
		seat = "USA", name = "B61-12 (0.3 kt)", blast = 4.5, settle = 60,
		caption = "1/6  B61-12 at 0.3 kt  -- 4.5 cell blast, USA",
	},
	{
		order = "B61MaxStrike", proxy = "power.b61max", missile = "b61maxmissile",
		seat = "USA", name = "B61-12 (50 kt)", blast = 24.6, settle = 190,
		caption = "2/6  B61-12 at 50 kt   -- 24.6 cells: same bomb, dial turned up, USA",
	},
	{
		order = "W76Strike", proxy = "power.w76", missile = "w76missile",
		seat = "USA", name = "W76-1 (100 kt)", blast = 31.0, settle = 235,
		caption = "3/6  W76-1 at 100 kt   -- 31.0 cells, USA",
	},
	{
		order = "SarmatStrike", proxy = "power.sarmat", missile = "sarmatmissile",
		seat = "RUSSIA", name = "RS-28 Sarmat (750 kt/RV)", blast = 60.7, settle = 445,
		caption = "4/6  RS-28 Sarmat      -- 750 kt per RV, package of six, RUSSIA",
	},
	{
		order = "B83Strike", proxy = "power.b83", missile = "b83missile",
		seat = "USA", name = "B83-1 (1.2 Mt)", blast = 71.0, settle = 520,
		caption = "5/6  B83-1 at 1.2 Mt   -- 71.0 cells each, package of six, USA",
	},
	{
		order = "TsarBombaStrike", proxy = "power.tsarbomba", missile = "tsarbombamissile",
		seat = "RUSSIA", name = "Tsar Bomba (50 Mt)", blast = 246.1, settle = 0,
		caption = "6/6  TSAR BOMBA 50 Mt  -- 246 cells, 25-second fireball, RUSSIA",
	},
}

-- ---- BUDGETS ---------------------------------------------------------------------------------
-- Each is generous against what the shipped numbers actually need, and each exists so that ONE
-- broken warhead cannot stall the arsenal behind it -- the demo logs why and moves to the next.
--
-- BuyPatience:   a purchase completes in a single tick under sandbox (GetBuildTime returns 0), and
--                in BuildDuration+2 = 7 without it. 300 is two orders of magnitude of slack.
-- FirePatience:  the magazine is already banked when this phase starts, so 'issued' is expected on
--                the first attempt. The budget covers a queue that reported ready a tick early.
-- WatchPatience: must exceed the longest possible flight. The slowest body is TsarBombaMissile at
--                Speed 400 over a 201747 standoff = 504 ticks, plus AimPointInterval stagger.
--                900 is comfortably past it.
local BuyPatience = 300
local FirePatience = 120
local WatchPatience = 900

-- Ticks between an observed detonation and its capture. The fireball is a point on the impact tick
-- and reaches its size over the following seconds, so a capture ON the detonation tick is a bright
-- dot rather than a weapon. Two ticks per cell of blast radius, floored at 6 and capped at 90
-- (5.4 s) so the Tsar Bomba -- whose fireball burns for 417 ticks -- is caught well grown while the
-- 0.3 kt shot, which is over almost at once, is caught before it fades.
local function CaptureDelayFor(shot)
	local d = math.floor(shot.blast * 2)
	if d < 6 then d = 6 end
	if d > 90 then d = 90 end
	return d
end

local tick = 0
local USA
local Russia

local idx = 1
local phase = "buy"
local phase_started = 0
local buy_status = "not-started"
local ordered_at = 0
local seen_peak = 0
local outcomes = {}

local function SeatFor(shot)
	if shot.seat == "RUSSIA" then
		return Russia
	end

	return USA
end

-- Everything that matters goes to lua.log on a fixed prefix so a capture run can be graded from
-- the log alone, and to the chat log so a viewer watching live sees the same thing.
local function Announce(line, chat)
	print(line)
	if chat ~= nil then
		Media.DisplayMessage(chat, "ARSENAL")
	end
end

-- Forward declaration. Advance calls it when the sixth shot leaves the machine, and Step's
-- failure branches all route through Advance -- so the demo ENDS on the last shot however that
-- shot turned out, rather than only on a clean detonation. The earlier draft of this file
-- returned without a verdict when the last warhead was the one that failed.
local Finish

-- THE ONLY EXIT FROM A SHOT. Every branch below records an outcome and comes through here, so the
-- summary line and the Test.Skip reason carry one token per warhead with no gaps.
local function Advance(result)
	outcomes[idx] = result
	idx = idx + 1
	phase = "buy"
	phase_started = tick
	seen_peak = 0
	buy_status = "not-started"

	if SHOTS[idx] == nil then
		Finish()
	end
end

local function Step()
	-- THE ENGINE'S OWN CLOCK, not a counter this file keeps. DateTime.GameTime is
	-- World.WorldTick (DateTimeGlobal.cs:49), which is EXACTLY what Test.Screenshot stamps into the
	-- manifest (TestGlobal.cs:165) -- so a `tick=` in lua.log and the `tick` on its PNG are the
	-- same number and can be read against each other. A private counter would have drifted from it
	-- by however many ticks elapsed before Trigger.OnTick was registered.
	tick = DateTime.GameTime

	local shot = SHOTS[idx]
	if shot == nil then
		return
	end

	local seat = SeatFor(shot)
	local n = idx

	if seat == nil then
		Announce(string.format(
			"ARSENAL NOT-FIRED %d/6 %s tick=%d reason=no-player seat=%s",
			n, shot.name, tick, shot.seat), shot.caption .. "  [NOT FIRED: no " .. shot.seat .. " player]")
		Advance("no-player")
		return
	end

	if phase == "buy" then
		-- EnsurePower is stateless and idempotent: it reads the power's own state, queues a
		-- purchase when the magazine is empty, and reports 'loading' while one is in flight. It
		-- also refuses to touch a production property before TestHarness.ProductionWarmupTicks,
		-- which is why shot 1 can be driven from tick 1 without the permanent empty-queue failure
		-- that helper's header describes.
		local ready, status = TestHarness.EnsurePower(seat, shot.proxy, shot.order, tick)
		buy_status = status
		if ready then
			phase = "fire"
			phase_started = tick
		elseif tick - phase_started > BuyPatience then
			-- 'buying' here is the SILENT prerequisite failure described in the header: the order
			-- was dropped by ProductionQueue.ResolveOrder because the proxy is not in
			-- BuildableItems for this seat. 'refused' means the queue rejected it outright (no
			-- producer, or the power's lobby checkbox is off). 'absent' means a wrong OrderName.
			Announce(string.format(
				"ARSENAL NOT-FIRED %d/6 %s tick=%d reason=never-banked magazine=%s firer=%s",
				n, shot.name, tick, buy_status, shot.seat),
				shot.caption .. "  [NOT FIRED: magazine " .. buy_status .. "]")
			Advance("never-banked")
		end

		return
	end

	if phase == "fire" then
		-- Test.ActivateSupportPower is staging, not an assertion: it is the only route to a
		-- support-power order from script. Its result IS read, because 'issued' only means the
		-- order was put on the wire -- the watch phase below is what says it arrived.
		local status = Test.ActivateSupportPower(seat, shot.order, CPos.New(GroundZero.X, GroundZero.Y))
		if status == "issued" then
			ordered_at = tick
			phase = "watch"
			phase_started = tick
			seen_peak = 0
			Announce(string.format(
				"ARSENAL LAUNCH %d/6 %s tick=%d firer=%s order=issued",
				n, shot.name, tick, shot.seat), shot.caption)
		elseif tick - phase_started > FirePatience then
			Announce(string.format(
				"ARSENAL NOT-FIRED %d/6 %s tick=%d reason=%s magazine=%s firer=%s",
				n, shot.name, tick, status, buy_status, shot.seat),
				shot.caption .. "  [NOT FIRED: " .. status .. ", magazine " .. buy_status .. "]")
			Advance("not-fired:" .. status)
		end

		return
	end

	if phase == "watch" then
		-- THE OBSERVATION. The strike's missile bodies are real actors owned by the firing player
		-- (MissileStrikePower.cs:818-821 creates them with an OwnerInit of the power's own actor
		-- owner), and they leave the world when they hit. GetActorsByType filters on
		-- `!IsDead && IsInWorld` (PlayerProperties.cs:92-102), so the count going back to zero after
		-- having been positive IS the last warhead of the salvo detonating. Nothing here is
		-- predicted from a tick table.
		local live = #seat.GetActorsByType(shot.missile)
		if live > seen_peak then
			seen_peak = live
		end

		if seen_peak > 0 and live == 0 then
			local flight = tick - ordered_at
			Announce(string.format(
				"ARSENAL DETONATION %d/6 %s tick=%d warheads=%d flight=%d firer=%s",
				n, shot.name, tick, seen_peak, flight, shot.seat),
				string.format("%d/6  %s -- IMPACT at tick %d, %d warhead(s)",
					n, shot.name, tick, seen_peak))

			local label = string.format("%02d-%s", n, shot.order)
			local note = string.format(
				"Detonation %d/6: %s, fired by %s. %d warhead(s) observed, %d ticks of flight, "
					.. "impact at tick %d, captured %d ticks later so the fireball has grown. "
					.. "expects: a fresh band of destruction centred on cell 64,64 reaching about "
					.. "%.1f cells, with intact targets beyond it.",
				n, shot.name, shot.seat, seen_peak, flight, tick, CaptureDelayFor(shot), shot.blast)

			Trigger.AfterDelay(CaptureDelayFor(shot), function()
				TestHarness.Screenshot(label, note)
			end)

			phase = "settle"
			phase_started = tick
		elseif tick - phase_started > WatchPatience then
			Announce(string.format(
				"ARSENAL NOT-OBSERVED %d/6 %s tick=%d reason=no-missile-seen peak=%d firer=%s",
				n, shot.name, tick, seen_peak, shot.seat),
				shot.caption .. "  [NO DETONATION SEEN in " .. WatchPatience .. " ticks]")
			Advance("not-observed")
		end

		return
	end

	if phase == "settle" then
		-- Let the blast wave finish before the next shot is ordered, so each ring can be attributed
		-- to exactly one weapon. The capture for this shot is already scheduled and lands inside
		-- this window on every row (the longest capture delay is 90 against the shortest settle of
		-- 190 that follows a delayed capture).
		if tick - phase_started >= shot.settle then
			Advance("detonated")
		end

		return
	end
end

Finish = function()
	-- The aftermath frame, then the verdict file. 240 ticks (~14 s) after the Tsar Bomba's settle so
	-- its fireball -- 417 ticks of burn -- is past its peak and the crater field is readable.
	Trigger.AfterDelay(240, function()
		TestHarness.Screenshot("07-aftermath",
			"AFTERMATH, all six warheads fired. expects: every ring of target sites flattened, the "
				.. "two Supply Routes still standing (their only target type is NoAutoTarget and no "
				.. "arsenal warhead lists it), and the tree actors untouched at every radius "
				.. "(^TreeIndestructible zeroes the damage; that is not a weapon underperforming).")
	end)

	Trigger.AfterDelay(300, function()
		local parts = {}
		for i = 1, #SHOTS do
			parts[#parts + 1] = string.format("%d:%s=%s", i, SHOTS[i].name, outcomes[i] or "unreached")
		end

		local summary = "eyeball: the nuclear arsenal, six warheads in ascending yield at cell "
			.. "64,64. Shots 1/2/3/5 are USA's, shots 4 and 6 are Russia's -- the Sarmat and the "
			.. "Tsar Bomba carry `player.russia` and an America seat cannot fire them. Outcomes: "
			.. table.concat(parts, ", ")
			.. ". Six `ARSENAL DETONATION` lines in lua.log is the demo working; grep it."

		print("ARSENAL SUMMARY " .. table.concat(parts, ", "))

		-- Skip, not Pass and not Fail: this is a demo and asserts nothing. It exists so run-test.sh
		-- gets a verdict file and exits cleanly instead of being killed by the watchdog. Same
		-- convention as demo-defcon-readout.
		Test.Skip(summary)
	end)
end

WorldLoaded = function()
	USA = Player.GetPlayer("USA")
	Russia = Player.GetPlayer("Russia")

	if USA == nil then
		print("ARSENAL FATAL tick=0 reason=no-USA-player")
	end

	-- Russia is a BARE MAP COMBATANT, not a lobby slot, so it has a Player object in a
	-- single-client run (CreateMapPlayers.cs:95-122). If this is ever nil, the seat was given
	-- `Playable: True` and the run seated nobody in it -- see the header.
	if Russia == nil then
		print("ARSENAL FATAL tick=0 reason=no-Russia-player "
			.. "(is PlayerReference@Russia marked Playable? it must NOT be)")
	end

	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New(GroundZero.X * 1024 + 512, GroundZero.Y * 1024 + 512, 0)

	-- FRAME THE SHOT RATHER THAN ASKING THE VIEWER TO. Camera.Zoom is a MULTIPLE OF THE DEFAULT
	-- level, not the engine's raw Viewport.Zoom, so the same value frames the same amount of map on
	-- any display (CameraGlobal.cs:30-60). MinZoom is as far out as this display goes, which is what
	-- a 246-cell blast wants. This file used to carry a header saying the camera could not be zoomed
	-- from Lua and a description line telling the viewer to zoom out by hand; the binding landed
	-- 2026-09-06 and DOCS/recipes/DEMO.md names that exact instruction as a fault to fix.
	Camera.Zoom = Camera.MinZoom

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Media.DisplayMessage("Nuclear arsenal: six warheads, ascending yield, one aim point. "
		.. "USA fires 1/2/3/5; RUSSIA fires the Sarmat and the Tsar Bomba.", "ARSENAL")

	print("ARSENAL START tick=0 aim=64,64 shots=6 "
		.. "seats=USA:B61Low,B61Max,W76,B83 RUSSIA:Sarmat,TsarBomba")

	-- NOT ON TICK 0. World SETUP completes well before that world's first render pass, and a
	-- capture taken in WorldLoaded comes back as menu widgets over a black background --
	-- indistinguishable from a map that failed to load. DOCS/recipes/SCREENSHOT.md records that
	-- exact false positive from 2026-08-16. 30 ticks is ~1.8 s of rendering.
	Trigger.AfterDelay(30, function()
		TestHarness.Screenshot("00-opening",
			"OPENING FRAME, before any shot. expects: the playfield zoomed fully out, ground zero "
				.. "(cell 64,64) centred, thirteen concentric rings of target sites intact, and "
				.. "the support-power bin drawn top-left with the USA Supply Route selected. A "
				.. "BLACK frame here is the render-pass trap, not a broken map -- check the PNG's "
				.. "byte size before believing it.")
	end)

	-- Trigger.OnTick is a WW3MOD addition to TriggerGlobal (TriggerGlobal.cs:35-52). Two older
	-- scenarios carry comments asserting it does not exist; those are stale.
	Trigger.OnTick(Step)
end
