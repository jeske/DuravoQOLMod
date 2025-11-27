# Terraria Survival Overhaul Mod

A tModLoader mod to slow the game pace and expand the depth by removing exploits that make interacting with enemies optional.

---

## Core Philosophy

Terraria presents platformer-combat aesthetics but gives players tools that make engagement optional. This mod surgically removes the worst offenders while preserving legitimate building and crafting QoL.

Currently a "Moon Lord" speedrun can be done in 50-70 minutes by an expert. WHen we are done, the minimum viable time for this should be closer to 10-20 hours, with a typical ru

NOTE: Reddit thread at: https://www.reddit.com/r/Terraria/comments/1p6n7jd/interest_in_survival_overhaul_mod/

---



## Smart Hopping (Priority: HIGH)

### Problem

Players can exploit zombie AI by building short walls, pits, or "murder holes" that cause zombies to overshoot with their fixed-height jump. The standard zombie jump is always the same arc regardless of obstacle height, so a 2-tile wall causes the same jump as a 4-tile wall - often landing them past the player or onto platforms where they get stuck.

### Solution

When a zombie is blocked by a short wall (1-4 tiles) in the direction of the player, calculate the exact jump trajectory to land ON TOP of that wall, not overshoot it.

### Trigger Condition

```
zombie.onGround AND
zombie.blocked by solid wall toward player AND
wall height is 1-4 tiles AND
clear space above wall to land
```

That's it. No pit detection, no complex state tracking.

### Physics

Terraria uses approximately:

- Gravity: `g ≈ 0.3` per tick (pixels/tick²)
- 1 tile = 16 pixels

For a zombie to land exactly on top of a wall H tiles high:

**Vertical velocity:**

```
// Minimum to reach height H, plus small margin
vy = -sqrt(2 * g * H * 16) * 1.15
```

**Flight time:**

```
t_peak = -vy / g
// We want to land AT height H, not return to ground
peak_height = vy² / (2g)
fall_distance = peak_height - (H * 16)
t_fall = sqrt(2 * fall_distance / g)
t_total = t_peak + t_fall
```

**Horizontal velocity:**

```
// Distance to land on top of wall (wall position + half zombie width)
distance = (tiles_to_wall + 1) * 16
vx = distance / t_total * direction
```

### Implementation

```csharp
public class SmartHopper : GlobalNPC {
    private const float GRAVITY = 0.3f;
    private const float TILE = 16f;
    private const int MAX_WALL_HEIGHT = 4;
  
    public override bool PreAI(NPC npc) {
        if (!IsGroundEnemy(npc)) return true;
        if (npc.velocity.Y != 0) return true; // Must be grounded
        if (npc.target < 0) return true;
  
        Player target = Main.player[npc.target];
        if (!target.active || target.dead) return true;
  
        int dir = (target.Center.X > npc.Center.X) ? 1 : -1;
        Point pos = npc.Center.ToTileCoordinates();
  
        // Check if blocked by wall in direction of player
        if (!IsSolid(pos.X + dir, pos.Y)) return true; // Not blocked
  
        // Find wall height (how many tiles until clear?)
        int wallHeight = 0;
        for (int h = 0; h <= MAX_WALL_HEIGHT; h++) {
            if (!IsSolid(pos.X + dir, pos.Y - h - 1)) {
                wallHeight = h + 1;
                break;
            }
        }
  
        if (wallHeight == 0 || wallHeight > MAX_WALL_HEIGHT) {
            return true; // Wall too high or no clearance - normal AI
        }
  
        // Check landing zone is clear (need 3 tiles vertical for zombie)
        for (int h = 0; h < 3; h++) {
            if (IsSolid(pos.X + dir, pos.Y - wallHeight - h)) {
                return true; // Can't fit on top of wall
            }
        }
  
        // Calculate smart jump
        float heightPixels = wallHeight * TILE;
  
        // Vertical: just enough to clear, with 15% margin
        float vy = -(float)Math.Sqrt(2 * GRAVITY * heightPixels) * 1.15f;
  
        // Time calculation
        float t_peak = -vy / GRAVITY;
        float peakHeight = (vy * vy) / (2 * GRAVITY);
        float fallDist = peakHeight - heightPixels;
        float t_fall = (float)Math.Sqrt(2 * Math.Max(0, fallDist) / GRAVITY);
        float t_total = t_peak + t_fall;
  
        // Horizontal: land on top of wall
        float distPixels = TILE * 1.5f; // Land solidly on the wall top
        float vx = (distPixels / t_total) * dir;
  
        // Clamp for sanity
        vy = Math.Clamp(vy, -12f, -2f);
        vx = Math.Clamp(vx, -5f, 5f);
  
        npc.velocity.Y = vy;
        npc.velocity.X = vx;
  
        return true;
    }
  
    private bool IsGroundEnemy(NPC npc) {
        return npc.aiStyle == 3 ||  // Fighter AI (zombies, skeletons)
               npc.aiStyle == 26 || // Unicorn AI  
               npc.aiStyle == 38;   // Tortoise AI
    }
  
    private bool IsSolid(int x, int y) {
        if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) return true;
        Tile tile = Main.tile[x, y];
        return tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
    }
}
```

