-- DEMO -- the RS-26 Oreshnik: six conventional re-entry vehicles arriving as plasma streaks, near
-- vertically, faster than anything else in the mod. Six passes. Nothing here is asserted: no
-- AssertWithin, no Test.Pass, no Test.Fail, no result.json. The viewer watches and closes the
-- window when done.
--
-- ================================================================================================
-- WHAT TO LOOK AT
-- ================================================================================================
--
-- 1. THE STREAK, on every pass. Six RVs drop into frame from the TOP of the viewport, not from the
--    side. Each should read as a bar of white light roughly four cells long -- 14 trailing samples
--    at 288 wdist -- with a blue-white bloom standing off the nose, and NOT as a missile sprite
--    wearing a bright trail. The airframe itself is replaced with flat white at alpha E0
--    (WithHypersonicPlasma.BodyColor); that field is what stops it reading as an airframe.
--
--    THE NOSE BLOOM IS MEANT TO BE ABSENT ON THE LAST TICK BEFORE EACH IMPACT -- about four frames
--    at 60 fps. The leading sheath is clamped against the distance still to travel with a whole
--    tick reserved, so at 6491 wdist/tick there is no room for it on the final step. The wake and
--    the white body are NOT clamped and must still be there. What must never appear on those
--    frames is any part of the bloom in FRONT of the crater; demo-missile-impact-clamp is the demo
--    for that invariant on its own.
--
-- 2. THE SPEED, on passes 2, 4 and 6 only. A Kh-47M2 Kinzhal -- the fastest thing in the mod before
--    this branch -- is ordered ON THE SAME TICK as the Oreshnik, at an aim point seven cells away.
--    Both fly the same 197-cell standoff and, because rules.yaml equalises their MissileDelay to
--    15, the gap between the two impacts is flight time and nothing else. THE ORESHNIK LANDS FIRST
--    BY 32 TICKS (1.9 s), and -- the cleaner thing to check, because it needs no stopwatch --
--    ALL SIX RVs HAVE LANDED BEFORE THE KINZHAL ARRIVES AT ALL. The salvo runs from +62 to +82
--    (AimPointInterval 4 x 5 warheads); the Kinzhal lands at +94. If they interleave, or the
--    Kinzhal lands first, the speed numbers are not doing what vehicles-russia.yaml says they do.
--
-- 3. THE ANGLE AND THE FOOTPRINT. The Kinzhal crosses the frame almost flat (10.5-degree terminal
--    slope). The Oreshnik falls through it (56.25 degrees). And under the aim ring is a 4x4 grid of
--    T-90s at three-cell spacing, rebuilt before every pass: each RV should kill what it lands on
--    or beside and leave vehicles three cells away standing. A grid wiped flat, or a grid barely
--    scratched, both mean the warhead is not the "high point damage, fairly low spread" profile the
--    ask calls for.
--
-- ================================================================================================
-- WHY THE WARHEADS LAND ON A RING RATHER THAN WHERE SOMEBODY CLICKED
-- ================================================================================================
-- Test.ActivateSupportPower issues a SINGLE-target order. That is the bot / Lua path, not the
-- placement-mode path a human uses: MissileStrikePower.ResolveAimPoints sees one point and lays the
-- other five on a ring of AimPointFallbackSpread (5c0, rules/player.yaml) around it. So this demo
-- shows a FIXED, REPEATABLE six-point pattern. It is not what a player would see -- a player clicks
-- six times and puts them wherever they like -- and it is the right thing for a demo, because every
-- pass then has the same geometry to compare against the last one.
--
-- ================================================================================================
-- THE SCHEDULE, in ticks from WorldLoaded
-- ================================================================================================
-- Timestep is 60 ms, so 16.67 ticks per second -- NOT the 25 that TestHarness.TicksPerSecond
-- carries (that is a preserved harness convention for AssertWithin budgets, documented in
-- test-helpers.lua, and is not the tick rate). Every delay below is therefore RAW TICKS through
-- Trigger.AfterDelay.
--
-- REWRITTEN 2026-09-09, AND THE NUMBERS IT USED TO CARRY WERE WRONG. It claimed a first Oreshnik
-- impact at +62, a last at +82 and a Kinzhal at +94. The real figures under the shipped arithmetic
-- were +82, +102 and +115: EstimateArcTicks divides the standoff by Speed, and the old block used
-- neither the 3000 the estimate actually reads nor the 2000 the Kinzhal carries. That is why the
-- first two captures of run 260909_102330_p4788 fired at +55 and +72 and both photographed an
-- untouched grid -- they were placed 27 and 10 ticks BEFORE anything had landed.
--
-- rules.yaml overrides MissileDelay to 15 for both powers, and the standoff on this map is
-- mapDiagonal + ApproachMargin = 185363 + 16384 = 201747 wdist = 197.0 cells.
--
-- THE ORESHNIK NO LONGER FLIES THAT STANDOFF. MissileStrikePower@Oreshnik sets ApproachDistance
-- 16c0, so each RV is born 16 cells from its aim point and flies 16384/3000 = 5 ticks. The 62 ticks
-- it no longer spends in the air are added back onto MissileDelay by the trait, so the IMPACT TICKS
-- BELOW ARE UNCHANGED BY THAT FEATURE -- only the moment each warhead appears is. The Kinzhal is
-- untouched and still flies the full 197 cells at 2000 wdist/tick.
--
-- So each pass runs:
--
--     +0     zoom set, grid rebuilt, order(s) issued. Nothing is on screen yet.
--     +15    the Kinzhal enters, far off screen and low -- the camera is on the impact zone.
--     +77    FIRST RV APPEARS, 16 cells from its aim point and 24 cells up. There is no earlier
--            Oreshnik sprite anywhere: the launch and the cruise are not drawn at all.
--     +82    FIRST ORESHNIK IMPACT.
--     +92    LAST ORESHNIK IMPACT. Six craters over 10 ticks, 0.6 s end to end (AimPointInterval 2
--            x 5 gaps). RVs appear at +77, 79, 81, 83, 85, 87 and land at +82, 84, 86, 88, 90, 92,
--            so three are falling at any moment.
--     +115   KINZHAL IMPACT, on the even passes -- 33 ticks (2.0 s) after the first RV and 23 after
--            the last. The whole salvo is down well before it arrives.
--
-- Passes are 140 ticks apart -- clear of the 90-tick ChargeInterval rules.yaml overrides in and of
-- the Kinzhal's 100-tick flight plus its 15-tick launch delay -- so nothing overlaps the next pass.
--
--     PASS 1  order  30   Oreshnik alone          zoom 2
--     PASS 2  order 170   Oreshnik + Kinzhal      zoom 1
--     PASS 3  order 310   Oreshnik alone          zoom 2
--     PASS 4  order 450   Oreshnik + Kinzhal      zoom 1
--     PASS 5  order 590   Oreshnik alone          zoom 2
--     PASS 6  order 730   Oreshnik + Kinzhal      zoom 1
--
-- ================================================================================================
-- WHY THE ZOOM ALTERNATES, AND THE NUMBER IT IS TRADING OFF
-- ================================================================================================
-- A near-vertical arrival at 6490 wdist/tick is on screen very briefly, and that is arithmetic
-- rather than a shortcoming of the demo. An RV becomes visible when its ALTITUDE lifts it into the
-- top of the viewport: at a slope of 1.50 against a NW heading whose ground track carries it 0.71
-- cells nearer per cell flown, the net rise above the impact point is 0.79 cells per cell of
-- approach. ApproachDistance 16c0 puts the whole flight inside 16 cells of approach, so the RV is
-- born 12.6 cells above its aim point on screen and these numbers are now bounded by the FLIGHT
-- rather than by the viewport. On a 1280x720 viewport, per RV:
--
--     zoom 1    15.0 cells of half-height   in frame from birth   all 5 ticks   ~18 frames @60fps
--     zoom 2     7.5                        enters  9.5 cells out      3 ticks   ~11 frames
--     zoom 3     5.0                        enters  6.3                2 ticks    ~7 frames
--
-- ONE of those ticks always has no nose bloom (the impact clamp; see above), so zoom 3 would show
-- the sheath for three frames and is not worth having. The two zooms below answer the two different
-- questions this demo is asking:
--
--   ZOOM 2, on the Oreshnik-only passes: 189 px of wake against a ~28 px sprite. This is the zoom
--   for "does it read as a streak rather than as a missile with a trail", which is a question about
--   pixels.
--   ZOOM 1, on the comparison passes: 94 px of wake, but 30 cells of sky above the impact. This is
--   the zoom for "is it steep" and "which one lands first", both of which are questions about
--   geometry and need the frame rather than the pixels.
--
-- The SALVO is on screen longer than any single RV, because AimPointInterval staggers them by 2
-- ticks each: from the first RV appearing (+77) to the last one impacting (+92) is 15 ticks, 0.9 s
-- of continuous streaks with three in the air at once. That is the thing to watch on the odd
-- passes -- and it is now the WHOLE of what the Oreshnik draws. Before ApproachDistance the RVs
-- were also drawn for the preceding 60-odd ticks, crossing the frame on a shallow diagonal from far
-- out while their altitude carried them down through it. That was the launch and the cruise, and
-- the user asked for neither.
--
-- After pass 6 nothing further is scheduled, but both powers keep recharging on their 90-tick
-- interval, so the viewer can fire either of them anywhere from the support-power bin. The Oreshnik
-- is the last icon in the bin (SupportPowerPaletteOrder 16), the Kinzhal the first.

