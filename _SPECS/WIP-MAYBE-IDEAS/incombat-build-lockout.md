## Combat Zone Block Lock (Priority: MEDIUM)

### Problem

Players can place blocks mid-combat to create instant barriers.

### Solution

Block placement disabled while in combat (enemies within X tiles).

### Considerations

* What's the radius? Too small = easy to cheese, too large = frustrating
* Should destruction also be blocked? Probably not - mining to escape is fair
* Boss fights: larger radius or always-on during boss?
* Exception for platforms? (mobility aid vs. full barrier)

```csharp
bool CanPlaceBlock(Player player) {
    // Check for nearby hostile NPCs
    float combatRadius = 400f; // ~25 tiles
  
    foreach (NPC npc in Main.npc) {
        if (npc.active && !npc.friendly && npc.damage > 0) {
            if (Vector2.Distance(player.Center, npc.Center) < combatRadius) {
                return false;
            }
        }
    }
    return true;
}
```

---