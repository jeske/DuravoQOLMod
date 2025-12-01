namespace DuravoQOLMod.CraftingInfoPanel;

/// <summary>
/// Static holder for the currently hovered crafting panel item ID.
/// Used to communicate between CraftingInfoPanelUI and any tooltip modifications.
/// </summary>
public static class CraftingPanelTooltipGlobalItem
{
    /// <summary>
    /// The item ID currently being hovered in the crafting panel.
    /// Set to -1 when not hovering any panel item.
    /// </summary>
    public static int HoveredCraftingPanelItemId = -1;
}