local OreshnikAim = { X = 45, Y = 49 }
local KinzhalAim = { X = 51, Y = 44 }

-- Fixed for the whole demo, between the ring centre and the Kinzhal's point.
local CameraCell = { X = 47, Y = 47 }

local OreshnikOrder = "OreshnikStrike"
local KinzhalOrder = "KinzhalStrike"

-- 4x4 on a three-cell lattice, centred on the Oreshnik's aim point. Three cells is measured against
-- the warhead: OreshnikRVExplosion reaches 2c0 with its shockwave and 512 wdist with its lethal
-- core, so a vehicle three cells from an impact is outside both and is the control.
local GridOrigin = { X = 41, Y = 45 }
local GridStep = 3
local GridSize = 4

local Passes = {
	{ tick =  30, kinzhal = false, zoom = 2 },
	{ tick = 170, kinzhal = true,  zoom = 1 },
	{ tick = 310, kinzhal = false, zoom = 2 },
	{ tick = 450, kinzhal = true,  zoom = 1 },
	{ tick = 590, kinzhal = false, zoom = 2 },
	{ tick = 730, kinzhal = true,  zoom = 1 }
}

local tick = 0
local next_pass = 1
local Russia
local USA
local targets = {}

-- Rebuilt rather than repaired, so every pass starts from an identical grid and a viewer comparing
-- pass 5 against pass 1 is comparing the strike and not the leftovers. Husks are cleared too: a
-- t90 leaves one on death, and six passes of accumulated wrecks would bury the thing being looked
-- at under scenery.
local function rebuildGrid()
	for i = 1, #targets do
		local a = targets[i]
		if a.IsInWorld then
			a.Destroy()
		end
	end

	targets = {}

	-- Anything else the USA still owns on the impact ground -- husks and bailed-out crew from the
	-- previous pass, mostly -- goes with them. The Supply Route at 18,18 is ninety cells away and is
	-- left alone.
	--
	-- SystemActorTypes IS NOT DEFENSIVE PADDING -- IT IS THE LINE THIS DEMO CRASHED ON.
	-- <player>.GetActors() DOES NOT MEAN "this player's units". Every Player builds a PlayerActor
	-- and calls Initialize(true), and that `true` is addToWorld: Actor.cs:313 runs World.Add(this),
	-- so the player actor is a genuine entry in world.Actors, owned by its own Player, not dead and
	-- IsInWorld. PlayerProperties.GetActors() filters on exactly those three things
	-- (PlayerProperties.cs:79), so USA.GetActors() HANDS BACK USA'S OWN PLAYER ACTOR alongside the
	-- tanks -- type "player", which is why the deny-list below is keyed on Type and why the old
	-- `a.Type ~= "supplyroute"` was not enough.
	--
	-- Destroying it is fatal and not locally: Destroy() queues RemoveSelf, which Disposes the
	-- actor, which drops every trait USA owns out of the TraitDictionary. Nothing complains at the
	-- time. The next engine read of USA.PlayerActor throws "Attempted to get trait from destroyed
	-- object (player 2 (not in world))" and the process is gone. Here that read was
	-- EnemyWatcher.cs:105, which reaches for the OWNER of each newly-seen enemy actor 30 ticks
	-- later -- the sixteen T-90s this very function creates are that owner's actors, so the loop
	-- kills the player and then manufactures the evidence that trips over the corpse. It is not
	-- EnemyWatcher's fault and guarding it would fix nothing: Health.cs:226 dereferences
	-- attacker.Owner.PlayerActor unguarded on the first damage event and would have been next.
	-- (An unexplained crash carrying this exact message came through Health.cs on 2026-08-14 --
	-- WORKSPACE/audit/logs-260816-snapshot/Logs/exception-2026-08-14T143631Z.log, the Javelin
	-- reversal sweep. That rig calls neither GetActors nor Destroy, so it is NOT this bug; it is
	-- only evidence that a destroyed player actor surfaces wherever the engine next looks.)
	--
	-- "world" is listed too. It cannot appear here (the world actor belongs to the world-owning
	-- player, Neutral on this map, never USA), but it is the other actor World.Add holds that no
	-- script ever means, and this loop is exactly the shape somebody copies against Neutral.
	local SystemActorTypes = { player = true, world = true }

	local leftovers = USA.GetActors()
	for i = 1, #leftovers do
		local a = leftovers[i]
		if a.IsInWorld and a.Type ~= "supplyroute" and not SystemActorTypes[a.Type] then
			a.Destroy()
		end
	end

	for row = 0, GridSize - 1 do
		for col = 0, GridSize - 1 do
			targets[#targets + 1] = Actor.Create("t90", true, {
				Owner = USA,
				Location = CPos.New(GridOrigin.X + (col * GridStep), GridOrigin.Y + (row * GridStep)),
				Facing = Angle.New(0)
			})
		end
	end
