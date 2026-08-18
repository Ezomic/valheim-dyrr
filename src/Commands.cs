using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;

namespace Dyrr
{
    /// <summary>
    /// Ask the door what it is doing, and unbind a character.
    ///
    /// Both of these exist because the mod already had the answers and no way to be asked.
    ///
    /// **The report** is the point of Enforce being off. Off is not a disabled state, it is the
    /// state an admin is meant to sit in while deciding: the door judges every connection and
    /// says what it would have done. But it said so one line at a time into a log, at the
    /// moment each player connected, which means the question actually being asked - "if I turn
    /// this on, who stops being able to play?" - could only be answered by reading back through
    /// a log for lines that scrolled past while nobody was watching. This prints the standing
    /// answer for everyone currently connected, plus which checks are live.
    ///
    /// **Forget** is the documented way out of a wrong binding, and until now the only way to
    /// do it was to edit a text file - which the mod then overwrote from memory on the next
    /// bind. That is fixed in Home; this is the version that does not require finding the file
    /// at all.
    ///
    /// Not a cheat command, and not admin-gated, because neither reads or changes anything a
    /// player could not already see or edit: the report is this server's own state to whoever
    /// is at its console, and the bindings are this machine's own file.
    /// </summary>
    internal static class Commands
    {
        private static bool _registered;

        /// <summary>
        /// Terminal.InitTerminal builds the command table once, from Terminal.Awake, and both
        /// Chat and Console derive from it - so this fires whichever comes up first, and the
        /// guard is because it is called on every subsequent Awake as well.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static void Register()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand("dyrr",
                "Dyrr: what the door is doing. 'dyrr home' lists this machine's character "
                + "bindings, 'dyrr forget <id>' unbinds one.",
                Run);
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            var what = args.Length > 1 ? args[1].ToLowerInvariant() : "";

            switch (what)
            {
                case "":
                case "status":
                    Say(args, Status());
                    return;

                case "home":
                    Say(args, Bindings());
                    return;

                case "forget":
                    Say(args, ForgetOne(args));
                    return;

                default:
                    Say(args, "dyrr - what the door is doing here\n"
                        + "dyrr home - which world each character on this machine belongs to\n"
                        + "dyrr forget <id> - unbind a character so it may be taken anywhere");
                    return;
            }
        }

        /// <summary>
        /// The console is where this was asked and the log is where it will be read back from.
        ///
        /// A dedicated server's console scrolls and is not kept; BepInEx's log is the file an
        /// admin still has an hour later, and the whole point of the report is to be looked at
        /// again while deciding whether to enforce. The client has the console in front of it
        /// and does not need the duplicate.
        /// </summary>
        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args.Context != null) args.Context.AddString(text);

            if (ZNet.instance != null && ZNet.instance.IsServer()) DyrrPlugin.Log.LogInfo(text);
        }

        private static string Status()
        {
            var text = new StringBuilder();

            text.Append("Dyrr ").Append(DyrrPlugin.PluginVersion);
            if (!DyrrPlugin.CorePresent) text.Append(" (standalone - no Core)");
            text.Append('\n');

            if (ZNet.instance == null)
            {
                text.Append("Not in a world. The door only has anything to say on a server.");
                return text.ToString();
            }

            if (!ZNet.instance.IsServer())
            {
                // A client has no verdicts to report - it is judged, it does not judge. What it
                // does know is whether it was ever told why, which is the only part of the
                // door a player has standing to ask about.
                text.Append("This is a client, so the door is the server's. ");
                text.Append(Doorman.LastRefusal == null
                    ? "Nothing has been refused to this machine this session."
                    : "Last refusal: " + Doorman.LastRefusal);
                text.Append("\n\n").Append(Bindings());
                return text.ToString();
            }

            text.Append("World: '").Append(ZNet.instance.GetWorldName())
                .Append("' (").Append(ZNet.instance.GetWorldUID()).Append(")\n");

            if (!DyrrConfig.Enabled.Value)
            {
                text.Append("Enabled is off - the door is not judging anything.");
                return text.ToString();
            }

            text.Append(DyrrConfig.Enforce.Value
                    ? "Enforce is ON - a character that fails a check does not come in.\n"
                    : "Enforce is OFF - failures are reported here and refused to nobody.\n")
                .Append("Checks: other worlds ").Append(On(DyrrConfig.RefuseOtherWorlds.Value))
                .Append(", cheats ").Append(On(DyrrConfig.RefuseCheats.Value))
                .Append(", unreported ").Append(On(DyrrConfig.RefuseUnreported.Value))
                .Append('\n');

            text.Append("Refused so far this session: ").Append(Doorman.RefusedCount).Append("\n\n");

            List<ZNetPeer> peers = ZNet.instance.GetPeers();
            var judged = 0;

            if (peers != null)
                foreach (var peer in peers)
                {
                    if (peer == null || peer.m_rpc == null) continue;
                    if (!Doorman.TryVerdict(peer.m_rpc, out var why)) continue;

                    judged++;

                    var name = string.IsNullOrEmpty(peer.m_playerName) ? "(unnamed)" : peer.m_playerName;
                    text.Append("  ").Append(name).Append("  ");

                    if (why == null) text.Append("admitted");
                    else text.Append(DyrrConfig.Enforce.Value ? "refused: " : "would refuse: ").Append(why);

                    text.Append('\n');
                }

            // A connection judged before this build started, or one the door skipped entirely,
            // has no verdict - so "nobody" and "nobody judged" are different answers and worth
            // distinguishing. With Enforce off, everyone still connected has been judged.
            if (judged == 0) text.Append("  nobody connected has been judged by this door");

            return text.ToString();
        }

        private static string Bindings()
        {
            var text = new StringBuilder("Character bindings on this machine:\n");
            var any = false;

            foreach (var kv in Home.All())
            {
                any = true;

                var who = string.IsNullOrEmpty(kv.Value.Character) ? "(unnamed)" : kv.Value.Character;
                text.Append("  ").Append(who).Append("  (id ").Append(kv.Key).Append(")  ->  ")
                    .Append(Home.Describe(kv.Value)).Append('\n');
            }

            if (!any) text.Append("  none yet - a character is bound the first time it plays in a world");

            return text.ToString();
        }

        private static string ForgetOne(Terminal.ConsoleEventArgs args)
        {
            // Character ids come from Utils.GenerateUID() and are perfectly free to be
            // negative, so this parses a signed long rather than a count.
            if (args.Length < 3 ||
                !long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return "dyrr forget <id> - the id is the number in brackets in 'dyrr home'.";

            var binding = Home.GetBinding(id);
            if (binding.World == 0L) return "No character with id " + id + " is bound to anything.";

            if (!Home.Forget(id)) return "Could not unbind " + id + ".";

            DyrrPlugin.Log.LogWarning("Unbound character " + id + " from world " +
                Home.Describe(binding) + " on request.");

            return "Unbound " + id + " from world " + Home.Describe(binding) + ".\n"
                + "It may now be taken into any world - and the first one it plays in becomes "
                + "its new home. Nothing about where it has already been is undone by this; "
                + "the game's own record of that is permanent and no mod can clear it.";
        }

        private static string On(bool value)
        {
            return value ? "on" : "off";
        }
    }
}
