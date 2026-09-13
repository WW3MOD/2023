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
 * DEFCON ESCALATION -- the match-wide escalation level, and the four lobby dropdowns that configure it.
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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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
		public const string NoRushOptionId = LobbyPhaseConsistency.NoRushOptionId;
		public const string FirstWarheadsOptionId = LobbyPhaseConsistency.FirstWarheadsOptionId;

		// THE WORD "DEFCON" APPEARS IN NO STRING A HOST CAN READ, and that is a user ruling
		// (decision 18) rather than a style preference: the lobby says what HAPPENS and the game says
		// what it is CALLED. The in-game readout keeps the name. The OPTION IDS still carry it --
		// `defcon-mode`, `defcon-start` -- and that is deliberate: an id is wire-visible state, and
		// renaming one silently discards every stored value set to it.

		[Desc("Label for the game mode dropdown.")]
		public readonly string ModeLabel = "Game mode";

		[Desc("Tooltip for the game mode dropdown.")]
		public readonly string ModeDescription = "Escalation opens the match with the border closed and steps the war up in phases: a no-rush period, then a cease-fire nobody has broken yet, then open war and the nuclear exchange. Skirmish is the ordinary game with no phases at all.";

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

		[Desc("Label for the starting phase dropdown.")]
		public readonly string StartAtLabel = "Opening phase";

		[Desc("Tooltip for the starting phase dropdown.")]
		public readonly string StartAtDescription = "The phase the match opens in. Positioning is the ceiling: it is the standing posture, so there is deliberately nothing calmer to climb down from. Starting later skips the phases before it.";

		[Desc("Default starting level. 3, 2 or 1 -- see " + nameof(DefconEscalationState.Ceiling) + ".")]
		public readonly int StartAtDefault = DefconEscalationState.Ceiling;

		[Desc("Whether to show the starting phase dropdown in the lobby.")]
		public readonly bool StartAtDropdownVisible = true;

		[Desc("Prevent the starting phase dropdown from being changed in the lobby.")]
		public readonly bool StartAtLocked = false;

		[Desc("Display order for the starting phase dropdown.")]
		public readonly int StartAtDisplayOrder = 86;

		// ==== THE TWO PHASE CLOCKS, AND WHY BOTH ARE MINUTES RATHER THAN A PACE ====
		// `defcon-pace` (slow/standard/fast) was three round numbers nobody had played, and its own
		// [Desc] called all three UNTUNED PLACEHOLDERs -- which is what kept MarkAsPlaceholder set.
		// A host choosing between three adjectives cannot tune what they cannot name, so both clocks
		// are now ordinary minute dropdowns and the host sets the number directly. The pace option,
		// its three keys and its three tick fields are gone; nothing reads them.
		//
		// MINUTES ARE CONVERTED WITH NuclearUnlockSchedule.TicksForMinutes AND NOT WITH
		// TimeLimitManager's `1000 / world.Timestep` IDIOM. That expression is integer division: at a
		// 60 ms timestep it yields 16 ticks per second rather than 16.67, so ten minutes becomes 9600
		// ticks -- 576 s, 4 % short, and short by more the longer the clock. Multiplying before
		// dividing keeps it exact: 10 * 60 * 1000 / 60 = 10000 ticks = 600.0 s. That identity is the
		// thing to check any change here against, and DefconEscalationTest pins it.

		[Desc("Label for the no-rush period dropdown.")]
		public readonly string NoRushLabel = "No-rush period";

		[Desc("Tooltip for the no-rush period dropdown.")]
		public readonly string NoRushDescription =
			"How long the border stays closed at the start of the match. Neither side may cross it or " +
			"fire across it until this runs out. It scales that clock and nothing else";

		[Desc("No-rush periods offered in the lobby, in MINUTES.",
			"",
			"Values are the wire-visible option keys, so this list is not free to reorder or retype:",
			"a key that stops existing silently discards every stored value set to it.",
			"",
			"There is NO zero. A no-rush period of nothing is the Skirmish game, which is a mode",
			"rather than a duration -- and the 3 -> 2 clock is required positive by " + nameof(IRulesetLoaded) + ".")]
		public readonly int[] NoRushOptions = { 2, 3, 5, 7, 10, 15 };

		[Desc("Default no-rush period in minutes. Must be one of " + nameof(NoRushOptions) + ".",
			"",
			"FIVE, which is what the retired Standard pace was worth (5000 ticks = 300 s at the mod's",
			"60 ms timestep), so the shipped default match is unchanged in length by this rework.")]
		public readonly int NoRushDefault = 5;

		[Desc("Prevent the no-rush period from being changed in the lobby.")]
		public readonly bool NoRushLocked = false;

		[Desc("Whether to show the no-rush period dropdown in the lobby.")]
		public readonly bool NoRushDropdownVisible = true;

		[Desc("Display order for the no-rush period dropdown. 20-23 is the Escalation section.")]
		public readonly int NoRushDisplayOrder = 87;

		[Desc("Label for the first warheads dropdown.")]
		public readonly string FirstWarheadsLabel = "First warheads";

		[Desc("Tooltip for the first warheads dropdown.")]
		public readonly string FirstWarheadsDescription =
			"Minutes after the first kill before nuclear weapons are released.";

		[Desc("First-warhead delays offered in the lobby, in MINUTES. See " + nameof(NoRushOptions) + ".")]
		public readonly int[] FirstWarheadsOptions = { 2, 5, 7, 10, 15, 20 };

		[Desc("Default first-warhead delay in minutes. Must be one of " + nameof(FirstWarheadsOptions) + ".",
			"",
			"TEN, WHICH IS THE USER'S OWN RULING (decision 17.1) AND NOT THE AGENT'S RECOMMENDATION:",
			"nuclear weapons are a mid-game tool rather than a late development. It is also exactly",
			"what the retired fixed field was worth -- 10000 ticks at 60 ms -- so this default changes",
			"no shipped behaviour, only who may change it.")]
		public readonly int FirstWarheadsDefault = 10;

		[Desc("Prevent the first warheads delay from being changed in the lobby.")]
		public readonly bool FirstWarheadsLocked = false;

		[Desc("Whether to show the first warheads dropdown in the lobby.")]
		public readonly bool FirstWarheadsDropdownVisible = true;

		[Desc("Display order for the first warheads dropdown. 20-23 is the Escalation section.")]
		public readonly int FirstWarheadsDisplayOrder = 88;

		[Desc("The rung a DEFCON Escalation match opens at ONCE THE RELEASE GATE HAS OPENED -- see",
			nameof(FirstWarheadsOptions) + ". Until then the match is at HOLD and no nuclear",
			"weapon of any yield is permitted, whatever this says.",
			"",
			"NOT A LOBBY OPTION, deliberately -- it is the ladder's shape rather than a host setting.",
			"",
			"IT MUST NOT BE SET TO " + nameof(NuclearRung.Hold) + ". The only thing that CLIMBS the",
			"ladder is a detonation, so a ladder that opened at HOLD would permit no warhead anyone",
			"could fire and could never be climbed -- the gate would open onto nothing.")]
		public readonly NuclearRung StartRungDefault = NuclearRung.Kiloton;

		// ==== THE TWO SCENARIO OVERRIDES, AND THEIR SENTINELS DIFFER ON PURPOSE ====
		// Both exist for the same reason TimeLimitManager.TimeLimitTicks does: the dropdowns' finest
		// grain is one minute, which is far longer than a demo or an autotest wants to sit through.
		// A scenario sets the tick count directly in its own rules.yaml and the lobby option is not
		// read at all. Intended for scenarios, NOT for the mod's own world.yaml.
		//
		// THE SENTINELS ARE NOT THE SAME VALUE AND THAT ASYMMETRY IS LOAD-BEARING. 0 is meaningless
		// for a no-rush period (the clock is required positive), so 0 is its "not in play". 0 is a
		// REAL SETTING for the warhead delay -- "open the ladder on the tick DEFCON 1 is reached" --
		// so that one has to reserve -1 instead. Unifying them would silently delete a setting.

		[Desc("ABSOLUTE no-rush period in TICKS, overriding the lobby dropdown entirely when non-zero.",
			"Zero -- the default -- means the field is not in play and the dropdown is read as usual.")]
		public readonly int NoRushTicksOverride = 0;

		[Desc("ABSOLUTE first-warhead delay in TICKS, overriding the lobby dropdown when 0 OR MORE.",
			"",
			"NEGATIVE -- the default -- is the 'not in play' sentinel here rather than zero, because",
			"ZERO IS A REAL SETTING: it opens the ladder on the tick DEFCON 1 is reached. There is",
			"deliberately no setting that hands nuclear weapons to a match which has not reached",
			"DEFCON 1 at all.")]
		public readonly int NuclearReleaseDelayTicksOverride = -1;

		[Desc("Render the Escalation dropdowns dimmed, with the lobby placeholder tooltip.",
			"",
			"FALSE SINCE THE PHASE CLOCKS LANDED, and both of the reasons it was ever set are gone.",
			"Decision 12 kept it set on one surviving argument -- that the three pace durations were",
			"self-declared UNTUNED PLACEHOLDERs, so the dropdown offered three choices whose",
			"difference nobody had felt. There is no pace dropdown any more: a host sets the minutes.",
			"The mode's other two grounds went earlier, when the readout shipped and the wall went",
			"live on all ten maps.",
			"",
			"Leave it false. A live feature wearing an inert label is the one thing a dimmed control",
			"must never be -- the 2026-09-10 mode audit called that the worst single item in its",
			"whole survey, and it was this flag it was describing.")]
		public readonly bool MarkAsPlaceholder = false;

		/// <summary>The no-rush clock in ticks, for a given timestep in milliseconds.</summary>
		// Public and Info-level so the lobby timeline can draw the clock the match will actually use
		// rather than a label, and so a test can check the conversion without a World.
		public int NoRushTicks(int minutes, int timestepMilliseconds)
		{
			if (NoRushTicksOverride > 0)
				return NoRushTicksOverride;

			return NuclearUnlockSchedule.TicksForMinutes(minutes, timestepMilliseconds);
		}

		/// <summary>The delay from DEFCON 1 to the nuclear release gate, in ticks.</summary>
		public int NuclearReleaseDelayTicks(int minutes, int timestepMilliseconds)
		{
			if (NuclearReleaseDelayTicksOverride >= 0)
				return NuclearReleaseDelayTicksOverride;

			return NuclearUnlockSchedule.TicksForMinutes(minutes, timestepMilliseconds);
		}

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (StartAtDefault < DefconEscalationState.Floor || StartAtDefault > DefconEscalationState.Ceiling)
				throw new YamlException($"{nameof(StartAtDefault)} must be between {DefconEscalationState.Floor} and {DefconEscalationState.Ceiling}.");

			if (!((IList<int>)NoRushOptions).Contains(NoRushDefault))
				throw new YamlException($"{nameof(NoRushDefault)} must be one of {nameof(NoRushOptions)}.");

			if (!((IList<int>)FirstWarheadsOptions).Contains(FirstWarheadsDefault))
				throw new YamlException($"{nameof(FirstWarheadsDefault)} must be one of {nameof(FirstWarheadsOptions)}.");

			// A no-rush period of zero minutes is the Skirmish game, not a duration, and the state
			// machine's 3 -> 2 clock is required positive.
			foreach (var minutes in NoRushOptions)
				if (minutes <= 0)
					throw new YamlException($"{nameof(NoRushOptions)} must all be positive; 0 would be a no-rush period of no length.");

			// ZERO IS LEGAL HERE and negative is not, which is the opposite shape to the line above.
			// 0 means "open the ladder as soon as DEFCON 1 is reached" and is a real setting a map
			// may want; a negative would silently behave as 0 rather than as anything a reader could
			// predict from the number they typed.
			foreach (var minutes in FirstWarheadsOptions)
				if (minutes < 0)
					throw new YamlException($"{nameof(FirstWarheadsOptions)} must all be 0 or positive.");

			if (NoRushTicksOverride < 0)
				throw new YamlException($"{nameof(NoRushTicksOverride)} must be 0 (not in play) or a positive tick count.");
		}

		/// <summary>The no-rush stops offered, as wire keys mapped to their lobby labels.</summary>
		// Public so the lobby timeline positions its band on exactly these stops rather than
		// inventing its own. A key the option does not define cannot reach the wire, and an
		// out-of-set value throws KeyNotFoundException on the next CLIENT JOIN
		// (LobbySettingsNotification.cs:39 indexes Values unchecked).
		public IReadOnlyDictionary<string, string> NoRushValues()
		{
			return MinuteValues(NoRushOptions);
		}

		/// <summary>The first-warhead stops offered, as wire keys mapped to their lobby labels.</summary>
		public IReadOnlyDictionary<string, string> FirstWarheadsValues()
		{
			return MinuteValues(FirstWarheadsOptions);
		}

		static IReadOnlyDictionary<string, string> MinuteValues(int[] options)
		{
			var values = new Dictionary<string, string>();
			foreach (var minutes in options)
				values[minutes.ToString(CultureInfo.InvariantCulture)] = minutes == 1 ? "1 minute" : $"{minutes} minutes";

			return new ReadOnlyDictionary<string, string>(values);
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			// Every control here is a DROPDOWN because it is not a LobbyBooleanOption -- that C# type is
			// the only thing that makes a checkbox. There is no integer option type in this engine, so
			// even the two minute clocks are enumerated string dropdowns keyed on the stringified
			// number of minutes, exactly as `timelimit` and `nuclear-unlock-interval` already are.
			var modes = new Dictionary<string, string>
			{
				{ nameof(DefconGameMode.Escalation).ToLowerInvariant(), "Escalation" },
				{ nameof(DefconGameMode.Skirmish).ToLowerInvariant(), "Skirmish" },
				{ nameof(DefconGameMode.Sandbox).ToLowerInvariant(), "Sandbox" },
			};

			// The levels are LABELLED BY WHAT THEY DO and keyed by the number, which is the whole of
			// decision 18 in one dictionary: the wire keeps "3" and the host reads "Positioning".
			var levels = new Dictionary<string, string>
			{
				{ "3", "Positioning" },
				{ "2", "Weapons free" },
				{ "1", "Open war" },
			};

			yield return new LobbyOption(ModeOptionId, ModeLabel, ModeDescription, ModeDropdownVisible, ModeDisplayOrder,
				modes, ModeDefault.ToString().ToLowerInvariant(), ModeLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(StartOptionId, StartAtLabel, StartAtDescription, StartAtDropdownVisible, StartAtDisplayOrder,
				levels, StartAtDefault.ToString(CultureInfo.InvariantCulture), StartAtLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(NoRushOptionId, NoRushLabel, NoRushDescription, NoRushDropdownVisible, NoRushDisplayOrder,
				NoRushValues(), NoRushDefault.ToString(CultureInfo.InvariantCulture), NoRushLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(FirstWarheadsOptionId, FirstWarheadsLabel, FirstWarheadsDescription, FirstWarheadsDropdownVisible, FirstWarheadsDisplayOrder,
				FirstWarheadsValues(), FirstWarheadsDefault.ToString(CultureInfo.InvariantCulture), FirstWarheadsLocked) { Placeholder = MarkAsPlaceholder };
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
		readonly NuclearReleaseLadder ladder;

		public readonly DefconGameMode Mode;

		[Sync]
		public int Level => state.Level;

		[Sync]
		public int TicksUntilNextLevel => state.TicksUntilNextLevel;

		// BOTH OF THESE ARE SIMULATION AND BOTH DECIDE WHAT A PLAYER MAY FIRE, so both are hashed.
		// The pressure is the shared counter itself and the rung is what every consumer reads; they
		// are redundant with each other by construction, and hashing both is deliberate -- a
		// divergence in the doubling and a divergence in the ceiling are different bugs.
		[Sync]
		public int NuclearPressure => ladder.Pressure;

		[Sync]
		public int NuclearRungLevel => ladder.RungFor(null);

		// THE RELEASE GATE'S COUNTDOWN, hashed for the same reason as the two above: it decides what
		// a player may fire. It is also the only member of this trait that moves on an ORDINARY tick
		// rather than on a level change or a detonation, so it is the earliest place a desync here
		// would surface -- the others can sit identical for minutes while the clocks drift apart.
		[Sync]
		public int TicksUntilNuclearRelease => ladder.TicksUntilRelease;

		// ==== READ-ONLY PROJECTIONS FOR THE READOUT. DELIBERATELY NOT [Sync]. ====
		// DefconReadoutWidget needs three things the trait already knows and had no way to hand out.
		// None of them is new state and none is written anywhere: ClockTicks is a constructor argument
		// kept, and the other two forward to fields NuclearReleaseLadder already owns.
		//
		// They are NOT hashed, and that is the correct call rather than an oversight. [Sync] is for
		// state whose divergence between clients is a desync, and each of these is already covered by
		// a hashed member computed from the same source: TicksUntilNuclearRelease hashes the gate that
		// NuclearReleaseOpen is the terminal state of, and NuclearRungLevel hashes the ladder position
		// that NuclearCeilingRung bounds. ClockTicks is immutable after construction and identical on
		// every client by construction -- it comes from the lobby settings every client loaded.
		// Hashing a constant costs a hash and can never catch anything.

		/// <summary>The full length of the DEFCON 3 clock, so a readout can draw a progress bar.</summary>
		public readonly int ClockTicks;

		/// <summary>Whether the nuclear release gate has opened. See <see cref="NuclearReleaseLadder"/>.</summary>
		public bool NuclearReleaseOpen => ladder.ReleaseOpen;

		/// <summary>The highest rung this match will ever permit -- the host's ceiling, after the mode.</summary>
		public int NuclearCeilingRung => ladder.Ceiling;

		public DefconEscalation(Actor self, DefconEscalationInfo info)
		{
			var settings = self.World.LobbyInfo.GlobalSettings;

			var mode = settings.OptionOrDefault(DefconEscalationInfo.ModeOptionId, info.ModeDefault.ToString());
			if (!Enum.TryParse(mode, true, out Mode))
				Mode = info.ModeDefault;

			var start = settings.OptionOrDefault(DefconEscalationInfo.StartOptionId, info.StartAtDefault.ToString(CultureInfo.InvariantCulture));
			if (!int.TryParse(start, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startLevel))
				startLevel = info.StartAtDefault;

			// world.Timestep, read ONCE here in the constructor, exactly as TimeLimitManager does
			// (TimeLimitManager.cs:118) and NuclearUnlockClock after it. It is the same value on every
			// client at this moment; the debug speed button mutates it later, which is why nothing
			// below re-reads it per tick. World.cs:220 assigns it before the World actor is created,
			// so it is already populated here.
			var timestep = self.World.Timestep;

			ClockTicks = info.NoRushTicks(
				LobbyPhaseConsistency.Minutes(settings, DefconEscalationInfo.NoRushOptionId, info.NoRushDefault), timestep);

			var releaseDelay = info.NuclearReleaseDelayTicks(
				LobbyPhaseConsistency.Minutes(settings, DefconEscalationInfo.FirstWarheadsOptionId, info.FirstWarheadsDefault), timestep);

			state = new DefconEscalationState(Mode, startLevel, ClockTicks);

			// NO HOST CEILING. `nuclear-ceiling` is gone (user ruling 2026-09-13): the exchange's top
			// rung is the game-enders and they are reached only through a chain of deliberate replies,
			// so there is nothing left for a host to cap. The ladder still TAKES a ceiling because it
			// clamps every rung against it; handing it the top of the type is the no-op value.
			ladder = new NuclearReleaseLadder(Mode, (int)NuclearRung.GameEnder, (int)info.StartRungDefault, releaseDelay);
		}

		void ITick.Tick(Actor self)
		{
			// THE RELEASE GATE IS TICKED BEFORE THE EARLY RETURN BELOW, and that ordering is
			// load-bearing rather than tidy: state.Tick() returns false on every tick except the one
			// the level actually moves, so a gate ticked after it would advance at most twice in a
			// whole match and the ladder would never open.
			//
			// `self` IS the World actor here -- this trait is [TraitLocation(SystemActors.World)] --
			// so nothing in this class may reach for self.World.WorldActor. DefconWall shipped that
			// idiom (copied from DefconCasualtyObserver, where it is correct because that trait lives
			// on the PLAYER actor) and threw a NullReferenceException in every match: World.cs:252 is
			// the line that assigns WorldActor, so it is still null while world traits are created.
			// That is a Created hazard rather than a Tick one, but the rule is the same either way.
			if (ladder.Tick(state.Level))
				Log.Write("debug", $"NUCLEAR RELEASE OPEN at rung {NuclearRungLevel} " +
					$"(DEFCON {Level}, tick {self.World.WorldTick}).");

			if (!state.Tick())
				return;

			Log.Write("debug", $"DEFCON {Level} (clock expired at tick {self.World.WorldTick}).");

			// THE ONE-SHOT HALF OF THE HOLD-FIRE RULE. The flag itself only stops a unit ACQUIRING a
			// target; an engagement already running when the level drops has to be cancelled explicitly,
			// exactly once, here -- otherwise a unit that closed on something at DEFCON 3 keeps chasing
			// and shooting it through the whole phase. Held as a one-shot rather than as state so that
			// no stance is disturbed and there is nothing to restore when the level moves on.
			//
			// ReportCasualty deliberately does NOT do this: it only ever moves 2 -> 1, the direction that
			// RESTORES autonomous fire. Starting a match AT DEFCON 2 is not a transition either -- nothing
			// is engaged on the opening tick.
			if (DefconFireDiscipline.HoldsFire(Level))
				CeaseAutonomousFireEverywhere(self);
		}

		// Deterministic without further qualification: ActorsWithTrait walks the trait dictionary in
		// ActorID order, nothing here draws from SharedRandom, and it runs on the single tick the level
		// moves -- so every client does identical work in identical order on the same tick. Materialised
		// with ToArray because the body cancels activities and rewrites trait state as it walks.
		static void CeaseAutonomousFireEverywhere(Actor self)
		{
			foreach (var pair in self.World.ActorsWithTrait<AutoTarget>().ToArray())
			{
				if (pair.Actor.IsDead || !pair.Actor.IsInWorld)
					continue;

				pair.Trait.CeaseAutonomousFire(pair.Actor);
			}
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

		/// <summary>May this player fire a warhead of this yield (in tons of TNT) right now?</summary>
		public bool PermitsNuclearYield(Player player, int tons)
		{
			return ladder.Permits(player?.InternalName, tons);
		}

		/// <summary>The rung this player is released to. Same for everyone; see NuclearReleaseLadder.</summary>
		public int NuclearRungFor(Player player)
		{
			return ladder.RungFor(player?.InternalName);
		}

		// A nuclear weapon has been RELEASED -- called from MissileStrikePower.Activate.
		//
		// THE DETERMINISM ARGUMENT, and it is a construction rather than an assurance. The only
		// caller is MissileStrikePower.Activate, reached exclusively through
		// SupportPowerManager.ResolveOrder -> SupportPowerInstance.Activate
		// (SupportPowerManager.cs:293-321). SupportPowerManager is IResolveOrder, so that path is
		// the synced order-resolution path: every client resolves the same order on the same tick,
		// which is the same seam MissileStrikePower.ResolveAimPoints already relies on and states
		// its own desync argument against. Everything this method then reads is either on the order
		// (the firing player) or a compile-time constant (the weapon's declared yield); the
		// arithmetic in NuclearReleaseLadder is integer throughout and draws no shared random
		// number. Nothing here reads a viewport, a LocalPlayer, a RenderPlayer or wall-clock time.
		//
		// THIS IS NOT THE SHAPE THAT DESYNCED BEFORE. The shipped desync in this codebase came from
		// bot modules writing synced actor state directly -- i.e. from OUTSIDE the order path, where
		// only the machine running the bot performs the write. A support-power order is issued by
		// one client and RESOLVED by all of them, so the write happens everywhere or nowhere.
		//
		// AND IT IS HASHED, so a mistake in the above is caught rather than silently played out:
		// NuclearPressure and NuclearRungLevel are [Sync] on a trait that implements ISync, which
		// Actor.cs:206 requires before it hashes anything at all.
		public void ReportNuclearRelease(Player firer, int tons)
		{
			if (!ladder.ReportDetonation(firer?.InternalName, tons))
				return;

			Log.Write("debug", $"NUCLEAR RUNG {NuclearRungLevel} (pressure {NuclearPressure}; " +
				$"{firer?.InternalName ?? "unknown"} released {tons} t).");
		}
	}
}
