using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using System;

namespace TerrariaSurvivalMod.Players
{
    /// <summary>
    /// Handles armor set bonuses including the Emergency Shield mechanic.
    /// Set bonuses are utility-focused, not additional defense.
    /// </summary>
    public class ArmorSetBonusPlayer : ModPlayer
    {
        // === SHIELD STATE ===
        /// <summary>Remaining shield HP that absorbs damage</summary>
        private int emergencyShieldHP;
        
        /// <summary>Maximum shield HP when activated (for visual ratio)</summary>
        private int emergencyShieldMaxHP;
        
        /// <summary>Remaining duration in ticks (60 ticks = 1 second)</summary>
        private int emergencyShieldDurationTicks;
        
        /// <summary>Cooldown remaining before shield can trigger again</summary>
        private int emergencyShieldCooldownTicks;

        // === SET BONUS TRACKING ===
        /// <summary>Current chestplate tier for single-piece bonuses</summary>
        private ChestplateTier currentChestplateTier = ChestplateTier.None;
        
        /// <summary>Whether player is wearing a full ore armor set (any tier)</summary>
        private bool hasFullOreArmorSet = false;

        /// <summary>Chestplate tiers for single-piece bonuses</summary>
        public enum ChestplateTier
        {
            None,
            TinCopper,      // Emergency Shield (5s duration, 60s cooldown)
            IronLead,       // +10% crit chance
            SilverTungsten, // +15% move speed
            GoldPlatinum    // Emergency Shield (10s duration, 120s cooldown, purges debuffs)
        }

        // === SPARKLE TRACKING ===
        /// <summary>Active sparkles being rendered with manual fade control</summary>
        private static readonly System.Collections.Generic.List<ActiveSparkle> activeSparkles = new System.Collections.Generic.List<ActiveSparkle>();
        
        /// <summary>Tracks an active sparkle with spawn time for fade calculation</summary>
        private struct ActiveSparkle
        {
            public Vector2 WorldPosition;
            public double SpawnTimeSeconds;
            public Color SparkleColor;
            public float InitialScale;
        }
        
        /// <summary>Duration in seconds for sparkle fade-out</summary>
        private const double SparkleFadeDurationSeconds = 1.0;

        // === ORE DETECTION CONSTANTS ===
        /// <summary>Range in tiles for mini-spelunker ore glow effect</summary>
        private const int OreDetectionRangeTiles = 4;
        
        /// <summary>TESTING FLAG: Set to true to always enable ore glow regardless of armor</summary>
        private const bool AlwaysEnableOreGlow = true;
        
        /// <summary>TESTING FLAG: Set to true to also highlight stone (for easier testing)</summary>
        private const bool AlsoHighlightStone = true;

        // === BUFF STATE ===
        /// <summary>Set by ShinyBuff.Update() when the buff is active</summary>
        public bool HasShinyBuff { get; set; }

        /// <summary>
        /// Check if emergency shield is currently active and has HP remaining.
        /// </summary>
        public bool HasActiveShield => emergencyShieldHP > 0 && emergencyShieldDurationTicks > 0;

        /// <summary>
        /// Current shield HP for display purposes.
        /// </summary>
        public int CurrentShieldHP => emergencyShieldHP;

        /// <summary>
        /// Ratio of current shield HP to maximum (for visual fill).
        /// </summary>
        public float ShieldRatio => emergencyShieldMaxHP > 0
            ? (float)emergencyShieldHP / emergencyShieldMaxHP
            : 0f;

        /// <summary>
        /// Whether the current shield is from Gold/Platinum tier (affects color).
        /// </summary>
        public bool IsGoldTierShield => currentChestplateTier == ChestplateTier.GoldPlatinum;

        public override void ResetEffects()
        {
            // Reset detection each frame (UpdateEquips will re-detect)
            currentChestplateTier = ChestplateTier.None;
            hasFullOreArmorSet = false;
            HasShinyBuff = false; // Reset each frame, buff Update() will re-set if active
        }

