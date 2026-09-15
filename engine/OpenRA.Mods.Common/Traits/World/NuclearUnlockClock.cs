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
 *      (There used to be a ninth, test-tacnuke-lobby-gated-off, which needed no exemption because
 *      its positive control was the CONVENTIONAL Kinzhal. It was deleted on 2026-09-15 with the
 *      `tactical-nuke` lobby option it existed to measure, so all eight are now exempt the same way.)
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
		// ==== FOUR CHECKBOXES WHERE THERE WAS ONE CAP DROPDOWN (decision 02, 2026-09-13) ====
		// The user: "in Skirmish mode they can be purchased but we can disable purchasing for various
		// levels too, like disable all high yield, or low yield etc, there can be 4 checkboxes, one
		// for each tier."
		//
		// THE SET IS NO LONGER A PREFIX, AND THAT IS THE WHOLE DIFFERENCE. `nuclear-highest-yield` was
		// a CEILING -- every band up to it was on sale and nothing above it ever was -- so one number
		// described it and NuclearUnlockSchedule.RungAt could clamp against it. A host can now turn
		// off 20 kt while leaving 1 kt and 50 kt on, which no single rung can express. The clock still
		// decides WHEN a band comes up (RungAt, unchanged); these decide WHETHER it is offered at all.
		//
		// GAME-ENDERS GET NO CHECKBOX, and that is decision 17.3 rather than an omission: "Game-enders
		// are NEVER purchasable in Skirmish", with no host override. HighestPurchasableRung enforces
		// it in code, so adding a fifth box here would not open that band either.
		public const string KilotonPurchasableOptionId = "nuke-1kt";
		public const string TwentyKilotonPurchasableOptionId = "nuke-20kt";
		public const string FiftyKilotonPurchasableOptionId = "nuke-50kt";
		public const string HundredKilotonPurchasableOptionId = "nuke-100kt";

		/// <summary>The four ids in ascending band order. THE ORDER IS LOAD-BEARING.</summary>
		// It is what pairs each id with its default, its label and its display order when the options
		// are generated below, and with its band in IsBandPurchasable. LobbyOptionsLogic names the
		// four constants individually rather than walking this, because its two collections are
		// static initialisers and reading well there matters more than saving four lines.
		public static readonly string[] PurchasableOptionIds =
		{
			KilotonPurchasableOptionId, TwentyKilotonPurchasableOptionId,
			FiftyKilotonPurchasableOptionId, HundredKilotonPurchasableOptionId,
		};

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

		[Desc("Whether each yield tier may be BOUGHT at all, lowest first. All four default ON, so an",
			"untouched lobby behaves exactly as the old `nuclear-highest-yield` default did (capped at",
			"100 kt, everything below it on sale).",
			"",
			"THESE GOVERN SKIRMISH ONLY. In DEFCON Escalation nothing nuclear is purchasable at all --",
			"a band is a free power on a regeneration timer (decision 02) -- so a host there controls",
			"the TIMING and nothing else, and these boxes are not consulted.",
			"See " + nameof(NuclearUnlockClock) + "." + nameof(NuclearUnlockClock.IsBandPurchasable) + ".")]
		public readonly bool KilotonPurchasable = true;

		[Desc("Whether the 20 kt tier may be bought. See " + nameof(KilotonPurchasable) + ".")]
		public readonly bool TwentyKilotonPurchasable = true;

		[Desc("Whether the 50 kt tier may be bought. See " + nameof(KilotonPurchasable) + ".")]
		public readonly bool FiftyKilotonPurchasable = true;

		[Desc("Whether the 100 kt tier may be bought. See " + nameof(KilotonPurchasable) + ".")]
		public readonly bool HundredKilotonPurchasable = true;

		[Desc("Prevent the four tier checkboxes from being changed in the lobby.")]
		public readonly bool PurchasableLocked = false;

		[Desc("Show the four tier checkboxes in the lobby.")]
		public readonly bool PurchasableVisible = true;

		[Desc("Display order of the FIRST tier checkbox; the other three follow it in band order.")]
		public readonly int PurchasableDisplayOrder = 35;

		/// <summary>The four defaults in ascending band order, matching <see cref="PurchasableOptionIds"/>.</summary>
		public IReadOnlyList<bool> PurchasableDefaults()
		{
			return new[] { KilotonPurchasable, TwentyKilotonPurchasable, FiftyKilotonPurchasable, HundredKilotonPurchasable };
		}

		/// <summary>The lobby label for each tier checkbox, ascending.</summary>
		// Built from the band table's own labels rather than written out, so a renamed rung cannot
		// leave a checkbox advertising a yield the game no longer has.
		public static string PurchasableLabel(int rung)
		{
			return Widgets.DefconReadoutModel.RungLabel(rung) + " purchasable";
		}

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

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			ThrowIfBadDefault();

			// NONE OF THESE IS A Placeholder. They govern behaviour that ships working the moment this
			// trait is registered, and dimming them would be a live control wearing an inert label --
			// which the 2026-09-10 mode audit called the worst single item in its whole survey.
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

			// ONE CHECKBOX PER TIER, ascending, generated from the id list and the band table rather
			// than written out four times -- so a renamed rung cannot leave a box advertising a yield
			// the game no longer has, and the ids, labels and defaults cannot fall out of order with
			// each other. Display orders run consecutively from PurchasableDisplayOrder.
			var defaults = PurchasableDefaults();
			for (var i = 0; i < PurchasableOptionIds.Length; i++)
			{
				var rung = NuclearUnlockSchedule.LowestRung + i;
				yield return new LobbyBooleanOption(
					PurchasableOptionIds[i],
					PurchasableLabel(rung),
					$"Allow the {Widgets.DefconReadoutModel.RungLabel(rung)} tier to be bought in Skirmish. " +
					"Ignored in Escalation, where nothing nuclear is purchasable at all",
					PurchasableVisible,
					PurchasableDisplayOrder + i,
					defaults[i],
					PurchasableLocked,
					"Powers");
			}
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

		// One flag per purchasable band, ascending, read once from the lobby at construction.
		readonly bool[] bandPurchasable;

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

			// NO HOST CAP ANY MORE. `nuclear-highest-yield` is replaced by the four per-tier checkboxes
			// (decision 02), and a set of four booleans is not a rung -- so the clock climbs to the
			// hard ceiling and the checkboxes decide which of the bands it passes are actually offered.
			// That ceiling is still decision 17.3's: game-enders are NEVER purchasable, from any
			// setting, and ClampCap enforces it a second time in code.
			CapRung = NuclearUnlockSchedule.ClampCap(NuclearUnlockSchedule.HighestPurchasableRung);

			var defaults = info.PurchasableDefaults();
			var purchasable = new bool[NuclearUnlockClockInfo.PurchasableOptionIds.Length];
			for (var i = 0; i < purchasable.Length; i++)
				purchasable[i] = settings.OptionOrDefault(NuclearUnlockClockInfo.PurchasableOptionIds[i], defaults[i]);

			bandPurchasable = purchasable;

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

		/// <summary>
		/// <para>May this band be bought at all this match? The host's four tier checkboxes, and the
		/// half of the Skirmish shop that is NOT about time.</para>
		///
		/// <para>TRUE FOR EVERYTHING THE BOXES DO NOT COVER, which is deliberate and is what keeps the
		/// change contained. The game-ender band has no checkbox and returns true here -- it is kept
		/// out by <see cref="NuclearUnlockSchedule.HighestPurchasableRung"/>, not by this, so a future
		/// fifth box could not accidentally open it either.</para>
		///
		/// <para>AND TRUE FOR EVERY BAND WHEN THE CLOCK IS SUSPENDED. Sandbox is "all support powers"
		/// and Escalation is not a shop at all (decision 02: a band there is a free power on a
		/// regeneration timer, and the host controls only the timing), so in both the boxes are not
		/// consulted rather than being read as "off".</para>
		/// </summary>
		public bool IsBandPurchasable(int rung)
		{
			if (!Active)
				return true;

			var index = rung - NuclearUnlockSchedule.LowestRung;
			if (index < 0 || index >= bandPurchasable.Length)
				return true;

			return bandPurchasable[index];
		}
	}
}
