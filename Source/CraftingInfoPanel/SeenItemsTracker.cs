// MIT Licensed - Copyright (c) 2025 David W. Jeske
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace DuravoQOLMod.CraftingInfoPanel;

/// <summary>
/// Tracks which items have been "seen" (picked up at least once) globally across all characters.
/// Uses file-based storage in the mod's save folder for persistence.
/// Used by the Crafting Panel to hide items until their recipe ingredients have been encountered.
/// </summary>
public static class SeenItemsTracker
{
    private const string SAVE_FILE_NAME = "SeenItems.json";
    
    /// <summary>Set of item IDs that have been seen globally</summary>
    private static HashSet<int> seenItemIds = new HashSet<int>();
    
    /// <summary>Whether the data has been loaded from file</summary>
    private static bool isLoaded = false;
    
    /// <summary>Whether there are unsaved changes</summary>
    private static bool isDirty = false;
    
    /// <summary>Whether any hardmode item has ever been seen (persisted)</summary>
    private static bool hasSeenAnyHardmodeItem = false;
    
    /// <summary>Public accessor for hardmode item detection</summary>
    public static bool HasSeenAnyHardmodeItem {
        get {
            EnsureLoaded();
            return hasSeenAnyHardmodeItem;
        }
    }
    
    /// <summary>Set of hardmode ore bar item IDs for quick lookup</summary>
    private static readonly HashSet<int> HardmodeOreBarItemIds = new HashSet<int> {
        ItemID.CobaltBar, ItemID.PalladiumBar,
        ItemID.MythrilBar, ItemID.OrichalcumBar,
        ItemID.AdamantiteBar, ItemID.TitaniumBar,
        ItemID.HallowedBar, ItemID.ChlorophyteBar,
        ItemID.ShroomiteBar, ItemID.SpectreBar,
        ItemID.LunarBar
    };
    
    /// <summary>
    /// Check if an item has been seen.
    /// </summary>
    public static bool HasSeenItem(int itemId)
    {
        EnsureLoaded();
        return seenItemIds.Contains(itemId);
    }
    
    /// <summary>
    /// Mark an item as seen and save if needed.
    /// Also checks if this is a hardmode item and sets the flag.
    /// </summary>
    public static void MarkItemAsSeen(int itemId)
    {
        EnsureLoaded();
        if (itemId > ItemID.None && seenItemIds.Add(itemId)) {
            isDirty = true;
            
            // Check if this is a hardmode ore bar and update the flag
            if (!hasSeenAnyHardmodeItem && HardmodeOreBarItemIds.Contains(itemId)) {
                hasSeenAnyHardmodeItem = true;
            }
            
            // Save immediately on new discovery to avoid data loss
            SaveToFile();
        }
    }
    
    /// <summary>
    /// Check if the item should be shown in the crafting panel.
    /// Returns true if:
    /// - The config setting is disabled (show all items), OR
    /// - The item itself has been seen (e.g., found a bar in a chest), OR
    /// - At least one recipe ingredient has been seen, OR
    /// - The item has no recipe (base materials, loot items)
    /// </summary>
    public static bool ShouldShowItem(int itemId)
    {
        // If setting is disabled, show all items
        if (!DuravoQOLModConfig.EnableCraftingPanelOnlyShowSeenItems) {
            return true;
        }
        
        EnsureLoaded();
        
        // If the item itself has been seen (e.g., found a bar in a chest), show it
        if (seenItemIds.Contains(itemId)) {
            return true;
        }
        
        // Find recipe for this item
        for (int recipeIndex = 0; recipeIndex < Recipe.numRecipes; recipeIndex++) {
            Recipe recipe = Main.recipe[recipeIndex];
            if (recipe.createItem.type == itemId) {
                // Check if any ingredient has been seen
                foreach (Item ingredient in recipe.requiredItem) {
                    if (ingredient.type <= ItemID.None) {
                        break;
                    }
                    if (seenItemIds.Contains(ingredient.type)) {
                        return true; // Found a seen ingredient
                    }
                }
                // Recipe found but no ingredients seen
                return false;
            }
        }
        
        // No recipe found - treat as visible (base material, loot, etc.)
        return true;
    }
    