### Examples

**Murder hole (2-tile pit):**

- Zombie falls in pit
- Walks toward player, hits 2-tile wall
- Smart hop: `vy ≈ -5.3` instead of default `vy ≈ -8`
- Lands on player's level, not on platform above

**Natural ledge:**

- Zombie chasing player uphill
- Hits 1-tile step
- Small hop lands exactly on step
- Continues chase smoothly

**Player barricade (3-tile wall):**

- Zombie approaches 3-high dirt wall
- Calculated jump lands on top
- Player can't hide behind short walls anymore

### What This Changes

| Obstacle     | Before                     | After                    |
| ------------ | -------------------------- | ------------------------ |
| 1-tile step  | Big jump, overshoot        | Tiny hop, lands on step  |
| 2-tile wall  | Big jump, often overshoots | Precise hop to top       |
| 3-tile wall  | Big jump, sometimes works  | Precise hop to top       |
| 4-tile wall  | Big jump, usually works    | Precise hop to top       |
| 5+ tile wall | Big jump, fails            | Normal AI (or burrowing) |

### Performance

- Check only runs when zombie is grounded
- Simple tile lookups, no pathfinding
- Adds maybe 4-8 tile checks per grounded zombie per tick
- Negligible impact

### Gameplay Impact

- Murder holes don't work anymore
- Short walls don't save you
- Natural terrain doesn't break zombie pathing
- Zombies feel smarter and more threatening
- Closed doors still block (this is about OPEN door cheese)

## Travel Rework (Priority: HIGH)

### Problem

Mirror and Recall Potions provide instant escape from anywhere. Combined with spawn-on-logout, there's zero commitment to exploration. Every expedition is risk-free.

### Solution

Delete recall items. Replace with earned portal infrastructure.

### Changes

**Removed from game:**

* Magic Mirror
* Ice Mirror
* Cell Phone (recall function)
* Recall Potions
* Remove from loot tables, crafting, NPC shops

**Softcore exception:**
Softcore players get a UI "Return Home" button. They can suicide for free anyway, so recall is just convenience. Let them have it - they chose easy mode.

## Enemy Rebalancing (Priority: HIGH)

### Problem

Expert mode enemies are balanced around the assumption that players will cheese. When cheese is removed, Expert damage may be too punishing for legitimate play - especially on the surface.

Current Expert mode + no cheese =  **impossible** , not  **challenging** .

### Observation

When enemies hit hard and cheese exists → players cheese more
When enemies hit hard and cheese is removed → players die immediately

The mod removes cheese. Enemy damage must be rebalanced to match.

### Approach

**Surface should be accessible.** New players need to learn the game. First few nights shouldn't be instant death.

**Depth scaling handles difficulty.** We already have depth-scaled damage. Surface can be easier because depth makes it harder naturally.

### Proposed Changes

| Layer           | Vanilla Expert | With Mod             |
| --------------- | -------------- | -------------------- |
| Surface (day)   | ~1.0x          | 0.6x                 |
| Surface (night) | ~1.0x          | 0.8x                 |
| Underground     | ~1.0x          | 1.0x (unchanged)     |
| Cavern          | ~1.0x          | 1.0x + depth scaling |
| Hell            | ~1.0x          | 1.0x + depth scaling |

The depth scaling system (Feature: Depth-Scaled Difficulty) adds multipliers as you go deeper. Surface gets a REDUCTION to compensate for cheese removal.

### Why This Works

**Vanilla Expert balance assumption:**

* Player can wall off
* Player can tunnel safely
* Player can recall instantly
* Player can AFK with minions
* Therefore: enemies must hit HARD to matter at all

**Mod balance assumption:**

* Player must fight
* Player is exposed
* Player can't escape easily
* Therefore: enemies can hit MODERATELY and still matter

### Implementation

```csharp
public override void ModifyHitPlayer(NPC npc, Player target, ref Player.HurtModifiers modifiers) {
    float depth = target.position.Y / (Main.maxTilesY * 16f);
    float surfaceThreshold = (float)Main.worldSurface / Main.maxTilesY;
  
    if (depth < surfaceThreshold) {
        // Surface - reduce damage
        bool isDay = Main.dayTime;
        float surfaceMultiplier = isDay ? 0.6f : 0.8f;
        modifiers.FinalDamage *= surfaceMultiplier;
    } else {
        // Underground and below - apply depth scaling
        float depthBelowSurface = (depth - surfaceThreshold) / (1f - surfaceThreshold);
        float depthMultiplier = 1f + (depthBelowSurface * 1.5f);
        modifiers.FinalDamage *= depthMultiplier;
    }
}
```

### Tuning Notes

These numbers are starting points. Playtesting required.

Key questions:

* Can a new player survive first night with copper armor?
* Is Underground the right difficulty for "you should have some gear now"?
* Does Hell feel appropriately deadly?

### Interaction with Difficulty Modes

