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

        /// <summary>Maximum search radius in tiles (bounded by minion tether distance)</summary>
        private const int MaxSearchRadiusTiles = 35;

        /// <summary>Maximum nodes to explore before giving up (prevents runaway on complex maps)</summary>
        private const int MaxNodesExplored = 1500;

        // 4-directional movement offsets (no diagonals - corners can't be cut)
        private static readonly Point[] CardinalOffsets = {
            new Point(0, -1),   // Up
            new Point(0, 1),    // Down
            new Point(-1, 0),   // Left
            new Point(1, 0)     // Right
        };

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
        /// Check if a walking/flying path exists between two world positions.
        /// Does NOT return the actual path, just whether one exists.
        /// </summary>
        /// <param name="fromWorldPosition">Start position in world coordinates (pixels)</param>
        /// <param name="toWorldPosition">Goal position in world coordinates (pixels)</param>
        /// <returns>True if a path exists, false if blocked or unreachable</returns>
        public static bool PathExists(Vector2 fromWorldPosition, Vector2 toWorldPosition)
        {
            // Convert world coords to tile coords
            Point startTile = fromWorldPosition.ToTileCoordinates();
            Point goalTile = toWorldPosition.ToTileCoordinates();

            return PathExistsBetweenTiles(startTile, goalTile);
        }

        /// <summary>
        /// Find the actual A* path between two world positions.
        /// Returns list of tile positions from start to goal, or null if no path exists.
        /// </summary>
        /// <param name="fromWorldPosition">Start position in world coordinates (pixels)</param>
        /// <param name="toWorldPosition">Goal position in world coordinates (pixels)</param>
        /// <returns>List of tile Points from start to goal, or null if no path</returns>
        public static List<Point>? FindPath(Vector2 fromWorldPosition, Vector2 toWorldPosition)
        {
            Point startTile = fromWorldPosition.ToTileCoordinates();
            Point goalTile = toWorldPosition.ToTileCoordinates();

            return FindPathBetweenTiles(startTile, goalTile);
        }

        /// <summary>
        /// Check if a path exists between two tile positions.
        /// </summary>
        public static bool PathExistsBetweenTiles(Point startTile, Point goalTile)
        {
            return FindPathBetweenTiles(startTile, goalTile) != null;
        }

        /// <summary>
        /// Find path between two tile positions using A*.
        /// </summary>
        /// <returns>List of tile Points from start to goal (inclusive), or null if no path</returns>
        public static List<Point>? FindPathBetweenTiles(Point startTile, Point goalTile)
        {
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
            if (tileDistance > MaxSearchRadiusTiles) {
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
                    if (Math.Abs(neighborTile.X - goalTile.X) > MaxSearchRadiusTiles ||
                        Math.Abs(neighborTile.Y - goalTile.Y) > MaxSearchRadiusTiles) {
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
        /// Check if a tile is passable (can be walked/flown through).
        /// </summary>
        private static bool IsTilePassable(int tileX, int tileY)
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