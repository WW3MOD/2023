#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * THE NUCLEAR MOMENTS -- the gate opening, being ARMED, and a grant running out unused.
 *
 * The ledger in DefconReadoutWidget carries the STATE: what both sides hold, right now, countable.
 * This carries the CHANGES, and the two are not the same job. A retaliation window is three minutes
 * that opened without the player doing anything, on a tick they were looking at a unit somewhere
 * else on the map; by the time they next glance at a 341-pixel panel in the corner, a third of it
 * is gone. The ruling's strategic claim -- that the winner's correct play is restraint and the
 * loser's is to reply -- is a claim about a DECISION, and a decision nobody knows they have been
 * handed is not one.
 *
 * ==== FOUR EDGES, AND WHAT EACH ONE GETS ====
 *   RELEASE OPENS        banner + speech. Both sides, simultaneously, and it is the first time
 *                        anything nuclear is possible at all.
 *   YOUR SIDE IS ARMED   banner + speech. The loud one. A grant with a clock on it.
 *   THEIR SIDE IS ARMED  a transient line and NO sound. It is the consequence of a shot the player
 *                        just fired deliberately, so they already know they did something; what
 *                        they do not know is what it bought the other side. A second alarm on your
 *                        own action would train the player to ignore the first one.
 *   YOUR GRANT LAPSES    a transient line. Nothing was lost that the player had spent, but the
 *                        ledger box going dark needs a reason attached to it or it reads as a bug.
 *
 * ==== IT WATCHES, IT IS NOT TOLD -- THE SAME RULE AS THE OTHER TWO BANNERS ====
 * NuclearExchange moves inside synced simulation code on every client. Everything here -- whether
 * to draw, what clock to freeze into the line, whether a sound has already played -- is
 * client-local render state read back out of the trait each frame. There is no route from a
 * rendering decision into the simulation, and nothing here to get wrong in a sync report.
 *
 * THE SERIAL IS WHAT MAKES THAT POSSIBLE. WindowTicksRemaining cannot distinguish "restarted on the
 * same band" from "not yet ticked", so a widget watching the ticks would miss the SECOND hit of a
 * pair -- which is the moment the player most needs telling about, because it is the one that says
 * the other side is not backing off. NuclearExchangeState.SideState.WindowSerial is bumped on every
 * open AND every restart precisely so a once-per-grant consumer can exist; this is that consumer.
 *
 * ==== THE FIRST FRAME NEVER ANNOUNCES ANYTHING ====
 * Same guard, and same reason, as DefconTransitionBannerWidget's `lastLevel == NoLevel`: a match
 * that opens already released -- a scenario starting at DEFCON 1 with the gate compressed to ten
 * ticks, which is exactly what test-nuclear-exchange does -- has not TRANSITIONED into anything,
 * and an alarm for a change that never happened is worse than silence. `primed` is false until the
 * first Tick has taken a snapshot, and nothing fires on that frame.
 *
 * ==== THE HOLD IS WORLD TICKS, CONVERTED WITH GameSpeed.Timestep ====
 * PITFALL: not world.Timestep. That one is mutated at runtime by the debug speed button and by
 * test-mode speed multipliers, so a four-second hold measured with it would be four seconds at
 * whatever speed the game happened to be running. Same rule, same reason, as the other two banners
 * and TimeLimitManager's countdown label. See conventions.md.
 */

