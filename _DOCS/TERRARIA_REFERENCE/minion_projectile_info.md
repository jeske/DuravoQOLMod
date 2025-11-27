# Terraria Minion Leash & Detection Mechanics

**Source References:**
- ProjectileID constants: [Decompiled Terraria 1.4.0.5 - ProjectileID.cs](https://raw.githubusercontent.com/AdamSavard/Terarria1405/refs/heads/master/ID/ProjectileID.cs)
- Leash distances: [Official Terraria Wiki](https://terraria.wiki.gg) (citations reference `Desktop 1.4.4.9 source code`)
- AI methods: `Terraria.Projectile.cs` → `AI()`, `AI_026()`, `AI_062()`, `AI_066()`, `AI_067_FreakingPirates()`, `AI_121_StardustDragon()`

---

## Key Insight: No Declarative Leash Data

Leash distances are **NOT** exposed in `ProjectileID.Sets` arrays. Unlike yoyos (which have `YoyosMaximumRange`), minion return distances are **hardcoded in AI switch statements** per `aiStyle`. This means there's no simple lookup—you must read the AI code for each minion type.

---

## Complete Minion Table

| Minion | Staff | ProjectileID(s) | Enemy Detection | Return (Leash) | aiStyle | Notes |
|--------|-------|-----------------|-----------|----------------|---------|-------|
| **Baby Finch** | Finch Staff | 759 `BabyBird` | 50 tiles | 50 tiles | — | Rests on player's head when idle |
| **Baby Slime** | Slime Staff | 266 `BabySlime` | — | — | — | Ground minion, jumps |
| **Flinx** | Flinx Staff | — (1.4.1+) | — | — | — | Can swim in liquids |
| **Abigail** | Abigail's Flower | — (1.4.4+) | — | — | — | Scales with playtime, special mechanic |
| **Hornet** | Hornet Staff | 373 `Hornet` | 125 tiles (187.5†) | **62.5 tiles** | 62 | †Player-targeted enemies |
| **Flying Imp** | Imp Staff | 375 `FlyingImp` | 125 tiles (187.5†) | **62.5 tiles** | 62 | Shoots 376 `ImpFireball` |
| **Vampire Frog** | Vampire Frog Staff | 758 `VampireFrog` | 50 tiles | — | — | Can swim in liquids |
| **Spider** | Spider Staff | 390 `VenomSpider`, 391 `JumperSpider`, 392 `DangerousSpider` | 50 + 2.5×pos | **87.5 + 2.5×pos** | 26 | `minionPos` scaling; wall-climbing |
| **Enchanted Dagger** | Blade Staff | 864 `Smolstar` | 50 tiles | **56.25 tiles** | — | Only 75% whip tag damage |
| **Twins** | Optic Staff | 387 `Retanimini`, 388 `Spazmamini` | 125 tiles | **75 tiles** ⚠️ | 66 | **TIGHTEST flying leash** - interrupts attacks frequently |
| **Pirate** | Pirate Staff | 393 `OneEyedPirate`, 394 `SoulscourgePirate`, 395 `PirateCaptain` | 50 tiles | — (parrot) | 67 | Parrot carries back when too far |
| **Pygmy** | Pygmy Staff | 191-194 `Pygmy`, `Pygmy2`, `Pygmy3`, `Pygmy4` | 50 + 2.5×pos | **62.5 + 2.5×pos** | 26 | Shoots 195 `PygmySpear`; `minionPos` scaling |
| **Raven** | Raven Staff | 317 `Raven` | 56.25 tiles | **87.5 tiles** | — | Charges through enemies |
| **Sanguine Bat** | Sanguine Staff | 755 `BatOfLight` | — | — | — | Flies through blocks |
| **Desert Tiger** | Desert Tiger Staff | 831-835 `StormTiger*` | 100×50 rect | — | — | Pounce-based, not traditional leash |
| **Sharknado** | Tempest Staff | 407 `Tempest` | 125 tiles (187.5†) | **125 tiles** | — | Shoots 408 `MiniSharkron`; flies through blocks |
| **Deadly Sphere** | Deadly Sphere Staff | 533 `DeadlySphere` | 50 tiles | **93.75 tiles** | — | Dash attack pattern |
| **UFO** | Xeno Staff | 423 `UFOMinion` | 125 tiles (187.5†) | **125 tiles** | — | Teleports 18.75-50 tiles to target; instant-hit laser |
| **Stardust Cell** | Stardust Cell Staff | 613 `StardustCellMinion` | 125 tiles (187.5†) | **84.4 tiles** | 62 | Teleports 28.75-53.75 tiles; shoots 614 `StardustCellMinionShot` |
| **Stardust Dragon** | Stardust Dragon Staff | 625-628 `StardustDragon1-4` | 62.5 tiles (87.5†) | **125 tiles** | 121 | Head segment (625); teleports when exceeds distance |
| **Terraprisma** | Terraprisma | 946 `EmpressBlade` | — | — | — | Flies through blocks |

**Legend:**
- `pos` = `minionPos` (slot index, starting at 0)
- † = Range when player targets enemy with whip/summon weapon
- ⚠️ = Notably problematic value

---

## ProjectileID.Sets Relevant to Minions

From `ProjectileID.cs` (1.4.0.5):

```csharp
// Minion projectile shots (NOT the minion itself)
public static bool[] MinionShot = ProjectileID.Sets.Factory.CreateBoolSet(
    374,  // HornetStinger
    376,  // ImpFireball  
    389,  // MiniRetinaLaser
    195,  // PygmySpear
    408,  // MiniSharkron
    433,  // UFOLaser
    614   // StardustCellMinionShot
);

// Stardust Dragon segments (need UUID for segment linking)
public static bool[] NeedsUUID = ProjectileID.Sets.Factory.CreateBoolSet(625, 626, 627, 628);
public static bool[] StardustDragon = ProjectileID.Sets.Factory.CreateBoolSet(625, 626, 627, 628);

// Desert Tiger variants
public static bool[] StormTiger = ProjectileID.Sets.Factory.CreateBoolSet(833, 834, 835);

// Minions that support targeting
public static bool[] MinionTargettingFeature = ProjectileID.Sets.Factory.CreateBoolSet(
    191, 192, 193, 194,  // Pygmy
    266,                  // BabySlime
    317,                  // Raven
    373, 375,            // Hornet, FlyingImp
    387, 388,            // Twins
    390,                  // Spider (only VenomSpider listed)
    393,                  // Pirate (only OneEyedPirate listed)
    407, 423,            // Tempest, UFO
    533,                  // DeadlySphere
    613,                  // StardustCell
    625,                  // StardustDragon (head)
    755, 758, 759,       // BatOfLight, VampireFrog, BabyBird
    831, 833, 834, 835,  // StormTiger variants
    864,                  // Smolstar (Blade Staff)
    946                   // EmpressBlade (Terraprisma)
    // ... plus sentries
);

// Can be sacrificed when re-summoning
public static bool[] MinionSacrificable = ProjectileID.Sets.Factory.CreateBoolSet(
    191, 192, 193, 194,  // Pygmy
    266,                  // BabySlime
    317,                  // Raven
    373, 375,            // Hornet, FlyingImp
    387, 388,            // Twins
    390,                  // Spider
    393,                  // Pirate
    407, 423,            // Tempest, UFO
    533,                  // DeadlySphere
    613,                  // StardustCell
    755, 758, 759,       // BatOfLight, VampireFrog, BabyBird
    831, 864, 946,       // StormTiger, Smolstar, EmpressBlade
    625, 626, 627, 628   // StardustDragon segments
);
```

---

## Leash Distance Tiers

### Tight Leash (≤75 tiles) - Frequently Interrupts Combat
| Minion | Return Distance |
|--------|----------------|
| Baby Finch | 50 tiles |
| Enchanted Dagger | 56.25 tiles |
| Pygmy (base) | 62.5 tiles |
| Hornet | 62.5 tiles |
| Flying Imp | 62.5 tiles |
| **Twins** | **75 tiles** ← tightest flying minion |

### Medium Leash (80-95 tiles) - Occasional Interruption
| Minion | Return Distance |
|--------|----------------|
| Stardust Cell | 84.4 tiles |
| Raven | 87.5 tiles |
| Spider (base) | 87.5 tiles |
| Deadly Sphere | 93.75 tiles |

### Loose Leash (125 tiles) - Rarely Interrupts
| Minion | Return Distance |
|--------|----------------|
| Sharknado | 125 tiles |
| UFO | 125 tiles |
| Stardust Dragon | 125 tiles |

---

## Modding: Building a Leash Lookup

Since there's no native lookup, you'd need to build one manually:

```csharp
public static class MinionLeashData
{
    public static float GetReturnDistance(int projectileType)
    {
        return projectileType switch
        {
            // Tight leash
            ProjectileID.BabyBird => 50f,         // 759
            ProjectileID.Smolstar => 56.25f,      // 864 (Blade Staff)
            ProjectileID.Pygmy or
            ProjectileID.Pygmy2 or
            ProjectileID.Pygmy3 or
            ProjectileID.Pygmy4 => 62.5f,         // 191-194 (base, +2.5×minionPos)
            ProjectileID.Hornet => 62.5f,         // 373
            ProjectileID.FlyingImp => 62.5f,      // 375
            ProjectileID.Retanimini or
            ProjectileID.Spazmamini => 75f,       // 387-388 (TIGHTEST flying)
            
            // Medium leash
            ProjectileID.StardustCellMinion => 84.4f,  // 613
            ProjectileID.Raven => 87.5f,               // 317
            ProjectileID.VenomSpider or
            ProjectileID.JumperSpider or
            ProjectileID.DangerousSpider => 87.5f,    // 390-392 (base, +2.5×minionPos)
            ProjectileID.DeadlySphere => 93.75f,      // 533
            
            // Loose leash
            ProjectileID.Tempest => 125f,         // 407
            ProjectileID.UFOMinion => 125f,       // 423
            ProjectileID.StardustDragon1 => 125f, // 625 (head segment)
            
            _ => 100f  // Conservative default
        };
    }
    
    public static float GetDetectionRange(int projectileType, bool playerTargeted = false)
    {
        float baseRange = projectileType switch
        {
            ProjectileID.VenomSpider or
            ProjectileID.JumperSpider or
            ProjectileID.DangerousSpider => 50f,
            ProjectileID.Pygmy or
            ProjectileID.Pygmy2 or
            ProjectileID.Pygmy3 or
            ProjectileID.Pygmy4 => 50f,
            ProjectileID.BabyBird => 50f,
            ProjectileID.DeadlySphere => 50f,
            ProjectileID.Smolstar => 50f,
            ProjectileID.Raven => 56.25f,
            ProjectileID.StardustDragon1 => 62.5f,
            ProjectileID.Hornet or
            ProjectileID.FlyingImp or
            ProjectileID.Retanimini or
            ProjectileID.Spazmamini or
            ProjectileID.Tempest or
            ProjectileID.UFOMinion or
            ProjectileID.StardustCellMinion => 125f,
            _ => 100f
        };
        
        // Many minions get boosted range when player targets with whip
        if (playerTargeted && baseRange >= 62.5f)
            return 187.5f;
            
        return baseRange;
    }
}
```

---

## Design Observations

1. **The Twins are broken by design** - 125 tile detection but only 75 tile leash means they constantly abandon attacks to return to player. This is the tightest leash of any flying minion.

2. **`minionPos` scaling is clever** - Spider and Pygmy get +2.5 tiles per slot index, so your first minion has the tightest leash while later ones spread out more.

3. **Player targeting is a big boost** - Many minions jump from 125 to 187.5 tile range when you target with a whip, which is 50% more reach.

4. **Ground minions (Pirate, Spider) don't teleport** - They use return-by-walking or special mechanics (parrot carry), so "leash" means something different.

5. **Post-Moon Lord minions are generous** - UFO, Stardust Dragon, and Sharknado all have 125-tile return distances, likely to handle larger arenas.