    /// <summary>
    /// Scan all currently available recipes and mark both the craftable item
    /// and all its ingredients as seen. Called periodically during gameplay.
    /// This ensures that when a recipe shows up in the native crafting UI,
    /// the player has "discovered" it and its ingredients.
    /// </summary>
    public static void ScanAvailableRecipes()
    {
        EnsureLoaded();
        
        bool anyNewItems = false;
        
        for (int availableIndex = 0; availableIndex < Main.numAvailableRecipes; availableIndex++) {
            int globalRecipeIndex = Main.availableRecipe[availableIndex];
            Recipe recipe = Main.recipe[globalRecipeIndex];
            
            // Mark the craftable item as seen
            int craftableItemId = recipe.createItem.type;
            if (craftableItemId > ItemID.None && seenItemIds.Add(craftableItemId)) {
                anyNewItems = true;
                
                // Check if this is a hardmode ore bar
                if (!hasSeenAnyHardmodeItem && HardmodeOreBarItemIds.Contains(craftableItemId)) {
                    hasSeenAnyHardmodeItem = true;
                }
            }
            
            // Mark all ingredients as seen
            foreach (Item ingredient in recipe.requiredItem) {
                if (ingredient.type <= ItemID.None) {
                    break;
                }
                if (seenItemIds.Add(ingredient.type)) {
                    anyNewItems = true;
                    
                    // Check if this is a hardmode ore bar
                    if (!hasSeenAnyHardmodeItem && HardmodeOreBarItemIds.Contains(ingredient.type)) {
                        hasSeenAnyHardmodeItem = true;
                    }
                }
            }
        }
        
        if (anyNewItems) {
            isDirty = true;
            SaveToFile();
        }
    }
    
    /// <summary>
    /// Scan a player's inventory and mark all items as seen.
    /// Called when entering a world.
    /// </summary>
    public static void ScanPlayerInventory(Player player)
    {
        EnsureLoaded();
        
        bool anyNewItems = false;
        
        // Helper to add item and check hardmode
        void AddItemIfNew(int itemType)
        {
            if (itemType > ItemID.None && seenItemIds.Add(itemType)) {
                anyNewItems = true;
                // Check if this is a hardmode ore bar
                if (!hasSeenAnyHardmodeItem && HardmodeOreBarItemIds.Contains(itemType)) {
                    hasSeenAnyHardmodeItem = true;
                }
            }
        }
        
        // Main inventory
        foreach (Item item in player.inventory) {
            AddItemIfNew(item.type);
        }
        
        // Equipped armor
        foreach (Item item in player.armor) {
            AddItemIfNew(item.type);
        }
        
        // Accessories
        foreach (Item item in player.miscEquips) {
            AddItemIfNew(item.type);
        }
        
        // Dyes
        foreach (Item item in player.dye) {
            AddItemIfNew(item.type);
        }
        
        // Misc dyes (grapple dye, mount dye, etc.)
        foreach (Item item in player.miscDyes) {
            AddItemIfNew(item.type);
        }
        
        // Trash slot
        AddItemIfNew(player.trashItem.type);
        
        // Piggy bank, Safe, Defender's Forge, Void Vault
        foreach (Item item in player.bank.item) {
            AddItemIfNew(item.type);
        }
        foreach (Item item in player.bank2.item) {
            AddItemIfNew(item.type);
        }
        foreach (Item item in player.bank3.item) {
            AddItemIfNew(item.type);
        }
        foreach (Item item in player.bank4.item) {
            AddItemIfNew(item.type);
        }
        
        if (anyNewItems) {
            isDirty = true;
            SaveToFile();
        }
    }
    
    /// <summary>
    /// Get the path to the save file.
    /// </summary>
    private static string GetSaveFilePath()
    {
        string modFolder = Path.Combine(Main.SavePath, "ModSaves", "DuravoQOLMod");
        Directory.CreateDirectory(modFolder);
        return Path.Combine(modFolder, SAVE_FILE_NAME);
    }
    
    /// <summary>
    /// Ensure data is loaded from file.
    /// </summary>
    private static void EnsureLoaded()
    {
        if (!isLoaded) {
            LoadFromFile();
            isLoaded = true;
        }
    }
    
