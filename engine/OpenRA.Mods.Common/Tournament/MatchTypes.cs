#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — common data types.
 *
 * Plugged into the engine via BotVsBotMatchWatcher (a world trait). The watcher
 * delegates scoring and win-rule evaluation to interfaces in this folder so
 * either side can be swapped without touching the trait itself.
 *
 * Adding a new scorer or win rule: drop a new file in Scorers/ or WinRules/,
 * register it in MatchHarness, reference it by name from tournament.yaml.
 *
 * See WORKSPACE/ai/tournament_swap_guide.md for the swap pattern.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Tournament
{
	/// <summary>
	/// One player's score at a single tick. Components are arbitrary named buckets
	/// (army_value, capture_income, kills_value, etc.); Total is the weighted sum
	/// per the active scorer's formula.
	/// </summary>
	public class MatchScoreSnapshot
	{
		public readonly Dictionary<string, long> Components = new Dictionary<string, long>();
		public long Total;
	}

	/// <summary>
	/// Cumulative per-player state the watcher feeds to scorers and win rules.
	/// Updated by the watcher each tick from observed game state and events.
	/// </summary>
	public class MatchTrackingState
	{
		/// <summary>The SR actor that started the match owned by this player.
		/// Used by win rules to detect "their SR was captured / lost".</summary>
		public readonly Dictionary<Player, Actor> OriginalSrOwner = new Dictionary<Player, Actor>();

		/// <summary>Cumulative cash income from captured income-providing structures.</summary>
		public readonly Dictionary<Player, long> CaptureIncome = new Dictionary<Player, long>();

		/// <summary>Cumulative value of enemy actors killed (sum of their costs).</summary>
		public readonly Dictionary<Player, long> KillsValue = new Dictionary<Player, long>();

		/// <summary>Per-player integrator of GROSS building income (pre-upkeep) actually
		/// granted over the match. Read-only observer state — see GrossIncomeIntegrator.
		/// This is the S1 economy metric (emitted as capture_income_gross); it is NOT read
		/// by any scorer or win rule, so populating it cannot alter match outcomes.</summary>
		public readonly Dictionary<Player, GrossIncomeIntegrator> GrossCaptureIncome = new Dictionary<Player, GrossIncomeIntegrator>();

		public long CaptureIncomeFor(Player p) => CaptureIncome.TryGetValue(p, out var v) ? v : 0;
		public long KillsValueFor(Player p) => KillsValue.TryGetValue(p, out var v) ? v : 0;

		public long GrossCaptureIncomeFor(Player p) => GrossCaptureIncome.TryGetValue(p, out var v) ? v.Value : 0;
	}

	/// <summary>
	/// Read-only integrator for a player's GROSS building income — the cumulative cash
	/// that income structures (CashTrickler-bearing buildings) they own or capture have
	/// granted them over a match, ignoring upkeep.
	///
	/// The unified economy pays out PlayerResources.TotalBuildingIncome (the sum of the
	/// currently-owned income entries, before upkeep) once every PassiveIncomeInterval
	/// ticks. Observing that value each tick and integrating the per-tick rate
	/// (TotalBuildingIncome / PassiveIncomeInterval) reconstructs the cumulative gross
	/// grant without ever touching sim state — the watcher only READS TotalBuildingIncome
	/// and writes to this accumulator, so it cannot affect determinism or the experiment.
	///
	/// It is naturally robust to mid-match ownership changes: CashTrickler re-registers
	/// under the new owner on capture (INotifyOwnerChanged), so TotalBuildingIncome — and
	/// therefore this integral — follows whoever currently owns each structure.
	/// </summary>
	public class GrossIncomeIntegrator
	{
		double accumulated;

		/// <summary>Cumulative gross income granted so far, truncated to whole cash units.</summary>
		public long Value => (long)accumulated;

		/// <summary>Advance one tick. totalBuildingIncome is paid every passiveIncomeInterval
		/// ticks, so a single tick contributes totalBuildingIncome / passiveIncomeInterval.
		/// Non-positive income or interval contributes nothing.</summary>
		public void Tick(float totalBuildingIncome, int passiveIncomeInterval)
		{
			if (passiveIncomeInterval <= 0 || totalBuildingIncome <= 0f)
				return;

			accumulated += totalBuildingIncome / (double)passiveIncomeInterval;
		}
	}

	/// <summary>
	/// Final result of one match. Written to disk as JSON via BotVsBotMatchWatcher.
	/// </summary>
	public class MatchVerdict
	{
		public Player Winner;
		public string Reason;         // "sr_capture", "time_limit", "elimination", ...
		public int EndTick;
		public Dictionary<Player, MatchScoreSnapshot> Scores;
	}
}
