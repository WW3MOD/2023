# Final Exchange: per-side package auto-fire on an anchor impact tick replaces the Dead Hand salvo

_Recorded 2026-09-20T11:07:14.066Z by 60a95888_

**What was wrong.** Dead Hand (`PlaceDeadHandSalvo`) was a map-wide, side-blind salvo fired at the window's close with a 30-tick lead-in and 44-tick flight — first impact at open+574 — while a player's Sarmat ordered on the window's first tick lands at order+592 (`MissileDelay 500` + ~92 flight). So the "safety net" always landed before the players' own strikes, on both halves of the map. The window was 30 s in code against 15 s in three rulings, `AimPoints: 6` was static (decision 17's partial salvo unbuilt), and the national enders were 1 × B83 vs 6 × Sarmat RV.

**Options.** (a) Keep Dead Hand and just delay it past the players' impacts; (b) keep Dead Hand but restrict it to the enemy half per side; (c) retire the separate salvo: at close, each unplaced side fires its OWN package (same power, same map-derived N) at the enemy half, and every exchange warhead is launched to land on an interleaved slot after one anchor impact tick.

**Picked (c)** — the user's design. (a) still nukes the placing player's own base; (b) keeps two weapons systems with different visuals and timings for one event. (c) makes the exchange symmetric (same N both sides), makes the lag budget predictable (2N warheads, all game-ender class), and makes arrival independent of missile speed and of who placed when. The time-limit door and its `doomsday` lobby option stay; only the payload changes. Kept: annihilation +90 and frozen-score resolution.

**Taste forks posted, proceeding on the first of each:** America's package = N × B83 (vs a new matched-yield MIRV ICBM); N = clamp(cells/2400, 2, 6) (vs /2000, vs uncapped).
