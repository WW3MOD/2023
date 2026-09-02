# Economy-loop audit — is the loop legible, and does it close?

**Read against `main @ 6a7e1839`** ("Merge wt/frozen-capture…"), in worktree `wt/economy-loop` cut from that
commit. Read-only: no game launched, no validator run, no build. Every `file:line` below was opened and
quoted; every "does not exist" line is a grep I ran, with its command.

---

## Headline

**The buy side of this economy is well instrumented. The sell side is not instrumented at all.**

A player can hover the cash counter and read a full income/upkeep/net breakdown; hover a production icon
and read the unit's cost, its per-weapon refill price and a grand ammo total. Then they select a damaged,
half-spent tank, press `E`, and the game asks them to make the single decision the whole economy is built
around — *is this unit worth more to me alive or banked?* — **with no number anywhere on screen.**

The number is not hard to get. It is one function call, and the formatting code already exists.

Two things I expected to find and did **not** — recorded so nobody re-proposes them:

- **The income/upkeep readout already exists and works.** `IngameCashCounterLogic.GetBreakdownText`
  builds `--- INCOME ---` / `--- UPKEEP ---` / `Net: ±$N / interval`, grouped by actor type, and
  `SimpleTooltipLogic` splits on `\n` and grows the widget to fit
  (`SimpleTooltipLogic.cs:39-56`). The commented-out `# LabelWithTooltip@UPKEEP` at
  `mods/ww3mod/chrome/ingame-player.yaml:1344-1359` is a *second*, redundant surface — not the only one.
- **Kill bounties are a live lobby dropdown**, not deferred work. `LobbyPrerequisiteDropdown@GlobalBounty`
  at `mods/ww3mod/rules/player.yaml:188-197`, `Default: 0`, wired to `^GlobalBounty`
  (`defaults.yaml:1039-1044`) which all four unit families inherit. `RELEASE_V1.md:171` lists
  "kill bounties" under deferred lobby dropdowns; that line is stale.

---

# TIER 1 — SAFE WINS

Ranked. **S1 first** — see "What I would do first" at the end.

---

## S1. Put a price on the Evacuate button

**Tier: SAFE WIN**

### What the player experiences

You select a beaten-up Abrams that has fired most of its rounds. The Evacuate button now reads a value,
and hovering it says **"Evacuate — recover $1,240 of $2,500"**. Select four units and it totals them.
Right-clicking your own Supply Route — the mouse route to the same order — shows the same figure on the
cursor. You can finally answer "one more fight, or bank it?" without guessing.

### Why it is worth doing

Rotating a unit out for its budget back is *the* verb that makes this game not-Red-Alert. It is
irreversible, it is bound to a single keypress, and today it is played blind. Every other number in this
economy is already exposed; this is the one that is missing, and it is the one that decides the match.

### Mechanism

The value is one call. `CustomSellValueExts.GetEvacuationRefund` (`CustomSellValue.cs:86-89`) returns
`GetSellValue()` with the owner's handicap applied, and `GetSellValue` (`:28-53`) is pure over
`ActorInfo` + current ammo + current supply:

```csharp
var missingBatches = missingRounds / batchSize;
missingAmmoValue += missingBatches * pool.Info.SupplyValue;      // CustomSellValue.cs:44-45
...
return System.Math.Max(0, baseValue - missingAmmoValue);          // :53
```

The **formatter already exists**, one file over — `Sellable.TooltipText`:

```csharp
var refund = (int)(sellValue * info.RefundPercent * hp / (100 * maxHP));
return "Refund: $" + refund;                                      // Sellable.cs:124-127
```

…but it is gated on `self.World.OrderGenerator is SellOrderGenerator` (`Sellable.cs:110`), and **no unit
carries `Sellable`**. All seven live declarations are buildings:
`structures.yaml:135`, `:437`, `structures-defenses.yaml:62, 86, 192, 288, 533` (the three in
`naval.yaml:58, 113, 179` are commented out).

The button to hang it on already exists, with a tooltip that promises the number in prose and does not
deliver it:

