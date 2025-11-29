// MIT Licensed - Copyright (c) 2025 David W. Jeske
// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║  CLIENT-SIDE SPAWN IMMUNITY                                                     ║
// ║                                                                                 ║
// ║  Provides temporary invulnerability when spawning/entering world.               ║
// ║  Can be triggered by:                                                           ║
// ║  - PersistentPositionPlayer (client-side position restore)                      ║
// ║  - World-side position restore (via packet from server)                         ║
// ║  - Any other system that needs spawn protection                                 ║
// ╚════════════════════════════════════════════════════════════════════════════════╝
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace DuravoQOLMod.PersistentPosition
{
    /// <summary>
    /// Handles temporary spawn immunity for the local player.
    /// Blocks all damage during immunity period with visual effects.
    /// </summary>
    public class TemporarySpawnImmunityPlayer : ModPlayer
    {
        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                        TUNABLE CONSTANTS                           ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>Default duration of spawn immunity in seconds</summary>
        public const double DefaultImmunityDurationSeconds = 3.0;

        /// <summary>Get debug setting from mod config</summary>
        private static bool DebugMessagesEnabled => ModContent.GetInstance<DuravoQOLModConfig>()?.Debug?.DebugPlayerPersistence ?? false;

        /// <summary>Block Environmental/Unknown damage during immunity (suffocation, etc)</summary>
        private const bool BlockEnvironmentalDamageDuringImmunity = true;

        /// <summary>Block NPC damage during immunity</summary>
        private const bool BlockNPCDamageDuringImmunity = true;

        /// <summary>Block Projectile damage during immunity</summary>
        private const bool BlockProjectileDamageDuringImmunity = true;

        /// <summary>Block Player (PvP) damage during immunity</summary>
        private const bool BlockPlayerDamageDuringImmunity = true;

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          INSTANCE STATE                            ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>Epoch time (seconds since 1970) when immunity expires (0 = not immune)</summary>
        private double immunityExpiresAtEpochSeconds;

        /// <summary>Track last logged second to avoid debug spam</summary>
        private int lastLoggedImmunitySecond = -1;

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                         TIME HELPERS                               ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>Get current time as epoch seconds (wall clock time, never wraps)</summary>
        private static double GetEpochTimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }

        /// <summary>Check if immunity is currently active</summary>
        private bool IsImmunityActive => GetEpochTimeSeconds() < immunityExpiresAtEpochSeconds;

        /// <summary>Get remaining immunity time in seconds (0 if expired)</summary>
        private double GetImmunityRemainingSeconds()
        {
            double remaining = immunityExpiresAtEpochSeconds - GetEpochTimeSeconds();
            return remaining > 0 ? remaining : 0;
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                    PUBLIC API - GRANT IMMUNITY                     ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Grant spawn immunity to this player for specified duration.
        /// </summary>
        /// <param name="durationSeconds">Duration of immunity in seconds</param>
        public void GrantImmunity(double durationSeconds)
        {
            immunityExpiresAtEpochSeconds = GetEpochTimeSeconds() + durationSeconds;
        }

        /// <summary>
        /// Grant spawn immunity with default duration.
        /// </summary>
        public void GrantImmunity()
        {
            GrantImmunity(DefaultImmunityDurationSeconds);
        }

        /// <summary>
        /// Grant spawn immunity to the local player. Can be called from anywhere on the client.
        /// </summary>
        /// <param name="durationSeconds">Duration of immunity in seconds</param>
        public static void GrantImmunityToLocalPlayer(double durationSeconds)
        {
            if (Main.netMode == NetmodeID.Server)
                return; // Only works on client

            Player localPlayer = Main.LocalPlayer;
            if (localPlayer == null)
                return;

            var immunityPlayer = localPlayer.GetModPlayer<TemporarySpawnImmunityPlayer>();
            immunityPlayer?.GrantImmunity(durationSeconds);
        }

        /// <summary>
        /// Grant spawn immunity to local player with default duration.
        /// </summary>
        public static void GrantImmunityToLocalPlayer()
        {
            GrantImmunityToLocalPlayer(DefaultImmunityDurationSeconds);
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                    VISUAL EFFECTS (PreUpdate)                      ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Called every frame - handle spawn light effect and particles during immunity.
        /// </summary>
        public override void PreUpdate()
        {
            if (!IsImmunityActive) {
                // Immunity just ended - log it once (only if debug enabled)
                if (lastLoggedImmunitySecond != -1) {
                    if (DebugMessagesEnabled) {
                        Main.NewText($"[DuravoQOL] Immunity ENDED - you can now take damage!", 255, 100, 100);
                    }
                    lastLoggedImmunitySecond = -1;
                }
                return;
            }

            double remainingSeconds = GetImmunityRemainingSeconds();

            // Calculate fade based on remaining time vs total duration (brighter at start, fades out)
            float lightIntensity = (float)(remainingSeconds / DefaultImmunityDurationSeconds);
            lightIntensity = Math.Min(lightIntensity, 1.0f); // Clamp in case granted longer duration

            // Add bright protective light around player (cyan/white glow)
            Lighting.AddLight(Player.Center, 0.8f * lightIntensity, 1.0f * lightIntensity, 1.2f * lightIntensity);

            // Spawn occasional sparkle particles (stop when nearly expired)
            if (Main.rand.NextBool(3) && remainingSeconds > 1.0) {
                float angle = Main.rand.NextFloat() * MathHelper.TwoPi;
                float radius = Main.rand.NextFloat(10f, 30f);
                Vector2 dustPos = Player.Center + new Vector2(
                    (float)Math.Cos(angle) * radius,
                    (float)Math.Sin(angle) * radius
                );

                Dust spawnDust = Dust.NewDustPerfect(
                    dustPos,
                    DustID.MagicMirror,
                    Vector2.Zero,
                    Alpha: 100,
                    Scale: 0.6f * lightIntensity
                );
                spawnDust.noGravity = true;
                spawnDust.fadeIn = 0.5f;
            }

            // Log approximately every 2 seconds so we can see it's working (only if debug enabled)
            int currentSecond = (int)remainingSeconds;
            if (DebugMessagesEnabled && currentSecond != lastLoggedImmunitySecond && currentSecond % 2 == 0 && currentSecond > 0) {
                lastLoggedImmunitySecond = currentSecond;
                Main.NewText($"[DuravoQOL] Immunity: {currentSecond}s remaining", 100, 255, 100);
            }
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                    DAMAGE BLOCKING (ModifyHurt)                    ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Damage source categories for selective blocking during immunity.
        /// </summary>
        private enum DamageSourceCategory
        {
            Environmental,  // Suffocation, drowning, lava tile contact, etc.
            NPC,            // Enemy contact or attacks
            Projectile,     // Hostile projectiles
            Player          // PvP damage
        }

        /// <summary>
        /// Block damage during immunity period with per-category control.
        /// </summary>
        public override void ModifyHurt(ref Player.HurtModifiers modifiers)
        {
            if (!IsImmunityActive)
                return;

            DamageSourceCategory damageCategory = CategorizeDamageSource(modifiers);

            // Get actual incoming damage values BEFORE any modification
            int sourceDamageBase = (int)modifiers.SourceDamage.Base;
            int finalDamageBase = (int)modifiers.FinalDamage.Base;

            bool shouldBlock = ShouldBlockDamageCategory(damageCategory);

            if (shouldBlock) {
                // Cancel removes the damage event entirely (no damage sound, no knockback, no visual feedback)
                modifiers.Cancel();

                if (DebugMessagesEnabled) {
                    string sourceDetails = GetDamageSourceDetails(modifiers, damageCategory);
                    int secondsRemaining = (int)GetImmunityRemainingSeconds();
                    Main.NewText($"[DuravoQOL] BLOCKING {damageCategory}: src={sourceDamageBase} final={finalDamageBase} | {sourceDetails} ({secondsRemaining}s left)", 100, 255, 100);
                }
                return;
            }

            if (DebugMessagesEnabled) {
                string sourceDetails = GetDamageSourceDetails(modifiers, damageCategory);
                Main.NewText($"[DuravoQOL] ALLOWING {damageCategory}: src={sourceDamageBase} final={finalDamageBase} | {sourceDetails}", 255, 100, 100);
            }
        }

        /// <summary>
        /// Determine the category of damage source.
        /// </summary>
        private static DamageSourceCategory CategorizeDamageSource(Player.HurtModifiers modifiers)
        {
            if (modifiers.DamageSource.SourceNPCIndex >= 0 && modifiers.DamageSource.SourceNPCIndex < Main.maxNPCs) {
                return DamageSourceCategory.NPC;
            }
            else if (modifiers.DamageSource.SourceProjectileLocalIndex >= 0) {
                return DamageSourceCategory.Projectile;
            }
            else if (modifiers.DamageSource.SourcePlayerIndex >= 0) {
                return DamageSourceCategory.Player;
            }
            else {
                return DamageSourceCategory.Environmental;
            }
        }

        /// <summary>
        /// Check if we should block this damage category based on current flags.
        /// </summary>
        private static bool ShouldBlockDamageCategory(DamageSourceCategory category)
        {
            switch (category) {
                case DamageSourceCategory.Environmental:
                    return BlockEnvironmentalDamageDuringImmunity;
                case DamageSourceCategory.NPC:
                    return BlockNPCDamageDuringImmunity;
                case DamageSourceCategory.Projectile:
                    return BlockProjectileDamageDuringImmunity;
                case DamageSourceCategory.Player:
                    return BlockPlayerDamageDuringImmunity;
                default:
                    return true; // Block unknown by default
            }
        }

        /// <summary>
        /// Get detailed info about the damage source for logging.
        /// </summary>
        private static string GetDamageSourceDetails(Player.HurtModifiers modifiers, DamageSourceCategory category)
        {
            switch (category) {
                case DamageSourceCategory.NPC:
                    int npcIndex = modifiers.DamageSource.SourceNPCIndex;
                    if (npcIndex >= 0 && npcIndex < Main.maxNPCs) {
                        NPC sourceNPC = Main.npc[npcIndex];
                        if (sourceNPC.active)
                            return $"{sourceNPC.FullName} (Type:{sourceNPC.type})";
                        return $"Inactive NPC #{npcIndex}";
                    }
                    return "Invalid NPC index";

                case DamageSourceCategory.Projectile:
                    int projIndex = modifiers.DamageSource.SourceProjectileLocalIndex;
                    if (projIndex >= 0 && projIndex < Main.maxProjectiles && Main.projectile[projIndex].active) {
                        Projectile sourceProj = Main.projectile[projIndex];
                        return $"{sourceProj.Name} (Type:{sourceProj.type})";
                    }
                    return $"Unknown projectile #{projIndex}";

                case DamageSourceCategory.Player:
                    return $"Player #{modifiers.DamageSource.SourcePlayerIndex}";

                case DamageSourceCategory.Environmental:
                    return $"Knockback:{modifiers.Knockback.Base:F1}";

                default:
                    return "Unknown";
            }
        }
    }
}