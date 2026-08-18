using System;
using System.Collections.Generic;
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

        private static string NotAllowed(List<string> reported)
        {
            var allowed = Listed(DyrrConfig.AllowedMods.Value);

            // The server's own plugins, always. A client running the same pack as the server is
            // the case this policy exists to wave through, and making an admin restate their
            // own mod list in a config entry would mean a server that refuses everybody the
            // day a mod is added to it - including the admin.
            foreach (var guid in Own()) allowed.Add(guid);

            var extra = new List<string>();

            foreach (var guid in reported)
            {
                if (guid == null) continue;
                if (allowed.Contains(guid.Trim().ToLowerInvariant())) continue;

                extra.Add(guid);
            }

            return extra.Count == 0
                ? null
                : "is running " + Name(extra) + ", which this server does not run and has not allowed";
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
