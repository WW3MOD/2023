# `test-escalation-banner-separate`

The far half of the §B6 pair. **Read
[`../test-escalation-banner-combined/README.md`](../test-escalation-banner-combined/README.md)** —
the mechanism, the table of the one differing quantity, the RED sabotage and the failure texts are
all there, and duplicating them here is how two documents start disagreeing.

```bash
./tools/autotest/run-test.sh --hidden test-escalation-banner-separate   # expect PASS
```

What this directory alone asserts: with the t90 at **40000 hp** it certainly survives the first
`TankRound.Abrams` round, and `BurstWait` is 130 ticks — so the 2 → 1 edge cannot land sooner than
~130 ticks after the 3 → 2 edge, twice the 66-tick banner hold, and in practice later. At that distance **two separate banners is the correct behaviour and
always was**, and this arm is what stops the §B6 fix from over-reaching into the "queueing" option
the review explicitly rejects: combining edges this far apart would draw a sentence about a finished
phase while the war is already on.

It is therefore **not a control that must fail**. Both arms of this pair are passes. The RED that
makes the combined arm mean anything is a one-line sabotage of
`DefconReadoutModel.CombinesWithPrevious`, and this arm is deliberately **unaffected** by it.