        public override void UpdateEquips()
        {
            // Detect chestplate tier for main bonuses
            currentChestplateTier = DetectChestplateTier();
            
            // Detect if wearing full set for light bonus
            hasFullOreArmorSet = DetectFullOreArmorSet();

            // Apply CHESTPLATE bonuses (tier-specific utility)
            switch (currentChestplateTier)
            {
                case ChestplateTier.IronLead:
                    // +10% crit chance from Iron/Lead chestplate
                    Player.GetCritChance(DamageClass.Generic) += 10f;
                    break;

                case ChestplateTier.SilverTungsten:
                    // +15% movement speed from Silver/Tungsten chestplate
                    Player.moveSpeed += 0.15f;
                    break;
                
                // Shield bonuses (TinCopper, GoldPlatinum) handled in OnHurt
            }
            
            // Apply Shiny buff when wearing full ore armor set
            // AlwaysEnableOreGlow flag for testing - when false, requires hasFullOreArmorSet
            if (AlwaysEnableOreGlow || hasFullOreArmorSet)
            {
                // Add the Shiny buff (infinite duration since we re-add each frame)
                Player.AddBuff(ModContent.BuffType<Buffs.ShinyBuff>(), 2); // 2 ticks, refreshed every frame
            }
        }

        /// <summary>
        /// Called after buffs are processed - apply ore glow if Shiny buff is active.
        /// </summary>
        public override void PostUpdateBuffs()
        {
            // TESTING: Apply directly when AlwaysEnableOreGlow is true, regardless of buff
            if (AlwaysEnableOreGlow || HasShinyBuff)
            {
                SpawnNewSparkles();
                UpdateAndCleanupSparkles();
            }
        }
        
        /// <summary>
        /// Spawn new sparkles for ore tiles (replaces ApplyMiniSpelunkerOreGlow for spawning).
        /// </summary>
        private void SpawnNewSparkles()
        {
            // Player's "eye" position for calculating incident rays
            Vector2 playerEyePosition = Player.Center + new Vector2(0, -8f);
            
            int playerTileX = (int)(Player.Center.X / 16f);
            int playerTileY = (int)(Player.Center.Y / 16f);

            // Scan tiles within range
            for (int scanTileX = playerTileX - OreDetectionRangeTiles; scanTileX <= playerTileX + OreDetectionRangeTiles; scanTileX++)
            {
                for (int scanTileY = playerTileY - OreDetectionRangeTiles; scanTileY <= playerTileY + OreDetectionRangeTiles; scanTileY++)
                {
                    // Bounds check
                    if (!WorldGen.InWorld(scanTileX, scanTileY, 1))
                        continue;

                    Tile scannedTile = Main.tile[scanTileX, scanTileY];
                    
                    // Check if tile exists and is a spelunker-type tile (ores, gems, etc)
                    bool isSpelunkerTile = Main.tileSpelunker[scannedTile.TileType];
                    bool isStoneForTesting = AlsoHighlightStone && scannedTile.TileType == TileID.Stone;
                    
                    if (scannedTile.HasTile && (isSpelunkerTile || isStoneForTesting))
                    {
                        TrySpawnSparkleForTile(scanTileX, scanTileY, playerEyePosition, scannedTile.TileType);
                    }
                }
            }
        }
        
        /// <summary>
        /// Update sparkle fade progress and remove expired ones.
        /// </summary>
        private static void UpdateAndCleanupSparkles()
        {
            double currentTimeSeconds = DateTime.Now.TimeOfDay.TotalSeconds;
            
            for (int i = activeSparkles.Count - 1; i >= 0; i--)
            {
                double ageSeconds = currentTimeSeconds - activeSparkles[i].SpawnTimeSeconds;
                
                // Handle midnight wraparound
                if (ageSeconds < 0) ageSeconds += 86400.0;
                
                if (ageSeconds > SparkleFadeDurationSeconds)
                {
                    activeSparkles.RemoveAt(i);
                }
            }
        }
        
