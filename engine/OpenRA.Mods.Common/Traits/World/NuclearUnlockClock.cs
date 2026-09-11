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

/*
 * SKIRMISH'S NUCLEAR SHOP OPENS ON A CLOCK -- the two lobby options, and the reading of the match
 * clock that turns them into a rung.
 *
 * ==== WHAT THIS FIXES, AND WHY IT IS A LIE RATHER THAN A MISSING FEATURE ====
 * The lobby timeline shipped at 3a1780bc drawing an amber band captioned NUCLEAR WEAPONS
 * PURCHASABLE from ten minutes in. In Skirmish -- the DEFAULT game mode -- that band was false:
 * every buy-tier nuclear power is gated on `Prerequisites: powers.america` / `powers.russia`, a
 * FACTION gate with no time component anywhere in the tree, so nukes were on sale from the first
 * second. The user's ruling (decision 22) was to make the bar true rather than trim it.
 *
 * ==== IT HOLDS NO STATE, WHICH IS THE WHOLE DETERMINISM ARGUMENT ====
 * There is no counter here and nothing is [Sync]ed, because there is nothing to sync: the released
 * rung is a PURE FUNCTION of `world.WorldTick` and two lobby options.
 *
 *   - WorldTick is the match clock every client already agrees on; it is what TimeLimitManager reads
 *     for exactly this purpose (TimeLimitManager.cs:157).
 *   - LobbyInfo.GlobalSettings is session state agreed before the first tick, not client-local.
 *   - NuclearUnlockSchedule is integer throughout and draws no shared random number.
 *
 * So every client computes the same rung on the same tick without anything being written, which is a
 * stronger property than a synced counter and needs no annotation to hold. A future edit that adds a
 * field here loses it -- if you need one, read SyncAnnotationTest first and be sure it is not
 * client-local.
 *
 * ==== THREE WAYS TO BE SUSPENDED, AND ALL THREE MATTER ====
 *   1. DEFCON ESCALATION. Nothing is purchasable there at all -- every warhead is handed to a player
 *      by somebody's decision to fire (decisions 16 and 17.2). This clock is Skirmish's alone, and
 *      NuclearReleaseLadder remains the only thing that moves Escalation's rung. Escalation is
 *      byte-for-byte unaffected by this file.
 *   2. SANDBOX. It is the user's test mode and its own lobby description already promises "no
 *      waiting"; decision 01 fixes the boundary it may cross as DEAD AIR, and a shop that will not
 *      open for ten minutes is the purest dead air in the mod. This is also what keeps EIGHT nuke
 *      scenarios green with no edit to any of them: demo-nuke-arsenal, demo-highyield-nuke,
 *      demo-nuke-edge-band, demo-nuke-fog-seam, demo-nuke-river-zeta, demo-nuke-shroud-still-hides,
 *      test-heavy-strike-wrecks-economy and test-tacnuke-delivers all set
 *      PowersSandboxCheckboxEnabled: true and all fire inside the first three minutes. Without this
 *      clause every one of them would have stopped firing and reported nothing but a demo gone
 *      quiet -- the exact failure GrantConditionOnNuclearRelease's header warns about.
 *      (The ninth, test-tacnuke-lobby-gated-off, needs no exemption: its positive control is the
 *      Kinzhal, which is CONVENTIONAL and carries no NuclearYieldTons, so no band gates it.)
 *   3. AN INTERVAL OF "0". The host's own opt-out, and the pre-change behaviour exactly.
 *
 * In all three cases ReleasedRung reports the top of the ladder and this trait changes nothing.
 */

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Makes nuclear support powers become PURCHASABLE in ascending yield order as a Skirmish",
		"match runs, instead of all being on sale from the first second. Attach to the World actor.",
		"",
		"Read by " + nameof(GrantConditionOnNuclearRelease) + " on the player actor, which is what",
		"turns the released rung into the band conditions YAML already gates every warhead on. This",
		"trait gates nothing by itself and grants no conditions.",
		"",
		"A STRICT NO-OP IN DEFCON ESCALATION AND IN SANDBOX -- see the file header for all three",
		"suspension cases and why each one is load-bearing.")]
	public class NuclearUnlockClockInfo : TraitInfo, ILobbyOptions
	{
		public const string IntervalOptionId = "nuclear-unlock-interval";
		public const string HighestYieldOptionId = "nuclear-highest-yield";

		[Desc("Label for the unlock interval dropdown.")]
		public readonly string IntervalLabel = "Nuclear Unlock";

		[Desc("Tooltip for the unlock interval dropdown.")]
		public readonly string IntervalDescription =
			"How long before each nuclear tier comes up for sale. The lowest yield is purchasable after " +
			"one interval, the next a further interval later, and so on. No wait puts every tier on sale " +
			"from the first second";

		[Desc("Unlock intervals offered in the lobby, in MINUTES. `0` is the no-wait opt-out and is",
			"kept first for the same reason `timelimit` keeps its own 0 -- see " + nameof(IntervalDefault) + ".",
			"",
			"Values are the wire-visible option keys, so this list is not free to reorder or retype:",
			"a key that stops existing silently discards every stored value set to it.")]
		public readonly int[] IntervalOptions = { 0, 5, 7, 10, 15, 20 };

		[Desc("Default unlock interval in minutes. Must be one of " + nameof(IntervalOptions) + ".",
			"",
			"TEN, BECAUSE THAT IS WHAT THE LOBBY BAR ALREADY DRAWS (decision 22). The shipped timeline",
			"captions its amber band from ten minutes in, so any other default here would leave the bar",
			"lying in the other direction -- which is the defect this whole change exists to remove.",
			"",
			"WHETHER TEN IS THE RIGHT NUMBER IS AN OPEN QUESTION AND IT IS THE USER'S, NOT A CODE ONE.",
			"Decision 16 records it: ten minutes puts the 100 kt tier at 40:00, most of a match, so if",
			"Skirmish nukes are meant to be a mid-game tool the interval wants to be five to seven. Both",
			"are offered above so answering it is a lobby choice rather than a rebuild. The implementer",
			"was told explicitly not to pre-empt it; do not 'tune' this without the user's word.")]
		public readonly int IntervalDefault = 10;

		[Desc("Prevent the unlock interval from being changed in the lobby.")]
		public readonly bool IntervalLocked = false;

		[Desc("Show the unlock interval dropdown in the lobby.")]
		public readonly bool IntervalVisible = true;

		[Desc("Display order for the unlock interval dropdown. 30-35 is the Arsenal section.")]
		public readonly int IntervalDisplayOrder = 34;

		[Desc("Label for the highest-yield dropdown.")]
		public readonly string HighestYieldLabel = "Highest Yield";

		[Desc("Tooltip for the highest-yield dropdown.")]
		public readonly string HighestYieldDescription =
			"The largest warhead this match will ever put on sale. Tiers above it never unlock, however " +
			"long the match runs. Game-enders are never purchasable and are not offered here";

		[Desc("Default highest yield. A " + nameof(NuclearRung) + " name; the 200 kt+ game-ender rung",
			"is deliberately NOT offered and cannot be reached -- decision 17.3, the user's own ruling",
			"and stricter than it was recommended: 'Game-enders are NEVER purchasable in Skirmish',",
			"with no host override. " + nameof(NuclearUnlockSchedule) + ".ClampCap enforces it in code",
			"as well, so a map that overrides this field cannot open that band either.")]
		public readonly NuclearRung HighestYieldDefault = NuclearRung.HundredKiloton;

		[Desc("Prevent the highest yield from being changed in the lobby.")]
		public readonly bool HighestYieldLocked = false;

		[Desc("Show the highest-yield dropdown in the lobby.")]
		public readonly bool HighestYieldVisible = true;

		[Desc("Display order for the highest-yield dropdown.")]
		public readonly int HighestYieldDisplayOrder = 35;

		void ThrowIfBadDefault()
		{
			if (!((IList<int>)IntervalOptions).Contains(IntervalDefault))
				throw new YamlException($"{nameof(IntervalDefault)} must be one of {nameof(IntervalOptions)}.");
		}

		/// <summary>The interval stops offered, as wire keys mapped to their lobby labels.</summary>
		// Public and static-shaped so the lobby timeline can position a marker on exactly these stops
		// rather than inventing its own. A stop the option does not define cannot be produced by
		// dragging, and an out-of-set value throws KeyNotFoundException on the next CLIENT JOIN
		// (LobbySettingsNotification.cs:39 indexes Values unchecked) -- the host sees a working lobby
		// and the next player to connect is thrown out.
		public IReadOnlyDictionary<string, string> IntervalValues()
		{
			var values = new Dictionary<string, string>();
			foreach (var minutes in IntervalOptions)
				values[minutes.ToString(CultureInfo.InvariantCulture)] = minutes > 0
					? $"{minutes} minutes"
					: "No wait";

			return new ReadOnlyDictionary<string, string>(values);
		}

		/// <summary>The yield caps offered: every band this clock may sell, lowest first.</summary>
		// Built from the rung range rather than written out, so a new NuclearRung between 1 kt and
		// 100 kt appears here automatically and cannot be forgotten. The game-ender rung is excluded
		// by HighestPurchasableRung, not by being left out of a hand-written list.
		public static IReadOnlyDictionary<string, string> HighestYieldValues()
		{
			var values = new Dictionary<string, string>();
			for (var rung = NuclearUnlockSchedule.LowestRung; rung <= NuclearUnlockSchedule.HighestPurchasableRung; rung++)
				values[((NuclearRung)rung).ToString().ToLowerInvariant()] = Widgets.DefconReadoutModel.RungLabel(rung);

			return new ReadOnlyDictionary<string, string>(values);
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			ThrowIfBadDefault();

			// NEITHER IS A Placeholder. The four DEFCON dropdowns are dimmed because their durations
			// are self-declared untuned guesses (decision 12); these two govern behaviour that ships
			// working the moment this trait is registered, and dimming them would be the inverse of
			// that ruling -- a live control wearing an inert label, which the 2026-09-10 mode audit
			// called the worst single item in its whole survey.
			yield return new LobbyOption(
				IntervalOptionId,
				IntervalLabel,
				IntervalDescription,
				IntervalVisible,
				IntervalDisplayOrder,
				IntervalValues(),
				IntervalDefault.ToString(CultureInfo.InvariantCulture),
				IntervalLocked,
				"Powers");

			yield return new LobbyOption(
				HighestYieldOptionId,
				HighestYieldLabel,
				HighestYieldDescription,
				HighestYieldVisible,
				HighestYieldDisplayOrder,
				HighestYieldValues(),
				HighestYieldDefault.ToString().ToLowerInvariant(),
				HighestYieldLocked,
				"Powers");
		}

		public override object Create(ActorInitializer init) { return new NuclearUnlockClock(init.Self, this); }
	}

	public class NuclearUnlockClock
	{
		readonly World world;

		/// <summary>Ticks between tiers coming up for sale; 0 when the host turned the wait off.</summary>
		public readonly int IntervalTicks;

		/// <summary>The highest band this match will sell, already clamped below the game-enders.</summary>
		public readonly int CapRung;

		/// <summary>Whether the clock governs this match at all. See the file header's three cases.</summary>
		public readonly bool Active;

		public NuclearUnlockClock(Actor self, NuclearUnlockClockInfo info)
		{
			world = self.World;
			var settings = world.LobbyInfo.GlobalSettings;

			var minutes = info.IntervalDefault;
			var raw = settings.OptionOrDefault(NuclearUnlockClockInfo.IntervalOptionId,
				info.IntervalDefault.ToString(CultureInfo.InvariantCulture));
			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
				minutes = info.IntervalDefault;

			// world.Timestep, read ONCE here in the constructor, exactly as TimeLimitManager does
			// (TimeLimitManager.cs:118). It is the same value on every client at this moment; the debug
			// speed button mutates it later, which is why nothing below re-reads it per tick.
			IntervalTicks = NuclearUnlockSchedule.TicksForMinutes(minutes, world.Timestep);

			var cap = settings.OptionOrDefault(NuclearUnlockClockInfo.HighestYieldOptionId,
				info.HighestYieldDefault.ToString().ToLowerInvariant());
			if (!System.Enum.TryParse<NuclearRung>(cap, true, out var capRung))
				capRung = info.HighestYieldDefault;

			CapRung = NuclearUnlockSchedule.ClampCap((int)capRung);

			// ==== THE THREE SUSPENSIONS ====
			// SANDBOX IS READ OFF self.Info, NOT off world.WorldActor, and that is not a style choice:
			// World.cs:252 is the line that assigns WorldActor, so it is still NULL while world traits
			// are being created and PowersLobbyOptionsInfo.SandboxSettingsOrNull would throw here.
			// `self` IS the World actor, so self.Info is its ActorInfo and is available immediately.
			// DefconWall shipped the WorldActor idiom in a world trait and threw a
			// NullReferenceException in every match; this is the same trap one constructor earlier.
			var sandboxInfo = self.Info.TraitInfoOrDefault<PowersLobbyOptionsInfo>();
			var sandbox = settings.OptionOrDefault("powers-sandbox",
				sandboxInfo?.PowersSandboxCheckboxEnabled ?? false);

			// The mode is read from the lobby rather than from the DefconEscalation TRAIT for the same
			// creation-order reason: another world trait may not exist yet. Same option id, same
			// fallback chain DefconEscalation itself uses one constructor away.
			var defconInfo = self.Info.TraitInfoOrDefault<DefconEscalationInfo>();
			var modeRaw = settings.OptionOrDefault(DefconEscalationInfo.ModeOptionId,
				(defconInfo?.ModeDefault ?? DefconGameMode.Skirmish).ToString());
			if (!System.Enum.TryParse<DefconGameMode>(modeRaw, true, out var mode))
				mode = defconInfo?.ModeDefault ?? DefconGameMode.Skirmish;

			Active = IntervalTicks > 0 && !sandbox && mode != DefconGameMode.Escalation;
		}

		/// <summary>
		/// The highest yield band on sale right now. Reports the top of the ladder whenever the clock
		/// is suspended, so a suspended clock is indistinguishable from this trait not being here.
		/// </summary>
		// A PURE READ, recomputed by the caller every tick. No state, nothing hashed, nothing written.
		public int ReleasedRung => Active
			? NuclearUnlockSchedule.RungAt(world.WorldTick, IntervalTicks, CapRung)
			: NuclearReleaseLadder.Highest;
	}
}
