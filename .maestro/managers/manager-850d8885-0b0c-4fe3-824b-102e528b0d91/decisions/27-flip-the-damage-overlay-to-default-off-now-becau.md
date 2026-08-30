# Flip the damage overlay to default-off now, because main is pushed and the user play-tests from it

_Recorded 2026-08-30T06:46:54.444Z by 17dc66e4_

## The call

`DebugVisualizations.DamageNumbers` was built to ship **default TRUE**, protected by release blocker R17 and a bidirectional test (deleting the blocker entry fails the build; flipping the default without deleting it also fails). I have told the implementer to flip it to **default FALSE** before merge, keep the checkbox, and reconcile R17.

## Why this is a sequencing change and NOT a reversal of the prior design

The instruction that created R17 said plainly the overlay *must not ship default-on*. The blocker was the mechanism for **deferring** the flip while work accumulated locally — it was never an argument for keeping default-on. Two circumstances have since changed and both point the same way:

1. **`main` is now pushed as work lands** (rule superseded 2026-08-11: the manager pushes after a verified merge), and the user play-tests **from a different machine**. So merging default-on means the user's next `git pull` puts floating damage numbers over every unit that takes a hit. That is not a deferred cost; it is immediate.

2. **The user has just defined the closing task of this arc** as one launch to *"experience the whole game experience, even menus etc"* and file a polish list. A debug overlay on by default corrupts exactly that pass — they would be filing polish items against a debug build.

Default-on was buying developer convenience. The checkbox buys the same thing at one click. Nothing is lost that matters.

## The mechanism has to move with it

The bidirectional lock asserts the R17 entry exists **iff** the default is true. So flipping the default *without* touching the entry will fail the build — that is the lock working correctly, and it must be reconciled rather than defeated. Either R17 retires (a default-off overlay behind a checkbox is not a release blocker) or it is repointed at whatever still needs blocking.

## Escape hatch offered

The implementer was told: if default-off meaningfully degrades the **detector's** usefulness — as distinct from the overlay's convenience — say so rather than comply, and it goes to the user. The `hitcheck.log` channel and the anomaly banner are unaffected either way.

## Related, recorded here because it is the third instance in one morning

The same branch's mutation audit found that **commenting the detector out of `DamageWarhead` entirely left all eleven pins green.** Three independent branches today (cursor-honesty, truck-refills-lc, battle-feedback) each had a test suite that pinned a pure helper while every real defect lived in what the call sites *supplied* to it. Two were caught by adversarial review; this one was caught by the worker actually running its mutations instead of reasoning about them.

**The remedy that worked twice: make the missing input a REQUIRED parameter of the shared helper**, so no call site can omit it and the compiler enforces what the tests did not. The remedy for the wiring seam itself was a source scan over the call site — brittle to renames, but its false failure is loud and instantly diagnosable, whereas the failure it replaces is a detector that has quietly not existed for months. Worth promoting to `DOCS/reference/` at the next curation pass.