        /// <summary>
        /// Try to spawn a sparkle for a specific tile (time-throttled).
        /// </summary>
        private void TrySpawnSparkleForTile(int tileX, int tileY, Vector2 playerEyePosition, int tileType)
        {
            int tileCoordinateHash = HashTileCoordinates(tileX, tileY);
            int dustType = GetDustTypeForTile(tileType);
            Vector2 primarySurfaceNormal = GetDeterministicPrimarySurfaceNormal(tileCoordinateHash);
            
            for (int sparkleIndex = 0; sparkleIndex < 2; sparkleIndex++)
            {
                Vector2 sparkleOffsetWithinTile = GetDeterministicSparkleOffset(tileCoordinateHash, sparkleIndex);
                Vector2 sparkleWorldPosition = new Vector2(tileX * 16, tileY * 16) + sparkleOffsetWithinTile;
                
                // Time-based spawn throttling: each sparkle spawns every 2 seconds
                const double SpawnIntervalSeconds = 2.0;
                double sparkleTimeOffsetSeconds = ((tileCoordinateHash + sparkleIndex * 12345) & 0xFFFF) / 65536.0 * SpawnIntervalSeconds;
                double currentTimeSeconds = DateTime.Now.TimeOfDay.TotalSeconds;
                double effectiveTimeForThisSparkle = currentTimeSeconds + sparkleTimeOffsetSeconds;
                double timeIntoCurrentInterval = effectiveTimeForThisSparkle % SpawnIntervalSeconds;
                
                // Only spawn if we're within a 100ms window at the start of the interval (wider for testing)
                if (timeIntoCurrentInterval > 0.1)
                    continue;
                
                // Get surface normal for specular calculation
                Vector2 sparkleSurfaceNormal = sparkleIndex == 0
                    ? primarySurfaceNormal
                    : new Vector2(-primarySurfaceNormal.Y, primarySurfaceNormal.X);
                
                // Calculate specular intensity
                Vector2 playerToSparkleDirection = sparkleWorldPosition - playerEyePosition;
                float distanceToSparkle = playerToSparkleDirection.Length();
                if (distanceToSparkle < 1f) continue;
                
                Vector2 incidentRayDirection = playerToSparkleDirection / distanceToSparkle;
                float dotProduct = Vector2.Dot(sparkleSurfaceNormal, -incidentRayDirection);
                float specularIntensity = Math.Max(0f, dotProduct);
                specularIntensity = (float)Math.Pow(specularIntensity, 4f) * 0.3f;
                
                // Only spawn if intensity exceeds threshold (lowered for testing)
                if (specularIntensity > 0.05f)
                {
                    Color sparkleColor = GetColorForOre(dustType) * (specularIntensity * 3f + 0.5f);
                    float sparkleScale = 0.5f + specularIntensity * 0.5f; // Larger base scale
                    
                    activeSparkles.Add(new ActiveSparkle
                    {
                        WorldPosition = sparkleWorldPosition,
                        SpawnTimeSeconds = currentTimeSeconds,
                        SparkleColor = sparkleColor,
                        InitialScale = sparkleScale
                    });
                }
            }
        }
        
        /// <summary>
        /// Draw all active sparkles using SpriteBatch with manual fade control.
        /// Called from ModSystem or similar draw hook.
        /// </summary>
        public static void DrawSparkles(SpriteBatch spriteBatch)
        {
            if (activeSparkles.Count == 0) return;
            
            double currentTimeSeconds = DateTime.Now.TimeOfDay.TotalSeconds;
            
            // Use vanilla star texture (small white star shape)
            Texture2D sparkleTexture = TextureAssets.Star[0].Value;
            Vector2 textureOrigin = new Vector2(sparkleTexture.Width / 2f, sparkleTexture.Height / 2f);
            
            foreach (ActiveSparkle sparkle in activeSparkles)
            {
                double ageSeconds = currentTimeSeconds - sparkle.SpawnTimeSeconds;
                if (ageSeconds < 0) ageSeconds += 86400.0; // Midnight wraparound
                
                if (ageSeconds > SparkleFadeDurationSeconds) continue;
                
                // Calculate fade: 1.0 at spawn, 0.0 at end
                float fadeProgress = (float)(ageSeconds / SparkleFadeDurationSeconds);
                float fadeMultiplier = 1f - fadeProgress;
                
                // Apply fade to color and scale
                // Star texture is ~20px, scale 0.3-0.5 gives ~6-10px sparkles
                Color fadedColor = sparkle.SparkleColor * fadeMultiplier;
                float currentScale = sparkle.InitialScale * (0.5f + fadeMultiplier * 0.5f) * 0.3f;
                
                // Convert world position to screen position
                Vector2 screenPosition = sparkle.WorldPosition - Main.screenPosition;
                
                spriteBatch.Draw(
                    sparkleTexture,
                    screenPosition,
                    null,
                    fadedColor,
                    0f,
                    textureOrigin,
                    currentScale,
                    SpriteEffects.None,
                    0f
                );
            }
        }
        
