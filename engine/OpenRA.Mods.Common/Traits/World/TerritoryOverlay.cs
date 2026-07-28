#region Copyright & License Information
/*
 * WW3MOD influence stack — Stage C: player-facing TERRITORY overlay (v2).
 *
 * The commander's map-wide ownership view, promoted from the dev /danger overlay's demoted
 * "Control" wash into a first-class, player-facing layer. Where /danger leads with SAFETY and
 * treats control as a secondary tint, this overlay leads with CONTROL:
 *
 *   - GREEN = believed OURS   (alpha ∝ how firmly held).
 *   - RED   = believed ENEMY  (alpha ∝ how firmly held) — even where nobody stands right now.
 *   - GRAY  = contested / grayzone (a muted wash) — the front, and cells no side owns.
 *
 * The whole map is coloured (the control field is Voronoi-seeded from tick 0), so the player reads
 * who holds what across the entire map, not just where units are — persistence-without-presence.
 *
 * STALENESS STRIPES: over every CONTROLLED cell (own or enemy) a diagonal-stripe hatch is drawn
 * whose opacity grows with the time since that cell was last observed (ControlField.LastVerified).
 * Freshly-seen ground reads clean; ground lost from view — or never seen (seed/persistence belief,
 * never verified) — reads heavily striped. At a glance: where our eyes are, where they are not, and
 * where enemy pressure sits behind fog. Intensity math is the pure, NUnit-pinned TerritoryStripeMath.
 *
 * TOGGLE: ships as a dedicated HOLD-KEY (default T), wired exactly like hold-Space/ShowAllOrders via
 * WorldRenderer.ShowTerritory. A SEPARATE key from hold-Space on purpose — hold-Space already draws
 * the SightingIntelOverlay balance-of-power wash, and stacking a second full-map wash on the same key
 * would z-fight. A dev "/territory" chat command force-enables it (for autotest/screenshot capture,
 * mirroring /intel); it otherwise shows only while the key is held. The dev /danger overlay is left
 * untouched.
 *
 * RENDER-SIDE ONLY. RenderPlayer is legal here (this is NOT sim code). It reads the viewing player's
 * OWN ControlField, so it leaks nothing through fog. HARD WALL: no simulation state depends on any
 * render path — the overlay only READS the sim-built control field; it never writes it, draws no RNG,
 * and performs no reads that perturb the sim. Byte-identity of the sim is trivially preserved.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// Pure staleness→stripe-opacity math, split from the trait (mirroring ControlFieldMath /
	// DangerKernelMath) so the ramp the overlay draws is unit-testable without mounting a world.
	public static class TerritoryStripeMath
	{
		/// <summary>Stripe opacity for a controlled cell as a function of ticks since it was last
		/// observed. Fresh (ticksSinceVerified ≤ 0) → 0 (no stripe). Ramps linearly minAlpha→maxAlpha
		/// over [0, stalenessWindow]; anything older — including never-verified (int.MaxValue) — caps
		/// at maxAlpha. The clamp happens before the multiply, so int.MaxValue cannot overflow.</summary>
		public static int StripeAlpha(int ticksSinceVerified, int stalenessWindow, int minAlpha, int maxAlpha)
		{
			if (ticksSinceVerified <= 0)
				return 0;
			if (stalenessWindow <= 0)
				return maxAlpha;

			var t = ticksSinceVerified >= stalenessWindow ? stalenessWindow : ticksSinceVerified;
			return minAlpha + (maxAlpha - minAlpha) * t / stalenessWindow;
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD influence stack Stage C: player-facing territory (control) overlay — first-class",
		"red/green/gray ownership wash + diagonal staleness stripes. Render-only; reads the viewing",
		"player's own ControlField. Ships as a hold-key (ShowTerritory); '/territory' forces it on (dev).")]
	public class TerritoryOverlayInfo : TraitInfo
	{
		[Desc("Believed-ours wash (green); alpha scales with how firmly the cell is held.")]
		public readonly Color OwnColor = Color.FromArgb(255, 40, 200, 70);

		[Desc("Believed-enemy wash (red); alpha scales with how firmly the cell is held.")]
		public readonly Color EnemyColor = Color.FromArgb(255, 210, 45, 45);

		[Desc("Contested / grayzone wash (gray) — the front, and cells no side owns.")]
		public readonly Color ContestedColor = Color.FromArgb(255, 140, 140, 140);

		[Desc("Min / max blend alpha for a CONTROLLED (own/enemy) cell — alpha ∝ |score| margin.",
			"Bold enough that the whole map reads green/red over dark terrain, but below opaque so",
			"units and terrain stay readable underneath.")]
		public readonly int OwnedMinAlpha = 95;
		public readonly int OwnedMaxAlpha = 180;

		[Desc("Fixed blend alpha for a contested/grayzone cell (muted vs. firmly-owned ground).")]
		public readonly int ContestedAlpha = 70;

		[Desc("Diagonal staleness-stripe colour. LIGHT on purpose so the hatch reads over both the",
			"green/red wash AND the dark terrain (a near-black stripe vanishes on dark ground).",
			"Alpha is computed per cell from staleness; this RGB is the tint.")]
		public readonly Color StripeColor = Color.FromArgb(255, 235, 235, 235);

		[Desc("Min / max stripe alpha across the staleness ramp. A barely-stale cell gets MinAlpha;",
			"a cell aged past the control field's StalenessWindow (or never observed) gets MaxAlpha.")]
		public readonly int StripeMinAlpha = 45;
		public readonly int StripeMaxAlpha = 165;

		[Desc("Screen-pixel width of the diagonal stripe lines.")]
		public readonly int StripeWidth = 2;

		[Desc("Dev chat command that force-enables the overlay (for autotest/screenshot capture).",
			"Ships as the ShowTerritory hold-key regardless of this switch.")]
		public readonly string CommandName = "territory";

		[Desc("Start with the dev force-on switch enabled (development only).")]
		public readonly bool StartAlwaysOn = false;

		public override object Create(ActorInitializer init) { return new TerritoryOverlay(this); }
	}

	public sealed class TerritoryOverlay : IWorldLoaded, IChatCommand, IRenderAnnotations
	{
		readonly TerritoryOverlayInfo info;
		World world;
		ControlField control;
		bool alwaysOn;

		public TerritoryOverlay(TerritoryOverlayInfo info)
		{
			this.info = info;
			alwaysOn = info.StartAlwaysOn;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			control = w.WorldActor.TraitOrDefault<ControlField>();

			var console = w.WorldActor.TraitOrDefault<ChatCommands>();
			console?.RegisterCommand(info.CommandName, this);
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (name == info.CommandName)
				alwaysOn = !alwaysOn;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			// Player-facing gate: shown while the ShowTerritory hold-key is held; the dev switch
			// (/territory) forces it always-on for autotest/screenshot capture.
			if ((!alwaysOn && !wr.ShowTerritory) || control == null)
				yield break;

			// The viewing player. RenderPlayer is the render-side identity in normal play; fall back
			// to LocalPlayer (autotest harness / before assignment). Reading the viewer's OWN control
			// field leaks nothing. No local viewer (dedicated observer) ⇒ show nothing.
			var viewer = world.RenderPlayer ?? world.LocalPlayer;
			if (viewer == null || !control.HasField(viewer))
				yield break;

			var cellSize = control.Info.CellSize;
			var side = cellSize * 1024;
			var half = side / 2;

			for (var gx = 0; gx < control.GridWidth; gx++)
			{
				for (var gy = 0; gy < control.GridHeight; gy++)
				{
					// Snap to the top-left map cell of this grid cell; skip grid cells off the map.
					var originCell = new CPos(gx * cellSize, gy * cellSize);
					if (!world.Map.Contains(originCell))
						continue;

					var origin = world.Map.CenterOfCell(originCell) - new WVec(512, 512, 0);
					var owner = control.OwnerAt(viewer, gx, gy);

					// --- Base ownership wash over the whole map. ---
					var fill = WashColor(viewer, gx, gy, owner);
					if (fill.A > 0)
					{
						var corners = new[]
						{
							origin,
							origin + new WVec(side, 0, 0),
							origin + new WVec(side, side, 0),
							origin + new WVec(0, side, 0),
						};
						yield return new FilledQuadAnnotationRenderable(corners, fill);
					}

					// --- Staleness stripes over CONTROLLED cells only (own/enemy). Contested/gray
					// ground is not striped — staleness of un-owned ground carries no signal. ---
					if (owner == ControlOwner.Contested)
						continue;

					var stripeAlpha = TerritoryStripeMath.StripeAlpha(
						control.TicksSinceVerified(viewer, gx, gy), control.Info.StalenessWindow,
						info.StripeMinAlpha, info.StripeMaxAlpha);
					if (stripeAlpha <= 0)
						continue;

					var stripe = Color.FromArgb(stripeAlpha, info.StripeColor);

					// Three parallel diagonals (BL→TR) at 1/4, 1/2, 3/4 across the cell — a regular
					// hatch that tiles into a continuous diagonal texture over adjacent stale cells.
					yield return new LineAnnotationRenderable(
						origin + new WVec(0, half, 0), origin + new WVec(half, 0, 0), info.StripeWidth, stripe);
					yield return new LineAnnotationRenderable(
						origin + new WVec(0, side, 0), origin + new WVec(side, 0, 0), info.StripeWidth, stripe);
					yield return new LineAnnotationRenderable(
						origin + new WVec(half, side, 0), origin + new WVec(side, half, 0), info.StripeWidth, stripe);
				}
			}
		}

		// Owner-coloured wash; controlled cells scale alpha by |score| margin, contested is a flat mute.
		Color WashColor(Player viewer, int gx, int gy, ControlOwner owner)
		{
			if (owner == ControlOwner.Contested)
				return Color.FromArgb(info.ContestedAlpha, info.ContestedColor);

			var rgb = owner == ControlOwner.Own ? info.OwnColor : info.EnemyColor;
			var magnitude = Math.Abs(control.ScoreAt(viewer, gx, gy));
			var maxScore = control.Info.MaxScore;
			var alpha = info.OwnedMinAlpha;
			if (maxScore > 0)
			{
				var scaled = Math.Min(magnitude, maxScore);
				alpha += (info.OwnedMaxAlpha - info.OwnedMinAlpha) * scaled / maxScore;
			}

			return Color.FromArgb(alpha, rgb);
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
