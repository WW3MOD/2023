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
using System.Linq;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("THE FINAL EXCHANGE: the replacement for a plain time-limited game. The match does not stop,",
		"it ENDS -- both sides put their strategic package in the air and the map is annihilated.",
		"",
		"THE SEQUENCE, in order, and each stage is separately tunable:",
		"  1. THE WINDOW OPENS. Statistics freeze on that exact tick, before anything is launched, the",
		"     map comes out of the fog, and every surviving side is handed its game-ender, fire-ready,",
		"     for " + nameof(DoomsdayStrikeInfo.FinalExchangeWindowTicks) + ". Players place their own",
		"     aim points with the ordinary targeting UI.",
		"  2. AT THE CLOSE, EVERY SIDE THAT PLACED NOTHING FIRES ANYWAY -- its own weapon, its own",
		"     package size, aimed by the machine at the ENEMY half of the map. See",
		"     " + nameof(FinalExchangeTargeting) + ".",
		"  3. ONE CASCADE. Every warhead of the exchange, whoever fired it and whenever, lands on a",
		"     slot of a single staggered sequence. See " + nameof(FinalExchangeCascade) + ".",
		"  4. Everything still alive is destroyed, and the winner is resolved from the FROZEN score.",
		"",
		"TWO WAYS IN, ONE ENDING (user ruling, 2026-09-13): the Time Limit reaching zero with this",
		"checkbox ticked, or a side firing a game-ender it was granted through the nuclear exchange.",
		"Both call " + nameof(DoomsdayStrike.BeginFinalExchange) + ", which is idempotent -- 'either way the outcome is the",
		"same, the nukes fly and the game ends'.",
		"",
		"REDESIGNED 2026-09-20, AND THE MAP-WIDE DEAD HAND SALVO IS GONE. It used to build one",
		"side-blind salvo over every derrick, Supply Route and city on the map and fire it with no",
		"owner -- so it bombed the player who had just declined to aim as thoroughly as it bombed",
		"their enemy, and because it flew on a 30-tick lead-in while a Sarmat carries MissileDelay",
		"500, the machine's warheads ALWAYS landed before the player's own. Both are fixed by the",
		"same change: a side that places nothing fires its own package at the other side, and every",
		"warhead in the exchange lands on one shared cascade.",
		"",
		"Attach to the World actor. Requires " + nameof(TimeLimitManager) + ", which supplies the trigger.")]
	public class DoomsdayStrikeInfo : TraitInfo, ILobbyOptions, Requires<TimeLimitManagerInfo>
	{
		// THE PLAYER-FACING NAME IS "NUCLEAR ENDING"; THE SYMBOL NAMES DELIBERATELY DO NOT FOLLOW IT.
		//
		// The feature is TWO controls and they are named separately: TimeLimitManager's dropdown says
		// HOW LONG ("Time Limit", world.yaml), and this checkbox says WHAT HAPPENS at zero ("Nuclear
		// ending"). Untick it and the match simply ends on score.
		//
		// The trait, its file, its fields and the `doomsday` option id keep the old name on purpose:
		// the id is wire-visible (saved skirmish settings, replays and any map that sets it), and
		// renaming a lobby option id silently discards the stored value. So this is not drift waiting
		// to be tidied up -- and note it has now survived three renames of the copy, which is the
		// argument for leaving it alone rather than against. Change the strings, never the symbols.
		// (Decision 17 section 5.)
		[Desc("Label for the lobby checkbox.")]
		public readonly string DoomsdayLabel = "Nuclear ending";

		[Desc("Tooltip for the lobby checkbox.")]
		public readonly string DoomsdayDescription =
			"What happens when the Time Limit expires: both sides' strike packages fire and the highest " +
			"score wins, or with this off the match simply ends on score. Inert while the Time Limit " +
			"reads No limit";

		[Desc("Default state of the lobby checkbox.")]
		public readonly bool DoomsdayEnabled = true;

		[Desc("Prevent the checkbox from being changed in the lobby.")]
		public readonly bool DoomsdayLocked = false;

		[Desc("Whether to show the checkbox in the lobby.")]
		public readonly bool DoomsdayCheckboxVisible = true;

		[Desc("Display order for the lobby checkbox.")]
		public readonly int DoomsdayCheckboxDisplayOrder = 12;

		// Lobby option id, so consumers stop repeating the string literal. LobbyOptionsLogic
		// needs it to file this checkbox in the same section as the Doomsday Clock it modifies.
		public const string DoomsdayOptionId = "doomsday";

		[Desc("Run the ending in TestMode sessions too. Defaults to false, which is what keeps the",
			"existing timed tournament and autotest configurations behaving exactly as they did:",
			"they set a time limit and expect the score comparison, not an apocalypse. The demo",
			"scenario sets this true.")]
		public readonly bool RunInTestMode = false;

		// ==== THE PACKAGE, SIZED FROM THE MAP ====================================================
		[Desc("Playable cells per warhead. The package one side delivers is",
			"round(playableCells / this), clamped to [" + nameof(MinPackage) + ", " + nameof(MaxPackage) + "].",
			"",
			"THIS IS DECISION 20's ARITHMETIC, BUILT. That ruling retired the host-facing game-ender",
			"count with the note that it 'was arithmetic: one per ~1340 cells of map, split between",
			"sides. Offering it invited a host to override a calculation the engine already does",
			"correctly.' The calculation did not exist: " + nameof(MissileStrikePowerInfo.AimPoints),
			"was a static 6 on the Sarmat and a static 1 on the B83, so arena-tank-duel (2048 playable",
			"cells) and x-lake (16384) got the same package -- and Russia got six warheads against",
			"America's one, which is not an exchange.",
			"",
			"2400 PER SIDE is the ~1340-per-map figure carried over: the ruling's number counts both",
			"sides' warheads together, so one side's share is a shade under double it. The shipped",
			"maps land on 2 / 3 / 3 / 3 / 4 / 4 / 6 / 6 / 6; " + nameof(FinalExchangePackage) + "Test",
			"pins the whole table so a retune here is visible as a table diff rather than as a number.")]
		public readonly int CellsPerImpact = 2400;

		[Desc("Floor on the package. TWO, NOT ONE, and arena-tank-duel is why: at 2048 playable cells",
			"the arithmetic gives 1, and a one-warhead 'exchange' on a duelling map is a coin toss",
			"rather than an ending.")]
		public readonly int MinPackage = 2;

		[Desc("Ceiling on the package. SIX, because that is what the Sarmat's re-entry bus carries and",
			"what its art, its camera budget and its " + nameof(MissileStrikePowerInfo.AimPointInterval),
			"were all tuned around. Nothing above it has ever been fired.")]
		public readonly int MaxPackage = 6;

		// ==== THE CASCADE ========================================================================
		[Desc("Ticks between consecutive impacts of the exchange. Every warhead of both packages lands",
			"on a slot of one shared sequence; this is the slot pitch. 15 ticks is 0.9 s at the mod's",
			"60 ms timestep -- NOT 25 tps, which would read this as 0.6 s (see conventions.md).")]
		public readonly int ImpactSpacingTicks = 15;

		[Desc("Pre-launch countdown for an EXCHANGE launch, replacing the power's own",
			nameof(MissileStrikePowerInfo.MissileDelay) + " for any game-ender fired while the",
			"window is open or at its close.",
			"",
			"WHY THE WEAPONS' OWN 500 IS WRONG HERE AND ONLY HERE. MissileDelay exists so a target",
			"has thirty seconds of beacon to react to. Inside the exchange there is nothing to react",
			"with: production is halted, statistics are frozen, the map is revealed, and Annihilate",
			"kills everything standing a few seconds later. Warning time is a balance property of a",
			"weapon IN PLAY, and the exchange is exactly where there is no play left -- so this is a",
			"clean boundary rather than a rebalance. Lowering MissileDelay on the powers themselves",
			"would change every ordinary match.",
			"",
			"IT IS WHAT LETS " + nameof(FinalExchangeFlightTicks) + " BE SMALL. The floor has to cover",
			"the longest order-to-impact interval a last-tick placement can need, and before this",
			"existed that interval carried the full 500: 800 ticks of floor meant the first warhead",
			"landed 63 s after the trigger, with 48 s of dead air after the window shut.",
			"",
			"SUBSTITUTED BEFORE THE IMPACT TICK IS COMPUTED, not after. The cascade already discards",
			"MissileDelay for every warhead it MOVES -- it solves the launch delay backwards from the",
			"reserved slot -- but the warhead that SETS the anchor takes the anchor from its own",
			"natural tick, and that is where the 500 leaked back in.",
			"",
			"ZERO OR LESS DISABLES THE SUBSTITUTION and restores the powers' own MissileDelay inside",
			"the exchange, which is the pre-2026-09-20 behaviour.")]
		public readonly int FinalExchangeMissileDelay = 100;

		[Desc("Ticks from the window CLOSING to the earliest tick the cascade may start on.",
			"",
			"IT IS A FLOOR ON THE ANCHOR AND IT IS WHAT MAKES THE CASCADE POSSIBLE AT ALL. A warhead",
			"ordered on the very last tick of the window still has its whole pre-launch countdown and",
			"its whole flight ahead of it; if the cascade started earlier than that, the last",
			"placement would be scheduled into the past and would arrive outside the sequence. So",
			"this must be at least the slowest game-ender's ARC on the largest map -- and NOT that",
			"plus " + nameof(FinalExchangeMissileDelay) + ", which is the mistake this line carried",
			"twice. A warhead the cascade MOVES has its launch delay solved backwards from its",
			"reserved slot, so the countdown is discarded; the one warhead the cascade does not move",
			"is the one that SET the anchor from its own natural tick, which is reachable by",
			"construction. The countdown therefore never enters this bound.",
			"",
			"350 FOR THE SHIPPED PAIR, DOWN FROM 800 ON 2026-09-20. The b83missile at Speed 700",
			"crosses x-lake's standoff (the 128x128 diagonal plus " +
			nameof(MissileStrikePowerInfo.ApproachMargin) + " 16c0, about 202k WDist) in roughly 292",
			"ticks, the longest arc in the arsenal on the largest shipped map -- so 350 carries 20%",
			"of headroom over the requirement. The old 800 assumed the floor had to cover the powers'",
			"own MissileDelay 500 as well, which stopped being true the moment the cascade began",
			"solving launch delays backwards from a reserved slot.",
			"",
			"A SCENARIO THAT SHORTENS " + nameof(FinalExchangeMissileDelay) + " MAY SHORTEN THIS TOO;",
			"one that lengthens it MUST. demo-doomsday-deadhand is the shipped example.")]
		public readonly int FinalExchangeFlightTicks = 350;

		// ==== WHAT AN UNPLACED PACKAGE IS AIMED AT ===============================================
		[Desc("Two enemy actors within this distance of each other belong to the same CONCENTRATION.",
			"Applied transitively, so a column on a road links into one target rather than several.",
			"",
			"Was CityLinkDistance, and the rename is the change: it is run over one side's own actors",
			"now rather than over every building on the map, so what it finds is an army or a base",
			"rather than a town.")]
		public readonly WDist ConcentrationLinkDistance = new(8 * 1024);

		[Desc("Actor types that are the HIGHEST-priority target in an unplaced package -- the thing",
			"the whole mod is about. One per player and nothing outranks it.")]
		public readonly HashSet<string> SupplyRouteTypes = new() { "supplyroute" };

		[Desc("NEUTRAL actor types worth a warhead when they stand on the enemy's side of the border.",
			"A derrick the enemy is drawing on is a target whoever nominally owns it.")]
		public readonly HashSet<string> NeutralHighValueTypes = new() { "oilb" };

		[Desc("Actor types never enumerated as a target even when the enemy owns them. Walls and tank",
			"traps are structures to the engine and scenery to a targeteer.")]
		public readonly HashSet<string> ExcludeTypes = new() { "barb", "sbag", "fenc", "brik", "cycl", "tanktrap", "tanktrap2" };

		// ==== THE TAIL ===========================================================================
		[Desc("Ticks after the LAST impact before anything still alive is destroyed outright. This is",
			"the backstop that makes 'nothing survives' unconditional rather than contingent on warhead",
			"tuning -- see the class remarks.")]
		public readonly int AnnihilationDelayTicks = 90;

		[Desc("Ticks after the annihilation before the win/loss verdict is applied from the frozen score.")]
		public readonly int ResolutionDelayTicks = 30;

		[Desc("THE FINAL EXCHANGE WINDOW: how long every surviving side holds its game-enders and may",
			"place them itself before the machine places for it. 250 ticks is 15.0 s at the mod's 60 ms",
			"timestep -- NOT 25 tps, which would read this as 10 s (see conventions.md).",
			"",
			"FIFTEEN, AND THE 2026-09-16 RAISE TO 500 IS REVERSED. That raise had an arithmetic",
			"argument: Russia's game-ender asked for SIX clicks and America's for ONE, so the two",
			"factions needed very different amounts of time and fifteen seconds did not cover the",
			"slower one. The asymmetry is gone -- both nations now ask for the same map-derived",
			nameof(CellsPerImpact) + "-sized package, which is 2 or 3 clicks on most shipped maps and",
			"6 only on the three largest -- so the argument for thirty has gone with it. Decisions 14,",
			"17 and 20 all say fifteen; audit item B9 asked for the mismatch to be resolved",
			"deliberately rather than by whoever next read one of them. This is that resolution.",
			"",
			"ZERO OR LESS SKIPS THE WINDOW ENTIRELY and fires every package on the trigger tick. That",
			"is the escape hatch for a scenario that wants no interaction without stripping the trait.")]
		public readonly int FinalExchangeWindowTicks = 250;

		// DELIBERATELY NOT [GrantedConditionReference]. That attribute states "this trait grants this
		// condition ON ITS OWN ACTOR", and CheckConditions is a strictly per-actor pass
		// (Lint/CheckConditions.cs:33-77): it collects granted and consumed names one actor at a time.
		// This trait sits on the WORLD actor and grants onto the PLAYER actors, a shape the lint cannot
		// model -- annotating it would raise "Actor type `world` grants conditions that are not
		// consumed" on every run, which is a warning that is simply false rather than a floor worth
		// keeping. The consumer side is still checked where it matters: the two powers' own
		// RequiresCondition is consumed on the player actor, where GrantConditionOnNuclearRelease's
		// annotated grant satisfies it.
		[Desc("Condition granted to EVERY surviving player actor when the window opens, and held for",
			"the rest of the match. It is the game-ender rung of the nuclear ladder -- the same name",
			nameof(GrantConditionOnNuclearReleaseInfo) + " grants at " + nameof(NuclearRung.GameEnder) + " -- so a power already gated on it",
			"needs no second condition and no edit.",
			"",
			"GRANTED HERE RATHER THAN THROUGH A NEW TRAIT, for one reason: Actor.GrantCondition applies",
			"IMMEDIATELY (Actor.cs:725-733 calls UpdateConditionState, which notifies synchronously),",
			"so the powers are enabled before the very next line arms them. A polling trait would have",
			"landed the condition a tick later and left the arming to race it.",
			"",
			"It does NOT override the host's arsenal checkbox: the shipped game-enders are gated on",
			"`!nuke-arsenal-disabled && nuclear-release-gameender` (nuclear-arsenal.yaml), so a host",
			"who turned the arsenal off still gets no cameo -- and, since 2026-09-20, no auto-fire",
			"either. A side with no weapon fires nothing; there is no longer a map-wide salvo standing",
			"in for it.")]
		public readonly string FinalExchangeCondition = "nuclear-release-gameender";

		[Desc("Prerequisites the final exchange is licensed to IGNORE when it arms a game-ender.",
			"",
			"SupportPowerInstance.MakeReady overrides the tech tree wholesale -- it sets",
			"prereqsAvailable true on whatever instance it is handed -- and that is deliberate for",
			"`powers.event`, which no faction provides and which exists so a game-ender is never on",
			"the shop floor. It is NOT deliberate for the faction tier beside it: overriding that",
			"hands an America player Russia's Sarmat and a Russia player America's B83, which is the",
			"one place the arsenal's faction lock did not hold. Everything a power requires that is",
			"not named here must be genuinely held by the player before it is armed.",
			"",
			"EMPTY IS THE STRICT SETTING, not the permissive one: it makes the window respect every",
			"prerequisite, which for the shipped pair means arming nobody outside Sandbox. The",
			"default is the one name the override is actually for.")]
		public readonly string[] OverriddenPrerequisites = { "powers.event" };

		[NotificationReference("Speech")]
		[Desc("Speech notification played to every surviving player when the window opens.",
			"",
			"DELIBERATELY UNSET. There is no recorded line for this moment -- rules/sound/notifications.yaml",
			"has AbombPrepping/AbombReady/AlertBuzzer and nothing that says 'place your warheads' -- and a",
			"wrong line is worse than none, because this is the one moment in the match a player has",
			"fifteen seconds to act on. The system line and the on-screen banner carry it until a line",
			"is recorded; set this then, with no other edit.")]
		public readonly string FinalExchangeNotification = null;

		[Desc("Damage type applied by the final annihilation sweep.")]
		public readonly BitSet<DamageType> AnnihilationDamageTypes = default;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption(DoomsdayOptionId, DoomsdayLabel, DoomsdayDescription,
				DoomsdayCheckboxVisible, DoomsdayCheckboxDisplayOrder, DoomsdayEnabled, DoomsdayLocked);
		}

		public override object Create(ActorInitializer init) { return new DoomsdayStrike(init.Self, this); }
	}

	/// <summary>
	/// <para>THE MODE. See <see cref="DoomsdayStrikeInfo"/> for the sequence; the interesting parts of the
	/// implementation are the guarantees it has to keep.</para>
	///
	/// <para>"NOTHING SURVIVES" IS THE BACKSTOP'S PROPERTY ALONE, not the warheads'. <see cref="Annihilate"/>
	/// destroys everything standing after the last impact, so the mode's guarantee is unchanged from a
	/// player's point of view whatever the two packages happen to be aimed at. If that sweep is ever
	/// removed, the guarantee goes with it; there is no second mechanism.</para>
	///
	/// <para>NOBODY'S WARHEADS LAND BEFORE THE WINDOW SHUTS, which is the 2026-09-20 fix stated as an
	/// invariant. Every game-ender fired inside the exchange -- by a player at the top of the window, by
	/// a player on its last tick, or by the auto-fire at its close -- has its impact tick reassigned by
	/// <see cref="FinalExchangeCascade"/>, whose anchor is floored at the close plus
	/// <see cref="DoomsdayStrikeInfo.FinalExchangeFlightTicks"/>. The old ordering defect could not be
	/// fixed by tuning either clock because there were two clocks; there is now one.</para>
	///
	/// <para>DETERMINISM. There is no RNG on this path AT ALL any more -- the salvo's jitter and its random
	/// approach bearing went with the salvo. Nothing in the pipeline iterates a Dictionary or a HashSet:
	/// the asset list is sorted by ActorID before it is used for anything, power keys are walked in
	/// ordinal order, and the two Info HashSets are only ever membership-TESTED. This is simulation state
	/// and it must be byte-identical on every client.</para>
	/// </summary>
	public class DoomsdayStrike : ITick, INotifyTimeLimit, ISync
	{
		readonly DoomsdayStrikeInfo info;
		readonly World world;
		readonly bool enabled;

		readonly int concentrationLinkCells;

		bool triggered;
		int lastImpactTick;
		int annihilationTick;
		int resolutionTick;
		bool annihilated;
		bool resolved;
		bool packagesFired;

		/// <summary>The window, as bookkeeping. See <see cref="FinalExchangeWindow"/>.</summary>
		readonly FinalExchangeWindow window = new();

		/// <summary>The one impact sequence. See <see cref="FinalExchangeCascade"/>.</summary>
		readonly FinalExchangeCascade cascade;

		// Sides already handed their game-enders, as "player|powerkey". MEMBERSHIP-TESTED ONLY, never
		// enumerated -- the same licence DoomsdayStrikeInfo's HashSets have, and for the same reason.
		//
		// It is what stops the window being a magazine. A purchased power's bank is emptied by
		// Activate (SupportPowerManager.cs:327), so re-arming an already-armed power every tick would
		// hand a player unlimited game-enders inside the window instead of the one the exchange grants.
		readonly HashSet<string> armed = new();

		// WHO FIRES WHAT AT THE CLOSE, recorded as ArmGameEnders walks. A List in seat order and not a
		// Dictionary, because it IS enumerated -- once, by the auto-fire -- and the order it is
		// enumerated in decides which side's warheads take the earlier cascade slots.
		readonly List<(Player Player, string Key)> autoFire = new();

		// ==== THE LEDGER. WHAT ACTUALLY FLEW, PER SIDE, FOR A SCENARIO TO ASSERT ON ====
		// Neither of these decides anything -- they are appended to on the same synced path they
		// record, and nothing reads them but the Test bindings and the log. They exist because the
		// two properties this redesign turns on are otherwise UNOBSERVABLE from a scenario: a
		// missile spends its whole MissileDelay held OUT of the world by SpawnActorEffect
		// (SpawnActorEffect.cs:44-49), so counting actors cannot tell "eight warheads are in the
		// air on one cascade" from "nothing was fired", and an aim point is consumed by
		// BallisticMissileFly and never stored anywhere a script can reach.
		//
		// Lists, not Dictionaries: they are enumerated, and by a reader that wants them in the order
		// things happened.
		readonly List<(Player Player, int Tick)> exchangeImpacts = new();
		readonly List<(Player Player, CPos Cell)> autoFiredAimPoints = new();

		// WHY EACH SIDE DID OR DID NOT FIRE AT THE CLOSE, one entry per PLAYER -- not per entry of
		// `autoFire`, and not per side the window knows about. That distinction is the whole point:
		// the 2026-09-20 defect was a side missing from BOTH of those lists, so a census built from
		// either would have printed the same nothing that sent a reader looking in the wrong place.
		readonly List<(string Side, string Reason, int Warheads)> closeCensus = new();

		// The latest tick at which a PLAYER-PLACED game-ender is due to detonate. The resolution is
		// held past it, so the verdict never lands while the player's own warhead is still in the air.
		int playerImpactTick;

		// THE TRIGGER, ON THE ZERO-WINDOW PATH ONLY, AND IT IS A RE-ENTRANCY GUARD RATHER THAN
		// BOOKKEEPING. With FinalExchangeWindowTicks <= 0 the packages fire on the trigger tick --
		// and on door (b) that call arrives from inside MissileStrikePower.Activate, i.e. from
		// inside SupportPowerInstance.Activate for the trigger's OWN power, BEFORE bank.Consume has
		// run. The instance is therefore still Ready, and auto-firing it here would re-enter it and
		// put the trigger's package up TWICE. FinalExchangeWindow cannot answer this: RecordPlacement
		// is a no-op outside an open window, and on this path the window never opened.
		string zeroWindowTrigger;

		// A launch reported on a tick the exchange had not yet begun, kept for exactly that tick. See
		// NotifyExchangeLaunch for why one tick of lookback is the whole of what is needed.
		int pendingLaunchReportedTick = -1;
		int pendingLaunchImpactTick;

		/// <summary>True while every surviving side may still place its own game-enders.</summary>
		public bool FinalExchangeOpen => window.Phase == FinalExchangePhase.Open;

		/// <summary>Ticks left to place. What the countdown banner reads; zero outside the window.</summary>
		public int FinalExchangeTicksRemaining => window.TicksRemaining(world.WorldTick);

		/// <summary>
		/// <para>Warheads ONE SIDE delivers, derived from the map once at construction. Both nations get
		/// the same number; see <see cref="DoomsdayStrikeInfo.CellsPerImpact"/>.</para>
		///
		/// <para>IT IS NOT GATED ON THE LOBBY CHECKBOX. The checkbox decides what happens when the clock
		/// runs out; this decides how big a game-ender is, which is a property of the weapon on this map
		/// and is the same whether the ending ever arrives. <see cref="MissileStrikePower"/> reads it
		/// from the first tick of the match, because the cameo caption and the placement mode both need
		/// it long before any exchange.</para>
		/// </summary>
		public int PackageSize { get; }

		// ==== SYNCED, BECAUSE ALL OF THESE DECIDE WHAT HAPPENS TO THE MATCH ====
		// Same argument as DefconEscalation's: ISync is load-bearing rather than decoration, since
		// Actor.cs:206 hashes a trait only when `trait is ISync`. The phase is an int projection
		// because the hasher is IL-emitted and cannot hash an enum. A client that disagreed about
		// whether the window was open would disagree about who may fire a 1.2 Mt warhead; a client
		// that disagreed about the anchor would disagree about when it lands.
		[Sync]
		public int FinalExchangePhaseValue => (int)window.Phase;

		[Sync]
		public int FinalExchangeClosesTick => window.ClosesTick;

		[Sync]
		public int FinalExchangePlacements => window.PlacementCount;

		[Sync]
		public int FinalExchangeAnchorTick => cascade.AnchorTick;

		[Sync]
		public int FinalExchangeWarheads => cascade.SlotsIssued;

		/// <summary>
		/// True from the trigger until the verdict is applied. While set, the ordinary victory checks
		/// stand down — see <see cref="VictoryChecksSuspended"/>.
		/// </summary>
		public bool SalvoInProgress { get; private set; }

		public DoomsdayStrike(Actor self, DoomsdayStrikeInfo info)
		{
			this.info = info;
			world = self.World;

			var option = world.LobbyInfo.GlobalSettings.OptionOrDefault(DoomsdayStrikeInfo.DoomsdayOptionId, info.DoomsdayEnabled.ToString());
			enabled = bool.TryParse(option, out var parsed) ? parsed : info.DoomsdayEnabled;

			concentrationLinkCells = info.ConcentrationLinkDistance.Length / 1024;
			cascade = new FinalExchangeCascade(info.ImpactSpacingTicks);

			// BOUNDS, NOT MapSize. The playable rectangle is what a unit can stand in; MapSize includes
			// the border margin every OpenRA map carries, which would inflate every package by a ring
			// of ground nothing can be aimed at.
			//
			// SAFE IN A World-ACTOR CONSTRUCTOR: Map is a field of World and is assigned before any
			// trait is created, unlike WorldActor -- which World.cs:252 assigns AFTER CreateActor
			// returns and which is therefore null here. See the worldactor-gate README.
			var bounds = world.Map.Bounds;
			PackageSize = FinalExchangePackage.SizeFor(
				bounds.Width * bounds.Height, info.CellsPerImpact, info.MinPackage, info.MaxPackage);
		}

		/// <summary>
		/// <para>Whether the ordinary win/loss machinery should stand down for this world.</para>
		///
		/// <para>It has to, and the reason is not cosmetic. Once the warheads start landing, players lose their
		/// last units in whatever order the geometry happens to produce, and
		/// <see cref="ConquestVictoryConditions"/> would award the match to whoever survived a few ticks
		/// longer. The user's requirement is that the winner comes from the SCORE as it stood before the
		/// first warhead — so the checks are suspended for the duration and the verdict is applied at the
		/// end from a score that has been frozen the whole time.</para>
		/// </summary>
		public static bool VictoryChecksSuspended(World world)
		{
			var dd = world.WorldActor.TraitOrDefault<DoomsdayStrike>();
			return dd != null && dd.SalvoInProgress;
		}

		/// <summary>
		/// <para>Warheads one side's game-ender delivers on this world's map, or <paramref name="fallback"/>
		/// on a world that carries no <see cref="DoomsdayStrike"/> at all.</para>
		///
		/// <para>TraitOrDefault, not Trait: a map or scenario free of this trait must leave a game-ender
		/// firing whatever its own YAML says rather than throwing. Same rule as DefconCasualtyObserver.</para>
		/// </summary>
		public static int PackageSizeFor(World world, int fallback)
		{
			return world?.WorldActor?.TraitOrDefault<DoomsdayStrike>()?.PackageSize ?? fallback;
		}

		void INotifyTimeLimit.NotifyTimerExpired(Actor self)
		{
			// PATH (a): the clock. Everything this used to do inline is now the first half of
			// BeginFinalExchange, which path (b) reaches from the other direction.
			BeginFinalExchange(null);
		}

		/// <summary>
		/// <para>OPEN THE FINAL EXCHANGE. The single entry point to the ending, and the only thing the
		/// nuclear-exchange side has to know about this trait.</para>
		///
		/// <para>TWO CALLERS, ONE ENDING (user ruling, 2026-09-13: "Both players have the 15 seconds to
		/// choose targets, or the Dead Hand places them for them. Either way the outcome is the same,
		/// the nukes fly and the game ends"):
		///   (a) the Time Limit reaching zero with the Nuclear ending checkbox ticked — <paramref name="trigger"/> null;
		///   (b) a side firing a game-ender it was granted through the exchange — that side is the
		///       trigger, its warhead is the first of the exchange, and the window opens for everyone else.</para>
		///
		/// <para>IDEMPOTENT. A second call while the exchange is in progress is a no-op: it does not restart
		/// the clock, re-arm anybody, or clear the placement record. Both paths can and will fire on the
		/// same tick — the time limit expiring while a warhead is already in the air is not a rare case —
		/// and the first one in wins.</para>
		///
		/// <para>CALL THIS BEFORE ACTIVATING THE TRIGGERING POWER, on path (b). The launch hook below needs
		/// the exchange to exist when the warhead is reported so it can hold the resolution open for that
		/// impact — and, since 2026-09-20, so the trigger's own warheads take cascade slots rather than
		/// flying their own schedule.</para>
		/// </summary>
		public void BeginFinalExchange(Player trigger)
		{
			// WHICH BAIL, NAMED. Three ways in and out of here and none of them logged anything until
			// 2026-09-14: a match that simply never ended looked identical whether this was never
			// called, called with the checkbox off, or called in a test-mode session that had not
			// opted in. Each is a different fix, so each says so.
			if (triggered)
			{
				Log.Write("debug", $"FINAL EXCHANGE: already triggered at tick {world.WorldTick}; call ignored (idempotent).");
				return;
			}

			if (!enabled)
			{
				Log.Write("debug", $"FINAL EXCHANGE: declined at tick {world.WorldTick} -- the `{DoomsdayStrikeInfo.DoomsdayOptionId}` lobby option is off, so the clock ends the match on score instead.");
				return;
			}

			if (TestMode.IsActive && !info.RunInTestMode)
			{
				Log.Write("debug", $"FINAL EXCHANGE: declined at tick {world.WorldTick} -- test-mode session and {nameof(DoomsdayStrikeInfo.RunInTestMode)} is false. Set it in the scenario's rules.yaml to let the packages fly.");
				return;
			}

			Log.Write("debug", $"FINAL EXCHANGE opening at tick {world.WorldTick}, trigger {trigger?.InternalName ?? "time limit"}, " +
				$"package {PackageSize} warhead(s) per side.");

			triggered = true;

			// ORDER IS LOAD-BEARING, and this is the requirement most easily done superficially.
			// TimeLimitManager notifies WORLD traits before PLAYER traits (TimeLimitManager.cs:150-157),
			// so this runs before ConquestVictoryConditions sees the same event. Freezing here means the
			// score every later reader sees — including the verdict at the end of the exchange — is the
			// score as it stood on the trigger tick.
			//
			// THE FREEZE IS AT THE WINDOW'S START, NOT AT ITS END, and that is the user's requirement
			// rather than an implementation convenience: the outcome is decided the moment the exchange
			// opens, so the fifteen seconds must not be a last chance to farm kills for score.
			FreezeStatistics();

			// Victory checks stand down from the START for the same reason. Without this a side that
			// loses its last unit during the window would be awarded a loss by ConquestVictoryConditions
			// before a single warhead had been placed.
			//
			// IT IS ALSO WHAT STOPS PRODUCTION. ProductionQueue reads this through
			// DoomsdayStrike.VictoryChecksSuspended and clears itself for as long as it is set.
			SalvoInProgress = true;

			// ==== THE WEAPON AND THE RULE THAT GOVERNS IT MUST AGREE. DEFECT (a), 2026-09-16 ====
			// ArmGameEnders below grants the condition, overrides the tier and forces the cameo ready
			// -- and before this line it touched NEITHER of the two numbers NuclearExchange holds. A
			// side inside its cooldown was handed a weapon that NuclearExchange put straight back on a
			// four-minute clock, inside a fifteen-second window. See
			// NuclearExchangeState.OpenFinalExchange for the recorded match.
			//
			// BEFORE ArmGameEnders, NOT AFTER, and for the same reason the condition is granted on the
			// line before MakeReady is called: everything downstream reads these numbers live.
			//
			// TraitOrDefault: a map or scenario that carries DoomsdayStrike without NuclearExchange --
			// every Skirmish map -- must reach the ending unchanged.
			world.WorldActor.TraitOrDefault<NuclearExchange>()?.OpenFinalExchange();

			// THE MAP COMES OUT OF THE FOG so the exchange can be aimed, and so it can be watched.
			RevealMap();

			if (window.Begin(world.WorldTick, info.FinalExchangeWindowTicks, SurvivingSides(), trigger?.InternalName))
			{
				// THE CASCADE'S FLOOR, SET BEFORE A SINGLE WARHEAD IS ARMED. Everything after this line
				// can reserve a slot -- including the trigger's own warheads on path (b), which are
				// activated a few lines further down the caller's stack -- so the floor has to exist
				// first or the first reservation fixes the anchor without it.
				cascade.SetFloor(window.ClosesTick + info.FinalExchangeFlightTicks);

				// A launch reported earlier on THIS tick is the trigger's own warhead arriving ahead of
				// the call. Absorb it so the resolution waits for it.
				if (pendingLaunchReportedTick == world.WorldTick && pendingLaunchImpactTick > playerImpactTick)
					playerImpactTick = pendingLaunchImpactTick;

				ArmGameEnders();
				AnnounceFinalExchange();
				return;
			}

			// FinalExchangeWindowTicks <= 0: no window, everybody's package fires on this tick --
			// except the trigger's, which is already being fired by the caller. See zeroWindowTrigger.
			zeroWindowTrigger = trigger?.InternalName;
			cascade.SetFloor(world.WorldTick + info.FinalExchangeFlightTicks);
			ArmGameEnders();
			FirePackagesAndScheduleTheTail();
		}

		/// <summary>
		/// <para>THE CASCADE HOOK. A warhead is about to be put in the air and would, left alone, detonate
		/// at <paramref name="naturalImpactTick"/>. Returns the tick it must detonate at instead.</para>
		///
		/// <para>INERT EVERYWHERE BUT THE EXCHANGE, and that is the byte-identity guarantee
		/// <see cref="MissileStrikeArrivalTest"/> stands on: outside a running exchange, and for any
		/// power that is not a game-ender, this returns its argument unchanged and
		/// <see cref="MissileStrikePower"/> computes exactly the numbers it computed before this
		/// existed. A tactical warhead fired inside the window is NOT slotted either — the exchange
		/// grants the top rung, it does not revoke the lower ones, and a 1 kt shot is not part of the
		/// ending.</para>
		///
		/// <para>Called from the SYNCED order-resolution path, once per warhead, in launch order — so
		/// every client hands out the same slot to the same warhead. See
		/// <see cref="FinalExchangeCascade"/> for why the slots are sequential rather than interleaved
		/// by side.</para>
		/// </summary>
		public static int ScheduleExchangeImpact(World world, Player firer, int naturalImpactTick, SupportPowerInfo powerInfo)
		{
			var dd = ExchangeLaunchOrNull(world, powerInfo);
			if (dd == null)
				return naturalImpactTick;

			var scheduled = dd.cascade.Reserve(naturalImpactTick);
			dd.exchangeImpacts.Add((firer, scheduled));
			return scheduled;
		}

		/// <summary>Ticks this side's warheads are due to detonate on, in launch order. Test reader.</summary>
		public IEnumerable<int> ExchangeImpactTicksFor(Player player)
		{
			foreach (var (p, tick) in exchangeImpacts)
				if (p == player)
					yield return tick;
		}

		/// <summary>Cells the machine aimed this side's package at, or empty if it placed its own. Test reader.</summary>
		public IEnumerable<CPos> AutoFiredAimPointsFor(Player player)
		{
			foreach (var (p, cell) in autoFiredAimPoints)
				if (p == player)
					yield return cell;
		}

		/// <summary>Slot pitch of the cascade, so a reader can state the span rather than guess it.</summary>
		public int ImpactSpacingTicks => info.ImpactSpacingTicks;

		/// <summary>
		/// <para>The pre-launch countdown an EXCHANGE launch uses instead of the power's own
		/// <see cref="MissileStrikePowerInfo.MissileDelay"/>, or -1 when this launch is not one and
		/// the power's own value stands.</para>
		///
		/// <para>INERT EVERYWHERE BUT THE EXCHANGE, by the same gate
		/// <see cref="ScheduleExchangeImpact"/> uses and for the same byte-identity reason: outside a
		/// running exchange, for a non-game-ender, or with the knob at zero, this returns -1 and
		/// <see cref="MissileStrikePower"/> computes exactly the numbers it computed before.</para>
		/// </summary>
		public static int ExchangeLaunchDelay(World world, SupportPowerInfo powerInfo)
		{
			var dd = ExchangeLaunchOrNull(world, powerInfo);
			if (dd == null)
				return -1;

			return dd.info.FinalExchangeMissileDelay > 0 ? dd.info.FinalExchangeMissileDelay : -1;
		}

		/// <summary>
		/// <para>THE ONE DEFINITION OF "THIS LAUNCH IS PART OF THE CASCADE", and the trait handle to act
		/// on it with, or null when it is not. Three separate consequences hang off this question --
		/// the impact is given a cascade slot (<see cref="ScheduleExchangeImpact"/>), the pre-launch
		/// countdown is shortened (<see cref="ExchangeLaunchDelay"/>), and since 2026-09-20 the
		/// EXCHANGE VARIANT of the missile is flown instead of the ordinary one
		/// (<see cref="MissileStrikePowerInfo.EscalationMissileActor"/>) -- and all three have to agree
		/// on every launch, so they ask once here rather than each carrying its own copy of the gate.</para>
		///
		/// <para>The two halves are not interchangeable and neither alone is the answer:</para>
		/// <list type="bullet">
		///   <item><description>A RUNNING EXCHANGE. Not the game MODE: a game-ender bought at
		///   <see cref="NuclearRung.GameEnder"/> and fired before the window opens is an ordinary strike
		///   in Escalation, and Skirmish's no-wait unlock hands out game-enders with no exchange in
		///   sight. A player who places their OWN package during the window IS in, because that launch
		///   really is slotted into the one cascade.</description></item>
		///   <item><description>A GAME-ENDER. The exchange grants the top rung; it does not revoke the
		///   lower ones, and a 1 kt shot fired inside the window is not part of the ending.</description></item>
		/// </list>
		///
		/// <para>TraitOrDefault, not Trait: a scenario or map that strips this trait from the World actor
		/// must leave every caller inert rather than throw. Same rule as DefconCasualtyObserver.</para>
		/// </summary>
		public static bool IsExchangeLaunch(World world, SupportPowerInfo powerInfo)
		{
			return ExchangeLaunchOrNull(world, powerInfo) != null;
		}

		static DoomsdayStrike ExchangeLaunchOrNull(World world, SupportPowerInfo powerInfo)
		{
			var dd = world.WorldActor.TraitOrDefault<DoomsdayStrike>();
			if (dd == null || !dd.SalvoInProgress || !NuclearGameEnders.Is(powerInfo))
				return null;

			return dd;
		}

		/// <summary>The last impact of the cascade, or -1 before anything is reserved.</summary>
		public int FinalExchangeLastImpactTick => cascade.LastImpactTick;

		/// <summary>
		/// <para>A game-ender has been put in the air by a player, and is due to detonate at
		/// <paramref name="impactTick"/>. Called from <see cref="MissileStrikePower"/> on the SYNCED
		/// order-resolution path, once per warhead, with the impact tick the power itself computed —
		/// so this cannot drift from the flight the missile actually flies.</para>
		///
		/// <para>It does two separate things, and they are separate on purpose:
		///   * records that this side placed its own — but ONLY for a game-ender, since the window
		///     leaves the lower rungs granted too and a tactical shot is not a placement;
		///   * holds the resolution open past the impact, so the verdict never lands while a player's
		///     own warhead is still in the air.</para>
		///
		/// <para>THE ONE-TICK LOOKBACK. A launch reported before the exchange exists is kept for exactly the
		/// tick it was reported on, and <see cref="BeginFinalExchange"/> absorbs it.</para>
		/// </summary>
		public static void NotifyExchangeLaunch(World world, Player firer, int impactTick, SupportPowerInfo powerInfo)
		{
			world.WorldActor.TraitOrDefault<DoomsdayStrike>()?.ReportExchangeLaunch(firer, impactTick, powerInfo);
		}

		void ReportExchangeLaunch(Player firer, int impactTick, SupportPowerInfo powerInfo)
		{
			if (!SalvoInProgress)
			{
				// Not (yet) an exchange. Keep it for this tick only; anything older is stale by
				// construction because BeginFinalExchange only ever looks at the current tick.
				if (pendingLaunchReportedTick != world.WorldTick)
				{
					pendingLaunchReportedTick = world.WorldTick;
					pendingLaunchImpactTick = impactTick;
				}
				else if (impactTick > pendingLaunchImpactTick)
					pendingLaunchImpactTick = impactTick;

				return;
			}

			// ONLY A GAME-ENDER IS A PLACEMENT, and the distinction is load-bearing rather than tidy.
			// A side still holding a 1 kt B61 can fire it inside the window — the exchange grants the
			// top rung, it does not revoke the lower ones — and counting that as "this side placed its
			// warheads" would take it out of the auto-fire list on the strength of a tactical shot.
			//
			// RecordPlacement IS A NO-OP ONCE THE WINDOW HAS CLOSED, which is exactly right for the
			// auto-fire: `placements` counts sides that CHOSE, and the machine firing on a side's
			// behalf is the other half of that partition rather than a placement.
			if (IsGameEnder(powerInfo))
				window.RecordPlacement(firer?.InternalName);

			if (impactTick > playerImpactTick)
				playerImpactTick = impactTick;

			// Once the tail exists it has to be moved, not just recorded against. Not after the sweep
			// has run: extending the annihilation at that point would not un-kill anything and would
			// only delay a verdict that is already decided.
			if (packagesFired && !annihilated)
				ExtendScheduleForImpact(impactTick);
		}

		/// <summary>
		/// Sides that are still in the match, in world.Players order — which is world-creation order and
		/// therefore identical on every client.
		/// </summary>
		// ==== CountsAsASide, NOT Playable, AND THE DIFFERENCE COST A WHOLE SCENARIO RUN ====
		// `Playable` is a statement about LOBBY SLOTS, not about who is in the match: a map-authored
		// combatant that owns a Supply Route and fights is `Playable: false`. This trait gated on it
		// in SIX places, so on test-final-exchange-autofire — whose Russia is a bare map combatant,
		// the shape every passing two-sided scenario in the tree uses — Russia was never enumerated
		// as a side, never armed, and never reached the auto-fire loop. The log said `auto-fired for
		// []` and named no reason, because every reason lived inside a loop Russia was not in.
		//
		// CombatantSides.CountsAsASide is the mod's one answer, already used by DefconWall,
		// NuclearExchange, InfluenceStack, SightingThreatLayer and SpawnStartingUnits — and
		// NuclearExchange using it while this used Playable is exactly the two-layers-disagree shape
		// CombatantSides exists to prevent. It additionally rejects a map-AUTHORED spectator slot a
		// client is sitting in, which the runtime flags do not reflect; see that file's header for
		// the phantom-third-player runs that cost.
		//
		// SHIPPED BEHAVIOUR IS UNCHANGED. On all ten shipped maps every non-playable player is
		// Neutral or Creeps, both NonCombatant; in a real lobby every human and bot seat is Playable
		// and neither non-combatant nor spectating. The delta is a latent phantom side, removed.
		IEnumerable<string> SurvivingSides()
		{
			foreach (var p in world.Players)
				if (IsSurvivingSide(p))
					yield return p.InternalName;
		}

		/// <summary>Is this player a side that is still in the match? The one test, so the six callers cannot drift.</summary>
		static bool IsSurvivingSide(Player p)
		{
			return CombatantSides.CountsAsASide(p) && p.WinState != WinState.Lost;
		}

		/// <summary>
		/// <para>Hand every surviving side its game-ender, fire-ready, for the duration of the window —
		/// and record, in seat order, which power each side will be auto-fired with at the close.</para>
		///
		/// <para>THREE THINGS GATE A GAME-ENDER AND ALL THREE HAVE TO GO, which is the part that is easy
		/// to do superficially — grant the condition, watch the cameo stay absent, and have no idea why:
		///   * THE TIER. Both shipped game-enders carry `Prerequisites: powers.event`, provided by NO
		///     faction and only by the Sandbox lobby option. That is the SHIPPED DEFAULT for both of
		///     them, so a condition-only implementation of this feature hands out nothing in a normal
		///     match while looking entirely correct in the code.
		///     <see cref="SupportPowerInstance.MakeReady"/> overrides it per instance.
		///   * THE CONDITION. `RequiresCondition: !nuke-arsenal-disabled &amp;&amp; nuclear-release-gameender`.
		///     Granted here on the player actor, where the powers live. Actor.GrantCondition applies
		///     IMMEDIATELY (Actor.cs:725-733), so the trait is enabled before the next line runs.
		///   * THE MAGAZINE. Both shipped game-enders are RequiresPurchase, so they are Ready only while
		///     a shot is banked (SupportPowerChargeBank.IconVisible), and nothing about the condition
		///     banks one. <see cref="SupportPowerInstance.MakeReady"/> is what does.</para>
		///
		/// <para>AND A DISABLED POWER DOES NOT CHARGE WHILE IT WAITS. SupportPowerInstance.Tick pins
		/// remainingSubTicks at TotalTicks * 100 on every tick it is disabled (SupportPowerManager.cs:246-248)
		/// and then returns before the countdown — so "grant the condition and let it charge" would hand a
		/// side a power that needed its whole charge interval, which no fifteen-second window can contain.</para>
		///
		/// <para>ONCE PER SIDE PER POWER. <see cref="armed"/> is what makes the window a single shot rather
		/// than a magazine — see its declaration.</para>
		/// </summary>
		void ArmGameEnders()
		{
			foreach (var p in world.Players)
			{
				if (!IsSurvivingSide(p))
					continue;

				p.PlayerActor.GrantCondition(info.FinalExchangeCondition);

				var manager = p.PlayerActor.TraitOrDefault<SupportPowerManager>();
				if (manager == null)
					continue;

				// SORTED BY KEY. Powers is a Dictionary and its enumeration order is not something
				// every client agrees about; the operations below are order-independent, but this file
				// does not iterate an unordered collection at all and that rule is worth keeping whole.
				var techTree = p.PlayerActor.TraitOrDefault<TechTree>();
				var keys = manager.Powers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

				var armedForThisPlayer = false;
				foreach (var key in keys)
				{
					// ONE CALL, NOT TWO. It folds in the user's "national ender only" ruling: an
					// unattributed top-rung power -- the 6 Mt strategic strike -- is armed by nobody,
					// here as well as in the retaliation window.
					if (!NuclearGameEnders.ArmableBy(techTree, manager.Powers[key].Info, info.OverriddenPrerequisites))
						continue;

					Arm(p, key, manager.Powers[key]);
					armedForThisPlayer = true;
				}

				if (armedForThisPlayer)
					continue;

				// ==== THE BORROWED WEAPON, AND IT IS A DELIBERATE EXCEPTION TO c8cadc8a ====
				// A side that owns no game-ender at all -- a faction with no `player.*` tier, a
				// scenario that stripped one, a spectator-shaped seat -- used to be covered by Dead
				// Hand, which was side-blind and fired for everybody. The map-wide salvo is gone, so
				// without this such a side would simply vanish from the ending while its enemy's
				// package still flew, which is strictly worse than the asymmetry c8cadc8a closed.
				//
				// THE RULING IT BENDS IS ABOUT THE SHOP FLOOR, NOT ABOUT THE LAST FIFTEEN SECONDS.
				// c8cadc8a's concern was that overriding the faction tier "hands an America player
				// Russia's Sarmat" in a live match, where owning the wrong national weapon is a real
				// advantage. Here the match is already decided on a frozen score and everything on
				// the map is about to be destroyed by Annihilate regardless.
				//
				// IT STILL REQUIRES A NAMED OWNER (NamesAnOwner), so the unattributed 6 Mt strategic
				// strike is not reachable through this door either -- that exclusion is decision-level
				// and is not the one being bent.
				var borrowed = keys.FirstOrDefault(k =>
					NuclearGameEnders.Is(manager.Powers[k].Info)
					&& NuclearGameEnders.NamesAnOwner(manager.Powers[k].Info, info.OverriddenPrerequisites));

				if (borrowed == null)
				{
					Log.Write("debug", $"FINAL EXCHANGE: {p.InternalName} holds no game-ender at all and will fire nothing.");
					continue;
				}

				Log.Write("debug", $"FINAL EXCHANGE: {p.InternalName} owns no national game-ender; borrowing `{borrowed}` for the auto-fire.");
				Arm(p, borrowed, manager.Powers[borrowed]);
			}
		}

		void Arm(Player p, string key, SupportPowerInstance instance)
		{
			if (!armed.Add(p.InternalName + "|" + key))
				return;

			instance.MakeReady();
			autoFire.Add((p, key));
		}

		/// <summary>
		/// <para>Is this power a GAME-ENDER — one of the weapons the exchange hands out? See
		/// <see cref="NuclearGameEnders.Is"/> for why it is asked of the YIELD, and for why the Tsar
		/// Bomba is excluded by the ladder's own constant rather than by name.</para>
		/// </summary>
		static bool IsGameEnder(SupportPowerInfo powerInfo)
		{
			return NuclearGameEnders.Is(powerInfo);
		}

		void AnnounceFinalExchange()
		{
			// PITFALL: GameSpeed.Timestep, not world.Timestep. The latter is mutated at runtime by the
			// debug speed button and by test-mode speed multipliers, so this line would announce a
			// different number of seconds for the same 250 ticks depending on what speed the session
			// happened to be running at -- and would then disagree with the countdown band, which
			// converts the same way. Constant per match, so reading it here stays deterministic.
			var seconds = (info.FinalExchangeWindowTicks * world.GameSpeed.Timestep) / 1000;
			TextNotificationsManager.AddSystemLine(
				$"FINAL EXCHANGE. {seconds} seconds to place your strike package — " +
				"unplaced packages fire automatically at the enemy.");

			// Deliberately may be null; see DoomsdayStrikeInfo.FinalExchangeNotification. PlayNotification
			// with a null key is a documented no-op, so this costs nothing until a line is recorded.
			foreach (var p in world.Players)
				if (IsSurvivingSide(p))
					Game.Sound.PlayNotification(world.Map.Rules, p, "Speech",
						info.FinalExchangeNotification, p.Faction.InternalName);
		}

		/// <summary>
		/// The window has expired (or was never opened). Every side that placed nothing fires its own
		/// package, and the tail — the sweep and the verdict — is hung off the last impact of the
		/// cascade.
		/// </summary>
		void FirePackagesAndScheduleTheTail()
		{
			FireUnplacedPackages();
			packagesFired = true;

			// THE TAIL WAITS FOR WHICHEVER IS LATER: the last slot of the cascade, or a player's own
			// warhead that somehow fell outside it (a late placement clamped to "launch now"). Seeded
			// from the current tick so a degenerate world in which nothing at all flew still runs the
			// delays rather than annihilating on this tick.
			var last = Math.Max(playerImpactTick, cascade.LastImpactTick);
			ExtendScheduleForImpact(Math.Max(last, world.WorldTick));

			// ==== THE ENDING IS LEGIBLE FROM A LOG ====
			// Everything from here on went to TextNotificationsManager -- the SCREEN -- and nowhere
			// else, so a debug.log covering a whole match showed the exchange OPENING and then nothing
			// at all. What each line carries is the thing that cannot be inferred from the others.
			Log.Write("debug", $"FINAL EXCHANGE closing at tick {world.WorldTick}: " +
				$"{window.PlacementCount} side(s) placed their own [{window.SidesThatPlaced().JoinWith(", ")}]; " +
				$"auto-fired for [{window.SidesPlacedForByDeadHand().JoinWith(", ")}].");

			Log.Write("debug", $"FINAL EXCHANGE cascade: {cascade.SlotsIssued} warhead(s), anchor tick " +
				$"{cascade.AnchorTick}, spacing {info.ImpactSpacingTicks}, last impact tick {cascade.LastImpactTick}; " +
				$"annihilation tick {annihilationTick}, resolution tick {resolutionTick}.");

			foreach (var (side, reason, warheads) in closeCensus)
				Log.Write("debug", $"FINAL EXCHANGE census: {side} -- {reason}; {warheads} warhead(s).");

			AnnounceAutoFire();
		}

		/// <summary>
		/// <para>EVERY SIDE THAT DID NOT PLACE FIRES ANYWAY — its own weapon, its own package size,
		/// aimed at the enemy. This is what replaced Dead Hand.</para>
		///
		/// <para>IT GOES THROUGH THE REAL ORDER PATH, and that is the whole implementation choice. The
		/// order built here is byte-for-byte the one <see cref="SelectMultiPowerTarget"/> emits on a
		/// player's last click — same key, same subject, same Target, same encoded aim-point list — so
		/// the auto-fire pays every toll a player's own placement pays: the launch sounds, the
		/// escalation report and its veto, the approach vector, the per-warhead cascade slot, the
		/// beacons, the minimap pings and the magazine. A private entry point would have had to
		/// reimplement all of that, and would have drifted from it at the first retune.</para>
		///
		/// <para>SEAT ORDER, WHICH DECIDES SLOT ORDER. <see cref="autoFire"/> is appended to by
		/// <see cref="ArmGameEnders"/> as it walks world.Players, so the earliest-seated unplaced side
		/// takes the earlier cascade slots — identically on every client.</para>
		/// </summary>
		void FireUnplacedPackages()
		{
			foreach (var (player, key) in autoFire)
			{
				if (player.WinState == WinState.Lost)
				{
					closeCensus.Add((player.InternalName, "defeated", 0));
					continue;
				}

				if (window.HasPlaced(player.InternalName))
				{
					closeCensus.Add((player.InternalName, "placed its own", ImpactCountFor(player)));
					continue;
				}

				// The zero-window path's trigger is mid-launch further up this very call stack.
				if (player.InternalName == zeroWindowTrigger)
				{
					closeCensus.Add((player.InternalName, "trigger, already launching", ImpactCountFor(player)));
					continue;
				}

				var manager = player.PlayerActor.TraitOrDefault<SupportPowerManager>();
				if (manager == null || !manager.Powers.TryGetValue(key, out var instance))
				{
					closeCensus.Add((player.InternalName, $"no `{key}` on its manager", 0));
					continue;
				}

				// NOT Ready IS A LEGITIMATE OUTCOME AND IS SAID OUT LOUD. The host turned the arsenal
				// off (`nuke-arsenal-disabled` disables the trait, so the instance has no enabled
				// Instances and never became Active), or something else spent the banked shot. The old
				// map-wide salvo hid this case by firing for everybody from a weapon nobody owned.
				if (!instance.Ready)
				{
					closeCensus.Add((player.InternalName,
						$"`{key}` not ready at the close (arsenal checkbox off, or the banked shot was spent)", 0));
					continue;
				}

				var cells = ChooseAimPoints(player, instance.Info);
				if (cells.Count == 0)
				{
					closeCensus.Add((player.InternalName,
						"no aim point could be found -- the enemy side classified as empty", 0));
					continue;
				}

				// Target is the first aim point, carried for the target line, the minimap ping and
				// SupportPowerInstance's own snap. The AUTHORITATIVE list is TargetString.
				var order = new Order(key, manager.Self, Target.FromCell(world, cells[0]), false)
				{
					SuppressVisualFeedback = true,
					TargetString = MultiAimPointOrder.Serialize(cells)
				};

				Log.Write("debug", $"FINAL EXCHANGE: auto-firing `{key}` for {player.InternalName} at " +
					$"{cells.Count} aim point(s) [{cells.Select(c => c.ToString()).JoinWith(" ")}].");

				foreach (var c in cells)
					autoFiredAimPoints.Add((player, c));

				instance.Activate(order);
				closeCensus.Add((player.InternalName, $"auto-fired `{key}`", ImpactCountFor(player)));
			}

			// ==== AND EVERY PLAYER THE LOOP ABOVE NEVER REACHED ====
			// `autoFire` only holds players ArmGameEnders armed, and ArmGameEnders only walks sides.
			// A player that is not a side -- or that is one and holds no game-ender -- falls out of
			// BOTH, which is precisely how Russia vanished from run 260920_140352 without a single
			// line naming it. Walking world.Players is what makes that impossible.
			foreach (var p in world.Players)
			{
				if (closeCensus.Any(e => e.Side == p.InternalName))
					continue;

				closeCensus.Add((p.InternalName, CombatantSides.CountsAsASide(p)
					? "a side, but ArmGameEnders armed it nothing -- see the arming log above"
					: $"NOT A SIDE (Playable={p.Playable}, NonCombatant={p.NonCombatant}, "
						+ $"authored-non-combatant={p.PlayerReference?.NonCombatant}, "
						+ $"authored-spectator={p.PlayerReference?.Spectating})", 0));
			}
		}

		/// <summary>Warheads this side has reserved a cascade slot for so far. Census column.</summary>
		int ImpactCountFor(Player player)
		{
			var n = 0;
			foreach (var (p, _) in exchangeImpacts)
				if (p == player)
					n++;

			return n;
		}

		/// <summary>
		/// <para>Where <paramref name="firer"/>'s unplaced package goes. The tiering and the geometry are
		/// <see cref="FinalExchangeTargeting"/>'s; everything here is the world lookup that feeds it.</para>
		/// </summary>
		// ==== THE SIDE CLASSIFIER IS BORROWED, NEVER COPIED ====
		// DefconWall owns where the border is, and since 33201a86 it exposes a LEVEL-INDEPENDENT
		// surface -- HasBorder / SideOf / IsInBand -- that resolves in every mode, at every DEFCON
		// level, because a border is a property of the map rather than of the escalation. That is
		// what is asked here. This file keeps no second copy of where the line is, and the two
		// delegates below are the whole of its knowledge of the subject.
		//
		// THE FALLBACK IS STILL REACHED, on a map with no border at all (HasBorder false) and on one
		// where neither anchor classifies. It is the Voronoi split by spawn -- the same construction
		// DefconWallGeometry.BisectorOfSides derives its LINE from, so where both exist they agree
		// almost everywhere.
		List<CPos> ChooseAimPoints(Player firer, SupportPowerInfo powerInfo)
		{
			var enemies = new List<Player>();
			foreach (var p in world.Players)
				if (CombatantSides.CountsAsASide(p) && p != firer && !p.IsAlliedWith(firer))
					enemies.Add(p);

			if (enemies.Count == 0)
				return new List<CPos>();

			var assets = EnemyAssets(firer, enemies);

			// THE SEPARATION IS THE POWER'S OWN AimPointRadius, which is the ring the placement overlay
			// draws around each click: "put two aim points closer than this and the second one is
			// buying nothing" (nuclear-arsenal.yaml). The machine obeys the rule the player is shown.
			var separation = powerInfo is MissileStrikePowerInfo missile ? missile.AimPointRadius.Length / 1024 : 0;

			var (sideOf, inBand) = Classifier(firer);

			return FinalExchangeTargeting.Choose(
				PackageSize, world.Map.Bounds, assets, sideOf, inBand, EnemySide, separation);
		}

		const int OwnSide = 0;
		const int EnemySide = 1;

		/// <summary>
		/// <para>The two delegates <see cref="FinalExchangeTargeting.Choose"/> asks about a cell, built
		/// for one firing side. Everything not on the firer's own side of the border is the enemy's —
		/// which is the right reading for a region map with more than two components, and identical to
		/// "the other player's half" on the two-component maps that ship.</para>
		/// </summary>
		// WHY BOTH DELEGATES WHEN SideOf ALREADY RETURNS NoSide IN THE BAND. It does, so `inBand` is
		// redundant on the wall path and the targeting would reject a band cell either way. It is
		// passed anyway because it is NOT redundant on the FALLBACK path, where there is no band at
		// all and nothing else would exclude one -- so the pair is the same shape whichever
		// classifier is in use, and a future caller cannot be caught out by the difference.
		(Func<CPos, int> SideOf, Func<CPos, bool> InBand) Classifier(Player firer)
		{
			var wall = world.WorldActor.TraitOrDefault<DefconWall>();
			if (wall != null && wall.HasBorder)
			{
				var ownAnchor = AnchorOf(firer);
				var ownSide = ownAnchor.HasValue ? wall.SideOf(ownAnchor.Value) : DefconWall.NoSide;

				if (ownSide != DefconWall.NoSide)
				{
					int WallSideOf(CPos c)
					{
						var s = wall.SideOf(c);
						if (s == DefconWall.NoSide)
							return DefconWall.NoSide;

						return s == ownSide ? OwnSide : EnemySide;
					}

					return (WallSideOf, wall.IsInBand);
				}

				Log.Write("debug", $"FINAL EXCHANGE: {firer.InternalName}'s anchor does not classify against the " +
					"border; falling back to spawn proximity for the targeting.");
			}

			// Anchors, in seat order, with a parallel side label. Indices are what HomeProximitySide
			// returns; the labels are what Choose compares against. A player with no anchor at all
			// contributes nothing rather than dragging the whole split onto cell (0, 0).
			var anchors = new List<CPos>();
			var anchorSides = new List<int>();
			foreach (var p in world.Players)
			{
				// LATENT UNTIL THE FALLBACK PATH IS TAKEN, AND THEN TOTAL: with one anchor in the
				// list HomeProximitySide returns index 0 for every cell, so the whole map classifies
				// as the enemy's and the firer bombs its own half.
				if (!CombatantSides.CountsAsASide(p))
					continue;

				var anchor = AnchorOf(p);
				if (!anchor.HasValue)
					continue;

				anchors.Add(world.Map.CellContaining(anchor.Value));
				anchorSides.Add(p.IsAlliedWith(firer) ? OwnSide : EnemySide);
			}

			// NOBODY HAS AN ANCHOR: no border, no Supply Routes, no spawn points. A classifier that
			// answered NoSide for every cell would reject the whole map and deliver an empty package,
			// so the honest answer is a null classifier -- the whole playable rectangle is in play.
			// See FinalExchangeTargeting.Choose, which treats null exactly that way.
			if (anchors.Count == 0)
				return (null, null);

			int ProximitySideOf(CPos c)
			{
				var i = FinalExchangeTargeting.HomeProximitySide(anchors, c);
				return i < 0 ? DefconWall.NoSide : anchorSides[i];
			}

			return (ProximitySideOf, null);
		}

		/// <summary>
		/// <para>Where a player IS, for the purpose of deciding which half of the map is theirs: the
		/// centre of their lowest-ActorID <see cref="BaseBuilding"/> — their Supply Route — or the
		/// centre of their HomeLocation cell when they have no structure left.</para>
		/// </summary>
		// ==== Player.HomeLocation IS A LIE ON EVERY AUTOTEST SCENARIO, AND THIS IS WHY THE ANCHOR
		// ==== EXISTS RATHER THAN A DIRECT DefconWall.SideOf(Player) CALL.
		// HomeLocation is CPos.Zero -- the FIELD DEFAULT of PlayerReference.HomeLocation -- for a map
		// player, and ALSO for an ordinary lobby player on any map that strips MapStartingLocations,
		// because Player.cs:213 falls back to the PlayerReference when there is no IAssignSpawnPoints
		// trait to ask. Every scenario under tools/autotest/scenarios strips it. So SideOf(Player)
		// there reads cell (0, 0) for BOTH sides, answers the same value twice, and this method's
		// caller would classify the entire map as the firer's own -- an empty asset list and a
		// package made entirely of padding. The same trap is documented at the declaration of
		// DefconWall.SideOf(Player), which says in terms that a caller with a better home should pass
		// that instead. This is that caller.
		//
		// ON EVERY SHIPPED MAP THE TWO AGREE: the Supply Route's CenterPosition sits exactly on the
		// centre of its owner's spawn cell, so the anchor IS CenterOfCell(HomeLocation) there. The
		// anchor is only DIFFERENT where HomeLocation is absent, which is where it is also the only
		// one of the two that is right.
		//
		// NULL RATHER THAN CPos.Zero when neither exists, so a caller cannot mistake the coordinate
		// default for a position a second time.
		WPos? AnchorOf(Player player)
		{
			if (player == null)
				return null;

			// Lowest ActorID: assigned in world-creation order and identical on every client, so a
			// player with two BaseBuildings anchors on the same one everywhere.
			var baseBuilding = world.Actors
				.Where(a => a.IsInWorld && !a.Disposed && a.Owner == player && a.Info.HasTraitInfo<BaseBuildingInfo>())
				.OrderBy(a => a.ActorID)
				.FirstOrDefault();

			if (baseBuilding != null)
				return baseBuilding.CenterPosition;

			return player.HomeLocation != CPos.Zero ? world.Map.CenterOfCell(player.HomeLocation) : null;
		}

		/// <summary>
		/// <para>Everything on the map worth a warhead, from <paramref name="firer"/>'s point of view, in
		/// the three asset tiers <see cref="FinalExchangeTargeting"/> ranks.</para>
		///
		/// <para>SORTED BY ActorID, which is assigned in world-creation order and is therefore identical
		/// on every client. Everything downstream — the clustering, the centroids, the tie-breaks —
		/// inherits this ordering.</para>
		///
		/// <para>NO TARGET-TYPE TEST AND NO BuildingInfo TEST, unlike the salvo this replaced. That test
		/// existed to keep river-zeta's four thousand crop tiles out of a MAP-WIDE enumeration; this one
		/// is restricted to actors the ENEMY owns, and no player owns a rice paddy. Neutral scenery is
		/// reached only through <see cref="DoomsdayStrikeInfo.NeutralHighValueTypes"/>, which is a short
		/// explicit list.</para>
		/// </summary>
		List<FinalExchangeAsset> EnemyAssets(Player firer, List<Player> enemies)
		{
			var assets = new List<FinalExchangeAsset>();

			var enemyCells = new List<CPos>();
			var actors = world.Actors
				.Where(a => a.IsInWorld && !a.Disposed
					&& !info.ExcludeTypes.Contains(a.Info.Name.ToLowerInvariant()))
				.OrderBy(a => a.ActorID)
				.ToList();

			foreach (var a in actors)
			{
				var name = a.Info.Name.ToLowerInvariant();

				if (enemies.Contains(a.Owner))
				{
					// ---- TIER 1. Nothing outranks the thing the whole mod is about.
					if (info.SupplyRouteTypes.Contains(name))
						assets.Add(new FinalExchangeAsset(a.Location, FinalExchangeTier.SupplyRoute, 0));

					// ---- and everything of theirs feeds the clustering below.
					if (a.Info.HasTraitInfo<HealthInfo>())
						enemyCells.Add(a.Location);

					continue;
				}

				// ---- TIER 3. A neutral high-value asset, wherever it stands; Choose drops the ones
				// that are not on the enemy's ground.
				if (a.Owner != firer && !a.Owner.IsAlliedWith(firer) && info.NeutralHighValueTypes.Contains(name))
					assets.Add(new FinalExchangeAsset(a.Location, FinalExchangeTier.NeutralAsset, 0));
			}

			// ---- TIER 2. Single-linkage clustering, ranked by member count. DoomsdayMath.ClusterAssets
			// is unchanged and still pinned by its own tests; what changed is the input, which is now
			// one side's actors rather than every building on the map.
			foreach (var cluster in DoomsdayMath.ClusterAssets(enemyCells, concentrationLinkCells))
				assets.Add(new FinalExchangeAsset(
					DoomsdayMath.Centroid(enemyCells, cluster), FinalExchangeTier.Concentration, cluster.Count));

			return assets;
		}

		void AnnounceAutoFire()
		{
			TextNotificationsManager.AddSystemLine("The packages are in the air.");

			// THE SPLIT, said out loud. The user's ruling is that placing and not placing reach the same
			// ending, so the only thing left to tell the player is which of the two they took — and a
			// side that never saw a cameo (arsenal off, power unaffordable, never clicked) reads its own
			// name here rather than being left to guess why nothing of theirs flew.
			var placedFor = window.SidesPlacedForByDeadHand().ToList();
			if (placedFor.Count > 0)
				TextNotificationsManager.AddSystemLine(
					"Fired automatically for: " + placedFor.JoinWith(", ") + ".");
		}

		/// <summary>
		/// Move the annihilation and the verdict out past an impact that lands later than anything
		/// already scheduled. Monotonic — it only ever pushes the schedule later, so no ordering of
		/// reports can bring the sweep forward onto a warhead still in the air.
		/// </summary>
		void ExtendScheduleForImpact(int impactTick)
		{
			if (impactTick <= lastImpactTick)
				return;

			lastImpactTick = impactTick;
			annihilationTick = lastImpactTick + info.AnnihilationDelayTicks;
			resolutionTick = annihilationTick + info.ResolutionDelayTicks;
		}

		/// <summary>
		/// <para>Stop every per-player accumulator, on the tick the clock expired.</para>
		///
		/// <para>This is a FREEZE OF THE ACCUMULATION, not a snapshot of the display. Two flags do it, and they
		/// were chosen because they are choke points rather than because they are convenient:
		///   * PlayerExperience.Frozen — GiveExperience is the single entry point through which every
		///     score-affecting event in the engine passes (kills via GivesExperience, captures, donations,
		///     infiltration, repairs, Lua). Guarding it there covers all of them at once, which is why the
		///     kills the nukes themselves cause credit nobody.
		///   * PlayerStatistics.Frozen — stops the income/army sampling in its Tick and every
		///     UpdatesPlayerStatistics lifecycle callback, so unit counts, asset values, kill/death tallies
		///     and the composition telemetry all stop where they were.</para>
		/// </summary>
		void FreezeStatistics()
		{
			foreach (var p in world.Players)
			{
				p.PlayerActor.TraitOrDefault<PlayerStatistics>()?.Freeze();
				p.PlayerActor.TraitOrDefault<PlayerExperience>()?.Freeze();
			}
		}

		/// <summary>
		/// <para>Lift the shroud and the fog for every player, so the exchange is watched over the whole map.</para>
		///
		/// <para>WHY THIS IS NOT THE INTELLIGENCE LEAK IT LOOKS LIKE. Revealing the map normally hands a player
		/// free information, which is exactly why the nuclear-flash-over-fog work brightens the effect
		/// without lifting the shroud. That objection does not apply here and it is worth being explicit
		/// about why, because a future reader will otherwise see a map reveal in gameplay code and assume
		/// it is a bug: by the time this runs the statistics are frozen (see <see cref="FreezeStatistics"/>),
		/// the winner is already determined by the frozen score, the ordinary victory checks are suspended,
		/// and <see cref="Annihilate"/> is going to kill every actor on the map in a few seconds. There is
		/// no information advantage left to leak because there is no game left to play.</para>
		///
		/// <para>IT IS ALSO WHAT MAKES THE WINDOW PLAYABLE. A player cannot aim at an enemy base they cannot
		/// see, and fifteen seconds is not long enough to go looking.</para>
		///
		/// <para>DETERMINISM. Disabled is [Sync] simulation state, and this runs on a tick every client agrees
		/// on, for every player in the same fixed world.Players order — so all clients make the same change
		/// on the same tick. Setting it on every player rather than only the local one is what keeps that
		/// true; a local-only reveal would desync the [Sync] hash.</para>
		/// </summary>
		void RevealMap()
		{
			// Player.MapLayers is a readonly field resolved with Trait<MapLayers>() at construction
			// (Player.cs:221), so it is never null here; the same direct access GpsWatcher and
			// RevealMapCrateAction use.
			foreach (var p in world.Players)
				p.MapLayers.Disabled = true;
		}

		void ITick.Tick(Actor self)
		{
			if (!triggered)
				return;

			// THE WINDOW. Tick reports the closing edge exactly once (FinalExchangeWindow.Tick), so the
			// auto-fire hangs off it with no second flag here.
			if (window.Phase == FinalExchangePhase.Open)
			{
				if (!window.Tick(world.WorldTick))
					return;

				FirePackagesAndScheduleTheTail();
			}

			if (!annihilated && world.WorldTick >= annihilationTick)
			{
				annihilated = true;
				Annihilate();
			}

			if (!resolved && world.WorldTick >= resolutionTick)
			{
				resolved = true;
				Resolve();
			}
		}

		/// <summary>
		/// <para>The backstop. Destroy everything still standing, so "nothing survives" is a property of the
		/// mode rather than a property of this week's warhead tuning. See the class remarks for why this
		/// exists alongside the packages rather than instead of them.</para>
		///
		/// <para>Statistics are already frozen, so none of these deaths reach anybody's score.</para>
		/// </summary>
		void Annihilate()
		{
			// Snapshot first: Kill mutates the actor list through husks and death effects, and iterating
			// world.Actors lazily while that happens is how this would throw.
			var doomed = world.Actors
				.Where(a => a.IsInWorld && !a.Disposed && a.Info.HasTraitInfo<HealthInfo>())
				.OrderBy(a => a.ActorID)
				.ToList();

			foreach (var a in doomed)
				if (a.IsInWorld && !a.Disposed)
					a.Kill(a, info.AnnihilationDamageTypes);

			Log.Write("debug", $"FINAL EXCHANGE annihilation at tick {world.WorldTick}: " +
				$"{doomed.Count} actor(s) destroyed. Verdict due at tick {resolutionTick}.");

			TextNotificationsManager.AddSystemLine("Total strategic annihilation.");
		}

		/// <summary>
		/// <para>Apply the verdict from the frozen score.</para>
		///
		/// <para>This deliberately REUSES the shipped time-limit resolution rather than reimplementing it: the
		/// suspension is lifted and <see cref="INotifyTimeLimit.NotifyTimerExpired"/> is re-raised on the
		/// player actors, which runs ConquestVictoryConditions' existing highest-Experience-wins
		/// comparison. Because PlayerExperience has been frozen since the expiry tick, that comparison
		/// reads exactly the numbers it would have read then — which is what makes the freeze
		/// load-bearing rather than decorative, and is the property DoomsdayStatsFreezeTest pins.</para>
		///
		/// <para>SIMULTANEOUS ELIMINATION IS NOT A CASE HERE. Every player was destroyed on the same tick by
		/// the sweep above, but no player has a WinState yet, because the victory checks were suspended
		/// for the whole exchange. So the tie-break never runs on "who lost their last unit last" — there
		/// is exactly one ordering decision, taken here, over frozen numbers. A genuine score TIE resolves
		/// through the existing OrderByDescending, which is a stable sort over world.Players in its fixed
		/// creation order: the earliest-seated tied player wins, identically on every client.</para>
		/// </summary>
		void Resolve()
		{
			SalvoInProgress = false;

			// THE FROZEN SCORE, NAMED, ON THE TICK IT IS READ. ConquestVictoryConditions does the
			// comparison and logs nothing about it, so a match that ended on the wrong winner had no
			// evidence trail at all -- and "the score as it stood when the exchange opened" is exactly
			// the claim a reader would want to check.
			Log.Write("debug", $"FINAL EXCHANGE resolution at tick {world.WorldTick}, from the score frozen at " +
				"the trigger tick: " + string.Join(", ", world.Players
					.Where(CombatantSides.CountsAsASide)
					.Select(p => $"{p.InternalName}={p.PlayerActor.TraitOrDefault<PlayerExperience>()?.Experience ?? 0}")));

			foreach (var p in world.Players)
				foreach (var ntl in p.PlayerActor.TraitsImplementing<INotifyTimeLimit>())
					ntl.NotifyTimerExpired(p.PlayerActor);
		}
	}
}
