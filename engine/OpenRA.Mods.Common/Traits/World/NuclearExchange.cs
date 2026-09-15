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
 * THE NUCLEAR EXCHANGE'S WORLD TRAIT -- the per-SIDE state of NuclearExchangeState, the one lobby
 * dropdown that configures it, and the three things it has to reach out and touch.
 *
 * The rules themselves are in NuclearExchangeState and are verified by unit test without a World.
 * Everything here is the part that needs one: who counts as a side, when the release gate opened,
 * putting a side's whole arsenal on one cooldown, and beginning the final exchange.
 *
 * ==== WHY THIS IS A NEW TRAIT AND NOT MORE FIELDS ON DefconEscalation ====
 * DefconEscalation owns the ALERT LEVEL and the clock that walks it down. That is a different
 * question from what each side may fire, it is asked in Skirmish too (where this trait does
 * nothing), and putting the exchange's dropdown on DefconEscalationInfo would have put two separate
 * work streams into one option block. The two traits meet at exactly one place: this one polls
 * DefconEscalation.NuclearReleaseOpen, which is still where the release GATE lives.
 *
 * ==== A SIDE IS A TEAM, OR A PLAYER WITH NO TEAM ====
 * Decision 15: exactly two sides. The key is the lobby team number when there is one, and a unique
 * negative derived from the player's index in world.Players when there is not -- so a 1v1 with no
 * teams set is two sides, and a 2v2 is two sides. THIS IS NOT ENFORCED: a lobby with three or more
 * sides logs a warning once and then escalates EVERY OTHER SIDE on each launch, which is the honest
 * reading of "the other side" when there is more than one of them. Building enforcement is
 * explicitly out of scope.
 *
 * ==== v2: A LEVEL RATCHET AND ONE SIDE-WIDE COOLDOWN (2026-09-15) ====
 * The retaliation window is GONE -- the model, the lobby dropdown, the banner copy and the bot's
 * reply logic with it. See NuclearExchangeState's header for why it was deleted rather than
 * lengthened. What this trait now has to make true on screen is two sentences:
 *
 *     A side's cameos are drawn for every band at or below its LEVEL, and for no band above it.
 *     All of them are simultaneously dark for the length of its COOLDOWN after any one of them fires.
 *
 * The first sentence is the condition layer's, unchanged: GrantConditionOnNuclearRelease reads
 * LevelFor and grants `nuclear-release-*` cumulatively. The second is this file's, and it is the
 * part with an engine problem behind it.
 *
 * ==== WHY THE COOLDOWN IS WRITTEN ONTO EVERY POWER RATHER THAN GATED ONCE ====
 * The obvious implementation is a gate: refuse the order while the side is on cooldown. It was
 * rejected because THE CAMEO'S CLOCK HAS TO BE HONEST. A gate leaves five cameos sitting there
 * reading READY, and the player finds out they are not by clicking one; the shipped support-power
 * widget already draws a countdown and a clock wipe, and the cooldown is exactly the number those
 * are for. So the cooldown is written onto the SupportPowerInstance of every nuclear power the side
 * holds -- SetSideCooldown -- and the engine draws it with no new widget at all.
 *
 * SupportPowerInstance.TotalTicks HAD TO BECOME SETTABLE FOR THAT, and the reason is a clamp rather
 * than a preference: Tick() pins remainingSubTicks to TotalTicks * 100 on the next tick, so a 1 kt
 * power built with a five-minute interval silently truncates the twelve-minute cooldown its team's
 * 100 kt shot just earned. SetCooldown writes both numbers together. See its doc comment.
 *
 * ==== AND THE GRANT STILL HAS TO FORCE READINESS, FOR TWO REASONS THAT SURVIVE v2 ====
 *   1. A power gated off by RequiresCondition DOES NOT ACCUMULATE CHARGE. SupportPowerInstance.Tick
 *      recomputes `instancesEnabled` and, when it is false, assigns `remainingSubTicks =
 *      TotalTicks * 100` -- i.e. resets the timer to FULL every tick it is disabled. So a band that
 *      has been dark all match starts a complete interval at the moment its level is reached, and a
 *      side escalated to 50 kt would wait a further cooldown before seeing the cameo it was just
 *      handed.
 *   2. ON TODAY'S ARSENAL THERE IS NO NATURAL TIMER AT ALL. Every nuclear power in the mod sets
 *      RequiresPurchase: True, which would force TotalTicks to 0 and make readiness a question of
 *      whether a shot has been BOUGHT -- and decision 02 says nothing nuclear is purchasable in this
 *      mode. EscalationCooldownTicks is the bypass; SupportPowerInstance's constructor is where it
 *      is applied.
 *
 * So a level rise makes the newly granted bands ready -- AT THE SIDE'S REMAINING COOLDOWN, not at
 * zero. That qualifier is rule 2 and it is easy to lose: a side that fires a 1 kt and is then hit by
 * a 20 kt has its level raised WHILE IT IS ON COOLDOWN, and a grant that zeroed the timer would hand
 * it a free 50 kt shot the cooldown was meant to deny. MakeBandsReady carries it.
 *
 * ==== AND THE TOP RUNG HAS A THIRD GATE THAT IS NOT A TIMER AT ALL (FIXED 2026-09-14) ====
 * A grant that solved both problems above still granted a player nothing at the END band. A user
 * reported it from a real match: the ledger lit the END box and drew its countdown, and no
 * game-ender cameo ever appeared.
 *
 * The cause is a PREREQUISITE and not a clock. MakeBandsReady gated on SupportPowerInstance.Permitted,
 * which ANDs `prereqsAvailable`; both shipped national game-enders declare `powers.event`
 * (player.yaml:236-240), which NO faction provides and which exists precisely so a game-ender can
 * never be bought. So the gate was shut, MakeReady -- the one call that clears that flag -- was never
 * reached, and the catch-22 was invisible at every band below the top because each faction owns its
 * own warhead there outright.
 *
 * DoomsdayStrike had already met and documented this on the final-exchange path and answered it with
 * OverriddenPrerequisites plus an ownership check; this trait was written later and did not read it.
 * The answer is now stated once in NuclearGameEnders and both paths ask it. See ArmableAtTopRung for
 * why the override is scoped to the top rung, why the power's own condition must still hold, and why
 * the FACTION half of a prerequisite is never overridable.
 *
 * NOTHING IS TAKEN BACK, and under v2 there is nothing to take back: levels never fall, so a band
 * once granted stays granted for the rest of the match. The revocation path in
 * GrantConditionOnNuclearRelease is still live and still correct -- it is what keeps a band above
 * the level dark -- it simply has no falling edge left to run on.
 *
 * ==== DETERMINISM ====
 * Integer arithmetic, no RNG, no wall-clock. Every enumeration is ordered: sides in registration
 * order (NuclearExchangeState.Sides), players in world.Players order, and a player's support powers
 * by ordinal key. The two writes that leave this trait -- MakeReady and SetCooldown -- run from
 * ITick on the World actor, which every client ticks identically, and from ReportNuclearRelease,
 * which is reached only through SupportPowerManager.ResolveOrder and is therefore on the synced
 * order-resolution path (the argument DefconEscalation.ReportNuclearRelease used to carry,
 * unchanged by the move).
 */

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("The DEFCON Escalation nuclear exchange: what each SIDE may fire, and the side-wide",
		"cooldown a launch puts that side on. Attach to the World actor, alongside",
		nameof(DefconEscalation) + ", which owns the release gate this trait polls.",
		"",
		"STRICT NO-OP OUTSIDE " + nameof(DefconGameMode.Escalation) + ". In Skirmish and Sandbox the",
		"released band comes from " + nameof(NuclearUnlockClock) + " instead and nothing here runs.")]
	public class NuclearExchangeInfo : TraitInfo, ILobbyOptions, IRulesetLoaded
	{
		public const string PostureOptionId = "nuclear-posture";

		[Desc("Label for the nuclear posture dropdown.")]
		// SENTENCE CASE, like every other lobby label this mod ships -- "Game mode", "Opening phase",
		// "No-rush period", "First warheads", "Nuclear ending". This one read "Nuclear Posture" until
		// 2026-09-14 and was the only title-cased label among them.
		public readonly string PostureLabel = "Nuclear posture";

		[Desc("Tooltip for the nuclear posture dropdown.")]
		// IT NAMES THE THREE VALUES THE DROPDOWN ACTUALLY OFFERS. It used to explain "Limited War",
		// "Flexible Response" and "Massive Retaliation" -- the Cold War doctrines the three postures are
		// drawn from -- while the dropdown itself offers "Limited", "Flexible" and "Massive" (see the
		// posture dictionary below), so the tooltip taught three names a host could not then find.
		//
		// IT SAYS "YOUR WHOLE ARSENAL" SINCE v2, because that is what changed: this used to scale four
		// independent per-band timers, and a host reading "how fast warheads come back" would have
		// taken it as a per-weapon wait rather than as the single side-wide lockout it now sets.
		public readonly string PostureDescription =
			"How long your whole arsenal is locked out after any nuclear launch. Limited stretches " +
			"every cooldown, so an exchange is a handful of deliberate shots; Flexible leaves them " +
			"as shipped; Massive shortens them, so a spiral runs to its end quickly.";

		[Desc("Default nuclear posture.")]
		public readonly NuclearPosture PostureDefault = NuclearPosture.Flexible;

		[Desc("Whether to show the nuclear posture dropdown in the lobby.")]
		public readonly bool PostureVisible = true;

		[Desc("Prevent the nuclear posture dropdown from being changed in the lobby.")]
		public readonly bool PostureLocked = false;

		[Desc("Display order for the nuclear posture dropdown.")]
		public readonly int PostureDisplayOrder = 24;

		// ==== THE SIDE-COOLDOWN TABLE. USER-RULED 2026-09-15, AND THE NUMBERS ARE THE RULING. ====
		// "Nukes become rare punctuation; conventional play dominates" -- the user's words after
		// playing v1, where a side could keep several bands loaded at once and did. These four are
		// what a launch at that band costs the FIRING SIDE, across every band it holds, and they are
		// the whole economy of the mode: there is no price, no queue and no bank (decision 02).
		//
		// TICKS, AT 60 MS, WRITTEN OUT because this repo has assumed 25 ticks/second at eleven sites
		// and been wrong at every one: 1000/60 = 16.67 ticks/s, so 5:00 = 300 s = 5000 ticks. The
		// identity to check any change against is that a value in ticks divided by 1000 is its length
		// in minutes at this timestep.
		//
		// NUCLEAR POSTURE SCALES ALL FOUR (150 / 100 / 60 %). At Flexible the slowest exchange
		// possible is one warhead every five minutes per side; at Massive, one every three.
		//
		// ==== A COOLDOWN IS SIDE-WIDE, WHICH IS NOT WHAT THESE FIELDS USED TO MEAN ====
		// RENAMED FROM *RegenTicks WITH v2. They used to be four INDEPENDENT per-band clocks: firing
		// 1 kt muted the 1 kt band alone and left 20 kt, 50 kt and 100 kt loaded. That is the shape
		// the user ruled against -- the rate was the number of bands, not the interval -- so the
		// quantity changed with the name. One shot now silences every band the side holds.
		//
		// A TEAM SHARES ONE. Two players on a side fire ONCE per cooldown between them, not once
		// each; the alt-account double-tap is closed by construction rather than by a check.

		[Desc("Ticks the WHOLE SIDE is locked out for after firing the 1 kt band, in Escalation.",
			"5000 ticks = 300 s = 5:00 at the default 60 ms timestep (16.67 ticks/s, NOT 25).",
			"USER-RULED 2026-09-15.")]
		public readonly int KilotonCooldownTicks = 5000;

		[Desc("Ticks the WHOLE SIDE is locked out for after firing the 20 kt band, in Escalation.",
			"7000 ticks = 420 s = 7:00 at the default 60 ms timestep. USER-RULED 2026-09-15.")]
		public readonly int TwentyKilotonCooldownTicks = 7000;

		[Desc("Ticks the WHOLE SIDE is locked out for after firing the 50 kt band, in Escalation.",
			"9000 ticks = 540 s = 9:00 at the default 60 ms timestep. USER-RULED 2026-09-15.")]
		public readonly int FiftyKilotonCooldownTicks = 9000;

		[Desc("Ticks the WHOLE SIDE is locked out for after firing the 100 kt band, in Escalation.",
			"12000 ticks = 720 s = 12:00 at the default 60 ms timestep. USER-RULED 2026-09-15.",
			"",
			"NOT THE GAME-ENDER BAND'S VALUE. Firing a game-ender takes NO cooldown at all -- the",
			"match ends on that launch, so a lockout would be a number nobody lives to read. See",
			nameof(NuclearExchangeState) + "." + nameof(NuclearExchangeState.ReportLaunch) + ".")]
		public readonly int HundredKilotonCooldownTicks = 12000;

		[Desc("Prerequisites the top rung is licensed to IGNORE when it arms a game-ender. Read ONLY",
			"on " + nameof(NuclearRung.GameEnder) + "; every band below it is armed on ",
			nameof(SupportPowerInstance.Permitted) + " alone and this field cannot reach them.",
			"",
			"WHY THE TOP RUNG NEEDS ONE AT ALL. Both shipped national game-enders declare",
			"`powers.event` (player.yaml:236-240), a prerequisite NO faction provides -- it exists so",
			"a game-ender is never on the shop floor. Every band below the top is a weapon each",
			"faction owns outright, so the ordinary gate opens for them; at the top it can never open,",
			"and before 2026-09-14 reaching the END level therefore granted a player nothing at all",
			"while the ledger lit its box. That was the reported bug.",
			"",
			"THE FACTION HALF IS NOT OVERRIDABLE AND MUST NOT BE ADDED HERE. `player.america` and",
			"`player.russia` are an identity rather than a shelf; naming one below would hand an",
			"America player Russia's Sarmat and undo c8cadc8a. Same field, same default and the same",
			"reasoning as " + nameof(DoomsdayStrikeInfo) + "." + nameof(DoomsdayStrikeInfo.OverriddenPrerequisites) + ",",
			"which is the other path that hands these weapons out; both ask",
			nameof(NuclearGameEnders) + "." + nameof(NuclearGameEnders.ArmableBy) + " so they cannot drift.",
			"",
			"EMPTY IS THE STRICT SETTING: it restores the pre-fix behaviour exactly, which is a top",
			"rung that grants nothing outside Sandbox.")]
		public readonly string[] OverriddenPrerequisites = { "powers.event" };

		[Desc("Ticks a grant is retried for while the band condition it needs has not reached the",
			"support power yet. NOT a gameplay duration: the condition is granted by a PLAYER-actor",
			"trait and this runs on the WORLD actor, so a grant issued here can land one or two ticks",
			"before the power it is aimed at is enabled. 30 ticks = 1.8 s at the 60 ms timestep, which",
			"is two orders of magnitude more slack than the one-or-two-tick case needs.")]
		public readonly int GrantRetryTicks = 30;

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (GrantRetryTicks < 0)
				throw new YamlException($"{nameof(GrantRetryTicks)} must be 0 or positive.");

			// POSITIVE, NOT MERELY NON-NEGATIVE. A cooldown of 0 ticks is a side that may fire again
			// on the tick after it fired, which is not a fast economy but no economy at all -- and it
			// is precisely the spam the 2026-09-15 ruling exists to stop.
			foreach (var (name, ticks) in new[]
			{
				(nameof(KilotonCooldownTicks), KilotonCooldownTicks),
				(nameof(TwentyKilotonCooldownTicks), TwentyKilotonCooldownTicks),
				(nameof(FiftyKilotonCooldownTicks), FiftyKilotonCooldownTicks),
				(nameof(HundredKilotonCooldownTicks), HundredKilotonCooldownTicks),
			})
				if (ticks <= 0)
					throw new YamlException($"{name} must be a positive tick count: in DEFCON Escalation " +
						"a launch locks out the firing side's whole arsenal, and 0 would let it fire every tick.");
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			var postures = new Dictionary<string, string>
			{
				{ nameof(NuclearPosture.Limited).ToLowerInvariant(), "Limited" },
				{ nameof(NuclearPosture.Flexible).ToLowerInvariant(), "Flexible" },
				{ nameof(NuclearPosture.Massive).ToLowerInvariant(), "Massive" },
			};

			yield return new LobbyOption(PostureOptionId, PostureLabel, PostureDescription, PostureVisible,
				PostureDisplayOrder, postures, PostureDefault.ToString().ToLowerInvariant(), PostureLocked);
		}

		/// <summary>The four side cooldowns in ascending band order, UNSCALED by posture.</summary>
		// Built here rather than at each call site so the ORDER is stated once: index 0 is
		// NuclearRung.Kiloton, which is what NuclearExchangeState.CooldownTicksFor indexes against.
		public IReadOnlyList<int> CooldownTicks()
		{
			return new[] { KilotonCooldownTicks, TwentyKilotonCooldownTicks, FiftyKilotonCooldownTicks, HundredKilotonCooldownTicks };
		}

		public override object Create(ActorInitializer init) { return new NuclearExchange(init.Self, this); }
	}

	// ISync is load-bearing rather than decoration: Actor.cs:206 hashes a trait only when
	// `trait is ISync`, so without it the [Sync] member below would be inert and this trait would be
	// absent from every sync report.
	public class NuclearExchange : ITick, IWorldLoaded, ISync
	{
		readonly NuclearExchangeInfo info;
		readonly World world;

		/// <summary>
		/// <para>The match's game mode, read from the LOBBY at construction rather than forwarded from
		/// <see cref="DefconEscalation"/>.</para>
		///
		/// <para>THAT IS AN ORDERING FIX, NOT A STYLE CHOICE. It used to read `escalation?.Mode`, and
		/// `escalation` is only resolved in WorldLoaded — but <see cref="SupportPowerInstance"/>'s
		/// constructor now asks this trait whether a power is free (see
		/// <see cref="EscalationCooldownTicks"/>), and a player actor can be built before WorldLoaded
		/// runs. Forwarding would have answered "Skirmish" there and silently left every nuclear power
		/// purchased in Escalation — the whole feature off, with nothing to see.</para>
		///
		/// <para>The default comes from <see cref="DefconEscalationInfo.ModeDefault"/> off the World
		/// actor's own ActorInfo, so the two traits cannot disagree about it. Same idiom, and the same
		/// creation-order reason, as <see cref="NuclearUnlockClock"/>'s constructor one file away.</para>
		/// </summary>
		public readonly DefconGameMode Mode;

		/// <summary>The posture the host picked. Scales the side cooldowns; see <see cref="NuclearPostureScale"/>.</summary>
		public readonly NuclearPosture Posture;

		NuclearExchangeState state;
		DefconEscalation escalation;
		DoomsdayStrike doomsday;
		bool released;

		// Player -> side, and the reverse. Built once in WorldLoaded and never written again.
		readonly Dictionary<Player, int> sideOfPlayer = new Dictionary<Player, int>();
		readonly List<Player> combatants = new List<Player>();

		// Side -> the name the ledger draws for it. First combatant registered on that side, so it is
		// filled in the same ordered pass and is identical on every client.
		readonly Dictionary<int, string> sideNames = new Dictionary<int, string>();

		// Each side's LEVEL last tick, so a rise can be spotted without either trait having to call
		// the other.
		//
		// THE LEVEL AND NOT THE SERIAL, and the difference is load-bearing here where it is not in
		// the banner widget. Both find the same EDGE -- levels never fall, so every change is a rise
		// -- but this consumer needs the VALUE as well, to know which bands are newly granted. A
		// serial says only "something moved". Storing the serial instead is what broke the grant
		// path: see ReconcileGrants.
		readonly Dictionary<int, int> lastSeenLevel = new Dictionary<int, int>();

		// Outstanding "make this player's newly granted bands fire-ready" requests. The band is the
		// LOWEST newly granted one; everything from there up to the side's level is topped up in the
		// same pass. See the file header for why a retry budget is needed at all.
		readonly Dictionary<Player, (int FromBand, int TicksLeft)> pendingReady = new Dictionary<Player, (int, int)>();

		/// <summary>
		/// One int covering every side's whole position, so a divergence anywhere in the exchange is
		/// caught rather than silently played out.
		/// </summary>
		// A MIXED HASH RATHER THAN A SUM, because a sum cannot tell "A is at 3 and B at 1" from
		// "A is at 1 and B at 3" -- which is precisely the asymmetry this whole feature is about.
		// Unchecked overflow is deterministic in C# and is what a hash wants.
		[Sync]
		public int ExchangeHash
		{
			get
			{
				if (state == null)
					return 0;

				unchecked
				{
					var h = state.Released ? 1 : 0;
					foreach (var side in state.Sides)
					{
						h = (h * 31) + state.LevelFor(side);
						h = (h * 31) + state.CooldownFor(side);
					}

					return h;
				}
			}
		}

		public NuclearExchange(Actor self, NuclearExchangeInfo info)
		{
			this.info = info;
			world = self.World;

			var settings = world.LobbyInfo.GlobalSettings;

			// `self` IS the World actor, so self.Info is its ActorInfo and is available immediately --
			// nothing here may reach for world.WorldActor, which World.cs:252 has not assigned yet.
			var defconInfo = self.Info.TraitInfoOrDefault<DefconEscalationInfo>();
			var modeDefault = defconInfo?.ModeDefault ?? DefconGameMode.Skirmish;
			var modeRaw = settings.OptionOrDefault(DefconEscalationInfo.ModeOptionId, modeDefault.ToString());
			if (!System.Enum.TryParse(modeRaw, true, out Mode))
				Mode = modeDefault;

			var posture = settings.OptionOrDefault(NuclearExchangeInfo.PostureOptionId, info.PostureDefault.ToString());
			if (!System.Enum.TryParse(posture, true, out Posture))
				Posture = info.PostureDefault;
		}

		/// <summary>The four side cooldowns with this match's posture already applied.</summary>
		// SCALED IN EXACTLY ONE PLACE. NuclearExchangeState is handed the result and applies nothing
		// further, and EscalationCooldownTicks below calls this rather than scaling again -- so a
		// posture can never be applied twice to the same number, which at Limited would be 225 %.
		IReadOnlyList<int> ScaledCooldownTicks()
		{
			var raw = info.CooldownTicks();
			var scaled = new int[raw.Count];
			for (var i = 0; i < raw.Count; i++)
				scaled[i] = NuclearPostureScale.Apply(raw[i], Posture);

			return scaled;
		}

		/// <summary>
		/// <para>The cooldown this power's own band costs in DEFCON Escalation, where it is FREE and
		/// timer-charged rather than bought. Returns -1 when the ordinary purchase economy applies,
		/// which is every power outside Escalation and every non-nuclear power inside it.</para>
		///
		/// <para>THE ONE ENTRY POINT <see cref="SupportPowerInstance"/>'s constructor uses, and it
		/// answers BOTH questions that constructor has to ask — "is this bought?" is `&lt; 0`, and
		/// "what interval does it start on?" is the value. Splitting them into two calls would let the
		/// two answers drift apart, which is exactly the state that produces a power with no timer AND
		/// no magazine: permanently unusable, with nothing logged.</para>
		///
		/// <para>THE VALUE IS A STARTING POINT AND NOT THIS POWER'S REAL WAIT. Under v2 a nuclear power
		/// waits for its SIDE's cooldown, chosen by the band somebody on that side last fired, and
		/// <see cref="SetSideCooldown"/> overwrites both of its numbers whenever that changes. This
		/// answers with the power's own band, which is what the first shot at that band would cost.</para>
		///
		/// <para>IT IS THE IDENTITY OUTSIDE ESCALATION. No World, no trait, a non-nuclear power, or any
		/// other mode all return -1, which is what keeps Skirmish and Sandbox — and every other mod —
		/// byte-identical. Skirmish is the shipped default and every nuclear scenario in the tree buys
		/// its shot; see NuclearExchangeState.IsFreeTimerPower.</para>
		/// </summary>
		public static int EscalationCooldownTicks(World world, SupportPowerInfo powerInfo)
		{
			if (world == null || !(powerInfo is MissileStrikePowerInfo missile))
				return -1;

			// WorldActor is null while world traits are being created (World.cs:252 is the line that
			// assigns it), and a support power manager on a player actor can be constructed inside
			// that window. Same Created hazard DefconWall threw a NullReferenceException on.
			var exchange = world.WorldActor?.TraitOrDefault<NuclearExchange>();
			if (exchange == null || !NuclearExchangeState.IsFreeTimerPower(exchange.Mode, missile.NuclearYieldTons))
				return -1;

			var band = NuclearReleaseLadder.RungForYield(missile.NuclearYieldTons);

			// ALREADY POSTURE-SCALED by ScaledCooldownTicks; do not apply the posture again here.
			return NuclearExchangeState.CooldownTicksFor(band, exchange.ScaledCooldownTicks());
		}

		/// <summary>Whether the release gate has opened and every side holds the 1 kt band.</summary>
		public bool Released => state != null && state.Released;

		/// <summary>The highest band this player's side may fire. THE ONE NUMBER the condition layer reads.</summary>
		public int LevelFor(Player player)
		{
			return state == null ? (int)NuclearRung.Hold : state.LevelFor(SideOf(player));
		}

		/// <summary>Ticks until this player's side may fire again, at any band. 0 is ready.</summary>
		public int CooldownTicksFor(Player player)
		{
			return state == null ? 0 : state.CooldownFor(SideOf(player));
		}

		/// <summary>May this player's side fire this band right now? Rule 2.</summary>
		public bool MayFire(Player player, int band)
		{
			return state != null && state.MayFire(SideOf(player), band);
		}

		// ==== THE PER-SIDE READ SURFACE, WHICH THE LEDGER IS THE ONLY CALLER OF ==================
		// Added 2026-09-13 with the HUD ledger. Everything above answers "what may THIS PLAYER fire";
		// the ledger has to draw the OTHER side's row too, and there was no way to ask for it.
		//
		// EVERY ONE OF THESE IS A READ AND NOTHING HERE IS NEW STATE. They are projections of the
		// same NuclearExchangeState the synced ExchangeHash already covers, so a widget calling them
		// cannot move the simulation and cannot add anything to a sync report.

		/// <summary>Every side in the match, in registration order. Empty outside Escalation.</summary>
		public IReadOnlyList<int> Sides => state?.Sides ?? NoSides;

		static readonly int[] NoSides = System.Array.Empty<int>();

		/// <summary>Which side is this player on? 0 for a non-combatant or a stripped trait.</summary>
		public int SideOf(Player player)
		{
			return player != null && sideOfPlayer.TryGetValue(player, out var side) ? side : 0;
		}

		/// <summary>
		/// The side opposite this player's, or 0 when there is not exactly one of them.
		/// </summary>
		// WITH MORE THAN TWO SIDES THIS RETURNS THE FIRST OTHER ONE, matching the trait's existing
		// stance rather than inventing a second one: decision 15 says two sides, the file header says
		// a larger lobby is warned about and then escalated one-against-all, and a ledger that refused
		// to draw at all in that case would be a third behaviour for the same unenforced rule. Two
		// rows is what the design is; the third side's row is simply not drawn.
		public int OpposingSideOf(Player player)
		{
			if (state == null)
				return 0;

			var own = SideOf(player);
			foreach (var side in state.Sides)
				if (side != own)
					return side;

			return 0;
		}

		/// <summary>The highest band this SIDE may fire. Never falls.</summary>
		public int LevelForSide(int side)
		{
			return state?.LevelFor(side) ?? (int)NuclearRung.Hold;
		}

		/// <summary>
		/// <para>Ticks until this SIDE may fire again, at any band. 0 means ready now.</para>
		/// </summary>
		// ---- ASKED OF THE STATE AND NOT OF THE POWERS, WHICH IS A CHANGE FROM v1 ----------------
		// v1's RegenTicksRemainingForSide walked every SupportPowerInstance on the side and took the
		// smallest RemainingTicks, because the quantity it wanted -- how long until THIS BAND comes
		// back -- lived only on the powers. v2's quantity lives here: there is ONE cooldown per side
		// and NuclearExchangeState is counting it.
		//
		// THAT IS ALSO THE HONEST SOURCE RATHER THAN MERELY THE SIMPLER ONE. The copy written onto
		// each power can be perturbed by things that are not the exchange -- DevMode.FastCharge
		// clamps any countdown over 2500 subticks (SupportPowerManager.cs), a disabled power has its
		// timer pinned to full every tick -- and the ledger reporting a number the launch gate does
		// not use is a readout that disagrees with the rule. The state is what ReportLaunch tests.
		public int CooldownTicksForSide(int side)
		{
			return state?.CooldownFor(side) ?? 0;
		}

		/// <summary>
		/// This SIDE's level serial: bumped every time its level RISES.
		/// </summary>
		// THE ONE THING A BANNER CAN WATCH WITHOUT HOLDING A COPY OF THE LEVEL. It carries no more
		// information than the level does -- levels never fall, so every change is a rise -- and it
		// exists so the widget and this trait are watching the same edge rather than two derivations
		// of it. See NuclearExchangeState.SideState.LevelSerial.
		public int LevelSerialForSide(int side)
		{
			return state?.LevelSerialFor(side) ?? 0;
		}

		/// <summary>
		/// <para>This player's nuclear support powers, paired with the band each one sits in, in
		/// ORDINAL KEY ORDER.</para>
		///
		/// <para>ONE WALK, TWO CALLERS -- <see cref="MakeBandsReady"/> and <see cref="SetSideCooldown"/>
		/// both need "every nuclear power this player has, and its band", and both write synced state.
		/// Two hand-rolled copies of the same filter is how one of them ends up disagreeing with the
		/// other about what counts as nuclear; the filter is `MissileStrikePowerInfo` with a positive
		/// yield, stated once.</para>
		///
		/// <para>ORDINAL, AND NOT BECAUSE THE OPERATIONS CARE. Dictionary enumeration order is not
		/// something every client agrees about, and this file does not iterate an unordered collection
		/// at all -- the rule is kept whole rather than argued per call site.</para>
		/// </summary>
		static IEnumerable<(SupportPowerInstance Instance, int Band)> NuclearPowersOf(SupportPowerManager manager)
		{
			if (manager == null)
				yield break;

			foreach (var key in manager.Powers.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
			{
				var instance = manager.Powers[key];
				if (instance.Info is not MissileStrikePowerInfo missile || missile.NuclearYieldTons <= 0)
					continue;

				yield return (instance, NuclearReleaseLadder.RungForYield(missile.NuclearYieldTons));
			}
		}

		/// <summary>
		/// <para>Put EVERY nuclear power this side holds, at EVERY band, on a cooldown of
		/// <paramref name="ticks"/>.</para>
		/// </summary>
		// ==== THE SIDE IS THE UNIT, AND THAT IS THE 2026-09-15 RULING ITSELF ====
		// v1 put one BAND on one timer and left the other three loaded; before that, one POWER. The
		// user played both and ruled that a launch locks out the firing side's whole arsenal: "one
		// nuke at a time per team". This loop is that sentence.
		//
		// EVERY BAND, INCLUDING ONES ABOVE THE SIDE'S LEVEL. A power the side does not yet hold is
		// disabled, and SupportPowerInstance.Tick pins a disabled power's countdown to full on every
		// tick -- so writing to it is a no-op that is immediately overwritten, and NOT writing to it
		// would be a bet on the level never rising during the cooldown. It does rise during the
		// cooldown, routinely: being shot at while you are reloading is the normal case. MakeBandsReady
		// is what re-synchronises such a band when it is granted, and it reads the SAME remaining
		// cooldown rather than zero.
		//
		// IT INCLUDES THE POWER THAT FIRED, and that is harmless rather than merely tolerable: this
		// runs from MissileStrikePower.Activate, which SupportPowerInstance.Activate calls BEFORE its
		// own `remainingSubTicks = TotalTicks * 100`. SetCooldown has by then set TotalTicks to this
		// very value, so both writes assign the same number and the order of the two cannot matter.
		//
		// WHICH ALSO MEANS THE FIRED POWER CANNOT TEST THIS LOOP. If this method skipped the firer
		// entirely, Activate would still put that cameo on a clock, and on the SAME number -- the
		// side cooldown for a launch at band B and that power's own constructed interval are the same
		// table entry. A 2026-09-15 RED sweep proved it the hard way: a sabotage skipping every
		// player but the side's first combatant left test-nuclear-side-cooldown GREEN, because the
		// one it skipped was the firer. Any test of this loop has to read a NON-FIRING teammate.
		void SetSideCooldown(int side, int ticks)
		{
			foreach (var p in combatants)
			{
				if (SideOf(p) != side)
					continue;

				foreach (var (instance, _) in NuclearPowersOf(p.PlayerActor?.TraitOrDefault<SupportPowerManager>()))
					instance.SetCooldown(ticks);
			}
		}

		/// <summary>A display name for this side: its first combatant, in registration order.</summary>
		// FIRST COMBATANT AND NOT A TEAM NUMBER, because "TEAM 1" means nothing on screen and the
		// player names do. Built once in WorldLoaded off the same ordered walk that registers the
		// sides, so every client produces the same name for the same side.
		public string SideNameFor(int side)
		{
			return sideNames.TryGetValue(side, out var name) ? name : null;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			// Resolved HERE rather than in the constructor: nothing may reach for World.WorldActor
			// while world traits are being created, and by WorldLoaded every trait on every actor
			// exists and world.Players is final.
			escalation = w.WorldActor.TraitOrDefault<DefconEscalation>();
			doomsday = w.WorldActor.TraitOrDefault<DoomsdayStrike>();

			state = new NuclearExchangeState(Mode, ScaledCooldownTicks());

			if (Mode != DefconGameMode.Escalation)
				return;

			// world.Players order, which is world-creation order and therefore identical on every
			// client. Non-combatants (Neutral, Creeps, the world owner) and spectators are not sides:
			// they cannot fire, and escalating them would put a phantom third side into the count below.
			//
			// THE PREDICATE IS DefconWall's AND NOT A `Playable` TEST. This read `!p.Playable` and
			// dropped every map-authored combatant that did not write the line; see
			// CombatantSides for what that cost and why Playable is the wrong question.
			//
			// IT IS NOW LITERALLY DefconWall's, via CombatantSides, rather than a second copy that
			// agrees: the predicate also has to consult the PlayerReference, because the runtime
			// NonCombatant/Spectating pair is false for any client-occupied slot regardless of what
			// the map authored. Both traits got a phantom Observer side out of that.
			for (var i = 0; i < w.Players.Length; i++)
			{
				var p = w.Players[i];
				if (!CombatantSides.CountsAsASide(p))
					continue;

				var side = SideKeyFor(w, p, i);
				sideOfPlayer.Add(p, side);
				combatants.Add(p);
				state.RegisterSide(side);

				// FIRST ONE WINS, so a 2v2's row is named for whichever of the pair world.Players
				// reached first rather than flickering between them. ResolvedPlayerName is not used:
				// it is a lobby-client lookup and a scenario's map players have no client at all.
				if (!sideNames.ContainsKey(side))
					sideNames.Add(side, p.InternalName);
			}

			foreach (var side in state.Sides)
				lastSeenLevel[side] = state.LevelFor(side);

			// WHO IS IN THE MATCH, NAMED RATHER THAN COUNTED, and written on every Escalation match
			// rather than only on the warning path. A miscounted side is silent everywhere else --
			// the excluded player's cameos simply never light -- and this one line is what turned a
			// four-fault scenario verdict into a one-minute diagnosis.
			Log.Write("debug", "NUCLEAR EXCHANGE sides: " +
				string.Join(", ", combatants.Select(p => $"{p.InternalName}({SideOf(p)})")));

			// THE COOLDOWN TABLE, ONCE, WITH THE POSTURE ALREADY IN IT. A match whose exchange feels
			// wrong is almost always a posture the host did not notice they set, and the four numbers
			// are the whole economy of the mode -- so they are in the log rather than inferable from
			// it. Printed in ticks because that is the unit every other line here uses.
			Log.Write("debug", $"NUCLEAR EXCHANGE cooldowns (posture {Posture}): " +
				string.Join(", ", ScaledCooldownTicks().Select((t, i) => $"{(NuclearRung)((int)NuclearRung.Kiloton + i)}={t}")));

			// NOT ENFORCED, BY INSTRUCTION. Decision 15 says two sides; a lobby that produces more
			// gets a warning and rule 3 applied to every other side. See the file header.
			if (state.Sides.Count > 2)
				Log.Write("debug", $"NUCLEAR EXCHANGE: {state.Sides.Count} sides, not 2. " +
					"Every launch will escalate every other side. Escalation is designed for two sides.");
		}

		// A side is the lobby TEAM when there is one. With no team the player is its own side, keyed
		// on a unique NEGATIVE so it can never collide with a team number: team numbers are positive
		// and the map-player fallback is positive too. The arithmetic is
		// NuclearExchangeState.SideKeyFor; what lives here is the LOOKUP, which is the part that was
		// wrong.
		//
		// ==== `ClientInSlot`, NEVER `ClientWithIndex(p.ClientIndex)` ==============================
		// FIXED 2026-09-14 after a 2v1 scenario logged `Volga(1), Enemy(1), USA(1)` and passed on the
		// resulting 3v0. This asked `w.LobbyInfo.ClientWithIndex(p.ClientIndex)?.Team` first, under a
		// comment claiming "a scenario's map players have no lobby client at all". BOTH HALVES OF
		// THAT WERE FALSE.
		//
		// A map player does not have NO client -- it is given the HOST'S. Player.cs:191 assigns
		// `ClientIndex = world.LobbyInfo.Clients.FirstOrDefault(c => c.IsAdmin)?.Index ?? 0` to every
		// player with no client of its own, under its own `// Owned by the host (TODO: fix this)`.
		// So ClientWithIndex returns a real client for a map player, that client is the human's, and
		// every map player on the board silently inherits the human's lobby team. The
		// `PlayerReference.Team` fallback below could never be reached for a host in any team at all,
		// and the map's `Team:` -- the only statement of sides a scenario can make -- was dead text.
		//
		// AND ConquestVictoryConditions IS NOT A PRECEDENT FOR THE OLD FORM, which the old comment
		// also claimed. It reads `ClientWithIndex(self.Owner.ClientIndex).Team` and nothing else
		// (ConquestVictoryConditions.cs:105-109) -- it never consults PlayerReference.Team, so it has
		// the same hazard rather than a solution to it. CreateMapPlayers.cs:202-205 does the same
		// lookup. Neither was checked before being cited.
		//
		// ClientInSlot ASKS THE QUESTION THAT WAS MEANT: "is there a client sitting in THIS player's
		// slot". A lobby slot's key is the PlayerReference's own name (Game.cs:604-612 builds
		// `Slots[slotKey]` with `PlayerReference = slotKey` from the map's playable players, and
		// Player.InternalName is that same name), so a non-playable map player has no slot, no client
		// in it, and falls through to the map's Team as intended. A real lobby player -- human or
		// bot -- is found by its own slot and keeps its own lobby team.
		static int SideKeyFor(World w, Player p, int index)
		{
			var owningClient = w.LobbyInfo.ClientInSlot(p.InternalName);

			return NuclearExchangeState.SideKeyFor(owningClient?.Team ?? 0, p.PlayerReference?.Team ?? 0, index);
		}

		void ITick.Tick(Actor self)
		{
			if (state == null || Mode != DefconGameMode.Escalation)
				return;

			// THE RELEASE GATE IS POLLED, NOT LISTENED FOR, and that is what makes the two traits'
			// tick order irrelevant. DefconEscalation.NuclearReleaseOpen latches true and stays true,
			// so whichever of the two ticks first this reads it at worst one tick late.
			if (!released && escalation != null && escalation.NuclearReleaseOpen)
			{
				released = true;
				if (state.Release())
					Log.Write("debug", $"NUCLEAR RELEASE: all {state.Sides.Count} sides are at level " +
						$"{NuclearRung.Kiloton} (tick {self.World.WorldTick}).");
			}

			// THE STATE'S COPY OF THE COOLDOWN. Each power carries its own, set from the same value on
			// the same tick and decremented by SupportPowerInstance.Tick; this is the one the launch
			// gate and the ledger read. See CooldownTicksForSide for why the two are not one.
			var expired = state.Tick();
			if (expired != null)
				foreach (var side in expired)
					Log.Write("debug", $"NUCLEAR COOLDOWN ENDED for side {side} " +
						$"(tick {self.World.WorldTick}); every band up to {(NuclearRung)state.LevelFor(side)} is back.");

			ReconcileGrants();
			ServicePendingReady();
		}

		// Spot which sides rose since last tick and queue the readiness work for them. Driven off a
		// SNAPSHOT rather than off the launch, so the release edge and every level rise go through one
		// path and neither can be missed by a call site that forgot to.
		void ReconcileGrants()
		{
			foreach (var side in state.Sides)
			{
				var now = state.LevelFor(side);
				if (!lastSeenLevel.TryGetValue(side, out var before))
					before = (int)NuclearRung.Hold;

				if (now == before)
					continue;

				lastSeenLevel[side] = now;

				// ==== THE REQUEST COVERS THE NEWLY GRANTED BANDS ONLY ====
				// THIS IS ONE OF TWO CHANGES THAT FIXED THE 2026-09-15 GRANT DEFECT, AND IT IS NOT
				// THE ONE DOING THE WORK. Read the pair together or a later reader will revert the
				// wrong half:
				//
				//   the defect was the CONJUNCTION of a range starting at Kiloton AND
				//   MakeBandsReady reporting itself finished when ANY band was armed.
				//
				// A range starting at the bottom always contains a band the side has held since
				// release; under the old `any` predicate that band armed on the first attempt, the
				// request reported itself finished, and the band the rise had actually granted --
				// whose condition has not crossed from the player actor yet -- was never looked at
				// again. It then sat on its own constructed interval and counted down a cooldown
				// nobody asked for: a 20 kt cameo reading 06:34 on a side that had fired nothing
				// (demo-defcon-readout frame 08).
				//
				// EITHER HALF ALONE PREVENTS IT, WHICH A RED SWEEP ESTABLISHED RATHER THAN ARGUED.
				// Restoring `Kiloton` here with GrantSatisfied intact leaves test-nuclear-exchange
				// GREEN (run 260915_035210), because the new bands are the only ones in a
				// correctly-scoped range and none of them is armable on the rise tick, so `all` and
				// `any` agree. Reproducing the defect against current code takes BOTH lines.
				//
				// SO WHY KEEP THIS ONE. It is the honest range -- "the bands this rise granted" --
				// and it keeps the retry loop off bands that need nothing, which matters because a
				// never-armable power in range (TacNuke with its checkbox off, the wrong faction's
				// ender) holds the request open for the full budget. A band the side ALREADY held is
				// already carrying the side's cooldown: SetSideCooldown wrote it to every band and
				// SupportPowerInstance.Tick decrements it in step with the state, so there is nothing
				// to top up. Defence in depth on a defect that shipped once, not redundancy to trim.
				var from = before + 1;
				if (from < (int)NuclearRung.Kiloton)
					from = (int)NuclearRung.Kiloton;

				foreach (var p in combatants)
					if (SideOf(p) == side)
						pendingReady[p] = (from, info.GrantRetryTicks);
			}
		}

		// Make every granted nuclear power fire-ready, retrying for a bounded number of ticks while
		// the band condition catches up. See the file header for why this is needed at all.
		void ServicePendingReady()
		{
			if (pendingReady.Count == 0)
				return;

			// Materialised because the body rewrites the dictionary, and walked in world.Players order
			// rather than in Dictionary order so every client does identical work in identical order.
			var done = new List<Player>();
			var retry = new List<(Player Player, int FromBand, int TicksLeft)>();

			foreach (var p in combatants)
			{
				if (!pendingReady.TryGetValue(p, out var req))
					continue;

				if (MakeBandsReady(p, req.FromBand) || req.TicksLeft <= 1)
					done.Add(p);
				else
					retry.Add((p, req.FromBand, req.TicksLeft - 1));
			}

			foreach (var p in done)
				pendingReady.Remove(p);

			foreach (var r in retry)
				pendingReady[r.Player] = (r.FromBand, r.TicksLeft);
		}

		/// <summary>
		/// True once EVERY power in the granted range has been armed -- see
		/// <see cref="NuclearExchangeState.GrantSatisfied"/> for why it is not "at least one".
		/// </summary>
		bool MakeBandsReady(Player player, int fromBand)
		{
			var manager = player.PlayerActor?.TraitOrDefault<SupportPowerManager>();
			if (manager == null)
				return false;

			var side = SideOf(player);
			var toBand = state.LevelFor(side);
			if (toBand < fromBand)
				return true;

			// ==== THE QUALIFIER THAT IS RULE 2, AND IT IS EASY TO LOSE ====
			// A grant does NOT mean "ready now" -- it means "ready when the side's cooldown ends".
			// A side that fires a 1 kt and is then hit by a 20 kt is escalated WHILE ON COOLDOWN, and
			// zeroing the newly granted band's timer here would hand it a free 50 kt shot that the
			// cooldown exists to deny. Read live rather than passed in, because the retry budget means
			// this can run several ticks after the rise that queued it.
			var cooldown = state.CooldownFor(side);

			// Only consulted on the top rung; see ArmableAtTopRung. Resolved once rather than per
			// power, and deliberately NOT null-guarded into "arm everything" -- a player actor with
			// no TechTree arms no game-ender, which is the failing-closed half of ArmableBy.
			var techTree = player.PlayerActor?.TraitOrDefault<TechTree>();

			// COUNTED, NOT FLAGGED. `armed >= inRange` is the retry predicate; `armed > 0` was the
			// bug, and this is the half of the 2026-09-15 fix that is actually load-bearing -- see
			// ReconcileGrants for why the other half is not, and for the RED that needs both.
			//
			// IT IS PINNED BY UNIT TEST AND NOT BY A SCENARIO, deliberately, because no scenario in
			// the tree can discriminate it on today's arsenal: every band a single rise grants has
			// its condition land on the same player tick, so `all` and `any` agree for a correctly
			// scoped range. NuclearExchangeStateTest.AGrantIsFinishedOnlyWhenEVERYPowerInRangeIsArmed
			// goes RED on `armed > 0` in 26 ms; a scenario sweep cannot.
			//
			// WHAT IT GUARDS IS A BAND HOLDING TWO POWERS WHOSE GATES DIFFER -- the event tier puts
			// two extra game-enders at the top rung, `powers-sandbox` puts the other faction's whole
			// ladder alongside a player's own, and each carries its own lobby condition on top of the
			// shared release one. They happen to be granted together today. `all` costs one int and
			// does not depend on that staying true.
			var inRange = 0;
			var armed = 0;

			foreach (var (instance, band) in NuclearPowersOf(manager))
			{
				if (band < fromBand || band > toBand)
					continue;

				inRange++;

				// PERMITTED IS THE GATE FOR EVERY BAND BUT THE TOP, and it is what the retry budget
				// exists for: it folds in `instancesEnabled`, which is false until the band condition
				// granted by a PLAYER-actor trait has reached this power. Forcing readiness on a power
				// that is still disabled would be undone by SupportPowerInstance.Tick on the same tick.
				if (!instance.Permitted && !ArmableAtTopRung(techTree, instance, band))
					continue;

				// MakeReady does the three things a cooldown cannot: it clears prereqsAvailable (the
				// top rung's whole problem), banks a shot when the magazine is empty, and zeroes the
				// countdown. The zero is then corrected below when the side owes time.
				instance.MakeReady();

				// AND THEN THE SIDE'S REMAINING COOLDOWN, IF IT OWES ONE. Read from the RECIPIENT's
				// side -- `side` is SideOf(player) and this method only ever runs for the player it
				// was queued for -- so a side escalated by an enemy launch owes nothing and keeps the
				// zero MakeReady just wrote. Reading the FIRER's cooldown here would hand every
				// victim the aggressor's lockout, which is the opposite of the rule.
				if (cooldown > 0)
					instance.SetCooldown(cooldown);

				armed++;
			}

			return NuclearExchangeState.GrantSatisfied(inRange, armed);
		}

		/// <summary>
		/// <para>May the TOP RUNG arm this power even though <see cref="SupportPowerInstance.Permitted"/>
		/// says no? True only for a game-ender this player's own faction owns, whose sole unmet
		/// prerequisite is one <see cref="NuclearExchangeInfo.OverriddenPrerequisites"/> licenses.</para>
		///
		/// <para>THREE CONDITIONS, AND DROPPING ANY OF THEM IS A DIFFERENT BUG:
		///   * <paramref name="band"/> IS THE TOP RUNG, and it is checked twice -- once here against the
		///     ladder's own rung and once inside <see cref="NuclearGameEnders.Is"/> against the yield, which
		///     is also what keeps the 50 Mt Tsar Bomba out (decision 04). Scoping to the top rung is what
		///     leaves every lower band byte-identical: `TacNukeStrike` is a band-2 power that also declares
		///     `powers.event` (player.yaml:711), and a fix applied at every band would have started
		///     handing it to any host who ticked its checkbox on.
		///   * THE POWER'S OWN CONDITION MUST STILL BE SATISFIED. PermittedIgnoringPrerequisites folds in
		///     `instancesEnabled`, so a host who turned the arsenal off still gets no cameo and a weapon
		///     gated on `nuclear-release-unrestricted` -- never granted inside Escalation -- stays
		///     unreachable. Skipping this would also be pointless: SupportPowerInstance.Tick pins a
		///     disabled power's timer back to full on the next tick and undoes the grant.
		///   * A FACTION MUST OWN IT, AND IT MUST BE THIS ONE. Both halves live in
		///     <see cref="NuclearGameEnders.ArmableBy"/>. `MakeReady` sets prereqsAvailable wholesale, so
		///     arming on the band alone hands an America player Russia's Sarmat and undoes c8cadc8a; and
		///     a top-rung power that names NO owner is armed by nobody, which is the user's 2026-09-14
		///     "national ender only" ruling and is what keeps the 6 Mt strategic strike out of the
		///     level. Exactly one END cameo per side.</para>
		///
		/// <para>IT CANNOT LEAK INTO THE BUY TAB. SupportPowerProductionQueue filters on
		/// SupportPowerInstance.Purchasable, which is `bank.CanPurchase(Permitted)`, and in Escalation a
		/// nuclear power's bank is built DISABLED -- so the prereqsAvailable this grant sets can make a
		/// cameo READY and can never make one BUYABLE. Decision 02 is safe by construction rather than
		/// by care, and this method runs in no other mode.</para>
		/// </summary>
		bool ArmableAtTopRung(TechTree techTree, SupportPowerInstance instance, int band)
		{
			if (band != (int)NuclearRung.GameEnder)
				return false;

			return instance.PermittedIgnoringPrerequisites
				&& NuclearGameEnders.ArmableBy(techTree, instance.Info, info.OverriddenPrerequisites);
		}

		/// <summary>
		/// <para>A nuclear weapon has been RELEASED by this player -- called from
		/// MissileStrikePower.Activate, once per launch order however many warheads it delivers.</para>
		///
		/// <para>RETURNS FALSE WHEN THE LAUNCH MUST NOT HAPPEN, and the caller is required to abort on
		/// it. This used to return void and the salvo flew regardless, so a launch the state machine
		/// refused still detonated, still killed, cost its side no cooldown and escalated nobody --
		/// the one outcome this whole model has no answer for. Reachable today through
		/// DevMode.FastCharge, which clamps any countdown over 2500 subticks and so can make a cameo
		/// Ready while the side is still inside its cooldown (found by review, 2026-09-15).</para>
		///
		/// <para>TRUE FOR EVERY ORDINARY NO-OP. Outside Escalation, before release, and for a
		/// non-nuclear power there is no exchange to consult and the weapon is none of this trait's
		/// business -- Skirmish and Sandbox must fire exactly as they always have. Only the two
		/// ALARMING refusals stop a warhead.</para>
		/// </summary>
		// THE DETERMINISM ARGUMENT IS UNCHANGED BY v2. The only caller is MissileStrikePower.Activate,
		// reached exclusively through SupportPowerManager.ResolveOrder -> SupportPowerInstance.Activate.
		// SupportPowerManager is IResolveOrder, so that is the synced order-resolution path: every
		// client resolves the same order on the same tick. Everything read here is either on the order
		// (the firing player) or a compile-time constant (the weapon's declared yield), and
		// NuclearExchangeState is integer arithmetic with no shared random number in it.
		public bool ReportNuclearRelease(Player firer, int tons)
		{
			if (state == null || Mode != DefconGameMode.Escalation)
				return true;

			var firerSide = SideOf(firer);
			var levelsBefore = SnapshotLevels();
			var outcome = state.ReportLaunch(firerSide, tons);

			if (!outcome.Counted)
			{
				// LOUD FOR THE TWO REFUSALS THAT CANNOT HAPPEN, quiet for the four that are ordinary.
				// AboveLevel and OnCooldown both mean a cameo was Ready that the rules say could not
				// have been -- the condition layer and the state disagreeing, or a power's timer and
				// the side's having drifted apart -- and the symptom in a match is a warhead that
				// lands and escalates nobody, which is invisible without this line.
				if (outcome.IsAlarming)
				{
					Log.Write("debug", $"NUCLEAR LAUNCH REFUSED: {firer?.InternalName ?? "unknown"} " +
						$"(side {firerSide}) fired {tons} t, band {(NuclearRung)outcome.Band}, but " +
						$"{outcome.Refusal} -- side level {(NuclearRung)state.LevelFor(firerSide)}, " +
						$"cooldown {state.CooldownFor(firerSide)}. THE POWER SHOULD NOT HAVE BEEN READY. " +
						"NO WARHEAD WAS RELEASED.");

					// THE ONLY PATH THAT STOPS A SALVO. An alarming refusal means the rules say this
					// launch cannot exist, so letting the warhead fly anyway would be the worst of
					// both: the damage lands, the firer pays nothing, and the victim is not escalated.
					return false;
				}

				// AND THE ORDINARY REFUSALS LET IT FLY. A pre-release or non-nuclear report is not a
				// rule violation, it is a question this trait has no opinion about.
				return true;
			}

			// ONE SHOT LOCKS OUT THE WHOLE SIDE. Done on the COUNTED edge and nowhere else: a launch
			// the state machine refused must not spend a cooldown either, or a Lua scenario poking the
			// trait directly could mute an arsenal that was never fired.
			//
			// AND NOT AT ALL ON A GAME-ENDER, WHICH IS NOT THE SAME AS "A COOLDOWN OF ZERO" (found by
			// review, 2026-09-15). The state deliberately charges the top rung nothing, because the
			// match is ending -- but pushing that 0 down onto the powers would write TotalTicks = 0
			// across the side's whole arsenal, which is the shape a PURCHASED power has: no clock, no
			// countdown, everything instantly Ready, and SupportPowerChargeBar dividing by it. In a
			// TestMode session without RunInTestMode the final exchange is inert and the match does
			// NOT end, so the firer would simply be handed free shots at every band it holds.
			//
			// Leaving the arsenal untouched is the honest reading either way: firing a game-ender
			// neither costs a cooldown nor clears one, and whatever the side already owed keeps
			// running.
			if (!outcome.FinalExchange)
				SetSideCooldown(firerSide, outcome.CooldownTicks);

			Log.Write("debug", $"NUCLEAR LAUNCH: {firer?.InternalName ?? "unknown"} (side {firerSide}) " +
				$"band {outcome.Band} -> cooldown {outcome.CooldownTicks}" + EscalationSummary(levelsBefore));

			if (outcome.FinalExchange)
				BeginFinalExchange(firer);

			return true;
		}

		/// <summary>Every side's level right now, in registration order, for the launch log's before/after.</summary>
		int[] SnapshotLevels()
		{
			var levels = new int[state.Sides.Count];
			for (var i = 0; i < levels.Length; i++)
				levels[i] = state.LevelFor(state.Sides[i]);

			return levels;
		}

		/// <summary>The `enemy N level X-&gt;Y` half of the launch log. Empty when nobody moved.</summary>
		// WHO WAS ESCALATED, AND FROM WHAT, ON THE SAME LINE AS THE LAUNCH. A ratchet is only legible
		// in a log as a pair of numbers: "level 3" alone cannot be told from "level 3, again" and the
		// difference is whether the launch did anything. A shot that escalates nobody -- everyone
		// already at 5, or a one-sided scenario -- says so by this string being empty.
		string EscalationSummary(int[] levelsBefore)
		{
			var parts = new List<string>();
			for (var i = 0; i < levelsBefore.Length && i < state.Sides.Count; i++)
			{
				var side = state.Sides[i];
				var now = state.LevelFor(side);
				if (now != levelsBefore[i])
					parts.Add($"enemy {side} level {levelsBefore[i]}->{now}");
			}

			return parts.Count == 0 ? "; no side escalated" : "; " + string.Join(", ", parts);
		}

		void BeginFinalExchange(Player firer)
		{
			Log.Write("debug", $"GAME-ENDER RELEASED by {firer?.InternalName ?? "unknown"}. Final exchange.");

			// TraitOrDefault, not Trait: a scenario or map that strips DoomsdayStrike from the World
			// actor must leave this inert rather than throw. Same rule as DefconCasualtyObserver.
			doomsday?.BeginFinalExchange(firer);
		}
	}
}
