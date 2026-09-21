import io
import re


def edit(p, pairs, bulk=None):
    s = io.open(p, encoding='utf-8-sig').read()
    for old, new in pairs:
        assert old in s, (p, old[:80])
        s = s.replace(old, new, 1)
    if bulk:
        for old, new in bulk:
            s = s.replace(old, new)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)
    print('ok', p)


# ===================== test-nuclear-ender-level: the faction-lock scenario =====================
P = 'tools/autotest/scenarios/test-nuclear-ender-level/test-nuclear-ender-level.lua'
edit(P, [
('local USA_ENDER  = "B83Strike"          -- 1200000 t band 5  GameEnder, powers.event + player.america',
 'local USA_ENDER  = "TridentStrike"      -- 455000 t band 5  GameEnder, powers.event + player.america'),

('''				.. " B83Strike must be a cameo USA can click."''',
 '''				.. " TridentStrike must be a cameo USA can click."'''),

('''				.. " which folds in prereqsAvailable -- and B83 declares `powers.event`, a"''',
 '''				.. " which folds in prereqsAvailable -- and the Trident declares `powers.event`, a"'''),

('''				.. " national ender per side -- B83 for USA, Sarmat for Russia. `ready` here means"''',
 '''				.. " national ender per side -- Trident for USA, Sarmat for Russia. `ready` here means"'''),

('''				"RUSSIA WAS HANDED AMERICA'S WARHEAD. B83Strike declares `powers.event,"''',
 '''				"RUSSIA WAS HANDED AMERICA'S WARHEAD. TridentStrike declares `powers.event,"'''),
])

# The header paragraph that names the pair.
s = io.open(P, encoding='utf-8-sig').read()
s = s.replace('B83 for USA', 'Trident for USA')
io.open(P, 'w', encoding='utf-8', newline='').write(s)

edit('tools/autotest/scenarios/test-nuclear-ender-level/map.yaml', [
("#              USA's B83Strike must read `ready` in the support bin, not `hidden`.",
 "#              USA's TridentStrike must read `ready` in the support bin, not `hidden`."),

('''#   FACTION -- USA must NOT see the Sarmat and Russia must NOT see the B83. The 2026-09-14 ruling''',
 '''#   FACTION -- USA must NOT see the Sarmat and Russia must NOT see the Trident. The 2026-09-14 ruling'''),
])

# ===================== test-final-exchange-autofire =====================
edit('tools/autotest/scenarios/test-final-exchange-autofire/test-final-exchange-autofire.lua', [
('--       130  USA places its B83. Russia deliberately does not.',
 '--       130  USA places its Trident. Russia deliberately does not.'),
('''				.. "there means the window never armed the B83, which is the tier, the condition or "''',
 '''				.. "there means the window never armed the Trident, which is the tier, the condition or "'''),
('''		-- A SINGLE-TARGET ORDER, which is what this binding issues and what a bot issues. The
		-- power is a game-ender, so MissileStrikePower asks DoomsdayStrike for the package size
		-- (2 here) and lays the second bomb on the AimPointFallbackSpread ring around this click.
		-- That path is only exercised because the binding cannot drive placement mode.
		PlacementStatus = Test.ActivateSupportPower(USA, "B83Strike", CPos.New(AimPoint.X, AimPoint.Y))''',
 '''		-- A SINGLE-TARGET ORDER, which is what this binding issues and what a bot issues. The
		-- power is a game-ender, so MissileStrikePower asks DoomsdayStrike for the package size
		-- (4 here) and lays the remaining RVs on the AimPointFallbackSpread ring around this click.
		-- That path is only exercised because the binding cannot drive placement mode.
		PlacementStatus = Test.ActivateSupportPower(USA, "TridentStrike", CPos.New(AimPoint.X, AimPoint.Y))'''),
])

edit('tools/autotest/scenarios/test-final-exchange-autofire/rules.yaml', [
('''	MissileStrikePower@B83:
		MissileDelay: 120''',
 '''	MissileStrikePower@TridentW88:
		MissileDelay: 120'''),
('''	# BOTH, NOT JUST THE ONE THAT IS PLACED. Russia's Sarmat is fired FOR it at the close, so its
	# MissileDelay is inside the same flight budget FinalExchangeFlightTicks above has to cover.''',
 '''	# BOTH, NOT JUST THE ONE THAT IS PLACED. Russia's Sarmat is fired FOR it at the close, so its
	# MissileDelay is inside the same flight budget FinalExchangeFlightTicks above has to cover.
	#
	# BOTH ARE NOW sarmatmissile AT SPEED 1600 (the Trident reuses the Sarmat body), so the two
	# arcs are equal on this map at ~58 ticks -- where the B83 this replaced flew ~132. The floor
	# below is therefore more generous than it needs to be, and is left alone: it is the anchor and
	# every asserted tick in the .lua derives from it.'''),
])

