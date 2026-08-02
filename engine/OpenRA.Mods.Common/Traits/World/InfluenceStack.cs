#region Copyright & License Information
/*
 * WW3MOD influence stack — shared participation + staggering seam (Stage C, §6 perf guardrail).
 *
 * The Stage-A/B/C world layers (BeliefStore, DangerFieldLayer, ControlField) are per-player
 * and, before Stage C, recomputed EVERY combatant's field on a single tick. Two of those
 * costs are avoidable:
 *   1. NARROW — only the players that actually READ the stack need a field: the @experimental
 *      bots (their strategy consumes it from Stage D) and the human who may open the overlay.
 *      "The human" is resolved here as a SIM-LEGAL proxy — any playable, non-bot combatant —
 *      NOT world.RenderPlayer. Reading the render player to decide what to simulate would make
 *      simulation depend on the render path and desync; the hard wall in §3 forbids it. So we
 *      compute for every human combatant (usually exactly one) and let the render-only overlay
 *      pick RenderPlayer among them.
 *   2. STAGGER — spread the per-player recomputes across the update interval so no single tick
 *      rebuilds every participant's field (§6). Each layer round-robins one participant per
 *      sub-slot; over UpdateInterval ticks every participant is refreshed exactly once.
 *
 * Keeping this in one place means the three layers narrow to the SAME participant set, so a
 * field a consumer reads is always populated by the same rule that let the consumer run.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class InfluenceStack
	{
		// ai.yaml gives the Experimental AI `Type: experimental` and the Stable AI `Type: stable`.
		// Both fog-respecting bots read the influence stack; any other bot profile never reads it
		// and stays byte-identical. (2026-08-02: @stable was promoted to full @experimental parity,
		// so it now participates too — its ai.yaml twins set the same fog-legal consumer flags, which
		// are inert without a field. See ai.yaml Stable-block header + DOCS/reference/influence-stack.md.)
		public const string ExperimentalBotType = "experimental";
		public const string StableBotType = "stable";

		/// <summary>Does this player read the influence stack? The fog-respecting bots
		/// (@experimental + @stable, Stage-D strategy) + human combatants (the sim-legal stand-in
		/// for "the overlay viewer"). Never reads RenderPlayer, so no simulation state depends on
		/// the render path.</summary>
		public static bool Participates(Player player)
		{
			if (player == null || player.NonCombatant || player.Spectating)
				return false;

			if (player.IsBot)
				return player.BotType == ExperimentalBotType || player.BotType == StableBotType;

			// Human combatant. Playable rules out dedicated observers.
			return player.Playable;
		}

		/// <summary>Fills `into` (cleared first) with the current participants, in the stable
		/// world player order — deterministic across clients.</summary>
		public static void GatherParticipants(World world, List<Player> into)
		{
			into.Clear();
			foreach (var player in world.Players)
				if (Participates(player))
					into.Add(player);
		}

		/// <summary>Ticks between processing one participant so that `count` of them spread
		/// evenly across `interval` ticks (one per sub-slot). Never less than 1.</summary>
		public static int SubInterval(int interval, int count)
		{
			if (count <= 0)
				return interval < 1 ? 1 : interval;

			var sub = interval / count;
			return sub < 1 ? 1 : sub;
		}
	}
}