        /// <summary>
        /// Convert dust type to a Color for manual rendering.
        /// </summary>
        private static Color GetColorForOre(int dustType)
        {
            switch (dustType)
            {
                case 9: return new Color(255, 140, 50);    // Copper - orange
                case 11: return new Color(200, 200, 200);  // Tin/Gray
                case 8: return new Color(180, 180, 180);   // Iron - gray
                case 14: return new Color(120, 140, 200);  // Lead - blue-gray
                case 63: return new Color(255, 255, 255);  // Silver - white
                case 15: return new Color(100, 180, 255);  // Cobalt/Sapphire - blue
                case 6: return new Color(255, 150, 60);    // Fire/Palladium - orange
                case 72: return new Color(255, 120, 220);  // Orichalcum/Amethyst - pink
                case 60: return new Color(255, 100, 100);  // Adamantite/Ruby - red
                case 75: return new Color(100, 255, 100);  // Chlorophyte/Emerald - green
                case 169: return new Color(255, 220, 80);  // Topaz/Amber - yellow
                case 27: return new Color(180, 100, 220);  // Meteorite - purple
                default: return new Color(255, 230, 120);  // Gold - default yellow
            }
        }


        /// <summary>
        /// Get appropriate dust type for a tile to match its visual appearance.
        /// Returns different dust colors for different ores and gems.
        /// </summary>
        private static int GetDustTypeForTile(int tileType)
        {
            // Ores - match their visual color using safe DustID values
            switch (tileType)
            {
                // Copper/Tin tier - orange/gray tones
                case TileID.Copper: return 9;  // Copper dust (orange)
                case TileID.Tin: return 11;    // Tin dust (gray)
                
                // Iron/Lead tier - gray/blue-gray tones
                case TileID.Iron: return 8;    // Iron dust (gray)
                case TileID.Lead: return 14;   // Lead dust (blue-gray)
                
                // Silver/Tungsten tier - white/green tones
                case TileID.Silver: return 63; // Silver dust (white)
                case TileID.Tungsten: return 11; // Similar gray
                
                // Gold/Platinum tier - yellow/platinum tones
                case TileID.Gold: return DustID.GoldFlame;
                case TileID.Platinum: return 63; // Bright white/silver
                
                // Hardmode ores - use colored dust
                case TileID.Cobalt: return 15;  // Blue
                case TileID.Palladium: return 6; // Orange/fire
                case TileID.Mythril: return 15; // Light blue
                case TileID.Orichalcum: return 72; // Pink
                case TileID.Adamantite: return 60; // Red
                case TileID.Titanium: return 11; // Gray/silver
                case TileID.Chlorophyte: return 75; // Green
                
                // Gems - use basic colored dust (gem tile IDs in Terraria)
                case TileID.Sapphire: return 15;  // Blue
                case TileID.Ruby: return 60;      // Red
                case TileID.Emerald: return 75;   // Green
                case TileID.Topaz: return 169;    // Yellow
                case TileID.Amethyst: return 72;  // Purple
                case TileID.Diamond: return 63;   // White/silver
                case TileID.AmberStoneBlock: return 169;  // Amber (orange/yellow)
                
                // Hellstone - fire
                case TileID.Hellstone: return 6;  // Fire dust
                
                // Meteorite - dark purple
                case TileID.Meteorite: return 27; // Purple/shadow
                
                // Default - gold sparkle for anything else (stone for testing, etc)
                default: return DustID.GoldFlame;
            }
        }

        /// <summary>
        /// Generate a deterministic hash from tile coordinates.
        /// Same coordinates always produce the same hash.
        /// </summary>
        private static int HashTileCoordinates(int tileX, int tileY)
        {
            // Use prime numbers for better distribution
            return tileX * 31337 + tileY * 7919;
        }

