using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace Threshold
{
    /// <summary>
    /// What the joining client says about its own character.
    ///
    /// The honest limit, stated once here rather than implied: PlayerProfile lives on the
    /// client, so everything below is self-reported and a purpose-built client can lie about
    /// it. What it does catch is the ordinary case - an unmodified player bringing a character
    /// that levelled somewhere else, or one that has used devcommands - because the game
    /// records both itself and has no reason to misreport them.
    ///
    /// That is worth being clear-eyed about: this is a house rule with a lock on the door, not
    /// a security boundary. It keeps honest people honest, which is what a door policy is for.
    /// </summary>
    internal static class Facts
    {
        private static FieldInfoCache _worldData;

        private sealed class FieldInfoCache
        {
            internal System.Reflection.FieldInfo Field;
        }

        /// <summary>
        /// Every world UID this character has ever spawned in, plus the cheats flag.
        ///
        /// The raw list is sent rather than a count of "other" worlds, because at the moment
        /// this is gathered the client does not reliably know which world it is joining - the
        /// world UID arrives from the server later in the handshake. Boon's version asked the
        /// client to do the subtraction and had to run after spawn to manage it. Sending the
        /// list lets the server, which certainly knows its own UID, do the arithmetic itself,
        /// and lets the whole exchange happen before the player is ever admitted.
        /// </summary>
        internal static ZPackage Gather()
        {
            var pkg = new ZPackage();

            var uids = new List<long>();
            var cheats = false;

            try
            {
                var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
                if (profile != null)
                {
                    cheats = profile.m_usedCheats;
                    CollectWorlds(profile, uids);
                }
            }
            catch (Exception e)
            {
                // A gather that throws must not read as a clean character. It is reported as
                // unreadable and the server decides what to do with that.
                ThresholdPlugin.Log.LogWarning("Could not read this character's profile: " + e.Message);
                pkg.Write(false);
                return pkg;
            }

            pkg.Write(true);
            pkg.Write(cheats);
            pkg.Write(uids.Count);
            foreach (var uid in uids) pkg.Write(uid);

            return pkg;
        }

        /// <summary>
        /// PlayerProfile.m_worldData is a Dictionary&lt;long, WorldPlayerData&gt; keyed by world
        /// UID, one entry per world this character has spawned in. Private, hence reflection,
        /// and it is the most direct answer the game holds to "where has this character been".
        ///
        /// Worth knowing before turning this on: entries are never removed. One visit anywhere
        /// else is permanent for that character file.
        /// </summary>
        private static void CollectWorlds(PlayerProfile profile, List<long> into)
        {
            if (_worldData == null)
                _worldData = new FieldInfoCache { Field = AccessTools.Field(typeof(PlayerProfile), "m_worldData") };

            if (_worldData.Field == null)
            {
                ThresholdPlugin.Log.LogError(
                    "PlayerProfile.m_worldData not found - this character's travel cannot be seen.");
                return;
            }

            if (!(_worldData.Field.GetValue(profile) is IDictionary map)) return;

            foreach (DictionaryEntry entry in map)
                if (entry.Key is long uid) into.Add(uid);
        }
    }
}