# ===================== demo-doomsday-deadhand =====================
D = 'tools/autotest/scenarios/demo-doomsday-deadhand/demo-doomsday-deadhand.lua'
edit(D, [], bulk=[
    ('"B83Strike"', '"TridentStrike"'),
    ('USA places its B83', 'USA places its Trident'),
    ('B83 state printed', 'Trident state printed'),
    ('B83 cameo', 'Trident cameo'),
    ('B83 reads:', 'Trident reads:'),
    ('B83 now reads:', 'Trident now reads:'),
    ('fires its B83;', 'fires its Trident;'),
    ('Both, not just the B83:', 'Both, not just the Trident:'),
    ("USA's own 1.2 Mt", "USA's own 455 kt RVs"),
    ("USA's two 1.2 Mt", "USA's two 455 kt RVs"),
    ("USA's first 1.2 Mt", "USA's first 455 kt RV"),
])

edit('tools/autotest/scenarios/demo-doomsday-deadhand/rules.yaml', [
('''	MissileStrikePower@B83:
		MissileDelay: 120''',
 '''	MissileStrikePower@TridentW88:
		MissileDelay: 120'''),
('''	# BOTH, NOT JUST THE B83, AND THAT IS NEW AS OF 2026-09-20. Russia's Sarmat is no longer a''',
 '''	# BOTH, NOT JUST AMERICA'S, AND THAT IS NEW AS OF 2026-09-20. Russia's Sarmat is no longer a'''),
])

# ===================== NuclearGameEndersTest prose =====================
edit('engine/OpenRA.Test/OpenRA.Mods.Common/NuclearGameEndersTest.cs', [], bulk=[
    ('''			// 750 kt (RS-28 Sarmat, one re-entry vehicle) and 1.2 Mt (B83-1). Both above the
			// 100 kt band ceiling, both below SandboxOnlyAboveTons.
			Assert.That(NuclearGameEnders.Is(Power(750000)), Is.True, "the Sarmat is a game-ender");
			Assert.That(NuclearGameEnders.Is(Power(1200000)), Is.True, "the B83 is a game-ender");''',
     '''			// 750 kt (RS-28 Sarmat) and 455 kt (Trident II D5 / W88), one re-entry vehicle each.
			// Both above the 100 kt band ceiling, both below SandboxOnlyAboveTons.
			//
			// 455 kt IS THE ONE WORTH CHECKING, because it is the closest a shipped ender comes to
			// the band below: RungForYield has four ceilings and the highest is 100 kt, so anything
			// past it falls through to GameEnder. If a band were ever inserted between 100 kt and
			// the top, America would silently stop having a national ender at all and the cameo
			// would vanish -- the 2026-09-16 defect, arriving from a new direction.
			Assert.That(NuclearGameEnders.Is(Power(750000)), Is.True, "the Sarmat is a game-ender");
			Assert.That(NuclearGameEnders.Is(Power(455000)), Is.True, "the Trident W88 is a game-ender");'''),

    ('''			Assert.That(NuclearGameEnders.NamesAnOwner(Power(1200000, "powers.event, player.america"), EventTier),
				Is.True, "the B83 names America as its owner, beside the licensed event tier");''',
     '''			Assert.That(NuclearGameEnders.NamesAnOwner(Power(455000, "powers.event, player.america"), EventTier),
				Is.True, "the Trident names America as its owner, beside the licensed event tier");'''),

    ('''			Assert.That(NuclearGameEnders.OwnerPrerequisites(Power(1200000, "powers.event, player.america"), EventTier),
				Is.EqualTo(new[] { "player.america" }),
				"the B83's arming must turn on America's faction identity and nothing else");''',
     '''			Assert.That(NuclearGameEnders.OwnerPrerequisites(Power(455000, "powers.event, player.america"), EventTier),
				Is.EqualTo(new[] { "player.america" }),
				"the Trident's arming must turn on America's faction identity and nothing else");

			// AND THE B83, WHICH IS NO LONGER ANYBODY'S. It kept `powers.event` and lost
			// `player.america` on 2026-09-20, so subtracting the licensed tier leaves NOTHING and
			// ArmableBy refuses it -- the same shape the unattributed 6 Mt strategic strike has.
			// Re-attributing it would give America two national enders and the exchange would arm
			// both; see rules/player.yaml for the standing note.
			Assert.That(NuclearGameEnders.OwnerPrerequisites(Power(1200000, "powers.event"), EventTier),
				Is.Empty,
				"the B83 is retired from Escalation and must name no owner");'''),
])
