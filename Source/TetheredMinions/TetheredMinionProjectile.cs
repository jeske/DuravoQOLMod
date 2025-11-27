using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaSurvivalMod.TetheredMinions
{
    /// <summary>
    /// GlobalProjectile that handles minion anti-cheese and QoL pathing.
    /// 
    /// Two main features:
    /// 1. CHEESE PREVENTION: When player places a block, if minion is nearby with no LOS
    ///    and no valid A* path, immediately phase the minion back to player.
    /// 2. QOL PATHING: When minion's velocity is pointing toward player but blocked,
    ///    compute A* path and follow it (or phase if no path exists).
    /// </summary>
    public class TetheredMinionProjectile : GlobalProjectile
    {
        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                        TUNABLE CONSTANTS                           ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>DEBUG: Set to true for verbose tether logging</summary>
        private const bool DebugTethering = false;

        /// <summary>Enable QoL "stuck detection" pathing (disabled for now - velocity detection doesn't work for bouncing minions)</summary>
        private const bool EnableQoLFollowPathing = false;

        /// <summary>Max distance in tiles for cheese check / QoL pathing to apply</summary>
        private const float MaxDistanceForCheckTiles = 35f;

        /// <summary>Max A* path length in tiles (prevents expensive long searches)</summary>
        private const int MaxAStarPathLengthTiles = 35;

        /// <summary>Cooldown in ticks before rechecking A* after cheese prevention triggers</summary>
        private const int CheeseCheckCooldownTicks = 120; // 2 seconds

        /// <summary>Cooldown in ticks before rechecking QoL pathing</summary>
        private const int QoLPathingCheckCooldownTicks = 30; // 0.5 seconds

        /// <summary>Maximum speed at which minion flies toward player when phasing</summary>
        private const float PhasingMaxFlySpeed = 15f;

        /// <summary>Distance at which minion starts slowing down (pixels)</summary>
        private const float PhasingSlowdownDistance = 150f;

        /// <summary>Distance at which minion is considered "arrived" (pixels)</summary>
        private const float PhasingArrivalDistance = 40f;

        /// <summary>Distance at which minion is considered to have reached a path waypoint (pixels)</summary>
        private const float WaypointArrivalDistance = 24f;

        /// <summary>Speed when following A* path (pixels per tick)</summary>
        private const float PathFollowSpeed = 8f;

        /// <summary>Minimum velocity magnitude to consider minion "moving"</summary>
        private const float MinVelocityMagnitude = 1f;

        /// <summary>
        /// Distance threshold (pixels) for stuck detection.
        /// If |currentPos - avgPos| < this value while sampling, minion is "stuck".
        /// </summary>
        private const float StuckDistanceThresholdPixels = 16f;

        /// <summary>
        /// Progress threshold (pixels) along dominant axis.
        /// If minion gets this much closer to player on the dominant axis, reset stuck detection.
        /// </summary>
        private const float ProgressThresholdPixels = 8f;

        /// <summary>
        /// Minimum samples before we consider the minion potentially stuck.
        /// Prevents false positives on short oscillations.
        /// </summary>
        private const int MinSamplesForStuckDetection = 30; // ~0.5 seconds

        /// <summary>
        /// Minimum consecutive frames where stuck condition is true before triggering action.
        /// Prevents triggering on momentary oscillations that happen to match the threshold.
        /// </summary>
        private const int MinFramesToConsiderStuck = 10;

        /// <summary>How long "phasing" state lasts before we force teleport (seconds)</summary>
        private const double PhasingTimeoutSeconds = 3.0;

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          STATIC STATE                              ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// The game tick on which cheese check should execute.
        /// Set to currentTick + 1 when block is placed, checked in PreAI.
        /// </summary>
        public static int DoCheeseCheckOnTick { get; set; } = int.MaxValue;

        /// <summary>
        /// The player who placed the block (for ownership check).
        /// </summary>
        public static int BlockPlacerPlayerIndex { get; set; } = -1;

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          INSTANCE STATE                            ║
        // ╚════════════════════════════════════════════════════════════════════╝

        // Per-projectile instance required for tracking state
        public override bool InstancePerEntity => true;

        /// <summary>Game tick when cheese check was last performed</summary>
        private int lastCheeseCheckTick;

        /// <summary>
        /// Running average of minion position during "velocity toward player" frames.
        /// Used for stuck detection.
        /// </summary>
        private Vector2 avgPositionDuringSampling;

        /// <summary>
        /// Number of consecutive ticks where velocity was toward player.
        /// Used for stuck detection averaging.
        /// </summary>
        private int numTicksVelocityTowardsPlayer;

        /// <summary>
        /// Number of consecutive frames the stuck condition (distance < threshold) has been true.
        /// Must reach MinFramesToConsiderStuck before triggering action.
        /// </summary>
        private int consecutiveFramesBelowStuckThreshold;

        /// <summary>
        /// True if horizontal is the dominant direction to player when sampling started.
        /// Used for single-axis progress tracking.
        /// </summary>
        private bool dominantAxisIsHorizontal;

        /// <summary>
        /// The distance to player on the dominant axis when sampling started.
        /// Progress = getting closer than this value.
        /// </summary>
        private float startingDistanceOnDominantAxis;

        /// <summary>Whether minion is currently in "phasing" state (flying through walls to owner)</summary>
        private bool isPhasingToOwner;

        /// <summary>Epoch time when phasing started (for timeout)</summary>
        private double phasingStartedEpoch;

        /// <summary>Original tileCollide value before phasing</summary>
        private bool originalTileCollide;

        /// <summary>Whether minion is currently following A* path waypoints</summary>
        private bool isFollowingPath;

        /// <summary>The A* path waypoints from minion to owner (world pixel coords)</summary>
        private List<Vector2>? currentPathWaypoints;

        /// <summary>Current waypoint index we're heading toward</summary>
        private int currentWaypointIndex;

        /// <summary>Get current time as epoch seconds</summary>
        private static double GetEpochTimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          MAIN LOGIC                                ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// PostAI runs AFTER the projectile's normal AI.
        /// Velocity here is the AI's INTENDED velocity (before collision modifies it).
        /// This is where we check for stuck detection and apply phasing/path-following.
        /// </summary>
        public override void PostAI(Projectile projectile)
        {
            // Only process minions
            if (!projectile.minion)
                return;

            // Get owner
            if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers)
                return;

            Player ownerPlayer = Main.player[projectile.owner];
            if (!ownerPlayer.active || ownerPlayer.dead)
                return;

            // Get distance to owner in tiles
            float distanceToOwnerPixels = Vector2.Distance(projectile.Center, ownerPlayer.Center);
            float distanceToOwnerTiles = distanceToOwnerPixels / 16f;

            // Only process if minion is within our check range
            if (distanceToOwnerTiles > MaxDistanceForCheckTiles)
                return;

            int currentTick = (int)Main.GameUpdateCount;

            // ═══════════════════════════════════════════════════════════
            // CHEESE PREVENTION: Check on block placement (delayed by 1 tick)
            // PlaceInWorld fires BEFORE the tile exists, so we wait until DoCheeseCheckOnTick
            // New block placement ALWAYS triggers check (no cooldown - player action is deliberate)
            // ═══════════════════════════════════════════════════════════
            if (currentTick >= DoCheeseCheckOnTick && BlockPlacerPlayerIndex == projectile.owner) {
                // Reset immediately so we don't re-check every frame
                DoCheeseCheckOnTick = int.MaxValue;

                if (DebugTethering) {
                    int clearanceW = TilePathfinder.CalculateTileClearance(projectile.width);
                    int clearanceH = TilePathfinder.CalculateTileClearance(projectile.height);
                    Main.NewText($"[TETHER] Block placed, checking {projectile.Name} (hitbox {projectile.width}x{projectile.height}px → {clearanceW}x{clearanceH} tiles)", Color.Yellow);
                }
                CheckCheesePrevention(projectile, ownerPlayer);
            }

            // ═══════════════════════════════════════════════════════════
            // QOL PATHING: Stuck detection using position averaging
            // In PostAI, velocity is the AI's INTENDED velocity (pre-collision).
            // This lets us detect when minion wants to move toward player but can't.
            // DISABLED: Velocity detection doesn't work for bouncing minions (e.g., Baby Slime)
            // ═══════════════════════════════════════════════════════════
            if (EnableQoLFollowPathing && !isPhasingToOwner && !isFollowingPath) {
                CheckStuckDetection(projectile, ownerPlayer);
            }

            // ═══════════════════════════════════════════════════════════
            // APPLY MOVEMENT: Override velocity for phasing/path-following
            // ═══════════════════════════════════════════════════════════
            if (isFollowingPath) {
                ApplyPathFollowingMovement(projectile, ownerPlayer);
            }
            else if (isPhasingToOwner) {
                ApplyPhasingMovement(projectile, ownerPlayer);
            }
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                      CHEESE PREVENTION                             ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Cheese prevention check: Player placed a block, minion is nearby.
        /// If no LOS and no A* path, immediately phase minion to player.
        /// </summary>
        private void CheckCheesePrevention(Projectile minion, Player ownerPlayer)
        {
            // Check LOS first
            bool hasLOS = HasLineOfSight(minion.Center, ownerPlayer.Center);
            if (hasLOS) {
                // LOS is clear, no cheese prevention needed
                return;
            }

            // No LOS - try A* path (max 18 tiles)
            List<Vector2>? pathToOwner = TilePathfinder.FindPath(
                minion.Center,
                ownerPlayer.Center,
                minion.width,
                minion.height,
                MaxAStarPathLengthTiles
            );

            if (pathToOwner != null && pathToOwner.Count > 0) {
                // Valid path exists - no cheese, minion can get back naturally
                if (DebugTethering) {
                    DebugPrintPathSteps(pathToOwner, $"Cheese check: {minion.Name}");
                }
                return;
            }

            // NO LOS and NO PATH = CHEESE DETECTED! Phase the minion back.
            if (DebugTethering) {
                Main.NewText($"[TETHER] CHEESE DETECTED! {minion.Name} has no path - phasing to player", Color.Red);
            }
            StartPhasingState(minion, "cheese prevention - no path after block placed");
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                     STUCK DETECTION (QOL PATHING)                  ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Stuck detection: When minion velocity has a positive component toward player,
        /// accumulate position samples. If the minion's current position is very close
        /// to the running average, it's "stuck" (bouncing against an obstacle).
        /// </summary>
        private void CheckStuckDetection(Projectile minion, Player ownerPlayer)
        {
            Vector2 minionVelocity = minion.velocity;
            float velocityMagnitude = minionVelocity.Length();

            // Must have some velocity
            if (velocityMagnitude < MinVelocityMagnitude) {
                // No velocity - reset sampling
                ResetStuckSampling();
                return;
            }

            // Get direction to player
            Vector2 vectorToPlayer = ownerPlayer.Center - minion.Center;
            if (vectorToPlayer.Length() < 1f) {
                // Already at player - reset sampling
                ResetStuckSampling();
                return;
            }

            // Check if velocity has positive component toward player (dot > 0)
            // Note: vectorToPlayer is NOT normalized yet for the dominant axis calculation
            float dotVelocityToPlayer = Vector2.Dot(minionVelocity, vectorToPlayer / vectorToPlayer.Length());

            if (dotVelocityToPlayer <= 0f) {
                // Velocity NOT toward player - reset sampling
                ResetStuckSampling();
                return;
            }

            Vector2 currentMinionPosition = minion.Center;
            Vector2 rawVectorToPlayer = ownerPlayer.Center - currentMinionPosition;

            // If this is the first sample, determine the dominant axis and record starting distance
            if (numTicksVelocityTowardsPlayer == 0) {
                dominantAxisIsHorizontal = Math.Abs(rawVectorToPlayer.X) >= Math.Abs(rawVectorToPlayer.Y);
                startingDistanceOnDominantAxis = dominantAxisIsHorizontal
                    ? Math.Abs(rawVectorToPlayer.X)
                    : Math.Abs(rawVectorToPlayer.Y);
            }

            // Check if minion made progress on the DOMINANT axis (got closer to player)
            float currentDistanceOnDominantAxis = dominantAxisIsHorizontal
                ? Math.Abs(rawVectorToPlayer.X)
                : Math.Abs(rawVectorToPlayer.Y);

            if (currentDistanceOnDominantAxis < startingDistanceOnDominantAxis - ProgressThresholdPixels) {
                // Made progress! Reset and start fresh with new baseline
                if (numTicksVelocityTowardsPlayer % 300 == 20) {
                    Main.NewText($"[STUCK-DBG] {minion.Name} PROGRESS on {(dominantAxisIsHorizontal ? "X" : "Y")}: {startingDistanceOnDominantAxis:F0} -> {currentDistanceOnDominantAxis:F0}", Color.Lime);
                }
                ResetStuckSampling();
                return;
            }

            // Velocity IS toward player but no progress on dominant axis - update running average
            avgPositionDuringSampling = ((avgPositionDuringSampling * numTicksVelocityTowardsPlayer) + currentMinionPosition) / (numTicksVelocityTowardsPlayer + 1);
            numTicksVelocityTowardsPlayer++;

            // Debug: Log every 300 ticks (at tick 20, 320, 620, etc.)
            if (numTicksVelocityTowardsPlayer % 300 == 20) {
                float distFromAvg = Vector2.Distance(currentMinionPosition, avgPositionDuringSampling);
                string axisName = dominantAxisIsHorizontal ? "X" : "Y";
                Main.NewText($"[STUCK-DBG] {minion.Name} tick={numTicksVelocityTowardsPlayer}, axis={axisName}, startDist={startingDistanceOnDominantAxis:F0}, currDist={currentDistanceOnDominantAxis:F0}, distFromAvg={distFromAvg:F1}px", Color.Cyan);
            }

            // Need minimum samples before checking for stuck
            if (numTicksVelocityTowardsPlayer < MinSamplesForStuckDetection)
                return;

            // Check if minion is stuck: |currentPos - avgPos| < threshold
            float distanceFromAverage = Vector2.Distance(currentMinionPosition, avgPositionDuringSampling);

            if (distanceFromAverage >= StuckDistanceThresholdPixels) {
                // Minion IS making progress - reset consecutive frames counter
                consecutiveFramesBelowStuckThreshold = 0;
                return;
            }

            // Distance is below threshold - increment consecutive frames counter
            consecutiveFramesBelowStuckThreshold++;

            // Must be stuck for MinFramesToConsiderStuck consecutive frames
            if (consecutiveFramesBelowStuckThreshold < MinFramesToConsiderStuck)
                return;

            // STUCK DETECTED! Minion velocity toward player but position not changing for sustained period.
            if (DebugTethering) {
                Main.NewText($"[TETHER] STUCK: {minion.Name} velocity toward player but avg distance={distanceFromAverage:F1}px over {numTicksVelocityTowardsPlayer} ticks", Color.Yellow);
            }

            // Reset sampling before triggering action
            ResetStuckSampling();

            // Try A* pathing
            List<Vector2>? pathToOwner = TilePathfinder.FindPath(
                minion.Center,
                ownerPlayer.Center,
                minion.width,
                minion.height,
                MaxAStarPathLengthTiles
            );

            if (pathToOwner != null && pathToOwner.Count > 0) {
                // Found a path - follow it!
                int pathLengthTiles = pathToOwner.Count - 1;
                StartPathFollowingState(minion, pathToOwner, $"stuck detection - {pathLengthTiles} tile path found");
            }
            else {
                // No path - phase toward player
                if (DebugTethering) {
                    Main.NewText($"[TETHER] STUCK: {minion.Name} no A* path, phasing toward player", Color.Orange);
                }
                StartPhasingState(minion, "stuck detection - no path available");
            }
        }

        /// <summary>
        /// Reset stuck detection sampling state.
        /// </summary>
        private void ResetStuckSampling()
        {
            avgPositionDuringSampling = Vector2.Zero;
            numTicksVelocityTowardsPlayer = 0;
            consecutiveFramesBelowStuckThreshold = 0;
            dominantAxisIsHorizontal = false;
            startingDistanceOnDominantAxis = 0f;
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                      PATH FOLLOWING                                ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Apply path-following movement - walk/fly through A* waypoints.
        /// </summary>
        private void ApplyPathFollowingMovement(Projectile minion, Player ownerPlayer)
        {
            // Check if we've reached the owner (back in LOS and close enough)
            bool hasLOSNow = HasLineOfSight(minion.Center, ownerPlayer.Center);
            float distanceToOwner = Vector2.Distance(minion.Center, ownerPlayer.Center);

            if (hasLOSNow && distanceToOwner < PhasingArrivalDistance) {
                // Arrived - end path following
                EndPathFollowingState(minion);

                if (DebugTethering) {
                    Main.NewText($"[TETHER] {minion.Name} reached owner via A* path", Color.Green);
                }
                return;
            }

            // Check if we have valid waypoints
            if (currentPathWaypoints == null || currentWaypointIndex >= currentPathWaypoints.Count) {
                // Path exhausted but not at owner - switch to phasing
                if (DebugTethering) {
                    Main.NewText($"[TETHER] {minion.Name} A* path exhausted, switching to phase", Color.Yellow);
                }
                EndPathFollowingState(minion);
                StartPhasingState(minion, "path exhausted");
                return;
            }

            // Get current waypoint target
            Vector2 waypointWorldPosition = currentPathWaypoints[currentWaypointIndex];

            // Check if we've reached this waypoint
            float distanceToWaypoint = Vector2.Distance(minion.Center, waypointWorldPosition);
            if (distanceToWaypoint < WaypointArrivalDistance) {
                // Move to next waypoint
                currentWaypointIndex++;
                return;
            }

            // Move toward current waypoint
            Vector2 directionToWaypoint = waypointWorldPosition - minion.Center;
            if (directionToWaypoint.Length() > 0) {
                directionToWaypoint.Normalize();
                minion.velocity = directionToWaypoint * PathFollowSpeed;
            }

            // Spawn subtle trail while path-following
            if (Main.rand.NextBool(8)) {
                Dust pathDust = Dust.NewDustPerfect(
                    minion.Center,
                    DustID.GreenTorch,
                    -minion.velocity * 0.1f,
                    Alpha: 180,
                    Scale: 0.4f
                );
                pathDust.noGravity = true;
            }
        }

        /// <summary>
        /// Apply phasing movement - fly through walls toward owner.
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
                float speedFactor = distanceToOwner >= PhasingSlowdownDistance
                    ? 1f
                    : distanceToOwner / PhasingSlowdownDistance;

                float currentSpeed = MathHelper.Lerp(2f, PhasingMaxFlySpeed, speedFactor);
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

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                      STATE MANAGEMENT                              ║
        // ╚════════════════════════════════════════════════════════════════════╝

        private void StartPathFollowingState(Projectile minion, List<Vector2> pathWaypoints, string reason)
        {
            if (isFollowingPath || isPhasingToOwner)
                return; // Already in a recall state

            isFollowingPath = true;
            currentPathWaypoints = pathWaypoints;
            currentWaypointIndex = 0;

            if (DebugTethering) {
                Main.NewText($"[TETHER] {minion.Name} starting A* path ({pathWaypoints.Count} waypoints): {reason}", Color.Lime);
            }
        }

        private void EndPathFollowingState(Projectile minion)
        {
            isFollowingPath = false;
            currentPathWaypoints = null;
            currentWaypointIndex = 0;
        }

        private void StartPhasingState(Projectile minion, string reason)
        {
            if (isPhasingToOwner)
                return; // Already phasing

            // End path-following if active
            if (isFollowingPath) {
                EndPathFollowingState(minion);
            }

            isPhasingToOwner = true;
            phasingStartedEpoch = GetEpochTimeSeconds();
            originalTileCollide = minion.tileCollide;
            minion.tileCollide = false;

            // Spawn phase-out effect
            SpawnPhaseEffect(minion.Center, fadeOut: true);

            if (DebugTethering) {
                Main.NewText($"[TETHER] {minion.Name} starting PHASE to owner: {reason}", Color.Orange);
            }
        }

        private void EndPhasingState(Projectile minion)
        {
            isPhasingToOwner = false;
            minion.tileCollide = originalTileCollide;

            // Spawn phase-in effect
            SpawnPhaseEffect(minion.Center, fadeOut: false);
        }

        /// <summary>
        /// Force teleport minion to player (fallback when phasing fails).
        /// </summary>
        private void TeleportMinionToPlayer(Projectile minion, Player targetPlayer, string reason)
        {
            // Calculate new position near player
            float randomOffsetX = Main.rand.NextFloat(-40f, 40f);
            Vector2 newPosition = targetPlayer.Center + new Vector2(randomOffsetX, -20f);

            // Spawn dust effect at old position
            SpawnPhaseEffect(minion.Center, fadeOut: true);

            // Move minion
            minion.Center = newPosition;
            minion.velocity = Vector2.Zero;

            // Spawn dust effect at new position
            SpawnPhaseEffect(newPosition, fadeOut: false);

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
        // ║                      PATH DEBUG                                    ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Debug helper: Print each step of the path as tile deltas.
        /// This shows the ACTUAL movements, not just waypoint count.
        /// </summary>
        private static void DebugPrintPathSteps(List<Vector2> pathWaypoints, string context)
        {
            if (pathWaypoints.Count == 0) {
                Main.NewText($"[TETHER] {context}: EMPTY PATH!", Color.Red);
                return;
            }

            // Convert world pixel waypoints to tile positions
            var tilePositions = new List<Point>();
            foreach (Vector2 worldPos in pathWaypoints) {
                tilePositions.Add(worldPos.ToTileCoordinates());
            }

            // Build string of relative moves
            var moves = new System.Text.StringBuilder();
            int totalManhattanDistance = 0;

            for (int i = 1; i < tilePositions.Count; i++) {
                Point prev = tilePositions[i - 1];
                Point curr = tilePositions[i];
                int dx = curr.X - prev.X;
                int dy = curr.Y - prev.Y;
                totalManhattanDistance += Math.Abs(dx) + Math.Abs(dy);
                moves.Append($"({dx:+0;-0;0},{dy:+0;-0;0})");
            }

            int waypointCount = pathWaypoints.Count;
            int stepCount = waypointCount - 1;

            Main.NewText($"[TETHER] {context}: {waypointCount} waypoints, {stepCount} steps, manhattan={totalManhattanDistance}", Color.Gray);
            if (moves.Length > 0) {
                // Print moves in chunks to avoid chat overflow
                string moveStr = moves.ToString();
                if (moveStr.Length > 100) {
                    Main.NewText($"  Moves: {moveStr[..100]}...", Color.DarkGray);
                }
                else {
                    Main.NewText($"  Moves: {moveStr}", Color.DarkGray);
                }
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

    /// <summary>
    /// GlobalTile that detects when players place blocks.
    /// Sets the tick when placed for TetheredMinionProjectile to check on the NEXT frame.
    /// </summary>
    public class TetheredMinionTileDetector : GlobalTile
    {
        public override void PlaceInWorld(int i, int j, int type, Item item)
        {
            // Find which player placed this tile (check who has itemTime > 0 and is placing)
            for (int playerIndex = 0; playerIndex < Main.maxPlayers; playerIndex++) {
                Player player = Main.player[playerIndex];
                if (player.active && !player.dead && player.itemTime > 0) {
                    // Check if player's held item could place this tile
                    if (player.HeldItem.createTile == type) {
                        // Schedule cheese check for next frame (tile doesn't exist yet in PlaceInWorld)
                        TetheredMinionProjectile.DoCheeseCheckOnTick = (int)Main.GameUpdateCount + 1;
                        TetheredMinionProjectile.BlockPlacerPlayerIndex = playerIndex;
                        return;
                    }
                }
            }
        }
    }
}