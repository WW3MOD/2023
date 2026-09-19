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
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("This trait allows setting a time limit on matches. Attach this to the World actor.")]
	public class TimeLimitManagerInfo : TraitInfo, ILobbyOptions, IRulesetLoaded
	{
		[Desc("Label that will be shown for the time limit option in the lobby.")]
		public readonly string TimeLimitLabel = "Time Limit";

		[Desc("Tooltip description that will be shown for the time limit option in the lobby.")]
		public readonly string TimeLimitDescription = "Player or team with the highest score after this time wins";

		[Desc("Time Limit options that will be shown in the lobby dropdown. Values are in minutes.")]
		public readonly int[] TimeLimitOptions = { 0, 10, 20, 30, 40, 60, 90 };

		[Desc("List of remaining minutes of game time when a text and optional speech notification should be made to players.")]
		public readonly Dictionary<int, string> TimeLimitWarnings = new Dictionary<int, string>
		{
			{ 1, null },
			{ 2, null },
			{ 3, null },
			{ 4, null },
			{ 5, null },
			{ 10, null },
		};

		[Desc("Default selection for the time limit option in the lobby. Needs to use one of the TimeLimitOptions.")]
		public readonly int TimeLimitDefault = 0;

		[Desc("Prevent the time limit option from being changed in the lobby.")]
		public readonly bool TimeLimitLocked = false;

		[Desc("ABSOLUTE time limit in TICKS, overriding the lobby dropdown entirely when non-zero.",
			"",
			"Exists because the dropdown's finest grain is one minute, which is far longer than a",
			"demo or a test wants to sit through. Zero — the default — means the field is not in play",
			"at all and the lobby option is read exactly as before, so no shipped configuration",
			"changes. Intended for scenario rules.yaml overrides, not for the mod's own world.yaml.")]
		public readonly int TimeLimitTicks = 0;

		[Desc("Whether to display the options dropdown in the lobby.")]
		public readonly bool TimeLimitDropdownVisible = true;

		[Desc("Display order for the time limit dropdown in the lobby.")]
		public readonly int TimeLimitDisplayOrder = 4;

		[Desc("Notification text for time limit warnings. The string '{0}' will be replaced by the remaining time in minutes, '{1}' is used for the plural form.")]
		public readonly string Notification = "{0} minute{1} remaining.";

		[Desc("ID of the LabelWidget used to display a text ingame that will be updated every second.")]
		public readonly string CountdownLabel = null;

		[Desc("Text to be shown using the CountdownLabel. The string '{0}' will be replaced by the time in hh:mm:ss format.")]
		public readonly string CountdownText = null;

		[Desc("Will prevent showing/playing the built-in time limit warnings when set to true.")]
		public readonly bool SkipTimeRemainingNotifications = false;

		[Desc("Will prevent showing/playing the built-in timer expired notification when set to true.")]
		public readonly bool SkipTimerExpiredNotification = false;

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (!TimeLimitOptions.Contains(TimeLimitDefault))
				throw new YamlException("TimeLimitDefault must be a value from TimeLimitOptions");
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			var timelimits = TimeLimitOptions.ToDictionary(c => c.ToString(), c =>
			{
				if (c == 0)
					return "No limit";
				else
					return c.ToString() + $" minute{(c > 1 ? "s" : null)}";
			});

			yield return new LobbyOption("timelimit", TimeLimitLabel, TimeLimitDescription, TimeLimitDropdownVisible, TimeLimitDisplayOrder,
				timelimits, TimeLimitDefault.ToString(), TimeLimitLocked);
		}

		public override object Create(ActorInitializer init) { return new TimeLimitManager(init.Self, this); }
	}

	public class TimeLimitManager : INotifyTimeLimit, ITick, IWorldLoaded
	{
		readonly TimeLimitManagerInfo info;

		// THE MATCH'S CONFIGURED MILLISECONDS PER TICK, not a ticks-per-second rate. See the block
		// comment in the constructor for why there is no rate here any more.
		readonly int timestepMilliseconds;
		LabelWidget countdownLabel;
		CachedTransform<int, string> countdown;
		int ticksRemaining;

		public int TimeLimit;
		public string Notification;

		public TimeLimitManager(Actor self, TimeLimitManagerInfo info)
		{
			this.info = info;
			Notification = info.Notification;

			// ==== THE CLOCK IS HONEST AS OF 2026-09-19, AND IT USED TO RUN 4 % FAST ====
			// This line was `ticksPerSecond = 1000 / self.World.Timestep`, and every duration below
			// was built by multiplying that. It is INTEGER division: at this mod's 60 ms timestep it
			// gives 16, not 16.667, so a 90-minute Time Limit was 86400 ticks and expired at 86:24.
			// The countdown label derives from the same tick count, so NOTHING ON SCREEN DISAGREED --
			// the clock lied consistently, which is the whole reason this survived.
			//
			// There is no ticks-per-second number here now, deliberately: an int cannot hold 16.667,
			// so any code that materialises one has already lost the 4 % before it multiplies.
			// TickTime multiplies before dividing instead. Timed matches are now ~4 % LONGER.
			//
			// world.Timestep read ONCE in the constructor, the established idiom here
			// (DefconEscalation.cs:432-437, NuclearUnlockClock.cs:281-284): it is the configured
			// value at this moment on every client, and the debug speed button and the test-mode
			// speed multipliers all mutate it later, at IWorldLoaded or after.
			timestepMilliseconds = self.World.Timestep;

			if (info.TimeLimitTicks > 0)
			{
				// Already in ticks; the minute conversion below must not be applied to it.
				TimeLimit = info.TimeLimitTicks;
				return;
			}

			var tl = self.World.LobbyInfo.GlobalSettings.OptionOrDefault("timelimit", info.TimeLimitDefault.ToString());
			if (!int.TryParse(tl, out TimeLimit))
				TimeLimit = info.TimeLimitDefault;

			// Convert from minutes to ticks. Exact: 90 * 60 * 1000 / 60 = 90000 ticks = 90 real
			// minutes. The old `*= 60 * (1000 / 60)` made that 86400.
			TimeLimit = TickTime.TicksForMinutes(TimeLimit, timestepMilliseconds);
		}

		void IWorldLoaded.WorldLoaded(World w, OpenRA.Graphics.WorldRenderer wr)
		{
			if (string.IsNullOrWhiteSpace(info.CountdownLabel) || string.IsNullOrWhiteSpace(info.CountdownText))
				return;

			countdownLabel = Ui.Root.GetOrNull<LabelWidget>(info.CountdownLabel);
			if (countdownLabel != null)
			{
				// PITFALL: not w.Timestep — that is mutated by the debug speed button, which would
				// rescale the countdown away from the tick-based TimeLimit and the game clock.
				countdown = new CachedTransform<int, string>(t =>
					string.Format(info.CountdownText, WidgetUtils.FormatTime(t, true, w.GameSpeed.Timestep)));
				countdownLabel.GetText = () => TimeLimit > 0 ? countdown.Update(ticksRemaining) : "";
			}
		}

		void ITick.Tick(Actor self)
		{
			if (TimeLimit <= 0)
				return;

			ticksRemaining = TimeLimit - self.World.WorldTick;

			if (ticksRemaining == 0)
			{
				// THE ONE LINE THAT SAYS THE CLOCK FIRED. Nothing on this path logged anything until
				// 2026-09-14, and an autotest that reached its deadline with no ending could not tell
				// "the limit never expired" from "it expired and every consumer declined" -- which cost
				// a run and a wrong diagnosis. World traits are notified before player traits, so this
				// line precedes every consumer's own record.
				Log.Write("debug", $"TIME LIMIT expired at tick {self.World.WorldTick} (limit {TimeLimit}).");

				foreach (var ntl in self.TraitsImplementing<INotifyTimeLimit>())
					ntl.NotifyTimerExpired(self);

				foreach (var p in self.World.Players)
					foreach (var ntl in p.PlayerActor.TraitsImplementing<INotifyTimeLimit>())
						ntl.NotifyTimerExpired(p.PlayerActor);

				return;
			}

			if (ticksRemaining < 0 || info.SkipTimeRemainingNotifications)
				return;

			foreach (var m in info.TimeLimitWarnings.Keys)
			{
				// MUST convert the same way TimeLimit did, or a warning fires on the old arithmetic
				// against a deadline set by the new one and lands ~4 % off the minute it announces.
				// Exact equality is safe only because both sides come from TickTime.TicksForMinutes.
				if (ticksRemaining == TickTime.TicksForMinutes(m, timestepMilliseconds))
				{
					TextNotificationsManager.AddSystemLine(string.Format(Notification, m, m > 1 ? "s" : null));

					var faction = self.World.LocalPlayer?.Faction.InternalName;
					Game.Sound.PlayNotification(self.World.Map.Rules, self.World.LocalPlayer, "Speech", info.TimeLimitWarnings[m], faction);
				}
			}
		}

		void INotifyTimeLimit.NotifyTimerExpired(Actor self)
		{
			if (countdownLabel != null)
				countdownLabel.GetText = () => null;

			if (!info.SkipTimerExpiredNotification)
				TextNotificationsManager.AddSystemLine("Time limit has expired.");
		}
	}
}
