# The four lobby defaults, ruled — and Doomsday becomes Dead Hand

_Recorded 2026-09-10T19:58:13.368Z by ffb08fdc_

All four answered by the user against the drawn defaults; two went the agent's way and two did not. Recorded together because they interlock, and because one of them reverses an earlier rename that a future session would otherwise "fix" back.

## 1. First warheads: 10 minutes — NOT the agent's recommendation

The agent drew 30 minutes (the user's own number from the exchange proposal) and rated it 78. The user chose **10 minutes**, the number from the superseded timed ladder.

The consequence is a genuinely different mode from the one drawn: nuclear weapons become a **mid-game tool** rather than a late development. A conventional game no longer gets thirty minutes to resolve itself before the exchange is available. Given the exchange's own logic — firing is what a losing player does — an earlier unlock means the rubber band engages sooner and more often, so more matches will actually reach the mechanic. That is a defensible reading and it is the user's call; the agent's 30-minute argument was about protecting the conventional game, which the user evidently weights lower.

**Read as 10 minutes after DEFCON 1, not after match start.** The question flagged that the two are different clocks and did not force a choice. The agent has drawn it as from-DEFCON-1 on two grounds: it is what ships (`NuclearReleaseDelayTicks` counts from DEFCON 1 and its default is 10000 ticks = exactly 10:00 at 16.67 ticks/s, so the ruling lands precisely on the existing default and needs no code change at all), and from-match-start with a 5:00 no-rush would put warheads five minutes after the line lifts, which is far more aggressive than anything discussed. Flagged to the user as the reading taken; it is one field if wrong.

## 2. No buying in Escalation — as recommended

Every warhead in an Escalation match is granted by somebody's decision to fire. Countability survives: both sides can count what is on the map, which is what makes the decision to fire a real one. Skirmish serves the buy-your-own audience, which is why this could be refused cleanly instead of compromised into an off-by-default toggle.

## 3. Game-enders are NEVER purchasable in Skirmish — stricter than recommended

The agent recommended offering them in the Highest yield dropdown but defaulting below (rated 72); the user chose the stricter rule (rated 58). The Skirmish schedule now stops at 100 kt with no host override.

The effect is that **game-enders keep their meaning**: they are the thing that ends matches, and they exist only in Escalation, or via a Dead Hand time limit the host set deliberately in the lobby. No player buys an apocalypse at minute forty. This is the better rule and the agent under-rated it — the option's existence was the only thing arguing for it, and "someone will pick it and complain" was already listed as its own con.

## 4. Three aim points, not three missiles — as recommended

"3 each" means three 750 kt impacts. One Sarmat launch covers a side's whole allocation, and the fifteen-second window is spent clicking three aim points — the interaction `NukeSarmatMIRV` already has, since it asks for six. A medium map gets 6 impacts total rather than 36.

Still to build: a **partial salvo**. The weapon fires six today; the allocation will frequently be fewer. That is the one piece of new firing behaviour this ruling creates.

## 5. Sidenote, and it reverses an earlier rename: "Doomsday" → "Dead Hand"

The user's note: *"Change the name 'Doomsday' to 'Dead hand'."*

This matters more than a label because the mod already went the other way once. `DoomsdayStrike`'s own `[Desc]` opens `DOOMSDAY / "Dead Hand"`, and the trait was introduced as the replacement for a plain time-limited game. **The player-facing name is now Dead Hand.** A future session finding "Doomsday" in the code and "Dead Hand" in the lobby must not reconcile them by renaming the lobby back.

Cheapest correct scope: change the lobby-facing strings (`DoomsdayLabel`, `DoomsdayDescription`) and any UI copy. Renaming the trait, its file and its option id is a wider change with no player-visible benefit, and can wait for a moment when that file is being touched anyway.
