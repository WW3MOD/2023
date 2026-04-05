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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Provides dummy lobby options for the WW3MOD settings redesign. These are visual placeholders until gameplay hooks are implemented.")]
	public class LobbyDummyOptionsInfo : TraitInfo, ILobbyOptions
	{
		static ReadOnlyDictionary<string, string> MakePercentDropdown(int min, int max, int step = 10)
		{
			var dict = new Dictionary<string, string>();
			for (var i = min; i <= max; i += step)
				dict[i.ToString()] = i + "%";
			return new ReadOnlyDictionary<string, string>(dict);
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			// ── COMBAT TAB ──

			yield return new LobbyOption(
				"weapon-range", "Weapon Range", "Scale all weapon ranges (100% = default, lower = shorter range)",
				true, 10,
				MakePercentDropdown(10, 100),
				"100", false, "Combat");

			yield return new LobbyOption(
				"damage-scale", "Damage Scale", "Scale all weapon damage (100% = default)",
				true, 11,
				MakePercentDropdown(10, 200),
				"100", false, "Combat");

			var suppressionValues = new Dictionary<string, string>
			{
				{ "off", "Off" },
				{ "low", "Low" },
				{ "normal", "Normal" },
				{ "high", "High" },
			};
			yield return new LobbyOption(
				"suppression", "Suppression", "Scale suppression intensity (Normal = default, Off = no suppression)",
				true, 12,
				new ReadOnlyDictionary<string, string>(suppressionValues),
				"normal", false, "Combat");

			yield return new LobbyOption(
				"veterancy-rate", "Veterancy Rate", "Scale experience gain speed (100% = default)",
				true, 13,
				MakePercentDropdown(10, 200),
				"100", false, "Combat");

			// ── ECONOMY TAB ──
			// (Starting Cash, Passive Income, Income Modifier are existing options — they'll be assigned Economy category in LobbyOptionsLogic)

			yield return new LobbyOption(
				"build-speed", "Build Speed", "Scale production/call-in speed (100% = default, 200% = twice as fast)",
				true, 34,
				MakePercentDropdown(10, 200),
				"100", false, "Economy");

			var supplyCapValues = new Dictionary<string, string>
			{
				{ "low", "Low" },
				{ "normal", "Normal" },
				{ "high", "High" },
			};
			yield return new LobbyOption(
				"supply-capacity", "Supply Capacity", "Scale supply truck ammo capacity",
				true, 35,
				new ReadOnlyDictionary<string, string>(supplyCapValues),
				"normal", false, "Economy");

			// ── MAP TAB ──
			// (Shroud, Fog, Separate Team Spawns are existing options — assigned Map category in LobbyOptionsLogic)

			yield return new LobbyOption(
				"sight-range", "Sight Range", "Scale all unit sight ranges (simulates fog/blizzard weather)",
				true, 53,
				MakePercentDropdown(10, 100),
				"100", false, "Map");

			// ── RULES TAB ──
			// (Game Speed, Tech Level, Time Limit, Starting Units are existing — assigned Rules category)

			yield return new LobbyBooleanOption(
				"friendly-fire", "Friendly Fire", "Your own units can damage each other",
				true, 74, true, false, "Rules");

			var bountyPercentValues = new Dictionary<string, string>
			{
				{ "1", "1%" },
				{ "2", "2%" },
				{ "5", "5%" },
				{ "10", "10%" },
				{ "15", "15%" },
				{ "20", "20%" },
				{ "25", "25%" },
				{ "50", "50%" },
				{ "75", "75%" },
				{ "100", "100%" },
			};
			yield return new LobbyOption(
				"bounty-percent", "Bounty %", "Percentage of killed unit's value awarded as bounty",
				true, 76,
				new ReadOnlyDictionary<string, string>(bountyPercentValues),
				"10", false, "Rules");

			// Powers master toggle
			yield return new LobbyBooleanOption(
				"powers-enabled", "Powers Enabled", "Enable support powers (airstrikes, etc.)",
				true, 80, true, false, "Rules");
		}

		public override object Create(ActorInitializer init) { return new LobbyDummyOptions(); }
	}

	public class LobbyDummyOptions { }
}
