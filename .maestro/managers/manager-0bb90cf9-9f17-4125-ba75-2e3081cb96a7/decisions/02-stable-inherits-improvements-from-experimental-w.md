# @stable inherits improvements from @experimental work; never gate it off deliberately

_Recorded 2026-08-08T07:32:20.362Z by bdedd544_

**Settled by the user, after the agent asked once too often.** Recorded here and as a hard rule in `CLAUDE.md` (commit `875c93c1`) so it does not recur.

**The user's words:** *"if a fix improves the Stable bot that is fine, document this so we dont have this discussion again. We dont improve the stable bot directly, but if it is improved from work on improving the experimental bot that is fine, we dont gate the stable from them if it requires something extra to do so."*

**The policy:**
- Working on `@stable` directly is NOT a goal.
- Work aimed at `@experimental` that also improves `@stable` — let it through.
- **Never spend extra effort building a gate whose only purpose is to withhold a fix from `@stable`.**
- Do not ask per-change. It is settled.

**Why the agent asked, and why that was still wrong.** `DOCS/reference/architecture.md` documents `@stable` as the *frozen validated snapshot used as a benchmark control*, with a standing rule that a new behavioural Info field on a shared trait must default to baseline and be opted into per-profile. The agent read "frozen benchmark control" as "changing it needs sign-off." That over-generalised.

**The distinction that actually matters — and it is about SILENCE, not about change:**
- The architecture rule guards against **accidental, unnoticed** drift: a non-baseline code default silently mutating the control while its YAML is untouched. Still binding.
- The user's policy governs **deliberate, visible** improvement. Fine, and not to be gated.

So both hold at once: default new fields to baseline so nothing moves unnoticed; but once an improvement is real and would naturally apply, do not build machinery to keep it out. Added obligation: a commit that DOES change `@stable` behaviour must say so, so the next benchmark baseline is re-taken knowingly rather than being silently invalidated.

**Concrete consequence for the item in flight:** the transport capacity/carrier win (`MaxPassengersPerLoad` 5→12, humvee/BTR as `CarrierTypes`, `TransportMissionSlots` on `@stable` where lift is currently starved to zero) is no longer a question. It lands where it naturally applies. The only remaining judgement is sequencing, which is the agent's to make — it will land after the purpose fix so the next live match can attribute what changed.
