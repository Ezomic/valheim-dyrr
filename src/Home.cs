using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace Threshold
{
    /// <summary>
    /// Which world each character on this machine belongs to.
    ///
    /// This is **protection, not enforcement**, and that distinction is why it can live on the
    /// client at all. Nothing the client holds can be trusted for a decision that turns people
    /// away - that is what the server side of this mod is for. Editing this file only lets you
    /// damage your own character, so a plain text file you can open and correct is exactly
    /// right.
    ///
    /// What it protects against: taking a character into a different world "just to look".
    /// That writes an entry into PlayerProfile.m_worldData, nothing ever removes it, and the
    /// door then refuses that character on its own server forever. Threshold is the only thing
    /// that will ever refuse it, so Threshold is the right place to stop it happening.
    ///
    /// Keyed on the profile's **player id**, not its filename. The filename is the character's
    /// name, which is reused the moment a character is deleted and another made with the same
    /// name - the first version of this keyed on the filename and refused a brand new character
    /// because an older one had shared its name. m_playerID comes from Utils.GenerateUID() at
    /// creation and is unique per character.
    /// </summary>
    internal static class Home
    {
        private static readonly Dictionary<long, long> _homes = new Dictionary<long, long>();
        private static FieldInfo _playerId;
        private static bool _loaded;

        private static string HomePath => Path.Combine(Paths.ConfigPath, "threshold-home.txt");

        /// <summary>
        /// Where this lived when it was part of Boon. Read once if the new file does not exist
        /// yet, so moving the feature between mods does not quietly unbind every character on
        /// a machine that already had bindings - which would hand everyone one free trip to
        /// another world, silently, exactly once.
        /// </summary>
        private static string LegacyPath => Path.Combine(Paths.ConfigPath, "boon-home.txt");

        /// <summary>The character's unique id, or 0 if it cannot be read.</summary>
        internal static long IdOf(PlayerProfile profile)
        {
            if (profile == null) return 0L;

            if (_playerId == null) _playerId = AccessTools.Field(typeof(PlayerProfile), "m_playerID");
            if (_playerId == null)
            {
                ThresholdPlugin.Log.LogError(
                    "PlayerProfile.m_playerID not found - character protection is off.");
                return 0L;
            }

            var value = _playerId.GetValue(profile);
            return value is long id ? id : 0L;
        }

        /// <summary>The world this character belongs to, or 0 if it has not been bound yet.</summary>
        internal static long Get(long playerId)
        {
            Load();

            if (playerId == 0L) return 0L;
            return _homes.TryGetValue(playerId, out var uid) ? uid : 0L;
        }

        /// <summary>
        /// Bind a character to a world the first time it is seen there. Never overwrites: a
        /// character already bound elsewhere has a problem to be told about, not one to paper
        /// over by re-pointing it at wherever it happens to be now.
        /// </summary>
        internal static void Bind(long playerId, string name, long worldUid)
        {
            Load();

            if (playerId == 0L || worldUid == 0L) return;

            if (_homes.TryGetValue(playerId, out var existing))
            {
                if (existing == worldUid) return;

                ThresholdPlugin.Log.LogWarning("Character '" + name + "' (" + playerId +
                    ") is bound to world " + existing + " but is in world " + worldUid +
                    ". Too late to stop it - that world is now written into the character.");
                return;
            }

            _homes[playerId] = worldUid;
            Save();

            ThresholdPlugin.Log.LogInfo(
                "Bound character '" + name + "' (" + playerId + ") to world " + worldUid + ".");
        }

        /// <summary>Forget a binding, so the character may be taken anywhere again.</summary>
        internal static void Forget(long playerId)
        {
            Load();
            if (_homes.Remove(playerId)) Save();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            var path = File.Exists(HomePath) ? HomePath
                     : File.Exists(LegacyPath) ? LegacyPath
                     : null;

            if (path == null) return;

            if (path == LegacyPath)
                ThresholdPlugin.Log.LogInfo(
                    "Adopting character bindings from Boon's " + LegacyPath +
                    "; they will be written to " + HomePath + " from now on.");

            foreach (var line in File.ReadAllLines(path))
            {
                var text = line.Trim();
                if (text.Length == 0 || text[0] == '#') continue;

                var bits = text.Split('|');
                if (bits.Length != 2) continue;

                // Lines from the first version were keyed by character name and are silently
                // dropped here - a name is not an identity, which is the bug this replaced.
                if (!long.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                if (!long.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid)) continue;

                _homes[id] = uid;
            }

            // Write the adopted set straight out, so the legacy file stops being consulted and
            // an old Boon left installed cannot start disagreeing with this one.
            if (path == LegacyPath) Save();
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Which world each character belongs to: playerId|worldUid",
                    "# Threshold refuses to start a character in any other world, because doing",
                    "# so permanently records that world in the character and locks it out of",
                    "# its own server. Delete a line to unbind that character.",
                };

                foreach (var kv in _homes) lines.Add(kv.Key + "|" + kv.Value);

                File.WriteAllLines(HomePath, lines.ToArray());
            }
            catch (Exception e)
            {
                ThresholdPlugin.Log.LogError("Could not write " + HomePath + ": " + e.Message);
            }
        }
    }
}
