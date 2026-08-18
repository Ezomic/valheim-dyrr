using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace Dyrr
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
    /// door then refuses that character on its own server forever. Dyrr is the only thing
    /// that will ever refuse it, so Dyrr is the right place to stop it happening.
    ///
    /// Keyed on the profile's **player id**, not its filename. The filename is the character's
    /// name, which is reused the moment a character is deleted and another made with the same
    /// name - the first version of this keyed on the filename and refused a brand new character
    /// because an older one had shared its name. m_playerID comes from Utils.GenerateUID() at
    /// creation and is unique per character.
    /// </summary>
    internal static class Home
    {
        /// <summary>
        /// A world, plus the names of both ends of the binding.
        ///
        /// The names are carried for the reader, not for the code: nothing is ever matched on
        /// them, and a line whose names are wrong or missing still binds. They exist because
        /// the only thing the file used to hold was a pair of 19-digit ids, and the one action
        /// this mod asks of a player - "delete the line starting with your character's id" -
        /// is a great deal harder when every line looks the same.
        /// </summary>
        internal struct Binding
        {
            internal long World;
            internal string Character;
            internal string WorldName;
        }

        private static readonly Dictionary<long, Binding> _homes = new Dictionary<long, Binding>();
        private static FieldInfo _playerId;
        private static bool _loaded;

        /// <summary>
        /// When the file we last read was written. Compared on every read so a hand edit made
        /// while the game is running is picked up - see Load.
        /// </summary>
        private static DateTime _stamp;

        private static string HomePath => Path.Combine(Paths.ConfigPath, "dyrr-home.txt");

        /// <summary>
        /// Every name this file has carried, newest first, read once if the current one does
        /// not exist yet.
        ///
        /// Without this, renaming quietly unbinds every character on a machine that already had
        /// bindings - which hands everyone one free trip to another world, silently, exactly
        /// once, and there is no undo for the trip. It is a chain rather than a single fallback
        /// because the file has now moved twice: out of Rist when this became its own mod, and
        /// again when that mod was renamed. A machine that skipped a release must not fall
        /// between the two.
        ///
        /// "boon-home.txt" is Rist's name from before that mod was renamed, so this chain is
        /// three names for two moves.
        /// </summary>
        private static readonly string[] LegacyNames = { "threshold-home.txt", "boon-home.txt" };

        /// <summary>The character's unique id, or 0 if it cannot be read.</summary>
        internal static long IdOf(PlayerProfile profile)
        {
            if (profile == null) return 0L;

            if (_playerId == null) _playerId = AccessTools.Field(typeof(PlayerProfile), "m_playerID");
            if (_playerId == null)
            {
                DyrrPlugin.Log.LogError(
                    "PlayerProfile.m_playerID not found - character protection is off.");
                return 0L;
            }

            var value = _playerId.GetValue(profile);
            return value is long id ? id : 0L;
        }

        /// <summary>The world this character belongs to, or 0 if it has not been bound yet.</summary>
        internal static long Get(long playerId)
        {
            return GetBinding(playerId).World;
        }

        /// <summary>The whole binding, names included. World is 0 when there is none.</summary>
        internal static Binding GetBinding(long playerId)
        {
            Load();

            if (playerId == 0L) return default(Binding);
            return _homes.TryGetValue(playerId, out var binding) ? binding : default(Binding);
        }

        /// <summary>Every binding on this machine, for the report.</summary>
        internal static IEnumerable<KeyValuePair<long, Binding>> All()
        {
            Load();
            return _homes;
        }

        /// <summary>
        /// Bind a character to a world the first time it is seen there. Never overwrites: a
        /// character already bound elsewhere has a problem to be told about, not one to paper
        /// over by re-pointing it at wherever it happens to be now.
        /// </summary>
        internal static void Bind(long playerId, string name, long worldUid, string worldName)
        {
            Load();

            if (playerId == 0L || worldUid == 0L) return;

            if (_homes.TryGetValue(playerId, out var existing))
            {
                if (existing.World == worldUid)
                {
                    // The ids already match, so nothing about the binding changes - but the
                    // names may be blank on a line adopted from the id-only format, and this
                    // is the one moment both are known. Filling them in costs one write, once.
                    var character = Clean(name);
                    var world = Clean(worldName);

                    // Only when there is something to add. Without this, a binding whose world
                    // cannot be named rewrites the whole file every time it is looked at.
                    var gained = (character.Length > 0 && existing.Character != character)
                        || (world.Length > 0 && existing.WorldName != world);
                    if (!gained) return;

                    if (character.Length > 0) existing.Character = character;
                    if (world.Length > 0) existing.WorldName = world;
                    _homes[playerId] = existing;
                    Save();
                    return;
                }

                DyrrPlugin.Log.LogWarning("Character '" + name + "' (" + playerId +
                    ") is bound to world " + Describe(existing) + " but is in world " +
                    worldName + " (" + worldUid + "). Too late to stop it - that world is now " +
                    "written into the character.");
                return;
            }

            _homes[playerId] = new Binding
            {
                World = worldUid,
                Character = Clean(name),
                WorldName = Clean(worldName),
            };
            Save();

            DyrrPlugin.Log.LogInfo("Bound character '" + name + "' (" + playerId +
                ") to world '" + worldName + "' (" + worldUid + ").");
        }

        /// <summary>
        /// Put a name on a binding that has only numbers, without touching which world it is.
        ///
        /// Bindings made before names were recorded, or made against a world this machine could
        /// not name at the time, are the reason this exists. The world is never changed here -
        /// that is Bind's job and Bind refuses to do it - so the worst this can be is a wrong
        /// label on a right binding, which is visible and correctable rather than silent.
        /// </summary>
        internal static void Name(long playerId, string worldName)
        {
            Load();

            var clean = Clean(worldName);
            if (clean.Length == 0) return;
            if (!_homes.TryGetValue(playerId, out var binding)) return;
            if (binding.WorldName == clean) return;

            binding.WorldName = clean;
            _homes[playerId] = binding;
            Save();
        }

        /// <summary>Forget a binding, so the character may be taken anywhere again.</summary>
        internal static bool Forget(long playerId)
        {
            Load();
            if (!_homes.Remove(playerId)) return false;

            Save();
            return true;
        }

        /// <summary>"name (uid)", or just the uid when the name was never recorded.</summary>
        internal static string Describe(Binding binding)
        {
            return string.IsNullOrEmpty(binding.WorldName)
                ? binding.World.ToString(CultureInfo.InvariantCulture)
                : "'" + binding.WorldName + "' (" + binding.World + ")";
        }

        /// <summary>
        /// Read the file if it has not been read, or re-read it if it has changed on disk since
        /// we last touched it.
        ///
        /// The re-read is not a nicety. This used to load exactly once per process and write
        /// the whole dictionary back on every Bind, so the documented fix for a wrong binding -
        /// "delete the line starting with your id" - was silently undone by the next character
        /// that bound, because that Save wrote out an in-memory set still holding the line the
        /// player had just deleted. The file the popup tells you to edit has to be the file
        /// that is actually read.
        ///
        /// Disk wins outright rather than merging. Every binding is written the moment it is
        /// made, so anything only in memory is either already on disk or was removed there on
        /// purpose, and a merge would resurrect exactly the line somebody meant to delete.
        /// </summary>
        private static void Load()
        {
            if (_loaded)
            {
                if (!File.Exists(HomePath)) return;

                DateTime now;
                try { now = File.GetLastWriteTimeUtc(HomePath); }
                catch (Exception) { return; }

                if (now == _stamp) return;

                _homes.Clear();
                Parse(HomePath);
                _stamp = now;

                DyrrPlugin.Log.LogInfo("Re-read " + HomePath + " after it changed on disk; " +
                    _homes.Count + " character(s) bound.");
                return;
            }

            _loaded = true;

            var path = File.Exists(HomePath) ? HomePath : null;

            if (path == null)
                foreach (var name in LegacyNames)
                {
                    var legacy = Path.Combine(Paths.ConfigPath, name);
                    if (!File.Exists(legacy)) continue;

                    path = legacy;
                    break;
                }

            if (path == null) return;

            var adopted = path != HomePath;

            if (adopted)
                DyrrPlugin.Log.LogInfo(
                    "Adopting character bindings from " + path +
                    "; they will be written to " + HomePath + " from now on.");

            Parse(path);

            // Write the adopted set straight out, so the legacy file stops being consulted and
            // an older build left installed cannot start disagreeing with this one.
            if (adopted) Save();
            else Stamp();
        }

        private static void Parse(string path)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogError("Could not read " + path + ": " + e.Message);
                return;
            }

            foreach (var line in lines)
            {
                var text = line.Trim();
                if (text.Length == 0 || text[0] == '#') continue;

                // Two fields or four: the ids are the binding and the names are for the
                // reader. Splitting with no limit and taking what is there means a file
                // written by the id-only version still loads, and a file written by this one
                // still loads on a build that only knows two fields.
                var bits = text.Split('|');
                if (bits.Length < 2) continue;

                // Lines from the first version were keyed by character name and are silently
                // dropped here - a name is not an identity, which is the bug this replaced.
                if (!long.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                if (!long.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid)) continue;

                _homes[id] = new Binding
                {
                    World = uid,
                    Character = bits.Length > 2 ? bits[2].Trim() : "",
                    WorldName = bits.Length > 3 ? bits[3].Trim() : "",
                };
            }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Which world each character belongs to.",
                    "#",
                    "#   playerId|worldUid|character|world",
                    "#",
                    "# Dyrr refuses to start a character in any other world, because doing",
                    "# so permanently records that world in the character and locks it out of",
                    "# its own server. Delete a line to unbind that character; the game may be",
                    "# running, the file is re-read when it changes.",
                    "#",
                    "# Only the two numbers are read. The names are here so the line you want",
                    "# is the one you can recognise.",
                };

                foreach (var kv in _homes)
                    lines.Add(kv.Key + "|" + kv.Value.World + "|" + kv.Value.Character + "|" + kv.Value.WorldName);

                File.WriteAllLines(HomePath, lines.ToArray());
                Stamp();
            }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogError("Could not write " + HomePath + ": " + e.Message);
            }
        }

        /// <summary>
        /// Remember the file as we left it, so our own write does not read back as somebody
        /// else's edit and trigger a pointless re-read on the next lookup.
        /// </summary>
        private static void Stamp()
        {
            try { _stamp = File.GetLastWriteTimeUtc(HomePath); }
            catch (Exception) { _stamp = default(DateTime); }
        }

        /// <summary>
        /// The separator and the line ending are the only two characters that could turn one
        /// binding into two, and a world name is whatever somebody typed into the menu.
        /// </summary>
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
