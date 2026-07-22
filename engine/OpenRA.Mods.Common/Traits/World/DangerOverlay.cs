#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage C: commander's danger overlay (v2).
 *
 * The full-map commander's view (design §1.8 + §2D). PRIMARY read is SAFETY, not ownership:
 *   - GREEN = verified safe — recently OBSERVED and currently outside every believed enemy weapon
 *     envelope. "Verified" is PERISHABLE: a green cell relaxes back to gray once its observation
 *     ages past the control field's staleness window (a commander does not trust 5-minute-old intel).
 *   - RED   = verified/assumed unsafe — inside a believed enemy danger envelope or a recent contact.
 *     Keyed on the DANGER field alone, so a cell holding our OWN units still reads red — they are
 *     standing in a danger zone (the consequence the user explicitly endorsed, §1.8).
 *   - GRAY  = potentially dangerous — unobserved fog with no positive evidence either way.
 *
 * MODES (cycled by the dev chat command):
 *   Off → DangerGround → DangerAir → Control → Off.
 *   - DangerGround / DangerAir: the tri-state above, on the anti-ground or anti-air channel. The
 *     air channel is the debugging window into Stage-D helicopter safety.
 *   - Control: the control field DEMOTED to a secondary visualization — owner colour, brightness by
 *     margin (how firmly held). A first-class DATA layer regardless; the overlay just leads with danger.
 *
 * DEV-GATED, OFF BY DEFAULT: reachable only via the chat command (same mechanism as FrontlineOverlay /
 * SightingIntelOverlay's dev switch). It does NOT ship on hold-Space — this is a development/debug tool.
 *
 * RENDER-SIDE ONLY. RenderPlayer is legal here (this is NOT sim code). It reads the viewing player's
 * OWN per-player layers, so it leaks nothing through fog. HARD WALL: no simulation state depends on any
 * render path — the overlay only READS the sim-built control/danger fields; it never writes them.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD influence stack Stage C: dev commander's danger overlay (tri-state safety + air +",
		"control-field secondary). Render-only; reads the viewing player's own ControlField/DangerFieldLayer.",
		"OFF by default — cycled by a dev chat command; never ships on hold-Space.")]
	public class DangerOverlayInfo : TraitInfo
	{
		[Desc("Dev chat command that cycles the overlay mode: Off → DangerGround → DangerAir → Control.")]
		public readonly string CommandName = "danger";

		[Desc("Verified-safe wash (green): observed clear AND outside every believed danger envelope.")]
		public readonly Color SafeColor = Color.FromArgb(120, 40, 200, 60);

		[Desc("Unsafe wash (red): inside a believed danger envelope / recent contact (own units too).")]
		public readonly Color UnsafeColor = Color.FromArgb(120, 210, 40, 40);

		[Desc("Potentially-dangerous wash (gray): unobserved fog, no positive evidence.")]
		public readonly Color UnknownColor = Color.FromArgb(70, 130, 130, 130);

		[Desc("Control secondary mode: own-territory colour (alpha scaled by margin).")]
		public readonly Color OwnColor = Color.FromArgb(255, 40, 200, 60);

		[Desc("Control secondary mode: enemy-territory colour (alpha scaled by margin).")]
		public readonly Color EnemyColor = Color.FromArgb(255, 210, 40, 40);

		[Desc("Control secondary mode: contested/grayzone colour (alpha scaled by margin).")]
		public readonly Color ContestedColor = Color.FromArgb(255, 130, 130, 130);

		[Desc("Minimum / maximum blend alpha for the control-mode wash.")]
		public readonly int ControlMinAlpha = 40;
		public readonly int ControlMaxAlpha = 150;

		public override object Create(ActorInitializer init) { return new DangerOverlay(this); }
	}

	public sealed class DangerOverlay : IWorldLoaded, IChatCommand, IRenderAnnotations
	{
		enum Mode { Off, DangerGround, DangerAir, Control }

		readonly DangerOverlayInfo info;
		World world;
		ControlField control;
		DangerFieldLayer danger;
		Mode mode = Mode.Off;

		public DangerOverlay(DangerOverlayInfo info)
		{
			this.info = info;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			control = w.WorldActor.TraitOrDefault<ControlField>();
			danger = w.WorldActor.TraitOrDefault<DangerFieldLayer>();

			var console = w.WorldActor.TraitOrDefault<ChatCommands>();
			console?.RegisterCommand(info.CommandName, this);
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (name != info.CommandName)
				return;

			// Cycle Off → DangerGround → DangerAir → Control → Off.
			mode = mode == Mode.Control ? Mode.Off : (Mode)((int)mode + 1);
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (mode == Mode.Off || control == null)
				yield break;

			// The viewing player. RenderPlayer is the render-side identity in normal play; fall back
			// to LocalPlayer (autotest harness / before assignment). Reading the viewer's OWN layers
			// leaks nothing. No local viewer (dedicated observer) ⇒ show nothing.
			var viewer = world.RenderPlayer ?? world.LocalPlayer;
			if (viewer == null || !control.HasField(viewer))
				yield break;

			var cellSize = control.Info.CellSize;
			var sideWDist = cellSize * 1024;
			for (var gx = 0; gx < control.GridWidth; gx++)
			{
				for (var gy = 0; gy < control.GridHeight; gy++)
				{
					var color = mode == Mode.Control
						? ControlColor(viewer, gx, gy)
						: DangerColor(viewer, gx, gy, mode == Mode.DangerAir ? DangerChannel.Air : DangerChannel.Ground);

					if (color.A == 0)
						continue;

					var originCell = new CPos(gx * cellSize, gy * cellSize);
					if (!world.Map.Contains(originCell))
						continue;

					var origin = world.Map.CenterOfCell(originCell) - new WVec(512, 512, 0);
					var corners = new[]
					{
						origin,
						origin + new WVec(sideWDist, 0, 0),
						origin + new WVec(sideWDist, sideWDist, 0),
						origin + new WVec(0, sideWDist, 0),
					};

					yield return new FilledQuadAnnotationRenderable(corners, color);
				}
			}
		}

		// Tri-state safety read for one channel. Danger drives RED (own units included); a recently
		// observed, danger-free cell is GREEN; everything else is GRAY.
		Color DangerColor(Player viewer, int gx, int gy, DangerChannel channel)
		{
			var d = danger != null ? danger.Danger(viewer, control.GridCellToMapCell(gx, gy), channel) : 0;
			if (d > 0)
				return info.UnsafeColor;

			if (control.IsVerifiedFresh(viewer, gx, gy))
				return info.SafeColor;

			return info.UnknownColor;
		}

		// Secondary control wash: owner colour, alpha scaled by how firmly the cell is held.
		Color ControlColor(Player viewer, int gx, int gy)
		{
			var score = control.ScoreAt(viewer, gx, gy);
			var owner = control.OwnerAt(viewer, gx, gy);

			var rgb = owner == ControlOwner.Own ? info.OwnColor
				: owner == ControlOwner.Enemy ? info.EnemyColor
				: info.ContestedColor;

			var magnitude = System.Math.Abs(score);
			var scale = control.Info.MaxScore > 0 ? System.Math.Min(magnitude, control.Info.MaxScore) : magnitude;
			var alpha = info.ControlMinAlpha;
			if (control.Info.MaxScore > 0)
				alpha += (info.ControlMaxAlpha - info.ControlMinAlpha) * scale / control.Info.MaxScore;

			return Color.FromArgb(alpha, rgb);
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
