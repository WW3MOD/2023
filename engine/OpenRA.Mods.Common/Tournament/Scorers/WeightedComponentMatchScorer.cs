#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — default scorer.
 *
 * Score = ArmyValueWeight × army_value
 *       + CaptureIncomeWeight × capture_income_gross
 *       + KillsValueWeight × kills_value
 *
 * army_value     = current army value, from PlayerStatistics.ArmyValue.
 *                  This is sum of UpdatesPlayerStatistics-tagged actors' costs
 *                  the player currently owns and that contribute to army value
 *                  (excludes buildings — that's by design; buildings count
 *                  via AssetsValue but we deliberately score on army size).
 * capture_income = cumulative GROSS building income (pre-upkeep) the player has
 *                  been granted by income structures they own/capture — the same
 *                  value the watcher emits as stats.capture_income_gross, read
 *                  from MatchTrackingState.GrossCaptureIncomeFor.
 *                  This REPLACES the former net PlayerResources.Earned source
 *                  (LADDER S1 follow-up 1a). Net Earned only rises on a
 *                  net-positive periodic economy tick, so in the SR-budget economy
 *                  it is structurally blind to a captured derrick whose gross
 *                  income doesn't overcome standing upkeep — it read 0 even when a
 *                  bot genuinely captured and held a derrick. Reading the gross
 *                  integral instead makes the economy axis actually count held
 *                  capture income in match outcomes (this scorer feeds the WinRule).
 *                  Component is still named "capture_income" to preserve the
 *                  tournament.yaml weight key.
 * kills_value    = cumulative Valued.Cost of enemies killed, from
 *                  PlayerStatistics.KillsCost. Wired automatically via
 *                  UpdatesPlayerStatistics on every combatant.
 *
 * Why we read PlayerStatistics instead of hooking events ourselves:
 * UpdatesPlayerStatistics is already attached to every combatant in WW3MOD
 * (via ^Combatant base templates and explicit declarations on tech buildings),
 * so PlayerStatistics tracks kills/deaths/income for free. Hooking our own
 * INotifyKilled/INotifyOwnerChanged would duplicate this and need careful YAML
 * wiring on every actor we care about.
 *
 * Swap point: implement IMatchScorer with a different formula (e.g. percentage
 * of map controlled, time-to-first-aggression, supply-route-contestation-
 * duration) and register in MatchHarness.
 */
#endregion

using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Tournament.Scorers
{
	public class WeightedComponentMatchScorer : IMatchScorer
	{
		readonly TournamentConfig config;

		public WeightedComponentMatchScorer(TournamentConfig config)
		{
			this.config = config;
		}

		public MatchScoreSnapshot ComputeScore(Player player, World world, MatchTrackingState state)
		{
			var stats = player.PlayerActor.TraitOrDefault<PlayerStatistics>();

			// Per-component reads. Stats may be null on players without PlayerStatistics
			// (Neutral, Everyone) — treat as zero. The economy term is the GROSS building
			// income integrated by the watcher (state.GrossCaptureIncomeFor), NOT net
			// PlayerResources.Earned: net Earned is blind to a held derrick whose gross
			// income doesn't overcome upkeep in the SR-budget economy (LADDER S1 1a).
			var armyValue = stats?.ArmyValue ?? 0;
			var captureIncomeGross = state.GrossCaptureIncomeFor(player);
			var killsCost = stats?.KillsCost ?? 0;

			return WeightedComponentScoring.Compute(armyValue, captureIncomeGross, killsCost, config.Score);
		}
	}

	/// <summary>
	/// Pure weighting math for the default scorer, factored out so it can be unit-tested
	/// without a World/Actor (the trait reads that feed it are validated separately).
	/// </summary>
	public static class WeightedComponentScoring
	{
		public static MatchScoreSnapshot Compute(long armyValue, long captureIncomeGross, long killsValue, TournamentConfig.ScoreConfig weights)
		{
			var snapshot = new MatchScoreSnapshot();
			snapshot.Components["army_value"] = (long)(armyValue * weights.ArmyValueWeight);
			snapshot.Components["capture_income"] = (long)(captureIncomeGross * weights.CaptureIncomeWeight);
			snapshot.Components["kills_value"] = (long)(killsValue * weights.KillsValueWeight);

			long total = 0;
			foreach (var v in snapshot.Components.Values)
				total += v;
			snapshot.Total = total;

			return snapshot;
		}
	}
}