```
Button@EVACUATE:                                    # ingame-player.yaml:314
    Key: Evacuate                                   # :322  → `Evacuate: E`, game.yaml:172
    TooltipText: Evacuate                           # :325
    TooltipDesc: Selected units leave the battlefield via the map edge,\n
                 refunding their value to your budget. …    # :326
```

Wired at `CommandBarLogic.cs:251-261`.

### The honest risk

Three real ones. (1) **The preview will drift from the payout.** `DeliversCash.GoDonateCash` freezes the
ammo term at *order* time (`DeliversCash.cs:96`, passed into `new RotateToEdge(self, true, amount)` at
`:130`) while the HP term is applied at *arrival* (`RotateToEdge.cs:449-452`). So a unit shot on the walk
home pays less than the preview said. Either say "at this instant" in the copy, or re-derive at arrival —
do not silently show a number the game will not honour. (2) Multi-select needs an aggregation rule and a
mixed-selection rule (what if half the selection cannot evacuate?). (3) `RefundPercent` applies on the
`Sellable` path but **not** on the rotation path, which uses `fixedRefund` — reusing `Sellable`'s
formatter verbatim would apply a percentage that the rotation path does not.

### Proof it does not already exist

```
$ grep -rn "GetSellValue\|GetEvacuationRefund" engine/OpenRA.Mods.Common/Widgets/
(no matches)
```

Zero. Every one of the ~30 engine call sites is in `Traits/`, `Activities/` or a bot module. **Nothing in
the UI layer has ever read what a fielded unit is worth.** The only place the player ever sees the figure
is floating text *after* the unit has already left the map (`RotateToEdge.cs:473-476`) — and the comment
there shows the authors already care about this feedback: *"'+$0' is an answer; silence is
indistinguishable from a bug."* The gap is that the answer arrives after the question is unanswerable.

