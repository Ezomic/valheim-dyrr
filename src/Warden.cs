using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Dyrr
{
    /// <summary>
    /// The client-side half of the door, on servers.
    ///
    /// MenuGuard covers local worlds, because FejdStartup.OnWorldStart knows which world is
    /// about to be started. A server does not offer that: its world identity is not known
    /// until after connecting, so until now the client had nothing to check and the only thing
    /// standing between a character and a server that would ruin it was that server choosing
    /// to enforce. A server that does not enforce is a hole, and it is not a hypothetical one -
    /// it is how a character was ruined here the first time two servers ran side by side, by
    /// the one that was being lenient.
    ///
    /// This closes it from the side that actually has something to lose. It is the same rule
    /// MenuGuard applies, moved to the one moment on the join path where the world uid is
    /// known and the damage has not happened yet.
    ///
    /// **Why this moment.** The client learns the world it is joining inside ZNet.RPC_PeerInfo,
    /// which reads the world name, seed and uid straight off the package. The permanent record
    /// this mod exists to prevent is written by PlayerProfile.GetWorldData, which is reached
    /// only from the logout point, the map data and the spawn point - all of which need a
    /// spawned player. So there is a window here, after the world is known and before anything
    /// has been written, and this is the whole of it.
    ///
    /// **Why aborting is safe.** Setting the connection status and disconnecting is exactly
    /// what vanilla RPC_Kicked does. Game.Update then sees a status that is neither Connecting
    /// nor Connected and calls Logout, which saves the profile on the way out - but
    /// Game.SavePlayerProfile does nothing at all without Player.m_localPlayer, and there is
    /// no local player yet. The abort therefore cannot itself write the entry it is preventing.
    ///
    /// The status used is ErrorDisconnected rather than ErrorKicked. Nobody kicked anybody:
    /// this machine refused on its own behalf, and the server may not even be running Dyrr.
    /// With Core installed the real reason is appended to that screen anyway.
    /// </summary>
    internal static class Warden
    {
        private static FieldInfo _world, _status;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static void GuardJoin(ZNet __instance, ZRpc rpc)
        {
            if (!DyrrConfig.ProtectCharacter.Value) return;
            if (!DyrrConfig.ProtectOnServers.Value) return;

            // On a local world this machine is the server, and MenuGuard has already had its
            // say before the world was ever started.
            if (__instance.IsServer()) return;

            // A postfix runs on every path out of RPC_PeerInfo, including the version-mismatch
            // return that never reaches the client branch. No world, nothing known, nothing to
            // judge.
            var world = World(__instance);
            if (world == null || world.m_uid == 0L) return;

            if (Game.instance == null) return;
            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return;

            var id = Home.IdOf(profile);
            if (id == 0L) return;

            var home = Home.GetBinding(id);

            // No binding means the character has not been accepted anywhere yet, so this is a
            // legitimate first world. DyrrPlugin.Update records it once the player spawns.
            if (home.World == 0L || home.World == world.m_uid) return;

            var why = "This character belongs to world " + Home.Describe(home) +
                ", and this server is world '" + world.m_name + "' (" + world.m_uid + "). " +
                "Joining would record this world in the character permanently and lock it out " +
                "of its own server, so Dyrr stopped the join before it happened. If the " +
                "binding is wrong, delete the line starting " + id +
                " from BepInEx/config/dyrr-home.txt.";

            DyrrPlugin.Log.LogWarning("Refused to join world '" + world.m_name + "' (" +
                world.m_uid + ") with character '" + profile.GetName() + "' (" + id +
                "), which belongs to world " + Home.Describe(home) + ".");

            DyrrPlugin.Explain("Dyrr: " + why);

            Abort(__instance, rpc);
        }

        /// <summary>
        /// Drop the connection the way vanilla drops a kicked one: set the status the menu will
        /// report, then disconnect the peer. Game.Update notices on the next frame and takes
        /// the player back to the start scene, where FejdStartup.Start shows the panel.
        /// </summary>
        private static void Abort(ZNet net, ZRpc rpc)
        {
            if (_status == null) _status = AccessTools.Field(typeof(ZNet), "m_connectionStatus");

            if (_status != null) _status.SetValue(null, ZNet.ConnectionStatus.ErrorDisconnected);
            else DyrrPlugin.Log.LogWarning(
                "ZNet.m_connectionStatus not found - disconnecting anyway, but the menu will " +
                "report a generic failure.");

            var peer = PeerOf(net, rpc);
            if (peer != null) net.Disconnect(peer);
        }

        /// <summary>ZNet.GetPeer(ZRpc) is private, and matching on the rpc is all it does.</summary>
        private static ZNetPeer PeerOf(ZNet net, ZRpc rpc)
        {
            List<ZNetPeer> peers = net.GetPeers();
            if (peers == null) return null;

            foreach (var peer in peers)
                if (peer != null && peer.m_rpc == rpc) return peer;

            return null;
        }

        /// <summary>
        /// The world by field rather than through GetWorldUID, which dereferences m_world
        /// without checking it and throws on every path where the client never got that far.
        /// </summary>
        private static World World(ZNet net)
        {
            if (_world == null) _world = AccessTools.Field(typeof(ZNet), "m_world");
            return _world != null ? _world.GetValue(net) as World : null;
        }
    }
}
