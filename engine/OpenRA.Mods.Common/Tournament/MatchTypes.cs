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

		// --- Option 4.D instrumentation (verdict_version 6) — OBSERVATION-ONLY. ---
		// All fields below are written exclusively by BotVsBotMatchWatcher's read-only poll/
		// sampling passes and read back only at serialization. Nothing here feeds a scorer or
		// win rule, draws RNG, or mutates sim state (see spec §5 / influence-stack.md §Invariants).

		/// <summary>Current believed owner of each tracked income POI (a CashTrickler-bearing
		/// actor), keyed by ActorID. Seeded at first tick, updated each tick by PollPoiOwnership.</summary>
		public readonly Dictionary<uint, Player> PoiOwner = new Dictionary<uint, Player>();

		/// <summary>ActorType name (oilb/bio/fcom) of each tracked POI, keyed by ActorID.</summary>
		public readonly Dictionary<uint, string> PoiType = new Dictionary<uint, string>();

		/// <summary>Per-POI set of tracked bots that have previously owned it — drives the
		/// `recapture` classification (spec §2.1).</summary>
		public readonly Dictionary<uint, HashSet<Player>> PoiPastBotOwners = new Dictionary<uint, HashSet<Player>>();

		/// <summary>The set of ActorType names that carry a CashTrickler (the income-POI set),
		/// seeded from live actors at first tick. This is the single source of truth for the
		/// per-POI income filter, so it can never drift from the capturable set — closing the
		/// spec §6 "single biggest risk" (misattributed income if a hardcoded triple diverges
		/// from CapturableActorTypes).</summary>
		public readonly HashSet<string> PoiActorTypes = new HashSet<string>();

		/// <summary>Two-sided capture-event stream, appended in tick order (spec §2.1).</summary>
		public readonly List<PoiCaptureEvent> CaptureEvents = new List<PoiCaptureEvent>();

		/// <summary>Live-accumulated hold time: total ticks each tracked bot has currently owned
		/// POIs, summed over all POIs (spec §2.3 H1 discriminator).</summary>
		public readonly Dictionary<Player, long> PoiHoldTicks = new Dictionary<Player, long>();

		/// <summary>Per-player integrator of POI-ONLY gross income — the POI-filtered twin of
		/// GrossCaptureIncome (spec §2.2/§3.2). Same read-only integrator, fed a filtered rate.</summary>
		public readonly Dictionary<Player, GrossIncomeIntegrator> PoiIncome = new Dictionary<Player, GrossIncomeIntegrator>();

		/// <summary>Per-player income timeseries samples (spec §2.2).</summary>
		public readonly Dictionary<Player, List<IncomeSample>> IncomeSamples = new Dictionary<Player, List<IncomeSample>>();

		public long CaptureIncomeFor(Player p) => CaptureIncome.TryGetValue(p, out var v) ? v : 0;
		public long KillsValueFor(Player p) => KillsValue.TryGetValue(p, out var v) ? v : 0;

		public long GrossCaptureIncomeFor(Player p) => GrossCaptureIncome.TryGetValue(p, out var v) ? v.Value : 0;

		public long PoiHoldTicksFor(Player p) => PoiHoldTicks.TryGetValue(p, out var v) ? v : 0;
		public long PoiIncomeFor(Player p) => PoiIncome.TryGetValue(p, out var v) ? v.Value : 0;
	}

	/// <summary>
	/// One ownership transition of an income POI (spec §2.1). A plain data record appended to
	/// MatchTrackingState.CaptureEvents by the watcher's read-only poll; never read by sim code.
	/// Owner fields are ClientIndex, or -1 for Neutral (and for the vacated side of a `destroyed`).
	/// </summary>
	public struct PoiCaptureEvent
	{
		public int Tick;
		public uint PoiId;
		public string PoiType;
		public int OldOwner;
		public int NewOwner;
		public string Event;
	}

	/// <summary>
	/// One income timeseries sample for a player (spec §2.2). Appended every SampleInterval ticks.
	/// </summary>
	public struct IncomeSample
	{
		public int Tick;
		public long IncomeRate;    // Σ AmountPerInterval over derrick ActorTypes at this tick
		public long IncomeGross;   // cumulative POI-only gross to this tick
		public int PoiCount;       // POIs currently owned by the player
	}

	/// <summary>
	/// Pure classification of a POI ownership transition into the spec §2.1 event label. Kept
	/// World/Actor-free so it is NUnit-pinnable without a game run. Precedence is
	/// recapture &gt; capture &gt; steal: a bot re-taking a POI it previously held is the
	/// meaningful churn signal (Option B "loses then re-takes"), so it wins over the generic
	/// steal label even when the intervening holder was another bot.
	/// </summary>
	public static class PoiEventClassifier
	{
		public const string Capture = "capture";
		public const string Steal = "steal";
		public const string Recapture = "recapture";
		public const string Destroyed = "destroyed";

		/// <param name="oldWasNeutral">Previous owner was Neutral / a non-combatant.</param>
		/// <param name="newPreviouslyOwned">New owner is a tracked bot that previously held this POI.</param>
		/// <param name="oldWasTrackedBot">Previous owner was a tracked bot different from the new owner.</param>
		public static string Classify(bool oldWasNeutral, bool newPreviouslyOwned, bool oldWasTrackedBot)
		{
			if (newPreviouslyOwned)
				return Recapture;
			if (oldWasNeutral)
				return Capture;
			if (oldWasTrackedBot)
				return Steal;

			// Conservative default: a transition from a non-neutral, non-tracked holder does not
			// occur in the WW3MOD model (POIs start Neutral and only bots capture). Treat as a
			// first acquisition rather than inventing a label.
			return Capture;
		}
	}

	/// <summary>Per-side scalar rollups (spec §2.3) derived purely from a capture-event stream.
	/// The H1 (hold) discriminator lives in the live PoiHoldTicks accumulator; the H2
	/// (time-to-first-capture) discriminator and the event tallies are computed here.</summary>
	public struct PoiRollup
	{
		public int FirstCaptureTick;   // tick of this side's first capture/steal/recapture, -1 if none
		public int Captures;
		public int Steals;
		public int Recaptures;
		public int Losses;             // events where this side was the OldOwner (lost the POI)

		/// <summary>Reduce the event stream for one player (by ClientIndex). Pure over the recorded
		/// stream — no World/Actor reads — so it is NUnit-pinnable.</summary>
		public static PoiRollup Compute(IReadOnlyList<PoiCaptureEvent> events, int clientIndex)
		{
			var r = new PoiRollup { FirstCaptureTick = -1 };
			if (events == null)
				return r;

			foreach (var ev in events)
			{
				if (ev.NewOwner == clientIndex)
				{
					switch (ev.Event)
					{
						case PoiEventClassifier.Capture: r.Captures++; break;
						case PoiEventClassifier.Steal: r.Steals++; break;
						case PoiEventClassifier.Recapture: r.Recaptures++; break;
					}

					if (ev.Event != PoiEventClassifier.Destroyed
						&& (r.FirstCaptureTick < 0 || ev.Tick < r.FirstCaptureTick))
						r.FirstCaptureTick = ev.Tick;
				}

				if (ev.OldOwner == clientIndex)
					r.Losses++;
			}

			return r;
		}
	}

	/// <summary>
	/// <para>Read-only integrator for a player's GROSS building income — the cumulative cash
	/// that income structures (CashTrickler-bearing buildings) they own or capture have
	/// granted them over a match, ignoring upkeep.</para>
	///
	/// <para>The unified economy pays out PlayerResources.TotalBuildingIncome (the sum of the
	/// currently-owned income entries, before upkeep) once every PassiveIncomeInterval
	/// ticks. Observing that value each tick and integrating the per-tick rate
	/// (TotalBuildingIncome / PassiveIncomeInterval) reconstructs the cumulative gross
	/// grant without ever touching sim state — the watcher only READS TotalBuildingIncome
	/// and writes to this accumulator, so it cannot affect determinism or the experiment.</para>
	///
	/// <para>It is naturally robust to mid-match ownership changes: CashTrickler re-registers
	/// under the new owner on capture (INotifyOwnerChanged), so TotalBuildingIncome — and
	/// therefore this integral — follows whoever currently owns each structure.</para>
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
