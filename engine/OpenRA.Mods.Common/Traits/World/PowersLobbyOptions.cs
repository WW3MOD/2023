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
	[Desc("Adds lobby options for configuring support powers (airstrikes, the tactical nuclear strike, etc.).")]
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

		[Desc("Label for the tactical nuclear strike checkbox.")]
		public readonly string TacticalNukeCheckboxLabel = "Tactical Nuclear Strike";

		[Desc("Tooltip for the tactical nuclear strike checkbox.")]
		public readonly string TacticalNukeCheckboxDescription = "Allow the tactical nuclear strike support power";

		[Desc("Default tactical nuclear strike setting. OFF by design -- the doomsday design this",
			"power belongs to is unresolved (WORKSPACE/archive/plans/260324-nukes.md), so v1 ships it",
			"one tickbox away rather than on.")]
		public readonly bool TacticalNukeCheckboxEnabled = false;

		[Desc("Lock the tactical nuclear strike option.")]
		public readonly bool TacticalNukeCheckboxLocked = false;

		[Desc("Show the tactical nuclear strike option.")]
		public readonly bool TacticalNukeCheckboxVisible = true;

		[Desc("Display order for the tactical nuclear strike option.")]
		public readonly int TacticalNukeCheckboxDisplayOrder = 102;

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

			// The gate the tactical nuclear strike hangs off. The POLARITY is load-bearing and is
			// the reason the mod pairs this with GrantConditionOnLobbyOption's GrantWhenOptionDisabled
			// form rather than the direct one: that trait falls back to
			// OptionOrDefault(id, !GrantWhenOptionDisabled), so a build where this option is not
			// registered at all -- PowersLobbyOptions removed from world.yaml, an old saved session,
			// a map that strips the trait -- reads FALSE and still grants the disabling condition.
			// Written the other way round (grant when the option is enabled) the same absence would
			// default to TRUE and hand every player a nuke nobody asked for.
			yield return new LobbyBooleanOption(
				"tactical-nuke",
				TacticalNukeCheckboxLabel,
				TacticalNukeCheckboxDescription,
				TacticalNukeCheckboxVisible,
				TacticalNukeCheckboxDisplayOrder,
				TacticalNukeCheckboxEnabled,
				TacticalNukeCheckboxLocked,
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
		public bool TacticalNukeEnabled { get; private set; }

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
			TacticalNukeEnabled = self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault("tactical-nuke", info.TacticalNukeCheckboxEnabled);
		}
	}
}
