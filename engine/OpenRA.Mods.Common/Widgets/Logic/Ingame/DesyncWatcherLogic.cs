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

using System.IO;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	// A desync latches the world permanently unresumable (World.OutOfSync -> EndGame), which switches
	// off selection, order generation and the chat panel. The only output upstream produces is a
	// single system chat line, written into the panel that was just disabled - so from the player's
	// side the game simply stops, with no statement that it has ended or why. This raises the dialog
	// that says so, and names the report file, because an unreported desync cannot be diagnosed.
	public class DesyncWatcherLogic : ChromeLogic
	{
		[FluentReference]
		const string Title = "dialog-desync.title";

		[FluentReference("frame", "folder", "file")]
		const string Prompt = "dialog-desync.prompt";

		[FluentReference("frame", "folder")]
		const string PromptNoReport = "dialog-desync.prompt-no-report";

		[FluentReference]
		const string Quit = "dialog-desync.confirm";

		[FluentReference]
		const string Stay = "dialog-desync.cancel";

		[ObjectCreator.UseCtor]
		public DesyncWatcherLogic(Widget widget, ModData modData, World world, OrderManager orderManager)
		{
			var shown = false;
			widget.Get<LogicTickerWidget>("DESYNC_WATCHER").OnTick = () =>
			{
				if (shown || !orderManager.IsOutOfSync)
					return;

				shown = true;

				// Null when no report could be written for the desynced frame, which is the normal
				// case in a replay. Naming a file whose whole content is "No sync report available!"
				// would send the player chasing evidence that isn't there.
				var path = orderManager.OutOfSyncReportPath;
				var frame = orderManager.OutOfSyncFrame;

				Game.RunAfterTick(() =>
				{
					if (string.IsNullOrEmpty(path))
						ConfirmationDialogs.ButtonPrompt(modData,
							title: Title,
							text: PromptNoReport,
							textArguments: new object[] { "frame", frame, "folder", Platform.SupportDir + "Logs" },
							onConfirm: () => IngameMenuLogic.OnQuit(world),
							confirmText: Quit,
							onCancel: () => { },
							cancelText: Stay,
							promptName: "DESYNC_PROMPT");
					else
						ConfirmationDialogs.ButtonPrompt(modData,
							title: Title,
							text: Prompt,
							textArguments: new object[]
							{
								"frame", frame,
								"folder", Path.GetDirectoryName(path),
								"file", Path.GetFileName(path)
							},
							onConfirm: () => IngameMenuLogic.OnQuit(world),
							confirmText: Quit,
							onCancel: () => { },
							cancelText: Stay,
							promptName: "DESYNC_PROMPT");
				});
			};
		}
	}
}
