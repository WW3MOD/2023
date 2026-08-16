# WW3MOD — store page copy

> Draft copy for a ModDB / itch.io style page. **Nothing here is published.** Publishing is the
> user's alone. Copy blocks are written to be pasted as-is.
>
> Written against `main` @ `2a9eb77d` (2026-08-17). Every claim below was checked against the
> tree; anything I could not verify without launching the game is marked **[unverified]**.
>
> **Read [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md) before publishing any of this.** There is an open
> blocker that makes the game unplayable on a clean machine. This copy describes the game that
> exists in the repo, not a game a stranger can currently install.

---

## 1. Tagline (one line)

> **There is no Construction Yard.**

Alternates, if a platform wants something less blunt:

- *A modern-war RTS where you never build a base — you call in what you can afford.*
- *No factories. No tech tree. Just what you can get to the front, and what you can afford to lose.*

The blunt one is better and should be the default. It is the same sentence the game itself opens
with on the How to Play screen, it states the single most important fact about the mod, and it
pre-empts the exact failure we know new players hit: hunting the sidebar for a construction yard
and quitting when there isn't one.

---

## 2. Short description (~60 words — ModDB summary field, itch "short description")

> WW3MOD is a modern-warfare total conversion of Command & Conquer: Red Alert. There is no base
> building. No construction yard, no factories, no tech tree. Every unit is called in from
> off-map reserves through a fixed Supply Route and walks to the front under its own power — and
> you win by cutting the enemy's link, not by levelling their base.

---

## 3. Long description

> **There is no Construction Yard.**
>
> WW3MOD is a total conversion of Command & Conquer: Red Alert into a modern conflict between
> NATO and Russia. It keeps the feel of a classic real-time strategy game — the same instant
> readability, the same mouse — and throws out the part where you spend the first four minutes
> laying out a base.
>
> **Nothing is built.** There is no construction yard, no barracks, no war factory, and no tech
> tree to climb. Anything your tech level allows can be ordered right now, from one list. What
> you spend is budget allocation, not materials.
>
> **Units are called in from off-map reserves.** What you order enters at the map edge nearest
> your Supply Route and drives or flies to its rally point. That march happens in real time, on
> the map, in front of everyone — which means your reinforcement lane is a thing the enemy can
> find and ambush, and so is theirs.
>
> **The Supply Route is your beachhead, not a factory.** You start with exactly one, fixed near
> your spawn. You cannot build it, move it, or destroy anyone else's — Supply Routes are
> indestructible. The rally point is the only part of it you set.
>
> **You win by cutting their link.** Park units inside the enemy Supply Route ring and their
> reinforcements slow, then halt, and a bar starts filling. If it fills, that side is out of the
> match. Yours works the same way — so clearing your own ring is as urgent as contesting theirs,
> and pushing them off lets it recover.
>
> The result is an RTS with no build order. The opening move is a decision about where to send
> what you already have, and the whole match is a fight over two pieces of ground that cannot be
> destroyed, only held.
>
> Spent units can Evacuate — walk back out through your Supply Route to recover what is left of
> their cost. A unit that dies takes its budget with it.

---

## 4. Feature bullets

Each of these was verified in the tree. Nothing aspirational.

- **No base building at all.** One list, one currency, no prerequisites. Your first click is a
  tactical decision, not a construction plan.
- **Reinforcements walk in.** Units arrive at the map edge and cross the map to reach you.
  Interdicting the enemy's lane is a real strategy, not a gimmick.
- **Indestructible Supply Routes, contested on foot.** The win condition is territorial control
  of a fixed point — a visible control bar, reinforcement slowdown, and a warning when yours is
  being contested.
- **Two factions:** America (NATO) and Russia (BRICS), each with their own vehicles and
  helicopters.
- **Roughly 22 vehicles**, from Humvees and BTRs up through Abrams and T-90s to Paladin, Grad,
  TOS, HIMARS and Iskander; **six rotary airframes** including Apache, Mi-28, Black Hawk, Hind
  and heavy-lift transports; and around **fifteen infantry roles** — riflemen, AT and AA
  specialists, snipers, medics, technicians, drone operators and squad leaders.
- **Infantry garrison buildings** — soldiers occupy houses, fire from the side they're actually
  standing on, and the building degrades to rubble around them.
- **Vehicle crews are people.** Crew eject from wrecks and can be picked up, and a lost commander
  is a lost capability, not just lost hit points.
- **A real order vocabulary:** attack-move, force-move and force-attack modifiers; Hold / Auto /
  Evacuate resupply stances; Tight / Loose / Spread formation cohesion; patrol waypoints.
