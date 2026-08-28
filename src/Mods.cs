using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;

namespace Dyrr
{
    /// <summary>
    /// What a client is allowed to be running.
    ///
    /// This is the check that actually addresses cheating on a dedicated server, and the reason
    /// is a single line of vanilla: Console.IsCheatsEnabled returns ZNet.instance.IsServer().
    /// A client's own devcommands is therefore inert on somebody else's server - it flips a
    /// bool the gate then ignores - so anyone cheating there is necessarily running a mod that
    /// patched around it. "Did this character use cheats" is a question about the past; "what
    /// is this client running" is a question about right now, and it is the one worth asking.
    ///
    /// Two policies, because server owners want opposite things and neither is wrong.
    ///
    /// **Allow** is the closed door. The server's own plugins are always allowed - a client
    /// running exactly what the server runs is the normal case and should never need
    /// configuring - and anything else has to be named in AllowedMods. This is right for a
    /// server with a pinned modpack, and it is the default, because a list of things to permit
    /// is knowable in advance where a list of every cheat mod that will ever exist is not.
    ///
    /// **Deny** is the open door with a bouncer: everything is fine except what is named. Right
    /// for a server that does not care what people run as long as it is not that.
    ///
    /// Both are self-reported, like everything else here, and a purpose-built client can lie.
    /// What this catches is somebody who installed a cheat mod from Thunderstore and did not
    /// think about it, which is the ordinary case and the whole of what a house rule is for.
    /// </summary>
    internal static class Mods
    {
        internal const string Allow = "Allow";
        internal const string Deny = "Deny";

        internal const string Off = "Off";
        internal const string Notice = "Notice";
        internal const string Refuse = "Refuse";

        /// <summary>The file the allowlist actually lives in, beside the .cfg files.</summary>
        private const string ListFile = "dyrr-mods.txt";

        private static HashSet<string> _fromFile;
        private static DateTime _fileStamp;
        private static bool _fileMissingLogged;

        /// <summary>
        /// How hard to judge: Off, Notice or Refuse.
        ///
        /// The legacy true and false are still accepted because three profiles have them
        /// written to disk and BepInEx's saved value beats any new default - so a bind that
        /// stopped understanding them would silently turn the rule off on the live server,
        /// which is the one machine where nobody would notice until it mattered.
        /// </summary>
        internal static string Tier()
        {
            var raw = (DyrrConfig.RefuseMods.Value ?? "").Trim();

            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return Refuse;
            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return Off;

            if (raw.Equals(Off, StringComparison.OrdinalIgnoreCase)) return Off;
            if (raw.Equals(Notice, StringComparison.OrdinalIgnoreCase)) return Notice;
            if (raw.Equals(Refuse, StringComparison.OrdinalIgnoreCase)) return Refuse;

            // An unreadable setting must not quietly open the door. Refuse is what the entry
            // has always defaulted to, so a typo lands on the strict reading rather than the
            // permissive one, and the log says which word was not understood.
            DyrrPlugin.Log.LogWarning(
                "RefuseMods is set to '" + raw + "', which is not Off, Notice or Refuse. "
                + "Treating it as Refuse.");

            return Refuse;
        }

        /// <summary>
        /// Null when this client's plugins are acceptable, otherwise the reason they are not.
        /// </summary>
        internal static string Judge(List<string> reported)
        {
            if (reported == null) return null;

            return DyrrConfig.ModPolicy.Value.Trim().Equals(Deny, StringComparison.OrdinalIgnoreCase)
                ? Denied(reported)
                : NotAllowed(reported);
        }

        private static string Denied(List<string> reported)
        {
            var denied = Listed(DyrrConfig.DeniedMods.Value);
            if (denied.Count == 0) return null;

            var hits = new List<string>();

            foreach (var guid in reported)
                if (guid != null && denied.Contains(guid.Trim().ToLowerInvariant())) hits.Add(guid);

            return hits.Count == 0 ? null : "is running " + Name(hits) + ", which this server does not permit";
        }

        /// <summary>
        /// Everything a client may be running without comment: what this server runs, plus both
        /// places a host can name extras.
        /// </summary>
        internal static HashSet<string> Permitted()
        {
            var allowed = Listed(DyrrConfig.AllowedMods.Value);

            // The server's own plugins, always. A client running the same pack as the server is
            // the case this policy exists to wave through, and making an admin restate their
            // own mod list in a config entry would mean a server that refuses everybody the
            // day a mod is added to it - including the admin.
            foreach (var guid in Own()) allowed.Add(guid);

            foreach (var guid in FromFile()) allowed.Add(guid);

            return allowed;
        }

        /// <summary>Everything this client brought that is not permitted. Empty when it is fine.</summary>
        internal static List<string> Unexpected(List<string> reported)
        {
            var extra = new List<string>();
            if (reported == null) return extra;

            // Under Deny nothing is "permitted" in this sense - the host is filling in a list of
            // what to bar, so the useful report is everything the server does not itself run.
            var permitted = DyrrConfig.ModPolicy.Value.Trim().Equals(Deny, StringComparison.OrdinalIgnoreCase)
                ? Own()
                : Permitted();

            foreach (var guid in reported)
            {
                if (guid == null) continue;
                if (permitted.Contains(guid.Trim().ToLowerInvariant())) continue;

                extra.Add(guid);
            }

            return extra;
        }

