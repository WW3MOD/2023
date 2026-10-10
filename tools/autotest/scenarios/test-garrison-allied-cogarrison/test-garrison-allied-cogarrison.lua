-- AUTO TEST: when two ALLIED players' soldiers garrison one neutral building, does every soldier keep
-- his owner?
--
-- WORKSPACE/ideas/261007_DIGEST.md #9, DOCS/design/261007_engine-robustness-scout.md §2 ENG #2
-- (labelled HYPOTHESIS there). Allied clone of test-garrison-hostile-cogarrison.
--
-- THE MECHANISM, as read at origin/main @ 0b08754c:
--   1. The first boarder's GarrisonManager.OnPassengerEntered records claimedOwner synchronously and
--      calls ChangeOwnerInPlace (GarrisonManager.cs:286-297), which only ENQUEUES the flip as a
--      frame-end task (Actor.cs:535-538).
--   2. A second, ALLIED boarder in the same frame-end drain passes the boarding filter -- it reads
--      claimedOwner, and MayBoard admits Ally (GarrisonManager.cs:341-352) -- and is loaded.
--   3. The flip runs: ChangeOwnerInPlaceSync notifies INotifyOwnerChanged (Actor.cs:558-559), and
--      Cargo.OnOwnerChanged calls p.ChangeOwner(newOwner) for EVERY passenger, with no test of whose
--      man he is (Cargo.cs:1276-1283).
--   So the ally's soldier becomes the first boarder's. World.cs:520-521 drains the queue until it is
--   empty, so the flip enqueued by boarder 1 runs AFTER boarder 2's already-queued load.
--   A boarder in a LATER drain is not affected: the house is already the ally's, nothing flips.
--
-- TWO LEGS, because only one of them can be made to hit the window on purpose:
--   LEG A -- WALKED, through Test.ClickOrder (the real targeter). Mirror-image start cells, same
--            order tick. Whether both men landed in the same drain is MEASURED (first tick each is
--            seen in the hold) and reported in the notes; a PASS of this leg with different ticks
--            says nothing about the race.
--   LEG B -- SEATED, two House.LoadPassenger calls on one tick. Lua runs during the world tick, so
--            both loads happen before the frame-end flip: the window is hit deterministically.
--            LoadPassenger goes through Cargo.CanLoad (TransportProperties.cs:42-56), so this is
--            the same filter path a walker meets, not a bypass of it.
--
-- VERDICTS:
--   FAIL "ALLIED PASSENGER CONVERTED: ..."  -- at least one soldier's Owner is not the player he
--                                             started with. The list names each one, leg and owners.
--                                             Expected for leg B at 0b08754c (see header).
--   PASS "ALLIED CO-GARRISON KEEPS OWNERS"  -- all four men read their original owner. The note
--                                             carries leg A's boarding ticks so a PASS that never
--                                             raced is visible as such.
--   FAIL "SETUP INVALID: ..."              -- a man was not offered EnterTransport, never boarded,
--                                             LoadPassenger refused, or a house never left Neutral.
--                                             Nothing about ownership was measured.

local BoardWithin = 25      -- s for leg A's walkers to cover one diagonal cell and board
local ClaimWithin = 3       -- s for leg B's house to read USA after the seat
local SettleSeconds = 2     -- s after the last boarding: ChangeOwner is itself a frame-end task

local Original = {}         -- actor -> owner name at WorldLoaded
local LegA = { us = nil, ally = nil }   -- first tick each walker was seen in HouseA's hold
local Tick = 0

local function OwnerOf(actor)
	local o = actor.Owner
	if o == nil then
		return "<none>"
	end

	return o.InternalName
end

local function Inside(soldier, house)
	return Test.IsLoadedInto(soldier, house) or Test.IsAtGarrisonPort(soldier, house)
end

local function State()
	return "HouseA owner " .. OwnerOf(HouseA) ..
		" (UsManA in=" .. tostring(Inside(UsManA, HouseA)) .. " owner " .. OwnerOf(UsManA) ..
		", AllyManA in=" .. tostring(Inside(AllyManA, HouseA)) .. " owner " .. OwnerOf(AllyManA) ..
		", boarded ticks us=" .. tostring(LegA.us) .. " ally=" .. tostring(LegA.ally) .. ")" ..
		"; HouseB owner " .. OwnerOf(HouseB) ..
		" (UsManB in=" .. tostring(Inside(UsManB, HouseB)) .. " owner " .. OwnerOf(UsManB) ..
		", AllyManB in=" .. tostring(Inside(AllyManB, HouseB)) .. " owner " .. OwnerOf(AllyManB) .. ")"
end

local function SetupInvalid(why)
	Test.Fail("SETUP INVALID: " .. why .. " Nothing about allied ownership was measured. " .. State())
end

local function SameDrainText()
	if LegA.us == nil or LegA.ally == nil then
		return "leg A boarding ticks unknown"
	end

	if LegA.us == LegA.ally then
		return "leg A boarded in the SAME drain (tick " .. LegA.us .. ")"
	end

	return "leg A boarded " .. math.abs(LegA.us - LegA.ally) .. " tick(s) apart, so leg A never " ..
		"raced the flip and its PASS is about a later boarder only"
end

