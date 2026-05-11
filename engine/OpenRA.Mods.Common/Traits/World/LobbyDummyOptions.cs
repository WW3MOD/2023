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
			// PITFALL: every option produced by this trait is a UI-only placeholder.
			// The wrapper below stamps Placeholder=true on each so the lobby can dim
			// the rows and surface "not yet implemented" tooltips — see LobbyOption.Placeholder.
			foreach (var opt in BuildOptions(map))
			{
				opt.Placeholder = true;
				yield return opt;
			}
		}

		static IEnumerable<LobbyOption> BuildOptions(MapPreview map)
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

			yield return new LobbyOption(
				"sight-range", "Sight Range", "Scale all unit sight ranges (simulates fog/blizzard weather)",
				true, 53,
				MakePercentDropdown(10, 100),
				"100", false, "Map");

			// ── UNITS TAB — Infantry ──

			yield return new LobbyBooleanOption(
				"unit-conscripts", "Conscripts", "Light infantry (Conscripts)",
				true, 200, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-riflemen", "Riflemen", "Standard assault infantry (Riflemen, Auto Riflemen, Team Leaders)",
				true, 201, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-grenadiers", "Grenadiers", "Grenade launchers and mortars",
				true, 202, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-snipers", "Snipers", "Long-range marksmen",
				true, 203, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-antitank", "Anti-Tank", "Anti-tank specialists (Javelin / RPG)",
				true, 204, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-manpads", "MANPADS", "Man-portable air defense",
				true, 205, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-specops", "Special Forces", "Elite special operations infantry",
				true, 206, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-flamethrower", "Flamethrowers", "Close-range incendiary infantry",
				true, 207, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-support-inf", "Support", "Engineers, Medics, Technicians",
				true, 208, true, false, "Units.Infantry");

			yield return new LobbyBooleanOption(
				"unit-drone-ops", "Drone Operators", "Infantry drone operators",
				true, 209, true, false, "Units.Infantry");

			// ── UNITS TAB — Vehicles ──

			yield return new LobbyBooleanOption(
				"unit-light-vehicles", "Light Vehicles", "Humvee / BTR — fast recon and transport",
				true, 220, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-apcs", "APCs", "M113 / BMP-2 — armored personnel carriers",
				true, 221, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-ifvs", "IFVs", "Bradley / BMP — infantry fighting vehicles",
				true, 222, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-mbts", "Main Battle Tanks", "Abrams / T-90 — heavy armor",
				true, 223, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-artillery", "Artillery", "Paladin / Giatsint — self-propelled guns",
				true, 224, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-mlrs", "MLRS", "M270 / Grad — multiple launch rocket systems",
				true, 225, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-shorad", "SHORAD", "Stryker SHORAD / Tunguska — mobile air defense",
				true, 226, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-tactical-missiles", "Tactical Missiles", "HIMARS / Iskander — precision strike",
				true, 227, true, false, "Units.Vehicles");

			yield return new LobbyBooleanOption(
				"unit-thermobaric", "Thermobaric", "TOS — heavy flamethrower system (Russia)",
				true, 228, true, false, "Units.Vehicles");

			// ── UNITS TAB — Aircraft ──

			yield return new LobbyBooleanOption(
				"unit-transport-heli", "Transport Helicopters", "Chinook / Halo — heavy cargo",
				true, 240, true, false, "Units.Aircraft");

			yield return new LobbyBooleanOption(
				"unit-scout-heli", "Scout Helicopters", "Littlebird / Mi-28 — light attack",
				true, 241, true, false, "Units.Aircraft");

			yield return new LobbyBooleanOption(
				"unit-attack-heli", "Attack Helicopters", "Apache / Hind — heavy gunships",
				true, 242, true, false, "Units.Aircraft");

			yield return new LobbyBooleanOption(
				"unit-ground-attack", "Ground Attack", "A-10 / Su-25 — close air support",
				true, 243, true, false, "Units.Aircraft");

			yield return new LobbyBooleanOption(
				"unit-fighters", "Fighters", "F-16 / MiG-29 — air superiority",
				true, 244, true, false, "Units.Aircraft");

			// ── RULES TAB ──

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
