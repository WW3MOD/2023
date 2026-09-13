# Escalation ladders are per pair of SIDES, and the ceiling is highest-yield-fired plus one

_Recorded 2026-09-10T19:05:25.177Z by ffb08fdc_

Recommended, not yet ruled by the user. Recorded because a future session asked "how does the exchange model work in 3v3" would otherwise re-derive it from scratch, and because two of the three findings below are spec holes rather than preferences.

## The problem

The user's exchange model — firing grants the next yield up to "the enemy", never to the launcher — is specified for 1v1 and does not state who "the enemy" is with more than two players. The naive readings all break, and they break in a way that gets worse as the lobby gets bigger.

**Grant to every hostile PLAYER.** One launch in 3v3 arms three enemies; in a 6-player FFA it arms five. The cost of firing scales with the enemy count while the benefit — one strike — does not. So the model becomes monotonically more deterrent-heavy with player count until, somewhere around 3v3, firing is simply never correct. The mode would work in 1v1 and be scenery in every larger format.

**Grant only to the player you hit.** Fixes the scaling but creates a target-selection exploit: you always nuke the weakest enemy, because choosing your victim is choosing whom you arm. It also inverts normal targeting logic — the nuke goes to the least threatening player — which feels wrong at the table.

**Grant to everyone.** Arms your own allies; incoherent.

## The decision

**A ladder exists per unordered pair of SIDES, where a side is a team, or a lone player in a free-for-all.** Firing raises the enemy side's ceiling on that pair's ladder only. Other pairs are untouched.

Two-part state, and keeping them separate is what makes it work:

- **Ceiling** is per side-pair — the largest yield that side may use against you.
- **Stock** is per player — the warheads actually held.

An escalation raises the enemy side's ceiling by one and grants **one** warhead to **the player who was hit**.

## Why this is right

- **Scale-invariant.** 2v2, 3v3 and 2v1 all behave exactly like 1v1: one launch, one enemy escalation. The mechanic the user designed survives at every team size unchanged.
- **Kills the FFA vulture.** Under a global ladder, two players exchanging strikes arm every bystander for free, so the correct FFA play is to never engage. Pairwise means a bystander who stays out gains no weapons at all — their only advantage is that others are damaging each other, which is ordinary multi-team politics and not a nuclear exploit.
- **Arms the man being overrun.** Because the warhead goes to the victim rather than the side, a team's arsenal distributes itself toward whoever is under pressure. That is the comeback mechanic the user wants, operating inside a team without any extra rule.
- **Keeps alliance entanglement.** A teammate who fires raises the enemy ceiling against the whole team. That is the most thematically true consequence in the design (it is how Article 5 works) and it is kept deliberately — mitigated by information, not by a vote: an announcement to your own side, and a confirmation that says what the launch will do to your allies.

## The second rule, which is a spec hole rather than a preference

**The enemy's ceiling becomes one above the HIGHEST yield you have ever fired at them — not one per launch.**

Counting launches lets five 1 kt strikes hand the enemy game-enders, which nobody intends. Highest-fired-plus-one also reproduces the user's own sentence exactly: firing the small one and then the larger one does escalate twice, because the highest keeps rising. Repeating a yield you have already used escalates nothing further.

Corollary: grant one warhead **per escalation step**, not per strike, or repeat strikes at a level already reached become a warhead pump for the victim.

## The exception at the top

Everything below the top rung is pairwise. **The game-enders are global**: once any side reaches and fires one, every surviving player is armed with theirs for the 15-second reply window.

Without the exception, a player who never escalated has no ender, so the user's described ending — everyone picks targets, all of it lands together — silently does not happen for most of the lobby. With it, the apocalypse is the one thing nobody can opt out of, which is both the drama and the moral.

## Rejected

**A match clock to force the spiral.** The prior session argued the model needs one because two good players would otherwise never fire. The user's counter is correct and the argument is withdrawn: a nuke is a comeback tool, so the trailing player always has a reason to fire and the leader always has a reason not to. A deterrent that is never used has not failed. The only clock the mode needs is the opening interval that stops matches beginning with a nuclear exchange, which the design already has.