local function Evaluate()
	local converted = {}
	local subjects = {
		{ "A", "UsManA", UsManA }, { "A", "AllyManA", AllyManA },
		{ "B", "UsManB", UsManB }, { "B", "AllyManB", AllyManB },
	}

	for _, s in ipairs(subjects) do
		local now = OwnerOf(s[3])
		if now ~= Original[s[3]] then
			converted[#converted + 1] = "leg " .. s[1] .. " " .. s[2] .. " " .. Original[s[3]] .. " -> " .. now
		end
	end

	if #converted > 0 then
		Test.Fail("ALLIED PASSENGER CONVERTED: " .. table.concat(converted, "; ") .. ". A soldier who " ..
			"boarded alongside an ally now belongs to the building's owner. Mechanism: the first " ..
			"boarder's neutral->owner flip is a frame-end task (GarrisonManager.OnPassengerEntered -> " ..
			"Actor.ChangeOwnerInPlace), and Cargo.OnOwnerChanged (Cargo.cs:1276-1283) re-owns EVERY " ..
			"passenger, including an ally loaded in the same drain. " .. SameDrainText() .. ". " .. State())
		return
	end

	Test.Pass("ALLIED CO-GARRISON KEEPS OWNERS. All four soldiers read their original owner " ..
		SettleSeconds .. "s after boarding; HouseA " .. OwnerOf(HouseA) .. ", HouseB " .. OwnerOf(HouseB) ..
		". Leg B seated both men on one tick, before the flip ran, so the Cargo.OnOwnerChanged path " ..
		"was exercised and something now spares the ally. " .. SameDrainText() .. ". " .. State())
end

-- LEG B. Seated on one tick; the house leaving Neutral is the proof that OnPassengerEntered ran
-- (Cargo.Load only notifies once its own Created frame-end task has run, Cargo.cs:729-738 -- a seat
-- before that would load both men with no claim and no flip, and PASS vacuously).
local function RunLegB(andThen)
	local ok, err = pcall(function()
		HouseB.LoadPassenger(UsManB)
		HouseB.LoadPassenger(AllyManB)
	end)

	if not ok then
		SetupInvalid("leg B LoadPassenger refused (" .. tostring(err) .. ").")
		return
	end

	if not (Inside(UsManB, HouseB) and Inside(AllyManB, HouseB)) then
		SetupInvalid("leg B LoadPassenger returned but both men are not in HouseB's hold.")
		return
	end

	local remaining = math.floor(ClaimWithin * TestHarness.TicksPerSecond)
	local check
	check = function()
		if OwnerOf(HouseB) ~= "Neutral" then
			andThen()
			return
		end

		remaining = remaining - 1
		if remaining <= 0 then
			SetupInvalid("HouseB still read Neutral " .. ClaimWithin .. "s after two men were seated, " ..
				"so OnPassengerEntered never claimed it and no flip ran -- the hypothesis was not exercised.")
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

-- LEG A. Polled every tick, so the first tick a man is seen in the hold identifies his drain: loads
-- happen at frame end, so two men in one drain are first seen on the same tick.
local function WatchLegA()
	local remaining = math.floor(BoardWithin * TestHarness.TicksPerSecond)
	local check
	check = function()
		Tick = Tick + 1
		if LegA.us == nil and Inside(UsManA, HouseA) then
			LegA.us = Tick
		end

		if LegA.ally == nil and Inside(AllyManA, HouseA) then
			LegA.ally = Tick
		end

		if LegA.us ~= nil and LegA.ally ~= nil then
			RunLegB(function()
				Trigger.AfterDelay(math.floor(SettleSeconds * TestHarness.TicksPerSecond), Evaluate)
			end)
			return
		end

		remaining = remaining - 1
		if remaining <= 0 then
			SetupInvalid("leg A: not both walkers boarded HouseA within " .. BoardWithin .. "s.")
			return
		end

		Trigger.AfterDelay(1, check)
	end

	Trigger.AfterDelay(1, check)
end

WorldLoaded = function()
	TestHarness.FocusBetween(HouseA, HouseB)

	for _, a in ipairs({ UsManA, AllyManA, UsManB, AllyManB }) do
		Original[a] = OwnerOf(a)
	end

	if Original[UsManA] ~= "USA" or Original[AllyManA] ~= "Allied" or
		Original[UsManB] ~= "USA" or Original[AllyManB] ~= "Allied" then
		SetupInvalid("the soldiers did not start with owners USA/Allied/USA/Allied. Check map.yaml.")
		return
	end

	if OwnerOf(HouseA) ~= "Neutral" or OwnerOf(HouseB) ~= "Neutral" then
		SetupInvalid("a house did not start Neutral. Check map.yaml.")
		return
	end

	-- CONTROL on the relationship: Allies has to be written on both PlayerReferences, and a one-sided
	-- entry makes the two disagree. The boarding filter reads the owner's view of the passenger.
	local usa = Player.GetPlayer("USA")
	local allied = Player.GetPlayer("Allied")
	if not (usa.IsAlliedWith(allied) and allied.IsAlliedWith(usa)) then
		SetupInvalid("USA and Allied are not mutually allied (USA->Allied " ..
			tostring(usa.IsAlliedWith(allied)) .. ", Allied->USA " .. tostring(allied.IsAlliedWith(usa)) ..
			"). Check Allies: on both PlayerReferences.")
		return
	end

	-- Both orders on the same tick, through the real targeter.
	local us = Test.ClickOrder(UsManA, HouseA)
	local ally = Test.ClickOrder(AllyManA, HouseA)
	if us ~= "EnterTransport" or ally ~= "EnterTransport" then
		SetupInvalid("leg A walkers were not both offered EnterTransport against a NEUTRAL house " ..
			"(USA got '" .. tostring(us) .. "', Allied got '" .. tostring(ally) .. "').")
		return
	end

	WatchLegA()
end
