using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaSurvivalMod.Items
{
    /// <summary>
    /// Rebalances vanilla armor defense values.
    /// Issue in Vanilla: Upgrading indivudal pieces does not feel meaningful because
    ///           of breaking set bonuses, and by the time you have enough for the full
    ///           next tier, you may have enough to skip 1-2 tiers of armor entirely.
    /// Goal: Make all upgrades feel meaningful. Move all defense to the pieces and buff
    ///       defense so there is more reason for piecemeal upgrades. 
    ///       Set bonuses become utility (handled in ArmorSetBonusPlayer), giving the
    ///       player more of a reason to make and maintain the kits they like.
    /// </summary>
    public class ArmorDefenseRebalance : GlobalItem
    {
        public override void SetDefaults(Item item)
        {
            // Tin Armor: 6 vanilla -> 10 proposed
            switch (item.type)
            {
                // === TIN ARMOR ===
                case ItemID.TinHelmet:
                    item.defense = 3; // was 2
                    break;
                case ItemID.TinChainmail:
                    item.defense = 4; // was 2
                    break;
                case ItemID.TinGreaves:
                    item.defense = 3; // was 2
                    break;

                // === COPPER ARMOR ===
                case ItemID.CopperHelmet:
                    item.defense = 3; // was 1
                    break;
                case ItemID.CopperChainmail:
                    item.defense = 4; // was 2
                    break;
                case ItemID.CopperGreaves:
                    item.defense = 3; // was 1
                    break;

                // === IRON ARMOR ===
                case ItemID.IronHelmet:
                    item.defense = 4; // was 3
                    break;
                case ItemID.IronChainmail:
                    item.defense = 6; // was 4
                    break;
                case ItemID.IronGreaves:
                    item.defense = 4; // was 2
                    break;

                // === LEAD ARMOR ===
                case ItemID.LeadHelmet:
                    item.defense = 4; // was 3
                    break;
                case ItemID.LeadChainmail:
                    item.defense = 6; // was 4
                    break;
                case ItemID.LeadGreaves:
                    item.defense = 4; // was 2
                    break;

                // === SILVER ARMOR ===
                case ItemID.SilverHelmet:
                    item.defense = 5; // was 4
                    break;
                case ItemID.SilverChainmail:
                    item.defense = 7; // was 5
                    break;
                case ItemID.SilverGreaves:
                    item.defense = 5; // was 3
                    break;

                // === TUNGSTEN ARMOR ===
                case ItemID.TungstenHelmet:
                    item.defense = 5; // was 4
                    break;
                case ItemID.TungstenChainmail:
                    item.defense = 7; // was 5
                    break;
                case ItemID.TungstenGreaves:
                    item.defense = 5; // was 3
                    break;

                // === GOLD ARMOR ===
                case ItemID.GoldHelmet:
                    item.defense = 6; // was 5
                    break;
                case ItemID.GoldChainmail:
                    item.defense = 8; // was 6
                    break;
                case ItemID.GoldGreaves:
                    item.defense = 6; // was 4
                    break;

                // === PLATINUM ARMOR ===
                case ItemID.PlatinumHelmet:
                    item.defense = 6; // was 5
                    break;
                case ItemID.PlatinumChainmail:
                    item.defense = 8; // was 6
                    break;
                case ItemID.PlatinumGreaves:
                    item.defense = 6; // was 4
                    break;
            }
        }

        /// <summary>
        /// Add custom tooltip lines describing chestplate and set bonuses.
        /// Also removes vanilla set bonus tooltip since we replace it with our own system.
        /// </summary>
        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            // Remove vanilla "Set bonus: X defense" tooltip line
            tooltips.RemoveAll(line => line.Name == "SetBonus");
            
            string chestplateBonusText = GetChestplateBonusText(item.type);
            bool isOreArmorPiece = IsOreArmorPiece(item.type);

            // Add chestplate-specific bonus first (if applicable)
            if (chestplateBonusText != null)
            {
                tooltips.Add(new TooltipLine(Mod, "ChestplateBonus", chestplateBonusText));
            }

            // Add set bonus for any armor piece
            if (isOreArmorPiece)
            {
                string setTooltip = "[c/ADFF2F:Set Bonus:] Shiny - nearby ores and gems sparkle";
                tooltips.Add(new TooltipLine(Mod, "FullSetBonus", setTooltip));
            }
        }

        /// <summary>
        /// Get the tooltip text for chestplate-specific bonus (null if not a chestplate).
        /// </summary>
        private static string GetChestplateBonusText(int itemType)
        {
            return itemType switch
            {
                // Tin/Copper tier - Emergency Shield
                ItemID.TinChainmail or ItemID.CopperChainmail
                    => "[c/00BFFF:25% Shield for 5s when hit (60s cooldown)]",
                
                // Iron/Lead tier - Crit bonus
                ItemID.IronChainmail or ItemID.LeadChainmail
                    => "[c/00BFFF:+10% critical strike chance]",
                
                // Silver/Tungsten tier - Speed bonus
                ItemID.SilverChainmail or ItemID.TungstenChainmail
                    => "[c/00BFFF:+15% movement speed]",
                
                // Gold/Platinum tier - Enhanced Emergency Shield
                ItemID.GoldChainmail or ItemID.PlatinumChainmail
                    => "[c/00BFFF:25% Shield for 10s when hit (120s cooldown, purges debuffs)]",
                
                _ => null
            };
        }

        /// <summary>
        /// Check if this item is part of an ore armor set.
        /// </summary>
        private static bool IsOreArmorPiece(int itemType)
        {
            return itemType switch
            {
                // Tin set
                ItemID.TinHelmet or ItemID.TinChainmail or ItemID.TinGreaves => true,
                
                // Copper set
                ItemID.CopperHelmet or ItemID.CopperChainmail or ItemID.CopperGreaves => true,
                
                // Iron set
                ItemID.IronHelmet or ItemID.IronChainmail or ItemID.IronGreaves => true,
                
                // Lead set
                ItemID.LeadHelmet or ItemID.LeadChainmail or ItemID.LeadGreaves => true,
                
                // Silver set
                ItemID.SilverHelmet or ItemID.SilverChainmail or ItemID.SilverGreaves => true,
                
                // Tungsten set
                ItemID.TungstenHelmet or ItemID.TungstenChainmail or ItemID.TungstenGreaves => true,
                
                // Gold set
                ItemID.GoldHelmet or ItemID.GoldChainmail or ItemID.GoldGreaves => true,
                
                // Platinum set
                ItemID.PlatinumHelmet or ItemID.PlatinumChainmail or ItemID.PlatinumGreaves => true,
                
                _ => false
            };
        }
    }
}