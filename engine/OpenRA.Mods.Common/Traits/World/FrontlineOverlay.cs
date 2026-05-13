#region Copyright & License Information
/*
 * WW3MOD FrontlineOverlay — Stage A.3 of the doctrine roadmap.
 *
 * In-game debug overlay that renders InfluenceMap.GetFrontline() as
 * coloured circles, one per contested grid cell. Toggled by chat
 * command (default "/frontline") — F-key hotkey wiring is Stage A.4.
 *
 * Renders ABOVE world units (annotation pass), so the user can see the
 * contested zone over their armies.
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
	[Desc("WW3MOD: in-game debug overlay for InfluenceMap frontline cells.",
		"Chat command toggles render. Reads from InfluenceMap (must also be on the World).")]
	public class FrontlineOverlayInfo : TraitInfo
	{
		[Desc("Chat command to toggle the overlay.")]
		public readonly string CommandName = "frontline";

		[Desc("Colour of the frontline cell fill (ARGB). Alpha lower than 255 lets",
			"adjacent contested cells merge visually without obscuring units underneath.")]
		public readonly Color Color = Color.FromArgb(110, 255, 140, 0);

		public override object Create(ActorInitializer init) { return new FrontlineOverlay(this); }
	}

	public sealed class FrontlineOverlay : IWorldLoaded, IChatCommand, IRenderAnnotations
	{
		readonly FrontlineOverlayInfo info;
		InfluenceMap influenceMap;
		World world;

		bool enabled;

		public FrontlineOverlay(FrontlineOverlayInfo info)
		{
			this.info = info;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			world = w;
			influenceMap = w.WorldActor.TraitOrDefault<InfluenceMap>();

			var console = w.WorldActor.TraitOrDefault<ChatCommands>();
			console?.RegisterCommand(info.CommandName, this);
		}

		void IChatCommand.InvokeCommand(string name, string arg)
		{
			if (name == info.CommandName)
				enabled = !enabled;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!enabled || influenceMap == null)
				yield break;

			// Perspective rule: render from the observing player's POV when there is one
			// (1v1 normal play). Spectator slots use the all-perspective view so the
			// observer sees every contested zone regardless of point of view.
			var perspective = world.LocalPlayer;
			bool[,] frontline;
			if (perspective == null || perspective.Spectating)
				frontline = influenceMap.GetFrontlineAnyPerspective();
			else
				frontline = influenceMap.GetFrontline(perspective);

			// One filled quad per contested grid cell. Adjacent cells share edges so
			// they tile seamlessly into a band — no gaps, no overlap.
			//
			// A grid cell footprint is CellSize × CellSize map cells. Map cells are
			// 1024 WDist on each side. We compute the 4 world corners from the cell
			// at (x, y)'s top-left map-cell origin.
			var cellSize = influenceMap.Info.CellSize;
			var sideWDist = cellSize * 1024;
			for (var x = 0; x < influenceMap.GridWidth; x++)
			{
				for (var y = 0; y < influenceMap.GridHeight; y++)
				{
					if (!frontline[x, y])
						continue;

					// World origin of the cell's top-left corner (map-cell index (x*cellSize, y*cellSize)).
					var originCell = new CPos(x * cellSize, y * cellSize);
					if (!world.Map.Contains(originCell))
						continue;

					var origin = world.Map.CenterOfCell(originCell) - new WVec(512, 512, 0);

					var corners = new[]
					{
						origin,                                            // top-left
						origin + new WVec(sideWDist, 0, 0),                // top-right
						origin + new WVec(sideWDist, sideWDist, 0),        // bottom-right
						origin + new WVec(0, sideWDist, 0),                // bottom-left
					};

					yield return new FilledQuadAnnotationRenderable(corners, info.Color);
				}
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
