# Stale NAT status falls back to the generic message rather than retrying the mapping

_Recorded 2026-08-11T21:53:10.262Z by d1c4aecd_

## Context

`Nat.ForwardStatus` is captured once, when `Server`'s constructor calls `TryForwardPort`. `MasterServerPinger.DiagnosePortForward` reads that snapshot seconds later to name a cause. If the forward attempt lost the discovery race, the snapshot stays `NoDeviceFound` permanently and the lobby asserts "No UPnP/NAT-PMP router answered" — while `ServerCreationLogic` shows the green "UPnP enabled" notice for the same session.

## Options considered

**A. Retry the forward when the status looks stale** (`ForwardStatus == NoDeviceFound && Nat.Status == Enabled`). This was the manager's first instruction.

**B. Fall back to the generic `notification-no-port-forward` string when the status looks stale.** Shipped.

**C. Add a fifth string ("mapping created just now — try hosting again") and retry.**

## Decision: B

The implementer pushed back on A and was right. The master server's verdict was formed *before* any retry could create the mapping. So a retry that SUCCEEDS flips `ForwardStatus` to `Forwarded`, and the diagnosis then asserts `notification-no-port-forward-upstream` — "your router accepted the mapping, the block is further upstream" — which is a *differently* wrong confident cause, and a worse one: it points the user at CGNAT or double-NAT when the mapping simply had not existed yet.

C would be correct but needs a fifth string and more state. Not worth it: the reviewer measured Mono.Nat 3.0.4 against the live network and found the router is discovered in **under one second**, with `StartDiscovery()` returning instantly. In the client path (`Nat.Initialize()` at launch, hosting from the menu minutes later) the race is effectively unreachable. It is only plausible in the dedicated-server path, where `Program.cs:79` is followed by just mod load and `MapCache.LoadMaps()` before `new Server(...)`.

## Principle

This change exists to stop the game stating a confident cause it cannot support. Trading one confidently-wrong message for another fails that on its own terms. The generic string plus the LAN-address detail line — which is the actually-useful payload and is unaffected by the staleness — is the honest floor.

## Revisit if

The dedicated-server path becomes a common way to host, which is where the race is real. Option C is then the right fix.
