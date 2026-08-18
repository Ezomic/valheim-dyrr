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
            if (_table == null)
                _table = AccessTools.Field(typeof(Terminal.ConsoleCommand), "commands");

            if (_table == null) return Fallback("Terminal.ConsoleCommand.commands not found");

            IDictionary map;
            try { map = _table.GetValue(null) as IDictionary; }
            catch (Exception e) { return Fallback("could not read the command table (" + e.Message + ")"); }

            if (map == null || map.Count == 0)
                return Fallback("no console has been created in this process yet");

            var names = new HashSet<string>();

            foreach (DictionaryEntry entry in map)
            {
                if (!(entry.Value is Terminal.ConsoleCommand command)) continue;
                if (!command.IsCheat) continue;

                var name = entry.Key as string;
                if (!string.IsNullOrEmpty(name)) names.Add(name.ToLowerInvariant());
            }

            return names.Count == 0 ? Fallback("the command table holds no cheat commands") : names;
        }

        /// <summary>Say once why the shipped list is being used, then stop saying it.</summary>
        private static HashSet<string> Fallback(string why)
        {
            if (!_warned)
            {
                _warned = true;
                DyrrPlugin.Log.LogInfo("Classifying cheat commands from the list built into " +
                    "Dyrr rather than from the game's own table, because " + why +
                    ". This is the expected case on a dedicated server.");
            }

            return null;
        }
    }
}
