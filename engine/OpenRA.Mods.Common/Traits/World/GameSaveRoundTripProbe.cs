#region Copyright & License Information
/*
 * WW3MOD test harness — saved-game round-trip probe (world trait).
 *
 * Drives the whole save -> restore path inside ONE process so it can be
 * exercised without manual input:
 *
 *   1. Recording life: request a game save at SaveAtTick.
 *   2. On the server's GameSaved acknowledgement, start a fresh local server
 *      seeded with a LoadGameSave order — exactly what the in-game "Load Game"
 *      button does (GameSaveBrowserLogic.Load).
 *   3. Restored life: wait for INotifyGameLoaded, dismiss the options menu that
 *      LoadWidgetAtGameStart.GameLoaded auto-opens, then check the world
 *      actually resumes.
 *
 * The stage field is static because step 2 destroys the world and builds a new
 * one from the same map, so the trait instance is recreated but the process is
 * not.
 *
 * Inert unless Test.Mode=true, and only ever placed by a test scenario's
 * rules.yaml — it is not part of any shipped ruleset.
 */
#endregion

using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Test-harness only. Saves the game, reloads it in-process, and verdicts on whether the",
		"restored world can be resumed. No-op unless Test.Mode=true.")]
	public class GameSaveRoundTripProbeInfo : TraitInfo
	{
		[Desc("World tick at which the recording life pauses, standing in for the player opening the options menu.")]
		public readonly int SaveAtTick = 60;

		[Desc("Render frames to wait after pausing before requesting the save, so the Pause order has",
			"round-tripped through the server and been recorded into the save's order stream.")]
		public readonly int PauseSettleFrames = 20;

		[Desc("Save file name, written under <SupportDir>/Saves/<mod>/<version>/.")]
		public readonly string SaveFilename = "ww3mod-roundtrip-probe.orasav";

		[Desc("Render frames to wait after the restore completes before dismissing the auto-opened options menu.")]
		public readonly int DismissMenuAfterFrames = 30;

		[Desc("Render frames to observe after dismissing the menu before writing the verdict.")]
		public readonly int ObserveFrames = 150;

		public override object Create(ActorInitializer init) { return new GameSaveRoundTripProbe(this); }
	}

	public class GameSaveRoundTripProbe : ITick, ITickRender, INotifyGameSaved, INotifyGameLoaded
	{
		enum Stage { Recording, Restoring, Observing, Done }

		static Stage stage = Stage.Recording;
		static string restoreMapUid;

		readonly GameSaveRoundTripProbeInfo info;

		bool pauseRequested;
		int framesSincePause;
		bool saveRequested;
		int framesSinceLoad;
		bool menuDismissed;
		int worldTickAtDismiss;
		int netFrameAtDismiss;

		public GameSaveRoundTripProbe(GameSaveRoundTripProbeInfo info)
		{
			this.info = info;
		}

		void ITick.Tick(Actor self)
		{
			if (!TestMode.IsActive || stage != Stage.Recording || pauseRequested)
				return;

			var world = self.World;
			if (world.WorldTick < info.SaveAtTick)
				return;

			if (!world.LobbyInfo.GlobalSettings.GameSavesEnabled)
			{
				stage = Stage.Done;
				TestMode.WriteResult("skip", "game saves are disabled for this session");
				Game.Exit();
				return;
			}

			// Saving is only reachable from the in-game menu, and opening that menu issues
			// SetPauseState(true) (MenuButtonsChromeLogic.cs:117) — a non-immediate order, so
			// every real save's order stream ends with a Pause. Reproduce that here: without it
			// the restore never exercises the pause round-trip the bug lives in.
			pauseRequested = true;
			restoreMapUid = world.Map.Uid;
			Log.Write("debug", $"[saveprobe] pausing at tick {world.WorldTick} (stands in for opening the options menu)");
			world.SetPauseState(true);
		}

		void INotifyGameSaved.GameSaved(World world)
		{
			if (!TestMode.IsActive || stage != Stage.Recording)
				return;

			stage = Stage.Restoring;
			Log.Write("debug", "[saveprobe] save written — restarting into the saved game");

			// Mirrors GameSaveBrowserLogic.Load: a fresh local server seeded with a
			// LoadGameSave order. RunAfterTick defers past the current tick; RunUnsynced
			// because the real path arrives here from a button click, which is already an
			// unsynced context (see the PITFALL in MenuButtonsChromeLogic).
			Game.RunAfterTick(() => Sync.RunUnsynced(world, () =>
				Game.CreateAndStartLocalServer(restoreMapUid, new[]
				{
					Order.FromTargetString("LoadGameSave", info.SaveFilename, true),
					Order.Command($"state {Session.ClientState.Ready}")
				})));
		}

		void INotifyGameLoaded.GameLoaded(World world)
		{
			if (!TestMode.IsActive || stage != Stage.Restoring)
				return;

			stage = Stage.Observing;
			framesSinceLoad = 0;
			Log.Write("debug", "[saveprobe] restore complete — " + Describe(world));
		}

		// TickRender rather than Tick: World.TickRender runs regardless of world.Paused
		// (World.cs:519), which is the whole point — a probe for a stuck-pause bug must not
		// be gated by the pause it is measuring.
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			if (!TestMode.IsActive)
				return;

			var world = self.World;

			// The recording life finishes here rather than in Tick: once the stand-in menu
			// pause lands, World.Tick stops ticking actors (World.cs:494) and ITick never
			// fires again — but a real player saves from exactly that paused state.
			if (stage == Stage.Recording)
			{
				if (!pauseRequested || saveRequested || ++framesSincePause < info.PauseSettleFrames)
					return;

				saveRequested = true;
				Log.Write("debug", $"[saveprobe] requesting save — {Describe(world)}");
				world.RequestGameSave(info.SaveFilename);
				return;
			}

			if (stage != Stage.Observing)
				return;

			framesSinceLoad++;

			if (!menuDismissed)
			{
				if (framesSinceLoad < info.DismissMenuAfterFrames)
					return;

				// A missing RESUME button means the restore never re-opened the options menu
				// (LoadWidgetAtGameStart.cs:83-88). Fail loudly rather than fall through — otherwise
				// the verdict below blames the pause for what is really a broken restore path.
				var resume = Ui.Root.GetOrNull("MENU_ROOT")?.GetOrNull<ButtonWidget>("RESUME");
				if (resume == null)
				{
					stage = Stage.Done;
					TestMode.WriteResult("fail", "restore did not re-open the options menu (no RESUME button): " + Describe(world));
					Game.Exit();
					return;
				}

				menuDismissed = true;
				worldTickAtDismiss = world.WorldTick;
				netFrameAtDismiss = Game.NetFrameNumber;

				Log.Write("debug", "[saveprobe] dismissing menu — " + Describe(world));
				resume.OnClick();
				return;
			}

			if (framesSinceLoad < info.DismissMenuAfterFrames + info.ObserveFrames)
				return;

			stage = Stage.Done;

			var worldTicks = world.WorldTick - worldTickAtDismiss;
			var netFrames = Game.NetFrameNumber - netFrameAtDismiss;
			var detail = $"{Describe(world)} worldticks-since-resume={worldTicks} netframes-since-resume={netFrames}";

			Log.Write("debug", "[saveprobe] verdict — " + detail);

			if (!world.Paused && worldTicks > 0)
				TestMode.WriteResult("pass", "restored game resumed after closing the menu: " + detail);
			else
				TestMode.WriteResult("fail", "restored game did not resume after closing the menu: " + detail);

			Game.Exit();
		}

		static string Describe(World world)
		{
			return $"paused={world.Paused} predictedpaused={world.PredictedPaused} gameover={world.IsGameOver} " +
				$"worldtick={world.WorldTick} netframe={Game.NetFrameNumber} localframe={Game.LocalTick} " +
				$"loadingsave={world.IsLoadingGameSave} replay={world.IsReplay} " +
				$"nonbotclients={world.LobbyInfo.NonBotClients.Count()}";
		}
	}
}
