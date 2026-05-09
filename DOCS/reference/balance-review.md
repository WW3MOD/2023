# WW3MOD Balance Review & Recommendations
**Date:** 2026-04-04
**Scope:** All combat units — infantry, vehicles, aircraft (NATO/America vs BRICS/Russia)
**Goal:** Faction parity, realistic asymmetric balance, cost-effectiveness consistency

## Changes Applied (2026-04-04)
- [x] **Humvee cost 600 → 450, speed 130 → 150** — cheaper/faster to reflect utility vehicle role
- [x] **SHOK Tesla Trooper disabled** — prerequisites set to `~disabled`, futuristic tech level removed from player.yaml
- [x] **Halo (Mi-26) cargo 24 → 36, thickness 6 → 10, speed 240 → 220** — matches Chinook capacity, realistic armor, slightly slower (IRL slower)
- [x] **Futuristic tech level removed** from lobby dropdown

## Review Notes
- **Paladin 3-burst: NOT broken** — cycle time (480 ticks for 3 shots) vs Giatsint (180 ticks for 1 shot) = only ~12% DPS difference. Burst is alpha-strike advantage, balanced by reload gap
- **ATGM Pen 100 vs heavy armor: WORKS via top-attack** — Abrams top armor = 700 * 10% = 70 effective, so Pen 100 penetrates. T-90 top = 280 * 80% = 224, so Pen 100 does NOT penetrate T-90 top (see T-90 distribution issue below)
- **T-90 armor distribution concern:** Distribution 100,80,80,80,60 gives T-90 224 effective top armor — infantry ATGMs can't penetrate it! Abrams (100,40,15,10,10) has only 70 top. T-90 top should be weaker (ERA provides less overhead protection IRL)

---

## Methodology

Each unit is compared against its faction counterpart and evaluated for:
1. **Cost efficiency** — damage output / survivability per credit spent
2. **Realism** — does the stat profile match real-world capabilities?
3. **Role clarity** — does it fill a unique niche without overlapping?
4. **Faction parity** — at equal budget, can both sides compete?

---

## INFANTRY

All infantry share: HP 200, Armor: Kevlar, Speed 25 (except noted).
Both factions have identical infantry templates — balance is symmetric.

### Observations & Recommendations

