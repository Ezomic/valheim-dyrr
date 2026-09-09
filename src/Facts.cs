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
        // 4 as of 2026-09-10: the travel record now says whether it could be READ, which is a
        // different question from whether it is empty. Without that the server could not tell
        // "this character has been nowhere" from "this build cannot see where it has been", and
        // read the second as the first - so an unreadable m_worldData made KnownWorlds >
        // Worlds.Count true for every player alive, and RefuseTampered refused all of them
        // while publishing an accusation that their travel record had been altered. See
        // WorldsOf below for the paths that produced the empty list, and Doorman's Verdict for
        // the comparison that now waits on this flag.
        //
        // 3 as of 2026-08-23: the character's own name rides along, so a refusal can say who
        // was turned away instead of only why. A server log line nobody can act on is a log
        // line nobody reads, and this one is forwarded to Discord where "somebody" is useless.
        internal const int Format = 4;

        private static System.Reflection.FieldInfo _worldData;

        /// <summary>
        /// Whether the travel record has already explained itself in this process.
        ///
        /// The shape of PlayerProfile.m_worldData is a property of the game build, not of the
        /// moment - so once it has failed to read it will fail identically on every connection,
        /// and Gather runs once per connection. One fully detailed line naming the runtime type
        /// is greppable and survives in the log; the same line a hundred times is what an admin
        /// scrolls past. The server says separately, per refused check, that it is running
        /// blind - see Doorman - so silence here never means the consequence went unreported.
        /// </summary>
        private static bool _worldsExplained;

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
            var worldsRead = false;
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

                    // m_usedCheats is still a field on the profile; everything else moved into
                    // the stats array in 1.0. See Totals.
                    cheats = profile.m_usedCheats;
                    cheatStat = CheatStat(profile);

                    var totals = Totals(profile);
                    if (totals != null)
                    {
                        if (totals.m_knownWorlds != null) knownWorlds = totals.m_knownWorlds.Count;

                        if (totals.m_knownCommands != null)
                            foreach (var command in totals.m_knownCommands) commands.Add(command.Key);
                    }

                    // The flag matters as much as the list. An empty list is a claim about
                    // this character; an unreadable one is a fact about this build, and the
                    // server must not judge the character for it.
                    uids = WorldsOf(profile, out worldsRead);
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

            // Written immediately before the list it qualifies, because it is a header on that
            // list rather than a fact about the character: false means "do not read anything
            // into the count that follows", including the zero.
            pkg.Write(worldsRead);
            pkg.Write(uids.Count);
            foreach (var uid in uids) pkg.Write(uid);

            var plugins = Plugins();
            pkg.Write(plugins.Count);
            foreach (var guid in plugins) pkg.Write(guid);

            return pkg;
        }

        /// <summary>
        /// The character's lifetime record, which Valheim 1.0 moved.
        ///
        /// PlayerProfile used to hold one PlayerStats, with m_knownWorlds and m_knownCommands as
        /// fields beside it. 1.0 made it an array of ten and moved both dictionaries inside, as
        /// part of the achievements system. The indices are not interchangeable:
        ///
        ///   [0]  every increment, unconditionally - IncrementStat writes here first
        ///   [1]  only when Achievements.CanGetAchievements(cheated) is true
        ///   [2..] one per achievement difficulty, via GetCurrentAchievementDifficultyIndex()
        ///
        /// [0] is the one this mod wants and the only one that preserves what it used to read.
        /// Dyrr asks "has this character ever", and the totals bucket is that question - the
        /// others answer "did it count toward an achievement", which is a different one.
        ///
        /// Returns null rather than throwing on a shape it does not recognise, and every caller
        /// treats null as "unreadable" the way the surrounding gather already does.
        /// </summary>
        internal static PlayerProfile.PlayerStats Totals(PlayerProfile profile)
        {
            try
            {
                if (profile == null) return null;

                var stats = profile.m_playerStats;
                if (stats == null || stats.Length == 0) return null;

                return stats[0];
            }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not reach this character's stat record: "
                    + e.Message);
                return null;
            }
        }

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

            var totals = Totals(profile);
            if (totals == null || totals.m_stats == null) return -1f;

            float value;
            return totals.m_stats.TryGetValue(_cheatStatType, out value) ? value : 0f;
        }

        /// <summary>
        /// Every BepInEx plugin loaded in this process, by GUID.
        ///
        /// Chainloader is what BepInEx itself judges by, so this is the same list the game's
        /// own logs show at startup and there is nothing clever about reading it. It is
        /// deliberately GUIDs only: the server needs to recognise a mod, not to be handed a
        /// tour of somebody's machine.
        /// </summary>
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
            bool read;
            return WorldsOf(profile, out read);
        }

        /// <summary>
        /// The same list, plus the only thing that made the difference between a door and a
        /// wall: whether the dictionary was actually read.
        ///
        /// Every failure below used to return an empty list and say nothing, and an empty list
        /// is indistinguishable from a character that has genuinely been nowhere. The server
        /// compares KnownWorlds against this count to catch a wiped travel record, so an
        /// unreadable m_worldData did not degrade that check, it inverted it: every player who
        /// had ever saved anywhere had KnownWorlds above zero, tripped the comparison, and was
        /// refused with a public accusation that they had altered their own save. One field
        /// rename in a game update was enough to do that to a whole server, and the log would
        /// have carried no line saying why.
        ///
        /// So `read` is true only when the dictionary was found, was a dictionary, and every
        /// entry in it converted. A partial read is reported as no read, because a list that is
        /// short by two worlds trips the same comparison as a list that is short by all of them.
        /// </summary>
        internal static List<long> WorldsOf(PlayerProfile profile, out bool read)
        {
            read = false;

            var into = new List<long>();
            if (profile == null) return into;

            // Bound lazily and never in a static initialiser: a throwing type initialiser
            // poisons every Harmony patch the type carries, and this one is reached from a
            // patched RPC on both ends of a connection.
            if (_worldData == null)
                _worldData = AccessTools.Field(typeof(PlayerProfile), "m_worldData");

            if (_worldData == null)
            {
                Explain("PlayerProfile.m_worldData not found - this character's travel cannot "
                    + "be seen, so Dyrr will not judge anyone's travel record on this build.");
                return into;
            }

            object value;
            try
            {
                value = _worldData.GetValue(profile);
            }
            catch (Exception e)
            {
                Explain("PlayerProfile.m_worldData could not be read (" + e.Message
                    + "), so Dyrr will not judge anyone's travel record on this build.");
                return into;
            }

            // Not `is IDictionary` in one line any more. The type that turned up is the single
            // most useful thing in the log when this fires, because it says whether the field
            // moved, changed container or went generic-only - and the old form threw it away.
            var map = value as IDictionary;
            if (map == null)
            {
                Explain("PlayerProfile.m_worldData is "
                    + (value == null ? "null" : "a " + value.GetType().FullName)
                    + " rather than a dictionary this build can enumerate, so Dyrr will not "
                    + "judge anyone's travel record on this build.");
                return into;
            }

            var entries = 0;
            string oddKey = null;

            // Caught here rather than left to Gather's outer catch, and that is the difference
            // between losing one field and losing the connection: the outer catch marks the
            // WHOLE report unreadable, and RefuseUnreported - on by default - then turns the
            // player away for it. A travel record that cannot be enumerated must cost the
            // travel rules and nothing else.
            try
            {
                foreach (DictionaryEntry entry in map)
                {
                    entries++;

                    // Plain type test, not a cast: a key type that changed must be named, not
                    // thrown. DictionaryEntry.Key is a boxed object and nothing here derives
                    // from UnityEngine.Object, so a null compare is honest.
                    if (entry.Key is long uid) into.Add(uid);
                    else if (oddKey == null)
                        oddKey = entry.Key == null ? "null" : entry.Key.GetType().FullName;
                }
            }
            catch (Exception e)
            {
                Explain("PlayerProfile.m_worldData is a " + map.GetType().FullName
                    + " that could not be walked entry by entry (" + e.Message
                    + "), so Dyrr will not judge anyone's travel record on this build.");
                return into;
            }

            if (into.Count != entries)
            {
                Explain("PlayerProfile.m_worldData holds " + entries + " entr(ies) keyed by "
                    + oddKey + " rather than long, so only " + into.Count + " of them could be "
                    + "read. Dyrr will not judge anyone's travel record on this build.");
                return into;
            }

            read = true;
            return into;
        }

        /// <summary>
        /// Say once, loudly, that the travel record cannot be read - and say what was found
        /// instead, which is the half that makes the line worth having.
        ///
        /// LogError rather than LogWarning: this is a check silently switching itself off, and
        /// the whole reason this mod exists in this shape is that "applied cleanly and did
        /// nothing" must never be quiet. See _worldsExplained for why it is said once.
        /// </summary>
        private static void Explain(string what)
        {
            if (_worldsExplained) return;
            _worldsExplained = true;

            DyrrPlugin.Log.LogError("Dyrr travel record unreadable: " + what);
        }
    }
}
