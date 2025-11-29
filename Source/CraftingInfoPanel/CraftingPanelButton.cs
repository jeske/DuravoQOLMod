using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace DuravoQOLMod.CraftingInfoPanel
{
    /// <summary>
    /// Static class that draws the Crafting Panel toggle button.
    /// Positioned in lower-left area of screen, near vanilla crafting button.
    /// Visible whenever inventory is open.
    /// </summary>
    public static class CraftingPanelButton
    {
        /// <summary>Button dimensions</summary>
        private const int BUTTON_SIZE = 32;

        /// <summary>
        /// Position offset from lower-left corner.
        /// Button sits in the very corner of the screen.
        /// </summary>
        private const int BUTTON_LEFT_MARGIN = 10;  // Near left edge
        private const int BUTTON_BOTTOM_MARGIN = 10;  // Near bottom edge

        /// <summary>
        /// Draw the toggle button. Called each frame when inventory is open.
        /// </summary>
        public static void Draw(SpriteBatch spriteBatch)
        {
            // Calculate button position (lower-left corner)
            // Y needs to account for button size so the entire button is on screen
            float buttonX = BUTTON_LEFT_MARGIN;
            float buttonY = Main.screenHeight - BUTTON_BOTTOM_MARGIN - BUTTON_SIZE;

            Rectangle buttonRect = new Rectangle((int)buttonX, (int)buttonY, BUTTON_SIZE, BUTTON_SIZE);

            // Check if panel is currently visible
            bool isPanelVisible = CraftingPanelSystem.Instance?.IsPanelVisible ?? false;

            // Check if near a crafting station (changes button appearance)
            bool isNearStation = CraftingPanelSystem.IsNearCraftingStation();

            // Button colors - show different states
            Color buttonBgColor;
            Color buttonBorderColor;

            if (isPanelVisible) {
                // Panel open - highlighted golden
                buttonBgColor = new Color(90, 74, 42, 230);
                buttonBorderColor = new Color(240, 192, 96);
            }
            else if (isNearStation) {
                // Near station - available, slightly highlighted
                buttonBgColor = new Color(58, 58, 90, 230);
                buttonBorderColor = new Color(138, 106, 74);
            }
            else {
                // No station nearby - dimmed but still clickable
                buttonBgColor = new Color(42, 42, 58, 200);
                buttonBorderColor = new Color(90, 90, 106);
            }

            // Check hover
            bool isHovering = buttonRect.Contains(Main.mouseX, Main.mouseY);
            if (isHovering) {
                buttonBorderColor = new Color(240, 192, 96);  // Golden hover highlight
                // Block game input when mouse is over button
                Main.LocalPlayer.mouseInterface = true;
            }

            Texture2D pixel = TextureAssets.MagicPixel.Value;

            // Draw button border
            int borderWidth = 2;
            Rectangle borderRect = new Rectangle(
                (int)buttonX - borderWidth,
                (int)buttonY - borderWidth,
                BUTTON_SIZE + borderWidth * 2,
                BUTTON_SIZE + borderWidth * 2
            );
            spriteBatch.Draw(pixel, borderRect, buttonBorderColor);

            // Draw button background
            spriteBatch.Draw(pixel, buttonRect, buttonBgColor);

            // Draw button icon - crafting hammer texture from Terraria
            // CraftToggle is an array: [0] = off state, [1] = on state
            int textureIndex = isPanelVisible ? 1 : 0;
            Texture2D craftToggleTexture = TextureAssets.CraftToggle[textureIndex].Value;
            Rectangle sourceRect = new Rectangle(0, 0, craftToggleTexture.Width, craftToggleTexture.Height);

            // Calculate scale to fit within button (with some padding)
            float iconPadding = 4;
            float maxIconSize = BUTTON_SIZE - iconPadding * 2;
            float scaleX = maxIconSize / craftToggleTexture.Width;
            float scaleY = maxIconSize / craftToggleTexture.Height;
            float iconScale = System.Math.Min(scaleX, scaleY);
            
            // Center the icon in the button
            Vector2 iconPosition = new Vector2(
                buttonX + BUTTON_SIZE / 2f,
                buttonY + BUTTON_SIZE / 2f
            );
            Vector2 iconOrigin = new Vector2(craftToggleTexture.Width / 2f, craftToggleTexture.Height / 2f);

            // Tint based on state
            Color iconTint = isPanelVisible
                ? new Color(240, 192, 96)  // Golden when active
                : (isNearStation ? Color.White : new Color(150, 150, 150));  // Dimmed when no station

            spriteBatch.Draw(
                craftToggleTexture,
                iconPosition,
                sourceRect,
                iconTint,
                0f,
                iconOrigin,
                iconScale,
                SpriteEffects.None,
                0f
            );

            // Draw tooltip on hover
            if (isHovering) {
                bool autoShowEnabled = CraftingPanelPlayer.LocalPlayerAutoShowAtBenches;
                string tooltip;
                
                if (isNearStation) {
                    // Near station: clicking toggles auto-show setting
                    tooltip = autoShowEnabled
                        ? "Disable auto-show at benches\n(Currently: ON)"
                        : "Enable auto-show at benches\n(Currently: OFF)";
                }
                else {
                    // Not near station: regular toggle
                    tooltip = isPanelVisible
                        ? "Hide Crafting Panel"
                        : "Show Crafting Panel";
                    tooltip += "\n(No crafting station nearby)";
                }

                Main.hoverItemName = tooltip;
            }

            // Handle click
            if (isHovering && Main.mouseLeft && Main.mouseLeftRelease) {
                if (isNearStation) {
                    // Near station: toggle auto-show setting and panel
                    bool newAutoShowState = !CraftingPanelPlayer.LocalPlayerAutoShowAtBenches;
                    CraftingPanelPlayer.SetLocalPlayerAutoShowAtBenches(newAutoShowState);
                    
                    // Also toggle panel to match new state
                    CraftingPanelSystem.Instance?.TogglePanel();
                }
                else {
                    // Not near station: just toggle panel
                    CraftingPanelSystem.Instance?.TogglePanel();
                }
                Main.mouseLeftRelease = false;  // Consume click
            }
        }
    }
}