// MIT Licensed - Copyright (c) 2025 David W. Jeske
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace DuravoQOLMod.MiniPlayerHealthbar
{
    /// <summary>
    /// Renders the mini healthbar below the player's feet in world space.
    /// Uses vanilla HB1/HB2 textures to match native NPC healthbar style.
    /// Shows current health, recently lost health (yellow drain animation),
    /// and emergency shield if active.
    /// </summary>
    public class MiniHealthbarSystem : ModSystem
    {
        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                        TUNABLE CONSTANTS                           ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>Healthbar width in pixels (vanilla enemy bars are ~36)</summary>
        private const int HealthbarWidth = 40;

        /// <summary>Offset below player feet (positive = lower)</summary>
        private const int VerticalOffsetFromFeet = 8;

        /// <summary>Padding between HB2 (background) edge and HB1 (fill)</summary>
        private const int FillPaddingX = 2;
        private const int FillPaddingY = 2;

        // Shield colors
        private static readonly Color ColorShieldLow = new Color(100, 180, 255, 255);   // Light blue (Copper/Silver)
        private static readonly Color ColorShieldHigh = new Color(200, 160, 50, 255);   // Gold (Gold/Platinum)

        // HUE CONSTANTS (HSL hue values)
        private const float HUE_RED = 0.0f;      // 0 degrees
        private const float HUE_YELLOW = 0.15f;  // ~60 degrees (biased more orange for visibility)
        private const float HUE_GREEN = 0.33f;   // ~120 degrees

        /// <summary>
        /// Health color gradient definition. Each entry is (Threshold, Hue).
        /// Colors lerp between adjacent entries. Same-color pairs create solid bands.
        /// Ordered highest to lowest threshold.
        /// </summary>
        private static readonly (float Threshold, float Hue)[] HealthColorGradient = {
            (1.00f, HUE_GREEN),   // Top of green band
            (0.65f, HUE_GREEN),   // Bottom of green band → transition starts
            (0.60f, HUE_YELLOW),  // Top of yellow band
            (0.40f, HUE_YELLOW),  // Bottom of yellow band → transition starts
            (0.35f, HUE_RED),     // Top of red band
            (0.00f, HUE_RED),     // Bottom of red band
        };

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          LAYER SETUP                               ║
        // ╚════════════════════════════════════════════════════════════════════╝

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            // Insert our layer right after the Vanilla Entity Health Bars
            // so it renders on top of enemies if they overlap, but under the main UI.
            int entityHealthBarsIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Entity Health Bars"));

            if (entityHealthBarsIndex != -1) {
                layers.Insert(entityHealthBarsIndex + 1, new LegacyGameInterfaceLayer(
                    "DuravoQOL: Player Mini Healthbar",
                    delegate {
                        DrawPlayerHealthbar();
                        return true;
                    },
                    InterfaceScaleType.Game // World-space positioning
                ));
            }
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          DRAWING                                   ║
        // ╚════════════════════════════════════════════════════════════════════╝

        private void DrawPlayerHealthbar()
        {
            Player player = Main.LocalPlayer;
            if (player.dead || player.ghost)
                return;

            // Check feature enabled via cached config
            if (!DuravoQOLModConfig.EnableMiniHealthbar)
                return;

            // Get the player's healthbar tracker
            var healthbarPlayer = player.GetModPlayer<MiniHealthbarPlayer>();
            if (!healthbarPlayer.ShouldShowHealthbar)
                return;

            // 1. Calculate Screen Position (world position converted to screen)
            // Player.position is top-left, player.Height gives full height
            Vector2 playerFeetWorld = new Vector2(
                player.Center.X,
                player.position.Y + player.height + VerticalOffsetFromFeet
            );
            Vector2 screenPos = playerFeetWorld - Main.screenPosition;

            // 2. Get Vanilla Textures
            Texture2D textureBackground = TextureAssets.Hb2.Value; // Dark border/background
            Texture2D textureFill = TextureAssets.Hb1.Value;       // White fill (tinted by color)

            // 3. Calculate Dimensions
            int barWidth = HealthbarWidth;
            int barHeight = textureBackground.Height; // Use vanilla texture height

            // 4. Get health ratios from tracker
            float currentHealthRatio = healthbarPlayer.CurrentHealthRatio;
            float recentHealthRatio = healthbarPlayer.RecentHealthRatio;
            var (hasShield, shieldHP, maxShieldHP, isGoldTier) = healthbarPlayer.GetShieldInfo();

            // Calculate shield width as percentage of health bar
            float shieldWidthRatio = 0f;
            if (hasShield && maxShieldHP > 0 && player.statLifeMax2 > 0) {
                shieldWidthRatio = (float)shieldHP / player.statLifeMax2;
            }

            // 5. Calculate fill widths
            int shieldFillWidth = hasShield ? (int)(barWidth * MathHelper.Min(shieldWidthRatio, 1f)) : 0;

            // 6. Centering Logic - center the bar horizontally on screen position
            int drawX = (int)(screenPos.X - barWidth / 2);
            int drawY = (int)screenPos.Y;

            // Snap to integer to prevent shimmer during movement
            Rectangle backgroundRect = new Rectangle(drawX, drawY, barWidth, barHeight);

            // ─────────────────────────────────────────────────────────────
            // Draw Order (back to front):
            // 1. Background (HB2) - dark border
            // 2. Recent damage bar (same traffic light color at 50% brightness)
            // 3. Current health (with traffic light color at full brightness)
            // 4. Shield (if active)
            // ─────────────────────────────────────────────────────────────

            SpriteBatch spriteBatch = Main.spriteBatch;

            // Get environment lighting at player position for cave darkness effect
            Color environmentLight = Lighting.GetColor((int)(player.Center.X / 16f), (int)(player.Center.Y / 16f));
            float rawLightBrightness = (environmentLight.R + environmentLight.G + environmentLight.B) / (255f * 3f);

            // Apply lighting boost: lerp between environment lighting and fully lit (1.0)
            // 0% = use raw environment lighting, 100% = always fully lit
            float boostedLightBrightness = MathHelper.Lerp(
                rawLightBrightness,
                1.0f,
                DuravoQOLModConfig.MiniHealthbarLightingBoost
            );

            // Remap brightness using config values (MinBrightness = darkness, MaxBrightness = full light)
            float lightBrightness = MathHelper.Lerp(
                DuravoQOLModConfig.MiniHealthbarMinBrightness,
                DuravoQOLModConfig.MiniHealthbarMaxBrightness,
                boostedLightBrightness
            );

            // 1. Draw Background (HB2) - stretched to full width
            spriteBatch.Draw(textureBackground, backgroundRect, Color.White * lightBrightness);

            // Calculate inner fill area (inside the border)
            int innerX = backgroundRect.X + FillPaddingX;
            int innerY = backgroundRect.Y + FillPaddingY;
            int innerWidth = barWidth - (FillPaddingX * 2);
            int innerHeight = barHeight - (FillPaddingY * 2);

            // Get the traffic light color based on current health, using lightBrightness for luminance
            Color healthColor = GetTrafficLightHealthColor(currentHealthRatio, lightBrightness);

            // 2. Draw Recent damage bar (same color at 50% brightness, trails behind current)
            if (recentHealthRatio > currentHealthRatio) {
                int recentFillWidth = (int)(innerWidth * recentHealthRatio);
                if (recentFillWidth > 0) {
                    Rectangle recentRect = new Rectangle(innerX, innerY, recentFillWidth, innerHeight);
                    // Same hue/saturation but 50% of the luminance
                    Color recentDamageColor = GetTrafficLightHealthColor(currentHealthRatio, lightBrightness * 0.5f);
                    spriteBatch.Draw(textureFill, recentRect, recentDamageColor);
                }
            }

            // 3. Draw Current health with traffic light color at full brightness
            int healthFillInnerWidth = (int)(innerWidth * currentHealthRatio);
            if (healthFillInnerWidth > 0) {
                Rectangle healthRect = new Rectangle(innerX, innerY, healthFillInnerWidth, innerHeight);
                spriteBatch.Draw(textureFill, healthRect, healthColor);
            }

            // 4. Draw Shield (if active)
            if (hasShield && shieldFillWidth > 0) {
                Color shieldColor = isGoldTier ? ColorShieldHigh : ColorShieldLow;

                if (currentHealthRatio >= 0.99f) {
                    // At full health: shield overflows past the right edge
                    int shieldOverflowWidth = (int)(innerWidth * shieldWidthRatio);
                    Rectangle shieldRect = new Rectangle(
                        innerX + innerWidth,  // Start at right edge
                        innerY,
                        shieldOverflowWidth,
                        innerHeight
                    );
                    spriteBatch.Draw(textureFill, shieldRect, shieldColor * 0.8f * lightBrightness);
                }
                else {
                    // Not at full health: shield overlays the empty portion
                    int shieldDisplayWidth = (int)(innerWidth * MathHelper.Min(shieldWidthRatio, 1f - currentHealthRatio));
                    if (shieldDisplayWidth > 0) {
                        int shieldStartX = innerX + (int)(innerWidth * currentHealthRatio);
                        Rectangle shieldRect = new Rectangle(shieldStartX, innerY, shieldDisplayWidth, innerHeight);
                        spriteBatch.Draw(textureFill, shieldRect, shieldColor * lightBrightness);
                    }
                }
            }
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                      HEALTH COLOR ALGORITHM                        ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Get the health color by interpolating through the HealthColorGradient array.
        /// Walks the gradient to find two adjacent entries, then lerps between their hues.
        /// </summary>
        /// <param name="healthRatio">0.0 to 1.0 health percentage</param>
        /// <param name="brightness">Environment lighting brightness (0.0 to 1.0)</param>
        private Color GetTrafficLightHealthColor(float healthRatio, float brightness)
        {
            float hue = HUE_RED; // Default fallback

            // Walk the gradient to find the two entries we're between
            for (int i = 0; i < HealthColorGradient.Length - 1; i++) {
                var upper = HealthColorGradient[i];
                var lower = HealthColorGradient[i + 1];

                if (healthRatio <= upper.Threshold && healthRatio >= lower.Threshold) {
                    // Found our band - lerp between the two hues
                    float range = upper.Threshold - lower.Threshold;
                    if (range > 0) {
                        float progress = (healthRatio - lower.Threshold) / range;
                        hue = MathHelper.Lerp(lower.Hue, upper.Hue, progress);
                    }
                    else {
                        hue = upper.Hue; // Zero range, use upper
                    }
                    break;
                }
            }

            // Handle edge case: health above maximum gradient threshold
            if (healthRatio > HealthColorGradient[0].Threshold) {
                hue = HealthColorGradient[0].Hue;
            }

            // Convert HSL to RGB (Saturation 1.0 = vibrant, Luminosity 0.5 = pure color)
            Color finalColor = Main.hslToRgb(hue, 1f, 0.5f);

            // Apply lighting brightness
            return finalColor * brightness;
        }
    }
}