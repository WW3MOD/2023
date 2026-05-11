#region Copyright & License Information
/*
 * WW3MOD AI tournament harness — scorer plug-in interface.
 */
#endregion

namespace OpenRA.Mods.Common.Tournament
{
	/// <summary>
	/// Computes a per-player score snapshot from current world state + tracked
	/// cumulative state. Implementations should be cheap — called every tick the
	/// watcher evaluates the win rule.
	///
	/// Register implementations in MatchHarness, then reference by name from
	/// tournament.yaml's "Scorer:" field.
	/// </summary>
	public interface IMatchScorer
	{
		MatchScoreSnapshot ComputeScore(Player player, World world, MatchTrackingState state);
	}
}
