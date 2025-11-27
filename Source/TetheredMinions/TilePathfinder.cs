using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace TerrariaSurvivalMod.TetheredMinions
{
    /// <summary>
    /// Simple A* pathfinder on Terraria's tile grid.
    /// Used to determine if a walking/flying path exists between two points.
    /// </summary>
    public static class TilePathfinder
    {
        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                        TUNABLE CONSTANTS                           ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>Default maximum search radius in tiles (bounded by minion tether distance)</summary>
        public const int DefaultMaxSearchRadiusTiles = 35;

        /// <summary>Maximum nodes to explore before giving up (prevents runaway on complex maps)</summary>
        private const int MaxNodesExplored = 1500;

        /// <summary>Pixels per tile for clearance calculations</summary>
        private const float PixelsPerTile = 16f;

        // 4-directional movement offsets (no diagonals - corners can't be cut)
        private static readonly Point[] CardinalOffsets = {
            new Point(0, -1),   // Up
            new Point(0, 1),    // Down
            new Point(-1, 0),   // Left
            new Point(1, 0)     // Right
        };

        // Thread-local storage for current pathfinding clearance (avoids parameter threading everywhere)
        [ThreadStatic]
        private static int _currentClearanceWidthTiles;
        [ThreadStatic]
        private static int _currentClearanceHeightTiles;

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                          A* NODE                                   ║
        // ╚════════════════════════════════════════════════════════════════════╝

        private struct PathNode : IComparable<PathNode>
        {
            public Point TilePosition;
            public float GCost;  // Cost from start
            public float FCost;  // GCost + heuristic to goal

            public int CompareTo(PathNode other)
            {
                return FCost.CompareTo(other.FCost);
            }
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                      PUBLIC API                                    ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Calculate the clearance in tiles needed for a given size in pixels.
        /// Rounds up to ensure the entity can always fit.
        /// </summary>
        public static int CalculateTileClearance(float sizePixels)
        {
            return (int)MathF.Ceiling(sizePixels / PixelsPerTile);
        }

        /// <summary>
        /// Check if a walking/flying path exists between two world positions.
        /// Does NOT return the actual path, just whether one exists.
        /// Uses default 2x2 tile clearance.
        /// </summary>
        public static bool PathExists(Vector2 fromWorldPosition, Vector2 toWorldPosition)
        {
            return PathExists(fromWorldPosition, toWorldPosition, 2, 2);
        }

        /// <summary>
        /// Check if a walking/flying path exists for an entity with specific hitbox size.
        /// </summary>
        /// <param name="fromWorldPosition">Start position in world coordinates (pixels)</param>
        /// <param name="toWorldPosition">Goal position in world coordinates (pixels)</param>
        /// <param name="entityWidthPixels">Entity hitbox width in pixels</param>
        /// <param name="entityHeightPixels">Entity hitbox height in pixels</param>
        /// <returns>True if a path exists, false if blocked or unreachable</returns>
        public static bool PathExists(Vector2 fromWorldPosition, Vector2 toWorldPosition, float entityWidthPixels, float entityHeightPixels)
        {
            Point startTile = fromWorldPosition.ToTileCoordinates();
            Point goalTile = toWorldPosition.ToTileCoordinates();

            int clearanceWidth = CalculateTileClearance(entityWidthPixels);
            int clearanceHeight = CalculateTileClearance(entityHeightPixels);

            return FindPathBetweenTiles(startTile, goalTile, clearanceWidth, clearanceHeight) != null;
        }

        /// <summary>
        /// Find the actual A* path between two world positions.
        /// Returns list of world pixel positions (centered for clearance) from start to goal, or null if no path exists.
        /// Uses default 2x2 tile clearance.
        /// </summary>
        public static List<Vector2>? FindPath(Vector2 fromWorldPosition, Vector2 toWorldPosition)
        {
            return FindPath(fromWorldPosition, toWorldPosition, 32f, 32f);  // Default 2x2 tile = 32x32 pixels
        }

        /// <summary>
        /// Find the actual A* path for an entity with specific hitbox size.
        /// Returns waypoints in WORLD PIXEL coordinates, centered for the entity's clearance.
        /// </summary>
        /// <param name="fromWorldPosition">Start position in world coordinates (pixels)</param>
        /// <param name="toWorldPosition">Goal position in world coordinates (pixels)</param>
        /// <param name="entityWidthPixels">Entity hitbox width in pixels</param>
        /// <param name="entityHeightPixels">Entity hitbox height in pixels</param>
        /// <param name="maxPathLengthTiles">Optional maximum search radius in tiles (default: 35)</param>
        /// <returns>List of Vector2 world positions from start to goal, or null if no path</returns>
        public static List<Vector2>? FindPath(Vector2 fromWorldPosition, Vector2 toWorldPosition, float entityWidthPixels, float entityHeightPixels, int maxPathLengthTiles = DefaultMaxSearchRadiusTiles)
        {
            Point startTile = fromWorldPosition.ToTileCoordinates();
            Point goalTile = toWorldPosition.ToTileCoordinates();

            int clearanceWidth = CalculateTileClearance(entityWidthPixels);
            int clearanceHeight = CalculateTileClearance(entityHeightPixels);

            List<Point>? tilePath = FindPathBetweenTiles(startTile, goalTile, clearanceWidth, clearanceHeight, maxPathLengthTiles);
            
            if (tilePath == null) {
                return null;
            }

            // Convert tile path to world coordinates, centering on the clearance area
            var worldPath = new List<Vector2>(tilePath.Count);
            float halfClearanceX = (clearanceWidth * PixelsPerTile) / 2f;
            float halfClearanceY = (clearanceHeight * PixelsPerTile) / 2f;

            foreach (Point tilePos in tilePath) {
                // Target the CENTER of the clearance area, not the tile corner
                Vector2 worldPos = new Vector2(
                    tilePos.X * PixelsPerTile + halfClearanceX,
                    tilePos.Y * PixelsPerTile + halfClearanceY
                );
                worldPath.Add(worldPos);
            }

            return worldPath;
        }

        /// <summary>
        /// Check if a path exists between two tile positions.
        /// </summary>
        public static bool PathExistsBetweenTiles(Point startTile, Point goalTile, int clearanceWidthTiles = 2, int clearanceHeightTiles = 2)
        {
            return FindPathBetweenTiles(startTile, goalTile, clearanceWidthTiles, clearanceHeightTiles) != null;
        }

        /// <summary>
        /// Find path between two tile positions using A*.
        /// </summary>
        /// <param name="startTile">Starting tile position</param>
        /// <param name="goalTile">Goal tile position</param>
        /// <param name="clearanceWidthTiles">Required clearance width in tiles</param>
        /// <param name="clearanceHeightTiles">Required clearance height in tiles</param>
        /// <param name="maxSearchRadiusTiles">Maximum search radius in tiles</param>
        /// <returns>List of tile Points from start to goal (inclusive), or null if no path</returns>
        public static List<Point>? FindPathBetweenTiles(Point startTile, Point goalTile, int clearanceWidthTiles = 2, int clearanceHeightTiles = 2, int maxSearchRadiusTiles = DefaultMaxSearchRadiusTiles)
        {
            // Store clearance in thread-local variables for use by IsTilePassable
            _currentClearanceWidthTiles = Math.Max(1, clearanceWidthTiles);
            _currentClearanceHeightTiles = Math.Max(1, clearanceHeightTiles);

            // Quick check: if start or goal is blocked, no path
            if (!IsTilePassable(startTile.X, startTile.Y) || !IsTilePassable(goalTile.X, goalTile.Y)) {
                return null;
            }

            // Quick check: same tile
            if (startTile == goalTile) {
                return new List<Point> { startTile };
            }

            // Quick check: if distance > max radius, definitely no path within bounds
            float tileDistance = Vector2.Distance(startTile.ToVector2(), goalTile.ToVector2());
            if (tileDistance > maxSearchRadiusTiles) {
                return null;
            }

            // A* search
            var openSet = new SortedSet<PathNode>(Comparer<PathNode>.Create((a, b) => {
                int cmp = a.FCost.CompareTo(b.FCost);
                if (cmp == 0) {
                    // Tiebreaker to prevent SortedSet from treating equal FCost as duplicates
                    cmp = a.TilePosition.X.CompareTo(b.TilePosition.X);
                    if (cmp == 0) {
                        cmp = a.TilePosition.Y.CompareTo(b.TilePosition.Y);
                    }
                }
                return cmp;
            }));

            var gCosts = new Dictionary<Point, float>();
            var cameFrom = new Dictionary<Point, Point>(); // Track path for reconstruction
            var explored = new HashSet<Point>();

            // Initialize with start node
            float startHeuristic = Heuristic(startTile, goalTile);
            openSet.Add(new PathNode {
                TilePosition = startTile,
                GCost = 0f,
                FCost = startHeuristic
            });
            gCosts[startTile] = 0f;

            int nodesExplored = 0;

            while (openSet.Count > 0 && nodesExplored < MaxNodesExplored) {
                // Get node with lowest FCost
                PathNode currentNode = openSet.Min;
                openSet.Remove(currentNode);

                Point currentTile = currentNode.TilePosition;

                // Check if we reached the goal
                if (currentTile == goalTile) {
                    // Reconstruct path from goal back to start
                    return ReconstructPath(cameFrom, startTile, goalTile);
                }

                // Skip if already explored
                if (explored.Contains(currentTile)) {
                    continue;
                }
                explored.Add(currentTile);
                nodesExplored++;

                // Explore cardinal neighbors (4-directional - no diagonals since corners can't be cut)
                for (int i = 0; i < CardinalOffsets.Length; i++) {
                    Point offset = CardinalOffsets[i];
                    Point neighborTile = new Point(currentTile.X + offset.X, currentTile.Y + offset.Y);

                    // Skip if out of bounds or already explored
                    if (explored.Contains(neighborTile)) {
                        continue;
                    }

                    // Skip if too far from goal (bounding box optimization)
                    if (Math.Abs(neighborTile.X - goalTile.X) > maxSearchRadiusTiles ||
                        Math.Abs(neighborTile.Y - goalTile.Y) > maxSearchRadiusTiles) {
                        continue;
                    }

                    // Skip if not passable
                    if (!IsTilePassable(neighborTile.X, neighborTile.Y)) {
                        continue;
                    }

                    float tentativeGCost = currentNode.GCost + 1f;  // All cardinal moves cost 1

                    // Skip if we already have a better path to this tile
                    if (gCosts.TryGetValue(neighborTile, out float existingGCost) && tentativeGCost >= existingGCost) {
                        continue;
                    }

                    // Update/add this neighbor
                    gCosts[neighborTile] = tentativeGCost;
                    cameFrom[neighborTile] = currentTile; // Track where we came from
                    float fCost = tentativeGCost + Heuristic(neighborTile, goalTile);

                    openSet.Add(new PathNode {
                        TilePosition = neighborTile,
                        GCost = tentativeGCost,
                        FCost = fCost
                    });
                }
            }

            // If we get here, no path was found
            return null;
        }

        /// <summary>
        /// Reconstruct the path from cameFrom dictionary.
        /// Returns list from start to goal (inclusive).
        /// </summary>
        private static List<Point> ReconstructPath(Dictionary<Point, Point> cameFrom, Point startTile, Point goalTile)
        {
            var path = new List<Point>();
            Point current = goalTile;

            while (current != startTile) {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Add(startTile);

            // Reverse so it goes start -> goal
            path.Reverse();
            return path;
        }

        // ╔════════════════════════════════════════════════════════════════════╗
        // ║                      HELPER METHODS                                ║
        // ╚════════════════════════════════════════════════════════════════════╝

        /// <summary>
        /// Check if a tile has clearance for minion movement (NxM tile area).
        /// This accounts for minion hitboxes being larger than 1 tile.
        /// Uses the current thread-local clearance values.
        /// </summary>
        private static bool IsTilePassable(int tileX, int tileY)
        {
            // Check an area of clearanceWidth x clearanceHeight tiles
            for (int xOffset = 0; xOffset < _currentClearanceWidthTiles; xOffset++) {
                for (int yOffset = 0; yOffset < _currentClearanceHeightTiles; yOffset++) {
                    if (!IsSingleTilePassable(tileX + xOffset, tileY + yOffset)) {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Check if a single tile is passable (can be walked/flown through).
        /// </summary>
        private static bool IsSingleTilePassable(int tileX, int tileY)
        {
            // Bounds check
            if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY) {
                return false;
            }

            Tile tile = Main.tile[tileX, tileY];

            // No tile = passable (air)
            if (!tile.HasTile) {
                return true;
            }

            // Platforms and other non-solid tiles are passable
            if (!Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]) {
                return true;
            }

            // Actuated tiles (toggled off) are passable
            if (!tile.IsActuated) {
                return false;  // Solid and not actuated = blocked
            }

            return true;  // Actuated solid = passable
        }

        /// <summary>
        /// Euclidean heuristic for A*.
        /// </summary>
        private static float Heuristic(Point from, Point to)
        {
            float dx = from.X - to.X;
            float dy = from.Y - to.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}