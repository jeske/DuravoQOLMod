using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaSurvivalMod.TetheredMinions
{
    /// <summary>
    /// GlobalProjectile that enforces minion tethering to player.
    /// When a minion loses line-of-sight to its owner for too long,
    /// or loses a viable travel path, it triggers the minion's return behavior.
    ///
    /// Approach priority:
    /// 1. Trigger native fly/phase behavior (disable collision, give velocity toward owner)
    /// 2. Fallback: teleport if stuck for too long
    /// </summary>
    public class TetheredMinionProjectile : GlobalProjectile
    {
        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                        TUNABLE CONSTANTS                           ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>DEBUG: Set to true for verbose tether logging</summary>
        private const bool DebugTethering = true;

        /// <summary>How long LOS can be blocked before minion phases to player (seconds)</summary>
        private const double LOSBlockedTimeoutSeconds = 5.0;

        /// <summary>How long "phasing" state lasts before we force teleport (seconds)</summary>
        private const double PhasingTimeoutSeconds = 3.0;

        /// <summary>How often to check tether conditions (ticks, 30 = 0.5 sec)</summary>
        private const int TetherCheckIntervalTicks = 30;

        /// <summary>Maximum speed at which minion flies toward player when phasing</summary>
        private const float PhasingMaxFlySpeed = 15f;

        /// <summary>Minimum speed when close to player to avoid drifting</summary>
        private const float PhasingMinFlySpeed = 2f;

        /// <summary>Distance at which minion starts slowing down (pixels)</summary>
        private const float PhasingSlowdownDistance = 150f;

        /// <summary>Distance at which minion is considered "arrived" (pixels)</summary>
        private const float PhasingArrivalDistance = 40f;

        /// <summary>Random offset range when teleporting back to player (pixels)</summary>
        private const float TeleportOffsetRange = 40f;

        /// <summary>Vertical offset when teleporting (pixels, negative = above player)</summary>
        private const float TeleportVerticalOffset = -20f;

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          INSTANCE STATE                            ║
        // ╚════════════════════════════════════════════════════════════════════╝

        // Per-projectile instance required for tracking state
        public override bool InstancePerEntity => true;

        /// <summary>Epoch time when LOS was last confirmed clear</summary>
        private double lastClearLOSTimeEpoch;

        /// <summary>Whether minion is currently in "phasing" state (flying through walls to owner)</summary>
        private bool isPhasingToOwner;

        /// <summary>Epoch time when phasing started (for timeout)</summary>
        private double phasingStartedEpoch;

        /// <summary>Original tileCollide value before phasing</summary>
        private bool originalTileCollide;

        /// <summary>Counter for periodic checks</summary>
        private int tetherCheckTimer;

        /// <summary>Flag to track if we're initialized</summary>
        private bool isInitialized;

        /// <summary>Get current time as epoch seconds</summary>
        private static double GetEpochTimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          MAIN LOGIC                                ║
        // ╚════════════════════════════════════════════════════════════════════╝

        public override bool PreAI(Projectile projectile)
        {
            // Only process minions
            if (!projectile.minion)
                return true;

            // Initialize on first tick
            if (!isInitialized) {
                lastClearLOSTimeEpoch = GetEpochTimeSeconds();
                isInitialized = true;
            }

            // Get owner
            if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers)
                return true;

            Player ownerPlayer = Main.player[projectile.owner];
            if (!ownerPlayer.active || ownerPlayer.dead)
                return true;

            // If phasing, disable tile collision before AI runs
            if (isPhasingToOwner) {
                projectile.tileCollide = false;
            }

            // Periodic check when not phasing
            if (tetherCheckTimer++ < TetherCheckIntervalTicks)
                return true;
            tetherCheckTimer = 0;

            // Check tether conditions
            CheckTetherConditions(projectile, ownerPlayer);

            return true;
        }

        /// <summary>
        /// PostAI runs AFTER the projectile's normal AI.
        /// This is where we override velocity during phasing so the minion flies toward player.
        /// </summary>
        public override void PostAI(Projectile projectile)
        {
            // Only process minions that are phasing
            if (!projectile.minion || !isPhasingToOwner)
                return;

            // Get owner
            if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers)
                return;

            Player ownerPlayer = Main.player[projectile.owner];
            if (!ownerPlayer.active || ownerPlayer.dead)
                return;

            // Apply phasing movement - override velocity AFTER normal AI ran
            ApplyPhasingMovement(projectile, ownerPlayer);
        }

        /// <summary>
        /// Apply phasing movement - fly through walls toward owner.
        /// Called from PostAI so it happens AFTER normal AI (prevents AI from overriding our velocity).
        /// </summary>
        private void ApplyPhasingMovement(Projectile minion, Player ownerPlayer)
        {
            double currentTimeEpoch = GetEpochTimeSeconds();
            Vector2 directionToOwner = ownerPlayer.Center - minion.Center;
            float distanceToOwner = directionToOwner.Length();

            // Check if we've reached the owner (back in LOS and close enough)
            bool hasLOSNow = HasLineOfSight(minion.Center, ownerPlayer.Center);
            if (hasLOSNow && distanceToOwner < PhasingArrivalDistance) {
                // Arrived - end phasing
                EndPhasingState(minion);

                if (DebugTethering) {
                    Main.NewText($"[TETHER] {minion.Name} reached owner - phasing complete", Color.Green);
                }
                return;
            }

            // Check for phasing timeout (stuck in geometry?)
            double phasingDuration = currentTimeEpoch - phasingStartedEpoch;
            if (phasingDuration >= PhasingTimeoutSeconds) {
                // Force teleport
                if (DebugTethering) {
                    Main.NewText($"[TETHER] {minion.Name} phasing timeout - force teleporting", Color.Red);
                }
                EndPhasingState(minion);
                TeleportMinionToPlayer(minion, ownerPlayer, "phasing timeout");
                return;
            }

            // Continue phasing - fly toward owner through walls
            minion.tileCollide = false;

            if (distanceToOwner > 0) {
                directionToOwner.Normalize();

                // Scale velocity based on distance - slow down as we approach
                float speedFactor;
                if (distanceToOwner >= PhasingSlowdownDistance) {
                    speedFactor = 1f; // Full speed when far
                }
                else {
                    // Linear interpolation from min to max speed based on distance
                    speedFactor = distanceToOwner / PhasingSlowdownDistance;
                }

                float currentSpeed = MathHelper.Lerp(PhasingMinFlySpeed, PhasingMaxFlySpeed, speedFactor);
                minion.velocity = directionToOwner * currentSpeed;
            }

            // Spawn trail particles while phasing
            if (Main.rand.NextBool(3)) {
                Dust phaseDust = Dust.NewDustPerfect(
                    minion.Center,
                    DustID.MagicMirror,
                    -minion.velocity * 0.2f,
                    Alpha: 150,
                    Scale: 0.6f
                );
                phaseDust.noGravity = true;
            }
        }

        /// <summary>
        /// Start phasing state - minion will fly through walls to owner.
        /// </summary>
        private void StartPhasingState(Projectile minion, string reason)
        {
            if (isPhasingToOwner)
                return; // Already phasing

            isPhasingToOwner = true;
            phasingStartedEpoch = GetEpochTimeSeconds();
            originalTileCollide = minion.tileCollide;
            minion.tileCollide = false;

            // Spawn phase-out effect
            SpawnPhaseEffect(minion.Center, fadeOut: true);

            if (DebugTethering) {
                Main.NewText($"[TETHER] {minion.Name} starting phase to owner: {reason}", Color.Orange);
            }
        }

        /// <summary>
        /// End phasing state - restore normal minion behavior.
        /// </summary>
        private void EndPhasingState(Projectile minion)
        {
            isPhasingToOwner = false;
            minion.tileCollide = originalTileCollide;
            lastClearLOSTimeEpoch = GetEpochTimeSeconds();

            // Spawn phase-in effect
            SpawnPhaseEffect(minion.Center, fadeOut: false);
        }

        /// <summary>
        /// Check if minion should phase back to player.
        /// Two triggers:
        /// (a) No A* path exists from minion to player = IMMEDIATE phasing
        /// (b) LOS blocked for 5 seconds = phasing
        /// </summary>
        private void CheckTetherConditions(Projectile minion, Player ownerPlayer)
        {
            double currentTimeEpoch = GetEpochTimeSeconds();
            Vector2 minionPosition = minion.Center;
            Vector2 playerPosition = ownerPlayer.Center;

            // Check if A* path exists to owner
            bool pathExistsToOwner = TilePathfinder.PathExists(minionPosition, playerPosition);

            if (!pathExistsToOwner) {
                // No path = IMMEDIATE phasing
                StartPhasingState(minion, "no path to player (A*)");
                return;
            }

            // Path exists - check LOS for the 5-second timeout rule
            bool hasLOSToOwner = HasLineOfSight(minionPosition, playerPosition);

            if (hasLOSToOwner) {
                // LOS clear - reset timer
                lastClearLOSTimeEpoch = currentTimeEpoch;
                return;
            }

            // LOS blocked but path exists - check timeout
            double losBlockedDuration = currentTimeEpoch - lastClearLOSTimeEpoch;

            if (losBlockedDuration >= LOSBlockedTimeoutSeconds) {
                // Start phasing behavior after timeout
                StartPhasingState(minion, $"LOS blocked for {losBlockedDuration:F1}s");
            }
        }

        /// <summary>
        /// Force teleport minion to player (fallback when phasing fails).
        /// </summary>
        private void TeleportMinionToPlayer(Projectile minion, Player targetPlayer, string reason)
        {
            // Calculate new position near player
            float randomOffsetX = Main.rand.NextFloat(-TeleportOffsetRange, TeleportOffsetRange);
            Vector2 newPosition = targetPlayer.Center + new Vector2(randomOffsetX, TeleportVerticalOffset);

            // Spawn dust effect at old position
            SpawnPhaseEffect(minion.Center, fadeOut: true);

            // Move minion
            minion.Center = newPosition;
            minion.velocity = Vector2.Zero;

            // Spawn dust effect at new position
            SpawnPhaseEffect(newPosition, fadeOut: false);

            // Reset LOS timer
            lastClearLOSTimeEpoch = GetEpochTimeSeconds();

            if (DebugTethering) {
                Main.NewText($"[TETHER] Teleported {minion.Name}: {reason}", Color.Cyan);
            }
        }

        /// <summary>
        /// Spawn visual phase effect (dust particles).
        /// </summary>
        private static void SpawnPhaseEffect(Vector2 effectPosition, bool fadeOut)
        {
            int dustCount = fadeOut ? 15 : 20;
            int dustType = DustID.MagicMirror;

            for (int i = 0; i < dustCount; i++) {
                float angle = Main.rand.NextFloat() * MathHelper.TwoPi;
                float speed = Main.rand.NextFloat(1f, 3f);
                Vector2 velocity = new Vector2(
                    (float)Math.Cos(angle) * speed,
                    (float)Math.Sin(angle) * speed
                );

                Dust phaseDust = Dust.NewDustPerfect(
                    effectPosition + velocity * 3f,
                    dustType,
                    velocity * (fadeOut ? 2f : -0.5f),
                    Alpha: 100,
                    Scale: 0.8f
                );
                phaseDust.noGravity = true;
            }
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                      LINE OF SIGHT CHECKS                          ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Check if there's a clear line of sight between two points.
        /// Uses Terraria's built-in collision check.
        /// </summary>
        private static bool HasLineOfSight(Vector2 fromPosition, Vector2 toPosition)
        {
            return Collision.CanHitLine(fromPosition, 1, 1, toPosition, 1, 1);
        }
    }
}