# AI debug overlay — the frontline

> **What it is.** A toggleable in-game visualisation that draws the **contested band** — the cells where both you and the enemy have influence overlap. It's the same data the AI uses to reason about defence in depth. Turning it on lets you see what the AI sees.

## How to toggle

1. Open the chat box (Enter key on most layouts).
2. Type `/frontline` and press Enter.
3. A band of orange filled circles appears across the contested zone.
4. Type `/frontline` again to turn it off.

If nothing happens, the chat command may not be registered for your map. Check that `FrontlineOverlay` is in the map's world rules (`mods/ww3mod/rules/world.yaml` has it as a default for the mod, but custom maps could override).

## What the orange means

Each orange circle covers a 2×2 map-cell grid square that the **InfluenceMap** has marked as *contested* — meaning at least one friendly military unit and one enemy military unit have influence reaching that cell.

The size of a unit's influence depends on its sell value (a $1500 Bradley contributes more than a $200 Conscript) and falls off linearly with distance (full strength at the unit's position, fading out over a 3-cell radius).

So a circle appears when two opposing forces are *within roughly 5–6 cells of each other*, not just when they're touching. This matches the doctrine idea that the *contact zone* is wider than the single tile where bullets are landing — it includes the area both sides could quickly maneuver into.

## What you'll see in practice

- **Beginning of a match:** no overlay band — both forces are too far apart. The InfluenceMap exists but no cells overlap.
- **Scouts make contact:** a few sparse orange circles appear where the scout's influence touches the enemy's first units.
- **Armies engage:** a thick band forms across the contact zone. The middle cells (where both sides have multiple units near) are pure orange; the edges fade.
- **One side pushes:** the band moves with the push. The retreating side's edge of the band recedes; the advancing side's edge extends forward.
- **A flank attempt:** a second, narrower band forms around the flanking force. Two bands = two contact zones = AI sees pressure on two axes.
- **One side wiped from a sector:** the band disappears there (no enemy influence to overlap).

## Why this overlay exists

Real military commanders don't think in terms of "individual units" — they think in terms of *lines, sectors, axes*. WW3MOD's v2 AI is being built around the same idea (see [`../../WORKSPACE/ai/archive/doctrine.md`](../../WORKSPACE/ai/archive/doctrine.md)). The frontline overlay is the first piece — once the AI knows where the contested zone is, every future doctrine piece (screen placement, main-line positioning, reserve management, 3:1 concentration of force) builds on it.

For a player, the overlay is *useful*:

- **Read enemy intent.** Where the band is thickest = where the enemy is committing forces.
- **Find gaps.** A break in the band = a sector with no contact = potential flanking lane.
- **Confirm your defensive posture.** If the band is too close to your Supply Route, you're losing the defence-in-depth fight; pull your main line forward.

## Performance

The overlay refreshes whenever the InfluenceMap does (every 25 sim-sec ticks ≈ 1 sim-second). It draws one circle per contested grid cell — on a 66×34 map at default settings, that's at most ~561 cells, in practice usually 20–80 once a band forms. Performance impact is negligible.

## Open extension points

- A hotkey binding (`F11` is the planned default; not wired yet — Stage A.4 work). For now, the chat command is the toggle.
- Colour coding per side: a future iteration may colour the band by **who is pushing whom** — your side advancing toward enemy = green; enemy pushing in = red.
- Per-faction filter so spectators can choose whose perspective to view.

## Related docs

- [`../../WORKSPACE/ai/archive/doctrine.md`](../../WORKSPACE/ai/archive/doctrine.md) — the doctrine roadmap. Defence in depth, 3:1 concentration, reserve management. The overlay is the visible piece of "frontline perception."
- [`../../WORKSPACE/ai/archive/stage_a_frontline_perception.md`](../../WORKSPACE/ai/archive/stage_a_frontline_perception.md) — the stage spec this overlay implements.
