# Runway goes to first-session chrome, because it is the only queue option that ships without simulation slots

_Recorded 2026-09-05T14:53:37.903Z by e0a0826c_

The user was asked where the surplus runway should go and answered *"You decide I dont know."* Recording the call and its reasoning, because the reasoning is entirely about a constraint that did not exist when the question was written and will not exist forever.

## The four options as posed

1. The 74–83 safe-wins batch — ten small independent player-facing gaps.
2. Visible bot stupidity — items 64 and 56, ranked HIGH by the release audit.
3. First-session chrome — lobby AI ladder and descriptions, branding residue, empty art and audio slots.
4. Item 42, the multiplayer desync — the declared hard blocker.

## What changed between asking and deciding

The question was framed for a window with roughly 34 hours of budget, five worker slots and free rein to launch simulations. By the time it was answered the daemon had crashed twice, the machine had been rebooted, and Root had capped this session at **two concurrent workers, woken one at a time, with game launches hard-frozen** pending an explicit simulation budget.

That inverts the ranking, because three of the four options are launch-bound or parallelism-bound:

- **The safe-wins batch loses its whole advantage.** Its case was that it parallelises across four or five workers immediately. At a cap of two, ten sequential items is simply a long queue, and several of them (76, 79, 82) are balance swings whose dossiers say outright they must be measured rather than reasoned.
- **Bot stupidity is measurement-shaped end to end.** Items 64 and 56 cannot be signed off by reading; both want benchmark runs that are exactly what is frozen. Starting them now manufactures a backlog of runs against a budget that does not exist yet.
- **Item 42 needs two humans**, which no constraint change makes available.
- **First-session chrome needs no runs at all.** Naming, descriptions, fluent strings and asset slots are verifiable by reading and by the YAML gate, which is a single serial manager-run rather than a game launch.

## The decision

**First-session chrome takes the runway.** It is the only option that converts a constrained window into shippable work rather than into queued verification debt.

Two things make it better than a default rather than merely available:

- The audit already ruled the audience is a **public release to strangers**, which promotes first-impression defects from polish to blocker. This is that class of work, by that ruling.
- The user, answering a *different* question in the same batch, volunteered that `@experimental` / `@stable` should be renamed before release — *"Maybe we should call them Staging and Main? Or something that is harder to misunderstand?"* Release blocker **R4** is already open on exactly that surface: the lobby AI picker has real names now but still no difficulty ladder and no descriptions (`grep -c 'Difficulty\|Description' rules/ai/ai.yaml` returns 0). The rename and R4 are one change to one file, not two pieces of work, and doing them apart would touch the same lines twice.

## What this decision is NOT

It does not re-rank the queue permanently and it does not demote the safe-wins batch or the bot items on their merits. It is a claim about **sequencing under a launch freeze**. When simulation slots are budgeted, items 64, 56 and the measured swings should be re-evaluated on their original ranking, which put them above chrome. Item 76 is already built-and-parked for exactly that moment.

## Alternative considered and rejected

Starting the safe-wins batch anyway and letting verification debt accumulate until slots open. Rejected because the same pattern is the queue's most expensive recurring defect in its own retrospectives — work that is well-reviewed but never measured, shipped on arithmetic. The first autoburn window's stated failure mode was precisely this, and the correction recorded then was *"measurement is the product."* Choosing launch-free work is that correction applied, rather than dodged.
