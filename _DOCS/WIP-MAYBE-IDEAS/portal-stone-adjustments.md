
### Portal Stone Adjustments

We are increasing the dependence on portal stones, so they might need adjustments. 
Maybe they should be easier to get, or something you can craft without needing an NPC. However, 
they might be fine as is, so we will wait and see.

**Placement requirements:**

* Valid NPC housing (walls, light, furniture)
* Built from **in-biome materials** (no importing wood everywhere)
* Minimum size threshold?

**Usage:**

* Interact with Portal Stone → shows list of other Portal Stones
* Select destination → teleport
* **Mana cost based on distance** - longer jumps cost more mana
* If insufficient mana: partial teleport? Failed teleport? Delayed teleport?

```csharp
int CalculateManaCost(Vector2 from, Vector2 to) {
    float distance = Vector2.Distance(from, to);
    float worldWidth = Main.maxTilesX * 16f;
    float distanceRatio = distance / worldWidth;
  
    // 10 mana for short hop, 200 mana for cross-world
    return (int)(10 + (distanceRatio * 190));
}
```

**Crafting (rough idea):**

* Early game: 20 Stone + 5 Gems + crafting station
* Should be achievable once you've established a real base
* Not so cheap you spam them everywhere

### Gameplay Loop Created

1. **Spawn** - You start here, it's safe
2. **Explore** - Travel to new biome (dangerous, one-way commitment)
3. **Survive** - Fight to gather local materials
4. **Establish** - Build valid home from in-biome blocks
5. **Anchor** - Place Portal Stone
6. **Connect** - Now you can travel between home and new base
7. **Repeat** - Push further, build more bases

Each Portal Stone is EARNED. The network grows with your accomplishment.

### Mana Cost Implications

* **Mages can travel easier** - Class identity perk
* **Warriors need mana potions** - Resource cost for travel
* **Long-distance travel is expensive** - Encourages intermediate bases
* **Emergency escape costs resources** - Not free like Mirror was

### Edge Cases

| Scenario                             | Behavior                                       |
| ------------------------------------ | ---------------------------------------------- |
| Not enough mana                      | Teleport fails, mana not consumed              |
| Portal Stone destroyed               | Removed from network                           |
| Die at destination                   | Normal death rules apply                       |
| Multiplayer                          | Each player can use any placed Portal Stone?   |
| Building destroyed but Stone remains | Stone stops working until housing valid again? |

### Synergy with Other Features

| Feature                       | Synergy                                           |
| ----------------------------- | ------------------------------------------------- |
| Persistent position           | Can't logout-escape, must reach a Portal Stone    |
| In-biome building requirement | Must engage with biome to build valid portal base |
| Burrowing enemies             | Base defense matters, can't just plop a portal    |
| Depth scaling                 | Deep bases are hard to establish = valuable       |

### Why This Works

* **Exploration has commitment** - No free escape
* **Bases have purpose** - Portal network is the reward
* **Biome engagement required** - Can't import materials
* **Mana becomes travel resource** - New use for mana on non-mages
* **Progression is visible** - Your portal network shows your accomplishment

---
