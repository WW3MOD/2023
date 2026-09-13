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
 * THE NUCLEAR EXCHANGE'S WORLD TRAIT -- the per-SIDE state of NuclearExchangeState, the two lobby
 * dropdowns that configure it, and the three things it has to reach out and touch.
 *
 * The rules themselves are in NuclearExchangeState and are verified by unit test without a World.
 * Everything here is the part that needs one: who counts as a side, when the release gate opened,
 * making a granted tier fire-ready, and beginning the final exchange.
 *
 * ==== WHY THIS IS A NEW TRAIT AND NOT MORE FIELDS ON DefconEscalation ====
 * DefconEscalation owns the ALERT LEVEL and the clock that walks it down. That is a different
 * question from what each side may fire, it is asked in Skirmish too (where this trait does
 * nothing), and putting the exchange's two dropdowns on DefconEscalationInfo would have put four
 * separate work streams into one option block. The two traits meet at exactly one place: this one
 * polls DefconEscalation.NuclearReleaseOpen, which is still where the release GATE lives.
 *
 * ==== A SIDE IS A TEAM, OR A PLAYER WITH NO TEAM ====
 * Decision 15: exactly two sides. The key is the lobby team number when there is one, and a unique
 * negative derived from the player's index in world.Players when there is not -- so a 1v1 with no
 * teams set is two sides, and a 2v2 is two sides. THIS IS NOT ENFORCED: a lobby with three or more
 * sides logs a warning once and then arms EVERY OTHER SIDE on each launch, which is the honest
 * reading of "the other side" when there is more than one of them. Building enforcement is
 * explicitly out of scope.
 *
 * ==== THE READINESS PROBLEM, AND WHAT THE CODE ACTUALLY SAYS ====
 * The ruling requires the window's Y+1 power to be "ready to fire the instant the window opens".
 * That cannot be left to the ordinary charge machinery, and the reason is in the engine rather than
 * in the design:
 *
 *   1. A power gated off by RequiresCondition DOES NOT ACCUMULATE CHARGE. SupportPowerInstance.Tick
 *      recomputes `instancesEnabled = Instances.Any(i => !i.IsTraitDisabled)` and, when it is false,
 *      assigns `remainingSubTicks = TotalTicks * 100` -- i.e. resets the timer to FULL every tick
 *      it is disabled (SupportPowerManager.cs:249-251). So a band that has been dark all match
 *      starts a complete ChargeInterval at the moment it is granted, and a three-minute window
 *      could easily lapse before the power was ever ready.
 *
 *   2. ON TODAY'S ARSENAL THERE IS NO TIMER AT ALL. Every nuclear power in the mod sets
 *      RequiresPurchase: True (nuclear-arsenal.yaml:108, :158, :199, :257, :315, :367, :458, :504,
 *      :552, :611), which forces TotalTicks to 0 (SupportPowerManager.cs:229) and makes readiness a
 *      question of whether a shot has been BOUGHT. A retaliation window that only granted permission
 *      would therefore hand the victim a shop entry and a bill, not a reply.
 *
 * So a grant does whichever of the two the power in front of it actually uses -- see
 * SupportPowerInstance.MakeReady. For a purchased power it banks ONE shot AND ONLY WHEN THE
 * BANK IS EMPTY, so repeated hits top the victim up to a single loaded warhead rather than
 * stockpiling them.
 *
 * NOTHING IS TAKEN BACK WHEN THE WINDOW LAPSES, and that is deliberate rather than an omission.
 * Rule 3 -- "no indefinite grants" -- is already enforced by the CONDITION: when the window closes,
 * GrantConditionOnNuclearRelease revokes the band, SupportPowerInstance.Permitted goes false, and
 * Ready reduces to false with it, so a banked shot at that band cannot be fired. Revoking the
 * charge as well would mean tracking which charges this trait granted and which the player paid
 * for, and getting that wrong would silently confiscate a purchase.
 *
 * ==== DETERMINISM ====
 * Integer arithmetic, no RNG, no wall-clock. Every enumeration is ordered: sides in registration
 * order (NuclearExchangeState.Sides), players in world.Players order, and a player's support powers
 * by ordinal key. The one write that leaves this trait -- MakeReady -- runs from ITick on the
 * World actor, which every client ticks identically, and from ReportLaunch, which is reached only
 * through SupportPowerManager.ResolveOrder and is therefore on the synced order-resolution path
 * (the argument DefconEscalation.ReportNuclearRelease used to carry, unchanged by the move).
 */

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("The DEFCON Escalation nuclear exchange: what each SIDE may fire, and the retaliation",
		"window a side gets for being shot at. Attach to the World actor, alongside",
		nameof(DefconEscalation) + ", which owns the release gate this trait polls.",
		"",
		"STRICT NO-OP OUTSIDE " + nameof(DefconGameMode.Escalation) + ". In Skirmish and Sandbox the",
		"released band comes from " + nameof(NuclearUnlockClock) + " instead and nothing here runs.")]
	public class NuclearExchangeInfo : TraitInfo, ILobbyOptions, IRulesetLoaded
	{
		public const string PostureOptionId = "nuclear-posture";
		public const string RetaliationWindowOptionId = "nuclear-retaliation-window";

		[Desc("Label for the nuclear posture dropdown.")]
		public readonly string PostureLabel = "Nuclear Posture";

		[Desc("Tooltip for the nuclear posture dropdown.")]
		public readonly string PostureDescription =
			"How fast warheads come back after firing. Limited War stretches every nuclear cooldown, " +
			"so an exchange is a handful of deliberate shots; Flexible Response leaves them as shipped; " +
			"Massive Retaliation shortens them, so a spiral runs to its end quickly.";

		[Desc("Default nuclear posture.")]
		public readonly NuclearPosture PostureDefault = NuclearPosture.Flexible;

		[Desc("Whether to show the nuclear posture dropdown in the lobby.")]
		public readonly bool PostureVisible = true;

		[Desc("Prevent the nuclear posture dropdown from being changed in the lobby.")]
		public readonly bool PostureLocked = false;

		[Desc("Display order for the nuclear posture dropdown.")]
		public readonly int PostureDisplayOrder = 24;

		[Desc("Label for the retaliation window dropdown.")]
		public readonly string RetaliationWindowLabel = "Retaliation window";

		[Desc("Tooltip for the retaliation window dropdown.")]
		public readonly string RetaliationWindowDescription =
			"How long a side may answer one band above what it was just hit with. The reply is ready " +
			"the instant the window opens, and the grant is gone when it closes -- there is no way to " +
			"hold one back for later.";

		[Desc("Retaliation window lengths offered, in MINUTES.")]
		public readonly int[] RetaliationWindowOptions = { 1, 2, 3, 5, 10 };

		[Desc("Default retaliation window, in MINUTES. UNTUNED PLACEHOLDER.",
			"Must be one of " + nameof(RetaliationWindowOptions) + ".")]
		public readonly int RetaliationWindowDefault = 3;

		[Desc("Whether to show the retaliation window dropdown in the lobby.")]
		public readonly bool RetaliationWindowVisible = true;

		[Desc("Prevent the retaliation window dropdown from being changed in the lobby.")]
		public readonly bool RetaliationWindowLocked = false;

		[Desc("Display order for the retaliation window dropdown.")]
		public readonly int RetaliationWindowDisplayOrder = 25;

		// ==== THE REGENERATION TABLE. UNTUNED PLACEHOLDERS, ALL FOUR. ====
		// Decision 02: in Escalation a permanent band is a FREE power on a timer, so these are the
		// whole economy of the mode -- there is no price, no queue and no bank. They rise with the
		// band because a bigger warhead should be rarer, and the shape (a minute per step) is a round
		// number nobody has played rather than anything measured.
		//
		// TICKS, AT 60 MS, WRITTEN OUT because this repo has assumed 25 ticks/second at eleven sites
		// and been wrong at every one: 1000/60 = 16.67 ticks/s, so 3:00 = 180 s = 3000 ticks. The
		// identity to check any change against is that a value in ticks divided by 1000 is its length
		// in minutes at this timestep.
		//
		// NUCLEAR POSTURE SCALES ALL FOUR (150 / 100 / 60 %), which is what makes that dropdown a live
		// lever rather than the inert one it was while every nuclear power was purchased.
		//
		// ==== A BAND IS NOT A WEAPON: EVERY BAND HOLDS TWO OR MORE POWERS ====
		// Observed 2026-09-13 from a demo capture, and it is the thing to know before tuning any of
		// these four numbers. The bands and their unlocked occupants are:
		//
		//     1 kt    2  (@B61Low 300 t, @Ru9M729 1000 t)
		//     20 kt   3  (@RuIskander, @B61Mid, @TacNuke)
		//     50 kt   2  (@B61Max, @RuKinzhalN)
		//     100 kt  2  (@W76, @RuKalibr)
		//
		// and nuclear-arsenal.yaml declares NO faction prerequisite for any of its ten entries, so
		// both sides hold both ladders. A side can therefore fire a band ONCE PER POWER IN IT before
		// anything here starts counting: two 1 kt warheads back to back, then a 900-tick wait.
		//
		// THAT IS NOT A BUG IN THE TIMER AND IS NOT ONE IN THE READOUT -- the readout reports the
		// band as available for exactly as long as the side has a loaded warhead in it, which is the
		// truth. It IS a fact about what these numbers mean: KilotonRegenTicks is the interval
		// between EXHAUSTING the band and getting it back, not the interval between shots. Whoever
		// tunes them first should decide whether that is the intended economy.

		[Desc("Ticks the 1 kt band takes to come back after firing, in Escalation. UNTUNED PLACEHOLDER.",
			"3000 ticks = 180 s = 3:00 at the default 60 ms timestep (16.67 ticks/s, NOT 25).")]
		public readonly int KilotonRegenTicks = 3000;

		[Desc("Ticks the 20 kt band takes to come back after firing, in Escalation. UNTUNED PLACEHOLDER.",
			"4000 ticks = 240 s = 4:00 at the default 60 ms timestep.")]
		public readonly int TwentyKilotonRegenTicks = 4000;

		[Desc("Ticks the 50 kt band takes to come back after firing, in Escalation. UNTUNED PLACEHOLDER.",
			"5000 ticks = 300 s = 5:00 at the default 60 ms timestep.")]
		public readonly int FiftyKilotonRegenTicks = 5000;

		[Desc("Ticks the 100 kt band takes to come back after firing, in Escalation. UNTUNED PLACEHOLDER.",
			"6000 ticks = 360 s = 6:00 at the default 60 ms timestep.",
			"",
			"ALSO THE GAME-ENDER BAND'S value -- see " + nameof(NuclearExchangeState.RegenTicksFor) + ".",
			"That band is only ever a retaliation window grant and firing one ends the match, so its",
			"timer is a post-fire lockout the match never outlives.")]
		public readonly int HundredKilotonRegenTicks = 6000;

		[Desc("Ticks a grant is retried for while the band condition it needs has not reached the",
			"support power yet. NOT a gameplay duration: the condition is granted by a PLAYER-actor",
			"trait and this runs on the WORLD actor, so a grant issued here can land one or two ticks",
			"before the power it is aimed at is enabled. 30 ticks = 1.8 s at the 60 ms timestep, which",
			"is two orders of magnitude more slack than the one-or-two-tick case needs.")]
		public readonly int GrantRetryTicks = 30;

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (!((IList<int>)RetaliationWindowOptions).Contains(RetaliationWindowDefault))
				throw new YamlException($"{nameof(RetaliationWindowDefault)} must be one of {nameof(RetaliationWindowOptions)}.");

			// A window of 0 minutes would open and lapse on the same tick, which is a grant nobody
			// could ever use -- so it is refused here rather than allowed to read as "no retaliation".
			foreach (var minutes in RetaliationWindowOptions)
				if (minutes <= 0)
					throw new YamlException($"{nameof(RetaliationWindowOptions)} must all be positive minute counts.");

			if (GrantRetryTicks < 0)
				throw new YamlException($"{nameof(GrantRetryTicks)} must be 0 or positive.");

			// POSITIVE, NOT MERELY NON-NEGATIVE. A regeneration of 0 ticks is a band that is ready
			// again on the tick after it fired, which is not a fast economy but no economy at all --
			// and it would silently undo the one-shot rule on a retaliation window.
			foreach (var (name, ticks) in new[]
			{
				(nameof(KilotonRegenTicks), KilotonRegenTicks),
				(nameof(TwentyKilotonRegenTicks), TwentyKilotonRegenTicks),
				(nameof(FiftyKilotonRegenTicks), FiftyKilotonRegenTicks),
				(nameof(HundredKilotonRegenTicks), HundredKilotonRegenTicks),
			})
				if (ticks <= 0)
					throw new YamlException($"{name} must be a positive tick count: in DEFCON Escalation " +
						"a band is a free power on a regeneration timer, and 0 would make it fire every tick.");
		}

		/// <summary>The window lengths offered, as wire keys mapped to their lobby labels.</summary>
		// Built from the field rather than written out, for NuclearUnlockClockInfo.IntervalValues's
		// reason: a value the option does not define throws KeyNotFoundException on the next CLIENT
		// JOIN (LobbySettingsNotification.cs:39 indexes Values unchecked), so the host sees a working
		// lobby and the next player to connect is thrown out.
		public IReadOnlyDictionary<string, string> RetaliationWindowValues()
		{
			var values = new Dictionary<string, string>();
			foreach (var minutes in RetaliationWindowOptions)
				values[minutes.ToString(CultureInfo.InvariantCulture)] = minutes == 1 ? "1 minute" : $"{minutes} minutes";

			return values;
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

			yield return new LobbyOption(RetaliationWindowOptionId, RetaliationWindowLabel, RetaliationWindowDescription,
				RetaliationWindowVisible, RetaliationWindowDisplayOrder, RetaliationWindowValues(),
				RetaliationWindowDefault.ToString(CultureInfo.InvariantCulture), RetaliationWindowLocked);
		}

		/// <summary>The four regeneration intervals in ascending band order, unscaled.</summary>
		// Built here rather than at each call site so the ORDER is stated once: index 0 is
		// NuclearRung.Kiloton, which is what NuclearExchangeState.RegenTicksFor indexes against.
		public IReadOnlyList<int> RegenTicks()
		{
			return new[] { KilotonRegenTicks, TwentyKilotonRegenTicks, FiftyKilotonRegenTicks, HundredKilotonRegenTicks };
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
		/// <see cref="EscalationRegenTicks"/>), and a player actor can be built before WorldLoaded
		/// runs. Forwarding would have answered "Skirmish" there and silently left every nuclear power
		/// purchased in Escalation — the whole feature off, with nothing to see.</para>
		///
		/// <para>The default comes from <see cref="DefconEscalationInfo.ModeDefault"/> off the World
		/// actor's own ActorInfo, so the two traits cannot disagree about it. Same idiom, and the same
		/// creation-order reason, as <see cref="NuclearUnlockClock"/>'s constructor one file away.</para>
		/// </summary>
		public readonly DefconGameMode Mode;

		/// <summary>The posture the host picked. Scales the regeneration timers; see <see cref="NuclearPostureScale"/>.</summary>
		public readonly NuclearPosture Posture;

		/// <summary>The retaliation window, in ticks, already converted from the lobby's minutes.</summary>
		public readonly int RetaliationWindowTicks;

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

		// What each side looked like last tick, so a RISE in the permanent level and a RESTART of the
		// window can both be spotted without either trait having to call the other.
		readonly Dictionary<int, (int Permanent, int WindowSerial)> lastSeen = new Dictionary<int, (int, int)>();

		// Outstanding "make this player's newly granted bands fire-ready" requests. The band is the
		// LOWEST newly granted one; everything from there up to the side's released level is topped
		// up in the same pass. See the file header for why a retry budget is needed at all.
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
						h = (h * 31) + state.PermanentLevelFor(side);
						h = (h * 31) + state.WindowLevelFor(side);
						h = (h * 31) + state.WindowTicksRemainingFor(side);
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

			var raw = settings.OptionOrDefault(NuclearExchangeInfo.RetaliationWindowOptionId,
				info.RetaliationWindowDefault.ToString(CultureInfo.InvariantCulture));
			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
				minutes = info.RetaliationWindowDefault;

			// world.Timestep read ONCE, in the constructor, exactly as NuclearUnlockClock and
			// TimeLimitManager do: it is the same value on every client at this moment, and the debug
			// speed button mutates it later. TicksForMinutes multiplies before dividing, so three
			// minutes at the 60 ms timestep is exactly 3000 ticks and not 2880.
			RetaliationWindowTicks = NuclearUnlockSchedule.TicksForMinutes(minutes, world.Timestep);
		}

		/// <summary>
		/// <para>How long this power takes to come back in DEFCON Escalation, where it is FREE and
		/// timer-charged rather than bought. Returns -1 when the ordinary purchase economy applies,
		/// which is every power outside Escalation and every non-nuclear power inside it.</para>
		///
		/// <para>THE ONE ENTRY POINT <see cref="SupportPowerInstance"/>'s constructor uses, and it
		/// answers BOTH questions that constructor has to ask — "is this bought?" is `&lt; 0`, and
		/// "what is its interval?" is the value. Splitting them into two calls would let the two
		/// answers drift apart, which is exactly the state that produces a power with no timer AND no
		/// magazine: permanently unusable, with nothing logged.</para>
		///
		/// <para>IT IS THE IDENTITY OUTSIDE ESCALATION. No World, no trait, a non-nuclear power, or any
		/// other mode all return -1, which is what keeps Skirmish and Sandbox — and every other mod —
		/// byte-identical. Skirmish is the shipped default and every nuclear scenario in the tree buys
		/// its shot; see NuclearExchangeState.IsFreeTimerPower.</para>
		/// </summary>
		public static int EscalationRegenTicks(World world, SupportPowerInfo powerInfo)
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
			var ticks = NuclearExchangeState.RegenTicksFor(band, exchange.info.RegenTicks());

			// THE POSTURE BITES HERE AND NOWHERE ELSE, which is what turns that dropdown from an inert
			// label into the mode's one economic lever: Limited stretches every band, Massive shortens
			// every band, Flexible is the identity.
			return NuclearPostureScale.Apply(ticks, exchange.Posture);
		}

		/// <summary>Whether the release gate has opened and both sides hold the 1 kt band.</summary>
		public bool Released => state != null && state.Released;

		/// <summary>The highest band this player's side may fire freely, on cooldown.</summary>
		public int PermanentLevelFor(Player player)
		{
			return state == null ? (int)NuclearRung.Hold : state.PermanentLevelFor(SideOf(player));
		}

		/// <summary>This player's side's retaliation band, or Hold when no window is open.</summary>
		public int WindowLevelFor(Player player)
		{
			return state == null ? (int)NuclearRung.Hold : state.WindowLevelFor(SideOf(player));
		}

		/// <summary>Ticks of retaliation window left for this player's side. 0 is shut.</summary>
		public int WindowTicksRemainingFor(Player player)
		{
			return state == null ? 0 : state.WindowTicksRemainingFor(SideOf(player));
		}

		/// <summary>
		/// Everything this player's side may fire right now. THE ONE NUMBER the condition layer reads
		/// -- see <see cref="GrantConditionOnNuclearRelease"/>.
		/// </summary>
		public int ReleasedLevelFor(Player player)
		{
			return state == null ? (int)NuclearRung.Hold : state.ReleasedLevelFor(SideOf(player));
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
		// a larger lobby is warned about and then armed one-against-all, and a ledger that refused to
		// draw at all in that case would be a third behaviour for the same unenforced rule. Two rows
		// is what the design is; the third side's row is simply not drawn.
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

		/// <summary>The highest band this SIDE may fire freely, on cooldown.</summary>
		public int PermanentLevelForSide(int side)
		{
			return state?.PermanentLevelFor(side) ?? (int)NuclearRung.Hold;
		}

		/// <summary>This SIDE's retaliation band, or Hold when no window is open.</summary>
		public int WindowLevelForSide(int side)
		{
			return state?.WindowLevelFor(side) ?? (int)NuclearRung.Hold;
		}

		/// <summary>Ticks of retaliation window left for this SIDE. 0 is shut.</summary>
		public int WindowTicksRemainingForSide(int side)
		{
			return state?.WindowTicksRemainingFor(side) ?? 0;
		}

		/// <summary>
		/// This SIDE's window serial: bumped every time its window is opened OR restarted.
		/// </summary>
		// THE ONE THING A BANNER CAN WATCH. WindowTicksRemaining cannot distinguish "restarted on the
		// same band" from "not yet ticked", so a widget watching it would miss the second hit of a
		// pair -- which is precisely the moment the player most needs telling about. See
		// NuclearExchangeState.SideState.WindowSerial.
		public int WindowSerialForSide(int side)
		{
			return state?.For(side)?.WindowSerial ?? 0;
		}

		/// <summary>
		/// <para>Ticks until this SIDE's <paramref name="band"/> is fireable again, 0 when it is ready
		/// now, and -1 when the side has no power at that band to ask about.</para>
		/// </summary>
		// ---- WHY THIS IS ASKED OF THE POWERS AND NOT OF NuclearExchangeState --------------------
		// The state knows what a side is PERMITTED to fire. It does not know, and deliberately does
		// not count, how long until the warhead is back: `ce397d9f` put regeneration on the support
		// power's own ChargeInterval through EscalationRegenTicks, and NuclearExchangeState's header
		// says outright that nothing there counts it. So the only honest source for a countdown is
		// SupportPowerInstance.RemainingTicks, which is where the engine is actually counting.
		//
		// ---- THE SMALLEST REMAINING, NOT THE FIRST FOUND -----------------------------------------
		// A side is a TEAM. Two players on one side each hold their own faction's warhead at a band,
		// and what the side can do is whatever comes back SOONEST -- so the minimum is the answer,
		// and the first entry in dictionary order is not.
		//
		// AND IT IS ALSO THE ANSWER FOR ONE PLAYER, which is not obvious and was read as a bug on
		// 2026-09-13. EVERY BAND HOLDS TWO OR MORE POWERS and neither ladder is faction-locked (see
		// the note on the regeneration fields above), so ONE player alone has two warheads in the
		// 1 kt band. After firing one of them this correctly returns 0 and the ledger correctly
		// leaves the box lit: the side really can fire that band again, this tick. A reading that
		// took the power that was just fired, or the maximum, would draw a countdown over a band the
		// player is holding a loaded warhead in -- which is the readout lying in the direction that
		// loses matches.
		//
		// ---- -1 IS NOT 0 -------------------------------------------------------------------------
		// "No power at this band" and "ready right now" are different facts and the ledger draws them
		// differently: a band with no power behind it has no countdown to show, and a box captioned
		// 0:00 forever would be a readout inventing one. Callers test for negative.
		//
		// ---- READ ONLY, FROM A WIDGET, AND THAT IS SAFE ------------------------------------------
		// RemainingTicks is synced simulation state already covered by the support power machinery's
		// own hashes. Nothing here writes, and the ledger reading the OTHER side's number is the
		// design rather than a leak: decision 01's restraint case only works if each side can count
		// what the other holds, which is why both rows are on screen for both players.
		public int RegenTicksRemainingForSide(int side, int band)
		{
			if (state == null || Mode != DefconGameMode.Escalation || band <= (int)NuclearRung.Hold)
				return -1;

			var best = -1;

			foreach (var p in combatants)
			{
				if (SideOf(p) != side)
					continue;

				var manager = p.PlayerActor?.TraitOrDefault<SupportPowerManager>();
				if (manager == null)
					continue;

				// Ordinal key order for the same reason MakeBandsReady walks it that way: Dictionary
				// order is not a guarantee, and two clients disagreeing about which of two equal
				// timers they looked at would be a readout that flickered between machines.
				foreach (var key in manager.Powers.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
				{
					var instance = manager.Powers[key];
					if (!(instance.Info is MissileStrikePowerInfo missile) || missile.NuclearYieldTons <= 0)
						continue;

					if (NuclearReleaseLadder.RungForYield(missile.NuclearYieldTons) != band)
						continue;

					// PERMITTED, NOT Ready. A power whose band condition is ungranted is not this
					// side's to count at all -- its timer is pinned to full every tick it is disabled
					// (SupportPowerManager.cs:249-251), so counting it would draw a countdown that
					// never moves under a box the side does not even hold.
					if (!instance.Permitted)
						continue;

					var remaining = instance.RemainingTicks;
					if (best < 0 || remaining < best)
						best = remaining;
				}
			}

			return best;
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

			state = new NuclearExchangeState(Mode, RetaliationWindowTicks);

			if (Mode != DefconGameMode.Escalation)
				return;

			// world.Players order, which is world-creation order and therefore identical on every
			// client. Non-combatants (Neutral, Creeps, the world owner) and spectators are not sides:
			// they cannot fire, and arming them would put a phantom third side into the count below.
			//
			// THE PREDICATE IS DefconWall's AND NOT A `Playable` TEST. This read `!p.Playable` and
			// dropped every map-authored combatant that did not write the line; see
			// NuclearExchangeState.CountsAsASide for what that cost and why Playable is the wrong
			// question.
			for (var i = 0; i < w.Players.Length; i++)
			{
				var p = w.Players[i];
				if (!NuclearExchangeState.CountsAsASide(p.NonCombatant, p.Spectating))
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
				lastSeen[side] = (state.PermanentLevelFor(side), 0);

			// WHO IS IN THE MATCH, NAMED RATHER THAN COUNTED, and written on every Escalation match
			// rather than only on the warning path. A miscounted side is silent everywhere else --
			// the excluded player's cameos simply never light -- and this one line is what turned a
			// four-fault scenario verdict into a one-minute diagnosis.
			Log.Write("debug", "NUCLEAR EXCHANGE sides: " +
				string.Join(", ", combatants.Select(p => $"{p.InternalName}({SideOf(p)})")));

			// NOT ENFORCED, BY INSTRUCTION. Decision 15 says two sides; a lobby that produces more
			// gets a warning and rule 2 applied to every other side. See the file header.
			if (state.Sides.Count > 2)
				Log.Write("debug", $"NUCLEAR EXCHANGE: {state.Sides.Count} sides, not 2. " +
					"Every launch will arm every other side. Escalation is designed for two sides.");
		}

		// A side is the lobby TEAM when there is one. With no team the player is its own side, keyed
		// on a unique NEGATIVE so it can never collide with a team number: team numbers are positive
		// and the map-player fallback below is positive too.
		static int SideKeyFor(World w, Player p, int index)
		{
			var team = w.LobbyInfo.ClientWithIndex(p.ClientIndex)?.Team ?? 0;

			// A scenario's map players have no lobby client at all, so the team comes off the map's
			// PlayerReference instead. ConquestVictoryConditions reads the same two sources.
			if (team <= 0)
				team = p.PlayerReference?.Team ?? 0;

			return team > 0 ? team : -(index + 1);
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
					Log.Write("debug", $"NUCLEAR RELEASE: all {state.Sides.Count} sides hold the " +
						$"{NuclearRung.Kiloton} band permanently (tick {self.World.WorldTick}).");
			}

			var lapsed = state.TickWindows();
			if (lapsed != null)
				foreach (var side in lapsed)
					Log.Write("debug", $"RETALIATION WINDOW LAPSED for side {side} " +
						$"(tick {self.World.WorldTick}); back to rung {state.PermanentLevelFor(side)}.");

			ReconcileGrants();
			ServicePendingReady();
		}

		// Spot what moved since last tick and queue the readiness work for it. Driven off a SNAPSHOT
		// rather than off the launch, so the release edge, a permanent rise and a window restart all
		// go through one path and none of them can be missed by a call site that forgot to.
		void ReconcileGrants()
		{
			foreach (var side in state.Sides)
			{
				var now = (Permanent: state.PermanentLevelFor(side), Serial: state.For(side).WindowSerial);
				if (!lastSeen.TryGetValue(side, out var before))
					before = ((int)NuclearRung.Hold, 0);

				if (now.Permanent == before.Permanent && now.Serial == before.WindowSerial)
					continue;

				lastSeen[side] = (now.Permanent, now.Serial);

				// The LOWEST newly granted band. A permanent rise from 1 to 3 grants 2 and 3; a window
				// restart grants its own band whether or not that band is new, because the whole point
				// of a restart is that the reply is available again.
				var from = now.Permanent > before.Permanent
					? before.Permanent + 1
					: state.WindowLevelFor(side);

				if (now.Serial != before.WindowSerial)
				{
					var windowBand = state.WindowLevelFor(side);
					if (windowBand > (int)NuclearRung.Hold && windowBand < from)
						from = windowBand;
				}

				if (from <= (int)NuclearRung.Hold)
					continue;

				foreach (var p in combatants)
					if (SideOf(p) == side)
						pendingReady[p] = (from, info.GrantRetryTicks);
			}
		}

		// Make every newly granted nuclear power fire-ready, retrying for a bounded number of ticks
		// while the band condition catches up. See the file header for why this is needed at all.
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

		/// <summary>True once at least one power in the granted range was made ready.</summary>
		bool MakeBandsReady(Player player, int fromBand)
		{
			var manager = player.PlayerActor?.TraitOrDefault<SupportPowerManager>();
			if (manager == null)
				return false;

			var toBand = ReleasedLevelFor(player);
			if (toBand < fromBand)
				return true;

			var any = false;

			// Ordinal key order, so the sequence is the same on every client. Dictionary order is not
			// a guarantee, and these calls write synced state.
			foreach (var key in manager.Powers.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
			{
				var instance = manager.Powers[key];
				if (!(instance.Info is MissileStrikePowerInfo missile) || missile.NuclearYieldTons <= 0)
					continue;

				var band = NuclearReleaseLadder.RungForYield(missile.NuclearYieldTons);
				if (band < fromBand || band > toBand)
					continue;

				// PERMITTED IS THE GATE, and it is what the retry budget exists for: it folds in
				// `instancesEnabled`, which is false until the band condition granted by a PLAYER-actor
				// trait has reached this power. Forcing readiness on a power that is still disabled
				// would be undone by SupportPowerInstance.Tick on the same tick.
				if (!instance.Permitted)
					continue;

				// MakeReady is wt/deadhand-window's, and this branch's near-identical MakeFireReady
				// was deleted at that merge rather than kept beside it. Theirs is the superset: it
				// also clears prereqsAvailable, which is a no-op HERE because the Permitted test
				// above already folds that in, and is what its own caller needs. It returns void
				// where mine returned a bool that was unconditionally true and therefore told a
				// caller nothing.
				instance.MakeReady();
				any = true;
			}

			return any;
		}

		/// <summary>
		/// A nuclear weapon has been RELEASED by this player -- called from MissileStrikePower.Activate,
		/// once per launch order however many warheads it delivers.
		/// </summary>
		// THE DETERMINISM ARGUMENT IS UNCHANGED BY THE MOVE off DefconEscalation. The only caller is
		// MissileStrikePower.Activate, reached exclusively through SupportPowerManager.ResolveOrder ->
		// SupportPowerInstance.Activate (SupportPowerManager.cs:293-321). SupportPowerManager is
		// IResolveOrder, so that is the synced order-resolution path: every client resolves the same
		// order on the same tick. Everything read here is either on the order (the firing player) or a
		// compile-time constant (the weapon's declared yield), and NuclearExchangeState is integer
		// arithmetic with no shared random number in it.
		public void ReportNuclearRelease(Player firer, int tons)
		{
			if (state == null || Mode != DefconGameMode.Escalation)
				return;

			var outcome = state.ReportLaunch(SideOf(firer), tons);
			if (!outcome.Counted)
				return;

			Log.Write("debug", $"NUCLEAR LAUNCH: {firer?.InternalName ?? "unknown"} (side {SideOf(firer)}) " +
				$"released {tons} t, band {(NuclearRung)outcome.Band}. Every other side is armed.");

			if (outcome.FinalExchange)
				BeginFinalExchange(firer);
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
