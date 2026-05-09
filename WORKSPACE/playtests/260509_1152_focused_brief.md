# Focused playtest brief — 260509

> Run: 1v1 skirmish on a moderate map, ~15-20 min. Goal: clear `[T:trusted]` items by glancing at the listed behaviour. If it looks right, mention it on the way out. If anything feels off, note which one + the rough situation.
>
> You don't have to hit all of these — anything you don't see is fine, it stays `[T:trusted]` for next time.

## 5-second sniff tests (just notice if these are obviously broken)

- **TECN capture under fire** — order a TECN to capture an enemy SR/building with hostiles nearby. After it gets shot, does it eventually resume the capture? *(fix: ScaredyCat snapshots Enter intent and re-issues post-panic)*
- **Soldiers under fire entering a building** — order infantry to enter a garrison while enemy fires at them. Do they stay focused on the entry, or get pulled off? *(fix: raw move bypasses SmartMove)*
- **Stop order on garrisoned firing** — soldiers shooting from a port, hit S. Do they stop? *(fix: AttackGarrisoned.OnStopOrder)*
- **Right-click own SR** — right-click your own Supply Route. Cursor should be enter/evacuate, NOT guard. *(was a regression)*
- **Heli evac off the map edge** — order a heli to retreat off the closest edge while enemy missiles are inbound. Does it actually fly past the edge before despawning, and can the missile still hit it during that flight? *(fix: AircraftOffMapCells=5)*
- **Ground unit production with bad rally** — set rally on water/wall, queue a unit. Does it spawn anyway? *(fix: production no longer blocks at 100% if rally is unreachable)*
- **Garrison batch entry** — shift-queue 4-5 soldiers into one garrison building. Do they all enter, or only the first? *(mitigation: ChangeOwnerInPlace updateGeneration:false)*

## Feel checks (need your judgement, no right answer)

- **Iskander/HIMARS shockwave radius** — radius was cut ~40% (Iskander 7c0→4c0, HIMARS 4c0→2c512). Is it still too generous? Too small now? Just right?
- **Ammo tier-cost feel** — when you evac a tank vs a rifleman vs an MLRS, does the credit refund difference feel right? T0=1 → T9=1500 spread is in place
- **Multi-pool tooltip** — hover over a tank or attack heli in production sidebar. Production tooltip should show one line per ammo pool + grand total. Does it read clearly?
- **Crew = total loss on vehicle death** — when a vehicle dies, NO crew should spawn from the wreck. (Crew can still escape during the Critical bleed-out window). Does this feel right or too harsh? *(superseded f1fdafea with stronger 94c88683)*

## Conditional checks (only if you happen to do these)

- **Supply truck deploy** — deploy a TRUK on empty ground. Drops a SUPPLYCACHE? Then deploy another TRUK on the same cache cell — merges? *(b3699b63)*
- **Supply truck resupply bar** — TRUK on Auto with low cargo, near a Logistics Center. Does it drive over and refill? *(179aba43)*
- **Right-click LC** — right-click own Logistics Center with a damaged TRUK. Default = repair+refill. Ctrl+click = deliver supply
- **Empty TRUK refund** — buy a TRUK, evacuate immediately. Refund should be 250 (vs 1000 for full)
- **Shift+G on attack-ground** — group with attack-move + force-attack-ground orders, hit Shift+G to scatter. The attack-ground orders should be preserved, not converted to plain moves *(IAttackActivity marker)*

## Known still-broken (don't bother)

- **Artillery force-attack during setup** — Layer 2 still open, turret stalls mid-rotation. `test-arty-force-attack-during-setup` is RED.
- **Bridge pathing** — units still walk off bridges into water. Not fixed.
- **Allied shared vision blink** — couldn't reproduce from static analysis. If you see it again, note attacker, healer presence, HP%, motion.

---

**After the playtest:** anything that worked → tell me, I remove from tracker. Anything that felt wrong → tell me what you saw, I open a fresh investigation.
