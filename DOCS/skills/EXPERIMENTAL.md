# EXPERIMENTAL — free exploration mode

**Trigger:** `EXPERIMENTAL` to switch out of RELEASE mode.

**Gives you:** room to prototype, learn a system, or chase ideas that aren't committed to v1. Less ceremony, no scope-locking, less tracker bookkeeping.

**When *not* to use it:** when working on something that should land in v1 — switch back to RELEASE.

---

## What's different from RELEASE

- Ideas don't need to fit v1 scope.
- Don't update `WORKSPACE/RELEASE_V1.md` unless something genuinely lands and graduates back into release work.
- Free to break things temporarily — but still: never commit broken code that blocks the user's next launch.
- `TRIAGE` and `PLAYTEST` are optional, not the default loop.
- `AUTOTEST` is still recommended for behavioral verification — works the same in any mode.
- Commit cadence still matters — small commits with clear messages so abandoned experiments are easy to identify and roll back.

## When this mode fits

- Trying an alternative architecture for a system already in code.
- Brainstorming or prototyping a v1.1+ feature.
- Learning unfamiliar engine code by writing throwaway examples.
- One-off investigation that's not tied to a tracker item.

## Switching back

Type `RELEASE` to return. Or just start working on a v1 item — context will make it clear and I'll re-anchor.
