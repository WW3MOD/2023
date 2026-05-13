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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Lobby dropdown that grants prerequisites for all players when set to a non-OffValue. " +
		"Used to merge a checkbox + intensity dropdown into a single control (e.g. Kill Bounties 0–100%).")]
	public class LobbyPrerequisiteDropdownInfo : TraitInfo, ILobbyOptions, ITechTreePrerequisiteInfo
	{
		[FieldLoader.Require]
		[Desc("Internal id for this dropdown (becomes the lobby option key).")]
		public readonly string ID = null;

		[FieldLoader.Require]
		[Desc("Display name for this dropdown.")]
		public readonly string Label = null;

		[Desc("Tooltip description.")]
		public readonly string Description = null;

		[FieldLoader.Require]
		[Desc("Selectable values. Keys are stored in lobby state; display strings can be looked up by Format.")]
		public readonly string[] Values = null;

		[Desc("Default value (must be one of Values).")]
		public readonly string Default = null;

		[Desc("Value that means \"off\" — prerequisites are NOT granted when the option equals this.")]
		public readonly string OffValue = "0";

		[Desc("Display format. Use {0} as the value placeholder. Default is plain value, e.g. \"{0}%\".")]
		public readonly string Format = "{0}";

		[Desc("Display string used for the OffValue. Overrides Format for that one entry.")]
		public readonly string OffLabel = "Off";

		[Desc("Prevent the dropdown from being changed from its default value.")]
		public readonly bool Locked = false;

		[Desc("Display the dropdown in the lobby.")]
		public readonly bool Visible = true;

		[Desc("Display order for the dropdown in the lobby.")]
		public readonly int DisplayOrder = 0;

		[FieldLoader.Require]
		[Desc("Prerequisites granted to every player when the dropdown is not at OffValue.")]
		public readonly HashSet<string> Prerequisites = new HashSet<string>();

		IEnumerable<string> ITechTreePrerequisiteInfo.Prerequisites(ActorInfo info) { return Prerequisites; }

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			var values = Values.ToDictionary(v => v, v => v == OffValue ? OffLabel : string.Format(Format, v));
			var defaultValue = Default ?? (Values.Length > 0 ? Values[0] : OffValue);
			yield return new LobbyOption(ID, Label, Description, Visible, DisplayOrder, values, defaultValue, Locked);
		}

		public override object Create(ActorInitializer init) { return new LobbyPrerequisiteDropdown(this); }
	}

	public class LobbyPrerequisiteDropdown : INotifyCreated, ITechTreePrerequisite
	{
		readonly LobbyPrerequisiteDropdownInfo info;
		HashSet<string> prerequisites = new HashSet<string>();

		public LobbyPrerequisiteDropdown(LobbyPrerequisiteDropdownInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			var value = self.World.LobbyInfo.GlobalSettings.OptionOrDefault(info.ID, info.Default ?? info.OffValue);
			if (value != info.OffValue)
				prerequisites = info.Prerequisites;
		}

		IEnumerable<string> ITechTreePrerequisite.ProvidesPrerequisites => prerequisites;
	}
}
