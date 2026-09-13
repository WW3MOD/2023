#region Copyright & License Information
/*
 * WW3MOD — the bot's nuclear decision, as arithmetic with no World in it.
 *
 * THE USER'S WHOLE BRIEF FOR THIS IS ONE SENTENCE: "It only fires if it is losing, so it never
 * escalates unnecessarily." Everything below is that sentence made testable, plus the two things
 * the 2026-09-13 exchange ruling (manager-2b944571 decision 01) adds on top of it — a retaliation
 * window that is the biggest thing a side may fire, and a top rung that is reachable only through
 * one.
 *
 * ==== WHY A SEPARATE FILE, AND NOT METHODS ON THE MODULE ====
 * The same reason NuclearExchangeState, FinalExchangeWindow and every *Math.cs beside this one give:
 * nothing in OpenRA.Test can construct a World, so a predicate living inside a trait method is a
 * predicate verified by reading. The module owns everything that needs a world — which powers exist,
 * what is ready, who is visible, what the order looks like. This owns only the decision.
 *
 * ==== WHAT "LOSING" MEANS, AND WHY IT IS TWO TESTS OR'd ====
 * Army ratio alone cannot see the mod's actual win condition. A side can hold a healthy army and
 * still be losing outright, because SupplyRouteContestation — not attrition — is what defeats a
 * player (its own [Desc], SupplyRouteContestation.cs:24-26: at 100 % you are passive if a teammate
 * can relieve you and DEFEATED if nobody can). So the contestation bar is the second test, and it is
 * OR rather than AND: either one being bad is a side that is losing.
 *
 * ==== HYSTERESIS IS NOT POLISH HERE ====
 * ArmyValue moves on every kill. A bare ratio crosses back and forth across its threshold during any
 * ordinary engagement, and a bot that re-decided on each crossing would fire on the first unlucky
 * trade. N consecutive losing evaluations is what makes "losing" a POSITION rather than a moment.
 * The streak resets to zero on a single not-losing evaluation — deliberately asymmetric: climbing
 * back out is instant, climbing in takes N.
 *
 * ==== DETERMINISM ====
 * Integer arithmetic, no RNG, no wall-clock, no floating point. The ratio test multiplies before it
 * compares (NuclearPostureScale.Apply's idiom, for its reason: a parenthesised integer division
 * yields 0 or 1 and loses the value entirely) and widens to long first, because ArmyValue times 100
 * is a product of two numbers neither of which this file bounds. Candidate ordering is supplied by
 * the caller and every tie breaks on the candidate's own actor id, so two callers handed the same
 * list get the same answer in the same order.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Why <see cref="NuclearPolicyMath.Choose"/> decided what it did. Carried on the decision so the
	/// bot debug line, the Lua readout and the fixtures all name the same reason — a policy that
	/// declined for the wrong reason and a policy that declined for the right one are the same
	/// observation otherwise.
	/// </summary>
	public enum NuclearBotReason
	{
		/// <summary>The release gate has not opened. Nothing nuclear is permitted to anybody yet.</summary>
		NotReleased = 0,

		/// <summary>The bot is not losing. THE DEFAULT ANSWER, and the user's whole rule.</summary>
		NotLosing = 1,

		/// <summary>Losing, but too soon after the last launch.</summary>
		RateLimited = 2,

		/// <summary>Losing and permitted, but no permitted band has a warhead ready right now.</summary>
		NoReadyBand = 3,

		/// <summary>A window granted a game-ender and <c>MayFireGameEnder</c> is off, with nothing else to fire.</summary>
		GameEnderWithheld = 4,

		/// <summary>The final exchange is already running: place the game-enders. Not a choice.</summary>
		FinalExchange = 5,

		/// <summary>Replying inside a retaliation window, at the band the window granted.</summary>
		Retaliation = 6,

		/// <summary>Firing the highest permanent band that is ready.</summary>
		Permanent = 7,

		/// <summary>
		/// <para>The policy chose a band and the MODULE could not use it: nothing legally visible to
		/// aim at, or the order was refused. Never returned by <see cref="NuclearPolicyMath.Choose"/> —
		/// only the module sets it.</para>
		/// </summary>
		// IT EXISTS SO A FAILED LAUNCH IS NOT REPORTED AS A DECISION. Without it the module leaves the
		// firing reason standing over a launch count that never moved, and a readout saying
		// `launches=0 | reason=Retaliation` sends whoever reads it after the policy when the problem is
		// an empty target list.
		NoTarget = 8,
	}

	/// <summary>One call to <see cref="NuclearPolicyMath.Choose"/>.</summary>
	public readonly struct NuclearBotDecision
	{
		/// <summary>The band to fire, as a <see cref="NuclearRung"/> value. <c>Hold</c> means DO NOT FIRE.</summary>
		public readonly int Band;

		/// <summary>Why. Meaningful whether or not anything is fired.</summary>
		public readonly NuclearBotReason Reason;

		public NuclearBotDecision(int band, NuclearBotReason reason)
		{
			Band = band;
			Reason = reason;
		}

		/// <summary>Is this a launch? Hold is the only non-firing band.</summary>
		public bool Fire => Band > (int)NuclearRung.Hold;

		public static NuclearBotDecision Hold(NuclearBotReason reason)
		{
			return new NuclearBotDecision((int)NuclearRung.Hold, reason);
		}
	}

	/// <summary>
	/// One place the bot might put a warhead, reduced to the four things the aim-point arithmetic
	/// reads. Built by the module from believed contacts or from legally-visible actors; this file
	/// never asks where it came from, which is what keeps the fog question on the module's side of
	/// the line.
	/// </summary>
	public readonly struct NuclearTargetCandidate
	{
		/// <summary>The enemy actor's synced ActorID. THE TIE-BREAK, and the reason one exists.</summary>
		public readonly uint Key;

		/// <summary>Where it is, or was last seen.</summary>
		public readonly CPos Cell;

		/// <summary>What it is worth, already scaled by belief confidence where that applies.</summary>
		public readonly int Value;

		/// <summary>Is this the enemy's Supply Route? The one target type the policy may prefer.</summary>
		public readonly bool IsSupplyRoute;

		public NuclearTargetCandidate(uint key, CPos cell, int value, bool isSupplyRoute)
		{
			Key = key;
			Cell = cell;
			Value = value;
			IsSupplyRoute = isSupplyRoute;
		}
	}

	public static class NuclearPolicyMath
	{
		/// <summary>
		/// A streak is capped here rather than allowed to run to <see cref="int.MaxValue"/>. The value is
		/// arbitrary and only has to exceed any sane hysteresis count; what matters is that a bot that
		/// has been losing for four hours cannot overflow the counter and read as "not losing" on the
		/// tick it wraps.
		/// </summary>
		public const int MaxStreak = 1000000;

		/// <summary>
		/// <para>Is this side losing RIGHT NOW — one evaluation, no memory. Two tests, OR'd; see the
		/// header for why the contestation half is not optional.</para>
		///
		/// <para>An enemy with no army at all cannot make anybody lose the ratio test: the comparison is
		/// <c>own * 100 &lt; enemy * ratio</c>, which is false at <c>enemy == 0</c> for every own value
		/// including zero. That is the honest reading — two sides with no army are not losing to each
		/// other — and it is also what stops the very first ticks of a match, before anything has been
		/// called in, from reading as a rout.</para>
		/// </summary>
		/// <param name="ownArmyValue">This player's <c>PlayerStatistics.ArmyValue</c>.</param>
		/// <param name="strongestEnemyArmyValue">The largest ArmyValue among enemies. 0 when there are none.</param>
		/// <param name="losingArmyRatioPercent">Below this percentage of the strongest enemy is losing.</param>
		/// <param name="haveSupplyRoute">False when this player owns no Supply Route to read a bar off.</param>
		/// <param name="controlBarPercent">Own SR's <c>ControlBarFraction</c> — ALREADY 0..100, not 0..1.</param>
		/// <param name="losingContestationPercent">Below this control percentage is losing.</param>
		public static bool IsLosingNow(
			int ownArmyValue,
			int strongestEnemyArmyValue,
			int losingArmyRatioPercent,
			bool haveSupplyRoute,
			int controlBarPercent,
			int losingContestationPercent)
		{
			// WIDENED BEFORE MULTIPLYING, not after. Neither operand is bounded by this file — ArmyValue
			// is a sum over every unit a player owns — and `(long)(a * 100)` would overflow in int and
			// then widen the wrong answer.
			if ((long)ownArmyValue * 100 < (long)strongestEnemyArmyValue * losingArmyRatioPercent)
				return true;

			return haveSupplyRoute && controlBarPercent < losingContestationPercent;
		}

		/// <summary>
		/// The streak after one more evaluation. Losing increments (saturating at
		/// <see cref="MaxStreak"/>), not losing RESETS TO ZERO — see the header on why that asymmetry is
		/// the point rather than an oversight.
		/// </summary>
		public static int AdvanceStreak(int streak, bool losingNow)
		{
			if (!losingNow)
				return 0;

			if (streak < 0)
				streak = 0;

			return streak >= MaxStreak ? MaxStreak : streak + 1;
		}

		/// <summary>
		/// Has the streak reached the hysteresis count? A required count of zero or less is read as one,
		/// so "no hysteresis" means "one losing evaluation is enough" rather than "always committed".
		/// </summary>
		public static bool IsCommitted(int streak, int requiredStreak)
		{
			return streak >= Math.Max(1, requiredStreak);
		}

		/// <summary>Is <paramref name="band"/> set in a ready-band bitmask?</summary>
		public static bool IsBandReady(int readyBandMask, int band)
		{
			if (band < NuclearReleaseLadder.Lowest || band > NuclearReleaseLadder.Highest)
				return false;

			return (readyBandMask & (1 << band)) != 0;
		}

		/// <summary>Set a band in a ready-band bitmask. Out-of-range bands are ignored, not shifted.</summary>
		public static int WithBand(int readyBandMask, int band)
		{
			if (band < NuclearReleaseLadder.Lowest || band > NuclearReleaseLadder.Highest)
				return readyBandMask;

			return readyBandMask | (1 << band);
		}

		/// <summary>
		/// The highest ready band at or below <paramref name="cap"/>, or <c>Hold</c> when none is.
		/// Walks downward, so "the biggest thing it may fire" is one loop and not a sort.
		/// </summary>
		public static int HighestReadyAtOrBelow(int readyBandMask, int cap)
		{
			if (cap > NuclearReleaseLadder.Highest)
				cap = NuclearReleaseLadder.Highest;

			for (var band = cap; band > (int)NuclearRung.Hold; band--)
				if (IsBandReady(readyBandMask, band))
					return band;

			return (int)NuclearRung.Hold;
		}

		/// <summary>
		/// <para>THE POLICY. Everything the bot decides about firing is these twenty lines.</para>
		///
		/// <para>The order of the gates is load-bearing and is not the order they were written in:</para>
		/// <list type="number">
		/// <item>THE FINAL EXCHANGE FIRST, ahead of every other gate including the losing test and the
		/// rate limit. Once <c>DoomsdayStrike</c> has opened that window the match is already ending —
		/// Dead Hand places for anyone who does not (DoomsdayStrike.cs, the fifteen seconds) — so
		/// declining here does not save the game, it only forfeits the bot's own aim points to the
		/// automatic placement. <c>mayFireGameEnder</c> deliberately does NOT apply: that flag governs
		/// STARTING an apocalypse, and this one has started.</item>
		/// <item>NOT LOSING IS THE WHOLE RULE and it is checked before anything about what is ready. A
		/// winning bot that happens to hold a loaded warhead must read as NotLosing, not as
		/// NoReadyBand.</item>
		/// <item>THE WINDOW BEFORE THE PERMANENT BAND. The ruling's model expects the loser to escalate:
		/// a retaliation grant is the biggest thing a side may fire and it is ONE SHOT that vanishes
		/// with the window, so a bot that spent the beat on its 1 kt permanent band instead would be
		/// throwing the reply away.</item>
		/// </list>
		///
		/// <para>A GAME-ENDER IS REACHABLE HERE ONLY THROUGH THE WINDOW, which mirrors
		/// NuclearExchangeState's rule 5 rather than relying on it: the permanent branch caps at
		/// HundredKiloton EXPLICITLY, exactly as that file caps its own permanent raise and for the
		/// reason it gives — writing the cap as a consequence of the other rules was a bug its fixture
		/// caught.</para>
		/// </summary>
		/// <param name="released">Has the release gate opened (<c>NuclearExchange.Released</c>)?</param>
		/// <param name="losingCommitted">Has the losing streak reached the hysteresis count?</param>
		/// <param name="finalExchangeOpen">Is <c>DoomsdayStrike.FinalExchangeOpen</c>?</param>
		/// <param name="permanentLevel">This side's <c>PermanentLevelFor</c>.</param>
		/// <param name="windowLevel">This side's <c>WindowLevelFor</c>.</param>
		/// <param name="windowTicksRemaining">This side's <c>WindowTicksRemainingFor</c>. 0 is shut.</param>
		/// <param name="readyBandMask">Bands with a warhead ready right now, as a bitmask.</param>
		/// <param name="ticksSinceLastLaunch">Ticks since this module last queued a launch order.</param>
		/// <param name="minTicksBetweenLaunches">The rate limit.</param>
		/// <param name="mayFireGameEnder">May the bot START an apocalypse from a window grant?</param>
		public static NuclearBotDecision Choose(
			bool released,
			bool losingCommitted,
			bool finalExchangeOpen,
			int permanentLevel,
			int windowLevel,
			int windowTicksRemaining,
			int readyBandMask,
			int ticksSinceLastLaunch,
			int minTicksBetweenLaunches,
			bool mayFireGameEnder)
		{
			// (1) The apocalypse is already running. See the remarks: this is placement, not a decision.
			if (finalExchangeOpen)
				return IsBandReady(readyBandMask, (int)NuclearRung.GameEnder)
					? new NuclearBotDecision((int)NuclearRung.GameEnder, NuclearBotReason.FinalExchange)
					: NuclearBotDecision.Hold(NuclearBotReason.NoReadyBand);

			if (!released)
				return NuclearBotDecision.Hold(NuclearBotReason.NotReleased);

			// (2) THE USER'S RULE, and it is checked before readiness on purpose.
			if (!losingCommitted)
				return NuclearBotDecision.Hold(NuclearBotReason.NotLosing);

			if (ticksSinceLastLaunch < minTicksBetweenLaunches)
				return NuclearBotDecision.Hold(NuclearBotReason.RateLimited);

			// (3) The retaliation window: the biggest thing this side may fire, and one shot only.
			var windowOpen = windowTicksRemaining > 0 && windowLevel > (int)NuclearRung.Hold;
			var gameEnderWithheld = false;
			if (windowOpen && IsBandReady(readyBandMask, windowLevel))
			{
				if (windowLevel >= (int)NuclearRung.GameEnder && !mayFireGameEnder)
					gameEnderWithheld = true;
				else
					return new NuclearBotDecision(windowLevel, NuclearBotReason.Retaliation);
			}

			// (4) Otherwise the highest permanent band that is ready. NEVER a game-ender — the cap is
			// written here rather than inherited; see the remarks.
			var permanent = HighestReadyAtOrBelow(
				readyBandMask, Math.Min(permanentLevel, (int)NuclearRung.HundredKiloton));

			if (permanent > (int)NuclearRung.Hold)
				return new NuclearBotDecision(permanent, NuclearBotReason.Permanent);

			return NuclearBotDecision.Hold(
				gameEnderWithheld ? NuclearBotReason.GameEnderWithheld : NuclearBotReason.NoReadyBand);
		}

		/// <summary>
		/// <para>The index of the candidate whose CELL is the best aim point: the one maximising the
		/// summed value of every candidate within <paramref name="radiusCells"/> of it. -1 when there
		/// is nothing to shoot at.</para>
		///
		/// <para>AIM POINTS ARE CANDIDATE CELLS, NOT A GRID SWEEP. The engine's generic
		/// <c>SupportPowerBotModule</c> scans the map in chunks and then fine-scans the winner, which
		/// costs a full map walk per ready power and ends in a <c>Shuffle(world.LocalRandom)</c>.
		/// Anchoring on the candidates themselves is cheaper (O(n²) in the candidate count, and the
		/// candidate count is the believed-contact list), draws nothing, and cannot pick a cell with
		/// nothing on it — the optimum of a radial sum is always within a radius of some member, so the
		/// only thing given up is the sub-radius offset that would centre the disc on a cluster's
		/// centroid rather than on one of its members.</para>
		///
		/// <para>TIES BREAK ON THE LOWER ACTOR ID and nothing else, so a caller handing the same list in
		/// any order gets the same cell.</para>
		/// </summary>
		/// <param name="candidates">Enemy positions. May be null or empty.</param>
		/// <param name="radiusCells">The warhead's effect radius, in cells. Clamped to at least 0.</param>
		/// <param name="supplyRouteBonusPercent">
		/// Percentage applied to an aim point whose disc contains the enemy Supply Route. 100 is
		/// inert; below 100 actively avoids one.
		/// </param>
		public static int PickAimIndex(
			IReadOnlyList<NuclearTargetCandidate> candidates, int radiusCells, int supplyRouteBonusPercent)
		{
			if (candidates == null || candidates.Count == 0)
				return -1;

			var best = -1;
			var bestScore = long.MinValue;
			var bestKey = uint.MaxValue;

			for (var i = 0; i < candidates.Count; i++)
			{
				var score = ScoreAt(candidates, i, radiusCells, supplyRouteBonusPercent);
				var key = candidates[i].Key;

				if (score > bestScore || (score == bestScore && key < bestKey))
				{
					best = i;
					bestScore = score;
					bestKey = key;
				}
			}

			return best;
		}

		/// <summary>
		/// The score of putting the aim point on candidate <paramref name="index"/>'s cell. Public
		/// because the module logs it and the fixture pins it.
		/// </summary>
		public static long ScoreAt(
			IReadOnlyList<NuclearTargetCandidate> candidates, int index, int radiusCells, int supplyRouteBonusPercent)
		{
			if (candidates == null || index < 0 || index >= candidates.Count)
				return 0;

			if (radiusCells < 0)
				radiusCells = 0;

			var centre = candidates[index].Cell;
			var radiusSquared = (long)radiusCells * radiusCells;

			var total = 0L;
			var hitsSupplyRoute = false;
			for (var j = 0; j < candidates.Count; j++)
			{
				var c = candidates[j];
				var dx = (long)c.Cell.X - centre.X;
				var dy = (long)c.Cell.Y - centre.Y;
				if (dx * dx + dy * dy > radiusSquared)
					continue;

				total += c.Value;
				if (c.IsSupplyRoute)
					hitsSupplyRoute = true;
			}

			if (hitsSupplyRoute && supplyRouteBonusPercent != 100)
				total = total * supplyRouteBonusPercent / 100;

			return total;
		}

		/// <summary>
		/// <para>Up to <paramref name="count"/> aim points for a MULTI-WARHEAD power, best first, each at
		/// least <paramref name="minSeparationCells"/> from every point already taken. Returns the
		/// candidate indices; the caller turns them into cells.</para>
		///
		/// <para>THE SEPARATION IS THE WHOLE POINT. Six warheads picked by score alone all land on the
		/// same cluster, because the six highest-scoring discs on any cluster overlap almost completely
		/// — that is six warheads doing slightly more than one. Greedy-with-exclusion is the cheapest
		/// thing that does not do that, and it is deterministic: each pick is a
		/// <see cref="PickAimIndex"/> over the surviving candidates, so the same tie-break applies at
		/// every step.</para>
		///
		/// <para>EVERY POINT AFTER THE FIRST IS ALSO BOUNDED to <paramref name="maxSpreadCells"/> of the
		/// first, because <c>MissileStrikePowerInfo.MaxAimPointSpread</c> CLAMPS a decoded order and a
		/// clamped point is not the point that was scored. Passing 0 means unbounded, matching that
		/// field's own sentinel.</para>
		/// </summary>
		public static int[] PickAimIndices(
			IReadOnlyList<NuclearTargetCandidate> candidates,
			int radiusCells,
			int supplyRouteBonusPercent,
			int count,
			int minSeparationCells,
			int maxSpreadCells)
		{
			if (candidates == null || candidates.Count == 0 || count <= 0)
				return Array.Empty<int>();

			var taken = new List<int>(count);
			var used = new bool[candidates.Count];
			var minSeparationSquared = (long)Math.Max(0, minSeparationCells) * Math.Max(0, minSeparationCells);
			var maxSpreadSquared = (long)Math.Max(0, maxSpreadCells) * Math.Max(0, maxSpreadCells);

			while (taken.Count < count)
			{
				var best = -1;
				var bestScore = long.MinValue;
				var bestKey = uint.MaxValue;

				for (var i = 0; i < candidates.Count; i++)
				{
					if (used[i])
						continue;

					if (!IsSeparated(candidates, taken, i, minSeparationSquared))
						continue;

					if (taken.Count > 0 && maxSpreadSquared > 0
						&& !IsWithin(candidates[taken[0]].Cell, candidates[i].Cell, maxSpreadSquared))
						continue;

					var score = ScoreAt(candidates, i, radiusCells, supplyRouteBonusPercent);
					var key = candidates[i].Key;
					if (score > bestScore || (score == bestScore && key < bestKey))
					{
						best = i;
						bestScore = score;
						bestKey = key;
					}
				}

				if (best < 0)
					break;

				used[best] = true;
				taken.Add(best);
			}

			return taken.ToArray();
		}

		static bool IsSeparated(
			IReadOnlyList<NuclearTargetCandidate> candidates, List<int> taken, int index, long minSeparationSquared)
		{
			if (minSeparationSquared <= 0)
				return true;

			foreach (var t in taken)
				if (IsWithin(candidates[t].Cell, candidates[index].Cell, minSeparationSquared))
					return false;

			return true;
		}

		static bool IsWithin(CPos a, CPos b, long radiusSquared)
		{
			var dx = (long)a.X - b.X;
			var dy = (long)a.Y - b.Y;
			return dx * dx + dy * dy <= radiusSquared;
		}
	}
}
