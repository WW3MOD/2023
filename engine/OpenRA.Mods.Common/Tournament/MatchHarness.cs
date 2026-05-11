#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — scorer/win-rule registry.
 *
 * Adding a new scorer:
 *   1. Drop a class in Tournament/Scorers/ implementing IMatchScorer.
 *   2. Add a RegisterScorer line in the static constructor below.
 *   3. Reference it by name from tournament.yaml's "Scorer:" field.
 *
 * Adding a new win rule: same pattern in Tournament/WinRules/.
 *
 * Static registry deliberately chosen over OpenRA's trait-based plugin system —
 * scorers/win rules aren't per-world configuration, they're match-config-driven
 * choices. Trait approach would force adding YAML wiring per variant.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Mods.Common.Tournament.Scorers;
using OpenRA.Mods.Common.Tournament.WinRules;

namespace OpenRA.Mods.Common.Tournament
{
	public static class MatchHarness
	{
		static readonly Dictionary<string, Func<TournamentConfig, IMatchScorer>> ScorerFactories
			= new Dictionary<string, Func<TournamentConfig, IMatchScorer>>(StringComparer.OrdinalIgnoreCase);

		static readonly Dictionary<string, Func<TournamentConfig, IWinRuleEvaluator>> WinRuleFactories
			= new Dictionary<string, Func<TournamentConfig, IWinRuleEvaluator>>(StringComparer.OrdinalIgnoreCase);

		static MatchHarness()
		{
			RegisterScorer("weighted_components", c => new WeightedComponentMatchScorer(c));
			RegisterWinRule("score_or_sr_capture", c => new TimeOrSrCaptureWinRule(c));
		}

		public static void RegisterScorer(string name, Func<TournamentConfig, IMatchScorer> factory)
		{
			ScorerFactories[name] = factory;
		}

		public static void RegisterWinRule(string name, Func<TournamentConfig, IWinRuleEvaluator> factory)
		{
			WinRuleFactories[name] = factory;
		}

		public static IMatchScorer CreateScorer(string name, TournamentConfig config)
		{
			if (!ScorerFactories.TryGetValue(name, out var factory))
				throw new InvalidOperationException(
					$"Unknown scorer '{name}'. Registered: {string.Join(", ", ScorerFactories.Keys)}");
			return factory(config);
		}

		public static IWinRuleEvaluator CreateWinRule(string name, TournamentConfig config)
		{
			if (!WinRuleFactories.TryGetValue(name, out var factory))
				throw new InvalidOperationException(
					$"Unknown win rule '{name}'. Registered: {string.Join(", ", WinRuleFactories.Keys)}");
			return factory(config);
		}
	}
}
