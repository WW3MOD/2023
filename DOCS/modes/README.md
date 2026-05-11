# MODES

Operating contexts I (Claude) work in. One mode at a time. Default is **RELEASE**.

A mode is the *stance* for a session — what scope is in play, how loose or strict the bar is, how commits map to trackers. It's set once and persists until switched.

> **Note for the agent.** These are project-convention docs, **not** harness-registered Skills. When the user says the trigger word, READ the relevant `.md` here and follow the procedure. Never call the `Skill` tool for these — that's a different system.

| Trigger | Mode | One-liner |
|---|---|---|
| [`RELEASE`](RELEASE.md) | Release mode (default) | v1 methodology — scope-locked, phase-driven, every commit moves a status |
| [`EXPERIMENTAL`](EXPERIMENTAL.md) | Free exploration | Outside v1 scope — looser, idea-friendly |

For one-shot workflows (DEMO, AUTOTEST, REVIEW, etc.) see [`../recipes/`](../recipes/README.md).