| Unit | Cost | Issue | Recommendation |
|------|------|-------|----------------|
| **E1 Conscript** | 50 | Disabled (`~disabled` prerequisite) for both factions. If intended as cannon fodder, 50 cost is fine. Uses same 5.56mm.E3 weapon as the E3 Rifleman but lacks the RPG — the cost gap (50 vs 100) feels right. | **No change needed** if intentionally disabled. If re-enabled, consider HP 150 (conscripts have worse gear/training) |
| **E3 Rifleman** | 100 | Solid baseline. RPG secondary (1 ammo, 6000 damage, 25 supply) gives anti-vehicle utility. Good cost-performance ratio. | **OK as-is** |
| **AR (LMG)** | 100 | Same cost as E3 but fills suppression role. 10-burst with 500 total ammo is generous. 100-round magazine is realistic (M249 box mag). | **OK as-is** |
| **E2 Grenadier** | 100 | GrenadeLauncher does 1000 damage + 150 spread per shot. 5-round magazine, 30 total ammo. Good anti-structure/light vehicle. | **OK as-is** |
| **TL Team Leader** | 200 | 7.62mm DMR (250 dmg, 15c0 range) + grenade launcher + morale aura. The 2x cost over riflemen is justified by leadership role. 15-cell range is significant advantage. | **OK as-is** — premium infantry leader role |
| **MT Mortar** | 300 | 60mm mortar: 3000 dmg, 25c0 range, 8c0 min. 25 ammo. Speed penalty. Realistically, 60mm mortars are crew-served — single operator is a game simplification. | **Consider:** HP 250 (mortar teams historically carry more gear/armor). Or leave at 200 since the range compensates |
| **SN Sniper** | 400 | 7.62mm Sniper: 250 dmg, 20c0 range, low visibility. Only 250 damage is low for a 400-cost specialist — in real life a 7.62 sniper round is devastating to personnel. | **Recommend: Damage 350-400.** A sniper should reliably 1-shot conscripts and 2-shot riflemen. At 250 damage vs 200 HP, it takes 1 shot to kill but leaves no margin for suppression/cover reductions. Bump to 350 for reliable kills through partial cover |
| **AT Anti-Tank** | 300 | ATGM: 10000 dmg, Penetration 100, 20c0 range, 3 ammo. **Penetration 100 is problematic.** Against Abrams (Thickness 700) or T-90 (280), the ATGM barely penetrates. A Javelin or TOW-2 IRL can defeat any MBT from the top. | **Critical: ATGM Penetration should be 400-600** (top-attack already modeled). At Pen 100, infantry AT is essentially useless vs heavy armor, which breaks the rock-paper-scissors. The TopAttack flag should help bypass frontal armor, but Pen 100 is still too low vs side/rear |
| **AA Anti-Air** | 300 | MANPAD: 3000 dmg, Penetration 15, 23c0 range, 3 ammo. Pen 15 is fine — helicopter armor is thin (5-20 thickness). Stinger IRL has ~4.5km range; 23 cells is proportional. | **OK as-is** |
| **E6 Engineer** | 250 | MP5 (100 dmg, 10c0 range) + mines + demolition. 250 cost for a utility unit is fair. | **OK as-is** |
| **E4 Flamethrower** | 100 | Flamespray: 10 dmg per tick, 6c0 range, fire effect. 50% explode on death. Very short range makes it high-risk. | **Consider:** Cost 150. Fire area denial is powerful in garrisons. At 100 it's the same price as a rifleman but much more situational. Or leave at 100 since the short range is a natural limiter |
| **SF Special Forces** | 600 | 5.56mm silenced DMR + C4 + amphibious + fast (Speed 32). The premium price buys versatility. | **OK as-is** — elite unit priced appropriately |
| **MEDI Medic** | 100 | Heals allies, unarmed. 100 cost is fine. | **OK as-is** |
| **TECN Technician** | 250 | Captures buildings. Armor: None (not Kevlar like soldiers). | **Consider:** Give Kevlar armor. At 250 cost, having worse armor than a 100-cost rifleman feels wrong. Technicians IRL would wear standard body armor |
| **DR Drone Operator** | 150 | Deploys recon drone + jammer. Unique utility role. | **OK as-is** |
| **SHOK Tesla Trooper** | 500 | Russia-only. PortaTesla: 150 dmg, 20c0 range, EMP 50 ticks. **No NATO equivalent.** | **Faction balance issue.** Russia gets a 500-cost infantry that can EMP-disable vehicles. NATO has no equivalent unique infantry. Consider: (a) Add a NATO unique infantry (e.g., Javelin specialist with better ATGM, or electronic warfare trooper), or (b) Increase SHOK cost to 700+ to reflect its unique power |

### Infantry Summary
- **Symmetric:** All shared templates are identical between factions (good)
- **Asymmetric concern:** SHOK gives Russia a unique anti-vehicle infantry with no NATO answer
- **Key fix needed:** ATGM Penetration too low (100) to threaten heavy armor

---

## VEHICLES

### Side-by-Side Comparison

#### Light Transport: Humvee vs BTR-80

| Stat | Humvee | BTR-80 | Real-World Context |
|------|--------|--------|-------------------|
| Cost | 600 | 600 | |
| HP | 8,000 | **14,000** | BTR-80 is indeed heavier/tougher than a Humvee |
| Armor | Light, Thick: 10 | Light, Thick: 10 | |
| Speed | 130 | 110 | Humvee is faster IRL (~113 km/h vs ~80 km/h) |
| Weapon | 7.62mm MG (250 dmg) | 12.7mm HMG (600 dmg) | BTR mounts a heavier gun IRL |
| Cargo | 8 infantry | 8 infantry | |
| Amphibious | Yes | Yes | BTR is amphibious IRL, Humvee generally isn't |

**Issue:** At same cost (600), the BTR has **75% more HP** AND a **140% stronger weapon**. The Humvee only gets +20 speed. This is heavily Russia-favored.

**Real-world context:** The BTR-80 IS a proper APC while the Humvee is a utility vehicle. They shouldn't be direct cost equivalents.

