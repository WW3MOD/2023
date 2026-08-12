# Detectable vision syncs the visibility level, not a bool — the specced mirror of the radar sibling was wrong

_Recorded 2026-08-12T09:43:15.044Z by 9da76256_

**Context.** `Detectable.cs:149` marks `visionDetectableConditionToken` `[Sync]`. A condition token is an opaque allocation handle whose value counts how many conditions the actor has ever been granted, and `DetectableVisionChanged` revokes and re-grants on every visibility change — so the sync covers the identity of the handle rather than the gameplay state behind it, and two clients with identical state desync on differing grant counts. `Detectable` is applied from `mods/ww3mod/rules/defaults.yaml`, so this is on essentially every actor. This is the leading named cause of the four 2-human desyncs recorded in `WORKSPACE/closeout/54ab3880.md`.

**What was specced, by both the closeout analysis and the manager's brief.** Mirror the sibling at `:160-162` literally: add a synced `bool IsVisionDetectable`, drop `[Sync]` from the token. `:191-193` (counter-battery radar) follows the same shape, making vision look like the lone outlier of three.

**What the implementer found, and why it overrides the spec.** Vision's condition is level-valued, not boolean: `:157` grants `VisionDetectableConditionPrefix + CurrentVisibility`. The two siblings each grant a single fixed condition (`:177`/`:185`, `:208`/`:216`) and so are honestly boolean. A `bool` for vision would therefore be true whenever the token is valid — essentially always after the first grant — and blind to a 3-vs-7 level divergence, which is precisely the class of divergence this change exists to catch. The siblings are the pattern to copy in PRINCIPLE (sync the state, not the handle) and NOT literally.

**Options considered.** (a) `[Sync]` on the existing `int CurrentVisibility` — implementer confidence 85. (b) Drop `[Sync]` and add nothing: strictly safe, since removing a sync cannot create a false desync, but leaves vision with no coverage — the outcome commit `9b77d1fd` explicitly argued against. (c) The specced bool — implementer confidence 12, listed only because it was what was asked for.

**Taken: (a).** It preserves the principle the task is built on while covering the state that actually varies. Verified independently by the manager against the file before answering rather than accepted on the implementer's say-so.

**Gate attached to the decision.** Adding `[Sync]` to a field CREATES desyncs if that field is ever written from client-local state. `CurrentVisibility` is assigned at `:84`; the implementer was told to trace that value and confirm it derives from synced simulation state rather than anything render-side, fog/shroud-query-side or viewer-relative, and to stop and report rather than commit if it does not hold. A per-client visibility answer would legitimately differ between clients, and syncing it would convert a working game into a guaranteed desync — the one way this change makes things worse instead of better.

**Generalisable.** A trait with three parallel-looking members is not necessarily three instances of one pattern. Check the arity of what each one actually grants before copying a sibling's shape across.