- **Supply and ammo actually exist.** Trucks carry supply, units run dry, and rotating a spent
  unit home recovers part of its budget.
- **Eight multiplayer maps**, 2 to 6 players, plus skirmish against an AI opponent.
- **Free and open source (GPLv3)**, built on the OpenRA engine.

---

## 5. "What this is not" — put this ON the page, not in a FAQ

This section is not a disclaimer to bury. It is the highest-value block on the page, because
every line of it is a refund, a bad review or a bounce that didn't happen.

> **Before you download, know what this isn't.**
>
> - **This is not Red Alert with new tanks.** If you are looking for a construction yard and a
>   build order, this is the wrong game. Nothing here is built.
> - **There is no campaign and no missions.** WW3MOD is skirmish and multiplayer only.
> - **There is no naval combat.** Some maps have water; nothing floats on it yet.
> - **There are no fixed-wing aircraft or airstrikes yet.** Air power in this release means
>   helicopters.
> - **This is an early public release** by a very small team. Expect rough edges — see the known
>   issues list below, which we would rather you read now than discover later.
> - **You need Red Alert's data files.** WW3MOD is a mod of a 1996 game and loads that game's
>   artwork and audio. The files are not ours and are not included. See "Requirements".

---

## 6. Requirements block

> **Requires the Command & Conquer: Red Alert data files.** WW3MOD is a total conversion and
> loads artwork and audio from Red Alert's data files. Those files are not part of WW3MOD, are
> not distributed with it, and remain the property of their owners. You can supply them from an
> original disc or an existing digital install, or fetch them from a mirror of the 2008 Red Alert
> freeware release.
>
> - **Windows:** installer or portable zip. Requires the .NET runtime (not bundled).
> - **macOS:** 10.15 Catalina or newer. **The build is not signed** — macOS will refuse to open
>   it on first try. See the install notes.
> - **Linux:** AppImage.
> - **Graphics:** OpenGL 3.2 or newer.
>
> WW3MOD is free software under the GNU GPL v3. Source: https://github.com/WW3MOD/2023

The licensing wording above is deliberately consistent with `SOURCE-OFFER.txt`, which ships in
the artifact. Do not reword one without the other.

---

## 7. Screenshot shot list

I could not launch the game, so this is a request, not a deliverable. Screenshots are the single
highest-leverage thing on a store page — a stranger decides from the first image, and the hook
of this mod is visual in a way the text can only gesture at.

Shots, in priority order:

1. **The How to Play screen itself.** Unusual choice, deliberate: it is a clean four-point
   statement of the entire game model on one screen. It defuses the construction-yard problem
   before download. Make it image #2 or #3, not buried.
2. **A Supply Route being contested** — enemy units inside the ring, control bar visibly
   part-filled. This is the win condition and no other RTS screenshot looks like it. If only one
   action shot makes the page, this is it.
3. **A reinforcement column crossing open ground** from the map edge, ideally under fire. Shows
   "units walk in" better than a paragraph can.
4. **The order sidebar with no construction options** — a wide shot where a viewer familiar with
   RTS sidebars can see what's missing. Pair it with the tagline as the header image.
5. **Infantry firing out of a garrisoned building**, with the directional fire visible.
6. **A tank engagement at night or in snow** (Nuclear Winter / Siberian Pass) — pure eye candy,
   for the gallery tail.

Avoid: any shot showing the RA-era leftovers listed in `KNOWN-ISSUES.md` (a "Tesla Coil
(Destroyed)" husk or an "Ore Refinery" label in a WW3 screenshot does specific damage to the
one claim the page is making), and any shot of the garrison sidebar, which is still a debug
panel.

---

## 8. Copy that must NOT be used

Recorded so nobody reinvents it later. Each of these is either false at `2a9eb77d` or unwired:

- **"Capture enemy Supply Routes."** Designed, explicitly not wired — `SUPPLYROUTE` has no
  `Capturable` and no `CaptureManager`.
- **"Call in airstrikes."** `AirstrikePower` is commented out for v1; the A-10, F-16, Su-25 and
  MiG actors exist but are unbuildable and on no map.
- **"Naval warfare."** `naval.yaml` is entirely commented out; the faction naval files are empty
  and the Ship queue has no items.
- **"Campaign"** or **"missions"** of any kind. The missions browser is empty.
- **"Build a second Supply Route"** or anything implying more than one per player.
- **Anything describing an online community, matchmaking or a server list.** See
  `KNOWN-ISSUES.md` — the browser works and is empty.
