#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

/*
 * THE NUCLEAR MOMENTS -- the gate opening, your side ESCALATING, and your cooldown ending.
 *
 * The ledger in DefconReadoutWidget carries the STATE: what both sides hold, right now, countable.
 * This carries the CHANGES, and the two are not the same job. A level rise happens without the
 * player doing anything, on a tick they were looking at a unit somewhere else on the map, and a
 * 341-pixel panel in the corner quietly gaining a lit box is not something anyone notices. The
 * ruling's strategic claim -- that the winner's correct play is restraint -- is a claim about a
 * DECISION, and a decision nobody knows they have been handed is not one.
 *
 * ==== FOUR EDGES, AND WHAT EACH ONE GETS ====
 *   RELEASE OPENS        banner + speech. Both sides, simultaneously, and it is the first time
 *                        anything nuclear is possible at all.
 *   YOUR LEVEL RISES     banner + speech + a transient. The loud one, and under v2 it is a
 *                        PERMANENT gain rather than a grant with a clock on it -- so the banner
 *                        names the band and gives the player no deadline to act inside.
 *   THEIR LEVEL RISES    a transient line and NO sound. It is the consequence of a shot the player
 *                        just fired deliberately, so they already know they did something; what
 *                        they do not know is what it handed the other side. A second alarm on your
 *                        own action would train the player to ignore the first one.
 *   YOUR COOLDOWN ENDS   a transient line. Your whole arsenal came back at once; the alternative is
 *                        the player re-checking the corner panel every few seconds to find out.
 *
 * ==== IT WATCHES, IT IS NOT TOLD -- THE SAME RULE AS THE OTHER TWO BANNERS ====
 * NuclearExchange moves inside synced simulation code on every client. Everything here -- whether
 * to draw, what clock to freeze into the line, whether a sound has already played -- is
 * client-local render state read back out of the trait each frame. There is no route from a
 * rendering decision into the simulation, and nothing here to get wrong in a sync report.
 *
 * THE SERIAL IS WHAT IT WATCHES. NuclearExchangeState.SideState.LevelSerial is bumped on every RISE
 * of a side's level, and carries no more information than the level itself does -- levels never fall,
 * so every change is a rise. Watching the serial rather than re-deriving the edge from the value keeps
 * this widget and NuclearExchange.ReconcileGrants looking at the same thing, which matters because
 * they act on the same edge: one puts a banner up and the other makes the newly granted cameo ready.
 *
 * (v1 had a harder version of this problem and the serial is inherited from it: a retaliation window
 * could be RESTARTED on the same band, which moved no other number at all, so a widget watching the
 * ticks missed the second hit of a pair.)
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

		/// <summary>Notification played when the viewer's own side's level rises.</summary>
		public readonly string ArmedNotification = "AbombReady";

		/// <summary>Fluent key of the system line shown when the release gate opens.</summary>
		public readonly string ReleaseTextNotification = "notification-nuclear-release";

		/// <summary>Fluent key of the line shown when your own side's level rises.</summary>
		public readonly string EscalatedTextNotification = "notification-nuclear-escalated";

		/// <summary>Fluent key of the line shown when the OTHER side is escalated by your launch.</summary>
		public readonly string EnemyArmedTextNotification = "notification-nuclear-enemy-armed";

		/// <summary>Fluent key of the line shown when your own side's cooldown ends.</summary>
		public readonly string CooldownEndedTextNotification = "notification-nuclear-cooldown-ended";

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

		// Whether the viewer's own side was inside its cooldown last frame, so the FALLING edge can
		// be found. NuclearExchange.Tick already computes exactly this list -- but it does so inside
		// simulation code and hands it to a log line; a widget reaching for it would be reading a
		// per-tick allocation whose lifetime it does not own. One bool is cheaper and is the same edge.
		bool lastOwnOnCooldown;

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
			var ownSerial = exchange.LevelSerialForSide(ownSide);
			var enemySerial = exchange.LevelSerialForSide(enemySide);
			var ownLevel = exchange.LevelForSide(ownSide);
			var ownOnCooldown = exchange.CooldownTicksForSide(ownSide) > 0;

			if (!primed)
			{
				primed = true;
				lastReleaseOpen = releaseOpen;
				lastOwnSerial = ownSerial;
				lastEnemySerial = enemySerial;
				lastOwnOnCooldown = ownOnCooldown;
				return;
			}

			// ---- THE GATE OPENS ------------------------------------------------------------------
			if (releaseOpen && !lastReleaseOpen)
			{
				shownBanner = BannerRelease;
				shownAtTick = world.WorldTick;
				shownLine = DefconReadoutModel.NuclearReleaseBannerLine;

				DefconAlert.Play(world, NotificationPool, ReleaseNotification);
				DefconAlert.Line(world, ReleaseTextNotification);
			}

			// ---- YOUR SIDE HAS ESCALATED ---------------------------------------------------------
			// ON EVERY RISE, INCLUDING ONE THAT ARRIVES WHILE THE RELEASE BANNER IS STILL UP -- the
			// later edge overwrites the earlier, which is correct: a player with four seconds to read
			// one line should be shown the newer fact.
			//
			// IT EXCLUDES THE RELEASE EDGE ITSELF, which also bumps the serial (Release() raises every
			// side from Hold to Kiloton). Without the level guard the gate opening would draw both
			// banners, and the second would replace "NUCLEAR RELEASE / 1 kt available to both sides"
			// with "ESCALATED / 1 kt now available" -- the same event, said worse, half a frame later.
			if (ownSerial != lastOwnSerial && ownLevel > (int)NuclearRung.Kiloton)
			{
				shownBanner = BannerArmed;
				shownAtTick = world.WorldTick;

				// NO CLOCK IN THE LINE. v1 froze the retaliation window's remaining time into it
				// because the grant expired; a LEVEL does not expire, so there is no deadline to state
				// and nothing to freeze. That is the whole difference the banner has to carry.
				shownLine = DefconReadoutModel.ArmedBannerLine(ownLevel);

				DefconAlert.Play(world, NotificationPool, ArmedNotification);

				// AND A TRANSIENT AS WELL AS THE BANNER, which is not duplication: the banner is gone
				// in four seconds and the transients panel keeps its lines for longer. A player who
				// was looking at the other end of the map can still find out what happened.
				DefconAlert.Line(world, EscalatedTextNotification,
					"yield", DefconReadoutModel.RungLabel(ownLevel));
			}

			// ---- THEIR SIDE HAS ESCALATED, BY SOMETHING YOU DID ----------------------------------
			// No banner and no sound. See the header: the player chose to fire, so the event is not a
			// surprise -- what they do not know is the size of the weapon it just handed over, and
			// under v2 they have handed it over permanently rather than for a minute.
			if (enemySerial != lastEnemySerial && enemySide != ownSide)
			{
				var band = exchange.LevelForSide(enemySide);

				// NAME/VALUE PAIRS, NOT POSITIONAL ARGUMENTS. FluentBundle.TryGetMessage walks `args`
				// two at a time and THROWS on an odd count (FluentBundle.cs:128-141), so a line passed
				// bare values would crash at the moment it was shown rather than render oddly.
				//
				// ABOVE Kiloton, for the release edge's reason again: release bumps every side's
				// serial, and "Enemy escalated: 1 kt" on the tick both sides were released at 1 kt
				// would be blaming the player for the gate opening.
				if (band > (int)NuclearRung.Kiloton)
					DefconAlert.Line(world, EnemyArmedTextNotification,
						"yield", DefconReadoutModel.RungLabel(band));
			}

			// ---- YOUR COOLDOWN ENDED -------------------------------------------------------------
			// The falling edge, and nothing else in the HUD announces it: every box on the row
			// brightens at once in a 341-pixel corner panel, which is not a change anyone catches
			// mid-fight. No sound -- the arsenal coming back is good news and does not need an alarm.
			if (lastOwnOnCooldown && !ownOnCooldown)
				DefconAlert.Line(world, CooldownEndedTextNotification);

			lastReleaseOpen = releaseOpen;
			lastOwnSerial = ownSerial;
			lastEnemySerial = enemySerial;
			lastOwnOnCooldown = ownOnCooldown;
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
