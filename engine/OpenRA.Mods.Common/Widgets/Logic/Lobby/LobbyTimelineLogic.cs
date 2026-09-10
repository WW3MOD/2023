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
 * WHAT THE TIMELINE'S MARKERS MEAN. TimelineWidget owns pixels and TimelineModel owns arithmetic;
 * this owns the binding to real lobby options.
 *
 * ==== WHY THIS IS NOT PART OF LobbyOptionsLogic ====
 * That class dispatches on `options[start] is LobbyBooleanOption` and then locates the row's
 * children BY C# TYPE -- `child is CheckboxWidget`, `child is DropDownButtonWidget`
 * (LobbyOptionsLogic.cs:513, :574). A custom widget in either queue makes the queue underflow and
 * throw. There is no third branch and no extension point, so the timeline is a sibling panel that
 * owns its own option ids and writes them directly. That is the same shape LobbyPresetLogic and the
 * test-mode stager already use: the wire protocol is `option <id> <value>` and does not know or
 * care what UI produced the string.
 *
 * ==== EVERY STOP COMES OUT OF THE OPTION'S OWN Values DICTIONARY ====
 * Nothing here invents a value. The stop lists are built by ENUMERATING `option.Values` and
 * positioning each existing key on the axis; a key the option does not define cannot be produced by
 * dragging, because there is no stop for it. See TimelineModel for why that matters more than it
 * looks: an out-of-set value throws KeyNotFoundException on the next client join.
 *
 * ==== A PLACEHOLDER OPTION'S MARKER IS DIMMED AND DEAD TO THE MOUSE ====
 * LobbyOptionsLogic disables both the checkbox (:557) and the dropdown (:609) for
 * LobbyOption.Placeholder, with a stated reason: an accepted order resets EVERY client to NotReady
 * and posts a chat line, which is a disruptive consequence for a control that governs nothing. A
 * timeline that ignored the flag would be a back door onto exactly the options the sibling panel
 * deliberately made dead, so TimelineMarker carries it and TimelineMarker.IsDraggable honours it.
 *
 * AT THE TIME OF WRITING THAT MAKES THE TWO DEFCON MARKERS UNDRAGGABLE, because
 * DefconEscalationInfo.MarkAsPlaceholder is still true -- and its own [Desc] says flipping it is "a
 * release decision rather than a code one" and "the last step of the feature". So this is not a
 * defect to fix here: the day that field goes false, both markers become live with no change to
 * this file.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class LobbyTimelineLogic : ChromeLogic
	{
		// The mockup's axis is 0-60 minutes. It is a FLOOR, not a fixed span: see Rebuild for why the
		// shipped `timelimit` set pushes it wider.
		const int MinimumAxisSeconds = 3600;

		readonly TimelineWidget timeline;
		readonly OrderManager orderManager;
		readonly Func<MapPreview> getMap;
		readonly Func<bool> configurationDisabled;

		readonly Dictionary<string, PredictedCachedTransform<Session.Global, string>> optionValues = new();

		MapPreview mapPreview;
		TimelineMarker[] markers = Array.Empty<TimelineMarker>();
		TimelineBand[] bands = Array.Empty<TimelineBand>();
		int axisSeconds = MinimumAxisSeconds;
		string lastTimestepKey;

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
			this.configurationDisabled = configurationDisabled;

			timeline.GetMarkers = () => markers;
			timeline.GetBands = () => bands;
			timeline.GetAxisSeconds = () => axisSeconds;
			timeline.GetValue = ReadValue;
			timeline.OnSet = Set;
			timeline.IsDisabled = () => configurationDisabled();

			// A map whose rules carry none of these options draws nothing at all rather than an empty
			// bar captioned with times it cannot set.
			timeline.IsVisible = () => markers.Length > 0;

			mapPreview = getMap();
			Rebuild();
		}

		public override void Tick()
		{
			var newMapPreview = getMap();
			var timestepKey = SelectedSpeedId();

			// The game speed is part of the arithmetic, not just the flavour: pace stops are stored in
			// TICKS and become seconds through the timestep, while `timelimit` is stored in minutes.
			// Mixing the two on one axis is only consistent if a speed change rebuilds.
			if (newMapPreview == mapPreview && timestepKey == lastTimestepKey && resolved)
				return;

			Game.RunAfterTick(() =>
			{
				mapPreview = newMapPreview;
				Rebuild();
			});
		}

		string ReadValue(TimelineMarker marker)
		{
			if (marker.OptionId == null || !optionValues.TryGetValue(marker.OptionId, out var cached))
				return null;

			return cached.Update(orderManager.LobbyInfo.GlobalSettings);
		}

		void Set(TimelineMarker marker, string value)
		{
			if (marker.OptionId == null || value == null || configurationDisabled())
				return;

			orderManager.IssueOrder(Order.Command($"option {marker.OptionId} {value}"));

			// Predict as the CHECKBOX does (LobbyOptionsLogic.cs:562), not as the dropdown does. The
			// dropdown deliberately does not predict and can afford not to, because its list is closed
			// while it waits for the server. A marker the host is still looking at would visibly jump
			// back to where it was and then forward again, which reads as the drag having failed.
			if (optionValues.TryGetValue(marker.OptionId, out var cached))
				cached.Predict(value);
		}

		string SelectedSpeedId()
		{
			var speeds = Game.ModData.Manifest.Get<GameSpeeds>();
			return orderManager.LobbyInfo.GlobalSettings.OptionOrDefault("gamespeed", speeds.DefaultSpeed);
		}

		int Timestep()
		{
			var speeds = Game.ModData.Manifest.Get<GameSpeeds>();
			var id = SelectedSpeedId();
			if (id != null && speeds.Speeds.TryGetValue(id, out var speed))
				return speed.Timestep;

			return speeds.Speeds[speeds.DefaultSpeed].Timestep;
		}

		bool OptionIsTrue(string id, bool def)
		{
			return orderManager.LobbyInfo.GlobalSettings.OptionOrDefault(id, def);
		}

		string RawOption(string id, string def)
		{
			return orderManager.LobbyInfo.GlobalSettings.OptionOrDefault(id, def);
		}

		void Rebuild()
		{
			lastTimestepKey = SelectedSpeedId();
			markers = Array.Empty<TimelineMarker>();
			bands = Array.Empty<TimelineBand>();

			resolved = mapPreview?.WorldActorInfo != null && mapPreview.PlayerActorInfo != null;
			if (!resolved)
				return;

			var options = new Dictionary<string, LobbyOption>();
			foreach (var option in mapPreview.PlayerActorInfo.TraitInfos<ILobbyOptions>()
				.Concat(mapPreview.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(mapPreview)))
				options.TryAdd(option.Id, option);

			var defcon = mapPreview.WorldActorInfo.TraitInfoOrDefault<DefconEscalationInfo>();
			var timestep = Timestep();

			var built = new List<TimelineMarker>();

			// ---- MARKER 0: THE LINE LIFTS ----------------------------------------------------
			// `defcon-pace` is the only lobby option that sets how long the no-rush period runs. Its
			// three keys are positioned from the trait's own SlowTicks/StandardTicks/FastTicks, so the
			// bar shows the durations the match will actually use rather than a label.
			var paceStops = Array.Empty<TimelineStop>();
			var paceDefault = 0;
			var pacePlaceholder = false;
			if (defcon != null && options.TryGetValue(DefconEscalationInfo.PaceOptionId, out var pace))
			{
				var stops = new List<TimelineStop>();
				foreach (var kv in pace.Values)
				{
					if (!Enum.TryParse<DefconPace>(kv.Key, true, out var parsed))
						continue;

					var seconds = TimelineModel.TicksToSeconds(defcon.TicksAtDefconThree(parsed), timestep);
					stops.Add(new TimelineStop(seconds, kv.Key, TimelineModel.Clock(seconds)));
				}

				stops.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
				paceStops = stops.ToArray();
				paceDefault = Math.Max(0, Array.FindIndex(paceStops, s => s.Value == pace.DefaultValue));
				pacePlaceholder = pace.Placeholder;

				if (paceStops.Length > 0)
				{
					Track(pace);
					built.Add(new TimelineMarker(DefconEscalationInfo.PaceOptionId, "The line lifts",
						paceStops, paceDefault, placeholder: pacePlaceholder));
				}
			}

			// ---- MARKER 1: FIRST WARHEADS ----------------------------------------------------
			// AN OFFSET FROM MARKER 0, WHICH IS THE USER'S RULING (2026-09-10) AND ALSO WHAT THE CODE
			// DOES: NuclearReleaseDelayTicks counts from the moment DEFCON 1 is REACHED, not from match
			// start (DefconEscalation.cs:142-151). Dragging the no-rush marker carries this one with it.
			//
			// IT IS NOT DRAGGABLE, AND THAT IS A FINDING RATHER THAN AN OMISSION: NuclearReleaseDelayTicks
			// is a trait field with NO lobby option behind it, deliberately -- its own [Desc] at :119-124
			// says "NOT A LOBBY OPTION, deliberately". There is therefore no Values dictionary to snap to,
			// and inventing one would be a design change rather than an implementation choice. It is drawn
			// because the host still needs to see WHERE the warheads land on the bar.
			if (defcon != null && built.Count > 0)
			{
				var delay = TimelineModel.TicksToSeconds(defcon.NuclearReleaseDelayTicks, timestep);
				built.Add(new TimelineMarker(null, "First warheads",
					new[] { new TimelineStop(delay, null, TimelineModel.Offset(delay)) }, 0,
					relativeTo: 0, highlight: true, placeholder: pacePlaceholder));
			}

			// ---- MARKER 2: MATCH ENDS --------------------------------------------------------
			// `timelimit` ships 0/10/20/30/40/60/90 minutes (TimeLimitManager.cs:32), so the axis is
			// widened to reach 90 rather than the mockup's 60. Narrowing it to 60 would put the 90
			// stop off the end of the bar and make it unreachable by drag, which would be a REGRESSION
			// against the dropdown this replaces. Trimming or widening that value set is a design
			// change and is not made here.
			var warheadReach = paceStops.Length > 0 && defcon != null
				? paceStops[^1].Seconds + TimelineModel.TicksToSeconds(defcon.NuclearReleaseDelayTicks, timestep)
				: 0;

			var limitSeconds = new List<int>();
			if (options.TryGetValue("timelimit", out var timeLimit))
				foreach (var kv in timeLimit.Values)
					if (int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
						limitSeconds.Add(minutes * 60);

			axisSeconds = TimelineModel.AxisSecondsFor(limitSeconds.Append(warheadReach), MinimumAxisSeconds);

			if (timeLimit != null)
			{
				var stops = new List<TimelineStop>();
				foreach (var kv in timeLimit.Values)
				{
					if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
						continue;

					// "No limit" is 0 minutes, which as a POSITION would draw the marker hard against
					// the left edge and read as "the match ends immediately". It is placed at the far
					// right instead, where "the match runs off the end of the bar" is what it means.
					// Its stored value is still the string "0" — only the drawn position moves.
					var seconds = minutes > 0 ? minutes * 60 : axisSeconds;
					stops.Add(new TimelineStop(seconds, kv.Key, minutes > 0 ? TimelineModel.Clock(seconds) : kv.Value));
				}

				stops.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
				if (stops.Count > 0)
				{
					var array = stops.ToArray();
					Track(timeLimit);
					built.Add(new TimelineMarker("timelimit", "Match ends", array,
						Math.Max(0, Array.FindIndex(array, s => s.Value == timeLimit.DefaultValue)),
						placeholder: timeLimit.Placeholder));
				}
			}

			markers = built.ToArray();
			bands = BuildBands(markers);

			// The hint says how many markers can actually be moved, so it never invites a drag that
			// does nothing. See the file header for why that count is currently one.
			var draggable = markers.Count(m => m.IsDraggable);
			timeline.NoteHint = draggable switch
			{
				0 => "not configurable on this map",
				1 => "drag the marker",
				2 => "drag either marker",
				_ => "drag any marker",
			};
		}

		void Track(LobbyOption option)
		{
			if (optionValues.ContainsKey(option.Id))
				return;

			var id = option.Id;
			var fallback = option.DefaultValue;
			optionValues[id] = new PredictedCachedTransform<Session.Global, string>(
				gs => gs.LobbyOptions.TryGetValue(id, out var state) ? state.Value : fallback);
		}

		// THE WORD "DEFCON" APPEARS NOWHERE IN ANY OF THESE STRINGS, and that is a user ruling
		// (decision 18), not a style preference: the lobby says what HAPPENS and the game says what it
		// is CALLED. The in-game readout keeps the name.
		//
		// The two mode variants are both taken from the approved mockup — tab 1 is Escalation and tab 2
		// is Skirmish — rather than written here, so nothing on the bar is wording nobody signed off.
		TimelineBand[] BuildBands(TimelineMarker[] built)
		{
			if (built.Length == 0)
				return Array.Empty<TimelineBand>();

			var lineLifts = Array.FindIndex(built, m => m.OptionId == DefconEscalationInfo.PaceOptionId);
			var warheads = Array.FindIndex(built, m => m.OptionId == null);
			var ends = Array.FindIndex(built, m => m.OptionId == "timelimit");

			var result = new List<TimelineBand>();

			if (lineLifts >= 0)
				result.Add(new TimelineBand(-1, lineLifts, () => "NO RUSH",
					TimelinePalette.NoRushFill, TimelinePalette.NoRushInk));

			if (lineLifts >= 0 && warheads >= 0)
				result.Add(new TimelineBand(lineLifts, warheads, () => "CONVENTIONAL",
					TimelinePalette.ConventionalFill, TimelinePalette.ConventionalInk));

			if (warheads >= 0)
				result.Add(new TimelineBand(warheads, ends, WarheadBandText,
					TimelinePalette.WarheadFill, TimelinePalette.WarheadInk));

			if (ends >= 0)
				result.Add(new TimelineBand(ends, -1, EndingBandText,
					TimelinePalette.EndingFill, TimelinePalette.EndingInk));

			return result.ToArray();
		}

		string WarheadBandText()
		{
			var mode = RawOption(DefconEscalationInfo.ModeOptionId, nameof(DefconGameMode.Skirmish).ToLowerInvariant());
			return string.Equals(mode, nameof(DefconGameMode.Escalation), StringComparison.OrdinalIgnoreCase)
				? "WARHEADS ISSUED · EITHER SIDE MAY FIRE"
				: "NUCLEAR WEAPONS PURCHASABLE";
		}

		// The tail band states which of the two endings the host has actually configured. `doomsday`
		// keeps its wire-visible id: renaming a lobby option id silently discards every stored value,
		// and this one has already survived three renames of the copy in front of it.
		string EndingBandText()
		{
			return OptionIsTrue(DoomsdayStrikeInfo.DoomsdayOptionId, true) ? "NUCLEAR ENDING" : "ENDS ON SCORE";
		}
	}
}