using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class NuclearArmedBannerWidget : Widget
	{
		public readonly string TitleFont = "BigBold";
		public readonly string LineFont = "Regular";

		// ---- WHAT THE MOMENTS SOUND LIKE --------------------------------------------------------
		// UNLIKE THE DEFCON TRANSITIONS, BOTH OF THESE ARE REAL RECORDED SPEECH. The mod's Speech
		// pool is Red Alert's lines and two of them say exactly the right thing:
		// AbombAvailable (aavail1, notifications.yaml:3) for the gate opening, and AbombReady
		// (aready1, :6) for a grant landing. No invented asset, no placeholder.
		//
		// NOT LINTED -- CheckNotifications does not walk widget fields. DefconAlert checks the pool
		// at runtime so a typo is a log line rather than a crash mid-banner.
		public readonly string NotificationPool = DefconAlert.SpeechPool;

		/// <summary>Notification played when the release gate opens for both sides.</summary>
		public readonly string ReleaseNotification = "AbombAvailable";

		/// <summary>Notification played when the viewer's own side gains a retaliation grant.</summary>
		public readonly string ArmedNotification = "AbombReady";

		/// <summary>Fluent key of the system line shown when the release gate opens.</summary>
		public readonly string ReleaseTextNotification = "notification-nuclear-release";

		/// <summary>Fluent key of the line shown when the OTHER side is armed by your launch.</summary>
		public readonly string EnemyArmedTextNotification = "notification-nuclear-enemy-armed";

		/// <summary>Fluent key of the line shown when your own grant lapses unused.</summary>
		public readonly string GrantExpiredTextNotification = "notification-nuclear-grant-expired";

		readonly World world;
		readonly SpriteFont titleFont, lineFont;

		DefconEscalation escalation;
		NuclearExchange exchange;
		bool initialised;

		// ---- THE SNAPSHOT, WHICH IS THE WHOLE MECHANISM -----------------------------------------
		// False until the first Tick has read the world. Nothing fires on that frame; see the header.
		bool primed;

		bool lastReleaseOpen;
		int lastOwnSerial, lastEnemySerial;

		// The band the viewer's own window was granting last frame, and 0 when it was shut. Kept
		// because NuclearExchangeState.TickWindows ZEROES WindowLevel on the tick the window lapses
		// -- so by the time this widget can see that it lapsed, the trait can no longer say what it
		// had been granting. "20 kt grant expired" needs the band, so it has to be remembered here.
		int lastOwnWindowBand;

		// What is on screen, and since when. NoBanner is not a level or a band -- it is this widget's
		// own "nothing".
		const int NoBanner = 0;
		const int BannerRelease = 1;
		const int BannerArmed = 2;

		int shownBanner = NoBanner;
		int shownAtTick;
		string shownLine;

		[ObjectCreator.UseCtor]
		public NuclearArmedBannerWidget(World world)
		{
			this.world = world;

			titleFont = Game.Renderer.Fonts[TitleFont];
			lineFont = Game.Renderer.Fonts[LineFont];
		}

		// WHOSE SIDE IS "YOURS". RenderPlayer first, so an observer or a replay following a player
		// gets that player's alerts rather than none. READ ONLY -- both are per-client and neither
		// may reach the simulation; nothing below writes anything.
		Player Viewer => world.RenderPlayer ?? world.LocalPlayer;

		public override void Tick()
		{
			if (!initialised)
			{
				initialised = true;
				escalation = world.WorldActor.TraitOrDefault<DefconEscalation>();
				exchange = world.WorldActor.TraitOrDefault<NuclearExchange>();
			}

			// A map is free not to carry either trait, and outside Escalation both are strict no-ops
			// whose state never moves -- so there is no edge to find and nothing to announce.
			if (exchange == null || escalation == null || escalation.Mode != DefconGameMode.Escalation)
				return;

			var viewer = Viewer;
			var ownSide = exchange.SideOf(viewer);
			var enemySide = exchange.OpposingSideOf(viewer);

			var releaseOpen = escalation.NuclearReleaseOpen;
			var ownSerial = exchange.WindowSerialForSide(ownSide);
			var enemySerial = exchange.WindowSerialForSide(enemySide);
			var ownWindowTicks = exchange.WindowTicksRemainingForSide(ownSide);
			var ownWindowBand = ownWindowTicks > 0 ? exchange.WindowLevelForSide(ownSide) : (int)NuclearRung.Hold;

			if (!primed)
			{
				primed = true;
				lastReleaseOpen = releaseOpen;
				lastOwnSerial = ownSerial;
				lastEnemySerial = enemySerial;
				lastOwnWindowBand = ownWindowBand;
				return;
			}

			var timestep = world.GameSpeed.Timestep;

			// ---- THE GATE OPENS ------------------------------------------------------------------
			if (releaseOpen && !lastReleaseOpen)
			{
				shownBanner = BannerRelease;
				shownAtTick = world.WorldTick;
				shownLine = DefconReadoutModel.NuclearReleaseBannerLine;

				DefconAlert.Play(world, NotificationPool, ReleaseNotification);
				DefconAlert.Line(world, ReleaseTextNotification);
			}

			// ---- YOUR SIDE IS ARMED --------------------------------------------------------------
			// The serial, not the ticks: a second hit at the same band restarts the window without
			// changing a single other number, and that restart is news.
			if (ownSerial != lastOwnSerial && ownWindowTicks > 0)
			{
				shownBanner = BannerArmed;
				shownAtTick = world.WorldTick;

				// THE CLOCK IS FROZEN AT THE MOMENT OF ARMING rather than counted down live, and the
				// banner is the one place that is right: it is telling the player HOW LONG THEY HAVE,
				// which is a property of the grant. The live countdown is the ledger's job and the
				// ledger is on screen the whole time. A banner whose number moved would also make the
				// centred line reflow under itself four seconds running.
				shownLine = DefconReadoutModel.ArmedBannerLine(ownWindowBand,
					WidgetUtils.FormatTime(ownWindowTicks, false, timestep));

				DefconAlert.Play(world, NotificationPool, ArmedNotification);
			}

			// ---- THEIR SIDE IS ARMED, BY SOMETHING YOU DID ---------------------------------------
			// No banner and no sound. See the header: the player chose to fire, so the event is not a
			// surprise -- what they do not know is the size of the reply it just bought.
			if (enemySerial != lastEnemySerial && enemySide != ownSide)
			{
				var band = exchange.WindowLevelForSide(enemySide);
				var ticks = exchange.WindowTicksRemainingForSide(enemySide);
				// NAME/VALUE PAIRS, NOT POSITIONAL ARGUMENTS. FluentBundle.TryGetMessage walks `args`
				// two at a time and THROWS on an odd count (FluentBundle.cs:128-141), so a line passed
				// bare values would crash at the moment it was shown rather than render oddly.
				if (ticks > 0 && band > (int)NuclearRung.Hold)
					DefconAlert.Line(world, EnemyArmedTextNotification,
						"yield", DefconReadoutModel.RungLabel(band),
						"time", WidgetUtils.FormatTime(ticks, false, timestep));
			}

			// ---- YOUR GRANT LAPSED UNUSED --------------------------------------------------------
			// Distinguished from a grant being REPLACED by the serial: a hit that restarts the window
			// also runs the branch above, and the band never passes through zero, so this cannot
			// double-report. Read off lastOwnWindowBand because the trait has already forgotten it.
			if (ownWindowTicks <= 0 && lastOwnWindowBand > (int)NuclearRung.Hold && ownSerial == lastOwnSerial)
				DefconAlert.Line(world, GrantExpiredTextNotification,
					"yield", DefconReadoutModel.RungLabel(lastOwnWindowBand));

			lastReleaseOpen = releaseOpen;
			lastOwnSerial = ownSerial;
			lastEnemySerial = enemySerial;
			lastOwnWindowBand = ownWindowBand;
		}

		public override void Draw()
		{
			if (shownBanner == NoBanner || !IsVisible())
				return;

			var hold = DefconReadoutModel.BannerHoldTicks(world.GameSpeed.Timestep);
			if (world.WorldTick - shownAtTick >= hold)
			{
				shownBanner = NoBanner;
				return;
			}

			// THE NUCLEAR ACCENT AND NOT A DEFCON HUE. The nuclear block is a different colour in the
			// readout for the reason the mockup states -- it is a different ladder that moves for
			// different reasons -- and a banner announcing a nuclear event in the DEFCON red would
			// undo that distinction at the loudest possible moment.
			DefconBannerBand.Draw(RenderBounds, DefconPalette.NuclearAccent,
				titleFont,
				shownBanner == BannerRelease
					? DefconReadoutModel.NuclearReleaseBannerTitle
					: DefconReadoutModel.ArmedBannerTitle,
				lineFont, shownLine);
		}
	}
}
