#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Pure arithmetic behind accumulated purchase ranks. Every method here is ints in, ints out,
	/// with no World, no Actor and no RNG, so the whole state machine is covered by plain NUnit
	/// fixtures without standing up a simulation.
	/// </summary>
	public static class RankAccrual
	{
		/// <summary>Ranks 1-3 can be bought. Rank 4 is forged in combat only, never purchased.</summary>
		public const int MaxPurchasableRank = 3;

		/// <summary>
		/// Base queue delay for an actor, in ticks, before any live modifier.
		/// Mirrors ProductionQueue.GetBuildTime's BuildDuration == -1 fall-through (cost / 10) and
		/// then applies only BuildableInfo.BuildDurationModifier - the one per-actor deviation that
		/// is constant for the whole match. The live GetBuildTime path is deliberately NOT used:
		/// it varies with DeveloperMode.FastBuild and with producer counts, which would make the
		/// accrual interval move under the player mid-game.
		/// </summary>
		public static int BaseBuildTimeTicks(int cost, int buildDuration, int buildDurationModifier)
		{
			var time = buildDuration == -1 ? cost / 10 : buildDuration;
			return Math.Max(1, time * buildDurationModifier / 100);
		}

		/// <summary>
		/// Ticks between two grants of <paramref name="tier"/> for a unit whose base build time is
		/// <paramref name="buildTimeTicks"/>. Tier 1 is rank1Multiplier percent of build time; each
		/// step up multiplies by higherTierMultiplier percent.
		/// Linear in build time, and therefore linear in cost, which is the point: spending an equal
		/// budget across the roster accrues an equal amount of free rank whatever you spend it on.
		/// </summary>
		public static int IntervalTicks(int buildTimeTicks, int tier, int rank1Multiplier, int higherTierMultiplier)
		{
			if (tier < 1 || tier > MaxPurchasableRank)
				throw new ArgumentOutOfRangeException(nameof(tier));

			var interval = (long)Math.Max(1, buildTimeTicks) * rank1Multiplier / 100;
			for (var i = 1; i < tier; i++)
				interval = interval * higherTierMultiplier / 100;

			return (int)Math.Max(1, Math.Min(int.MaxValue, interval));
		}

		/// <summary>
		/// Advance one tier's timer to <paramref name="now"/>, granting stock as it crosses each
		/// interval boundary. A grant that lands on a full stock is discarded - the timer still
		/// advances, so nothing is banked for later. The timer never consults, and is never reset
		/// by, a purchase.
		/// </summary>
		public static void Advance(int now, int interval, int cap, ref int stock, ref int nextGrantTick)
		{
			while (now >= nextGrantTick)
			{
				if (stock < cap)
					stock++;

				nextGrantTick += interval;
			}
		}

		/// <summary>
		/// The rank a purchase would consume: the highest tier holding any stock, or 0 for none.
		/// <paramref name="stock"/> is indexed by tier - 1.
		/// </summary>
		public static int HighestHeldTier(IReadOnlyList<int> stock)
		{
			for (var tier = Math.Min(stock.Count, MaxPurchasableRank); tier >= 1; tier--)
				if (stock[tier - 1] > 0)
					return tier;

			return 0;
		}

		/// <summary>
		/// Ticks of progress a recovered crew member is worth: its share of the crew, measured
		/// against the full interval for its tier. A whole crew's shares sum to the interval, which
		/// is what makes returning everyone worth exactly one rank.
		/// </summary>
		public static int ShareTicks(int interval, int numerator, int denominator)
		{
			if (denominator <= 0 || numerator <= 0)
				return 0;

			return (int)Math.Min(int.MaxValue, (long)interval * numerator / denominator);
		}
	}

	/// <summary>
	/// Accrual state for a single actor type: one independent timer and one stock per tier.
	/// Holds no World reference, so it is directly testable.
	/// </summary>
	public sealed class UnitRankStock
	{
		/// <summary>Accrued from the wall clock. Capped.</summary>
		public readonly int[] Stock = new int[RankAccrual.MaxPurchasableRank];

		/// <summary>
		/// Earned by recovering units and crew alive. Deliberately NOT capped: you fielded and
		/// brought these home, so they are earned rather than accrued, and only recovery can push a
		/// type's holding above its cap.
		/// </summary>
		public readonly int[] BonusStock = new int[RankAccrual.MaxPurchasableRank];

		readonly int[] intervals = new int[RankAccrual.MaxPurchasableRank];
		readonly int[] nextGrantTick = new int[RankAccrual.MaxPurchasableRank];
		readonly int[] creditTicks = new int[RankAccrual.MaxPurchasableRank];
		readonly int[] caps;

		public UnitRankStock(int buildTimeTicks, int rank1Multiplier, int higherTierMultiplier, int[] caps)
		{
			this.caps = caps;
			for (var tier = 1; tier <= RankAccrual.MaxPurchasableRank; tier++)
			{
				var interval = RankAccrual.IntervalTicks(buildTimeTicks, tier, rank1Multiplier, higherTierMultiplier);
				intervals[tier - 1] = interval;

				// Seeded from tick 0, never from the tick this object happened to be constructed on,
				// so the schedule is a function of the rules alone.
				nextGrantTick[tier - 1] = interval;
			}
		}

		public int IntervalFor(int tier) => intervals[tier - 1];

		public void Advance(int now)
		{
			for (var tier = 1; tier <= RankAccrual.MaxPurchasableRank; tier++)
				RankAccrual.Advance(now, intervals[tier - 1], caps[tier - 1],
					ref Stock[tier - 1], ref nextGrantTick[tier - 1]);
		}

		/// <summary>Everything held of a tier, accrued plus recovered.</summary>
		public int Total(int tier) => Stock[tier - 1] + BonusStock[tier - 1];

		/// <summary>Highest tier held, without consuming it.</summary>
		public int Peek()
		{
			for (var tier = RankAccrual.MaxPurchasableRank; tier >= 1; tier--)
				if (Total(tier) > 0)
					return tier;

			return 0;
		}

		/// <summary>Consume one unit of <paramref name="tier"/>. Tier 0 is a no-op.</summary>
		public void Spend(int tier)
		{
			if (tier < 1 || tier > RankAccrual.MaxPurchasableRank)
				return;

			// Accrued stock first: it is the pool that sits against a cap, and draining it lets the
			// wall clock start granting again instead of idling full. Invisible to the player either
			// way - both pools spend as the same rank.
			if (Stock[tier - 1] > 0)
				Stock[tier - 1]--;
			else if (BonusStock[tier - 1] > 0)
				BonusStock[tier - 1]--;
		}

		/// <summary>A whole unit of this type came home alive at <paramref name="tier"/>.</summary>
		public void CreditWhole(int tier)
		{
			if (tier < 1 || tier > RankAccrual.MaxPurchasableRank)
				return;

			BonusStock[tier - 1]++;
		}

		/// <summary>
		/// One crew member of this vehicle type came home alive at <paramref name="tier"/>, worth
		/// numerator/denominator of the crew. Partial credit persists until it completes, so a crew
		/// recovered piecemeal across several wrecks still adds up.
		/// </summary>
		public void CreditShare(int tier, int numerator, int denominator)
		{
			if (tier < 1 || tier > RankAccrual.MaxPurchasableRank)
				return;

			var interval = intervals[tier - 1];
			creditTicks[tier - 1] += RankAccrual.ShareTicks(interval, numerator, denominator);

			while (creditTicks[tier - 1] >= interval)
			{
				creditTicks[tier - 1] -= interval;
				BonusStock[tier - 1]++;
			}
		}

		/// <summary>Progress banked toward the next recovered rank of this tier, in ticks.</summary>
		public int PendingCreditTicks(int tier) => creditTicks[tier - 1];
	}

	[TraitLocation(SystemActors.Player)]
	[Desc("Accumulates free veterancy ranks for each buildable combat unit type over time.",
		"Buying a unit spends the highest rank held; it never resets the timers, so ignoring a unit",
		"type is what lets its rank pile up. Attach to the Player actor.")]
	public class RankAccumulationInfo : TraitInfo
	{
		[Desc("Ticks between rank-1 grants, as a percentage of the unit's base build time.",
			"500 = every five build times. Build time is cost / 10 ticks, so this is linear in cost.")]
		public readonly int Rank1IntervalMultiplier = 500;

		[Desc("Each tier above 1 multiplies the previous tier's interval by this percentage.",
			"300 gives rank-2 at 15x build time and rank-3 at 45x.")]
		public readonly int HigherTierIntervalMultiplier = 300;

		[Desc("Maximum stock held per tier, lowest tier first. Must have exactly three entries;",
			"rank 4 is never purchasable.")]
		public readonly int[] Caps = { 3, 2, 1 };

		public override object Create(ActorInitializer init) { return new RankAccumulation(init.World, this); }
	}

	public class RankAccumulation : ITick
	{
		// Keyed by actor name. Nothing observable depends on this dictionary's enumeration order:
		// Tick advances each entry from the shared tick counter and each entry's own timers only,
		// so entries never interact and no aggregate is computed across them.
		readonly Dictionary<string, UnitRankStock> stocks = new();

		// Local lockstep counter rather than World.WorldTick, so the schedule cannot pick up any
		// offset from map setup. ITick runs in the synchronised simulation on every client.
		int ticks;

		public RankAccumulation(World world, RankAccumulationInfo info)
		{
			if (info.Caps.Length != RankAccrual.MaxPurchasableRank)
				throw new YamlException(
					$"{nameof(RankAccumulationInfo)}.{nameof(RankAccumulationInfo.Caps)} needs exactly " +
					$"{RankAccrual.MaxPurchasableRank} entries, got {info.Caps.Length}.");

			// Every entry is created up front from the rules, so no entry's schedule depends on when
			// it was first touched. Rules are identical on all clients.
			foreach (var actor in world.Map.Rules.Actors.Values)
			{
				// Rules.Actors carries the abstract ^Inherit templates alongside real actors.
				if (actor.Name.StartsWith("^", StringComparison.Ordinal))
					continue;

				if (!Accrues(actor))
					continue;

				var buildable = actor.TraitInfo<BuildableInfo>();
				var valued = actor.TraitInfoOrDefault<ValuedInfo>();
				var buildTime = RankAccrual.BaseBuildTimeTicks(
					valued?.Cost ?? 0, buildable.BuildDuration, buildable.BuildDurationModifier);

				stocks[actor.Name] = new UnitRankStock(buildTime,
					info.Rank1IntervalMultiplier, info.HigherTierIntervalMultiplier, info.Caps);
			}
		}

		/// <summary>
		/// Combat units only. GainsExperience is the gate rather than a hand-kept list: a rank is a
		/// bundle of combat multipliers, so on a unit that cannot hold veterancy at all the prize is
		/// inert - and this also guarantees we never hand levels to an actor lacking the trait.
		/// </summary>
		static bool Accrues(ActorInfo actor)
		{
			return actor.HasTraitInfo<BuildableInfo>() && actor.HasTraitInfo<GainsExperienceInfo>();
		}

		void ITick.Tick(Actor self)
		{
			ticks++;
			foreach (var stock in stocks.Values)
				stock.Advance(ticks);
		}

		/// <summary>Stock held of a tier (1-3) for an actor type, accrued plus recovered.
		/// Read-only; safe to call from render code.</summary>
		public int StockOf(string actorName, int tier)
		{
			if (tier < 1 || tier > RankAccrual.MaxPurchasableRank)
				return 0;

			return stocks.TryGetValue(actorName, out var stock) ? stock.Total(tier) : 0;
		}

		/// <summary>A whole unit of this type reached safety at this rank. Exempt from the cap.</summary>
		public void CreditWholeUnit(string actorName, int tier)
		{
			if (stocks.TryGetValue(actorName, out var stock))
				stock.CreditWhole(tier);
		}

		/// <summary>
		/// A crew member reached safety at this rank. Credits the vehicle type it came out of - not
		/// vehicles in general - by its share of that vehicle's crew.
		/// </summary>
		public void CreditCrewShare(string vehicleName, int tier, int numerator, int denominator)
		{
			if (stocks.TryGetValue(vehicleName, out var stock))
				stock.CreditShare(tier, numerator, denominator);
		}

		/// <summary>The rank the next purchase of this type would arrive at, or 0 for none.</summary>
		public int PeekRank(string actorName)
		{
			return stocks.TryGetValue(actorName, out var stock) ? stock.Peek() : 0;
		}

		/// <summary>
		/// Consume a rank previously reported by <see cref="PeekRank"/>. Kept separate from the peek
		/// so the caller only commits once the unit has actually been placed in the world - the
		/// production path can fail after choosing a rank, and a failed attempt must not burn stock.
		/// </summary>
		public void SpendRank(string actorName, int tier)
		{
			if (tier > 0 && stocks.TryGetValue(actorName, out var stock))
				stock.Spend(tier);
		}
	}
}
