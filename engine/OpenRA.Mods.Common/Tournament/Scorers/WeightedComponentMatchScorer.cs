#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — default scorer.
 *
 * Score = ArmyValueWeight × army_value
 *       + CaptureIncomeWeight × capture_income
 *       + KillsValueWeight × kills_value
 *
 * army_value     = sum of Valued.Cost over all alive owned actors
 * capture_income = cumulative cash from captured income-providing structures
 *                  (tracked by the watcher; 0 in Phase 1 — wiring TBD)
 * kills_value    = cumulative Valued.Cost of enemy actors killed by this player
 *                  (tracked by the watcher; 0 in Phase 1 — wiring TBD)
 *
 * Phase 1 limitation: only army_value is non-zero. Capture/kills tracking lands
 * when we hook the relevant engine events. This is the swap point for richer
 * scoring — register a new IMatchScorer in MatchHarness with a different
 * formula and reference it from tournament.yaml.
 */
#endregion

using System.Linq;
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

			var armyValue = ComputeArmyValue(player, world);
			var captureIncome = state.CaptureIncomeFor(player);
			var killsValue = state.KillsValueFor(player);

			snapshot.Components["army_value"] = (long)(armyValue * config.Score.ArmyValueWeight);
			snapshot.Components["capture_income"] = (long)(captureIncome * config.Score.CaptureIncomeWeight);
			snapshot.Components["kills_value"] = (long)(killsValue * config.Score.KillsValueWeight);

			long total = 0;
			foreach (var v in snapshot.Components.Values)
				total += v;
			snapshot.Total = total;

			return snapshot;
		}

		static long ComputeArmyValue(Player player, World world)
		{
			long sum = 0;
			foreach (var actor in world.Actors.Where(a => a.Owner == player && !a.IsDead && a.IsInWorld))
			{
				var valued = actor.Info.TraitInfoOrDefault<ValuedInfo>();
				if (valued != null)
					sum += valued.Cost;
			}

			return sum;
		}
	}
}