end

-- ================================================================================================
-- THE CAPTURES WATCH FOR THE WARHEAD INSTEAD OF PREDICTING IT
-- ================================================================================================
-- WHY THIS EXISTS, AND IT IS NOT A REFINEMENT. The offsets in this file were derived by hand from
-- EstimateArcTicks TWICE and were wrong BOTH times -- once before ApproachDistance (claiming +62
-- when the shipped number was +82) and once after. Run 260909_110510_p6562 caught nothing in any of
-- six frames as a result. A third derivation is not worth having: a warhead that is drawn for five
-- ticks cannot be photographed by arithmetic that has already missed twice.
--
-- So the demo no longer guesses. It counts `oreshnikrv` actors in the world every tick and fires
-- its capture ON THE TICK the first one appears. That is not an approximation of the right moment;
-- it IS the right moment, and it is correct whatever the flight arithmetic turns out to be.
--
-- IT IS ALSO THE MEASUREMENT. Each note carries the tick the RV was actually observed at and how
-- many were in the world, so one run replaces the derivation permanently -- and it separates the
-- two failures that look identical in a PNG. An empty sky in a frame fired on the tick an RV is
-- provably in the world is a RENDERING failure. A note that never appears at all means no RV ever
-- entered the world, which is a timing or an activation failure. The old fixed offsets could not
-- tell those apart, which is exactly why two runs have now been spent on it.
local RVType = "oreshnikrv"
local KinzhalType = "kinzhalmissile"

