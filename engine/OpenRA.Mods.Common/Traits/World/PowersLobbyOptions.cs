#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Collections.ObjectModel;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Adds lobby options for configuring support powers (airstrikes, the nuclear arsenal, and",
		"the sandbox switch that suspends the buy tab's faction locks).")]
	public class PowersLobbyOptionsInfo : TraitInfo, ILobbyOptions
	{
		[Desc("Label for the airstrike checkbox.")]
		public readonly string AirstrikeCheckboxLabel = "Airstrikes";

		[Desc("Tooltip for the airstrike checkbox.")]
		public readonly string AirstrikeCheckboxDescription = "Enable airstrike support powers";

		[Desc("Default airstrike setting.")]
		public readonly bool AirstrikeCheckboxEnabled = true;

		[Desc("Lock the airstrike option.")]
		public readonly bool AirstrikeCheckboxLocked = false;

		[Desc("Show the airstrike option.")]
		public readonly bool AirstrikeCheckboxVisible = true;

		[Desc("Display order for the airstrike option.")]
		public readonly int AirstrikeCheckboxDisplayOrder = 100;

		[Desc("Label for the airstrike cooldown dropdown.")]
		public readonly string AirstrikeCooldownLabel = "Airstrike Cooldown";

		[Desc("Tooltip for the airstrike cooldown dropdown.")]
		public readonly string AirstrikeCooldownDescription = "Time between airstrike uses";

		[Desc("Default airstrike cooldown.")]
		public readonly string AirstrikeCooldownDefault = "4min";

		[Desc("Lock the airstrike cooldown option.")]
		public readonly bool AirstrikeCooldownLocked = false;

		[Desc("Show the airstrike cooldown option.")]
		public readonly bool AirstrikeCooldownVisible = true;

		[Desc("Display order for the airstrike cooldown option.")]
		public readonly int AirstrikeCooldownDisplayOrder = 101;

		[Desc("Label for the extended nuclear arsenal checkbox.")]
		public readonly string NuclearArsenalCheckboxLabel = "Nuclear Arsenal";

		[Desc("Tooltip for the extended nuclear arsenal checkbox.")]
		public readonly string NuclearArsenalCheckboxDescription =
			"Allow the full nuclear arsenal: B61-12 (both dial settings), W76-1, RS-28 Sarmat, B83-1 and Tsar Bomba";

		[Desc("Default extended nuclear arsenal setting. ON, deliberately, and the user's own ruling:",
			"\"I want all of them added as nukes, gated behind the lobby option like the old nuke was, but",
			"reachable from in game.\" Expected to be revisited before release. (It used to be described",
			"here as following the high-yield nuke's default rather than the tactical nuke's; both of",
			"those checkboxes were retired on 2026-09-15 and this is now the only weapon gate with a",
			"default to choose.)",
			"",
			"ONE CHECKBOX FOR SIX POWERS, which is the reason this is not six fields. The powers differ by",
			"yield across five orders of magnitude but they are one feature, and a lobby row per warhead",
			"would be six rows describing a single decision.",
			"",
			"This does NOT make the arsenal fail open. The registered default (this field) and the",
			"unregistered fallback are separate values: GrantConditionOnLobbyOption reads",
			"OptionOrDefault(Option, !GrantWhenOptionDisabled) (GrantConditionOnLobbyOption.cs:45-49), and",
			"that fallback is `!GrantWhenOptionDisabled` from nuclear-arsenal.yaml, never this field. The",
			"gate there keeps the GrantWhenOptionDisabled: true form, so a build where this trait is",
			"stripped still resolves the option to FALSE and hides all six. Registered: on. Absent: off.")]
		public readonly bool NuclearArsenalCheckboxEnabled = true;

		[Desc("Lock the extended nuclear arsenal option.")]
		public readonly bool NuclearArsenalCheckboxLocked = false;

		[Desc("Show the extended nuclear arsenal option.")]
		public readonly bool NuclearArsenalCheckboxVisible = true;

		[Desc("Display order for the extended nuclear arsenal option.")]
		public readonly int NuclearArsenalCheckboxDisplayOrder = 104;

		[Desc("Label for the sandbox checkbox.")]
		public readonly string PowersSandboxCheckboxLabel = "Sandbox: All Support Powers";

		[Desc("Tooltip for the sandbox checkbox.")]
		public readonly string PowersSandboxCheckboxDescription =
			"Sandbox/debug only, not for normal play: every support power purchasable by both " +
			"factions, including the event-only warheads, and no waiting. Ignores faction locks; " +
			"strikes load instantly and launch the moment you order them. They still fly in from " +
			"off-map";

		[Desc("Default sandbox setting. OFF, and unlike the two nuclear defaults above this one is",
			"not expected to be revisited before release -- it is a permanent test mode, not a",
			"staging decision. THE USER'S REASON FOR IT, 2026-09-07: they had all ten powers buyable",
			"by everyone specifically so they could test them, and the faction gate this option",
			"escapes would otherwise have taken that away.",
			"",
			"WHAT TICKING IT DOES: rules/player.yaml carries three ProvidesPrerequisite traits with",
			"no Factions filter, gated on `!powers-sandbox-disabled`, which hand every player",
			"powers.america, powers.russia AND powers.event at once. All sixteen entries then appear",
			"in the buy tab for both sides.",
			"",
			"AND AS EVERYWHERE ELSE IN THIS FILE, THE REGISTERED DEFAULT AND THE UNREGISTERED",
			"FALLBACK ARE SEPARATE VALUES. GrantConditionOnLobbyOption reads",
			"OptionOrDefault(Option, !GrantWhenOptionDisabled) (GrantConditionOnLobbyOption.cs:45-49);",
			"the fallback is `!GrantWhenOptionDisabled` from player.yaml, never this field, which is",
			"only consulted when this trait is present to register the option at all. player.yaml",
			"uses the same GrantWhenOptionDisabled: true form as the nuke gates, so a build with this",
			"trait stripped, an old saved session, or a map that removes it all resolve the option to",
			"FALSE and grant `powers-sandbox-disabled` -- i.e. they fall back to a NORMAL,",
			"faction-locked match. That is the safe direction here: the failure mode of getting it",
			"backwards would be handing both factions the Tsar Bomba in a game nobody asked it of.")]
		public readonly bool PowersSandboxCheckboxEnabled = false;

		[Desc("Lock the sandbox option.")]
		public readonly bool PowersSandboxCheckboxLocked = false;

		[Desc("Show the sandbox option.")]
		public readonly bool PowersSandboxCheckboxVisible = true;

		[Desc("Display order for the sandbox option.")]
		public readonly int PowersSandboxCheckboxDisplayOrder = 105;

		// ==== WHAT SANDBOX REMOVES, AND WHAT IT DELIBERATELY DOES NOT ====
		// The three fields below are the sub-behaviours the ONE checkbox above turns on. They exist
		// as fields rather than as inline constants for two reasons: a scenario can dial one back in
		// its rules.yaml without losing the rest of sandbox (test-tacnuke-delivers does exactly
		// that), and reverting any one of them is a single value rather than a code edit.
		//
		// EVERY CONSUMER GUARDS ON SandboxSettingsOrNull RETURNING NON-NULL, so all three are
		// unreachable with the checkbox off. Nothing here can move a normal match.
		//
		// USER REQUEST, 2026-09-08: "We have a 'Sandbox' option in the lobby, when active make it so
		// that all powers/strikes arrive from outside the map immediately without any extra wait."
		// The last five words are the whole brief, and "from outside the map" is the constraint on
		// it -- the off-map approach is the thing being looked at, so removing the WAIT must not
		// remove the APPROACH. That is why the first two default true and the third does not.

		[Desc("Sandbox: complete a support-power purchase in one tick instead of over its",
			"Buildable.BuildDuration, and ignore the Supply Route contestation throttle on the",
			"Powers queue. Money is still taken in full -- this removes the WAIT, not the price.",
			"Read by " + nameof(SupportPowerProductionQueue) + " and by nothing else, so no unit",
			"queue is affected. Purely gating: no visual consequence at all.")]
		public readonly bool SandboxRemovesPurchaseDelay = true;

		[Desc("Sandbox: drop " + nameof(MissileStrikePower) + "'s MissileDelay, the ticks between",
			"the order and the missile being ADDED TO THE WORLD. During that window the missile does",
			"not exist and SpawnActorEffect renders nothing (SpawnActorEffect.cs:60), so this is dead",
			"air ahead of the approach rather than any part of it -- 30.0 s on the tactical nuke,",
			"36.0 s on the Tsar Bomba. THE FLIGHT ITSELF IS UNTOUCHED: the missile still appears at",
			"the full standoff and still flies the whole way in.",
			"",
			"MissileDelayPerAimPoint (AimPointInterval) is NOT dropped. That one staggers the",
			"warheads of a salvo against EACH OTHER so an RS-28's six RVs cross the edge as a stream",
			"rather than as one stack of sprites, which is a shape and not a wait.")]
		public readonly bool SandboxRemovesLaunchDelay = true;

		[Desc("Sandbox: percentage of the normal off-map standoff a strike is born at, and so the",
			"percentage of its normal flight time. 100 = UNCHANGED, and is the shipped value.",
			"",
			"DEFAULTED TO THE IDENTITY ON PURPOSE, and it is the one lever here that is not the",
			"user's stated request. Flight time is the off-map approach -- the thing being evaluated",
			"-- so shortening it is a look-and-feel decision rather than a testing convenience, and",
			"it is theirs to make. On the largest shipped map (x-lake, 130x130) the standoff is 199.8",
			"cells and the flight is 511 ticks (30.7 s) for a Tsar Bomba, 127 ticks (7.6 s) for an",
			"RS-28 RV; at 50 those halve. Values are clamped to " + nameof(MissileStrikeApproach) +
			".MinStandoff, so even 1 leaves a cell of approach rather than teleporting the warhead",
			"onto its aim point.")]
		public readonly int SandboxStandoffPercent = 100;

		/// <summary>
		/// The sandbox settings when the option is ON, and NULL when it is off -- which is what makes
		/// "sandbox changes nothing when it is off" one visible guard at each consumer rather than a
		/// property a reader has to reconstruct from three call sites.
		/// </summary>
		/// <remarks>
		/// <para>THE AUTHORITY ORDER, which is not obvious and is asked about every time: the host's
		/// lobby tick (stored in <c>LobbyInfo.GlobalSettings</c>) beats the registered default, and
		/// the registered default is <see cref="PowersSandboxCheckboxEnabled"/> AS OVERRIDDEN FROM
		/// YAML -- it is an ordinary TraitInfo field, so a map's rules.yaml can set it and seven
		/// autotest scenarios do. <c>PowersSandboxCheckboxLocked: true</c> removes the host from that
		/// ordering entirely, which is why those scenarios set both.</para>
		///
		/// <para>The C# value is consulted at RUNTIME only as OptionOrDefault's fallback, i.e. when
		/// the option is not in GlobalSettings at all: the trait stripped from world.yaml, an old
		/// saved session, a map that removes it. A missing trait therefore yields null here and a
		/// NORMAL match, the same fail-safe direction the GrantWhenOptionDisabled polarity buys on
		/// the YAML side.</para>
		///
		/// <para>SAFE ON THE SYNCED ORDER PATH, which matters because
		/// <see cref="MissileStrikePower"/> calls this while resolving an order.
		/// <c>LobbyInfo.GlobalSettings</c> is shared session state agreed before the first tick, not
		/// anything client-local, and every read below it is integer -- so identical inputs still
		/// produce byte-identical positions on every client. This is the same thing
		/// GrantConditionOnLobbyOption already does for conditions that gate simulation.</para>
		/// </remarks>
		public static PowersLobbyOptionsInfo SandboxSettingsOrNull(World world)
		{
			var info = world.WorldActor.Info.TraitInfoOrDefault<PowersLobbyOptionsInfo>();
			var enabled = world.LobbyInfo.GlobalSettings
				.OptionOrDefault("powers-sandbox", info?.PowersSandboxCheckboxEnabled ?? false);

			return enabled ? info : null;
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption(
				"airstrikes",
				AirstrikeCheckboxLabel,
				AirstrikeCheckboxDescription,
				AirstrikeCheckboxVisible,
				AirstrikeCheckboxDisplayOrder,
				AirstrikeCheckboxEnabled,
				AirstrikeCheckboxLocked,
				"Powers");

			var cooldownValues = new Dictionary<string, string>
			{
				{ "2min", "2 minutes" },
				{ "3min", "3 minutes" },
				{ "4min", "4 minutes" },
				{ "5min", "5 minutes" },
				{ "8min", "8 minutes" },
			};

			// One gate for the whole extended arsenal (rules/ingame/nuclear-arsenal.yaml): B61-12 at both
			// its lowest and highest dial settings, W76-1, RS-28 Sarmat, B83-1 and Tsar Bomba. The
			// GrantWhenOptionDisabled polarity on the player.yaml side is what makes an unregistered
			// option resolve to false and hide all six: GrantConditionOnLobbyOption falls back to
			// `!GrantWhenOptionDisabled` rather than to the default field below, so a build with this
			// trait stripped, an old saved session or a map that removes it all fail CLOSED.
			//
			// This and `powers-sandbox` are the only two weapon gates left here. `tactical-nuke` and
			// `high-yield-nuke` were retired on 2026-09-15: each gated exactly one `powers.event`
			// power, a tier no faction provides, so outside the sandbox neither decided anything and
			// the four per-tier NuclearUnlockClock checkboxes (decision 02) are the controls a host
			// actually reaches for. The two powers are now gated by the ladder band and the event
			// tier alone.
			yield return new LobbyBooleanOption(
				"nuclear-arsenal",
				NuclearArsenalCheckboxLabel,
				NuclearArsenalCheckboxDescription,
				NuclearArsenalCheckboxVisible,
				NuclearArsenalCheckboxDisplayOrder,
				NuclearArsenalCheckboxEnabled,
				NuclearArsenalCheckboxLocked,
				"Powers");

			// THE SANDBOX GATE. Same "Powers" group and the same GrantWhenOptionDisabled polarity on
			// the player.yaml side as the three above, and default FALSE like the tactical nuke rather
			// than true like the arsenal -- a match nobody configured must be a normal, faction-locked
			// match. Registered LAST of the checkboxes (display order 105) because it is not a
			// content switch like the other three: it does not decide which weapons exist, it
			// suspends the faction rules governing who may buy them.
			yield return new LobbyBooleanOption(
				"powers-sandbox",
				PowersSandboxCheckboxLabel,
				PowersSandboxCheckboxDescription,
				PowersSandboxCheckboxVisible,
				PowersSandboxCheckboxDisplayOrder,
				PowersSandboxCheckboxEnabled,
				PowersSandboxCheckboxLocked,
				"Powers");

			yield return new LobbyOption(
				"airstrike-cooldown",
				AirstrikeCooldownLabel,
				AirstrikeCooldownDescription,
				AirstrikeCooldownVisible,
				AirstrikeCooldownDisplayOrder,
				new ReadOnlyDictionary<string, string>(cooldownValues),
				AirstrikeCooldownDefault,
				AirstrikeCooldownLocked,
				"Powers");
		}

		public override object Create(ActorInitializer init) { return new PowersLobbyOptions(this); }
	}

	public class PowersLobbyOptions : INotifyCreated
	{
		readonly PowersLobbyOptionsInfo info;

		public bool AirstrikesEnabled { get; private set; }
		public string AirstrikeCooldown { get; private set; }
		public bool NuclearArsenalEnabled { get; private set; }
		public bool PowersSandboxEnabled { get; private set; }

		public PowersLobbyOptions(PowersLobbyOptionsInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			AirstrikesEnabled = self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault("airstrikes", info.AirstrikeCheckboxEnabled);
			AirstrikeCooldown = self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault("airstrike-cooldown", info.AirstrikeCooldownDefault);
			NuclearArsenalEnabled = self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault("nuclear-arsenal", info.NuclearArsenalCheckboxEnabled);
			PowersSandboxEnabled = self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault("powers-sandbox", info.PowersSandboxCheckboxEnabled);
		}
	}
}
