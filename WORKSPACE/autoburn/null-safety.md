# auto/null-safety — autoburn 260520

## Status

SALVAGED — original worker was killed when the Maestro daemon was terminated under CPU pressure. The 1 commit below is clean and shipped; this report is the conductor's post-mortem.

## Commits

- `53d4ad72` — `null-safety: AutoTarget: guard self.Owner.FrozenActorLayer in ChooseTarget`
  - `Player.FrozenActorLayer` is `TraitOrDefault` → can be null (Neutral player, or any player whose YAML omits the trait).
  - Two other engine call sites guard the same field explicitly (`Network/Order.cs:138`, `SupportPowerBotModule.cs:156`); `AutoTarget.ChooseTarget` did not.
  - **Strong evidence:** inconsistent guarding across known-equivalent call sites — the methodology required by the original prompt.
  - **Exposure path is WW3MOD-specific:** AutoTarget recently dropped its `Requires<AttackBaseInfo>` so weaponless actors (e.g. a TRUK from a captured supply truck transferred to Neutral via `WinState`) can host AutoTarget and tick ChooseTarget.

## Verification

No "build verified" note in commit. User should confirm with `make all` or `dotnet build`.

## Skipped / not done

Original prompt scoped 3-8 surgical fixes. Worker shipped 1 and was killed mid-survey. More inconsistent-guarding patterns likely remain — the cross-site grep methodology in the prompt still applies.

## Files touched

```
engine/OpenRA.Mods.Common/Traits/AutoTarget.cs
```
