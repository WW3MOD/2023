# Seek gate approved-but-deferred and lead-hold not-standalone: recorded as pipeline items 84/85, not built

_Recorded 2026-09-06T11:58:44.999Z by 0d084dc5_

## Context (2026-09-06, main @ 9a247a40)

Two behaviour changes surfaced by measurement were put to the user; both got a reading, neither a build.

**AutoSeekSupplies closing-provider gate on `^Soldier`** (question kJ4nXIypbX-Gm7TSDxPFJ). Options: enable on ^Soldier (humans + both bots) / bots-only wiring / leave as is. User picked **enable on ^Soldier** with the note "Do NOT build now … it is a good idea and I have thought about it so we might do it … put it in the pipeline for decision." → PIPELINE item 84; the dossier carries the worker's exact spec (Info field default false; hold when the same provider returns strictly closer; idle path only), the acceptance (clause 4 green with `[seek] leave` absent) and the human-play cost.

**Lead-hold (item 64's last mechanism)** (question aEpyOHRYwtEx0HKX9mFdT). Options: V1 throttle / defer / V2 hold / V3 echelon. User made **no pick**: "not needed as a standalone feature, but maybe it can be worked into a larger change. I have some ideas … will probably not be a focus before v1.0." → PIPELINE item 85 with the three-variant design kept for folding into that larger change.

## Why recorded rather than built
The user is archiving this chat to continue later and asked explicitly that findings not be lost. The pipeline (stubs + dossiers) is the durable home; backlog items 5292fd02 (ambush-lane share → item 86) and 5509c41b (citation drift → item 88) were moved there too, and the deaths-audit scorer choice became item 87.

## Alternatives rejected
- Building the seek gate now on the strength of "approved in principle": rejected — the note says not now, and it moves human play.
- Leaving the findings only in tracks/transcript: rejected — the user said the chat will be archived.
