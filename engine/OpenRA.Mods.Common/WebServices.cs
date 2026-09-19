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

using System;
using System.Threading.Tasks;
using OpenRA.Support;

namespace OpenRA.Mods.Common
{
	public enum ModVersionStatus { NotChecked, Latest, Outdated, Unknown, PlaytestAvailable }

	public class WebServices : IGlobalModData
	{
		public readonly string ServerList = "https://master.openra.net/games";
		public readonly string ServerAdvertise = "https://master.openra.net/ping";
		public readonly string MapRepository = "https://resource.openra.net/map/";
		public readonly string GameNews = "https://master.openra.net/gamenews";
		public readonly string GameNewsFileName = "news.yaml";
		public readonly string VersionCheck = "https://master.openra.net/versioncheck";

		// WW3MOD: the master-server protocol at VersionCheck only answers for mods the OpenRA master
		// server knows about; for anyone else it answers "unknown" forever and the update notice can
		// never appear. Setting this to the URL of a static file containing the latest released
		// version string switches the check to a client-side comparison, which any plain file host
		// can serve. Empty (the default) keeps the master-server path exactly as it was.
		public readonly string LatestVersionUrl = "";

		// WW3MOD: page the update notice's download button opens. Empty hides the button.
		public readonly string LatestVersionDownloadUrl = "";

		// WW3MOD: the sysinfo parameters MainMenuLogic appends to the GameNews query are meaningful
		// only to the OpenRA master server, which aggregates them. A mod serving its news as a static
		// file should set this false: the host cannot use the data, and sending it there would make
		// the consent dialog's "help us optimize the OpenRA engine" untrue about where it goes.
		public readonly bool GameNewsSendClientInfo = true;

		public ModVersionStatus ModVersionStatus { get; private set; }
		const int VersionCheckProtocol = 1;

		// The menu must come up whether or not this ever answers, so every failure path below lands
		// on a status rather than propagating.
		static readonly TimeSpan LatestVersionTimeout = TimeSpan.FromSeconds(10);

		public void CheckModVersion()
		{
			if (!string.IsNullOrEmpty(LatestVersionUrl))
			{
				CheckLatestVersionFile();
				return;
			}

			Task.Run(async () =>
			{
				var queryURL = new HttpQueryBuilder(VersionCheck)
				{
					{ "protocol", VersionCheckProtocol },
					{ "engine", Game.EngineVersion },
					{ "mod", Game.ModData.Manifest.Id },
					{ "version", Game.ModData.Manifest.Metadata.Version }
				}.ToString();

				try
				{
					var client = HttpClientFactory.Create();

					var httpResponseMessage = await client.GetAsync(queryURL);
					var result = await httpResponseMessage.Content.ReadAsStringAsync();

					var status = ModVersionStatus.Latest;
					switch (result)
					{
						case "outdated": status = ModVersionStatus.Outdated; break;
						case "unknown": status = ModVersionStatus.Unknown; break;
						case "playtest": status = ModVersionStatus.PlaytestAvailable; break;
					}

					Game.RunAfterTick(() => ModVersionStatus = status);
				}
				catch { }
			});
		}

		// WW3MOD: fetch a static file and do the comparison here. No query parameters are sent, so
		// the URL can be any dumb file host; no state reaches the menu except the resulting status.
		void CheckLatestVersionFile()
		{
			// Read on the calling (main) thread: the manifest is not ours to touch from a worker.
			var url = LatestVersionUrl;
			var localVersion = Game.ModData.Manifest.Metadata.Version;

			Task.Run(async () =>
			{
				// Network failure, HTTP error, timeout and garbage all land here. Unknown is the
				// honest answer to every one of them, and it never shows the notice.
				var status = ModVersionStatus.Unknown;

				try
				{
					var client = HttpClientFactory.Create();
					client.Timeout = LatestVersionTimeout;

					var httpResponseMessage = await client.GetAsync(url);
					httpResponseMessage.EnsureSuccessStatusCode();

					var result = await httpResponseMessage.Content.ReadAsStringAsync();
					status = ModVersion.Compare(localVersion, result);
				}
				catch (Exception e)
				{
					Log.Write("debug", $"Failed to query the latest version from {url}.");
					Log.Write("debug", e);
				}

				Game.RunAfterTick(() => ModVersionStatus = status);
			});
		}
	}
}
