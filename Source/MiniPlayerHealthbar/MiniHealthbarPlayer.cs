// MIT Licensed - Copyright (c) 2025 David W. Jeske
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using DuravoQOLMod.ArmorRebalance;

namespace DuravoQOLMod.MiniPlayerHealthbar
{
    /// <summary>
    /// Tracks player health state for the mini healthbar display.
    /// Monitors recent damage to show the "yellow drain" animation effect.
    /// </summary>
    public class MiniHealthbarPlayer : ModPlayer
    {
        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                        CONSTANTS & CONFIG                          ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>When health drops below this value (4 hearts), always show the healthbar (fallback threshold)</summary>
        private const int HealthAbsoluteThreshold = 80; // 4 hearts

        /// <summary>How much of max health to decay per second (10% = drains full bar in 10s)</summary>
        private const float RecentHealthDecayPerSecond = 0.10f;

        // Config-driven values (accessed via cached static properties):
        // - DuravoQOLModConfig.MiniHealthbarShowAtHealthPercent (0.0 to 1.0)
        // - DuravoQOLModConfig.MiniHealthbarShowOnDamagePercent (0.0 to 1.0)
        // - DuravoQOLModConfig.MiniHealthbarAutoHideSeconds (int seconds)

        /// <summary>Get the linger time in ticks from config (seconds * 60)</summary>
        private static int HealthbarLingerTicks => (int)(DuravoQOLModConfig.MiniHealthbarAutoHideSeconds * 60f);

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          INSTANCE STATE                            ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>The health value displayed by the recent damage bar (trails actual health)</summary>
        private int recentHealthValue;

        /// <summary>How many ticks the healthbar should remain visible</summary>
        private int healthbarVisibleTicks;

        /// <summary>Track previous health to detect damage</summary>
        private int previousHealth;

        /// <summary>Accumulated damage in recent window for threshold check</summary>
        private int recentDamageAccumulator;

        /// <summary>Ticks until recent damage accumulator resets</summary>
        private int recentDamageResetTicks;

        /// <summary>How long to accumulate damage before the window resets (1 second @ 60fps)</summary>
        private const int RecentDamageWindowTicks = 60; // 1 second

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          PUBLIC PROPERTIES                         ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>Whether the mini healthbar should currently be displayed</summary>
        public bool ShouldShowHealthbar => DuravoQOLModConfig.EnableMiniHealthbar
            && (DuravoQOLModConfig.EnableMiniHealthbarAlwaysOn || healthbarVisibleTicks > 0 || CheckShowConditions());

        /// <summary>Current health as a ratio (0.0 to 1.0)</summary>
        public float CurrentHealthRatio => Player.statLifeMax2 > 0
            ? MathHelper.Clamp((float)Player.statLife / Player.statLifeMax2, 0f, 1f)
            : 0f;

        /// <summary>Recent health as a ratio (trails actual health for damage animation)</summary>
        public float RecentHealthRatio => Player.statLifeMax2 > 0
            ? MathHelper.Clamp((float)recentHealthValue / Player.statLifeMax2, 0f, 1f)
            : 0f;

