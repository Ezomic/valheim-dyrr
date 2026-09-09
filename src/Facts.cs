using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace Dyrr
{
    /// <summary>
    /// What the joining client says about its own character, and about itself.
    ///
    /// The honest limit, stated once here rather than implied: PlayerProfile lives on the
    /// client, so everything below is self-reported and a purpose-built client can lie about
    /// it. What it does catch is the ordinary case - an unmodified player bringing a character
    /// that levelled somewhere else, or one that has used devcommands - because the game
    /// records both itself and has no reason to misreport them.
    ///
    /// That is worth being clear-eyed about: this is a house rule with a lock on the door, not
    /// a security boundary. It keeps honest people honest, which is what a door policy is for.
    ///
    /// **Why there is more than one record of the same thing.** A lie has to be consistent, and
    /// the game keeps the same facts in several places written at different moments by
    /// different code. m_usedCheats is a bool set in Terminal.ConsoleCommand.RunAction;
    /// PlayerStatType.Cheats is a counter incremented on the very next line; m_knownCommands
    /// gains the command's name a few lines further down, outside the cheat branch, for every
    /// command whatever it was; and m_knownWorlds records worlds by *name* in SavePlayerToDisk,
    /// where m_worldData records them by uid on spawn. Clearing one is easy. Clearing all four
    /// so they still agree is a different piece of work, and a mod written to switch
    /// devcommands on is not going to have done it. **Disagreement between the records is
    /// itself the signal**, and it needs no honesty from the client - only consistency.
    ///
    /// The plugin list is reported for the same reason and with the same limit. It matters more
    /// than it looks: Console.IsCheatsEnabled returns ZNet.instance.IsServer(), so on a
    /// dedicated server a client's own devcommands is inert in vanilla and anybody cheating
    /// there is necessarily running something that patched around it. The useful question on a
    /// server is therefore not "did this character use cheats" but "what is this client
    /// running", and this is the only place that can be asked.
    /// </summary>
    internal static class Facts
    {
        /// <summary>
        /// The shape of the package below. Sent so a mismatch is a clear log line rather than
        /// a garbled read - Core's version gate should have refused the connection long before
        /// this could happen, and this is what says so if it did not.
        /// </summary>
        // 3 as of 2026-08-23: the character's own name rides along, so a refusal can say who
        // was turned away instead of only why. A server log line nobody can act on is a log
        // line nobody reads, and this one is forwarded to Discord where "somebody" is useless.
        internal const int Format = 3;

        private static System.Reflection.FieldInfo _worldData;

        /// <summary>
        /// Everything this client is willing to say about itself.
        ///
        /// The raw lists are sent rather than any conclusion drawn from them, because at the
        /// moment this is gathered the client does not reliably know which world it is joining
        /// - the world UID arrives from the server later in the handshake - and because the
        /// arithmetic is the server's business anyway. Rist's version asked the client to do
        /// the subtraction and had to run after spawn to manage it. Sending the facts lets the
        /// server, which certainly knows its own UID and its own policy, work it out, and lets
        /// the whole exchange happen before the player is ever admitted.
        /// </summary>
        internal static ZPackage Gather()
        {
            var pkg = new ZPackage();

            var uids = new List<long>();
            var commands = new List<string>();
            var cheats = false;
            var cheatStat = 0f;
            var knownWorlds = 0;
            var name = "";

            // See CheatStat below for why that is a method rather than one dictionary lookup.
            try
            {
                var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
                if (profile != null)
                {
                    // The character's name, not the Steam persona. It is the name the other
                    // players know, and it is the one the door policy is actually about: this
                    // mod refuses characters, not people.
                    name = profile.GetName() ?? "";

                    cheats = profile.m_usedCheats;
                    cheatStat = CheatStat(profile);
                    knownWorlds = profile.m_knownWorlds.Count;

                    foreach (var command in profile.m_knownCommands) commands.Add(command.Key);

                    uids = WorldsOf(profile);
                }
            }
            catch (Exception e)
            {
                // A gather that throws must not read as a clean character. It is reported as
                // unreadable and the server decides what to do with that.
                DyrrPlugin.Log.LogWarning("Could not read this character's profile: " + e.Message);
                pkg.Write(false);
                return pkg;
            }

            pkg.Write(true);
            pkg.Write(Format);

            // First after the version, deliberately. Everything below can throw on a read and
            // leave the report unreadable, and a report that failed halfway is exactly the one
            // worth naming - so the name is the field that survives.
            pkg.Write(name ?? "");

            pkg.Write(cheats);
            pkg.Write(cheatStat);
            pkg.Write(knownWorlds);

            pkg.Write(commands.Count);
            foreach (var command in commands) pkg.Write(command);

            pkg.Write(uids.Count);
            foreach (var uid in uids) pkg.Write(uid);

            var plugins = Plugins();
            pkg.Write(plugins.Count);
            foreach (var guid in plugins) pkg.Write(guid);

            return pkg;
        }

        /// <summary>
        /// Every BepInEx plugin loaded in this process, by GUID.
        ///
        /// Chainloader is what BepInEx itself judges by, so this is the same list the game's
        /// own logs show at startup and there is nothing clever about reading it. It is
        /// deliberately GUIDs only: the server needs to recognise a mod, not to be handed a
        /// tour of somebody's machine.
        /// </summary>
        /// <summary>
        /// The Cheats counter, found by NAME rather than by the ordinal the compiler baked in.
        ///
        /// m_stats is a Dictionary&lt;PlayerStatType, float&gt; and C# compiles an enum member to
        /// its constant, so `m_stats[PlayerStatType.Cheats]` is the literal 4 in the shipped
        /// DLL. Insert a member above it - and an achievements system is exactly the thing that
        /// reshuffles a player-stat enum - and this reads a different counter entirely, with
        /// nothing to notice.
        ///
        /// That is worse here than the usual silent-wrong-number, because of what the value
        /// feeds: Doorman treats "says it never cheated but carries a cheat count" as a tamper
        /// signal and refuses the connection when RefuseTampered is on. A stat that is really
        /// somebody's death count or crafting total is almost always above zero, so a
        /// renumbering does not degrade the door, it slams it on everyone.
        ///
        /// Resolved by name, once, and cached. -1 when the member cannot be found at all, which
        /// every consumer already treats as "no signal" because the test is `&gt; 0f` - so a
        /// rename costs the corroboration and never invents a refusal. m_usedCheats, the actual
        /// flag this corroborates, is a plain bool field and is unaffected.
        /// </summary>
        private static bool _cheatStatResolved;
        private static PlayerStatType _cheatStatType;
        private static bool _cheatStatKnown;

        private static float CheatStat(PlayerProfile profile)
        {
            if (!_cheatStatResolved)
            {
                _cheatStatResolved = true;
                try
                {
                    _cheatStatType = (PlayerStatType)Enum.Parse(typeof(PlayerStatType), "Cheats");
                    _cheatStatKnown = true;
                }
                catch (Exception e)
                {
                    DyrrPlugin.Log.LogWarning("This game build has no PlayerStatType.Cheats, so "
                        + "Dyrr cannot corroborate the cheat flag against its counter. The flag "
                        + "itself still works; only the altered-record check loses this half. "
                        + e.Message);
                }
            }

            if (!_cheatStatKnown) return -1f;

            float value;
            return profile.m_playerStats.m_stats.TryGetValue(_cheatStatType, out value) ? value : 0f;
        }

        private static List<string> Plugins()
        {
            var guids = new List<string>();

            try
            {
                foreach (var plugin in Chainloader.PluginInfos) guids.Add(plugin.Key);
            }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not read the plugin list: " + e.Message);
            }

            return guids;
        }

        /// <summary>
        /// PlayerProfile.m_worldData is a Dictionary&lt;long, WorldPlayerData&gt; keyed by world
        /// UID, one entry per world this character has spawned in. Private, hence reflection,
        /// and it is the most direct answer the game holds to "where has this character been".
        ///
        /// Worth knowing before turning this on: entries are never removed. One visit anywhere
        /// else is permanent for that character file.
        ///
        /// Also read from the menu, where it is the answer rather than the evidence: a
        /// character with exactly one world in here has a home whether or not this mod has ever
        /// watched it play. SaveSystem.GetAllPlayerProfiles calls PlayerProfile.Load, which
        /// parses this dictionary in full, so the menu's profile objects carry it already.
        /// </summary>
        internal static List<long> WorldsOf(PlayerProfile profile)
        {
            var into = new List<long>();
            if (profile == null) return into;

            if (_worldData == null)
                _worldData = AccessTools.Field(typeof(PlayerProfile), "m_worldData");

            if (_worldData == null)
            {
                DyrrPlugin.Log.LogError(
                    "PlayerProfile.m_worldData not found - this character's travel cannot be seen.");
                return into;
            }

            if (!(_worldData.GetValue(profile) is IDictionary map)) return into;

            foreach (DictionaryEntry entry in map)
                if (entry.Key is long uid) into.Add(uid);

            return into;
        }
    }
}
