#region Copyright & License Information
/*
 * THE BOT'S NUCLEAR POLICY -- "It only fires if it is losing, so it never escalates unnecessarily"
 * (the user, 2026-09-13), plus the two things the exchange ruling adds: a retaliation window that is
 * the biggest thing a side may fire, and a top rung reachable only through one.
 *
 * Everything under test lives in NuclearPolicyMath, a world-free static class, for the reason
 * NuclearExchangeStateTest's header gives: nothing in OpenRA.Test can construct a World, so a
 * predicate inside a trait method is a predicate verified by READING. NuclearBotModule is a thin
 * shell over this -- it answers "which powers exist, what is ready, who can I see, what does the
 * order look like" and nothing else -- and can only be checked by playing a match.
 *
 * THE LOAD-BEARING TESTS ARE WinningNeverFires AND GameEnderNeedsBothLosingAndAWindow. The first is
 * the user's entire rule; if it ever goes green-by-accident the bot is a first-striker. The second
 * is the ruling's top rung: a bot that could reach a game-ender from a permanent level would make
 * the apocalypse a thing one side simply accumulates, which is the "draw card" decision 01 exists
 * to prevent.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class NuclearPolicyMathTest
	{
		const int Hold = (int)NuclearRung.Hold;
		const int Kiloton = (int)NuclearRung.Kiloton;
		const int TwentyKiloton = (int)NuclearRung.TwentyKiloton;
		const int FiftyKiloton = (int)NuclearRung.FiftyKiloton;
		const int HundredKiloton = (int)NuclearRung.HundredKiloton;
		const int GameEnder = (int)NuclearRung.GameEnder;

		const int RatioPercent = 60;          // the module's LosingArmyRatioPercent default.
		const int ContestationPercent = 40;   // the module's LosingContestationPercent default.
		const int RateLimit = 900;            // the module's MinTicksBetweenLaunches default.

		static int Mask(params int[] bands)
		{
			var mask = 0;
			foreach (var b in bands)
				mask = NuclearPolicyMath.WithBand(mask, b);

			return mask;
		}

		/// <summary>Released, losing, nothing fired recently: the ordinary shooting position.</summary>
		// THE SIDE COOLDOWN IS NOT A PARAMETER, and its absence is the v2 model rather than an
		// omission: a side inside its cooldown has EVERY band unready, because NuclearExchange writes
		// the cooldown onto every one of that side's support powers. So `Mask()` -- an empty ready
		// mask -- IS a bot on cooldown, and that is the only way this fixture can express one.
		static NuclearBotDecision Choose(
			int readyMask,
			bool losing = true,
			int level = Kiloton,
			bool released = true,
			bool finalExchange = false,
			int ticksSinceLastLaunch = 100000,
			bool mayFireGameEnder = true)
		{
			return NuclearPolicyMath.Choose(
				released, losing, finalExchange, level,
				readyMask, ticksSinceLastLaunch, RateLimit, mayFireGameEnder);
		}

		// ==== THE LOSING PREDICATE ===========================================================

		[Test]
		public void ArmyRatioBelowThresholdIsLosing()
		{
			// 59 % of 1000 is below the 60 % line; 60 % is not, because the comparison is strict.
			Assert.That(NuclearPolicyMath.IsLosingNow(590, 1000, RatioPercent, false, 100, ContestationPercent),
				Is.True, "59 % of the strongest enemy must read as losing.");
			Assert.That(NuclearPolicyMath.IsLosingNow(600, 1000, RatioPercent, false, 100, ContestationPercent),
				Is.False, "Exactly at the threshold is NOT losing -- the test is strictly below.");
		}

		[Test]
		public void AnEnemyWithNoArmyCannotMakeAnybodyLose()
		{
			// The comparison is own*100 < enemy*ratio, which is false at enemy == 0 for EVERY own value
			// including zero. This is what stops the opening ticks of a match -- before either side has
			// called anything in -- from reading as a rout and firing on tick one.
			Assert.That(NuclearPolicyMath.IsLosingNow(0, 0, RatioPercent, false, 100, ContestationPercent),
				Is.False, "Two empty armies are not losing to each other.");
			Assert.That(NuclearPolicyMath.IsLosingNow(0, 1, RatioPercent, false, 100, ContestationPercent),
				Is.True, "One credit of enemy army against none of ours IS losing.");
		}

		[Test]
		public void ContestationIsTheOtherHalfAndItIsOred()
		{
			// A HEALTHY ARMY AND A FALLING SUPPLY ROUTE IS A LOSING POSITION, and this is the case an
			// army-only predicate cannot see. Contestation, not attrition, is what actually defeats a
			// player in this mod (SupplyRouteContestation's own [Desc]).
			Assert.That(NuclearPolicyMath.IsLosingNow(5000, 1000, RatioPercent, true, 39, ContestationPercent),
				Is.True, "A five-to-one army lead with the SR bar at 39 % must still read as losing.");
			Assert.That(NuclearPolicyMath.IsLosingNow(5000, 1000, RatioPercent, true, 40, ContestationPercent),
				Is.False, "Exactly at the contestation threshold is not losing.");
		}

		[Test]
		public void NoSupplyRouteSkipsTheContestationHalf()
		{
			// haveSupplyRoute false must not read the bar at all: a player with no SR would otherwise
			// take whatever sentinel the caller passed as gospel.
			Assert.That(NuclearPolicyMath.IsLosingNow(5000, 1000, RatioPercent, false, 0, ContestationPercent),
				Is.False, "With no Supply Route, a zero bar is not evidence of anything.");
		}

		[Test]
		public void TheArmyRatioDoesNotOverflow()
		{
			// ArmyValue is a sum over every unit a player owns and is bounded by nothing this code can
			// see. `own * 100` in int wraps above ~21.5 M, so both operands below are already past the
			// point where the un-widened comparison returns garbage. The widen-BEFORE-multiply is what
			// makes these two answers the arithmetic ones rather than whatever the wrap produced.
			Assert.That(
				NuclearPolicyMath.IsLosingNow(1000000000, 1000000000, RatioPercent, false, 100, ContestationPercent),
				Is.False, "Two equal billion-credit armies: 100 % of the enemy is not below 60 %.");
			Assert.That(
				NuclearPolicyMath.IsLosingNow(100000000, 1000000000, RatioPercent, false, 100, ContestationPercent),
				Is.True, "A tenth of the enemy's army is losing at any scale.");
		}

		// ==== HYSTERESIS =====================================================================

		[Test]
		public void StreakClimbsOnLosingAndResetsInstantlyOnNot()
		{
			var streak = 0;
			streak = NuclearPolicyMath.AdvanceStreak(streak, true);
			streak = NuclearPolicyMath.AdvanceStreak(streak, true);
			Assert.That(streak, Is.EqualTo(2));
			Assert.That(NuclearPolicyMath.IsCommitted(streak, 3), Is.False, "Two of three is not committed.");

			streak = NuclearPolicyMath.AdvanceStreak(streak, true);
			Assert.That(NuclearPolicyMath.IsCommitted(streak, 3), Is.True, "Three of three is.");

			// THE ASYMMETRY IS THE POINT. One good evaluation drops the whole streak -- climbing out is
			// instant, climbing in takes N -- so a bot that stops losing stops firing on the next beat
			// rather than decaying back down through the count.
			streak = NuclearPolicyMath.AdvanceStreak(streak, false);
			Assert.That(streak, Is.EqualTo(0));
			Assert.That(NuclearPolicyMath.IsCommitted(streak, 3), Is.False);
		}

		[Test]
		public void StreakSaturatesRatherThanOverflowing()
		{
			var streak = NuclearPolicyMath.AdvanceStreak(NuclearPolicyMath.MaxStreak, true);
			Assert.That(streak, Is.EqualTo(NuclearPolicyMath.MaxStreak),
				"A bot losing for hours must not wrap the counter and read as not-losing on the tick it does.");
		}

		[Test]
		public void ZeroRequiredStreakMeansOneNotAlways()
		{
			Assert.That(NuclearPolicyMath.IsCommitted(0, 0), Is.False,
				"'No hysteresis' must mean one losing evaluation is enough, not that the bot is always committed.");
			Assert.That(NuclearPolicyMath.IsCommitted(1, 0), Is.True);
		}

		// ==== THE POLICY =====================================================================

		[Test]
		public void WinningNeverFires()
		{
			// THE USER'S WHOLE RULE. Every band ready, a window wide open, nothing on cooldown -- and it
			// must still decline, naming the right reason.
			var decision = Choose(
				Mask(Kiloton, TwentyKiloton, FiftyKiloton, HundredKiloton, GameEnder),
				losing: false, level: GameEnder);

			Assert.That(decision.Fire, Is.False, "A winning bot fired. That is the one thing it must never do.");
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.NotLosing),
				"Declining for the wrong reason reads the same in a log as declining for the right one.");
		}

		[Test]
		public void NotLosingIsCheckedBeforeReadiness()
		{
			// A winning bot with an empty bin must read NotLosing, not NoReadyBand: the two are the same
			// observation otherwise, and only one of them says the rule is working.
			var decision = Choose(Mask(), losing: false);
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.NotLosing));
		}

		[Test]
		public void BeforeReleaseNothingFiresEvenWhenLosing()
		{
			var decision = Choose(Mask(Kiloton), released: false);
			Assert.That(decision.Fire, Is.False);
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.NotReleased));
		}

		[Test]
		public void LosingFiresTheHighestBandTheLevelAllows()
		{
			// ONE FIRING BRANCH, WHERE v1 HAD TWO. Under v2 firing ANY band costs the side the same
			// thing -- its whole arsenal for a cooldown -- so taking the smallest would be paying full
			// price for the least effect.
			var decision = Choose(Mask(Kiloton, TwentyKiloton), level: TwentyKiloton);

			Assert.That(decision.Band, Is.EqualTo(TwentyKiloton));
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.HighestAllowed));
		}

		[Test]
		public void ABandAboveTheLevelIsNeverFired()
		{
			// READY IS NOT THE SAME AS PERMITTED. A 50 kt warhead sitting loaded -- from a
			// `powers-sandbox` lobby, or from a level the side has since... it cannot lose, but the
			// mask is measured off the powers and the level off the state, and the two are allowed to
			// disagree for a tick or two while a condition propagates. The level is the authority.
			var decision = Choose(Mask(Kiloton, FiftyKiloton), level: Kiloton);
			Assert.That(decision.Band, Is.EqualTo(Kiloton));
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.HighestAllowed));
		}

		[Test]
		public void ACooledDownSideReadsAsNoReadyBandRatherThanFiring()
		{
			// THE COOLDOWN, AS THIS FIXTURE CAN SEE IT. NuclearExchange puts the side's cooldown on
			// every one of its nuclear powers, so a reloading side reaches the policy with an EMPTY
			// mask however high its level is. This is the assertion that says the policy needs no
			// second copy of rule 2 -- and it is why `Choose` has no cooldown parameter.
			var decision = Choose(Mask(), level: HundredKiloton);

			Assert.That(decision.Fire, Is.False, "a bot with nothing loaded fired anyway");
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.NoReadyBand),
				"a reloading bot must read NoReadyBand, not NotLosing -- the two want opposite investigations");
		}

		[Test]
		public void TheRateLimitHoldsEvenWhileLosing()
		{
			var decision = Choose(Mask(Kiloton), ticksSinceLastLaunch: RateLimit - 1);
			Assert.That(decision.Fire, Is.False);
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.RateLimited));

			decision = Choose(Mask(Kiloton), ticksSinceLastLaunch: RateLimit);
			Assert.That(decision.Fire, Is.True, "Exactly at the limit is permitted.");
		}

		// ==== THE TOP RUNG ===================================================================

		[Test]
		public void GameEnderNeedsBothLosingAndAWindow()
		{
			// (a) Losing, game-ender loaded, but the side is only at level 4: must not fire it. Under
			// v1 the cap here was written explicitly to mirror "game-enders are only ever a window
			// grant"; under v2 END is an ordinary rung and the LEVEL is the cap, so this is the
			// assertion that the level is honoured at the top exactly as it is lower down.
			var belowEnd = Choose(Mask(GameEnder), level: HundredKiloton);
			Assert.That(belowEnd.Band, Is.Not.EqualTo(GameEnder),
				"a game-ender was fired by a side that has not been hit with a 100 kt");
			Assert.That(belowEnd.Fire, Is.False, "nothing else was ready either, so it must decline");

			// (b) Winning and at level 5 with the ender loaded: must not fire it. The user's rule
			// outranks the ladder at every rung including the last.
			var winning = Choose(Mask(GameEnder), losing: false, level: GameEnder);
			Assert.That(winning.Fire, Is.False);

			// (c) Losing AND at level 5: fires.
			var both = Choose(Mask(GameEnder), level: GameEnder);
			Assert.That(both.Band, Is.EqualTo(GameEnder));
			Assert.That(both.Reason, Is.EqualTo(NuclearBotReason.HighestAllowed));
		}

		[Test]
		public void MayFireGameEnderFalseWithholdsItAndSaysSo()
		{
			var decision = Choose(Mask(GameEnder), level: GameEnder, mayFireGameEnder: false);

			Assert.That(decision.Fire, Is.False);
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.GameEnderWithheld),
				"Withholding must be distinguishable from having nothing to fire.");

			// AND IT IS ONLY "WITHHELD" WHEN THERE WAS SOMETHING TO WITHHOLD. A bot at level 5 with an
			// EMPTY bin declined nothing -- it had nothing -- and reporting GameEnderWithheld there
			// would send a triager after the flag when the problem is an unloaded arsenal.
			var empty = Choose(Mask(), level: GameEnder, mayFireGameEnder: false);
			Assert.That(empty.Reason, Is.EqualTo(NuclearBotReason.NoReadyBand));
		}

		[Test]
		public void MayFireGameEnderFalseStillFiresTheBandBelow()
		{
			// WITHHOLDING THE APOCALYPSE MUST NOT WITHHOLD THE REPLY. This is why the cap is lowered
			// BEFORE the search rather than the top band being rejected after it: rejecting afterwards
			// would make a `MayFireGameEnder: false` bot stop fighting entirely whenever its END cameo
			// happened to be loaded, because it would never look at the 100 kt underneath.
			var decision = Choose(
				Mask(HundredKiloton, GameEnder), level: GameEnder, mayFireGameEnder: false);

			Assert.That(decision.Band, Is.EqualTo(HundredKiloton));
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.HighestAllowed));
		}

		[Test]
		public void TheFinalExchangeOverridesEveryOtherGate()
		{
			// Not losing, rate-limited, game-enders withheld -- and it must STILL place. Once
			// DoomsdayStrike has opened the window the match is ending and Dead Hand places for anyone
			// who does not, so declining forfeits the bot's aim points and saves nothing.
			var decision = Choose(
				Mask(GameEnder), losing: false, finalExchange: true,
				ticksSinceLastLaunch: 0, mayFireGameEnder: false);

			Assert.That(decision.Band, Is.EqualTo(GameEnder));
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.FinalExchange));
		}

		[Test]
		public void TheFinalExchangeWithNoGameEnderLoadedDeclinesQuietly()
		{
			var decision = Choose(Mask(HundredKiloton), finalExchange: true);
			Assert.That(decision.Fire, Is.False, "A side with no game-ender has nothing to place.");
			Assert.That(decision.Reason, Is.EqualTo(NuclearBotReason.NoReadyBand));
		}

		// ==== AIM POINTS =====================================================================

		static NuclearTargetCandidate C(uint key, int x, int y, int value, bool sr = false)
		{
			return new NuclearTargetCandidate(key, new CPos(x, y), value, sr);
		}

		[Test]
		public void TheAimPointMaximisesValueInsideTheRadius()
		{
			// Two clusters: a lone 1000 at (0,0) and three 400s packed at (20,20). The pair sums to 1200
			// inside a radius of 4, so the aim point belongs on the cluster and not on the single
			// biggest actor -- which is the whole reason the score is a radial sum.
			var candidates = new List<NuclearTargetCandidate>
			{
				C(1, 0, 0, 1000),
				C(2, 20, 20, 400),
				C(3, 21, 20, 400),
				C(4, 20, 21, 400),
			};

			var index = NuclearPolicyMath.PickAimIndex(candidates, 4, 100);
			Assert.That(candidates[index].Cell.X, Is.EqualTo(20).Or.EqualTo(21));
			Assert.That(NuclearPolicyMath.ScoreAt(candidates, index, 4, 100), Is.EqualTo(1200));
		}

		[Test]
		public void TiesBreakOnTheLowerActorId()
		{
			// Two identical clusters infinitely far apart. Without an id tie-break the answer would
			// depend on the order the caller happened to build the list in, which is the one thing a
			// host-only decision that turns into a synced ORDER must not do.
			var candidates = new List<NuclearTargetCandidate> { C(7, 0, 0, 500), C(3, 40, 40, 500) };
			Assert.That(NuclearPolicyMath.PickAimIndex(candidates, 2, 100), Is.EqualTo(1),
				"The lower ActorID (3) must win, whichever position it is in.");

			var reversed = new List<NuclearTargetCandidate> { C(3, 40, 40, 500), C(7, 0, 0, 500) };
			Assert.That(candidates[NuclearPolicyMath.PickAimIndex(candidates, 2, 100)].Key,
				Is.EqualTo(reversed[NuclearPolicyMath.PickAimIndex(reversed, 2, 100)].Key),
				"The same set in a different order must produce the same cell.");
		}

		[Test]
		public void TheSupplyRouteBonusMovesTheAimPointAndIsInertAtOneHundred()
		{
			// A 1000-value cluster with no SR against a 700 one with it. At 100 % the big cluster wins;
			// at 150 % the SR side scores 1050 and takes it.
			var candidates = new List<NuclearTargetCandidate>
			{
				C(1, 0, 0, 1000),
				C(2, 40, 40, 700),
				C(3, 40, 41, 0, sr: true),
			};

			Assert.That(candidates[NuclearPolicyMath.PickAimIndex(candidates, 3, 100)].Key, Is.EqualTo(1u));
			Assert.That(candidates[NuclearPolicyMath.PickAimIndex(candidates, 3, 150)].Key, Is.EqualTo(2u));
		}

		[Test]
		public void EmptyCandidatesGiveNoAimPoint()
		{
			Assert.That(NuclearPolicyMath.PickAimIndex(new List<NuclearTargetCandidate>(), 4, 100), Is.EqualTo(-1));
			Assert.That(NuclearPolicyMath.PickAimIndex(null, 4, 100), Is.EqualTo(-1));
		}

		[Test]
		public void MultiAimPointsAreSpreadRatherThanStacked()
		{
			// Three clusters far apart. Six highest-scoring discs over the same cluster overlap almost
			// completely -- six warheads doing slightly more than one -- so the separation is what makes
			// a MIRV a MIRV.
			var candidates = new List<NuclearTargetCandidate>
			{
				C(1, 0, 0, 900), C(2, 1, 0, 900),
				C(3, 30, 0, 800), C(4, 31, 0, 800),
				C(5, 60, 0, 700), C(6, 61, 0, 700),
			};

			var picked = NuclearPolicyMath.PickAimIndices(candidates, 4, 100, 3, 8, 0);
			Assert.That(picked.Length, Is.EqualTo(3));

			var xs = new List<int>();
			foreach (var i in picked)
				xs.Add(candidates[i].Cell.X);

			Assert.That(xs[0], Is.LessThan(2), "Best cluster first.");
			Assert.That(xs, Has.Some.GreaterThan(25), "The second aim point must leave the first cluster.");
			Assert.That(xs, Has.Some.GreaterThan(55), "And the third must leave the second.");
		}

		[Test]
		public void MultiAimPointsObeyTheSpreadBound()
		{
			// MissileStrikePowerInfo.MaxAimPointSpread CLAMPS a decoded order against the FIRST aim
			// point, so a point outside the bound is not the point that was scored. Picking one would
			// silently land the warhead somewhere nobody chose.
			var candidates = new List<NuclearTargetCandidate>
			{
				C(1, 0, 0, 900),
				C(2, 5, 0, 800),
				C(3, 100, 0, 850),
			};

			var picked = NuclearPolicyMath.PickAimIndices(candidates, 2, 100, 3, 3, 10);
			foreach (var i in picked)
				Assert.That(candidates[i].Cell.X, Is.LessThanOrEqualTo(10),
					"An aim point outside MaxAimPointSpread of the first was chosen.");
		}

		[Test]
		public void MultiAimPointsStopWhenTheCandidatesRunOut()
		{
			// Asking for six aim points over one cluster must return fewer, not six copies of the same
			// cell and not an exception.
			var candidates = new List<NuclearTargetCandidate> { C(1, 0, 0, 900), C(2, 1, 0, 900) };
			var picked = NuclearPolicyMath.PickAimIndices(candidates, 4, 100, 6, 8, 0);
			Assert.That(picked.Length, Is.EqualTo(1));
		}
	}
}
