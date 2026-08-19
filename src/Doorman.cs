using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace Dyrr
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
    /// It refuses before the player is admitted rather than after they have spawned. Rist's
    /// version - which is where this policy came from - ran after spawn on a routed RPC, so a
    /// refused player watched the world load and then got dropped, which reads far more like a
    /// crash than like a rule.
    /// </summary>
    internal static class Doorman
    {
        private const string RpcFacts = "Dyrr_Facts";
        private const string RpcRefused = "Dyrr_Refused";

        /// <summary>What each connection told us, keyed by ZRpc - the only identity that
        /// exists this early, since ZNetPeer is not set up until PeerInfo.</summary>
        private static readonly Dictionary<ZRpc, Report> Received = new Dictionary<ZRpc, Report>();

        /// <summary>
        /// The verdict reached for each connection, null where the character was admitted.
        ///
        /// Kept purely so the door can be asked what it is doing rather than only reporting it
        /// one line at a time into a log. That matters most with Enforce off, which is the
        /// setting an admin is supposed to sit on while deciding: without a way to see the
        /// whole picture, "off and reporting" means reading a log for lines that scrolled past
        /// while nobody was watching. See Commands.
        /// </summary>
        private static readonly Dictionary<ZRpc, string> Verdicts = new Dictionary<ZRpc, string>();

        /// <summary>How many connections have actually been turned away since startup.</summary>
        internal static int RefusedCount;

        /// <summary>Client side: the last reason a server gave for refusing this machine.</summary>
        internal static string LastRefusal;

        /// <summary>The verdict for a live connection: null when it was admitted, and false
        /// when the door never judged it at all (disabled, or not the server).</summary>
        internal static bool TryVerdict(ZRpc rpc, out string why)
        {
            return Verdicts.TryGetValue(rpc, out why);
        }

        private struct Report
        {
            internal bool Readable;
            internal int Format;
            internal bool Cheats;
            internal float CheatStat;
            internal int KnownWorlds;
            internal List<string> Commands;
            internal List<long> Worlds;
            internal List<string> Plugins;
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
            var report = new Report
            {
                Worlds = new List<long>(),
                Commands = new List<string>(),
                Plugins = new List<string>(),
            };

            try
            {
                report.Readable = pkg.ReadBool();
                if (report.Readable)
                {
                    report.Format = pkg.ReadInt();

                    // A format this build cannot read is treated as no answer rather than
                    // guessed at. Core's version gate refuses a mismatched build long before
                    // this can happen; this is what says so if somebody is running without it.
                    if (report.Format != Facts.Format)
                    {
                        DyrrPlugin.Log.LogWarning("A client reported facts in format " +
                            report.Format + ", and this build speaks " + Facts.Format +
                            ". Treating it as unreported. Install Core to have builds checked " +
                            "before it gets this far.");

                        report.Readable = false;
                        Received[rpc] = report;
                        return;
                    }

                    report.Cheats = pkg.ReadBool();
                    report.CheatStat = pkg.ReadSingle();
                    report.KnownWorlds = pkg.ReadInt();

                    var commands = pkg.ReadInt();
                    for (var i = 0; i < commands; i++) report.Commands.Add(pkg.ReadString());

                    var worlds = pkg.ReadInt();
                    for (var i = 0; i < worlds; i++) report.Worlds.Add(pkg.ReadLong());

                    var plugins = pkg.ReadInt();
                    for (var i = 0; i < plugins; i++) report.Plugins.Add(pkg.ReadString());
                }
            }
            catch (Exception e)
            {
                // A package that runs out mid-read must not leave a half-filled report looking
                // like a clean character. Unreadable is the honest answer and the server's
                // own RefuseUnreported decides what that is worth.
                DyrrPlugin.Log.LogWarning("Could not read a client's report: " + e.Message);
                report.Readable = false;
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
            if (!DyrrConfig.Enabled.Value) return true;
            if (!__instance.IsServer()) return true;

            Report report;
            var heard = Received.TryGetValue(rpc, out report);

            if (heard) LogWhatTheyBrought(report);

            var why = Verdict(__instance, heard, report);
            Verdicts[rpc] = why;
            if (why == null) return true;

            if (!DyrrConfig.Enforce.Value)
            {
                DyrrPlugin.Log.LogWarning("Would have refused a connection: " + why);
                return true;
            }

            DyrrPlugin.Log.LogWarning("Refused a connection: " + why);
            RefusedCount++;

            // Tell them before dropping them. The reason travels to the client so it lands in
            // *their* log, because Valheim's refusal screen carries no text of its own and a
            // player on somebody else's server can never read the server's log. Being told
            // which rule you broke is the difference between a door and a mystery.
            // "It" plus the reason, because every reason is phrased as something the
            // connection did or is - "has played on 2 other world(s)", "is running 'x'". A
            // parenthesis after a fixed sentence read as an aside on that sentence, which is
            // how a mod refusal ended up presented as a travel refusal.
            rpc.Invoke(RpcRefused, DyrrConfig.RefusedMessage.Value + " It " + why + ".");
            rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorKicked);
            return false;
        }

        /// <summary>Null when the character may come in, otherwise the reason it may not.</summary>
        private static string Verdict(ZNet net, bool heard, Report report)
        {
            if (!heard)
            {
                // No answer at all means the far end has no Dyrr. Core's gate should have
                // turned that away already, so this is a backstop rather than the main line -
                // but a door that opens when the question goes unanswered is not a door.
                return DyrrConfig.RefuseUnreported.Value
                    ? "this client did not report its character"
                    : null;
            }

            if (!report.Readable)
                return DyrrConfig.RefuseUnreported.Value
                    ? "this character's profile could not be read"
                    : null;

            var reasons = new StringBuilder();

            if (DyrrConfig.RefuseOtherWorlds.Value)
            {
                var here = net.GetWorldUID();
                var others = 0;
                foreach (var uid in report.Worlds)
                    if (uid != here) others++;

                if (others > 0)
                    reasons.Append("has played on ").Append(others).Append(" other world(s)");
            }

            if (DyrrConfig.RefuseCheats.Value && report.Cheats)
                Also(reasons, "is flagged as having used cheats");

            // A different record of the same thing, and the one a mod that clears the flag is
            // least likely to have thought about: m_knownCommands gains every command's name a
            // few lines below the flag in Terminal.ConsoleCommand.RunAction, outside the branch
            // that sets it. Naming the command matters - "used cheats" is an accusation and
            // "has run 'spawn'" is a fact somebody can answer.
            if (DyrrConfig.RefuseCheatCommands.Value)
            {
                var ran = CheatCommands.Among(report.Commands);
                if (ran.Count > 0)
                    Also(reasons, "has run " + Listed(ran, "cheat command"));
            }

            // The records disagreeing is the one signal here that does not need the client to
            // be honest, only consistent. Both directions are chosen so the game itself cannot
            // trip them: the counter is only ever incremented on the same line that sets the
            // flag, and m_knownWorlds is written on save where m_worldData is written on spawn,
            // so a character can legitimately have more of the second than the first but never
            // the other way round.
            if (DyrrConfig.RefuseTampered.Value)
            {
                if (!report.Cheats && report.CheatStat > 0f)
                    Also(reasons, "says it has never cheated but carries a cheat count of " +
                        report.CheatStat + " - that record has been altered");

                if (report.KnownWorlds > report.Worlds.Count)
                    Also(reasons, "has played in " + report.KnownWorlds + " world(s) by its own " +
                        "history but admits to " + report.Worlds.Count +
                        " - its travel record has been altered");
            }

            if (DyrrConfig.RefuseMods.Value)
            {
                var mods = Mods.Judge(report.Plugins);
                if (mods != null) Also(reasons, mods);
            }

            return reasons.Length == 0 ? null : reasons.ToString();
        }

        /// <summary>
        /// Write down every plugin the client brought that this server does not itself run,
        /// whether or not it was refused for it.
        ///
        /// This is how AllowedMods and DeniedMods get filled in. Neither ships with anything in
        /// it and neither could: a list of cheat mod GUIDs written in advance is out of date
        /// the week after it ships and reads as complete when it is not. What an admin can
        /// actually work from is what turned up at their own door, which is this. It logs on an
        /// admitted client too, deliberately - the useful entry is usually somebody's map mod,
        /// found before they are ever turned away for it.
        /// </summary>
        private static void LogWhatTheyBrought(Report report)
        {
            if (report.Plugins == null || report.Plugins.Count == 0) return;

            var own = Mods.Own();
            var extra = new List<string>();

            foreach (var guid in report.Plugins)
                if (guid != null && !own.Contains(guid.Trim().ToLowerInvariant())) extra.Add(guid);

            if (extra.Count == 0) return;

            DyrrPlugin.Log.LogInfo("A client brought " + extra.Count +
                " plugin(s) this server does not run: " + string.Join(", ", extra.ToArray()));
        }

        private static void Also(StringBuilder reasons, string reason)
        {
            if (reasons.Length > 0) reasons.Append(", and ");
            reasons.Append(reason);
        }

        /// <summary>"cheat command 'god'", or "3 cheat commands: 'god', 'fly', 'spawn'".</summary>
        private static string Listed(List<string> items, string noun)
        {
            if (items.Count == 1) return noun + " '" + items[0] + "'";

            var shown = Math.Min(items.Count, 3);
            var text = items.Count + " " + noun + "s: ";

            for (var i = 0; i < shown; i++)
            {
                if (i > 0) text += ", ";
                text += "'" + items[i] + "'";
            }

            if (items.Count > shown) text += " and " + (items.Count - shown) + " more";

            return text;
        }

        /// <summary>
        /// Client side: say out loud why the door shut - on the screen, not only in a log.
        ///
        /// The log alone was not enough, which is the whole lesson of the mod this policy came
        /// out of. A refused player saw "Kicked from server" and had to be told where to look;
        /// Core carries the reason through to the refusal screen itself.
        ///
        /// Without Core the log is all there is, and that is a real regression rather than a
        /// tidy fallback - it is precisely the failure the split from Rist was meant to fix.
        /// It is still better than refusing to run: a server owner who wants the policy and
        /// not the suite gets a working door, and the log line is unchanged. Installing Core
        /// on the client is what puts the reason back on the screen.
        /// </summary>
        private static void OnRefused(ZRpc rpc, string why)
        {
            DyrrPlugin.Log.LogError("This server refused this connection: " + why);

            LastRefusal = why;
            DyrrPlugin.Explain(why);
        }

        /// <summary>A connection that is gone cannot be asked about again.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), "Disconnect")]
        private static void Forget(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;

            Received.Remove(peer.m_rpc);
            Verdicts.Remove(peer.m_rpc);
        }
    }
}
