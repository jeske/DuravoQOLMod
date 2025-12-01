// MIT Licensed - Copyright (c) 2025 David W. Jeske
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace DuravoQOLMod.MiniPlayerHealthbar
{
    /// <summary>
    /// Renders the mini healthbar below the player's feet.
    /// Shows current health, recently lost health (yellow drain animation),
    /// and emergency shield if active.
    /// </summary>
    public class MiniHealthbarLayer : PlayerDrawLayer
    {
        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                        TUNABLE CONSTANTS                           ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>Healthbar width in pixels</summary>
        private const int HealthbarWidth = 40;

        /// <summary>Healthbar height in pixels</summary>
        private const int HealthbarHeight = 6;

        /// <summary>Border thickness in pixels</summary>
        private const int BorderThickness = 1;

        /// <summary>Offset below player feet (positive = lower)</summary>
        private const int VerticalOffsetFromFeet = 8;

        // Colors
        private static readonly Color ColorBorder = new Color(40, 40, 40, 220);       // Dark gray border
        private static readonly Color ColorBackground = new Color(20, 20, 20, 180);   // Near-black background
        private static readonly Color ColorCurrentHealth = new Color(50, 200, 50, 255);    // Green
        private static readonly Color ColorRecentDamage = new Color(230, 200, 50, 255);    // Yellow/gold
        private static readonly Color ColorShieldLow = new Color(100, 180, 255, 255);      // Light blue (Copper/Silver)
        private static readonly Color ColorShieldHigh = new Color(200, 160, 50, 255);      // Gold (Gold/Platinum)

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          LAYER SETUP                               ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Draw after most layers so healthbar is visible above effects.
        /// </summary>
        public override Position GetDefaultPosition()
        {
            return new AfterParent(PlayerDrawLayers.LastVanillaLayer);
        }

        /// <summary>
        /// Only visible when feature is enabled.
        /// </summary>
        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            // Don't draw for other players' copies in our game
            if (drawInfo.drawPlayer.whoAmI != Main.myPlayer)
                return false;

            // Check feature enabled via cached config
            if (!DuravoQOLModConfig.EnableMiniHealthbar)
                return false;

            // Get the player's healthbar tracker
            var healthbarPlayer = drawInfo.drawPlayer.GetModPlayer<MiniHealthbarPlayer>();
            return healthbarPlayer.ShouldShowHealthbar;
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          DRAWING                                   ║
        // ╚════════════════════════════════════════════════════════════════════╝

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            var healthbarPlayer = player.GetModPlayer<MiniHealthbarPlayer>();

            // Calculate position below player feet
            // Player.position is top-left, player.Height gives full height
            Vector2 playerFeetWorld = player.position + new Vector2(player.width / 2f, player.height);
            Vector2 playerFeetScreen = playerFeetWorld - Main.screenPosition;

            // Center the healthbar horizontally below feet
            float healthbarX = playerFeetScreen.X - HealthbarWidth / 2f;
            float healthbarY = playerFeetScreen.Y + VerticalOffsetFromFeet;

            // Get health ratios
            float currentHealthRatio = healthbarPlayer.CurrentHealthRatio;
            float yellowBarRatio = healthbarPlayer.YellowBarHealthRatio;
            var (hasShield, shieldHP, maxShieldHP, isGoldTier) = healthbarPlayer.GetShieldInfo();
            float shieldRatio = maxShieldHP > 0 ? MathHelper.Clamp((float)shieldHP / maxShieldHP, 0f, 1f) : 0f;

            // Calculate shield width as percentage of health bar
            // Shield overflows past 100% if health is full
            float shieldWidthRatio = 0f;
            if (hasShield && maxShieldHP > 0 && player.statLifeMax2 > 0) {
                // Shield is displayed as portion of max health
                shieldWidthRatio = (float)shieldHP / player.statLifeMax2;
            }

            // Get the texture for drawing solid rectangles
            Texture2D pixelTexture = Terraria.GameContent.TextureAssets.MagicPixel.Value;

            // ─────────────────────────────────────────────────────────────
            // Draw Order (back to front):
            // 1. Background (full width)
            // 2. Shield (if at full health, extends past 100%)
            // 3. Yellow damage indicator (trails current health)
            // 4. Current health (green)
            // 5. Shield (if not at full health, overlays from right)
            // 6. Border (top)
            // ─────────────────────────────────────────────────────────────

            // Inner dimensions (inside border)
            int innerX = (int)healthbarX + BorderThickness;
            int innerY = (int)healthbarY + BorderThickness;
            int innerWidth = HealthbarWidth - BorderThickness * 2;
            int innerHeight = HealthbarHeight - BorderThickness * 2;

            // 1. Background
            Rectangle backgroundRect = new Rectangle((int)healthbarX, (int)healthbarY, HealthbarWidth, HealthbarHeight);
            DrawRectangle(ref drawInfo, pixelTexture, backgroundRect, ColorBackground);

            // 2. Shield overflow (if health is full and shield is active)
            if (hasShield && currentHealthRatio >= 0.99f && shieldWidthRatio > 0) {
                int shieldOverflowWidth = (int)(innerWidth * shieldWidthRatio);
                Rectangle shieldOverflowRect = new Rectangle(
                    innerX + innerWidth,  // Start at right edge of health bar
                    innerY,
                    shieldOverflowWidth,
                    innerHeight
                );
                Color shieldColor = isGoldTier ? ColorShieldHigh : ColorShieldLow;
                DrawRectangle(ref drawInfo, pixelTexture, shieldOverflowRect, shieldColor * 0.7f);
            }

            // 3. Yellow damage indicator (shows where health WAS)
            if (yellowBarRatio > currentHealthRatio) {
                int yellowWidth = (int)(innerWidth * yellowBarRatio);
                Rectangle yellowRect = new Rectangle(innerX, innerY, yellowWidth, innerHeight);
                DrawRectangle(ref drawInfo, pixelTexture, yellowRect, ColorRecentDamage);
            }

            // 4. Current health (green)
            int healthWidth = (int)(innerWidth * currentHealthRatio);
            if (healthWidth > 0) {
                Rectangle healthRect = new Rectangle(innerX, innerY, healthWidth, innerHeight);
                DrawRectangle(ref drawInfo, pixelTexture, healthRect, ColorCurrentHealth);
            }

            // 5. Shield overlay (if NOT at full health, shows as overlay on health bar)
            if (hasShield && currentHealthRatio < 0.99f && shieldWidthRatio > 0) {
                int shieldWidth = (int)(innerWidth * MathHelper.Min(shieldWidthRatio, 1f - currentHealthRatio));
                if (shieldWidth > 0) {
                    Rectangle shieldRect = new Rectangle(
                        innerX + healthWidth,  // Start where health ends
                        innerY,
                        shieldWidth,
                        innerHeight
                    );
                    Color shieldColor = isGoldTier ? ColorShieldHigh : ColorShieldLow;
                    DrawRectangle(ref drawInfo, pixelTexture, shieldRect, shieldColor);
                }
            }

            // 6. Border (drawn as 4 lines around the health bar)
            DrawBorder(ref drawInfo, pixelTexture, (int)healthbarX, (int)healthbarY, HealthbarWidth, HealthbarHeight, ColorBorder);
        }

        /// <summary>
        /// Draw a filled rectangle.
        /// </summary>
        private void DrawRectangle(ref PlayerDrawSet drawInfo, Texture2D texture, Rectangle rect, Color color)
        {
            DrawData rectData = new DrawData(
                texture,
                new Vector2(rect.X, rect.Y),
                new Rectangle(0, 0, 1, 1),
                color,
                0f,
                Vector2.Zero,
                new Vector2(rect.Width, rect.Height),
                SpriteEffects.None,
                0
            );
            drawInfo.DrawDataCache.Add(rectData);
        }

        /// <summary>
        /// Draw a 1-pixel border around a rectangle.
        /// </summary>
        private void DrawBorder(ref PlayerDrawSet drawInfo, Texture2D texture, int x, int y, int width, int height, Color color)
        {
            // Top edge
            DrawRectangle(ref drawInfo, texture, new Rectangle(x, y, width, BorderThickness), color);
            // Bottom edge
            DrawRectangle(ref drawInfo, texture, new Rectangle(x, y + height - BorderThickness, width, BorderThickness), color);
            // Left edge
            DrawRectangle(ref drawInfo, texture, new Rectangle(x, y, BorderThickness, height), color);
            // Right edge
            DrawRectangle(ref drawInfo, texture, new Rectangle(x + width - BorderThickness, y, BorderThickness, height), color);
        }
    }
}