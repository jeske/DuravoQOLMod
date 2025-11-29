// MIT Licensed - Copyright (c) 2025 David W. Jeske
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DuravoQOLMod.CraftingInfoPanel;

/// <summary>
/// ModPlayer to persist crafting panel settings per-player.
/// Tracks hardmode toggle state and auto-show at benches preference.
/// </summary>
public class CraftingPanelPlayer : ModPlayer
{
    /// <summary>Whether the hardmode view toggle is active for this player</summary>
    private bool isHardmodeToggleActive = false;
    
    /// <summary>Whether to auto-show crafting panel when near crafting benches with inventory open</summary>
    private bool autoShowCraftingPanelAtBenches = true; // Default to enabled
    
    #region Static Accessors for Hardmode Toggle
    
    /// <summary>
    /// Get the hardmode toggle state for the local player.
    /// Returns false if no local player exists.
    /// </summary>
    public static bool LocalPlayerHardmodeToggleActive {
        get {
            if (Main.LocalPlayer?.active != true) {
                return false;
            }
            CraftingPanelPlayer? modPlayer = Main.LocalPlayer.GetModPlayer<CraftingPanelPlayer>();
            return modPlayer?.isHardmodeToggleActive ?? false;
        }
    }
    
    /// <summary>
    /// Set the hardmode toggle state for the local player.
    /// </summary>
    public static void SetLocalPlayerHardmodeToggle(bool active)
    {
        if (Main.LocalPlayer?.active != true) {
            return;
        }
        CraftingPanelPlayer? modPlayer = Main.LocalPlayer.GetModPlayer<CraftingPanelPlayer>();
        if (modPlayer != null) {
            modPlayer.isHardmodeToggleActive = active;
        }
    }
    
    #endregion
    
    #region Static Accessors for Auto-Show at Benches
    
    /// <summary>
    /// Get auto-show at benches preference for local player.
    /// </summary>
    public static bool LocalPlayerAutoShowAtBenches {
        get {
            if (Main.LocalPlayer?.active != true) {
                return true; // Default to enabled
            }
            CraftingPanelPlayer? modPlayer = Main.LocalPlayer.GetModPlayer<CraftingPanelPlayer>();
            return modPlayer?.autoShowCraftingPanelAtBenches ?? true;
        }
    }
    
    /// <summary>
    /// Set auto-show at benches preference for local player.
    /// </summary>
    public static void SetLocalPlayerAutoShowAtBenches(bool enabled)
    {
        if (Main.LocalPlayer?.active != true) {
            return;
        }
        CraftingPanelPlayer? modPlayer = Main.LocalPlayer.GetModPlayer<CraftingPanelPlayer>();
        if (modPlayer != null) {
            modPlayer.autoShowCraftingPanelAtBenches = enabled;
        }
    }
    
    #endregion
    
    public override void PostUpdate()
    {
        // Handle auto-open/auto-close logic for crafting panel
        // Skip if crafting panel feature is disabled entirely
        if (!DuravoQOLModConfig.EnableCraftingPanel) {
            return;
        }
        
        CraftingPanelSystem? system = CraftingPanelSystem.Instance;
        if (system == null) {
            return;
        }
        
        bool inventoryOpen = Main.playerInventory;
        bool nearBench = CraftingPanelSystem.IsNearCraftingStation();
        
        // Auto-open logic:
        // - "Sticky Auto Show" config enabled (global setting)
        // - Player's auto-show toggle is ON (per-player preference)
        // - Inventory open + near bench + panel not already open
        bool stickyAutoShowEnabled = DuravoQOLModConfig.EnableCraftingPanelStickyAutoShow;
        if (stickyAutoShowEnabled && autoShowCraftingPanelAtBenches && inventoryOpen && nearBench && !system.IsPanelVisible) {
            system.OpenPanel(wasAutoOpened: true);
        }
        
        // Auto-close logic: panel was auto-opened + no longer near bench
        if (system.WasAutoOpened && !nearBench) {
            system.ClosePanel();
        }
    }
    
    public override void SaveData(TagCompound tag)
    {
        if (isHardmodeToggleActive) {
            tag["HardmodeToggleActive"] = true;
        }
        
        // Only save if false (true is default)
        if (!autoShowCraftingPanelAtBenches) {
            tag["AutoShowAtBenchesDisabled"] = true;
        }
    }
    
    public override void LoadData(TagCompound tag)
    {
        isHardmodeToggleActive = tag.GetBool("HardmodeToggleActive");
        
        // Default is true, so check if disabled
        autoShowCraftingPanelAtBenches = !tag.GetBool("AutoShowAtBenchesDisabled");
    }
}