local function CountOfType(t)
	local n = 0
	local all = Map.ActorsInWorld
	for i = 1, #all do
		if all[i].Type == t then
			n = n + 1
		end
	end

	return n
end

-- Per-pass observation state. Nil except during a pass that captures.
local watch = nil

local function watchTick()
	if watch == nil or watch.finished then
		return
	end

	local rel = tick - watch.start
	local rv = CountOfType(RVType)
	local kz = CountOfType(KinzhalType)

	-- Bail out rather than watch forever if nothing ever flies: the next pass would otherwise
	-- overwrite this state mid-observation and the labels would land on the wrong pass.
	if rel > 200 then
		watch.finished = true
		TestHarness.Screenshot(watch.labels[1],
			"MEASUREMENT: no " .. RVType .. " ever entered the world within 200 ticks of the " ..
			"order. This is NOT a capture-timing miss -- the warhead never existed. Look at the " ..
			"power activation (Test.ActivateSupportPower, the charge bank) and at MissileDelay, " ..
			"not at the screenshot offsets.")
		return
	end

	if rv > 0 and watch.rvFirst == nil then
		watch.rvFirst = rel
		TestHarness.Screenshot(watch.labels[1],
			"MEASURED at pass+" .. rel .. " with " .. rv .. " RV(s) in the world -- fired ON the " ..
			"tick the first warhead entered the world, not on a predicted offset. " ..
			watch.note1 .. " IF THIS FRAME IS EMPTY SKY the warhead exists and is not being " ..
			"DRAWN: that is a rendering fault (WithHypersonicPlasma / SubTickMotionSmoothing / the " ..
			"sequence), not a mistimed capture.")

		-- Four ticks on: some down, some still falling. Relative to the OBSERVED first sighting,
		-- so it stays correct even if the flight time changes again.
		--
		-- The label and note are bound to LOCALS rather than read off `watch` inside the closure:
		-- `watch` is reassigned at the start of every pass, and a deferred capture that outlived
		-- its pass would otherwise file itself under the next pass's label.
		local label2, note2 = watch.labels[2], watch.note2
		Trigger.AfterDelay(4, function()
			TestHarness.Screenshot(label2,
				"MEASURED 4 ticks after the first RV was seen (pass+" .. (rel + 4) .. "). " .. note2)
		end)
	end

	-- The salvo is over the moment the world holds no RV again. Everything downstream keys off
	-- THIS rather than off an impact tick, because this is observable and the impact tick is not.
	if watch.rvFirst ~= nil and rv == 0 and watch.rvLast == nil then
		watch.rvLast = rel
		local airborne = rel - watch.rvFirst
		local label3, note3 = watch.labels[3], watch.note3
		local first, last = watch.rvFirst, watch.rvLast

		Trigger.AfterDelay(8, function()
			TestHarness.Screenshot(label3,
				"MEASURED: RVs were in the world from pass+" .. first .. " to pass+" .. last ..
				", i.e. " .. airborne .. " ticks of salvo, and this frame is 8 ticks after the " ..
				"last one left. " .. note3)
		end)

		if not watch.watchKinzhal then
			watch.finished = true
		end
	end

	-- The comparison pass only: the Kinzhal is untouched by ApproachDistance and still flies the
	-- full standoff, so its impact is many ticks after the salvo. Keyed off the same observation.
	if watch.watchKinzhal then
		if kz > 0 then
			watch.kzSeen = true
		elseif watch.kzSeen and watch.kzGone == nil then
			watch.kzGone = rel
			watch.finished = true
			local label4, note4 = watch.labels[4], watch.note4
			local rvLast = tostring(watch.rvLast)

			Trigger.AfterDelay(8, function()
				TestHarness.Screenshot(label4,
					"MEASURED: the Kinzhal left the world at pass+" .. rel .. ", against the RVs " ..
					"at pass+" .. rvLast .. ". THE SPEED CLAIM IS THE GAP BETWEEN THOSE TWO " ..
					"NUMBERS and it is now measured rather than asserted. " .. note4)
			end)
		end
	end
