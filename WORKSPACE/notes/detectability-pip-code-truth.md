# Detectability pip — what the code actually says

Read at `main @ fa284169` (briefed against `6a7e1839`, which is an ancestor; the intervening
commits are the strafe aim-point work and touch no detection code). No game launched, no YAML
linted, no game code changed.

Written for whoever builds the detectability-pip mockup. Three of these contradict the brief
that was circulating.

---

## 1 · The orange veto is NOT hypothetical for humans. Half of it ships today, ungated.

**Brief said:** orange has no data source on human-controlled units; a human clicking Ambush gets
plain hold-fire and the halt branch stays dead.

**Code says:** that is true of the *movement halt* only. The *fire veto* is on the ungated stock
path and runs for anyone in Ambush stance.

`engine/OpenRA.Mods.Common/Traits/AutoTarget.cs`, `AmbushTickIdle` (`:714`):

- `:720` — `var stage3 = AmbushTacticsGranted(self);` — false for every human unit.
- `:763-765` — `PreAimAtTarget(self, target)`: rotate toward the target **without firing**.
- `:769` — `var isSpotted = self.CanBeViewedByPlayer(targetOwner);`
- `:771-786` — the ungated stock path: `if (isSpotted || ambushTriggered) { … Attack(…); } return;`

So a human's Ambush soldier with a live scanned target pre-aims, asks whether the target's owner
can see him, and **returns without firing** when the answer is no. That is exactly "blocked from
doing what we otherwise would, because it would reveal us" — live, today, no prerequisite.

**Changes:** the design is not choosing between one state that ships and one that never does. The
fire half of orange is computable now from a variable already in hand at `:769`. Only the movement
half needs `enable-ambush-tactics`. The half that ships is also the half that occurs far more
often — a soldier holding fire is much commoner than a soldier halting an attack-move.

## 2 · The two feeds are not independent. Detection *releases* the veto.

**Brief said:** two independent feeds; a soldier can be spotted AND blocked at once.

**Code says:** being spotted is the veto's exit condition.

- `AutoTarget.cs:774` — `if (isSpotted || ambushTriggered)` → Attack.
- `engine/OpenRA.Mods.Common/Traits/AmbushTactics.cs:48-60` — `ShouldHaltBeforeContact` ends
  `return !groupDetected;`. The halt holds **only while unseen**.
- `AmbushTactics.cs:161-191` — `EvaluateSpring`: `if (detected) return AmbushSpringTrigger.Detected;`
  is trigger **1**, ahead of damage and all three score triggers. Fed at `AutoTarget.cs:831` as
  `detected: isSpotted`.

For a single enemy, "spotted AND blocked" is a **contradiction**, not a collision.

**Changes:** red-vs-orange precedence almost never arbitrates anything, so the head-on collision the
mockup was to dramatise mostly dissolves. That still argues for *colour = detectability, shape =
veto* — but on inverted grounds: not "they collide constantly so they need separate channels", but
"they barely collide, so give colour wholly to the continuum and spend a cheap second channel on
the rare veto".

### 2a · Where the collision IS real — two narrow edges, both worth drawing

**Different enemies.** `isSpotted` (`:769`) is `self.CanBeViewedByPlayer(targetOwner)` — only the
owner of the actor he is aiming at. Red's predicate in `WithSpottedDecoration.IsSpotted` (`:95-118`)
quantifies over **every** enemy observer inside `MaximumObserverRange` (32c0). Red is a strict
superset. With 3+ players: enemy B sees him (red lights) while he aims at enemy A who cannot (veto
holds). This is the only ordinary-play state where the brief's collision exists.

**The latch.** `ambushTriggered` is terminal — `AutoTarget.cs:1007-1014`, `ResetAmbushState` is
"the ONLY path that clears the terminal SPRUNG latch", and it runs on stance change away from
Ambush. Once sprung, `isSpotted || ambushTriggered` keeps him firing after he re-conceals: the pip
reads "concealed" while he shoots. No colour/shape resolution fixes this, because the pip is honest
about *detectability* — it is the player's inference about *behaviour* that breaks. Worth knowing
before spending art on the pip.

## 3 · The worked example in the brief cannot happen with a plain move order.

**Brief quoted the user:** "Why is this soldier not moving even though he has a move order — Aha,
he has that orange diamond, he is blocked, I need to order a force move."

**Code says**, `engine/OpenRA.Mods.Common/Activities/Move/AttackMoveActivity.cs:188-189`:

> Plain player Move never enters this activity (it is a bare Move), so — per resolved fork B — a
> plain Move is always obeyed; only attack-move / bot auto-move can halt.

The halt (`:190-205`) lives inside `AttackMoveActivity` and is additionally gated at `:191-193` on
Ambush stance **and** `GetConditionCount(ambushGate) > 0`.

**Changes:** (a) orange's movement remit is narrower than the user believes — it explains a stalled
*attack-move*, never a stalled move. (b) The remedy he names is wrong: force-move is not needed, an
**ordinary move** already always works. If that sentence is repeated verbatim in a tooltip or design
doc it teaches a wrong mental model.

---

## Smaller findings, not in the brief

**The shipped mark is a text decoration.** `WithSpottedDecoration : WithTextDecoration` (`:45`),
Info extends `WithTextDecorationInfo` (`:29`). If the pip stays text, a hollow-centre veto modifier
costs literally one character — `◆` → `◇`. That is the cheapest possible second channel and it was
not in the brief's cost picture.

**`RecalculationInterval = 7`** (`:33`), cached between recomputes (`:72-79`). The spotted test
refreshes ~2.4×/sec at 16.67 tps. A continuous "margin closing" gradient sampled that coarsely will
**step**, not glide. If the grading is meant to read as motion, that interval has to come down —
a cost that belongs in the decision, not in implementation surprise.

**The asymmetry rule constrains what "no pip" may mean.** `:20-22` (Desc) and `:105-111`: an enemy
that can see us but that we have not spotted does **not** light the mark — "a badge driven by true
visibility alone would be a wallhack". So the bottom of the detectability slider is "you know of no
watcher", not "you are safe". Player-facing wording has to survive that; "no enemy can see you"
is a lie the engine deliberately refuses to tell.

## Brief's own numbers — re-derived and confirmed

- `Detectable.ClampConcealment` (`:118-125`): floor 1, ceiling `MapLayers.VisionLayers - 2`; and
  `engine/OpenRA.Game/Traits/Player/MapLayers.cs:75` — `VisionLayers = 11`. **[1, 9] confirmed.**
- The bool collapse: `IsSpotted` enumerates observers, applies the asymmetry gate, `VisionCovers`
  (range + band strength vs `required = CurrentVisibility`), then the truth gate
  `self.CanBeViewedByPlayer(owner)` and `return true` (`:112-118`). **Margin discarded — confirmed.**
  Grading yellow is a `bool` becoming an `int`; red is essentially that existing bool.