        /// <summary>
        /// Get a deterministic sparkle position within a tile (0-16 pixel range).
        /// Position is fixed for each tile+sparkleIndex combination.
        /// </summary>
        private static Vector2 GetDeterministicSparkleOffset(int tileHash, int sparkleIndex)
        {
            int seed = tileHash + sparkleIndex * 12345;
            float offsetX = ((seed & 0xF) + 0.5f);           // 0.5 to 15.5
            float offsetY = (((seed >> 4) & 0xF) + 0.5f);    // 0.5 to 15.5
            return new Vector2(offsetX, offsetY);
        }

        /// <summary>
        /// Get a deterministic primary surface normal for sparkle 0.
        /// The second sparkle will use a perpendicular vector.
        /// Normal is a unit vector pointing in a fixed direction for this tile.
        /// </summary>
        private static Vector2 GetDeterministicPrimarySurfaceNormal(int tileHash)
        {
            int seed = tileHash * 54321;
            // Generate angle in radians (0 to 2π)
            float normalAngle = (seed & 0x3FF) / 163f; // ~0 to 6.28
            return new Vector2(
                (float)Math.Cos(normalAngle),
                (float)Math.Sin(normalAngle)
            );
        }

        public override void PostUpdate()
        {
            // Tick down shield duration
            if (emergencyShieldDurationTicks > 0)
            {
                emergencyShieldDurationTicks--;
                if (emergencyShieldDurationTicks == 0)
                {
                    // Shield expired
                    emergencyShieldHP = 0;
                }
            }

            // Tick down cooldown
            if (emergencyShieldCooldownTicks > 0)
            {
                emergencyShieldCooldownTicks--;
            }
        }

        /// <summary>
        /// Called when player takes damage - activates shield if wearing shield-tier chestplate.
        /// </summary>
        public override void OnHurt(Player.HurtInfo info)
        {
            // Don't activate if on cooldown or already have shield
            if (emergencyShieldCooldownTicks > 0 || HasActiveShield)
                return;

            // Shield comes from CHESTPLATE, not full set
            switch (currentChestplateTier)
            {
                case ChestplateTier.TinCopper:
                    ActivateEmergencyShield(
                        hpPercent: 0.25f,      // 25% of max HP
                        durationSeconds: 5,
                        cooldownSeconds: 60,
                        purgeDebuffs: false
                    );
                    break;

                case ChestplateTier.GoldPlatinum:
                    ActivateEmergencyShield(
                        hpPercent: 0.25f,      // 25% of max HP
                        durationSeconds: 10,
                        cooldownSeconds: 120,
                        purgeDebuffs: true
                    );
                    break;
            }
        }

        /// <summary>
        /// Modify incoming damage - shield absorbs damage first.
        /// </summary>
        public override void ModifyHurt(ref Player.HurtModifiers modifiers)
        {
            if (!HasActiveShield)
                return;

            // Calculate how much damage the shield can absorb
            int incomingDamage = (int)modifiers.FinalDamage.Base;
            int absorbedDamage = Math.Min(emergencyShieldHP, incomingDamage);

            // Reduce shield HP
            emergencyShieldHP -= absorbedDamage;

            // Reduce incoming damage
            modifiers.FinalDamage.Base -= absorbedDamage;

            // Visual feedback
            if (absorbedDamage > 0)
            {
                CombatText.NewText(Player.Hitbox, Color.Cyan, $"Blocked {absorbedDamage}");
            }
        }

        /// <summary>
        /// Draw shield visual effects around the player.
        /// </summary>
        public override void DrawEffects(PlayerDrawSet drawInfo, ref float r, ref float g, ref float b, ref float a, ref bool fullBright)
        {
            if (!HasActiveShield)
                return;

            // Create dust particles around player for shield effect
            if (Main.rand.NextBool(3)) // ~20 particles per second
            {
                float angle = Main.rand.NextFloat() * MathHelper.TwoPi;
                float radius = 30f;
                Vector2 dustPosition = Player.Center + new Vector2(
                    (float)Math.Cos(angle) * radius,
                    (float)Math.Sin(angle) * radius
                );

                Dust shieldDust = Dust.NewDustPerfect(
                    dustPosition,
                    DustID.MagicMirror, // Cyan sparkle
                    Vector2.Zero,
                    Alpha: 100,
                    Scale: 0.8f
                );
                shieldDust.noGravity = true;
            }
        }

