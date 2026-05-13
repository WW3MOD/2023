#region Copyright & License Information
/*
 * WW3MOD: filled quad annotation renderable. Draws a solid-colour
 * quadrilateral over a region of world space, sized in WPos. Used by
 * FrontlineOverlay so adjacent contested cells merge visually into a
 * continuous band rather than appearing as discrete dots.
 */
#endregion

using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Graphics
{
	public class FilledQuadAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		readonly WPos[] worldCorners;  // 4 corners in world space (TL, TR, BR, BL).
		readonly Color color;

		public FilledQuadAnnotationRenderable(WPos[] worldCorners, Color color)
		{
			this.worldCorners = worldCorners;
			Pos = worldCorners[0];
			this.color = color;
		}

		public WPos Pos { get; }
		public int ZOffset => 0;
		public bool IsDecoration => true;

		public IRenderable WithZOffset(int newOffset) { return new FilledQuadAnnotationRenderable(worldCorners, color); }

		public IRenderable OffsetBy(in WVec vec)
		{
			var off = vec;
			var shifted = new WPos[4];
			for (var i = 0; i < 4; i++)
				shifted[i] = worldCorners[i] + off;
			return new FilledQuadAnnotationRenderable(shifted, color);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }

		public void Render(WorldRenderer wr)
		{
			var pa = wr.Viewport.WorldToViewPx(wr.ScreenPosition(worldCorners[0]));
			var pb = wr.Viewport.WorldToViewPx(wr.ScreenPosition(worldCorners[1]));
			var pc = wr.Viewport.WorldToViewPx(wr.ScreenPosition(worldCorners[2]));
			var pd = wr.Viewport.WorldToViewPx(wr.ScreenPosition(worldCorners[3]));
			var a = new float3(pa.X, pa.Y, 0);
			var b = new float3(pb.X, pb.Y, 0);
			var c = new float3(pc.X, pc.Y, 0);
			var d = new float3(pd.X, pd.Y, 0);
			Game.Renderer.RgbaColorRenderer.FillRect(a, b, c, d, color);
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
