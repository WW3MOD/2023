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

/*
 * THE DEFCON STRIP, and under it the nuclear release readout -- Direction A of
 * WORKSPACE/mockups/defcon-hud-directions.html, which is the approved design.
 *
 * The whole of the DEFCON Escalation mode worked and NONE of it reached the screen: the level moved,
 * the conditions were granted and revoked, the release gate counted down, and a player saw a match
 * that looked exactly like Skirmish. This widget and DefconTransitionBannerWidget are that half.
 *
 * ==== WHAT IS DECIDED HERE AND WHAT IS DECIDED IN DefconReadoutModel ====
 * This file owns pixels: rectangles, fonts, colours, stacking. Every string it draws and every
 * "is this field shown at all" decision comes from DefconReadoutModel, which is a plain class with
 * an NUnit fixture, because copy that states the game's rules is worth pinning and a Draw() is not
 * reachable from a test. If you are about to add a `if (level == 2)` here, it belongs there.
 *
 * ==== IT DRAWS BOTTOM-UP FROM A FIXED BOTTOM EDGE ====
 * Both blocks are variable height -- the rule line wraps, DEFCON 2 adds the trigger line, the
 * nuclear block appears and disappears mid-match -- so everything is measured first and then drawn
 * upward from RenderBounds.Bottom. That is what keeps the DEFCON strip in ONE place on screen for a
 * whole match: a top-anchored layout would slide the strip down the screen the moment the nuclear
 * block appeared, and the player would lose the thing they had learned the position of.
 *
 * ==== WHY IT IS NOT A ChromeLogic OVER Label WIDGETS ====
 * The pips, the step boxes and the pulsing dot are all drawn geometry with no widget behind them,
 * and the two blocks stack against each other rather than against the window. Expressed as chrome
 * YAML that would be about forty widgets whose Y expressions all encode the same stacking rule.
 * StrategicProgressWidget is the existing precedent in this engine for a readout that draws itself.
 *
 * ==== THE WORLD-ACTOR TRAIT IS READ FROM A WIDGET, WHICH IS SAFE, AND WHY ====
 * DefconWall shipped a NullReferenceException in every match by reading self.World.WorldActor from
 * inside its own Created -- World.cs:252 IS the line that assigns WorldActor, so it is null while
 * world traits are being created. A widget is not in that window: chrome is loaded from
 * LoadIngamePlayerOrObserverUILogic, long after the world is up, and that logic class reads
 * world.WorldActor itself. GameTimerLogic reaches TimeLimitManager exactly this way. The trait is
 * still fetched with TraitOrDefault and null-checked, because a map is free not to have it.
 */

