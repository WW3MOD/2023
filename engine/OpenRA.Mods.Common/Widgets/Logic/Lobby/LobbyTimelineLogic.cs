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
 * WHAT THE TIMELINE'S BANDS MEAN. TimelineWidget owns pixels and TimelineModel owns the types; this
 * owns the reading of real lobby options.
 *
 * ==== IT READS AND NEVER WRITES (user ruling 2026-09-13) ====
 * The bar was the panel's primary control and is now a read-only overview; the dropdowns lower down
 * are the source of truth. So the Set/OnSet path, the PredictedCachedTransform prediction and the
 * per-marker option binding are all gone -- not disabled, gone. What is left reads
 * `orderManager.LobbyInfo.GlobalSettings` every frame, which is the same live state the dropdowns
 * render from, so the bar cannot disagree with the control that set it.
 *
 * THE PREDICTION WENT WITH THE WRITE AND THAT IS NOT A REGRESSION. It existed because a marker the
 * host was still looking at would visibly jump back to the server's value and then forward again
 * mid-drag. Nothing here is dragged now: the host clicks a dropdown, the order round-trips while
 * the list is closed, and the bar redraws with everything else -- which is exactly what the
 * dropdown itself does and deliberately does not predict (LobbyOptionsLogic).
 *
 * ==== WHY THIS IS NOT PART OF LobbyOptionsLogic ====
 * That class dispatches on `options[start] is LobbyBooleanOption` and then locates the row's
 * children BY C# TYPE -- `child is CheckboxWidget`, `child is DropDownButtonWidget`. A custom
 * widget in either queue makes the queue underflow and throw. There is no third branch and no
 * extension point, so the timeline is a sibling panel that reads its own option ids.
 *
 * ==== THE TWO MODES DRAW DIFFERENT AXES BECAUSE THEY MEASURE DIFFERENT THINGS ====
 * SKIRMISH is entirely absolute: every boundary is a time on the match clock (the unlock intervals,
 * the time limit), so it keeps the minute ruler.
 *
 * ESCALATION is not, and cannot be made so. Its middle phase ends when somebody takes the first
 * kill, and no clock runs during it. Drawing that on an absolute ruler would put a minute number
 * under a boundary that has none -- so Escalation draws PHASE LENGTHS with no ruler, and the peace
 * band is hatched at a nominal width with a caption that says it ends on an event. That is the
 * user's own framing of the ruling: "it can show how long each phase is and that will be enough".
 *
 * THE ONE ABSOLUTE NUMBER IN THE ESCALATION LAYOUT IS THE TIME LIMIT, and it is captioned as a
 * clock time rather than a length for exactly that reason -- it is the one thing on the bar that is
 * a moment rather than a duration, and TimelineModel.Offset's plus sign on the phases around it is
 * what keeps the distinction visible.
 *
 * ==== REBUILD ON A MODE CHANGE, NOT JUST ON A MAP OR SPEED CHANGE ====
 * Which bands exist at all is baked at Rebuild; only their captions are delegates read live. So the
 * mode is part of Tick's change detection alongside the timestep. Without that, a host switching
 * Escalation <-> Skirmish would keep the other mode's bar for the rest of the lobby -- silently,
 * and looking exactly like a working one.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class LobbyTimelineLogic : ChromeLogic
	{
		// The mockup's axis is 0-60 minutes. It is a FLOOR for the RULED layout, not a fixed span:
		// the shipped `timelimit` set reaches 90 and pushes it wider.
		const int MinimumAxisSeconds = 3600;

		readonly TimelineWidget timeline;
		readonly OrderManager orderManager;
		readonly Func<MapPreview> getMap;

		MapPreview mapPreview;
		TimelineLayout layout = TimelineLayout.Empty;
		string lastTimestepKey;
		string lastModeKey;

		// Whether the last Rebuild saw a map preview with its actor info populated. A MapPreview is
		// MUTATED IN PLACE as it validates rather than replaced, so "has the map changed?" is false
		// forever if the ctor happened to run while the preview was still loading — and the timeline
		// would then stay invisible for the whole lobby with nothing to indicate why. Retrying while
		// unresolved costs one comparison per tick and only until the preview lands.
		bool resolved;

		[ObjectCreator.UseCtor]
		internal LobbyTimelineLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			timeline = (TimelineWidget)widget;
			this.orderManager = orderManager;
			this.getMap = getMap;

			timeline.GetLayout = () => layout;
			timeline.IsDisabled = () => configurationDisabled();

			// DO NOT MAKE THIS WIDGET'S VISIBILITY DEPEND ON ANYTHING Rebuild() PRODUCES. That is not a
			// style rule, it is a deadlock, and it shipped once (2026-09-10) and blanked the bar in the
			// live lobby:
			//
			//     Widget.TickOuter (Widget.cs:512-524) ticks a widget's LogicObjects ONLY inside
			//     `if (IsVisible())`. So `IsVisible = () => bands.Length > 0` is self-latching --
			//     the moment the constructor's Rebuild comes up empty (a map preview whose rules have
			//     not finished loading is enough), the widget is invisible, Tick() is therefore never
			//     called, Rebuild() never runs again, and it stays empty for the rest of the lobby.
			//     It cannot recover, and it fails silently: no exception, no log line, just a
			//     reserved gap with nothing in it.
			//
			// The `resolved` retry below was written for exactly that case and was rendered dead by
			// that line, which is the tell -- a retry that never fires is usually gated on the thing
			// it was meant to repair. Visibility stays at the Widget default of true and
			// TimelineWidget.Draw() no-ops while it has nothing to draw.

			mapPreview = getMap();
			Rebuild();
		}

		public override void Tick()
		{
			var newMapPreview = getMap();
			var timestepKey = SelectedSpeedId();
			var modeKey = SelectedModeId();

			// The game speed is part of the arithmetic, not just the flavour: a scenario's tick
			// overrides become seconds through the timestep. And the MODE, because it decides which
			// bands exist at all -- see the file header.
			if (newMapPreview == mapPreview && timestepKey == lastTimestepKey && modeKey == lastModeKey && resolved)
				return;

			Game.RunAfterTick(() =>
			{
				mapPreview = newMapPreview;
				Rebuild();
			});
		}

		Session.Global Settings => orderManager.LobbyInfo.GlobalSettings;

		string SelectedSpeedId()
		{
			var speeds = Game.ModData.Manifest.Get<GameSpeeds>();
			return Settings.OptionOrDefault("gamespeed", speeds.DefaultSpeed);
		}

		int Timestep()
		{
			var speeds = Game.ModData.Manifest.Get<GameSpeeds>();
			var id = SelectedSpeedId();
			if (id != null && speeds.Speeds.TryGetValue(id, out var speed))
				return speed.Timestep;

			return speeds.Speeds[speeds.DefaultSpeed].Timestep;
		}

		/// <summary>The game mode as a wire value. Skirmish is the fallback because it is the default.</summary>
		string SelectedModeId()
		{
			return Settings.OptionOrDefault(DefconEscalationInfo.ModeOptionId, nameof(DefconGameMode.Skirmish).ToLowerInvariant());
		}

		bool IsEscalation()
		{
			return string.Equals(SelectedModeId(), nameof(DefconGameMode.Escalation), StringComparison.OrdinalIgnoreCase);
		}

		int Minutes(string id, int fallback)
		{
			return LobbyPhaseConsistency.Minutes(Settings, id, fallback);
		}

		void Rebuild()
		{
			lastTimestepKey = SelectedSpeedId();
			lastModeKey = SelectedModeId();
			layout = TimelineLayout.Empty;

			resolved = mapPreview?.WorldActorInfo != null && mapPreview.PlayerActorInfo != null;
			if (!resolved)
				return;

			var options = new Dictionary<string, LobbyOption>();
			foreach (var option in mapPreview.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(mapPreview.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(mapPreview)))
				options.TryAdd(option.Id, option);

			var defcon = mapPreview.WorldActorInfo.TraitInfoOrDefault<DefconEscalationInfo>();
			if (defcon == null)
				return;

			layout = IsEscalation()
				? BuildEscalation(defcon, options)
				: BuildSkirmish(options);
		}

		/// <summary>The time limit in seconds, or 0 for "No limit".</summary>
		int TimeLimitSeconds()
		{
			var minutes = Minutes(LobbyPhaseConsistency.TimeLimitOptionId, 0);
			return minutes > 0 ? minutes * 60 : 0;
		}

		string EndingCaption()
		{
			return Settings.OptionOrDefault(DoomsdayStrikeInfo.DoomsdayOptionId, true) ? "NUCLEAR ENDING" : "ENDS ON SCORE";
		}

		// ==================== ESCALATION: PHASE LENGTHS, NO RULER ====================
		//
		// THE AXIS IS THE SUM OF THE BANDS AND IS NOT ROUNDED. AxisSecondsFor rounds up to a labelled
		// ruler tick, which is right for a ruled layout and wrong here: there is no ruler to align to,
		// and the rounding stub would draw as a phase past the last one that nobody captioned.
		TimelineLayout BuildEscalation(DefconEscalationInfo defcon, Dictionary<string, LobbyOption> options)
		{
			var timestep = Timestep();

			// THE CLOCKS COME BACK THROUGH THE TRAIT'S OWN CONVERTERS rather than being recomputed as
			// `minutes * 60` here, which is what makes the bar draw the clock the MATCH will use: a
			// scenario that pins NoRushTicksOverride is honoured on the bar exactly as it is honoured
			// in the state machine, and the minutes-to-ticks conversion cannot drift between them.
			var noRushSeconds = TimelineModel.TicksToSeconds(
				defcon.NoRushTicks(Minutes(DefconEscalationInfo.NoRushOptionId, defcon.NoRushDefault), timestep), timestep);

			var warheadSeconds = TimelineModel.TicksToSeconds(
				defcon.NuclearReleaseDelayTicks(Minutes(DefconEscalationInfo.FirstWarheadsOptionId, defcon.FirstWarheadsDefault), timestep), timestep);

			var limitSeconds = TimeLimitSeconds();

			// The two open-ended bands are sized from the two the host DID set, so every band keeps
			// its share of the bar at every setting. See TimelineModel for why that is not cosmetic.
			var configured = noRushSeconds + warheadSeconds;
			var peace = TimelineModel.IndeterminateNominalSeconds(configured);
			var exchange = TimelineModel.OpenEndedNominalSeconds(configured);

			var bands = new List<TimelineBand>();
			var cursor = 0;

			cursor = Append(bands, cursor, noRushSeconds, () => "NO RUSH", () => TimelineModel.Clock(noRushSeconds),
				TimelinePalette.NoRushFill, TimelinePalette.NoRushInk);

			// THE EVENT BOUNDARY. Its width is a share of the clocks around it and means NOTHING; the
			// hatch and the caption are what carry that. Never caption this with a clock.
			cursor = Append(bands, cursor, peace, () => "CEASE-FIRE",
				() => "until the first kill", TimelinePalette.PeaceFill, TimelinePalette.PeaceInk, indeterminate: true);

			cursor = Append(bands, cursor, warheadSeconds, () => "OPEN WAR",
				() => "first warheads " + TimelineModel.Offset(warheadSeconds),
				TimelinePalette.ConventionalFill, TimelinePalette.ConventionalInk);

			cursor = Append(bands, cursor, exchange, () => "NUCLEAR EXCHANGE",
				() => "either side may fire", TimelinePalette.WarheadFill, TimelinePalette.WarheadInk);

			// The tail band exists only when the host has set a limit. With "No limit" the match ends
			// on a Supply Route rather than on a clock, and drawing an ENDING span for it would put a
			// phase on the bar that no setting produces.
			//
			// ITS DETAIL READS "ends 10:00", NOT "10:00". This is the ONE absolute moment on a bar of
			// lengths, and a bare clock under the rightmost band reads as another duration -- which at
			// a short time limit prints a SMALLER number to the right of a larger one and looks like a
			// bug. The verb is what tells the host it is a point on the match clock.
			if (limitSeconds > 0)
				cursor = Append(bands, cursor, exchange, EndingCaption,
					() => "ends " + TimelineModel.Clock(limitSeconds), TimelinePalette.EndingFill, TimelinePalette.EndingInk);

			var hint = options.TryGetValue(DefconEscalationInfo.NoRushOptionId, out var noRush) && noRush.Placeholder
				? "not configurable on this map"
				: "phase lengths — set below";

			return new TimelineLayout(bands, cursor, false, hint, LobbyPhaseConsistency.Warning(Settings));
		}

		static int Append(List<TimelineBand> bands, int cursor, int length, Func<string> caption, Func<string> detail,
			Primitives.Color fill, Primitives.Color ink, bool indeterminate = false)
		{
			if (length <= 0)
				return cursor;

			bands.Add(new TimelineBand(cursor, cursor + length, caption, detail, fill, ink, indeterminate));
			return cursor + length;
		}

		// ==================== SKIRMISH: THE MATCH CLOCK, WITH A RULER ====================
		//
		// NO NO-RUSH BAND, AND THAT IS THE CORRECTION RATHER THAN AN OMISSION. DefconEscalation is a
		// strict no-op in Skirmish -- no level, no clock, no wall -- so the bar used to caption a
		// timer that never ran, in the DEFAULT mode. Decision 22 ruled that the fix for a false band
		// is to draw only what is true, and in Skirmish what is true is the unlock schedule and the
		// time limit.
		//
		// NO TIMESTEP CONVERSION HERE, and the asymmetry with the Escalation layout is real rather
		// than an oversight: `nuclear-unlock-interval` and `timelimit` both store MINUTES of real
		// time. Multiplying by 60 is the whole conversion; routing them through the timestep as well
		// would scale them twice.
		TimelineLayout BuildSkirmish(Dictionary<string, LobbyOption> options)
		{
			var unlockInfo = mapPreview.WorldActorInfo.TraitInfoOrDefault<NuclearUnlockClockInfo>();
			var intervalSeconds = 60 * Minutes(LobbyPhaseConsistency.UnlockIntervalOptionId, unlockInfo?.IntervalDefault ?? 0);
			var limitSeconds = TimeLimitSeconds();

			// THE LAST BAND THE BAR DRAWS IS THE HIGHEST TIER STILL TICKED, which is where the single
			// `nuclear-highest-yield` cap used to be read (decision 02 replaced it with four per-tier
			// checkboxes). It is the HIGHEST enabled one rather than the count of enabled ones,
			// because the clock still unlocks in band order: switching off 20 kt does not make 50 kt
			// arrive an interval sooner, it leaves a tier nobody may buy in the middle of the run.
			//
			// All four off is a legal lobby and means nothing is ever purchasable; the bar then draws
			// no tier bands at all, which is the honest picture rather than a hidden minimum of one.
			var defaults = unlockInfo?.PurchasableDefaults();
			var cap = 0;
			for (var i = 0; i < NuclearUnlockClockInfo.PurchasableOptionIds.Length; i++)
				if (Settings.OptionOrDefault(NuclearUnlockClockInfo.PurchasableOptionIds[i],
					defaults == null || defaults[i]))
					cap = NuclearUnlockSchedule.LowestRung + i;

			cap = cap > 0 ? NuclearUnlockSchedule.ClampCap(cap) : 0;
			var lastRungSeconds = intervalSeconds > 0 ? cap * intervalSeconds : 0;

			var axis = TimelineModel.AxisSecondsFor(new[] { limitSeconds, lastRungSeconds }, MinimumAxisSeconds);

			// Everything after the time limit is unreachable, so the bands stop there and the ending
			// band takes the rest. With no limit the bar runs to the end of the ruler.
			var end = limitSeconds > 0 ? limitSeconds : axis;

			var bands = new List<TimelineBand>();

			if (intervalSeconds <= 0)
			{
				// The no-wait opt-out: every tier up to the cap is on sale from the first second.
				bands.Add(new TimelineBand(0, end, () => "NUCLEAR WEAPONS PURCHASABLE",
					() => "from 0:00, up to " + DefconReadoutModel.RungLabel(cap),
					TimelinePalette.WarheadFill, TimelinePalette.WarheadInk));
			}
			else
			{
				bands.Add(new TimelineBand(0, Math.Min(intervalSeconds, end), () => "CONVENTIONAL",
					() => "no warheads on sale", TimelinePalette.ConventionalFill, TimelinePalette.ConventionalInk));

				// One band per rung, opening at rung x interval -- which is NuclearUnlockSchedule's own
				// arithmetic (TicksUntilRung divides where RungAt multiplies), read through the same
				// helper so the bar and the match cannot disagree about when the shop opens.
				for (var rung = NuclearUnlockSchedule.LowestRung; rung <= cap; rung++)
				{
					var from = rung * intervalSeconds;
					var to = rung == cap ? end : Math.Min((rung + 1) * intervalSeconds, end);
					if (from >= end)
						break;

					var label = DefconReadoutModel.RungLabel(rung);
					var opensAt = TimelineModel.Clock(from);
					bands.Add(new TimelineBand(from, to, () => label + " ON SALE", () => "from " + opensAt,
						TimelinePalette.WarheadFill, TimelinePalette.WarheadInk));
				}
			}

			if (limitSeconds > 0 && limitSeconds < axis)
				bands.Add(new TimelineBand(limitSeconds, axis, EndingCaption,
					() => TimelineModel.Clock(limitSeconds), TimelinePalette.EndingFill, TimelinePalette.EndingInk));

			var hint = options.ContainsKey(LobbyPhaseConsistency.UnlockIntervalOptionId)
				? "match clock — set below"
				: "not configurable on this map";

			return new TimelineLayout(bands, axis, true, hint, LobbyPhaseConsistency.Warning(Settings));
		}
	}
}