        /// <summary>
        /// Draw the shield circle and HP indicator (called after player is drawn).
        /// </summary>
        public static void DrawShieldVisuals(PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            ArmorSetBonusPlayer modPlayer = player.GetModPlayer<ArmorSetBonusPlayer>();

            if (!modPlayer.HasActiveShield)
                return;

            // Calculate shield fill ratio for visual
            float shieldRatio = (float)modPlayer.emergencyShieldHP / modPlayer.emergencyShieldMaxHP;

            // Draw shield circle (simple alpha circle for prototype)
            DrawShieldCircle(drawInfo, player, shieldRatio);

            // Draw shield HP text near player
            DrawShieldHPIndicator(drawInfo, player, modPlayer.emergencyShieldHP);
        }

        /// <summary>
        /// Draw a semi-transparent circle around the player.
        /// </summary>
        private static void DrawShieldCircle(PlayerDrawSet drawInfo, Player player, float fillRatio)
        {
            SpriteBatch spriteBatch = Main.spriteBatch;

            // Use vanilla white pixel texture for simple circle (prototype)
            Texture2D pixelTexture = TextureAssets.MagicPixel.Value;

            Vector2 playerScreenPos = player.Center - Main.screenPosition;
            float circleRadius = 40f;
            int segments = 32;

            // Color based on shield tier (cyan for regular, gold for gold/platinum)
            Color circleColor = Color.Cyan * 0.3f * fillRatio;

            // Draw circle outline using line segments
            for (int i = 0; i < segments; i++)
            {
                float angle1 = (float)i / segments * MathHelper.TwoPi;
                float angle2 = (float)(i + 1) / segments * MathHelper.TwoPi;

                Vector2 point1 = playerScreenPos + new Vector2(
                    (float)Math.Cos(angle1) * circleRadius,
                    (float)Math.Sin(angle1) * circleRadius
                );
                Vector2 point2 = playerScreenPos + new Vector2(
                    (float)Math.Cos(angle2) * circleRadius,
                    (float)Math.Sin(angle2) * circleRadius
                );

                // Draw line segment
                Vector2 direction = point2 - point1;
                float length = direction.Length();
                float rotation = (float)Math.Atan2(direction.Y, direction.X);

                spriteBatch.Draw(
                    pixelTexture,
                    point1,
                    null,
                    circleColor,
                    rotation,
                    Vector2.Zero,
                    new Vector2(length, 2f), // 2 pixel thick line
                    SpriteEffects.None,
                    0f
                );
            }
        }

        /// <summary>
        /// Draw shield HP text indicator near the player.
        /// </summary>
        private static void DrawShieldHPIndicator(PlayerDrawSet drawInfo, Player player, int shieldHP)
        {
            Vector2 playerScreenPos = player.Center - Main.screenPosition;
            Vector2 indicatorPos = playerScreenPos + new Vector2(30f, -30f); // Top-right of player

            // Draw shield HP as text
            string shieldText = $"🛡{shieldHP}";
            
            Utils.DrawBorderString(
                Main.spriteBatch,
                shieldText,
                indicatorPos,
                Color.Cyan,
                scale: 0.8f
            );
        }

        /// <summary>
        /// Activate the emergency shield with specified parameters.
        /// </summary>
        private void ActivateEmergencyShield(float hpPercent, int durationSeconds, int cooldownSeconds, bool purgeDebuffs)
        {
            // Calculate shield HP
            emergencyShieldMaxHP = (int)(Player.statLifeMax2 * hpPercent);
            emergencyShieldHP = emergencyShieldMaxHP;

            // Set duration and cooldown (in ticks, 60 = 1 second)
            emergencyShieldDurationTicks = durationSeconds * 60;
            emergencyShieldCooldownTicks = cooldownSeconds * 60;

            // Purge debuffs if gold/platinum tier
            if (purgeDebuffs)
            {
                PurgeCommonDebuffs();
            }

            // Visual feedback
            CombatText.NewText(Player.Hitbox, Color.Gold, $"+{emergencyShieldHP} Shield!");

            // Burst of particles
            for (int i = 0; i < 30; i++)
            {
                float angle = Main.rand.NextFloat() * MathHelper.TwoPi;
                Vector2 velocity = new Vector2(
                    (float)Math.Cos(angle) * 3f,
                    (float)Math.Sin(angle) * 3f
                );

                Dust.NewDust(
                    Player.Center,
                    0, 0,
                    DustID.MagicMirror,
                    velocity.X, velocity.Y,
                    Alpha: 100,
                    Scale: 1.2f
                );
            }
        }

