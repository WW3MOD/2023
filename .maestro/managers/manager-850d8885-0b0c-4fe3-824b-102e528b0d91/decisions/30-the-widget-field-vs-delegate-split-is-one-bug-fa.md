# The widget field-vs-delegate split is one bug family with four instances, and cloning is only its most common cause

_Recorded 2026-08-30T19:28:35.278Z by 17dc66e4_

## The pattern

Four separate defects today, all the same shape, none caught by a build or a test suite:

1. **`LabelWidget.GetText`** shared with the template through the copy constructor → the typed tooltip rendered an **entirely blank panel**, past a clean build and 2032 green tests.
2. **The lobby ACTIVE CHANGES chip** frozen at the template's 180px — a cloned `ColorBlock` reading the template's width.
3. **`ColorBlockWidget.GetColor`** copied while `Color` is not, and the base constructor's `GetColor = () => Color` closing over the template instance → a cloned `ColorBlock` draws the template's colour with its own `Color` left at `default`. Flagged by the info-panel worker as the most likely fault in its own work.
4. **`TestModeScreenshots.FindVisible`** testing the raw `Visible` field where `DrawOuter` and `HandleMouseInput` both use `IsVisible()`. Tab containers are `Visible: False` in YAML and switched on by `GameInfoLogic` assigning `IsVisible = () => true`, so the screenshot harness had been **walking past every tab button in the Esc menu**.

## The correction to my own framing

I initially had this written up as a **cloning** trap, and instructed a worker to capture it that way. Instance 4 has no clone in it at all — it is a widget whose delegate was assigned at runtime by logic. The framing was too narrow and would have left the next agent unable to recognise the fourth case as the same thing.

The generalisation that actually covers all four: **a widget's backing field and the delegate that reads it can disagree, and the engine consults the DELEGATE.** Cloning is the most common way they come apart, because the copy constructor carries the delegate while the field is re-initialised. Runtime assignment of `IsVisible` / `GetText` / `GetColor` by panel logic is a second, independent way.

## Why this keeps costing whole sessions

Every instance survived the full gate. A build cannot see it, NUnit cannot see it, and `--check-yaml` cannot see it — the code is correct C# and the YAML is valid. **The only detector is looking at the rendered frame**, which is exactly the step that is scarce here because launches serialize and need a granted slot.

Operational consequence, and the reason this is a decision rather than a note: **any task touching a templated, cloned, or logic-driven widget must budget a screenshot slot up front, not treat one as a contingency if something looks wrong.** Two of these four were found only because a capture happened; instance 1 was found on a first launch that nobody had planned as verification. Treating the render as optional is what makes this family expensive.

## Filed

Widened `DISCOVERIES.md` entry commissioned from worker `69ab1b3f`, keeping all four as instances under the broader rule. Destination on promotion is `DOCS/reference/architecture.md` §"Widget gotchas", which already holds two of the four.