end

local function step()
	tick = tick + 1

	watchTick()

	local pass = Passes[next_pass]
	if pass and tick >= pass.tick then
		next_pass = next_pass + 1

		rebuildGrid()

		-- Set at the START of the pass, ~62 ticks before anything is drawn, so the change never
		-- happens while a streak is in frame. Camera.Zoom is a multiple of the default level and is
		-- clamped to Camera.MinZoom..Camera.MaxZoom.
		Camera.Zoom = math.min(pass.zoom, Camera.MaxZoom)

		-- Test.ActivateSupportPower is STAGING, not an assertion, and its return value is
		-- deliberately not checked: this is a demo and there is no verdict to fail. If a pass does
		-- not arrive, the thing to look at is the support-power bin -- and then rules.yaml, where
		-- both powers have RequiresPurchase turned off so that a script can fire them at all.
		--
		-- BOTH ARE ORDERED ON THE SAME TICK on the even passes. That is the comparison: same tick,
		-- same standoff, same MissileDelay, so the only thing left to explain the gap between the
		-- two impacts is how fast the two missiles fly.
		Test.ActivateSupportPower(Russia, OreshnikOrder, CPos.New(OreshnikAim.X, OreshnikAim.Y))

		if pass.kinzhal then
			Test.ActivateSupportPower(Russia, KinzhalOrder, CPos.New(KinzhalAim.X, KinzhalAim.Y))
		end

		-- CAPTURE, on the first two passes only. The demo is for a viewer, but nobody has yet
		-- LOOKED at any of this -- every image of the plasma so far is reasoned from the two
		-- shipped configurations, not seen. Six frames is the whole evidence base, so they are
		-- placed on the beats the three claims actually turn on and nowhere else. Passes 3-6 take
		-- none: they are repeats for a human watching, and a seventh PNG of the same thing buys
		-- nothing.
		--
		-- NO OFFSETS. Every capture below is armed here and fired by watchTick() on the tick the
		-- warhead is observed in the world -- see the block above the step function for why the
		-- previous two sets of hand-derived offsets were both wrong.
		if not pass.kinzhal and next_pass == 2 then
			-- Pass 1, zoom 2, Oreshnik alone. The streak claim.
			watch = {
				start = tick,
				watchKinzhal = false,
				labels = { "01-streak-mid-descent", "02-mid-salvo", "03-footprint" },
				note1 =
					"expects: one or more white streaks falling STEEPLY toward the grid with a " ..
					"blue-white bloom at the nose, close above the impact zone, and a pristine " ..
					"grid. A streak entering from a frame EDGE on a shallow diagonal means " ..
					"ApproachDistance is not being applied.",
				note2 =
					"expects: some craters down and some RVs still falling -- the salvo is " ..
					"staggered. The ones still in the air keep their nose bloom; the bloom must " ..
					"never appear IN FRONT of a crater.",
				note3 =
					"expects: in the 4x4 T-90 grid at three-cell spacing, each RV killed what it " ..
					"landed on or beside while tanks three cells away stand. Not a flattened grid " ..
					"(spread too wide) and not an intact one (point damage too low)."
			}
		elseif pass.kinzhal and next_pass == 3 then
			-- Pass 2, zoom 1, both weapons on the same tick. The speed claim.
			watch = {
				start = tick,
				watchKinzhal = true,
				labels = {
					"04-both-in-flight", "05-mid-salvo-wide",
					"06-oreshnik-down-kinzhal-flying", "07-after-kinzhal"
				},
				note1 =
					"expects: the Oreshnik RVs dropping steeply onto the grid from close above " ..
					"it. The Kinzhal is ALSO in the air on this pass but is still far out and " ..
					"need not be in frame -- it is untouched by this work and flies the full " ..
					"197-cell standoff.",
				note2 =
					"expects: the wide view of the same salvo, four ticks on. This is the frame " ..
					"for `is it steep`, which is a question about the frame rather than the pixels.",
				note3 =
					"expects: all six RVs on the ground and the Kinzhal STILL IN THE AIR. If the " ..
					"Kinzhal has already landed, or they are interleaved, the speed claim is wrong.",
				note4 =
					"expects: both impacts done. The Kinzhal crater is visibly WIDER and softer " ..
					"than the six Oreshnik points, which is the conventional-precision profile."
			}
		end

		if next_pass > #Passes then
			return
		end
	end

	Trigger.AfterDelay(1, step)
end

WorldLoaded = function()
	Russia = Player.GetPlayer("Russia")
	USA = Player.GetPlayer("USA")

	-- Cell centre in world coordinates is cell * 1024 + 512.
	Camera.Position = WPos.New((CameraCell.X * 1024) + 512, (CameraCell.Y * 1024) + 512, 0)

	-- Pass 1's zoom, set here so the first frame is already right; every pass sets its own when it
	-- starts. See the table above for why it alternates.
	Camera.Zoom = math.min(Passes[1].zoom, Camera.MaxZoom)

	-- Pre-selected so the support-power bin is on screen without the viewer clicking first.
	TestHarness.Select(OwnSR)

	Trigger.AfterDelay(1, step)
end
