# Playtest Reports

Raw findings from playtest sessions. One file per session: `<YYMMDD_HHMM>_<topic>.md`.

## Format

Don't polish these. Bullet fragments, half-thoughts, "feels off" — all welcome. Future-Claude needs the unfiltered version more than the pretty one.

```markdown
# Playtest <YYMMDD_HHMM> — <topic>

**Build:** <git short hash>
**Focus:** what we set out to test

## Bugs
- thing X broke when Y

## Balance / feel
- tank vs tank takes forever
- artillery feels weak

## Polish
- icon hard to read at zoom-out

## Surprises
- found that X works in a way I didn't expect

## Ideas (don't act on)
- what if Y
```

## After the session

Run **TRIAGE** to process the report. Every item gets sorted into:

- `WORKSPACE/RELEASE_V1.md` (Phase A/B/C, or Pending decisions)
- `WORKSPACE/BACKLOG.md` (clearly off-scope ideas)
- `WORKSPACE/bugs/discovered.md` (incidental bugs)
- `[cut]` (won't fix)

The playtest file stays put as the historical record.
