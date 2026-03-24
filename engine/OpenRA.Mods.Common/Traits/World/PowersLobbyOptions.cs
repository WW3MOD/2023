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
	[Desc("Adds lobby options for configuring support powers (airstrikes, etc.).")]
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
		}
	}
}