| Mode    | Surface Mult | Depth Scaling |
| ------- | ------------ | ------------- |
| Classic | 0.7x         | 1.0x - 2.0x   |
| Expert  | 0.6x         | 1.0x - 2.5x   |
| Master  | 0.5x         | 1.0x - 3.0x   |

Master gets the BIGGEST surface reduction because Master enemies hit absurdly hard. But also the steepest depth scaling.

---



## Open Questions

1. **Mod compatibility** : How will this interact with Calamity, Thorium, etc.? New enemy types need categorization.
2. **Configuration** : Should features be toggleable per-world or global?
3. **Multiplayer** : Sync issues with position persistence, portal networks, burrowing state?
4. **Performance** : Raycast per placement attempt, pathfinding for burrowers, dig-noise tracking?
5. **Portal Stone crafting recipe** : What's the right cost? Too cheap = spam, too expensive = tedious.
6. **Biome detection** : How to determine "in-biome" for housing validation? Block percentage threshold?
7. **Mana cost tuning** : What's the right curve for portal distance? Linear? Exponential?
8. **Counter balance** : Is obsidian too hard to create in Hell? Too easy?
9. **Audio mixing** : How many simultaneous digging sounds before it's cacophony?
10. **Modded enemies** : Default behavior for unrecognized enemy types? (Probably: can dig everything except native biome blocks)
11. **Softcore recall button** : Where in UI? Always visible or only when safe?

---

## Implementation Order

**Foundational (do first):**

* Persistent Player Position - without this nothing else matters
* LOS Interactions - affects all other features

**Core anti-cheese (do together):**

* Depth-Scaled Difficulty - makes the world dangerous
* Aggro Burrowing - enemies dig to reach you
* Enemy Rebalancing - make surface survivable without cheese

**Travel overhaul:**

* Travel Rework - delete recall, add portal stones

**Cleanup:**

* Minion Tethering - no AFK murder rooms
* Combat Block Lock - can't wall off mid-fight (lowest priority, may not need)

---

## Testing Scenarios

**Persistent Position:**

* [ ] Logout in Hell, reload - should be in Hell
* [ ] Logout in cave, cave gets filled in by world edit - should fall back to spawn
* [ ] Die, then logout - should NOT restore death position
* [ ] Softcore suicide after loading in dangerous area - should work (intended escape)

**Depth-Scaled Difficulty:**

* [ ] Surface zombie damage vs cavern zombie damage - cavern should hurt more
* [ ] Dig 50 blocks on surface - spawn rate should increase slightly
* [ ] Dig 50 blocks in cavern - spawn rate should increase dramatically
* [ ] Walk through existing cave system - spawn rate should stay low
* [ ] Attempt hellevator with no armor - should be overwhelmed and killed
* [ ] Hell enemy damage should be ~2.5x surface equivalent

**Travel Rework:**

* [ ] Magic Mirror removed from loot tables
* [ ] Recall Potions removed from loot/shop
* [ ] Softcore player has UI recall button
* [ ] Mediumcore/Hardcore player has NO recall option
* [ ] Portal Stone placeable in valid in-biome housing
* [ ] Portal Stone NOT placeable if house built from foreign materials
* [ ] Teleport between two Portal Stones works
* [ ] Mana cost scales with distance
* [ ] Insufficient mana prevents teleport

**LOS Block Interaction:**

* [ ] Mining ore veins (LOS should allow normal mining)
* [ ] Mining ore through a 1-block wall (should fail - no LOS)
* [ ] Extending tunnel from inside sealed tube (should fail)
* [ ] Building through a small gap (should work - one ray has LOS)

**Aggro Burrowing:**

* [ ] Box yourself in during Eye of Cthulhu fight (surface - no burrowing)
* [ ] Box yourself in underground with zombies using wood (should hear digging, breach)
* [ ] Box yourself in underground using stone (should hold - native counter)
* [ ] Box yourself in Corruption using ebonstone (should hold - native counter)
* [ ] Box yourself in Hell using obsidian (should hold - obsidian counter)
* [ ] Box yourself in Hell using hellstone (should fail - hellstone is NOT the counter)
* [ ] Enemy burrows into lava (should take damage/die)

**Minion Tethering:**

* [ ] Minion stays within ~5 tiles of player
* [ ] Minion teleports back if it goes through a wall
* [ ] Switching away from summon weapon despawns minions
* [ ] Cannot AFK farm - minion won't fight in another room

**Integration Tests:**

* [ ] Hellevator attempt with all features enabled - should be suicide
* [ ] Proper cave exploration with native counter blocks and armor - should be challenging but viable
* [ ] Early game surface gameplay - should be unchanged/accessible
* [ ] Establish base in Corruption: mine ebonstone, build house, place portal - should work
* [ ] Try to build portal in Corruption using surface wood - should fail

---

## Resources

* tModLoader docs: https://github.com/tModLoader/tModLoader/wiki
* Terraria source (decompiled): `Player.PlaceThing_Tiles()`, `Player.PickTile()`
* Example mods with similar hooks: TBD