        /// <summary>Get shield info from armor system if available</summary>
        public (bool hasShield, int shieldHP, int maxShieldHP, bool isGoldTier) GetShieldInfo()
        {
            var shieldPlayer = Player.GetModPlayer<EmergencyShieldPlayer>();
            if (shieldPlayer != null && shieldPlayer.HasActiveShield) {
                // Calculate max shield HP based on tier
                // CopperTin = 30HP flat, GoldPlatinum = 15% of max health
                int maxShieldHP = shieldPlayer.IsGoldTierShield
                    ? (int)(Player.statLifeMax2 * 0.15f)
                    : 30;
                return (true, shieldPlayer.CurrentShieldHP, maxShieldHP, shieldPlayer.IsGoldTierShield);
            }
            return (false, 0, 0, false);
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          INITIALIZATION                            ║
        // ╚════════════════════════════════════════════════════════════════════╝

        public override void Initialize()
        {
            recentHealthValue = 0;
            healthbarVisibleTicks = 0;
            previousHealth = 0;
            recentDamageAccumulator = 0;
            recentDamageResetTicks = 0;
        }

        public override void OnEnterWorld()
        {
            // Initialize to current health so recent damage bar starts correct
            recentHealthValue = Player.statLife;
            previousHealth = Player.statLife;
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          TICK UPDATES                              ║
        // ╚════════════════════════════════════════════════════════════════════╝

        public override void PostUpdate()
        {
            UpdateDamageTracking();
            UpdateRecentHealthDecay();
            UpdateVisibility();
        }

        /// <summary>
        /// Track damage taken this frame and accumulate for threshold checks.
        /// </summary>
        private void UpdateDamageTracking()
        {
            int currentHealth = Player.statLife;
            int damageTaken = previousHealth - currentHealth;

            // Detect damage (positive change means health went down)
            if (damageTaken > 0) {
                // Accumulate recent damage for threshold check
                recentDamageAccumulator += damageTaken;
                recentDamageResetTicks = RecentDamageWindowTicks;

                // Show healthbar
                healthbarVisibleTicks = HealthbarLingerTicks;
            }

            // Detect healing - snap recentHealthValue up to current health
            if (currentHealth > recentHealthValue) {
                recentHealthValue = currentHealth;
            }

            previousHealth = currentHealth;

            // Decay recent damage accumulator for threshold check
            if (recentDamageResetTicks > 0) {
                recentDamageResetTicks--;
                if (recentDamageResetTicks == 0) {
                    recentDamageAccumulator = 0;
                }
            }
        }

        /// <summary>
        /// Decay recentHealthValue toward current health at a fixed rate.
        /// 10% of max health per second = ~0.17% per tick.
        /// </summary>
        private void UpdateRecentHealthDecay()
        {
            int currentHealth = Player.statLife;

            // If recentHealthValue > currentHealth, decay it down
            if (recentHealthValue > currentHealth) {
                // Calculate decay amount: 10% of max health per second / 60 ticks
                int decayPerTick = (int)System.Math.Max(1, Player.statLifeMax2 * RecentHealthDecayPerSecond / 60f);
                
                recentHealthValue -= decayPerTick;
                
                // Don't go below current health
                if (recentHealthValue < currentHealth) {
                    recentHealthValue = currentHealth;
                }
            }
        }

        /// <summary>
        /// Update healthbar visibility timer.
        /// </summary>
        private void UpdateVisibility()
        {
            // Check if conditions warrant showing
            if (CheckShowConditions()) {
                healthbarVisibleTicks = HealthbarLingerTicks;
            }

            // Decay visibility timer
            if (healthbarVisibleTicks > 0) {
                healthbarVisibleTicks--;
            }
        }

        /// <summary>
        /// Check if any condition for showing the healthbar is met.
        /// </summary>
        private bool CheckShowConditions()
        {
            // Condition 1: Health below percentage threshold (from config slider)
            float healthThreshold = DuravoQOLModConfig.MiniHealthbarShowAtHealthPercent;
            if (healthThreshold > 0 && CurrentHealthRatio < healthThreshold) {
                return true;
            }

            // Condition 2: Health below absolute threshold (4 hearts - hardcoded fallback)
            if (Player.statLife < HealthAbsoluteThreshold) {
                return true;
            }

            // Condition 3: Recent damage exceeds threshold (from config slider)
            float damageThreshold = DuravoQOLModConfig.MiniHealthbarShowOnDamagePercent;
            if (damageThreshold > 0) {
                float recentDamagePercent = Player.statLifeMax2 > 0
                    ? (float)recentDamageAccumulator / Player.statLifeMax2
                    : 0f;
                if (recentDamagePercent >= damageThreshold) {
                    return true;
                }
            }

            // Condition 4: Shield is active
            var shieldPlayer = Player.GetModPlayer<EmergencyShieldPlayer>();
            if (shieldPlayer != null && shieldPlayer.HasActiveShield) {
                return true;
            }

            return false;
        }
    }
}