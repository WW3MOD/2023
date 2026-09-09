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
 * DEFCON ESCALATION -- the match-wide escalation level, and the three lobby dropdowns that configure it.
 *
 * This trait is the spine only. It owns the level; it does not gate anything. Gameplay reaches the
 * level through GrantConditionOnDefconLevel, which grants and REVOKES a per-level condition on each
 * player actor, so ordinary `RequiresCondition:` consumers pick a change up on their next tick.
 *
 * TICK RATE: 60 ms per tick, i.e. 16.67 ticks per second (mods/ww3mod/mod.yaml:358 selects the block
 * at :382). NOT 25 -- TestHarness.TicksPerSecond carries 25 as a harness convention for test budgets
 * and is not the tick rate. Every duration below is written in raw ticks with its seconds beside it.
 */

using System;
using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Holds the match-wide DEFCON level and the lobby options that configure it.",
		"Attach to the World actor. Pairs with " + nameof(DefconCasualtyObserver) + " on the player actor,",
		"which is what supplies the 2 -> 1 trigger, and with " + nameof(GrantConditionOnDefconLevel) + ",",
		"which is how YAML sees the level at all.")]
	public class DefconEscalationInfo : TraitInfo, ILobbyOptions, IRulesetLoaded
	{
		public const string ModeOptionId = "defcon-mode";
		public const string StartOptionId = "defcon-start";
		public const string PaceOptionId = "defcon-pace";

		[Desc("Label for the game mode dropdown.")]
		public readonly string ModeLabel = "Game Mode";

		[Desc("Tooltip for the game mode dropdown.")]
		public readonly string ModeDescription = "DEFCON Escalation opens the match at a fixed alert posture and steps it down as the war widens. Skirmish is the ordinary game with no escalation at all.";

		[Desc("Default game mode. MUST REMAIN " + nameof(DefconGameMode.Skirmish) + " until the feature is",
			"complete: Skirmish is a strict no-op, so while this is the default nothing a player sees",
			"changes, however much of the mode is already built underneath.")]
		public readonly DefconGameMode ModeDefault = DefconGameMode.Skirmish;

		[Desc("Whether to show the game mode dropdown in the lobby.")]
		public readonly bool ModeDropdownVisible = true;

		[Desc("Prevent the game mode dropdown from being changed in the lobby.")]
		public readonly bool ModeLocked = false;

		[Desc("Display order for the game mode dropdown.")]
		public readonly int ModeDisplayOrder = 85;

		[Desc("Label for the starting level dropdown.")]
		public readonly string StartAtLabel = "Start At";

		[Desc("Tooltip for the starting level dropdown.")]
		public readonly string StartAtDescription = "The alert level the match opens at. DEFCON 3 is the ceiling: it is the real-world standing posture, so there is deliberately no 4 or 5 to climb down from.";

		[Desc("Default starting level. 3, 2 or 1 -- see " + nameof(DefconEscalationState.Ceiling) + ".")]
		public readonly int StartAtDefault = DefconEscalationState.Ceiling;

		[Desc("Whether to show the starting level dropdown in the lobby.")]
		public readonly bool StartAtDropdownVisible = true;

		[Desc("Prevent the starting level dropdown from being changed in the lobby.")]
		public readonly bool StartAtLocked = false;

		[Desc("Display order for the starting level dropdown.")]
		public readonly int StartAtDisplayOrder = 86;

		[Desc("Label for the escalation pace dropdown.")]
		public readonly string PaceLabel = "Escalation Pace";

		[Desc("Tooltip for the escalation pace dropdown.")]
		public readonly string PaceDescription = "How long the match holds at DEFCON 3. It scales that clock and nothing else -- DEFCON 2 ends when a life is taken, at any pace.";

		[Desc("Default escalation pace.")]
		public readonly DefconPace PaceDefault = DefconPace.Standard;

		[Desc("Whether to show the escalation pace dropdown in the lobby.")]
		public readonly bool PaceDropdownVisible = true;

		[Desc("Prevent the escalation pace dropdown from being changed in the lobby.")]
		public readonly bool PaceLocked = false;

		[Desc("Display order for the escalation pace dropdown.")]
		public readonly int PaceDisplayOrder = 87;

		// UNTUNED PLACEHOLDERS, all three. Nobody has played this mode; these are round numbers chosen
		// so the phase is long enough to deploy from the Supply Route and short enough to sit through.
		// Seconds are ticks x 0.06 at the default 60 ms timestep.
		[Desc("Ticks spent at DEFCON 3 at the Slow pace. UNTUNED PLACEHOLDER.",
			"9000 ticks = 540 s = 9:00 at the default 60 ms timestep.")]
		public readonly int SlowTicks = 9000;

		[Desc("Ticks spent at DEFCON 3 at the Standard pace. UNTUNED PLACEHOLDER.",
			"5000 ticks = 300 s = 5:00 at the default 60 ms timestep.")]
		public readonly int StandardTicks = 5000;

		[Desc("Ticks spent at DEFCON 3 at the Fast pace. UNTUNED PLACEHOLDER.",
			"2500 ticks = 150 s = 2:30 at the default 60 ms timestep.")]
		public readonly int FastTicks = 2500;

		[Desc("Render the three dropdowns dimmed, with the lobby placeholder tooltip.",
			"TRUE TODAY AND HONEST: the level moves and the conditions are granted, but no shipped",
			"content consumes them yet, so picking " + nameof(DefconGameMode.Escalation) + " changes",
			"nothing a player can see. Set this false in world.yaml on the change that lands the first",
			"consumer -- it is one line, and it is the only thing that has to move.")]
		public readonly bool MarkAsPlaceholder = true;

		public int TicksAtDefconThree(DefconPace pace)
		{
			switch (pace)
			{
				case DefconPace.Slow: return SlowTicks;
				case DefconPace.Fast: return FastTicks;
				default: return StandardTicks;
			}
		}

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (StartAtDefault < DefconEscalationState.Floor || StartAtDefault > DefconEscalationState.Ceiling)
				throw new YamlException($"{nameof(StartAtDefault)} must be between {DefconEscalationState.Floor} and {DefconEscalationState.Ceiling}.");

			if (SlowTicks <= 0 || StandardTicks <= 0 || FastTicks <= 0)
				throw new YamlException($"{nameof(SlowTicks)}, {nameof(StandardTicks)} and {nameof(FastTicks)} must all be positive tick counts.");
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			// Every control here is a DROPDOWN because it is not a LobbyBooleanOption -- that C# type is
			// the only thing that makes a checkbox. There is no integer option type in this engine, so
			// even "Start At" is an enumerated string dropdown keyed on the stringified level.
			var modes = new Dictionary<string, string>
			{
				{ nameof(DefconGameMode.Escalation).ToLowerInvariant(), "DEFCON Escalation" },
				{ nameof(DefconGameMode.Skirmish).ToLowerInvariant(), "Skirmish" },
				{ nameof(DefconGameMode.Sandbox).ToLowerInvariant(), "Sandbox" },
			};

			var levels = new Dictionary<string, string>();
			for (var level = DefconEscalationState.Ceiling; level >= DefconEscalationState.Floor; level--)
				levels.Add(level.ToString(), $"DEFCON {level}");

			var paces = new Dictionary<string, string>
			{
				{ nameof(DefconPace.Slow).ToLowerInvariant(), "Slow" },
				{ nameof(DefconPace.Standard).ToLowerInvariant(), "Standard" },
				{ nameof(DefconPace.Fast).ToLowerInvariant(), "Fast" },
			};

			yield return new LobbyOption(ModeOptionId, ModeLabel, ModeDescription, ModeDropdownVisible, ModeDisplayOrder,
				modes, ModeDefault.ToString().ToLowerInvariant(), ModeLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(StartOptionId, StartAtLabel, StartAtDescription, StartAtDropdownVisible, StartAtDisplayOrder,
				levels, StartAtDefault.ToString(), StartAtLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(PaceOptionId, PaceLabel, PaceDescription, PaceDropdownVisible, PaceDisplayOrder,
				paces, PaceDefault.ToString().ToLowerInvariant(), PaceLocked) { Placeholder = MarkAsPlaceholder };
		}

		public override object Create(ActorInitializer init) { return new DefconEscalation(init.Self, this); }
	}

	// ISync is load-bearing, not decoration: Actor.cs:206 hashes a trait only when `trait is ISync`, so
	// without it the [Sync] members below would be inert and this trait would be absent from every sync
	// report. Both members are int projections for the same reason SyncAnnotationTest exists -- the
	// hasher is IL-emitted at runtime and cannot hash an enum.
	public class DefconEscalation : ITick, ISync
	{
		readonly DefconEscalationState state;

		public readonly DefconGameMode Mode;
		public readonly DefconPace Pace;

		[Sync]
		public int Level => state.Level;

		[Sync]
		public int TicksUntilNextLevel => state.TicksUntilNextLevel;

		public DefconEscalation(Actor self, DefconEscalationInfo info)
		{
			var settings = self.World.LobbyInfo.GlobalSettings;

			var mode = settings.OptionOrDefault(DefconEscalationInfo.ModeOptionId, info.ModeDefault.ToString());
			if (!Enum.TryParse(mode, true, out Mode))
				Mode = info.ModeDefault;

			var pace = settings.OptionOrDefault(DefconEscalationInfo.PaceOptionId, info.PaceDefault.ToString());
			if (!Enum.TryParse(pace, true, out Pace))
				Pace = info.PaceDefault;

			var start = settings.OptionOrDefault(DefconEscalationInfo.StartOptionId, info.StartAtDefault.ToString());
			if (!int.TryParse(start, out var startLevel))
				startLevel = info.StartAtDefault;

			state = new DefconEscalationState(Mode, startLevel, info.TicksAtDefconThree(Pace));
		}

		void ITick.Tick(Actor self)
		{
			if (state.Tick())
				Log.Write("debug", $"DEFCON {Level} (clock expired at tick {self.World.WorldTick}).");
		}

		// Called by DefconCasualtyObserver on each player actor once a death has passed the casualty
		// rule. Only DEFCON 2 listens, so a second casualty on the same tick, or any casualty at 3 or
		// 1, does nothing.
		public void ReportCasualty(Actor victim)
		{
			if (!state.ReportCasualty())
				return;

			Log.Write("debug", $"DEFCON {Level} (enemy action destroyed {victim.Info.Name}, owner {victim.Owner.InternalName}).");
		}
	}
}
