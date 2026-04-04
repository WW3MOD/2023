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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class ModContentPromptLogic : ChromeLogic
	{
		readonly ModContent content;
		bool requiredContentInstalled;

		[ObjectCreator.UseCtor]
		public ModContentPromptLogic(ModData modData, Widget widget, Manifest mod, ModContent content, Action continueLoading)
		{
			this.content = content;
			CheckRequiredContentInstalled();

			var panel = widget.Get("CONTENT_PROMPT_PANEL");
			var headerTemplate = panel.Get<LabelWidget>("HEADER_TEMPLATE");
			var headerLines = !string.IsNullOrEmpty(content.InstallPromptMessage)
				? content.InstallPromptMessage.Replace("\\n", "\n").Split('\n')
				: Array.Empty<string>();
			var headerHeight = 0;
			foreach (var l in headerLines)
			{
				var line = (LabelWidget)headerTemplate.Clone();
				line.GetText = () => l;
				line.Bounds.Y += headerHeight;
				panel.AddChild(line);

				headerHeight += headerTemplate.Bounds.Height;
			}

			panel.Bounds.Height += headerHeight;
			panel.Bounds.Y -= headerHeight / 2;

			var advancedButton = panel.Get<ButtonWidget>("ADVANCED_BUTTON");
			advancedButton.Bounds.Y += headerHeight;
			advancedButton.OnClick = () =>
			{
				Ui.OpenWindow("CONTENT_PANEL", new WidgetArgs
				{
					{ "mod", mod },
					{ "content", content },
					{ "onCancel", CheckRequiredContentInstalled }
				});
			};

			var quickButton = panel.Get<ButtonWidget>("QUICK_BUTTON");
			quickButton.IsVisible = () => !string.IsNullOrEmpty(content.QuickDownload);
			quickButton.Bounds.Y += headerHeight;
			quickButton.OnClick = () =>
			{
				var downloadYaml = LoadYamlFromModPackage(mod, content.Downloads);
				var download = downloadYaml.FirstOrDefault(n => n.Key == content.QuickDownload);
				if (download == null)
					throw new InvalidOperationException($"Mod QuickDownload `{content.QuickDownload}` definition not found.");

				Ui.OpenWindow("PACKAGE_DOWNLOAD_PANEL", new WidgetArgs
				{
					{ "download", new ModContent.ModDownload(download.Value) },
					{ "onSuccess", continueLoading }
				});
			};

			var quitButton = panel.Get<ButtonWidget>("QUIT_BUTTON");
			quitButton.GetText = () => requiredContentInstalled ? "Continue" : "Quit";
			quitButton.Bounds.Y += headerHeight;
			quitButton.OnClick = () =>
			{
				if (requiredContentInstalled)
					continueLoading();
				else
					Game.Exit();
			};

			Game.RunAfterTick(Ui.ResetTooltips);
		}

		void CheckRequiredContentInstalled()
		{
			requiredContentInstalled = content.Packages
				.Where(p => p.Value.Required)
				.All(p => p.Value.TestFiles.All(f => File.Exists(Platform.ResolvePath(f))));
		}

		/// <summary>Load YAML files from the mod package, stripping the "modid|" prefix from paths.</summary>
		internal static List<MiniYamlNode> LoadYamlFromModPackage(Manifest mod, IEnumerable<string> files)
		{
			var nodes = new List<MiniYamlNode>();
			var prefix = mod.Id + "|";
			foreach (var f in files)
			{
				var path = f.StartsWith(prefix) ? f[prefix.Length..] : f;
				var stream = mod.Package.GetStream(path);
				if (stream == null)
					continue;

				nodes.AddRange(MiniYaml.FromStream(stream, f));
			}

			return nodes;
		}
	}
}