Not in `PIPELINE.md` (R11 is the *production* tooltip's ammo total, closed at `db2b2fa6`). Adjacent to
`RELEASE_V1.md:49` "Verify unit sell value at different ammo levels", which is a **verification sweep of
the arithmetic** — this is about **displaying** it, and the sweep would be far easier to run with a
readout in place.

---

## S2. The onboarding panel never mentions the dominant source of income

**Tier: SAFE WIN**

### What the player experiences

The How To Play panel teaches four things and a footer. None of them is *"capture the oil derricks."* On
nine of ten shipped maps, derricks are worth more than the entire passive income stream, and a new player
is told nothing — they will play their first match on the trickle and lose to anyone who took the map.

### Why it is worth doing

This is the largest teaching gap in the game, and it is text. The panel is otherwise good: it explicitly
frames the model as *"four points"* and covers no-factories, off-map reinforcement, the beachhead, and
the contestation win condition. It just omits where the money comes from.

### Mechanism — the arithmetic that makes this the dominant source

Passive income is **100 per interval** (`PlayerResources.cs:63-69` defaults; `player.yaml:166-171` leaves
every override commented out), paid on `PassiveIncomeInterval` = **50 ticks**:

```csharp
if (self.Owner.Playable || (self.Owner.IsBot && !self.Owner.NonCombatant))
    ChangeCash(PassiveIncomeAmount + (int)TotalBuildingIncome - (int)Upkeep);   // PlayerResources.cs:208-209
```

Three neutral actors register into `TotalBuildingIncome` via `CashTrickler.Register` →
`resources.AddIncome` (`CashTrickler.cs:127`, adding at `PlayerResources.cs:338`), all in
`rules/ingame/structures-neutral.yaml`: **OILB `Amount: 50`** (`:19-20`), **FCOM `Amount: 100`**
(`:51-52`), **BIO `Amount: 150`** (`:83-84`).

So **one oil derrick is half your entire base income.** Counts I ran myself
(`grep -cE '^\tActor[0-9]+: (oilb|fcom|bio)$' <map>/map.yaml`):

| map | income buildings |
|---|---|
| x-lake-ww3 | 17 |
| polar-disorder-ww3 / river-zeta-ww3 | 12 |
| twin-rivers-ww3 | 10 |
| woodland-warfare-ww3 | 9 |
| nuclear-winter-ww3 | 8 |
| seventh-woods-ww3 | 6 |
| siberian-pass-ww3 | 4 |
| arena-tank-duel / shellmap-open-field | 0 (dev maps) |

A technician costs **250** (`infantry.yaml:2381-2382`) and is consumed on capture
(`ConsumedByCapture: true`, `infantry.yaml:860`). A derrick repays that in **five intervals**. There is
**no cap on how many a human may buy** — `grep -rn "BuildLimit" mods/ww3mod/rules/` returns only
`old.yaml:68` and `:76` (both on `MCV.ai`, prerequisite `~aitoodumb`) plus two commented lines in
`mcvs.yaml`; the `UnitLimits: tecn: 3` figure that circulates is `UnitBuilderBotModule` config
(`ai-america.yaml:41`, inside a block gated `RequiresCondition: enable-ai-experimental && player.nato`)
and binds bots only.

### The honest risk

The panel is already dense — four headed blocks plus a footer, laid out with hardcoded `Y:` offsets from
`54` to `396` (`ingame-info-howtoplay.yaml:19-151`). A fifth block means re-flowing every offset below it,
or replacing the weakest existing block. It is fiddly rather than hard, and it wants a screenshot pass
(`DOCS/recipes/SCREENSHOT.md`) that I could not run.

### Proof it does not already exist

```
$ grep -c -i "captur\|derrick\|technician\|oil\|income" mods/ww3mod/chrome/ingame-info-howtoplay.yaml
0
```

Zero mentions of any of those five words in the entire panel. I read the file end to end (151 lines) to
confirm the grep is not missing a paraphrase: the four headings are *"No factories. No tech tree."*,
*"Units are called in from off-map reserves."*, *"The Supply Route is your beachhead, not a factory."*,
*"You win by cutting their link, not by levelling their base."*, and the footer is the Evacuate line.

Not queued. `PIPELINE.md` R9 concerns this same file but is strictly about the **contestation
overstatement** at `:123`/`:130` ("that side is out of the match") — a different sentence and a different
defect. Fixing R9 does not touch this.

---

## S3. Upkeep is invisible at the moment you decide to spend, and it sets an army ceiling nobody states

**Tier: SAFE WIN**

### What the player experiences

The production tooltip gains one row: **"Upkeep — $12 / interval"**. You learn what a tank costs to *own*
before you buy it, not after. Right now you can only discover upkeep by hovering the cash counter, which
requires already owning the units.

### Why it is worth doing

The numbers hide a hard ceiling that is almost certainly deliberate and is never stated. Every infantryman
and every vehicle carries `InfersUpkeep: PermilleCost: 5` (`vehicles.yaml:144-145`,
`infantry.yaml:154-155`), and `InfersUpkeep.Cost` computes

```csharp
cost += self.Info.TraitInfoOrDefault<ValuedInfo>().Cost * (float)info.PermilleCost / 1000;   // InfersUpkeep.cs:47
```

— 0.5% of unit cost, charged on the *same* 50-tick line as income (`PlayerResources.cs:209`, above). So
passive income alone sustains exactly **20,000 of fielded army value** (100 ÷ 0.005), which is exactly
`DefaultCash` 20000 (`PlayerResources.cs:32`; the mod leaves it commented at `player.yaml:167`). Past that
line you *must* take the map. That is an elegant piece of design and the game never says it out loud.

### Mechanism

The extension point already exists and is already used twice. `ProductionTooltipLogic` collects every
`IProvideTooltipDescription` contributor by priority (`ProductionTooltipLogic.cs:429-437`), which is how
`AmmoPoolInfo` gets its per-weapon refill rows in. `InfersUpkeepInfo` is a plain
`TraitInfo` (`InfersUpkeep.cs:18`) and implements nothing — adding the interface plus a one-row
`ProvideTooltipDescription` is the whole change.

### The honest risk

The tooltip is already long on multi-pool units, and there is a real question of whether the row should
read "per interval" (accurate, opaque — an interval is 3 s at 16.67 tps) or "per minute" (legible,
derived). Pick one and be consistent with the cash tooltip, which currently says `/ interval`
(`IngameCashCounterLogic.cs:98`). **Also note the ceiling arithmetic above is mine, derived from YAML and
the timestep — it is not measured**, and a match would confirm it cheaply.

### Proof it does not already exist

```
$ grep -rn "InfersUpkeep\|Upkeep" engine/OpenRA.Mods.Common/Widgets/
engine/.../IngameCashCounterLogic.cs:75:  var upkeepByType = playerResources.UpkeepEntries
engine/.../IngameCashCounterLogic.cs:93:  lines.Append("Total: -$" + (int)playerResources.Upkeep + "\n");
```

Two hits, both in the cash counter's post-hoc breakdown, both aggregate-by-type. The per-unit figure
appears in no tooltip and on no sidebar icon. `ProductionTooltipLogic` renders name, hotkey, requires
(never shown in practice — every shipped `Prerequisites:` is all-`~` and is filtered at `:104-105`),
description rows, power (hidden, `:138-139`, no `PowerManager`), time (`:145`) and cost (`:149-150`).
No upkeep row.

---

## S4. The onboarding tells the player their units leave via the Supply Route. They do not.

**Tier: SAFE WIN** (one line of text — but read S4 and A2 together)

### What the player experiences

`ingame-info-howtoplay.yaml:144` reads *"Spent units can Evacuate, leaving via your Supply Route to
recover"*, `:151` *"what is left of their cost."* A player reasonably concludes that evacuation is a
journey home, with a cost and a risk. It is neither.

### Mechanism

`RotateToEdge.ChooseEdgeCell` sends a ground unit to the closest matching edge cell from
`spawnAreaHintGround ?? self.Location` (`RotateToEdge.cs:165-168`) — the unit's **own cell** whenever
`FindClosestSpawnAreaForOwner` returns null, which it does on every map with no `spawnarea` actor. I
counted those myself rather than relying on the doc:

| map | `spawnarea` actors |
|---|---|
| river-zeta-ww3 | 6 |
| **all nine others** | **0** |

And the rotation branch discards the clicked target outright, which the code says in as many words:

```csharp
// this branch DISCARDS `target`: the unit walks to the nearest map edge, not to the thing
// that was clicked.                                            // DeliversCash.cs:108-109
```

The second omission is that the sentence *"what is left of their cost"* never names the two deductions
that make the mechanic interesting: **spent ammo** and **damage**. Both are in the formula
(`CustomSellValue.cs:44-53` and the `hp/maxHP` term at `RotateToEdge.cs:451`).

### The honest risk

**This is the one item on the list where fixing the text may be the wrong move.** If A2 below is taken,
the sentence becomes true and should not be rewritten first. If A2 is rejected, rewrite the sentence.
Doing the text fix now and A2 later means writing the line twice — which is precisely the ordering
principle `PIPELINE.md:87-97` states.

### Proof it does not already exist

The sentence is present verbatim at `:144`/`:151` at this commit. `PIPELINE.md` R9 covers `:123`/`:130`
in the same file and does not touch these lines — its own 2026-09-01 verdict block quotes exactly which
two lines it means.

---

## S5. `CashTrickler.Interval` is a dead knob, and income is 20% larger than the field implies

**Tier: SAFE WIN** (minutes — a comment or a field deletion, plus not repeating the number)

`CashTricklerInfo.Interval = 60` (`CashTrickler.cs:24-26`) has **zero readers**:

```
$ grep -rn "info.Interval\|Info.Interval" engine/OpenRA.Mods.Common/Traits/CashTrickler.cs
(no matches)
```

The trait was refactored into a rate *registration* — `Register` calls `resources.AddIncome`
(`:127`), `Unregister` calls `RemoveIncome` (`:136`), and `ITick.Tick` (`:140-152`) now only
re-registers the amount when a modifier changes. Payment happens once, on `PassiveIncomeInterval` = 50
(`PlayerResources.cs:209-211`). **There is no double-payout** — I checked for one specifically, because
"registers a rate *and* ticks a payment" is the classic shape of that bug, and this is not it.

The consequence: a derrick pays every **50** ticks, not every 60. Anyone sizing the capture economy from
the field value is 20% low. Worth deleting the field or commenting it dead so the next reader does not
tune against it.

---

# TIER 2 — AMBITIOUS SWINGS

---

## A1. The reserve remembers — veterans come back as veterans

**Tier: AMBITIOUS**

### What the player experiences

Your Abrams has three gold chevrons. It has been alive for twenty minutes, it hits harder and takes less,
and it is now nearly out of ammo. You pull it out. Instead of vanishing for scrap, it appears in your
sidebar as a **reserve unit — "Abrams (Veteran III), $900"** — cheaper than a fresh one, and when you call
it back in it arrives with its chevrons and a full magazine.

The verb stops being a euphemism. Today "rotate out" means *sell*; this makes it mean **rotate**.

### Why it is worth doing

It is the mechanic the game's own fiction already claims. `game-model.md:13` says *"'Rotating out' a unit
= sending it back to the map edge"* and `supply-route.md:7` calls the SR the place *"a sector's units
muster after being deployed in from off-map reserves"* — but there are no reserves. The map edge is a
shredder. This is the single largest gap between what this game says it is and what it does.

It also fixes a real economic hole. **Veterancy is the only thing in this economy that appreciates, and
the refund arithmetic cannot see it.** A rank-4 veteran and a fresh recruit evacuate for the same cash.
That means the correct play with a veteran is to *never* rotate it — which quietly deletes the signature
mechanic from exactly the units it matters most for.

### Mechanism

Veterancy is fully live and visibly so. `^GainsExperience` (`defaults.yaml:265-273`) grants
`rank-veteran` at 100/200/400/800, and the bonuses are large — `DamageMultiplier` 95→80,
`FirepowerMultiplier` 105→120, `SpeedMultiplier` 105→120, `ReloadDelayMultiplier` 95→80
(`defaults.yaml:290-335`). It reaches essentially the whole roster: `infantry.yaml:4`,
`aircraft.yaml:102`, and 15 explicit `Inherits@GainsExperience` sites across `vehicles-america.yaml` and
`vehicles-russia.yaml`. Rank **is** rendered on the unit — `WithDecoration@Rank_1..4` with `Image: rank`,
`Sequence: rank-veteran-1..4` (`defaults.yaml:339-365`), and `rank` is **not** in
`lint-baseline.txt`, so the sprite is present. So the player watches the chevrons accumulate.

The refund path is blind to all of it:

```
$ grep -c "Experience\|Level\|Rank" engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs
0
$ grep -c "Experience\|Level\|Rank" engine/OpenRA.Mods.Common/Activities/RotateToEdge.cs
0
```

`GetSellValue` reads only `CustomSellValueInfo.Value` or `ValuedInfo.Cost`, minus missing ammo and supply
(`CustomSellValue.cs:28-53`), and `RotateToEdge` ends in `self.Dispose()` (`:479`).

**The re-entry primitive already exists and is already used.** `ProducibleWithLevel` grants levels on
creation, gated on a `TechTree` prerequisite list:

```csharp
public readonly int InitialLevels = 1;                                  // ProducibleWithLevel.cs:24
void INotifyCreated.Created(Actor self) {
    if (!self.Owner.PlayerActor.Trait<TechTree>().HasPrerequisites(info.Prerequisites)) return;
```

and twelve `*R1` variants already carry it — e.g. `E3R1.america` with `InitialLevels: 2` and
`-Buildable:` (`infantry-america.yaml:26-29`). **The actors that would represent a returned veteran are
already in the ruleset and are simply unreachable by the player.** The work is a per-player reserve ledger,
a sidebar surface for it, and a price rule; the actor plumbing is done.

### The honest risk

Three, and the second is the one that would kill it.

1. **Balance.** A reserve that returns a veteran cheap and full is a *stronger* play than keeping the unit
   fighting, which inverts the tension the mechanic is supposed to create. It needs a real cost — time
   out of the line, a rank decay, a re-entry premium — and that cost is a tuning problem, not a coding one.
2. **Sidebar scope.** Reserves need a UI surface that does not exist. `PIPELINE.md` already carries
   *"Cargo Phase 3 — template sidebar for pre-loaded transport purchasing"* (`RELEASE_V1.md:138`) as an
   open development thread, which is the same class of work and is not done. If that is hard, this is hard
   for the same reason.
3. **`ProducibleWithLevel` is prerequisite-gated, not order-gated.** Reserving *a specific unit* at *a
   specific rank* is not what the trait models — it grants a fixed level to anything built while a
   prerequisite holds. Either accept coarse rank tiers (three reserve variants per unit, which is roughly
   what the `*R1` pattern already does) or write a new init-based path. Do not assume the trait drops in.

### Proof it does not already exist

The two `grep -c … 0` results above are the core proof: no experience term anywhere in the refund path.
Beyond that, there is no reserve concept at all —
`grep -rin "reserve" mods/ww3mod/rules/` finds no trait, and `RotateToEdge` disposes the actor
unconditionally at `:479` with no ledger write on any branch. Not in `PIPELINE.md` (I read all 472 lines);
not in `RELEASE_V1.md`. The nearest queued thing is `[v1.1] Ammo costs money (full economy rework)`
(`RELEASE_V1.md:169`), which is a different rework.

---

## A2. Make the evacuation lane real — and raidable

**Tier: AMBITIOUS**

### What the player experiences

Evacuation stops being free. A unit you pull out walks toward **your** side of the map and leaves through
your edge, which takes time and crosses ground the enemy can contest. A wrecked tank stranded deep in
their territory is genuinely stranded — you can try to walk it home and lose it on the way, or write it
off. And the reverse becomes a play you can make: **their** evacuation lane is a place to sit.

### Why it is worth doing

Today evacuation has no travel cost and no risk in exactly the situation where it should have the most of
both. On nine of ten maps a ground unit resolves its exit to the nearest edge cell **from its own
position** (`RotateToEdge.cs:165-168`, `searchOrigin = spawnAreaHintGround ?? self.Location`, with
`FindClosestSpawnAreaForOwner` returning null on every map with no `spawnarea` — self-counted above:
river-zeta 6, the other nine 0). A unit that has pushed into the enemy half is *closer* to the enemy's
back edge than to yours. It banks its refund in seconds, through their territory, uninterceptable.

That converts a deep raid from a commitment into a free option: push in, do damage, and cash out whatever
survives at the nearest wall. The one decision the economy is built around currently has no downside.

### Mechanism

Everything needed is present; what is missing is the anchor and the exposure.

- **The anchor mechanism ships and is used by aircraft already.** The aircraft branch immediately above
  does the right thing: it takes `FindClosestSpawnAreaForOwner(self) ?? self.Owner.HomeLocation` and then
  `GetSpawnCandidatesOnSameEdge(searchOrigin, 30)` (`RotateToEdge.cs:153-160`) — *the owner's own edge*.
  The ground branch falls back to `self.Location` instead of `self.Owner.HomeLocation`. **That one-token
  difference is most of the behaviour**, and the ground branch's `CanReach` pathfinder check
  (`:168`, defined `:175-180`) already exists to keep it honest.
- **The exposure half is partly scoped already** — `RELEASE_V1.md:56` asks for *"Vehicle off-map evac
  flight… Past the boundary: targetable but unselectable. Goal: prevent border-camp evac that dodges
  incoming fire."* **That item is about the last few tiles past the boundary; this proposal is about which
  edge the unit walks to in the first place.** They compose well and should probably be done together,
  but they are not the same item and neither subsumes the other.
- `evacuating` is already a real condition with real consumers (`RotateToEdge.OnFirstRun`, and
  `DropsSupplyCache` exempts an evacuating truck from its dry break-off — `economy.md` §Supply Truck), so
  an interception rule has a state to hang off.

### The honest risk

The biggest one is that **this is a balance change wearing a bugfix's clothes**, and it moves both bot
profiles by construction — `RotateToEdge` is the shared path for the manual Evacuate order, the
`AmmoPool` evacuate-when-dry stance, `DropsSupplyCache`'s empty-truck return, `VehicleCrew.cs:514` and
`EvacuateWhenUnrearmable.cs:82`. Per `CLAUDE.md` that is allowed to flow to `@stable`, but it **must be
called out in the commit message so the next benchmark baseline is re-taken knowingly**, and item 43's
re-baseline is already gated behind item 40.

Second: units that cannot path home. `CanReach` will refuse the far edge and the current code falls back
to re-choosing (`RotateToEdge.cs:193`) — a unit cut off behind a river would silently revert to
today's behaviour, which is *fine* but must be a deliberate, documented fallback rather than an accident.

Third, and I want to be plain about it: **I have not measured how often this actually matters.** The
claim that a unit deep in enemy ground exits through the enemy edge is read off the code and the
`spawnarea` census; I have not watched it happen. See "What I could not settle" below for the exact run.

### Proof it does not already exist

`RotateToEdge.cs:165-168` is the whole ground branch, quoted:

```csharp
var spawnAreaHintGround = FindClosestSpawnAreaForOwner(self);
var searchOrigin = spawnAreaHintGround ?? self.Location;
return self.World.Map.ChooseClosestMatchingEdgeCell(searchOrigin,
    c => mobileInfo.CanEnterCell(self.World, null, c) && CanReach(self, mobileInfo, c));
```

There is no owner-side term, no interception hook, and no `evacuating`-gated targetability change. Not in
`PIPELINE.md`. `RELEASE_V1.md:56` is the adjacent item and is scoped to the off-map flight, as quoted.

---

## A3. Ground supply is a one-way street — give the map a second thing worth fighting over

**Tier: AMBITIOUS** — **and the one I am least confident is distinct from queued work. Read the overlap
note before costing it.**

### What the player experiences

A forward supply dump becomes a position, not a consumable. Trucks stack into the same dump and it visibly
grows; it holds a supply bar the enemy badly wants to blow up; you can cash out what is left by sending a
truck to collect it, or lose the lot. Fighting over a fat forward dump becomes a thing that happens.

### Why it is worth doing

Right now every unit of supply put on the ground is **cash-terminal**. `SUPPLYCACHE` (`misc.yaml:370`)
carries `Selectable, SelectionDecorations, Tooltip, Building, FrozenUnderFog, Health, Armor, Targetable,
ProximityCapturable, HitShape, RenderSprites, WithSpriteBody@Full/Mid/Low, SupplyProvider, Explodes@Band1-8,
RenderRangeCircle@Supply` — and **no `Valued`, no `Sellable`, no `DeliversCash`** (I read lines 370-500 and
grepped the block; zero hits for all three). Since `GetSellValue` falls back to `ValuedInfo.Cost`
(`CustomSellValue.cs:31-32`), an actor with no `Valued` is worth **0** even if a sell path were added.

So the player's supply has three states: in a truck (worth face value on evac), in a Logistics Centre
(worth 34% — `structures.yaml:437-438`, the deliberate anti-money-pump cap), or on the ground (worth
nothing but its ammo). The ground is where the game *wants* you to put it, and the ground is the one place
it can never come back.

### Mechanism

Two of the pieces already shipped and are worth knowing before anyone scopes this:

- **The destructible half is done.** `Explodes@Band1-8` on the cache is exactly the "large explosion on
  death, size scaling with remaining supplies" that `RELEASE_V1.md:52` asks for, and the parity work of
  2026-08-21 already matched the crate to TRUK at `Range: 5c0`, `RearmDelay: 6`, pinned by
  `SupplyCacheTruckParityTest`.
- **Collection already exists as an order** — `PickupSupply`, capped at the truck's headroom
  (`economy.md` §SUPPLYCACHE), and the asymmetry with the LC's aura absorption is deliberate and
  documented.

What is missing is accretion (merging dumps into one growing object), a cash-out route, and the
positional identity that would make one worth defending.

### The honest risk — and the overlap

**`PIPELINE.md` R12 already owns "a supply truck cannot replenish a dropped supply cache"**
(`DropsSupplyCache.cs` gates on `AbsorbsSupplyCache`, which only `logisticscenter` carries), and
`RELEASE_V1.md:52` carries the same item marked urgent, explicitly wanting the cache to be *"targetable by
other supply trucks to replenish"*. **A large part of the accretion half of this proposal is R12.** What I
believe is genuinely new is the *cash-terminal* finding — that a crate has no `Valued` and therefore no
route back to money under any implementation — and the positional framing. **If R12 is scoped generously
it may swallow this entirely, and that would be a fine outcome.** Do not fund this as a separate item
without putting it next to R12's dossier first.

### Proof it does not already exist

The trait list above, read from `misc.yaml:370` onward, contains no `Valued`/`Sellable`/`DeliversCash`.
R12's own text (`PIPELINE.md:203-205`) is scoped to *replenishment* — *"gets no cursor and no order"* —
and says nothing about recovering value or about merging dumps.

---

# What I would do first

**S1, the refund preview.** It is the smallest change on this list and it unblocks the largest number of
other things.

The reasoning: this game's differentiating verb is bound to one keypress, is irreversible, and is played
with no information. Every *other* number in the economy is already on screen — the cash breakdown, the
income, the upkeep, the per-weapon refill cost, the ammo grand total. The buy side got instrumented and
the sell side got skipped. That is a one-sided instrument, and it is why `RELEASE_V1.md:49`'s sweep
("verify unit sell value at different ammo levels") has sat open: **it is hard to sweep a number that is
never displayed.** Shipping S1 makes that sweep a matter of hovering units instead of instrumenting a run.

It is also the prerequisite for the interesting half. A1 (the veteran reserve) and A2 (the evacuation
lane) are both proposals about making the rotate-out decision *cost something*. You cannot ask a player to
weigh a cost they cannot see. Put the number on screen first, then make the number interesting.

Second would be **S2**, the onboarding gap — it is text, it teaches the actual economy, and under the
release audit's "stranger's first session" ranking function (`PIPELINE.md:34-36`) a new player who never
learns to capture derricks is a new player who loses their first match without understanding why.

---

# What I could not settle without a run

Handing these up rather than guessing, per the launch rule.

1. **Does a unit deep in enemy territory really exit through the enemy edge?** (A2's central premise.)
   Read off `RotateToEdge.cs:163-168` plus the `spawnarea` census; not observed.
   Command: `./run-test.sh test-dry-evac-drops-queued-order` is the closest existing scenario but its map
   has no depot and short-circuits earlier. A purpose-built check is cheaper: place one own-player unit in
   the far corner of `twin-rivers-ww3` (spawns `112,92` / `112,28`, zero `spawnarea`), issue `Evacuate`,
   and log the `edgeCell` chosen. **Answer that counts:** the chosen cell's edge is the one nearest the
   unit, not the one nearest `self.Owner.HomeLocation`.

2. **Is the 20,000-army upkeep ceiling real in play?** (S3's arithmetic.) Derived from
   `PermilleCost: 5` × `PassiveIncome 100` on the shared 50-tick line; not measured.
   **Answer that counts:** in a match where a player holds no income buildings, cash should go flat and
   then negative as fielded army value crosses ~20,000. The cash tooltip's own `Net: ±$N / interval` line
   reports this directly, so a screenshot of that tooltip at two army sizes settles it with no
   instrumentation.

3. **Does the multi-line cash tooltip actually render legibly at its current size?** `SimpleTooltipLogic`
   grows the widget correctly (`:52-56`), but with 10+ income and upkeep rows it could run off-screen near
   the sidebar. This is a screenshot question, not a code question.

---

# Corrections to existing documents found on the way

Filed here rather than acted on; I changed no file but this one.

- `RELEASE_V1.md:171` lists "kill bounties" among deferred v1.1 lobby dropdowns. It is **live** —
  `LobbyPrerequisiteDropdown@GlobalBounty`, `player.yaml:188-197`, default Off.
- `CashTricklerInfo.Interval` (`CashTrickler.cs:26`) has zero readers; income pays on
  `PassiveIncomeInterval` (50), not 60. Any sizing done from the field value is 20% low. (S5.)
- `AllyRepair:` is live at `player.yaml:164`, but **no actor in the mod carries `RepairableBuilding`** —
  `grep -rn "RepairableBuilding" mods/ww3mod/` returns only `disable-player-experience.yaml:6` (a file
  nothing references) and a comment at `aircraft.yaml:321`. The building-repair cash sink never fires.
  Small, and I did not chase whether that is deliberate.
