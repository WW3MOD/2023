#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — default scorer.
 *
 * Score = ArmyValueWeight × army_value
 *       + CaptureIncomeWeight × income_earned
 *       + KillsValueWeight × kills_value
 *
 * army_value     = current army value, from PlayerStatistics.ArmyValue.
 *                  This is sum of UpdatesPlayerStatistics-tagged actors' costs
 *                  the player currently owns and that contribute to army value
 *                  (excludes buildings — that's by design; buildings count
 *                  via AssetsValue but we deliberately score on army size).
 * income_earned  = cumulative cash earned, from PlayerStatistics.resources.Earned
 *                  (proxied via PlayerStatistics public fields). Captures the
 *                  "play for objectives" axis since WW3MOD income is almost
 *                  entirely from captured CashTrickler structures (oilb, bio,
 *                  miss, fcom, hosp) plus passive starting income.
 *                  Component is named "capture_income" in the snapshot to
 *                  preserve the tournament.yaml weight key, but the underlying
 *                  source is total earned, not capture-specific.
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
			var snapshot = new MatchScoreSnapshot();
			var stats = player.PlayerActor.TraitOrDefault<PlayerStatistics>();
			var resources = player.PlayerActor.TraitOrDefault<PlayerResources>();

			// Per-component reads. Stats / resources may be null on players without
			// PlayerStatistics / PlayerResources attached (Neutral, Everyone) — treat as zero.
			// PITFALL: PlayerStatistics.Income is a 60-sec rolling window, not cumulative.
			// Use PlayerResources.Earned for total earnings since match start.
			var armyValue = stats?.ArmyValue ?? 0;
			var earnedTotal = resources?.Earned ?? 0;
			var killsCost = stats?.KillsCost ?? 0;

			snapshot.Components["army_value"] = (long)(armyValue * config.Score.ArmyValueWeight);
			snapshot.Components["capture_income"] = (long)(earnedTotal * config.Score.CaptureIncomeWeight);
			snapshot.Components["kills_value"] = (long)(killsCost * config.Score.KillsValueWeight);

			long total = 0;
			foreach (var v in snapshot.Components.Values)
				total += v;
			snapshot.Total = total;

			return snapshot;
		}
	}
}