        private static string NotAllowed(List<string> reported)
        {
            var extra = Unexpected(reported);

            return extra.Count == 0
                ? null
                : "is running " + Name(extra) + ", which this server does not run and has not allowed";
        }

        /// <summary>
        /// The allowlist held in a text file rather than a config entry, re-read whenever it
        /// changes on disk.
        ///
        /// It exists because BepInEx never reloads a .cfg on its own - ConfigFile.Reload is
        /// public and nothing calls it, and Core installs no watcher either - so permitting one
        /// friend's map mod through AllowedMods costs a server restart and drops everybody who
        /// is online. A house rule that expensive to relax stops being relaxed, and the door
        /// ends up either wide open or turning away friends.
        ///
        /// Cached on the file's last-write time. Every connection would otherwise re-read it,
        /// and a connection is not the moment to touch a disk more than once.
        /// </summary>
        private static HashSet<string> FromFile()
        {
            var path = Path.Combine(Paths.ConfigPath, ListFile);

            try
            {
                if (!File.Exists(path))
                {
                    // Written once, so the feature is discoverable. A host who never opens it is
                    // exactly where they were; a host looking for where to add a mod finds it
                    // next to the .cfg they are already editing.
                    if (!_fileMissingLogged)
                    {
                        _fileMissingLogged = true;
                        Seed(path);
                    }

                    _fromFile = new HashSet<string>();
                    return _fromFile;
                }

                var stamp = File.GetLastWriteTimeUtc(path);
                if (_fromFile != null && stamp == _fileStamp) return _fromFile;

                _fileStamp = stamp;
                _fromFile = new HashSet<string>();

                foreach (var line in File.ReadAllLines(path))
                {
                    if (line == null) continue;

                    // Everything after a # is a note. Commas too, because a host copying GUIDs
                    // out of AllowedMods will paste them comma-separated and be right to.
                    var text = line.Split('#')[0];

                    foreach (var raw in text.Split(',', ';'))
                    {
                        var guid = raw.Trim().ToLowerInvariant();
                        if (guid.Length > 0) _fromFile.Add(guid);
                    }
                }

                DyrrPlugin.Log.LogInfo(
                    ListFile + " read: " + _fromFile.Count + " extra plugin(s) permitted.");

                return _fromFile;
            }
            catch (Exception e)
            {
                // A list that cannot be read must not become a list that permits everything, and
                // must not become a refused connection either. It becomes an empty list and a
                // warning, which is the same answer as a host who has not written one.
                DyrrPlugin.Log.LogWarning("Could not read " + ListFile + ": " + e.Message);

                _fromFile = new HashSet<string>();
                return _fromFile;
            }
        }

        private static void Seed(string path)
        {
            try
            {
                File.WriteAllText(path,
                    "# Plugin GUIDs a client may run that this server does not.\r\n"
                    + "# One per line. Everything after a # is ignored. Case does not matter.\r\n"
                    + "#\r\n"
                    + "# Read fresh whenever this file changes, so adding a line here takes\r\n"
                    + "# effect on the next person to connect - no restart, nobody dropped.\r\n"
                    + "# The AllowedMods setting in ezomic.valheim.dyrr.cfg still works and is\r\n"
                    + "# added to this, but that one only takes effect when the server restarts.\r\n"
                    + "#\r\n"
                    + "# The plugins this server runs are always allowed and never need listing.\r\n"
                    + "#\r\n"
                    + "# Uncomment to let people keep the BepInEx config editor:\r\n"
                    + "# com.bepis.bepinex.configurationmanager\r\n");

                DyrrPlugin.Log.LogInfo("Wrote " + path + " - add client-only plugin GUIDs there.");
            }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not write " + ListFile + ": " + e.Message);
            }
        }

        /// <summary>Every plugin loaded on this machine, lowercased for comparison.</summary>
        internal static HashSet<string> Own()
        {
            var guids = new HashSet<string>();

            try
            {
                foreach (var plugin in Chainloader.PluginInfos)
                    if (plugin.Key != null) guids.Add(plugin.Key.Trim().ToLowerInvariant());
            }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not read this machine's plugin list: " + e.Message);
            }

            return guids;
        }

        /// <summary>
        /// A config list: GUIDs separated by commas, and by newlines too, because BepInEx cfg
        /// entries can be wrapped and somebody maintaining twenty of these will wrap them.
        /// </summary>
        private static HashSet<string> Listed(string text)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(text)) return set;

            foreach (var raw in text.Split(',', '\n', '\r', ';'))
            {
                var guid = raw.Trim().ToLowerInvariant();
                if (guid.Length > 0) set.Add(guid);
            }

            return set;
        }

        /// <summary>
        /// Name the mods, up to a point. The player being refused needs to know which one to
        /// remove, and a list of forty is not that - it is a wall of text on a screen that
        /// already only has room for a sentence.
        /// </summary>
        private static string Name(List<string> guids)
        {
            if (guids.Count == 1) return "'" + guids[0] + "'";

            var shown = Math.Min(guids.Count, 3);
            var text = "";

            for (var i = 0; i < shown; i++)
            {
                if (i > 0) text += ", ";
                text += "'" + guids[i] + "'";
            }

            if (guids.Count > shown) text += " and " + (guids.Count - shown) + " more";

            return guids.Count + " mods: " + text;
        }
    }
}
