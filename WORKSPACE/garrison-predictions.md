# Pre-registered predictions — garrison research, 2026-08-16

Written BEFORE reading GarrisonManager.cs or grepping. Scored at the end.

1. **`IsDucking` has zero readers** (the brief's claim). Confidence 55%.
   Counter-hypothesis: it is read under a different surface — a property
   wrapping the field, a `ps.` struct copied into a render/damage path, or
   a name-mangled access (`.IsDucking` on a differently-named local). A
   grep for the literal token would still catch those, so the more likely
   escape hatch is that the *value* is consumed but the effect is
   negligible, not that it is unread.

2. **Even if `IsDucking` is unread, restoring it will NOT be the top-ranked
   proposal.** Confidence 65%. A graduated internal number that the player
   cannot see is invisible whether or not it is wired. The readability
   problem is more likely at the *affordance* layer — how you enter/leave a
   garrison and what the UI says — than in the suppression curve.

3. **There is a second, larger unintuitiveness than suppression**: entering
   or exiting garrison, or target/firing-arc behaviour from inside a
   building. Confidence 60%.

4. **Something garrison-related is client-local** (a toggle, a stance, a
   selection-scoped flag) given three such bugs were found today.
   Confidence 35%.
