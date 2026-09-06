#version {VERSION}
#ifdef GL_ES
precision highp float;
#endif

// Heat haze: refraction shimmer over hot air. Same primitive as postprocess_textured_vortex.frag --
// re-sample the already-rendered frame at an offset rather than straight through -- but the offset
// is computed procedurally here instead of read from a baked LUT, because a heat event's radius
// varies over four orders of magnitude across its consumers and no fixed-size lookup table survives
// that.
//
// WHY SUMMED SINES AND NOT A NOISE TEXTURE. Three sine layers at frequency ratios that are not
// small-integer multiples of each other (1.00 / 1.61 / 0.77) do not visibly repeat over the life of
// any event this drives, cost about a dozen ALU ops, need no sheet, no upload and no sampler, and
// are identical on every driver. Real convective shimmer is low-frequency and smooth; this is one
// of the few places where a handful of sines is not a cheap approximation of noise but is actually
// the better model of it.
//
// IT RISES. Every layer carries a POSITIVE y coefficient and an ADDED phase term, which is what
// makes all three travel toward -y, i.e. up the screen; see the note on the sines below. That is
// what separates heat haze from water: convection has a direction, and a layer going the wrong way
// reads as a liquid surface rather than as hot air.
//
// EVERY UNIFORM BELOW IS READ. That is not a stylistic note: Shader.SetVec looks the name up in a
// dictionary built from the program's ACTIVE uniforms (Shader.cs:119, :198), so a uniform the
// compiler eliminates as unused becomes a KeyNotFoundException at draw time rather than a warning.
// Deleting a term here means deleting its SetVec in HeatHazeRenderer in the same edit.

uniform sampler2D WorldTexture;

// Peak displacement at the centre of the event, in FRAMEBUFFER pixels. The caller divides its
// world-pixel figure by Renderer.WorldDownscaleFactor before setting this, because gl_FragCoord is
// in framebuffer pixels while the vertex shader's Radius is in world pixels. (postprocess_textured_
// vortex.frag conflates the two; at downscale 1 they are equal, which is why it has never shown.)
uniform float Strength;

// Animation phase in radians. Driven from wall-clock time rather than from the simulation tick, so
// the shimmer is smooth at any framerate above the 16.67 Hz tick rate. Nothing here is simulation
// state -- this pass reads the frame buffer and writes the frame buffer, and no gameplay value
// depends on it.
uniform float Phase;

// How many shimmer cells fit across the event's radius. Held constant in WORLD terms by the caller,
// so a 124-cell fireball gets a fine-grained shimmer and a 3-cell wreck a coarse one, rather than
// both getting the same number of cells stretched to fit.
uniform float Frequency;

// 0 distorts equally in both axes; 1 distorts vertically only. Hot air rising is strongly vertical,
// so the default consumer sits near the top of that range.
uniform float Anisotropy;

in vec2 vLocal;
out vec4 fragColor;

void main()
{
	float r2 = dot(vLocal, vLocal);
	if (r2 > 1.0)
		discard;

	// Radial falloff. (1-r^2)^2 is zero-VALUED and zero-SLOPED at the rim, which matters more than
	// it sounds: a falloff that merely reaches zero still leaves a visible circular seam where the
	// displacement gradient jumps, and the eye finds a circle in a shimmering field immediately.
	float f = 1.0 - r2;
	f *= f;

	vec2 q = vLocal * Frequency;

	// EVERY y coefficient is positive and every phase term is ADDED, which is what makes the pattern
	// travel toward -y, i.e. UP the screen. A layer written `sin(k*y - w*t)` travels the other way,
	// and one layer out of three going the wrong way is enough to read as boiling water rather than
	// as rising air. The x coefficients carry the signs instead, which tilts each layer differently
	// without touching its direction of travel.
	//
	// The three apparent rise speeds are w/k_y = 1.43, 1.24 and 0.37 -- deliberately unequal,
	// because large eddies really do rise more slowly than small ones.
	float a = sin(q.x * 1.00 + q.y * 0.70 + Phase * 1.00);
	float b = sin(q.x * 1.70 + q.y * 1.30 + Phase * 1.61);
	float c = sin(q.x * -0.40 + q.y * 2.10 + Phase * 0.77);

	vec2 delta = vec2((a + 0.6 * b) * (1.0 - Anisotropy), b + 0.7 * c) * Strength * f;

	// texelFetch is undefined outside the texture, and an event near the screen edge really does
	// sample outside it. Clamping costs one instruction and turns a driver-dependent black fringe
	// into an edge-repeat that nobody notices.
	ivec2 size = textureSize(WorldTexture, 0);
	ivec2 uv = clamp(ivec2(gl_FragCoord.xy + delta), ivec2(0), size - ivec2(1));

	fragColor = texelFetch(WorldTexture, uv, 0);
}