using System;
using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>The mockup's palette, shared with <see cref="DefconTransitionBannerWidget"/>.</summary>
	// Lifted from the CSS custom properties at the top of defcon-hud-directions.html rather than
	// re-picked by eye, so the built thing and the approved drawing are the same colours. The level
	// hues carry meaning -- green/amber/red is the ladder -- so they are looked up by level and not
	// written at the call site.
	public static class DefconPalette
	{
		public static readonly Color PanelBackground = Color.FromArgb(230, 10, 12, 9);   // rgba(10,12,9,.9)
		public static readonly Color Edge = Color.FromArgb(42, 44, 38);                  // #2a2c26
		public static readonly Color Dim = Color.FromArgb(141, 139, 128);                // #8d8b80
		public static readonly Color Label = Color.FromArgb(107, 106, 97);               // #6b6a61
		public static readonly Color Rule = Color.FromArgb(194, 192, 181);               // between #b4b2a7 and #e2e0d5
		public static readonly Color PipEmpty = Color.FromArgb(36, 38, 31);              // #24261f
		public static readonly Color PipSpent = Color.FromArgb(60, 63, 51);              // #3c3f33

		public static readonly Color DefconThree = Color.FromArgb(138, 168, 74);         // #8aa84a
		public static readonly Color DefconTwo = Color.FromArgb(200, 130, 58);           // #c8823a
		public static readonly Color DefconOne = Color.FromArgb(192, 64, 42);            // #c0402a

		public static readonly Color TriggerBorder = Color.FromArgb(74, 58, 32);         // #4a3a20
		public static readonly Color TriggerFill = Color.FromArgb(34, 26, 14);           // #221a0e

		// The nuclear block is a DIFFERENT SHAPE AND COLOUR ON PURPOSE -- it is a different ladder
		// that moves for different reasons, and the mockup's note says so explicitly. Nothing here is
		// a tint of the DEFCON hues.
		public static readonly Color NuclearAccent = Color.FromArgb(45, 106, 134);       // #2d6a86
		public static readonly Color NuclearLabel = Color.FromArgb(111, 143, 163);       // #6f8fa3
		public static readonly Color NuclearValue = Color.FromArgb(143, 182, 204);       // #8fb6cc
		public static readonly Color StepFill = Color.FromArgb(27, 29, 24);              // #1b1d18
		public static readonly Color StepEdge = Color.FromArgb(35, 37, 32);              // #232520
		public static readonly Color StepText = Color.FromArgb(78, 80, 72);              // #4e5048
		public static readonly Color StepReleasedFill = Color.FromArgb(30, 42, 48);      // #1e2a30
		public static readonly Color StepReleasedEdge = Color.FromArgb(45, 75, 89);      // #2d4b59
		public static readonly Color StepCurrentFill = Color.FromArgb(36, 66, 79);       // #24424f
		public static readonly Color StepCurrentEdge = Color.FromArgb(78, 139, 166);     // #4e8ba6
		public static readonly Color StepCurrentText = Color.FromArgb(214, 236, 247);    // #d6ecf7
		public static readonly Color StepCeilingFill = Color.FromArgb(42, 26, 22);       // #2a1a16
		public static readonly Color StepCeilingEdge = Color.FromArgb(90, 47, 38);       // #5a2f26
		public static readonly Color StepCeilingText = Color.FromArgb(168, 106, 92);     // #a86a5c
		public static readonly Color Foot = Color.FromArgb(169, 167, 156);               // #a9a79c

		public static Color ForLevel(int level)
		{
			switch (level)
			{
				case 3: return DefconThree;
				case 2: return DefconTwo;
				default: return DefconOne;
			}
		}
	}

	public class DefconReadoutWidget : Widget
	{
		public readonly string LevelFont = "Bold";
		public readonly string NameFont = "TinyBold";
		public readonly string RuleFont = "Small";
		public readonly string SmallFont = "Tiny";

		// The mockup's own measurements, in the same units it was drawn in.
		const int PadX = 11;
		const int PadTop = 8;
		const int PadBottom = 9;
		const int AccentWidth = 3;
		const int PipHeight = 3;
		const int PipGap = 3;
		const int StepHeight = 16;
		const int StepGap = 2;
		const int BlockGap = 6;

		// ---- THE LEDGER'S MEASUREMENTS ----------------------------------------------------------
		// A row is TALLER THAN A STEP BOX because it has to carry two things: the band's label, which
		// is what makes the row countable, and a countdown on the boxes that have one. Stacking them
		// is the only arrangement that keeps both -- putting the clock INSIDE the box in place of the
		// label would destroy the identity of the band it belongs to, which is the one thing the
		// ledger exists to show.
		const int LedgerRowHeight = 22;
		const int LedgerRowGap = 2;

		// Wide enough for "ENEMY" in Tiny with a gap after it. The two rows share it so their boxes
		// line up in a column -- a ledger whose rows were indented differently could not be read by
		// running an eye down it, which is exactly how it is meant to be read.
		const int LedgerLabelWidth = 42;

		readonly World world;
		readonly SpriteFont levelFont, nameFont, ruleFont, smallFont;

		DefconEscalation escalation;
		NuclearExchange exchange;
		bool initialised;

		[ObjectCreator.UseCtor]
		public DefconReadoutWidget(World world)
		{
			this.world = world;

			levelFont = Game.Renderer.Fonts[LevelFont];
			nameFont = Game.Renderer.Fonts[NameFont];
			ruleFont = Game.Renderer.Fonts[RuleFont];
			smallFont = Game.Renderer.Fonts[SmallFont];
		}

		void Init()
		{
			initialised = true;
			escalation = world.WorldActor.TraitOrDefault<DefconEscalation>();
			exchange = world.WorldActor.TraitOrDefault<NuclearExchange>();
		}

		public override void Draw()
		{
			if (!initialised)
				Init();

			if (escalation == null || !IsVisible())
				return;

			var level = escalation.Level;
			if (!DefconReadoutModel.IsVisible(level))
				return;

			var contentWidth = Bounds.Width - (2 * PadX);
			var ruleLines = WrapLines(DefconReadoutModel.RuleLine(level), contentWidth, ruleFont);
			var showTrigger = DefconReadoutModel.ShowsTrigger(escalation.Mode, level);

			var stripHeight = PadTop + levelFont.Measure(LevelHeightSample).Y + 7 + PipHeight + BlockGap
				+ (ruleLines.Count * LineHeight(ruleFont)) + PadBottom;
			if (showTrigger)
				stripHeight += BlockGap + smallFont.Measure(DefconReadoutModel.TriggerLine).Y + 4;

			var stripTop = RenderBounds.Bottom - stripHeight;
			DrawStrip(new Rectangle(RenderBounds.X, stripTop, Bounds.Width, stripHeight), level, ruleLines, showTrigger);

			if (DefconReadoutModel.ShowsNuclear(escalation.Mode, level, escalation.NuclearReleaseOpen))
				DrawNuclear(stripTop - BlockGap, contentWidth);
		}

		// A string with both an ascender and a descender, so the level row's height does not change
		// as the level does -- "DEFCON 1" and "DEFCON 3" measure the same, but a future name might not.
		const string LevelHeightSample = "DEFCONgy";

		void DrawStrip(Rectangle bounds, int level, IReadOnlyList<string> ruleLines, bool showTrigger)
		{
			var accent = DefconPalette.ForLevel(level);
			DrawPanel(bounds, accent);

			var x = bounds.X + PadX;
			var contentWidth = bounds.Width - (2 * PadX);
			var y = bounds.Y + PadTop;

			// ROW 1: the level, its name, and the clock field.
			var levelText = $"DEFCON {level}";
			var levelSize = levelFont.Measure(levelText);
			levelFont.DrawText(levelText, new float2(x, y), accent);

			var nameText = DefconReadoutModel.LevelName(level).ToUpperInvariant();
			var nameSize = nameFont.Measure(nameText);

			// Bottom-aligned rather than top-aligned: the two fonts are different sizes and the mockup
			// sits them on a shared baseline.
			var baselineY = y + levelSize.Y - nameSize.Y;
			nameFont.DrawText(nameText, new float2(x + levelSize.X + 8, baselineY), DefconPalette.Dim);

			// THE CLOCK FIELD, WHICH IS NEVER HIDDEN. At DEFCON 2 and 1 it holds an em dash, and that
			// emptied slot is the design's way of teaching that this rung does not end on a clock --
			// see DefconReadoutModel.NoClock. Removing the field would undo it.
			//
			// PITFALL: GameSpeed.Timestep, NOT world.Timestep. The latter is mutated at runtime by the
			// debug speed button and by test-mode speed multipliers, which would rescale this countdown
			// out of step with the game clock beside it. See conventions.md.
			var clockText = DefconReadoutModel.ShowsClock(escalation.Mode, level)
				? $"{WidgetUtils.FormatTime(escalation.TicksUntilNextLevel, false, world.GameSpeed.Timestep)} to DEFCON {level - 1}"
				: DefconReadoutModel.NoClock;

			var clockSize = nameFont.Measure(clockText);
			nameFont.DrawText(clockText, new float2(x + contentWidth - clockSize.X, baselineY), DefconPalette.Label);

			y += levelSize.Y + 7;

			DrawPips(x, y, contentWidth, level);
			y += PipHeight + BlockGap;

			foreach (var line in ruleLines)
			{
				ruleFont.DrawText(line, new float2(x, y), DefconPalette.Rule);
				y += LineHeight(ruleFont);
			}

			if (showTrigger)
				DrawTrigger(x, y + BlockGap);
		}

		// The three rungs of the ladder as a row of pips: spent, current, still to come. Index 0 is
		// DEFCON 3, so the pip for the current level is at Ceiling - level.
		void DrawPips(int x, int y, int contentWidth, int level)
		{
			const int Count = DefconEscalationState.Ceiling - DefconEscalationState.Floor + 1;
			var pipWidth = (contentWidth - ((Count - 1) * PipGap)) / Count;
			var current = DefconEscalationState.Ceiling - level;

			for (var i = 0; i < Count; i++)
			{
				var rect = new Rectangle(x + (i * (pipWidth + PipGap)), y, pipWidth, PipHeight);
				if (i < current)
				{
					WidgetUtils.FillRectWithColor(rect, DefconPalette.PipSpent);
					continue;
				}

				if (i > current)
				{
					WidgetUtils.FillRectWithColor(rect, DefconPalette.PipEmpty);
					continue;
				}

				// THE CURRENT PIP FILLS ONLY WHERE A CLOCK IS ACTUALLY RUNNING. At DEFCON 2 there is
				// nothing to fill toward -- the phase ends on a casualty -- so it is drawn solid. A
				// creeping bar there would be the exact misreading the mockup warns about: a player
				// waiting for a bar that never fills.
				var full = escalation.ClockTicks;
				if (!DefconReadoutModel.ShowsClock(escalation.Mode, level) || full <= 0)
				{
					WidgetUtils.FillRectWithColor(rect, DefconPalette.ForLevel(level));
					continue;
				}

				WidgetUtils.FillRectWithColor(rect, DefconPalette.PipEmpty);

				var elapsed = Math.Max(0, full - escalation.TicksUntilNextLevel);
				var filled = Math.Min(pipWidth, (int)((long)pipWidth * elapsed / full));
				if (filled > 0)
					WidgetUtils.FillRectWithColor(new Rectangle(rect.X, rect.Y, filled, PipHeight), DefconPalette.ForLevel(level));
			}
		}

		void DrawTrigger(int x, int y)
		{
			var text = DefconReadoutModel.TriggerLine;
			var size = smallFont.Measure(text);
			var box = new Rectangle(x, y, size.X + 24, size.Y + 4);

			WidgetUtils.FillRectWithColor(box, DefconPalette.TriggerFill);
			DrawBorder(box, DefconPalette.TriggerBorder);

			// The pulse is what carries "this is waiting for something" without a clock to do it. A
			// triangle wave over 27 ticks: 27 x 0.06 s = 1.62 s, the mockup's 1.6 s period. Game.LocalTick
			// rather than wall-clock, so it stops when the game does -- a heartbeat on a paused game
			// reads as a hung UI.
			var phase = (Game.LocalTick % 27) / 27f;
			var pulse = phase < 0.5f ? phase * 2 : 2 - (phase * 2);
			var alpha = (int)(64 + (191 * pulse));

			var dot = new Rectangle(box.X + 6, box.Y + ((box.Height - 5) / 2), 5, 5);
			WidgetUtils.FillRectWithColor(dot, Color.FromArgb(alpha, DefconPalette.DefconTwo));

			smallFont.DrawText(text, new float2(box.X + 17, box.Y + 2), DefconPalette.DefconTwo);
		}

		// WHOSE POSITION IS DRAWN. The exchange is per SIDE, so this block is no longer one number the
		// whole match shares -- it is the viewer's own side's. RenderPlayer first so an observer or a
		// replay following a player draws that player's, falling back to LocalPlayer.
		//
		// READOUT ONLY. RenderPlayer and LocalPlayer are per-client and MUST NOT reach the simulation;
		// nothing below writes anything.
		Player Viewer => world.RenderPlayer ?? world.LocalPlayer;

		void DrawNuclear(int bottom, int contentWidth)
		{
			var releaseOpen = escalation.NuclearReleaseOpen;
			var viewer = Viewer;
			var ownSide = exchange?.SideOf(viewer) ?? 0;
			var enemySide = exchange?.OpposingSideOf(viewer) ?? 0;

			var permanent = exchange?.PermanentLevelForSide(ownSide) ?? (int)NuclearRung.Hold;
			var windowTicks = exchange?.WindowTicksRemainingForSide(ownSide) ?? 0;
			var windowBand = windowTicks > 0 ? exchange.WindowLevelForSide(ownSide) : (int)NuclearRung.Hold;

			// BOTH ROWS ARE DRAWN WHENEVER THERE ARE TWO SIDES, and only then. A one-sided match --
			// a scenario with a single combatant, or a stripped trait -- gets the viewer's row alone
			// rather than an "ENEMY" row of five dark boxes, which would be a claim about an opponent
			// who does not exist.
			var hasEnemy = exchange != null && enemySide != ownSide;
			var rows = hasEnemy ? 2 : 1;

			var footLines = WrapLines(DefconReadoutModel.NuclearFootLine(releaseOpen, windowTicks > 0), contentWidth, ruleFont);

			var labelHeight = smallFont.Measure(LevelHeightSample).Y;
			var height = PadTop + labelHeight + 7
				+ (rows * LedgerRowHeight) + ((rows - 1) * LedgerRowGap) + BlockGap
				+ (footLines.Count * LineHeight(ruleFont)) + PadBottom;

			var bounds = new Rectangle(RenderBounds.X, bottom - height, Bounds.Width, height);
			DrawPanel(bounds, DefconPalette.NuclearAccent);

			var x = bounds.X + PadX;
			var y = bounds.Y + PadTop;

			smallFont.DrawText("NUCLEAR RELEASE", new float2(x, y), DefconPalette.NuclearLabel);

			// THE GATE'S COUNTDOWN TAKES THE CURRENT-YIELD SLOT while the gate is shut, because until
			// it opens the yield is HOLD and saying so twice tells the player nothing. This is the
			// ten-minute wait the mode used to serve with a blank screen.
			//
			// AN OPEN RETALIATION WINDOW TAKES IT INSTEAD, for the same reason the other way round:
			// the window's own box in the ledger below is one of five and carries a small clock, and
			// this slot is the largest text in the block. The thing a player about to lose a grant
			// needs is the number, at the size they will see without looking for it.
			//
			// PITFALL: GameSpeed.Timestep, not world.Timestep -- see DrawStrip.
			string valueText;
			if (!releaseOpen)
				valueText = $"RELEASE IN {WidgetUtils.FormatTime(escalation.TicksUntilNuclearRelease, false, world.GameSpeed.Timestep)}";
			else if (windowTicks > 0)
				valueText = $"{DefconReadoutModel.RungLabel(windowBand)} FOR " +
					$"{WidgetUtils.FormatTime(windowTicks, false, world.GameSpeed.Timestep)}";
			else
				valueText = DefconReadoutModel.RungLabel(permanent);

			var valueSize = smallFont.Measure(valueText);
			smallFont.DrawText(valueText, new float2(x + contentWidth - valueSize.X, y), DefconPalette.NuclearValue);

			y += labelHeight + 7;

			// ---- THE LEDGER ---------------------------------------------------------------------
			// YOUR ROW FIRST AND THEIRS SECOND, always, on every client. Not registration order: the
			// player reads their own position first and then compares, and a ledger whose rows
			// swapped depending on which seat you were in would make two players describing the same
			// screen to each other disagree about which row was which.
			DrawLedgerRow(x, y, contentWidth, ownSide, true, releaseOpen);

			if (hasEnemy)
				DrawLedgerRow(x, y + LedgerRowHeight + LedgerRowGap, contentWidth, enemySide, false, releaseOpen);

			y += (rows * LedgerRowHeight) + ((rows - 1) * LedgerRowGap) + BlockGap;

			foreach (var line in footLines)
			{
				ruleFont.DrawText(line, new float2(x, y), DefconPalette.Foot);
				y += LineHeight(ruleFont);
			}
		}

		/// <summary>One side's whole nuclear position, as a labelled row of band boxes.</summary>
		void DrawLedgerRow(int x, int y, int contentWidth, int side, bool isViewerSide, bool releaseOpen)
		{
			var label = DefconReadoutModel.LedgerRowLabel(world.LocalPlayer != null, isViewerSide,
				exchange?.SideNameFor(side));

			var labelSize = smallFont.Measure(label);
			smallFont.DrawText(label, new float2(x, y + ((LedgerRowHeight - labelSize.Y) / 2)),
				isViewerSide ? DefconPalette.NuclearValue : DefconPalette.NuclearLabel);

			var permanent = exchange?.PermanentLevelForSide(side) ?? (int)NuclearRung.Hold;
			var windowTicks = exchange?.WindowTicksRemainingForSide(side) ?? 0;
			var windowBand = windowTicks > 0 ? exchange.WindowLevelForSide(side) : (int)NuclearRung.Hold;

			var bands = DefconReadoutModel.LedgerBands();
			var boxesX = x + LedgerLabelWidth;
			var boxesWidth = contentWidth - LedgerLabelWidth;
			var count = bands.Count;
			var boxWidth = (boxesWidth - ((count - 1) * StepGap)) / count;

			for (var i = 0; i < count; i++)
			{
				// ---- IS THIS BAND COMING BACK, AND WHEN --------------------------------------
				// Wired at the `cc1cbe78` merge. Before it, nothing nuclear had a timer at all --
				// every power was RequiresPurchase, which forces TotalTicks to 0
				// (SupportPowerManager.cs:229) -- so there was no countdown in the engine for a
				// readout to show. `ce397d9f` made Escalation's bands free and put them on per-band
				// regeneration, and RegenTicksRemainingForSide is the one place that is counted.
				//
				// A NEGATIVE ANSWER IS NOT A ZERO. -1 means this side has no power at this band to
				// ask about, which is a different fact from "ready now" and must not draw a clock:
				// a box captioned 0:00 that never moves is a readout inventing a countdown. Held is
				// the honest cell in that case, and it is also what a band drawn dark would ignore
				// anyway.
				var regen = exchange?.RegenTicksRemainingForSide(side, (int)bands[i]) ?? -1;
				var charging = regen > 0;

				var cell = DefconReadoutModel.CellFor(bands[i], permanent, windowBand,
					windowTicks > 0, releaseOpen, charging);

				var rect = new Rectangle(boxesX + (i * (boxWidth + StepGap)), y, boxWidth, LedgerRowHeight);

				// THE WINDOW'S CLOCK WINS WHERE BOTH EXIST, and both can: a granted band may also be
				// regenerating from an earlier shot. The window is the one that ENDS THE PERMISSION,
				// so it is the number the player has to act on -- a box counting down to its next
				// warhead while the right to fire it expires sooner would be telling the player the
				// less urgent of the two facts.
				var clock = cell == DefconReadoutModel.LedgerCell.Window ? windowTicks
					: cell == DefconReadoutModel.LedgerCell.Charging ? regen : 0;

				DrawLedgerBox(rect, DefconReadoutModel.LedgerRungLabel(bands[i]), cell, clock);
			}
		}

		void DrawLedgerBox(Rectangle rect, string label, DefconReadoutModel.LedgerCell cell, int clockTicks)
		{
			Color fill, edge, text;
			switch (cell)
			{
				// THE BRIGHTEST STATE IS "THEY HAVE THIS, NOW". It reuses the step row's CURRENT
				// colours rather than a new pair, because that is what those colours already mean --
				// the band in force -- and the ledger is the same claim said twice, once per side.
				case DefconReadoutModel.LedgerCell.Held:
					(fill, edge, text) = (DefconPalette.StepCurrentFill, DefconPalette.StepCurrentEdge, DefconPalette.StepCurrentText);
					break;

				// Held but not available: dimmer, and carrying the clock that says when it is back.
				case DefconReadoutModel.LedgerCell.Charging:
					(fill, edge, text) = (DefconPalette.StepReleasedFill, DefconPalette.StepReleasedEdge, DefconPalette.NuclearValue);
					break;

				// A GRANT, IN THE CEILING'S COLOURS, which is the reading the single step row already
				// used for this exact band: the one step above a side's own that is momentarily
				// reachable. Its border also pulses -- see below -- because a grant is the only cell
				// on this panel that is disappearing while you look at it.
				case DefconReadoutModel.LedgerCell.Window:
					(fill, edge, text) = (DefconPalette.StepCeilingFill, DefconPalette.StepCeilingEdge, DefconPalette.StepCeilingText);
					break;

				default:
					(fill, edge, text) = (DefconPalette.StepFill, DefconPalette.StepEdge, DefconPalette.StepText);
					break;
			}

			WidgetUtils.FillRectWithColor(rect, fill);

			if (cell == DefconReadoutModel.LedgerCell.Window)
			{
				// The trigger line's pulse, at the same period and for the same reason: a triangle
				// wave over 27 ticks is 1.62 s at the 60 ms timestep, the mockup's 1.6 s. Game.LocalTick
				// rather than wall-clock, so it stops when the game does -- a heartbeat on a paused
				// game reads as a hung UI.
				var phase = (Game.LocalTick % 27) / 27f;
				var pulse = phase < 0.5f ? phase * 2 : 2 - (phase * 2);
				DrawBorder(rect, Color.FromArgb((int)(96 + (159 * pulse)), edge));
			}
			else
				DrawBorder(rect, edge);

			var labelSize = smallFont.Measure(label);
			var hasClock = clockTicks > 0;

			// WITH A CLOCK THE LABEL SITS HIGH AND THE CLOCK UNDER IT; WITHOUT ONE THE LABEL CENTRES.
			// A box that kept the label in the high position when there was nothing beneath it would
			// leave the row looking mis-aligned in the common case, which is every box most of the time.
			var labelY = hasClock
				? rect.Y + 2
				: rect.Y + ((rect.Height - labelSize.Y) / 2);

			smallFont.DrawText(label, new float2(rect.X + ((rect.Width - labelSize.X) / 2), labelY), text);

			if (!hasClock)
				return;

			// PITFALL: GameSpeed.Timestep, not world.Timestep -- see DrawStrip.
			var clock = WidgetUtils.FormatTime(clockTicks, false, world.GameSpeed.Timestep);
			var clockSize = smallFont.Measure(clock);
			smallFont.DrawText(clock,
				new float2(rect.X + ((rect.Width - clockSize.X) / 2), rect.Bottom - clockSize.Y - 2),
				DefconPalette.NuclearValue);
		}

		static void DrawPanel(Rectangle bounds, Color accent)
		{
			WidgetUtils.FillRectWithColor(bounds, DefconPalette.PanelBackground);
			DrawBorder(bounds, DefconPalette.Edge);
			WidgetUtils.FillRectWithColor(new Rectangle(bounds.X, bounds.Y, AccentWidth, bounds.Height), accent);
		}

		static void DrawBorder(Rectangle r, Color c)
		{
			WidgetUtils.FillRectWithColor(new Rectangle(r.X, r.Y, r.Width, 1), c);
			WidgetUtils.FillRectWithColor(new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
			WidgetUtils.FillRectWithColor(new Rectangle(r.X, r.Y, 1, r.Height), c);
			WidgetUtils.FillRectWithColor(new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
		}

		internal static int LineHeight(SpriteFont font)
		{
			return font.Measure(LevelHeightSample).Y + 2;
		}

		internal static IReadOnlyList<string> WrapLines(string text, int width, SpriteFont font)
		{
			return WidgetUtils.WrapText(text, width, font).Split('\n');
		}
	}
}
