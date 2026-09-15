#region Copyright & License Information
/*
 * WHO COUNTS AS A SIDE. Moved here from NuclearExchangeStateTest when the predicate became shared
 * between NuclearExchange and DefconWall (CombatantSides), rather than a copy in each that agreed
 * by inspection.
 *
 * ONLY THE PURE OVERLOAD IS REACHABLE FROM HERE, and that is the constraint rather than a choice:
 * nothing in OpenRA.Test can construct a World, and therefore not a Player either. The
 * Player-taking adapter is deliberately one statement of field-unpacking for exactly that reason --
 * every rule worth pinning lives in the four-flag form below.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CombatantSidesTest
	{
		[Test]
		public void ASideIsAnyCombatantAndNotOnlyALobbySlot()
		{
			// A REGRESSION TEST WITH A RUN BEHIND IT. This rule read `!NonCombatant && Playable` and
			// dropped Russia from test-nuclear-exchange, because that scenario's map.yaml wrote
			// `Playable: True` on USA and not on Russia -- and PlayerReference.Playable defaults to
			// FALSE (PlayerReference.cs:24). The run logged "NUCLEAR RELEASE: all 1 sides", every
			// Russian nuclear power stayed dark for the whole match, and DefconWall logged "derived
			// from 2 home(s) in 2 group(s)" off the same players on the same tick.
			//
			// So the assertion that matters is the NEGATIVE one: a player who is not a lobby slot is
			// still a side. Everything else here is the boundary around it.
			Assert.That(CombatantSides.CountsAsASide(false, false, false, false), Is.True,
				"an ordinary combatant is a side");

			// Neutral, Creeps and the world owner. Arming them would put a phantom third side into
			// the count and make every launch warn about a lobby nobody configured.
			Assert.That(CombatantSides.CountsAsASide(true, false, false, false), Is.False,
				"a non-combatant became a side");

			// A spectator has no side to be on, which is DefconWall's reasoning verbatim
			// (DefconWall.cs:303-310) -- including them would arm somebody who cannot fire.
			Assert.That(CombatantSides.CountsAsASide(false, true, false, false), Is.False,
				"a spectator became a side");

			Assert.That(CombatantSides.CountsAsASide(true, true, false, false), Is.False);
		}

		[Test]
		public void AnAuthoredSpectatorSlotIsExcludedEvenWhenAClientOccupiesIt()
		{
			// THE ARM THAT COST TWO RUNS, AND THE REASON THE PREVIOUS FIXTURE COULD NOT CATCH IT.
			// Every case above passes RUNTIME flags. Player has two constructor branches and only
			// the map-player one copies the PlayerReference through: the CLIENT branch
			// (Player.cs:170-186) assigns ClientIndex, colour, name, faction, HomeLocation, spawn
			// and handicap, and never touches NonCombatant, Playable or spectating. So for a
			// `Playable: True` slot that a client is seated in -- which is what the autotest
			// harness does with an Observer -- BOTH runtime flags are false no matter what the map
			// wrote, and the pair (false, false) above is the only thing the old predicate ever saw.
			//
			// Runs 260914_141246 and 260914_181212 both logged
			//     DEFCON wall: no line derived from 3 combatant home(s) in 3 alliance group(s)
			// with an Observer authored `Spectating: True, NonCombatant: True`. The second run had
			// the NonCombatant already added, which is what proved the flag was being dropped rather
			// than mis-set.
			Assert.That(CombatantSides.CountsAsASide(false, false, false, true), Is.False,
				"a client-occupied slot the MAP marked Spectating became a side");

			Assert.That(CombatantSides.CountsAsASide(false, false, true, false), Is.False,
				"a client-occupied slot the MAP marked NonCombatant became a side");

			// THE SAFETY ARGUMENT, PINNED AS A TEST BECAUSE IT IS THE WHOLE LICENCE FOR THE NEW ARM:
			// nothing here can exclude a real player. A genuine lobby spectator is a client with NO
			// SLOT and never gets a Player object at all, so the only thing these two flags can
			// describe is a map-authored spectator/non-combatant slot -- exactly the case that must
			// never be a side. An ordinary combatant, authored ordinary, is unaffected.
			Assert.That(CombatantSides.CountsAsASide(false, false, false, false), Is.True,
				"the new arm excluded an ordinary combatant");
		}
	}
}
