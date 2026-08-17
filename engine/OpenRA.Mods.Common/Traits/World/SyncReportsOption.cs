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
using OpenRA.Network;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Controls the 'Sync Reports' checkbox in the lobby options.")]
	public class SyncReportsOptionInfo : TraitInfo, ILobbyOptions
	{
		[FluentReference]
		[Desc("Descriptive label for the sync reports checkbox in the lobby.")]
		public readonly string CheckboxLabel = "checkbox-sync-reports.label";

		[FluentReference]
		[Desc("Tooltip description for the sync reports checkbox in the lobby.")]
		public readonly string CheckboxDescription = "checkbox-sync-reports.description";

		[Desc("Default value of the sync reports checkbox in the lobby.")]
		public readonly bool CheckboxEnabled = true;

		[Desc("Prevent the sync reports state from being changed in the lobby.")]
		public readonly bool CheckboxLocked = false;

		[Desc("Whether to display the sync reports checkbox in the lobby.")]
		public readonly bool CheckboxVisible = true;

		[Desc("Display order for the sync reports checkbox in the lobby.")]
		public readonly int CheckboxDisplayOrder = 0;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption(Session.SyncReportsOptionId,
				CheckboxLabel, CheckboxDescription,
				CheckboxVisible, CheckboxDisplayOrder, CheckboxEnabled, CheckboxLocked);
		}

		public override object Create(ActorInitializer init) { return new SyncReportsOption(); }
	}

	// Declaration only: the value is read by OrderManager.StartGame, which runs before the world
	// exists, so there is nothing for a world trait instance to hold.
	public class SyncReportsOption { }
}
