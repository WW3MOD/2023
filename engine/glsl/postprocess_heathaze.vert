#version {VERSION}

// Positions ONE heat event's quad. The vertex buffer holds a static unit quad (-1..1) and every
// event's size comes from the Radius uniform, so one 6-vertex buffer serves every event at every
// size -- a 0.7-cell shimmer over a burning wreck and a 124-cell one over a megaton fireball are the
// same six vertices drawn twice with a different uniform.
//
// COORDINATE SPACE, which is the whole trick and is inherited from postprocess_textured.vert:
// aVertexPosition, Pos, Scroll and Radius are all in WORLD PIXELS (WDist * TileSize / 1024), NOT in
// framebuffer pixels and NOT in clip space. Pos comes from WorldRenderer.Screen3DPxPosition of the
// event's WPos and Scroll is Viewport.TopLeft, so subtracting them puts the quad where the world
// position is on screen; p1/p2 then divide by the downscaled framebuffer size to reach clip space.
// That is how a world position reaches a screen-space pass: the pass is not fullscreen, it is a
// quad pinned to a world coordinate, and only the fragments under it are touched.
//
// Because vertex units are world pixels rather than framebuffer pixels, the quad scales with zoom
// exactly like a sprite does. The fragment shader's displacement does NOT -- see the note on
// Strength there.

uniform vec2 Pos, Scroll;
uniform vec2 p1, p2;
uniform float Radius;

in vec2 aVertexPosition;
out vec2 vLocal;

void main()
{
	vLocal = aVertexPosition;
	gl_Position = vec4((aVertexPosition * Radius + Pos - Scroll) * p1 + p2, 0, 1);
}
