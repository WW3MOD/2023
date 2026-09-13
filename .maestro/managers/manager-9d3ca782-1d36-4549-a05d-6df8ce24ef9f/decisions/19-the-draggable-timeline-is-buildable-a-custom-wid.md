# The draggable timeline is buildable — a custom widget, zero server change

_Recorded 2026-09-10T20:21:49.851Z by ffb08fdc_

Closes the hazard flagged in decision 18, which recorded the timeline direction as ruled but not yet known to be buildable. Researched at `main @ fc1d104c`; every claim below was verified against the tree by a worker and its two sub-agents, not inferred.

## The limit is real, and it does not block us

`DefconEscalation.cs:211-213`'s comment is **accurate**. A repo-wide grep for `LobbyOption` subclasses returns exactly one hit — `LobbyBooleanOption` at `TraitsInterfaces.cs:721` — and it adds no new value type, merely hard-coding a two-entry `Values` dictionary. The only value domain the engine has is `IReadOnlyDictionary<string, string>`. **There is no integer, slider, range or numeric path anywhere in the option pipeline.**

Worse for the naive plan: an option cannot supply its own widget. `LobbyOptionsLogic` dispatches on `options[start] is LobbyBooleanOption` and then locates children **by C# type** (`child is CheckboxWidget`, `child is DropDownButtonWidget`). Swapping a `DropDownButton@A` for a custom widget in chrome yaml alone makes the dropdown queue underflow and throw. There is no third branch and no extension point.

## Why it is nonetheless feasible

The wire protocol is the text order **`option <id> <value>`**, and the server validates only `option.Values.ContainsKey(value)`. `LobbyOptionState` carries plain strings — booleans are literally `"True"`/`"False"`. **The transport does not know or care what UI produced the string.**

That is already proven by existing non-widget callers: `LobbyPresetLogic.cs:323`/`:348` and the test-mode stager at `LobbyLogic.cs:992` all drive options with no dropdown anywhere in the path.

**So a custom timeline widget that writes enumerated string values needs zero server or protocol changes.** The values stay `"10"`, `"20"`, `"30"` — the timeline is a nicer way to set values the engine already understands, and the same match stays configurable without it.

A free continuous value is firmly rejected, and by two independent gates: the server's `ContainsKey` check, and `LobbySettingsNotification.cs:39`, which does an *unchecked* `option.Values[...]` on live session state and throws `KeyNotFoundException` on client join for any out-of-set value. **Snap to enumerated stops; never emit a raw float.**

## `SliderWidget` cannot be reused as-is

It exists (`SliderWidget.cs:19`), is fully general, is yaml-settable, and is used in six places — but **zero of them are in the WW3MOD tree**. Two traps, both read in source:

1. **`Draw()` calls `UpdateValue(GetValue())` every frame** (`:118`). `GetValue` is the authority, so a naive synced port **snaps back to the server's value mid-drag**.
2. **`Ticks` is draw-only** (`:128-135`) — it renders tickmarks and does not quantize `Value`. There is an in-source TODO at `:74`: *"handle snapping to ticks properly again"*. **Marker snapping does not exist and must be written.**

## What must actually be built

1. **`TimelineWidget : InputWidget`** in `engine/OpenRA.Mods.Common/Widgets/` — WW3MOD ships no assembly of its own (`Assemblies:` lists only the two engine DLLs), so a new widget is an engine-tree source change. Three WW3MOD-specific widgets already live there, so this is the established pattern. Needs `HandleMouseInput` with focus take/yield modelled on `SliderWidget.cs:58-86`, a `Draw`, **a copy-constructor and `Clone()` override** (`Widget.cs:256-259` throws otherwise), and snapping from scratch.
2. **A new `ChromeLogic`** to bind it. It cannot live inside `LobbyOptionsLogic`; the realistic shape is a sibling panel owning the relevant option ids and writing them directly.
3. **Chrome yaml + a `ChromeLayout` entry.** The pre-game panel currently uses the *stock* `common|chrome/lobby-players.yaml`, so hosting the timeline means forking that file into `mods/ww3mod/chrome/` or overlaying a container.
4. **Drag-vs-network reconciliation** — the real design problem, not the widget. **Issue the order on mouse-up, never on drag-move**: every accepted order resets all clients to `NotReady` and emits a chat line. Suppress the `GetValue` readback until mouse-up, or predict as the checkbox does (`LobbyOptionsLogic.cs:541` — note the dropdown does *not* predict).

Registration itself is free: reflection maps yaml `Timeline@FOO` → `TimelineWidget` across all loaded assemblies (`WidgetLoader.cs:82-86`), so no registry edit.

## The fallback stays valid

If the widget work is deferred, the tab-1 dropdown panel already drawn and agreed configures exactly the same match, because the underlying options are enumerated strings either way. **The timeline is a presentation upgrade, not a prerequisite** — which is the property that makes it safe to commit to.
