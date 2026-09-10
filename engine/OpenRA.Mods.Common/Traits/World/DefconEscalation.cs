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
		public const string PaceOptionId = "defcon-pace";
		public const string CeilingOptionId = "nuclear-ceiling";

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

		[Desc("Label for the nuclear ceiling dropdown.")]
		public readonly string CeilingLabel = "Nuclear Ceiling";

		[Desc("Tooltip for the nuclear ceiling dropdown.")]
		public readonly string CeilingDescription = "The largest warhead the release ladder will ever permit. Every detonation doubles a single shared pressure value that both sides read, so the war escalates together until it reaches this cap. HOLD is no nuclear weapons at all.";

		[Desc("Default nuclear ceiling. See " + nameof(NuclearRung) + "; " + nameof(NuclearRung.Hold),
			"means no nuclear weapons this match and " + nameof(NuclearRung.GameEnder) + " is the",
			"200 kt+ rung. The Tsar Bomba is above EVERY setting and is Sandbox-only.")]
		public readonly NuclearRung CeilingDefault = NuclearRung.GameEnder;

		[Desc("Whether to show the nuclear ceiling dropdown in the lobby.")]
		public readonly bool CeilingDropdownVisible = true;

		[Desc("Prevent the nuclear ceiling dropdown from being changed in the lobby.")]
		public readonly bool CeilingLocked = false;

		[Desc("Display order for the nuclear ceiling dropdown.")]
		public readonly int CeilingDisplayOrder = 88;

		[Desc("The rung a DEFCON Escalation match opens at ONCE THE RELEASE GATE HAS OPENED -- see",
			nameof(NuclearReleaseDelayTicks) + ". Until then the match is at HOLD and no nuclear",
			"weapon of any yield is permitted, whatever this says.",
			"",
			"NOT A LOBBY OPTION, deliberately -- it is the ladder's shape rather than a host setting,",
			"and the lobby already carries the ceiling, which is the knob a host actually wants.",
			"",
			"IT MUST NOT BE SET TO " + nameof(NuclearRung.Hold) + ". The only thing that CLIMBS the",
			"ladder is a detonation, so a ladder that opened at HOLD would permit no warhead anyone",
			"could fire and could never be climbed -- the gate would open onto nothing. Decision 06's",
			"accepted cost is that 'going first is free', which presumes firing first is possible.")]
		public readonly NuclearRung StartRungDefault = NuclearRung.Kiloton;

		// THE RELEASE GATE'S DELAY, and the arithmetic is written out because this repo has assumed
		// 25 ticks/second at eleven sites and has been wrong at every one of them.
		//
		//     The timestep is 60 ms (mods/ww3mod/mod.yaml:381-382 selects `default`, whose block is
		//     at :404-407), so one tick is 0.06 s and the rate is 1000/60 = 16.67 ticks/s -- NOT 25.
		//     10 minutes = 600 s; 600 / 0.06 = 10000 ticks.
		//
		// The identity to check it against is the StandardTicks field below: 5000 = 300 s = 5:00, so ten
		// minutes is exactly twice that field. Reading the rate as 25 tps would have produced 15000,
		// which is fifteen minutes of real time -- the same 1.5x error, reached the same way.
		[Desc("Ticks spent at DEFCON 1 before the nuclear release ladder opens. UNTUNED PLACEHOLDER.",
			"10000 ticks = 600 s = 10:00 at the default 60 ms timestep (16.67 ticks/s, NOT 25).",
			"",
			"The clock starts when DEFCON 1 is REACHED, however the match got there -- the 3 -> 2",
			"clock and then a casualty, or a lobby Start At of 1 -- and does not run before then.",
			"",
			"0 IS LEGAL and opens the ladder on the tick DEFCON 1 is reached. It does NOT disable the",
			"gate: there is deliberately no setting that hands nuclear weapons to a match which has",
			"not reached DEFCON 1. Negative values are refused in " + nameof(IRulesetLoaded) + ".")]
		public readonly int NuclearReleaseDelayTicks = 10000;

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
			"",
			"STILL TRUE, BUT ITS ORIGINAL JUSTIFICATION IS GONE. This used to read 'nothing a player",
			"can see', and that is now false twice over: the DEFCON readout draws the level, the",
			"phase, the clock and the rule line on screen, and the DEFCON 3 dividing wall stands on",
			"every shipped map via " + nameof(DefconWallInfo.DeriveFromSpawns) + ". Picking",
			nameof(DefconGameMode.Escalation) + " now visibly changes the match.",
			"",
			"It is left TRUE because flipping it is a release decision rather than a code one -- the",
			"three placeholder durations above are still untuned, and the ladder's opening rung is",
			"still an open question with the user. Setting this false is one line and is the last",
			"step of the feature, not a cleanup to be done in passing.")]
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

			// ZERO IS LEGAL HERE and negative is not, which is the opposite shape to the three above.
			// 0 means "open the ladder as soon as DEFCON 1 is reached" and is a real setting a map
			// may want; a negative would silently behave as 0 rather than as anything a reader could
			// predict from the number they typed.
			if (NuclearReleaseDelayTicks < 0)
				throw new YamlException($"{nameof(NuclearReleaseDelayTicks)} must be 0 or positive. " +
					$"0 opens the nuclear release ladder on the tick DEFCON {DefconEscalationState.Floor} is reached.");
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

			// The labels are the ladder as the user drew it, not the enum names: a host is choosing
			// between yields they will recognise from the cameo captions, not between rung indices.
			// The Tsar Bomba is deliberately absent -- it is above every one of these.
			var ceilings = new Dictionary<string, string>
			{
				{ nameof(NuclearRung.Hold).ToLowerInvariant(), "HOLD - no nuclear weapons" },
				{ nameof(NuclearRung.Kiloton).ToLowerInvariant(), "1 kt" },
				{ nameof(NuclearRung.TwentyKiloton).ToLowerInvariant(), "20 kt" },
				{ nameof(NuclearRung.FiftyKiloton).ToLowerInvariant(), "50 kt" },
				{ nameof(NuclearRung.HundredKiloton).ToLowerInvariant(), "100 kt" },
				{ nameof(NuclearRung.GameEnder).ToLowerInvariant(), "200 kt+ (game-enders)" },
			};

			yield return new LobbyOption(ModeOptionId, ModeLabel, ModeDescription, ModeDropdownVisible, ModeDisplayOrder,
				modes, ModeDefault.ToString().ToLowerInvariant(), ModeLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(StartOptionId, StartAtLabel, StartAtDescription, StartAtDropdownVisible, StartAtDisplayOrder,
				levels, StartAtDefault.ToString(), StartAtLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(PaceOptionId, PaceLabel, PaceDescription, PaceDropdownVisible, PaceDisplayOrder,
				paces, PaceDefault.ToString().ToLowerInvariant(), PaceLocked) { Placeholder = MarkAsPlaceholder };

			yield return new LobbyOption(CeilingOptionId, CeilingLabel, CeilingDescription, CeilingDropdownVisible, CeilingDisplayOrder,
				ceilings, CeilingDefault.ToString().ToLowerInvariant(), CeilingLocked) { Placeholder = MarkAsPlaceholder };
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
		public readonly DefconPace Pace;

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

			var pace = settings.OptionOrDefault(DefconEscalationInfo.PaceOptionId, info.PaceDefault.ToString());
			if (!Enum.TryParse(pace, true, out Pace))
				Pace = info.PaceDefault;

			var start = settings.OptionOrDefault(DefconEscalationInfo.StartOptionId, info.StartAtDefault.ToString());
			if (!int.TryParse(start, out var startLevel))
				startLevel = info.StartAtDefault;

			var ceiling = settings.OptionOrDefault(DefconEscalationInfo.CeilingOptionId, info.CeilingDefault.ToString());
			if (!Enum.TryParse<NuclearRung>(ceiling, true, out var ceilingRung))
				ceilingRung = info.CeilingDefault;

			ClockTicks = info.TicksAtDefconThree(Pace);

			state = new DefconEscalationState(Mode, startLevel, ClockTicks);
			ladder = new NuclearReleaseLadder(Mode, (int)ceilingRung, (int)info.StartRungDefault, info.NuclearReleaseDelayTicks);
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
