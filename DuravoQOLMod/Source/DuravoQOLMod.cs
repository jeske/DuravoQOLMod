// MIT Licensed - Copyright (c) 2025 David W. Jeske
using System.IO;
using Terraria.ModLoader;
using DuravoQOLMod.PersistentPosition;

namespace DuravoQOLMod
{
    /// <summary>
    /// Packet types for mod networking (server <-> client communication)
    /// </summary>
    public enum ModPacketType : byte
    {
        /// <summary>Server sends saved position to client for restore</summary>
        RestoreSavedPosition = 1,
    }

    public class DuravoQOLMod : Mod
    {
        // Main mod class - tModLoader entry point
        // Features are implemented via ModPlayer, GlobalNPC, etc. classes

        /// <summary>
        /// Handle incoming packets from server or client.
        /// Called by tModLoader when a ModPacket is received.
        /// Dispatches to feature-specific packet handlers.
        /// </summary>
        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            ModPacketType packetType = (ModPacketType)reader.ReadByte();

            // NOTE: do not put the packet handling code right here! All feature code should
            // be in the feature directory, and this code should call into it as you see below.


            switch (packetType) {
                case ModPacketType.RestoreSavedPosition:
                    PersistentPositionPacketHandler.HandleRestoreSavedPositionPacket(reader, whoAmI);
                    break;
                default:
                    // Unknown packet type - silently ignore
                    break;
            }
        }
    }
}