        /// <summary>
        /// Clear common negative debuffs (gold/platinum set bonus).
        /// </summary>
        private void PurgeCommonDebuffs()
        {
            Player.ClearBuff(BuffID.OnFire);
            Player.ClearBuff(BuffID.OnFire3); // Hellfire
            Player.ClearBuff(BuffID.Poisoned);
            Player.ClearBuff(BuffID.Venom);
            Player.ClearBuff(BuffID.Chilled);
            Player.ClearBuff(BuffID.Frozen);
            Player.ClearBuff(BuffID.Burning);
            Player.ClearBuff(BuffID.Bleeding);
            Player.ClearBuff(BuffID.Confused);
            Player.ClearBuff(BuffID.Slow);
        }

        /// <summary>
        /// Detect which tier of ore chestplate the player is wearing.
        /// Only checks the chestplate slot - helmet and legs can be mixed.
        /// </summary>
        private ChestplateTier DetectChestplateTier()
        {
            int chestplateType = Player.armor[1].type;

            // Tin/Copper tier
            if (chestplateType == ItemID.TinChainmail || chestplateType == ItemID.CopperChainmail)
                return ChestplateTier.TinCopper;

            // Iron/Lead tier
            if (chestplateType == ItemID.IronChainmail || chestplateType == ItemID.LeadChainmail)
                return ChestplateTier.IronLead;

            // Silver/Tungsten tier
            if (chestplateType == ItemID.SilverChainmail || chestplateType == ItemID.TungstenChainmail)
                return ChestplateTier.SilverTungsten;

            // Gold/Platinum tier
            if (chestplateType == ItemID.GoldChainmail || chestplateType == ItemID.PlatinumChainmail)
                return ChestplateTier.GoldPlatinum;

            return ChestplateTier.None;
        }

        /// <summary>
        /// Detect if player is wearing a FULL ore armor set (all 3 pieces from same tier).
        /// This grants the mini-spelunker bonus.
        /// </summary>
        private bool DetectFullOreArmorSet()
        {
            int helmetType = Player.armor[0].type;
            int chestplateType = Player.armor[1].type;
            int greavesType = Player.armor[2].type;

            // Tin set
            if (helmetType == ItemID.TinHelmet &&
                chestplateType == ItemID.TinChainmail &&
                greavesType == ItemID.TinGreaves)
                return true;

            // Copper set
            if (helmetType == ItemID.CopperHelmet &&
                chestplateType == ItemID.CopperChainmail &&
                greavesType == ItemID.CopperGreaves)
                return true;

            // Iron set
            if (helmetType == ItemID.IronHelmet &&
                chestplateType == ItemID.IronChainmail &&
                greavesType == ItemID.IronGreaves)
                return true;

            // Lead set
            if (helmetType == ItemID.LeadHelmet &&
                chestplateType == ItemID.LeadChainmail &&
                greavesType == ItemID.LeadGreaves)
                return true;

            // Silver set
            if (helmetType == ItemID.SilverHelmet &&
                chestplateType == ItemID.SilverChainmail &&
                greavesType == ItemID.SilverGreaves)
                return true;

            // Tungsten set
            if (helmetType == ItemID.TungstenHelmet &&
                chestplateType == ItemID.TungstenChainmail &&
                greavesType == ItemID.TungstenGreaves)
                return true;

            // Gold set
            if (helmetType == ItemID.GoldHelmet &&
                chestplateType == ItemID.GoldChainmail &&
                greavesType == ItemID.GoldGreaves)
                return true;

            // Platinum set
            if (helmetType == ItemID.PlatinumHelmet &&
                chestplateType == ItemID.PlatinumChainmail &&
                greavesType == ItemID.PlatinumGreaves)
                return true;

            return false;
        }
    }
}