# nuke-light

Generator for the `Warhead@FireballLight` envelope of every nuclear weapon in the mod.

```
python3 tools/nuke-light/gen_fireball_light.py            # table only, touches nothing
python3 tools/nuke-light/gen_fireball_light.py --check    # non-zero exit if the shipped YAML has drifted
python3 tools/nuke-light/gen_fireball_light.py --write    # rewrite the Light: blocks in place
```

No build, no launch, ~0.1 s. It edits five fields inside each `Light:` block -- `Times`,
`Intensities`, `Radii`, `Tints`, `TerrainRefreshInterval` -- and nothing else in either weapons file.

## When you have to run it

**Whenever the fireball ANIMATION changes.** The rule the envelopes obey is

    D_light = max(fireball animation length, physical fireball lifetime)

so anything that moves an animation moves fourteen light envelopes with it:

* the `Tick` or `ChangeTick` ladder of an explosion sequence in `mods/ww3mod/sequences/sequences-ingame.yaml`
* the SHP a sequence draws, or its frame count
* a weapon's `Warhead@Fireball` `Explosions:` name -- that is how each weapon finds *its* animation,
  and it is why ten weapons on ten sequences will work here exactly as fourteen on one does today
* a weapon's `Warhead@Fireball` `DurationScalePercent` -- since 2026-09-08 every weapon plays the
  same `nuke_large` at its own speed, `t = 11.95 s * (Y/20)^0.12`, so the animation length is the
  ladder walk TIMES that percentage. 60% at the 0.3 kt B61 dial, 100% at the 20 kt anchor, 256% at
  Tsar Bomba. The script reads it from the same warhead block it reads `Explosions:` from, which
  matters on Tsar Bomba: it carries five fireball warheads offset into one cloud.

`ScalePercent` is NOT one of them: it scales the sprite in space, not in time. That is the whole
reason `DurationScalePercent` exists as a separate field rather than being folded into it.

`NuclearYieldTest.EveryNuclearFireballIsATwoStageFlashThatCoolsAndLastsAsLongAsItsFireballAnimation`
re-derives the animation length from the sequences file and the SHP header independently of this
script and fails if the shipped YAML has drifted away from it. So forgetting to run this is caught by
`dotnet test`, not discovered in a playtest.

## The law it applies

    stage 1, white spike   0 <= t <= W:  I = 4.9 + 2.1 * (1 - t/W)^2
    stage 2, red fade      W <= t <= D:  I = 4.9 * (1 - (t-W)/(D-W))^2
    W = round(4 * (kt/20)^0.15) clamped to [3, 14]

Peak 7.0 at tick 0, shoulder at 70% of peak, zero at D. Single-peaked and monotone non-increasing
throughout -- **do not make it non-monotone.** A dip anywhere reads as a strobe at a 60 ms timestep;
that was the 2026-09-06 bug and the test pins the invariant.

Colour is a separate ramp keyed on `t/D`, white through the spike and dull red at the end, so
brightness and hue decay independently. The sprite over it draws with `BlendMode: Additive` and its
own core is white for the first ~20 ticks, so the tint's visible work is mostly OUTSIDE the cloud
early and UNDERNEATH it late.

Radius growth is carried over from whatever the YAML already had rather than recomputed: it is the
Taylor-Sedov rise to the thermal radius and it was never what needed fixing. Only the hold and the
decay are restretched onto the new duration.
