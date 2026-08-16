using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace Threshold
{
    /// <summary>
    /// The door.
    ///
    /// The shape is lifted from Core's version handshake, and for the same reason: both ends
    /// register an RPC the moment the connection object exists, in OnNewConnection, which
    /// happens before either side sends PeerInfo. ZRpc delivers in order on one connection, so
    /// by the time RPC_PeerInfo runs the answer has already arrived and there is something to
    /// judge. Anything later means deciding on data that has not turned up yet, and the
    /// symptom is a door that admits the first connection and works ever after.
    ///
    /// It refuses before the player is admitted rather than after they have spawned. Boon's
    /// version - which is where this policy came from - ran after spawn on a routed RPC, so a
    /// refused player watched the world load and then got dropped, which reads far more like a
    /// crash than like a rule.
    /// </summary>
    internal static class Doorman
    {
        private const string RpcFacts = "Threshold_Facts";
        private const string RpcRefused = "Threshold_Refused";

        /// <summary>What each connection told us, keyed by ZRpc - the only identity that
        /// exists this early, since ZNetPeer is not set up until PeerInfo.</summary>
        private static readonly Dictionary<ZRpc, Report> Received = new Dictionary<ZRpc, Report>();

        private struct Report
        {
            internal bool Readable;
            internal bool Cheats;
            internal List<long> Worlds;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static void Handshake(ZNetPeer peer)
        {
            peer.m_rpc.Register<ZPackage>(RpcFacts, Receive);
            peer.m_rpc.Register<string>(RpcRefused, OnRefused);

            peer.m_rpc.Invoke(RpcFacts, Facts.Gather());
        }

        private static void Receive(ZRpc rpc, ZPackage pkg)
        {
            var report = new Report { Worlds = new List<long>() };

            report.Readable = pkg.ReadBool();
            if (report.Readable)
            {
                report.Cheats = pkg.ReadBool();
                var count = pkg.ReadInt();
                for (var i = 0; i < count; i++) report.Worlds.Add(pkg.ReadLong());
            }

            Received[rpc] = report;
        }

        /// <summary>
        /// The decision. Only the server can act on it; a client that receives this is simply
        /// answering the server's question and has nothing to decide.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static bool Judge(ZNet __instance, ZRpc rpc)
        {
            if (!ThresholdConfig.Enabled.Value) return true;
            if (!__instance.IsServer()) return true;

            Report report;
            var heard = Received.TryGetValue(rpc, out report);

            var why = Verdict(__instance, heard, report);
            if (why == null) return true;

            if (!ThresholdConfig.Enforce.Value)
            {
                ThresholdPlugin.Log.LogWarning("Would have refused a connection: " + why);
                return true;
            }

            ThresholdPlugin.Log.LogWarning("Refused a connection: " + why);

            // Tell them before dropping them. The reason travels to the client so it lands in
            // *their* log, because Valheim's refusal screen carries no text of its own and a
            // player on somebody else's server can never read the server's log. Being told
            // which rule you broke is the difference between a door and a mystery.
            rpc.Invoke(RpcRefused, ThresholdConfig.RefusedMessage.Value + " (" + why + ")");
            rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorKicked);
            return false;
        }

        /// <summary>Null when the character may come in, otherwise the reason it may not.</summary>
        private static string Verdict(ZNet net, bool heard, Report report)
        {
            if (!heard)
            {
                // No answer at all means the far end has no Threshold. Core's gate should have
                // turned that away already, so this is a backstop rather than the main line -
                // but a door that opens when the question goes unanswered is not a door.
                return ThresholdConfig.RefuseUnreported.Value
                    ? "this client did not report its character"
                    : null;
            }

            if (!report.Readable)
                return ThresholdConfig.RefuseUnreported.Value
                    ? "this character's profile could not be read"
                    : null;

            var reasons = new StringBuilder();

            if (ThresholdConfig.RefuseOtherWorlds.Value)
            {
                var here = net.GetWorldUID();
                var others = 0;
                foreach (var uid in report.Worlds)
                    if (uid != here) others++;

                if (others > 0)
                    reasons.Append("has played on ").Append(others).Append(" other world(s)");
            }

            if (ThresholdConfig.RefuseCheats.Value && report.Cheats)
            {
                if (reasons.Length > 0) reasons.Append(", ");
                reasons.Append("is flagged as having used cheats");
            }

            return reasons.Length == 0 ? null : reasons.ToString();
        }

        /// <summary>Client side: say out loud why the door shut.</summary>
        private static void OnRefused(ZRpc rpc, string why)
        {
            ThresholdPlugin.Log.LogError("This server refused your character: " + why);
        }

        /// <summary>A connection that is gone cannot be asked about again.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), "Disconnect")]
        private static void Forget(ZNetPeer peer)
        {
            if (peer != null && peer.m_rpc != null) Received.Remove(peer.m_rpc);
        }
    }
}