**Recommendations:**
- **Humvee cost: 400-500** (it's a utility truck, not an APC)
- OR **Humvee HP: 10,000** (still less than BTR but closer)
- OR **BTR cost: 800** (reflects its superior capabilities)
- **Remove Humvee amphibious** — standard Humvees are NOT amphibious. Only specialized M1097A2 variants can ford, and even those aren't fully amphibious

#### APC: M113 vs BTR-80 (continued)

The M113 at 700 cost with 12,000 HP is closer to the BTR-80 equivalent.

| Stat | M113 | BTR-80 |
|------|------|--------|
| Cost | 700 | 600 |
| HP | 12,000 | 14,000 |
| Weapon | 12.7mm HMG | 12.7mm HMG |
| Cargo | 12 | 8 |
| Speed | 100 | 110 |
| Amphibious | Yes | Yes |

**Verdict:** M113 vs BTR is actually well-balanced — M113 carries more troops (12 vs 8) at slightly higher cost. Both amphibious IRL (correct). **OK as-is.**

#### IFV: Bradley vs BMP-2

| Stat | Bradley | BMP-2 | Real-World Context |
|------|---------|-------|-------------------|
| Cost | 1,500 | 1,300 | Bradley is more expensive IRL |
| HP | 14,000 | 14,000 | |
| Armor | Medium, Thick: 15 | Medium, Thick: 15 | |
| Primary | 25mm (500 dmg, Pen 60) | 30mm (500 dmg, Pen 60) | BMP-2 has slightly larger caliber |
| ATGM | WGM.bradley (2-burst, Pen 800) | WGM (single, Pen 800) | Bradley fires TOW pairs |
| ATGM Ammo | 8 | 8 | |
| Cargo | 6 | 6 | |
| ATGM BurstWait | 1000 | 500 | **BMP-2 fires ATGMs 2x faster** |

**Issue:** BMP-2 costs 200 less AND fires ATGMs twice as fast (BurstWait 500 vs 1000). The Bradley fires 2 missiles per burst, but with 1000 ticks between bursts vs BMP-2's 500 ticks for single shots. Over time:
- Bradley: 2 missiles per 1000 ticks = 1 missile per 500 ticks
- BMP-2: 1 missile per 500 ticks

So ATGM DPS is actually equal. The 200 cost gap favors Russia.

**Recommendations:**
- **BMP-2 cost: 1,400** (closer to Bradley) — BMP-2 IRL is cheaper than Bradley, but 200 gap is too much for equal performance
- OR **Bradley HP: 16,000** to reflect its better composite armor
- **BMP-2 30mm Penetration: 65** vs Bradley 25mm at 60 — BMP-2's 30mm should penetrate slightly more

#### MBT: Abrams M1A2 vs T-90

| Stat | Abrams | T-90 | Real-World Context |
|------|--------|------|-------------------|
| Cost | 2,500 | 2,400 | Abrams costs more IRL (~$10M vs ~$4.5M) |
| HP | 28,000 | 24,000 | |
| Armor Thick | **700** | **280** | Abrams has DU armor, T-90 has Kontakt-5 ERA |
| Armor Dist | 100,40,15,10,10 | 100,80,80,80,60 | Abrams: strong front, weak everywhere else. T-90: more even |
| Primary | 120mm (20k dmg, Pen 800) | 125mm (20k dmg, Pen 800) | |
| Range | **25c0** | 24c0 | Abrams has slightly better FCS IRL |
| BurstWait | 130 | 110 | **T-90 fires faster** |
| Speed | 90 | **100** | T-90 is faster (counterintuitive — Abrams has more power IRL) |
| Ammo | 40 | 40 | |

**Critical Issues:**

1. **Armor Thickness 700 vs 280 is wildly imbalanced.** The Abrams frontal armor is nearly impenetrable by anything except artillery (Pen 1000) and other tank rounds (Pen 800). Meanwhile the T-90 at 280 thickness is vulnerable to Bradley/BMP ATGMs (Pen 800 >> 280). The T-90 with Kontakt-5 ERA should still be formidable — IRL it's estimated at ~700-800mm RHA equivalent frontally.

2. **Abrams armor distribution (100,40,15,10,10)** means sides are only 280 effective (700*0.4) and rear is 70 effective (700*0.1). T-90 distribution (100,80,80,80,60) means sides are 224 (280*0.8) and rear is 168 (280*0.6). The T-90 actually has better side/rear protection relative to its frontal, but the Abrams still wins everywhere:
   - Front: 700 vs 280 (Abrams wins massively)
   - Sides: 280 vs 224 (Abrams still wins)
   - Rear: 70 vs 168 (T-90 wins — which is realistic, Abrams has weak rear)

3. **T-90 compensates with:** faster fire rate (110 vs 130), faster speed (100 vs 90), lower cost (2400 vs 2500). But these don't offset the survivability gap.

**Recommendations:**
- **T-90 Thickness: 500-550** — real-world T-90M (latest variant) has significantly improved armor. 500 still keeps Abrams as the armor king but makes the gap reasonable
- **T-90 Distribution: 100,60,40,20,15** — make it more front-focused like the Abrams (realistic: ERA is concentrated on frontal arc)
- **Abrams Speed: 80** — Abrams weighs 73 tons, it's not faster than a 46-ton T-90. The T-90 at 100 speed being faster is correct
- OR **Abrams Thickness: 550, T-90 Thickness: 450** — narrower gap, both formidable
- **Cost gap should be larger.** IRL the Abrams costs 2x+ more. Consider Abrams: 2800, T-90: 2200

#### SPH: Paladin vs Giatsint

| Stat | Paladin (M109) | Giatsint (2S5) | Real-World |
|------|----------------|----------------|------------|
| Cost | 1,800 | 1,800 | |
| HP | 14,000 | 14,000 | |
| Caliber | 155mm | 152mm | Near-identical IRL |
| Damage | 15,000 | 15,000 | |
| Pen | 1,000 | 1,000 | |
| Range | 40c0 | 40c0 | |
| Burst | **3 (delays: 120)** | 1 | **Paladin fires 3-round salvos!** |
| BurstWait | 240 | 180 | |
| Thickness | 10 | 19 | Giatsint slightly tougher |

**Critical Issue:** The Paladin fires **3 shells per engagement** (Burst: 3, BurstDelays: 120) vs Giatsint's single shell. That's 3x the damage output per cycle. Even with longer BurstWait (240 vs 180), the Paladin deals 45,000 damage per cycle vs 15,000.

**This is massively NATO-favored.** The M109 Paladin does NOT fire 3-round bursts IRL — it has a fire rate of ~4 rounds/minute sustained. The 2S5 Giatsint actually has a comparable or faster rate.

**Recommendations:**
- **Remove Paladin Burst: 3.** Change to single-shot like the Giatsint
- **Paladin BurstWait: 150** (slightly faster than Giatsint — M109A7 has better autoloader)
- OR if burst stays for gameplay reasons, **reduce Paladin damage to 8000** per shot (total 24,000 per cycle vs 15,000 — still NATO-favored but less extreme)
- **Giatsint Thickness: 19 vs Paladin 10** — the Giatsint being tougher is correct (it's on a heavier chassis), keep this

#### MLRS: M270 vs Grad vs TOS

| Stat | M270 | Grad | TOS |
|------|------|------|-----|
| Cost | 1,800 | 1,500 | 2,000 |
| HP | 10,000 | 10,000 | 20,000 |
| Damage/rocket | 15,000 | 6,000 | 3,000 |
| Penetration | 500 | 250 | 100 |
| Spread | 96 | 96 | 100 |
| Spread Dmg | 1,500 | 1,000 | 1,500 |
| Rockets | 12 | 40 | 24 |
| Inaccuracy | 2c128 | 4c0 | 3c512 |
| Range | 40c0 | 40c0 | 28c0 |
| BurstWait | 10 | 4 | 10 |

**Analysis:**
- **M270** (NATO): 12 rockets x 15,000 = 180,000 total potential damage, Pen 500, most accurate (2c128)
- **Grad** (Russia): 40 rockets x 6,000 = 240,000 total, Pen 250, least accurate (4c0) — area saturation
- **TOS** (Russia): 24 rockets x 3,000 = 72,000 total, Pen 100, thermobaric (anti-infantry) — shorter range

Russia gets TWO rocket systems. Together (Grad + TOS = 3,500 cost) they outclass the M270 (1,800) in total firepower. However, that's 2 units vs 1 at nearly 2x cost.

**Issues:**
1. **M270 damage per rocket (15,000) with Pen 500 is very high** — each rocket hits harder than a tank shell. IRL, 227mm GMLRS is powerful but not tank-shell equivalent
2. **Grad 40 rockets at BurstWait 4** means it fires its entire load in 160 ticks — massive saturation
3. **TOS range 28c0 is short** for an MLRS — but this is accurate, TOS-1A has ~6km range vs Grad's ~20km

**Recommendations:**
- **M270 damage: 10,000** per rocket (still powerful, more realistic for unguided rockets)
- **M270 Penetration: 300** (DPICM submunitions, not AP rounds)
- **Grad is well-designed** — low accuracy, high volume, cheap. OK as-is
- **TOS is fine** — short range + thermobaric anti-infantry is a distinct role
- Consider **M270 ammo: 12 → 6** (IRL M270 carries 12 rockets in 2 pods of 6, but reloading takes ~5 min)

#### AA: Stryker SHORAD vs Tunguska

| Stat | Stryker SHORAD | Tunguska | Real-World |
|------|----------------|----------|------------|
| Cost | 2,500 | 1,700 | |
| HP | 14,000 | 14,000 | |
| Armor | Medium, 15 | Medium, 19 | |
| Speed | 120 (wheeled) | 100 (tracked) | |
| Gun | 25mm (same as Bradley) | 30mm dual mount | Tunguska has bigger guns |
| SAM | Stinger quad (5000 dmg) | 9M311 (5000 dmg) | |
| SAM Ammo | 8 | 8 | |
| Extra | Hellfire ATGM (4 missiles) | Ground-mode autocannon | |

**Issue:** Stryker SHORAD costs 800 more (2500 vs 1700) and gets Hellfire ATGMs making it multi-role. The Tunguska's dual 30mm can engage ground targets too.

**The Stryker is overpriced.** IRL, the Stryker SHORAD variant is much cheaper than a full SHORAD system. The Tunguska is appropriately priced.

**Recommendations:**
- **Stryker SHORAD cost: 2,000** — still premium for multi-role, but 2,500 is too expensive when a dedicated Bradley (1,500) + AA infantry (300) does similar work
- OR **keep at 2,500 but add HP: 16,000** — you're paying for a premium multi-role platform

#### Ballistic Missiles: HIMARS vs Iskander

| Stat | HIMARS | Iskander | Real-World |
|------|--------|----------|------------|
| Cost | 3,500 | 3,500 | |
| HP | 6,000 | 10,000 | |
| Ammo | 6 | 2 | |
| Warhead Dmg | 5,000 + 8,000 spread | 10,000 + 15,000 spread | |
| Spread radius | 512 | 768 | |
| Penetration | 1,500 | 2,000 | |
| Range | 50c0 | 50c0 | |

**Total potential damage:**
- HIMARS: 6 missiles x (5,000 + 8,000) = 78,000
- Iskander: 2 missiles x (10,000 + 15,000) = 50,000

**Analysis:**
- HIMARS has 56% more total damage potential and 3x more shots (better for multiple targets)
- Iskander has bigger per-hit impact (25,000 vs 13,000), larger blast (768 vs 512), better penetration (2,000 vs 1,500)
- Iskander is tougher (10,000 vs 6,000 HP) — important for survivability
- IRL: HIMARS carries 6x GMLRS or 1x ATACMS. Iskander carries 2x quasi-ballistic missiles

**Issue:** HIMARS is significantly better in total damage output. 6 shots vs 2 means much more flexibility.

**Recommendations:**
- **HIMARS HP: 8,000** (still fragile but less of a glass cannon)
- **Iskander warhead damage: 12,000 + 18,000 spread** (bigger per-hit to justify only 2 shots)
- OR **Iskander ammo: 3** (2 is too few for a 3,500-cost unit)
- **HIMARS cost: 3,000** and **Iskander cost: 3,500** — HIMARS is more of a tactical weapon, Iskander is strategic

---

## AIRCRAFT

### Transport: Chinook vs Mi-26 Halo

| Stat | Chinook (TRAN) | Halo (HALO) | Real-World |
|------|----------------|-------------|------------|
| Cost | 2,000 | 2,000 | |
| HP | 600 | 600 | |
| Armor Thick | 10 | 6 | |
| Speed | 240 | 240 | Mi-26 is slower IRL (295 vs 315 km/h) |
| Cargo | 36 weight | 24 weight | Chinook carries more |

**Issue:** At same cost, Chinook carries 50% more cargo AND has better armor (10 vs 6 thickness). NATO-favored.

**Recommendations:**
- **Halo cargo: 30-36** — Mi-26 Halo IRL is the world's largest production helicopter, carries MORE than a Chinook (20 tons vs 12 tons). It should carry at minimum equal cargo
- **Halo HP: 700** — Mi-26 is a much bigger target but also more robust
- **Halo Armor Thickness: 10** — same protection class as Chinook
- OR **Halo cost: 1,500** if keeping lower cargo capacity

### Scout/Light Attack: Littlebird vs Hind

These aren't direct counterparts. NATO has a dedicated scout (Littlebird, 3000) while Russia has an attack/transport hybrid (Hind, 4000).

**Littlebird (3000):** 300 HP, 7.62mm minigun + 2 Hellfires, carries 4 infantry
**Hind (4000):** 800 HP, 12.7mm + 80 rocket pods, carries 8 infantry

**Assessment:** The Hind at 4000 fills the gap between transport and attack helicopter. The Littlebird at 3000 is a fragile scout/light striker. These serve different roles and the cost difference reflects it. **OK — asymmetric by design.**

However, **Russia lacks a cheap scout helicopter equivalent.** Consider whether this is intentional asymmetry or a gap.

### Attack Helicopter: Apache vs Mi-28

| Stat | Apache (HELI) | Mi-28 (MI28) | Real-World |
|------|---------------|--------------|------------|
| Cost | 6,000 | 6,000 | |
| HP | 800 | 800 | |
| Armor Thick | 20 | 20 | |
| Speed | 245 | 245 | |
| Primary | 30mm (1000 dmg, 200 ammo) | 30mm (1000 dmg, 200 ammo) | |
| Secondary | Hellfire (8, 10000 dmg) | Hellfire (8, 10000 dmg) | |

**Assessment:** These are perfectly mirrored. **OK as-is.** IRL the Apache and Mi-28 are near-equivalents.

### Attack Aircraft: A-10 vs Su-25 Frogfoot

| Stat | A-10 | Su-25 Frogfoot | Real-World |
|------|------|----------------|------------|
| Cost | 6,000 | 6,000 | |
| HP | 800 | 700 | A-10 is legendarily tough |
| Armor Thick | 20 | 20 | |
| Speed | 390 | 420 | Su-25 is faster IRL (~950 vs ~706 km/h) |
| Primary | 30mm GAU-8 (1000 dmg, 100 ammo) | Rocket Pods (5000 dmg, 60 ammo) | |
| Secondary | Hellfire x4 | None | |

**Issue:** The A-10 has both a 30mm cannon (anti-everything) AND 4 Hellfires (anti-tank). The Frogfoot ONLY has rocket pods. The A-10 is massively more versatile.

**Critical comparison:**
- A-10 burst: 15 rounds x 1000 = 15,000 damage per pass (precise, Pen 70), plus 4 x 10,000 Hellfires
- Frogfoot burst: 10 rockets x 5,000 = 50,000 per pass (inaccurate, Pen 50), no secondary

The Frogfoot has higher raw burst damage but worse accuracy (2c0 inaccuracy). It also can't hit infantry (InvalidTargets: Infantry on RocketPods).

**Recommendations:**
- **Frogfoot HP: 800** — match the A-10. Su-25 is known for its titanium bathtub, similar to A-10's survivability design
- **Frogfoot: add secondary weapon** — IRL Su-25 carries Kh-25 or Kh-29 ASMs, or R-60 air-to-air self-defense missiles. Suggest adding 2x Kh-25 guided missiles (similar to Hellfire, 10000 dmg, Pen 800, ammo: 2)
- OR **Frogfoot cost: 4,500-5,000** — if keeping it as rocket-pod only, it shouldn't cost the same as the A-10

### Fighter: F-16 vs MiG-29

| Stat | F-16 | MiG-29 | Real-World |
|------|------|--------|------------|
| Cost | 6,000 | 6,000 | |
| HP | 400 | 550 | |
| Armor Thick | 10 | (not specified — inherits ^Aircraft: 3?) | |
| Speed | 525 | 525 | |
| IdleSpeed | 220 | 200 | |
| Primary | AirToAir (6 missiles) | AirToAir (6 missiles) | |
| Secondary | 20mm CRAM (150 ammo) | 20mm CRAM (150 ammo) | |

**Issue:** MiG-29 has 37.5% more HP (550 vs 400) with identical weapons. This favors Russia in air-to-air. However, the F-16 likely has better armor thickness (10 vs what appears to be 3 from ^Aircraft default).

**Recommendations:**
- **F-16 HP: 500** — close the gap. F-16 is robust in practice
- OR **MiG-29 HP: 450** — MiG-29 is slightly less survivable IRL
- **Both should cost 6000** — they're generation peers
- **Verify MiG-29 armor thickness** — if it inherited 3 from ^Aircraft, give it explicit Thickness: 10 (like F-16)
- **Note:** The MiG-29 tooltip says "Falcrum" — should be "Fulcrum" (typo)

### Drones

**Quadcopter drone:** 50 HP, unarmed scout. Not directly purchasable (spawned by DR operator). **OK as-is.**

---

## CROSS-CUTTING ISSUES

### 1. Penetration vs Thickness Scaling

The penetration/thickness system has inconsistencies:

| Weapon | Penetration | vs Abrams (700) | vs T-90 (280) |
|--------|-------------|------------------|----------------|
| ATGM (infantry) | 100 | 14% (barely scratches) | 36% |
| WGM/Hellfire | 800 | 114% (penetrates) | 286% (overkill) |
| Tank Round | 800 | 114% | 286% |
| 25mm/30mm | 60-70 | 8-10% (useless) | 21-25% |
| MANPAD | 15 | N/A | N/A |
| Artillery | 1000 | 143% | 357% |

**Problem:** There's a massive gap between "useless" (Pen < 100) and "overkill" (Pen 800). The ATGM at Pen 100 is barely effective against the T-90 (280 thickness) and completely useless against Abrams (700). Meanwhile Hellfires/WGMs at Pen 800 completely ignore all armor.

**Recommendation:** Create more granular penetration tiers:
- Small arms (5.56mm): Pen 3-5 (can't touch any vehicle)
- HMG (12.7mm): Pen 15 (threatens light armor)
- Autocannon (25-30mm): Pen 60-70 (threatens medium armor)
- **Infantry ATGM: Pen 400** (threatens all armor, reduced by thickness)
- Vehicle ATGM/Hellfire: Pen 800 (penetrates heavy armor)
- Tank main gun: Pen 800 (penetrates heavy armor)
- Artillery: Pen 1000 (penetrates everything via top-attack)

### 2. The Abrams Armor Problem

At Thickness 700, the Abrams is practically invulnerable to everything except ATGMs (Pen 800) and artillery (Pen 1000). Combined with 28,000 HP, it takes ~3 tank rounds to kill from the front. This makes Abrams rushes extremely hard to counter.

**Recommendation:** Reduce Abrams Thickness to 500-550. Still the best-armored unit, but ATGMs and flanking become viable.

### 3. Cost-Effectiveness Table (Per 1000 Credits)

| Unit | Cost | HP per 1000cr | Main DPS Estimate | Notes |
|------|------|---------------|-------------------|-------|
| Humvee | 600 | 13,333 | Low | Outclassed by BTR |
| BTR | 600 | **23,333** | Medium | Best value APC |
| M113 | 700 | 17,143 | Medium | |
| Bradley | 1,500 | 9,333 | High | |
| BMP-2 | 1,300 | 10,769 | High | Better value than Bradley |
| Abrams | 2,500 | 11,200 | Very High | Armor makes it much tougher |
| T-90 | 2,400 | 10,000 | Very High | Faster but less armored |
| Paladin | 1,800 | 7,778 | **Extreme** (3-burst) | Broken — 3x damage of Giatsint |
| Giatsint | 1,800 | 7,778 | High | |

### 4. Russia Has More Unit Variety

Russia gets: BTR, BMP-2, T-90, Giatsint, Grad, TOS, Tunguska, Iskander, SHOK (infantry)
NATO gets: Humvee, M113, Bradley, Abrams, Paladin, M270, Stryker SHORAD, HIMARS

Russia: 8 vehicles + 1 unique infantry = 9 unique units
NATO: 8 vehicles + 0 unique infantry = 8 unique units

The TOS gives Russia an extra anti-infantry artillery platform with no NATO equivalent. Consider adding a NATO thermobaric/incendiary equivalent or acknowledging this as intentional asymmetry.

---

## PRIORITY CHANGES (Ranked)

### Critical (game balance broken)
1. **Paladin 3-round burst** — remove or reduce. Currently deals 3x damage of its counterpart
2. **Abrams Thickness 700 vs T-90 280** — gap too large. Suggest 550 vs 450 or 500 vs 400
3. **ATGM Penetration 100** — infantry AT is useless vs heavy armor. Increase to 400+

### High (significant imbalance)
4. **Humvee vs BTR parity** — Humvee needs cost reduction or HP increase
5. **A-10 vs Frogfoot** — A-10 is vastly more capable at same cost
6. **Chinook vs Halo cargo** — Chinook carries 50% more at same cost
7. **SHOK has no NATO equivalent** — Russia advantage in unique infantry

### Medium (tuning)
8. **Sniper damage 250 → 350** — should reliably kill infantry
9. **F-16 vs MiG-29 HP gap** — 400 vs 550 is too wide
10. **Stryker SHORAD overpriced** — 2500 is too much for the role
11. **HIMARS vs Iskander total damage** — HIMARS has too much ammo advantage
12. **MiG-29 "Falcrum" typo** — should be "Fulcrum"

### Low (polish)
13. **TECN armor: None → Kevlar** — shouldn't be squishier than riflemen
14. **M270 damage per rocket** — 15,000 is very high for unguided rockets
15. **Humvee amphibious** — remove (not realistic)
16. **Abrams Speed 90 → 80** — too fast for 73 tons

---

## SUGGESTED FINAL STAT CHANGES

### Infantry
| Unit | Current | Proposed | Reason |
|------|---------|----------|--------|
| SN (Sniper) | Damage: 250 | **Damage: 350** | Reliable kill through cover |
| AT (ATGM) | Penetration: 100 | **Penetration: 400** | Must threaten heavy armor |
| TECN | Armor: None | **Armor: Kevlar** | Standard body armor |
| SHOK | Russia-only | **Add NATO equivalent or raise cost to 700** | Faction parity |

### Vehicles - America
| Unit | Current | Proposed | Reason |
|------|---------|----------|--------|
| Humvee | Cost: 600, HP: 8000 | **Cost: 450, HP: 8000** | Not equivalent to BTR |
| Humvee | Amphibious: yes | **Remove amphibious** | Not realistic |
| Abrams | Thickness: 700 | **Thickness: 550** | Too dominant |
| Abrams | Speed: 90 | **Speed: 80** | 73-ton tank should be slower |
| Abrams | Cost: 2500 | **Cost: 2800** | Premium tank, premium price |
| Paladin | Burst: 3, BurstWait: 240 | **Burst: 1, BurstWait: 150** | Broken 3x damage |
| M270 | Damage: 15000 | **Damage: 10000** | Too high for unguided rockets |
| Stryker SHORAD | Cost: 2500 | **Cost: 2000** | Overpriced for role |

### Vehicles - Russia
| Unit | Current | Proposed | Reason |
|------|---------|----------|--------|
| T-90 | Thickness: 280 | **Thickness: 450** | Too low vs peer MBTs |
| T-90 | Distribution: 100,80,80,80,60 | **Distribution: 100,60,40,20,15** | More front-focused (realistic ERA placement) |
| T-90 | Cost: 2400 | **Cost: 2200** | Cheaper than Abrams IRL |

### Aircraft - America
| Unit | Current | Proposed | Reason |
|------|---------|----------|--------|
| F-16 | HP: 400 | **HP: 500** | Too fragile vs MiG-29 (550) |
| A-10 | (no change) | Consider **cost: 6500** if Frogfoot stays at 6000 | A-10 is significantly better |

### Aircraft - Russia
| Unit | Current | Proposed | Reason |
|------|---------|----------|--------|
| Halo | Cargo: 24 | **Cargo: 36** | Mi-26 carries MORE than Chinook IRL |
| Halo | Thickness: 6 | **Thickness: 10** | Match Chinook |
| Frogfoot | HP: 700 | **HP: 800** | Match A-10 survivability |
| Frogfoot | No secondary | **Add 2x guided missiles** | A-10 has Hellfires |
| MiG-29 | Tooltip: "Falcrum" | **"Fulcrum"** | Typo |

---

## NOTES

- These recommendations assume the penetration/thickness system works as: `effectiveDamage = damage * min(1, penetration / thickness)` or similar. If the formula is different, penetration recommendations may need adjustment.
- All recommendations maintain asymmetric balance — factions should feel different, not identical.
- Changes should be implemented incrementally and playtested between batches.
- Priority order: fix the 3 critical issues first, then playtest before addressing medium/low items.
