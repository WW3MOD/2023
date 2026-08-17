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
using System.Collections.ObjectModel;
using System.Linq;
using OpenRA.Support;

namespace OpenRA.Mods.Common.Lint
{
	public sealed class CheckChromeIntegerExpressions : ILintPass
	{
		// VariableExpression.ParseSymbol is a TryGetValue that returns the default on a miss, so a symbol
		// nobody registered is not an error at runtime — it is silently 0. A widget positioned at
		// `X: WINDOW_RIGHT - 240` therefore lands at -240 and renders entirely off screen, which is how the
		// cargo panel stayed invisible for months (fixed at c9fdf334). The names read as plausible in YAML;
		// only this list decides which of them exist.
		static readonly string[] BoundsSymbols =
		{
			// Widget.Initialize, before any of the four expressions are evaluated.
			"WINDOW_WIDTH", "WINDOW_HEIGHT", "PARENT_WIDTH", "PARENT_HEIGHT"
		};

		static readonly string[] PositionOnlySymbols =
		{
			// Added only after Width and Height have been evaluated, so these resolve for X and Y and are
			// silently 0 inside a Width or Height expression.
			"WIDTH", "HEIGHT"
		};

		static readonly string[] CallerSuppliedSymbols =
		{
			// Passed in as "substitutions" by DropDownButtonWidget.ShowDropDown for the panel template it
			// loads. Which templates those are is only known at runtime, so it is accepted everywhere.
			"DROPDOWN_WIDTH"
		};

		// `positional` is true for X and Y, which are evaluated after Width and Height and can therefore see
		// the widget's own size.
		public static bool IsRegistered(string symbol, bool positional)
		{
			return BoundsSymbols.Contains(symbol)
				|| CallerSuppliedSymbols.Contains(symbol)
				|| (positional && PositionOnlySymbols.Contains(symbol));
		}

		public static IEnumerable<string> RegisteredSymbols(bool positional)
		{
			return BoundsSymbols
				.Concat(positional ? PositionOnlySymbols : Array.Empty<string>())
				.Concat(CallerSuppliedSymbols);
		}

		public void Run(Action<string> emitError, Action<string> emitWarning, ModData modData)
		{
			foreach (var filename in modData.Manifest.ChromeLayout)
				CheckInner(MiniYaml.FromStream(modData.DefaultFileSystem.Open(filename), filename), filename, emitError);
		}

		static void CheckInner(IEnumerable<MiniYamlNode> nodes, string filename, Action<string> emitError)
		{
			var substitutions = new Dictionary<string, int>();
			var readOnlySubstitutions = new ReadOnlyDictionary<string, int>(substitutions);

			foreach (var node in nodes)
			{
				if (node.Value == null)
					continue;

				if (node.Key == "X" || node.Key == "Y" || node.Key == "Width" || node.Key == "Height")
				{
					try
					{
						var expression = FieldLoader.GetValue<IntegerExpression>(node.Key, node.Value.Value);
						var positional = node.Key == "X" || node.Key == "Y";
						foreach (var symbol in expression.Variables)
						{
							if (IsRegistered(symbol, positional))
								continue;

							// The first line is the whole error as far as any tooling is concerned, so it
							// carries no line number and stays stable as the file is edited around it.
							emitError($"Unknown widget substitution symbol `{symbol}` in {filename} " +
								$"(`{node.Key}: {node.Value.Value}`). It is not registered anywhere and " +
								$"evaluates to 0 with no warning. Registered here: " +
								$"{string.Join(", ", RegisteredSymbols(positional))}.\n" +
								$"  at {node.Location}");
						}
					}
					catch (YamlException e)
					{
						emitError($"Failed to parse integer expression in {node}: {e.Message}");
					}
				}

				if (node.Value.Nodes != null)
					CheckInner(node.Value.Nodes, filename, emitError);
			}
		}
	}
}