    /// <summary>
    /// Load seen items from file.
    /// Supports both old format (int[]) and new format (Dictionary with seenItems and hasSeenAnyHardmodeItem).
    /// </summary>
    private static void LoadFromFile()
    {
        try {
            string filePath = GetSaveFilePath();
            if (File.Exists(filePath)) {
                string json = File.ReadAllText(filePath);
                
                // Try new format first (object with seenItems and hasSeenAnyHardmodeItem)
                if (json.TrimStart().StartsWith("{")) {
                    try {
                        var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("seenItems", out var seenItemsElement)) {
                            var idList = new List<int>();
                            foreach (var item in seenItemsElement.EnumerateArray()) {
                                idList.Add(item.GetInt32());
                            }
                            seenItemIds = new HashSet<int>(idList);
                        }
                        
                        if (root.TryGetProperty("hasSeenAnyHardmodeItem", out var hardmodeElement)) {
                            hasSeenAnyHardmodeItem = hardmodeElement.GetBoolean();
                        }
                        
                        isDirty = false;
                        return;
                    }
                    catch {
                        // Fall through to try old format
                    }
                }
                
                // Try old format (just an int array)
                int[]? loadedIds = JsonSerializer.Deserialize<int[]>(json);
                if (loadedIds != null) {
                    seenItemIds = new HashSet<int>(loadedIds);
                    // Scan for any existing hardmode items in old data
                    hasSeenAnyHardmodeItem = false;
                    foreach (int itemId in loadedIds) {
                        if (HardmodeOreBarItemIds.Contains(itemId)) {
                            hasSeenAnyHardmodeItem = true;
                            break;
                        }
                    }
                    // Mark dirty to save in new format
                    isDirty = true;
                }
            }
        }
        catch (Exception) {
            // Log error but don't crash - start fresh if file is corrupted
            seenItemIds = new HashSet<int>();
            hasSeenAnyHardmodeItem = false;
        }
        isDirty = false;
    }
    
    /// <summary>
    /// Save seen items to file in new format using manual JSON building.
    /// </summary>
    private static void SaveToFile()
    {
        if (!isDirty) {
            return;
        }
        
        try {
            string filePath = GetSaveFilePath();
            
            // Build JSON manually to avoid class serialization issues
            var itemArray = new int[seenItemIds.Count];
            seenItemIds.CopyTo(itemArray);
            
            string itemsJson = JsonSerializer.Serialize(itemArray);
            string json = $"{{\"seenItems\":{itemsJson},\"hasSeenAnyHardmodeItem\":{(hasSeenAnyHardmodeItem ? "true" : "false")}}}";
            
            File.WriteAllText(filePath, json);
            isDirty = false;
        }
        catch (Exception) {
            // Silently fail - not critical
        }
    }
    
    /// <summary>
    /// Force save (e.g., on mod unload).
    /// </summary>
    public static void ForceSave()
    {
        if (isDirty) {
            SaveToFile();
        }
    }
    
    /// <summary>
    /// Reset for testing/debugging.
    /// </summary>
    public static void Reset()
    {
        seenItemIds.Clear();
        hasSeenAnyHardmodeItem = false;
        isDirty = true;
        SaveToFile();
    }
}

/// <summary>
/// ModPlayer to hook into item pickup events and world entry.
/// Delegates to the static SeenItemsTracker for actual tracking.
/// </summary>
public class SeenItemsTrackerPlayer : ModPlayer
{
    /// <summary>Last chest index that was scanned (-1 = no chest open)</summary>
    private int lastScannedChestIndex = -1;
    
    public override void OnEnterWorld()
    {
        SeenItemsTracker.ScanPlayerInventory(Player);
        lastScannedChestIndex = -1;
    }
    
    public override bool OnPickup(Item item)
    {
        SeenItemsTracker.MarkItemAsSeen(item.type);
        return true;
    }
    
    /// <summary>Frame counter for throttled recipe scanning</summary>
    private int recipeScanFrameCounter = 0;
    
    /// <summary>How often to scan recipes (every N frames)</summary>
    private const int RECIPE_SCAN_INTERVAL_FRAMES = 30; // ~0.5 seconds at 60fps
    
    public override void PostUpdate()
    {
        // Check if player has a chest open and scan its contents
        int currentChestIndex = Player.chest;
        
        if (currentChestIndex >= 0 && currentChestIndex != lastScannedChestIndex) {
            // Player opened a new chest - scan its contents
            ScanOpenChest(currentChestIndex);
            lastScannedChestIndex = currentChestIndex;
        }
        else if (currentChestIndex < 0) {
            // No chest open, reset tracking
            lastScannedChestIndex = -1;
        }
        
        // Periodically scan available recipes to discover new items
        // This marks items and their ingredients as "seen" when they become craftable
        recipeScanFrameCounter++;
        if (recipeScanFrameCounter >= RECIPE_SCAN_INTERVAL_FRAMES) {
            recipeScanFrameCounter = 0;
            SeenItemsTracker.ScanAvailableRecipes();
        }
    }
    
    /// <summary>
    /// Scan the contents of an open chest and mark items as seen.
    /// </summary>
    private void ScanOpenChest(int chestIndex)
    {
        if (chestIndex < 0 || chestIndex >= Main.maxChests) {
            return;
        }
        
        Chest? chest = Main.chest[chestIndex];
        if (chest?.item == null) {
            return;
        }
        
        foreach (Item item in chest.item) {
            if (item.type > ItemID.None) {
                SeenItemsTracker.MarkItemAsSeen(item.type);
            }
        }
    }
}