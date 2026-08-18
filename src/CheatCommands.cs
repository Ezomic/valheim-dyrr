using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace Dyrr
{
    /// <summary>
    /// Which console commands count as cheating.
    ///
    /// Deliberately answered on the **server**, from the names the client reported, rather than
    /// asked of the client. The client is being judged; letting it also decide what counts
    /// would leave the rule in the hands of the thing the rule is about. It also means a
    /// cheat command added by some other mod on the server is classified correctly for free,
    /// without this having to know it exists.
    ///
    /// The live table is Terminal.ConsoleCommand's own static dictionary, which every command
    /// registers itself into, carrying the IsCheat flag the game already uses to gate them.
    /// That is the real answer and it is used whenever it is available.
    ///
    /// The fallback list below is the same answer, ripped out of the game rather than typed
    /// from memory: every command in Terminal.InitTerminal constructed with isCheat: true, as
    /// of the build this was written against. It exists because the table is built from
    /// Terminal.Awake, and a dedicated server has no guarantee of having awoken one by the time
    /// the first player knocks - and a check that silently classifies nothing is worse than no
    /// check, because it reads as "clean".
    /// </summary>
    internal static class CheatCommands
    {
        /// <summary>
        /// Snapshot, not gospel. The live table wins wherever it exists; this is what is used
        /// before it does. Being a version behind here costs a command or two, never a false
        /// accusation, because a name that has stopped being a cheat is simply one the live
        /// table would have cleared.
        /// </summary>
        private static readonly HashSet<string> Ripped = new HashSet<string>
        {
            "addstatus", "adrenaline", "aggravate", "beard", "catch", "clearstatus", "cr",
            "debugmode", "dpsdebug", "env", "event", "exploremap", "ffsmooth", "find", "findtp",
            "fly", "forcedelete", "freefly", "gc", "ghost", "god", "goto", "hair", "heal",
            "itemset", "killall", "killenemies", "killenemycreatures", "killtame", "listkeys",
            "location", "model", "nextseed", "nocost", "nospawn", "players", "pos",
            "printcreatures", "printlocations", "printnetobj", "puke", "raiseskill",
            "randomevent", "recall", "removebirds", "removedrops", "removefish", "removekey",
            "removekeyplayer", "resetcharacter", "resetenv", "resetkeys", "resetmap",
            "resetskill", "resetwind", "respawntime", "setfuel", "setkey", "setkeyplayer",
            "setpower", "skiptime", "sleep", "stopevent", "stopfire", "stopsmoke", "tame",
            "test", "time", "timescale", "tod", "tombstone", "vegetation", "wind",
        };

        private static System.Reflection.FieldInfo _table;
        private static bool _warned;

        /// <summary>
        /// The cheat commands among the ones reported, in the order they were reported.
        ///
        /// Names arrive lowercased because that is how the game stores them - m_knownCommands
        /// is keyed on args[0].ToLower() - but they are lowercased again here rather than
        /// trusted, since they came off the wire.
        /// </summary>
        internal static List<string> Among(IEnumerable<string> reported)
        {
            var found = new List<string>();
            if (reported == null) return found;

            var live = Live();

            foreach (var raw in reported)
            {
                if (string.IsNullOrEmpty(raw)) continue;

                var name = raw.Trim().ToLowerInvariant();

                var cheat = live != null ? live.Contains(name) : Ripped.Contains(name);
                if (cheat && !found.Contains(name)) found.Add(name);
            }

            return found;
        }

        /// <summary>
        /// The game's own table, or null when it has not been built yet. Not cached as a set,
        /// because a mod registering a command later would leave a cached copy quietly wrong,
        /// and this runs once per connection rather than once per frame.
        /// </summary>
        private static HashSet<string> Live()
        {
            // On Terminal, not on Terminal.ConsoleCommand. The dictionary is written from the
            // ConsoleCommand constructor - "commands[command.ToLower()] = this" - which reads
            // like a member of that class and is not: a nested class reaches its outer class's
            // statics by plain name. AccessTools.Field walks base types, never outer ones, so
            // asking the nested type found nothing and this quietly ran on the fallback list
            // for every connection, while reporting the dedicated server as the reason.
            if (_table == null) _table = AccessTools.Field(typeof(Terminal), "commands");

            if (_table == null)
                return Fallback("Terminal.commands was not found at all, which means the game "
                    + "has changed - only cheat commands Dyrr already knew about are seen", true);

            IDictionary map;
            try { map = _table.GetValue(null) as IDictionary; }
            catch (Exception e)
            {
                return Fallback("the command table could not be read (" + e.Message + ")", true);
            }

            if (map == null || map.Count == 0)
                return Fallback("no console has been created in this process yet, which is "
                    + "ordinary on a dedicated server", false);

            var names = new HashSet<string>();

            foreach (DictionaryEntry entry in map)
            {
                if (!(entry.Value is Terminal.ConsoleCommand command)) continue;
                if (!command.IsCheat) continue;

                var name = entry.Key as string;
                if (!string.IsNullOrEmpty(name)) names.Add(name.ToLowerInvariant());
            }

            return names.Count == 0
                ? Fallback("the command table holds no cheat commands", true)
                : names;
        }

        /// <summary>
        /// Say once why the shipped list is being used, then stop saying it.
        ///
        /// Loud or quiet depending on whether it is a situation or a fault. A dedicated server
        /// that has never built a console is working exactly as expected and does not deserve a
        /// warning every time it starts. A table that cannot be found or read means the game
        /// moved and the list is frozen at whatever shipped, which is worth being told about -
        /// the first version logged both at Info and the real one hid in plain sight for a
        /// whole session, reported as the expected case.
        /// </summary>
        private static HashSet<string> Fallback(string why, bool wrong)
        {
            if (!_warned)
            {
                _warned = true;

                var said = "Classifying cheat commands from the list built into Dyrr rather "
                    + "than from the game's own table, because " + why + ".";

                if (wrong) DyrrPlugin.Log.LogWarning(said);
                else DyrrPlugin.Log.LogInfo(said);
            }

            return null;
        }
    }
}
