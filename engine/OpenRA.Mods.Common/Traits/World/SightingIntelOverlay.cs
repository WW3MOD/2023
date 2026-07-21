#region Copyright & License Information
/*
 * WW3MOD strategic/tactical split — Phase 1, §3d.
 *
 * Hold-Space intel overlay. Space already renders order/waypoint lines
 * (wr.ShowAllOrders); this extends that same held-key pass with two views built
 * ONLY from the viewing player's own §3a SightingThreatLayer:
 *
 *   - Balance-of-power color wash: green where the player's own forces dominate,
 *     red where sighted enemy forces dominate. The GRAYZONE is COMPUTED, not
 *     stored — a cell whose |friendly − enemy| dominance fails to clear a
 *     threshold renders neutral gray. No third data channel.
 *   - Last-seen enemies as GPS dots, reusing the in-repo satellite substrate
 *     (the "gpsdot" sprite), driven from the player's FrozenActorLayer.
 *
 * This is the Phase-1 VERIFICATION tool: the user eyeballs layer correctness
 * in-game before any behavior consumes the layers in Phase 2.
 *
 * RENDER-SIDE ONLY. RenderPlayer is legal here (this is NOT sim code). It reads
 * the viewing player's own per-player layer + own frozen actors, so it leaks
 * nothing through fog. A dev switch (chat command / Info flag) can force it
 * always-visible for development; it SHIPS as hold-Space.
 *
 * See WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Commands;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("WW3MOD Phase 1 (§3d): hold-Space intel overlay — balance-of-power wash + GPS dots.",
		"Render-only; reads the viewing player's own SightingThreatLayer (§3a) + frozen actors.")]
	public class SightingIntelOverlayInfo : TraitInfo
	{
		[Desc("Dominance (friendly − enemy) magnitude below which a cell renders",
			"neutral GRAY instead of green/red. This is the computed grayzone band.")]
		public readonly int GrayzoneThreshold = 60;

		[Desc("Minimum blend alpha for a washed cell.")]
		public readonly int MinAlpha = 50;

		[Desc("Maximum blend alpha for a washed cell (kept below opaque so units stay visible).")]
		public readonly int MaxAlpha = 150;

		[Desc("Dominance/intensity magnitude that maps to MaxAlpha.")]
		public readonly int AlphaFullScale = 600;

		[Desc("Friendly-dominant wash color (RGB; alpha is computed).")]
		public readonly Color FriendlyColor = Color.FromArgb(255, 40, 200, 60);

		[Desc("Enemy-dominant wash color (RGB; alpha is computed).")]
		public readonly Color EnemyColor = Color.FromArgb(255, 210, 40, 40);

		[Desc("Grayzone (contested-but-inconclusive) wash color (RGB; alpha is computed).")]
		public readonly Color GrayzoneColor = Color.FromArgb(255, 150, 150, 150);

		[Desc("Sprite collection for the last-seen enemy dots (satellite substrate).")]
		public readonly string DotImage = "gpsdot";

		[Desc("Sequence within DotImage to use for the dots.")]
		public readonly string DotSequence = "Infantry";

		[PaletteReference(true)]
		[Desc("Palette prefix for the dots; suffixed with the enemy owner's internal name.")]
		public readonly string DotPalettePrefix = "player";

		[Desc("Chat command that toggles the dev always-on switch. Ships hold-Space regardless.")]
		public readonly string CommandName = "intel";

		[Desc("Start with the dev always-on switch enabled (development only).")]
		public readonly bool StartAlwaysOn = false;

		public override object Create(ActorInitializer init) { return new SightingIntelOverlay(this); }
	}

	public sealed class SightingIntelOverlay : IWorldLoaded, IChatCommand, IRenderAnnotations
	{
		readonly SightingIntelOverlayInfo info;
		World world;
		SightingThreatLayer sighting;
		Animation dotAnim;
		bool alwaysOn;

		public SightingIntelOverlay(SightingIntelOverlayInfo info)
		{
			this.info = info;
			alwaysOn = info.StartAlwaysOn;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			sighting = w.WorldActor.TraitOrDefault<SightingThreatLayer>();

			// The gpsdot sprite may be absent in some content configs — degrade to
			// wash-only rather than crashing the overlay.
			try
			{
				dotAnim = new Animation(w, info.DotImage);
				dotAnim.PlayRepeating(info.DotSequence);
			}
			catch (System.Exception e)
			{
				dotAnim = null;
				Log.Write("debug", $"SightingIntelOverlay: gpsdot animation unavailable ({e.Message}); dots disabled.");
			}

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
			// Hold-Space (ShowAllOrders) is the shipped trigger; the dev switch forces it on.
			if (!alwaysOn && !wr.ShowAllOrders)
				yield break;

			if (sighting == null)
				yield break;

			// Viewing player. RenderPlayer is the legal render-side per-player identity.
			// No viewer (pure spectator) ⇒ nothing to show without leaking fog.
			var viewer = world.RenderPlayer;
			if (viewer == null)
				yield break;

			// --- Balance-of-power wash, over the viewer's own §3a active cells only. ---
			foreach (var cell in sighting.ActiveCells(viewer))
			{
				var enemy = sighting.ThreatIntensity(viewer, cell);
				var friendly = sighting.FriendlyIntensity(viewer, cell);
				if (enemy == 0 && friendly == 0)
					continue;

				var dominance = friendly - enemy;

				Color rgb;
				int magnitude;
				if (dominance > info.GrayzoneThreshold)
				{
					rgb = info.FriendlyColor;
					magnitude = dominance;
				}
				else if (dominance < -info.GrayzoneThreshold)
				{
					rgb = info.EnemyColor;
					magnitude = -dominance;
				}
				else
				{
					// Computed grayzone: neither side clears the threshold.
					rgb = info.GrayzoneColor;
					magnitude = enemy + friendly;
				}

				var alpha = info.MinAlpha + (info.MaxAlpha - info.MinAlpha) * System.Math.Min(magnitude, info.AlphaFullScale) / info.AlphaFullScale;
				yield return new MarkerTileRenderable(cell, Color.FromArgb(alpha, rgb));
			}

			// --- Last-seen enemy GPS dots, from the viewer's own frozen actors. ---
			if (dotAnim == null)
				yield break;

			foreach (var fa in world.ScreenMap.RenderableFrozenActorsInBox(viewer, wr.Viewport.TopLeft, wr.Viewport.BottomRight))
			{
				if (!fa.IsValid || !fa.Visible || fa.Owner == null)
					continue;

				if (viewer.RelationshipWith(fa.Owner) != PlayerRelationship.Enemy)
					continue;

				var palette = wr.Palette(info.DotPalettePrefix + fa.Owner.InternalName);
				var screenPos = wr.Viewport.WorldToViewPx(wr.ScreenPxPosition(fa.CenterPosition));
				foreach (var r in dotAnim.RenderUI(wr, screenPos, WVec.Zero, 0, palette))
					yield return r;
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
