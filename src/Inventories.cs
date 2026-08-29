using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dyrr
{
    /// <summary>
    /// Notices when a character comes back carrying something it did not leave with.
    ///
    /// **Why this cannot be done on the server alone, stated once.** A player's inventory is
    /// not on the server and never crosses the wire. PlayerProfile.SavePlayerData writes it
    /// into m_playerData and Save() puts that on the CLIENT's disk; the world knows a
    /// character's position and its equipped visuals and nothing else about what it holds.
    /// That is exactly the hole ServerCharacters fills by moving the whole profile server
    /// side, and it is a far larger change than this.
    ///
    /// So the inventory is self-reported, like everything else in Facts, and carries the same
    /// honest limit: a client built to lie can lie. What it catches is the ordinary case, and
    /// the ordinary case is the one that happens - somebody closes the game, edits the .fch,
    /// and comes back richer. The mod they would additionally have to patch to hide it is
    /// this one, and Dyrr already reports its own plugin list for exactly that reason.
    ///
    /// **Why a snapshot stream rather than one report at login.** "Different from last login"
    /// is not the question worth asking: an inventory changes constantly during honest play,
    /// so comparing two logins would fire every time. What matters is whether it changed
    /// WHILE AWAY. That needs a last-known state from the previous session, so the client
    /// sends a snapshot shortly after spawning and then every Interval seconds. The server
    /// keeps the most recent one and compares the first of the next session against it.
    ///
    /// **The false positive that is real, and not hidden.** A client that crashes stops
    /// sending, so the stored snapshot can be up to one interval stale, and Valheim may itself
    /// roll a character back to its own last local save. Both show up here as a change. The
    /// wording says "changed while away" rather than accusing anybody, and the line names what
    /// moved so a human can tell forty iron from one arrow.
    /// </summary>
    internal static class Inventories
    {
        internal const string Rpc = "Dyrr_Inv";

        /// <summary>The marker Crier matches. Reword only together with its config.</summary>
        private const string Marker = "Inventory changed while away:";

        private const string FileName = "dyrr-inventories.txt";

        // ------------------------------------------------------------------ server side

        /// <summary>Last snapshot seen for a character, by its player id.</summary>
        private static readonly Dictionary<long, string> Known = new Dictionary<long, string>();

        /// <summary>
        /// Connections that have already reported once.
        ///
        /// Keyed by ZRpc rather than by player id, which is the same identity Doorman uses
        /// and for the same reason: a connection IS a session. Keyed by id instead, a player
        /// who disconnects and rejoins while the server stays up would still be marked as
        /// greeted, and the one comparison worth making - what they came back with - would
        /// be skipped for the rest of the server's life. Doorman drops its entry on
        /// ZNet.Disconnect and this rides along there.
        /// </summary>
        private static readonly HashSet<ZRpc> Greeted = new HashSet<ZRpc>();

        private static bool _loaded;

        /// <summary>
        /// A snapshot arriving from one client.
        ///
        /// The first one of a session is the interesting one: it is what the character walked
        /// back in with. Every later one only refreshes what to compare against next time.
        /// </summary>
        internal static void Receive(ZRpc rpc, ZPackage pkg)
        {
            if (!DyrrConfig.WatchInventories.Value) return;

            var net = ZNet.instance;
            if (net == null || !net.IsServer()) return;

            long id;
            string name, snapshot;
            try
            {
                id = pkg.ReadLong();
                name = pkg.ReadString();
                snapshot = pkg.ReadString();
            }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not read an inventory report: " + e.Message);
                return;
            }

            if (id == 0L) return;

            Load();

            string before;
            var known = Known.TryGetValue(id, out before);

            if (Greeted.Add(rpc) && known && before != snapshot)
            {
                var moved = Difference(before, snapshot);

                // Nothing worth naming means the two strings differed in a way the diff
                // does not consider a change - report nothing rather than an empty alarm.
                if (moved.Length > 0)
                    DyrrPlugin.Log.LogWarning(Marker + " " + Display(name, id) + ", " + moved);
            }

            Known[id] = snapshot;
            Save();
        }

        /// <summary>The connection is gone; its next one is a fresh session.</summary>
        internal static void Forget(ZRpc rpc)
        {
            Greeted.Remove(rpc);
        }

        /// <summary>
        /// What moved, most significant first, and never the whole inventory.
        ///
        /// A line naming sixty items is a line nobody reads. The counts are what tell an
        /// honest crash apart from a helping hand, so they are always shown.
        /// </summary>
        private static string Difference(string before, string after)
        {
            var was = Parse(before);
            var now = Parse(after);

            var deltas = new List<KeyValuePair<string, int>>();

            foreach (var pair in now)
            {
                int had;
                was.TryGetValue(pair.Key, out had);
                if (pair.Value != had) deltas.Add(
                    new KeyValuePair<string, int>(pair.Key, pair.Value - had));
            }

            foreach (var pair in was)
            {
                if (now.ContainsKey(pair.Key)) continue;
                deltas.Add(new KeyValuePair<string, int>(pair.Key, -pair.Value));
            }

            if (deltas.Count == 0) return "";

            // Biggest movement first, gains before losses at equal size: somebody granting
            // themselves iron is the thing being looked for, and it should lead.
            deltas.Sort((a, b) =>
            {
                var bySize = Mathf.Abs(b.Value).CompareTo(Mathf.Abs(a.Value));
                return bySize != 0 ? bySize : b.Value.CompareTo(a.Value);
            });

            var cap = Mathf.Max(1, DyrrConfig.InventoryDetail.Value);
            var sb = new StringBuilder();

            for (var i = 0; i < deltas.Count && i < cap; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(deltas[i].Value > 0 ? "+" : "").Append(deltas[i].Value)
                  .Append(' ').Append(deltas[i].Key);
            }

            if (deltas.Count > cap)
                sb.Append(" and ").Append(deltas.Count - cap).Append(" more");

            return sb.ToString();
        }

        /// <summary>"name:count,name:count" back into a tally.</summary>
        private static Dictionary<string, int> Parse(string snapshot)
        {
            var tally = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(snapshot)) return tally;

            foreach (var entry in snapshot.Split(','))
            {
                var cut = entry.LastIndexOf(':');
                if (cut <= 0) continue;

                int count;
                if (!int.TryParse(entry.Substring(cut + 1), out count)) continue;

                var key = entry.Substring(0, cut);
                int running;
                tally.TryGetValue(key, out running);
                tally[key] = running + count;
            }

            return tally;
        }

        private static string Display(string name, long id)
        {
            var who = string.IsNullOrEmpty(name) ? "someone" : name;
            return DyrrConfig.InventoryNamesIds.Value ? who + " (" + id + ")" : who;
        }

        // ----------------------------------------------------------------- persistence

        private static string Path()
        {
            return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, FileName);
        }

        /// <summary>
        /// Read once. Kept beside the config as plain text on purpose: it is a record a
        /// person may want to look at or clear, and a binary blob for a list of item counts
        /// would be a worse answer to every question anybody asks of it.
        /// </summary>
        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                var path = Path();
                if (!File.Exists(path)) return;

                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split(new[] { '|' }, 3);
                    if (parts.Length < 3) continue;

                    long id;
                    if (!long.TryParse(parts[0], out id)) continue;

                    Known[id] = parts[2];
                }
            }
            catch (Exception e)
            {
                // A record that cannot be read costs the comparison, not the connection.
                DyrrPlugin.Log.LogWarning("Could not read " + FileName + ": " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var pair in Known)
                    sb.Append(pair.Key).Append('|').Append('|').Append(pair.Value).Append('\n');

                File.WriteAllText(Path(), sb.ToString());
            }
            catch (Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not write " + FileName + ": " + e.Message);
            }
        }

        // ------------------------------------------------------------------ client side

        private static float _next;

        /// <summary>
        /// Send a snapshot, on a timer. Called every frame from the plugin's Update and
        /// does nothing at all on a server or before a character exists.
        /// </summary>
        internal static void Tick()
        {
            if (!DyrrConfig.WatchInventories.Value) return;

            var net = ZNet.instance;
            if (net == null || net.IsServer()) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup
                  + Mathf.Max(5f, DyrrConfig.InventoryInterval.Value);

            Send(player);
        }

        /// <summary>Reset the timer so the next Tick reports at once - used on spawn.</summary>
        internal static void ReportSoon()
        {
            _next = 0f;
        }

        private static void Send(Player player)
        {
            var server = ZNet.instance != null ? ZNet.instance.GetServerRPC() : null;
            if (server == null) return;

            var pkg = new ZPackage();
            pkg.Write(player.GetPlayerID());
            pkg.Write(player.GetPlayerName() ?? "");
            pkg.Write(Snapshot(player));

            server.Invoke(Rpc, pkg);
        }

        /// <summary>
        /// The inventory as a sorted tally of what is in it.
        ///
        /// Sorted and summed rather than listed slot by slot, because moving a stack from one
        /// square to another is not a change anybody wants reported. Quality rides along in
        /// the key: a level 1 and a level 4 sword are different objects, and upgrading one
        /// while offline is not possible.
        /// </summary>
        private static string Snapshot(Player player)
        {
            var inventory = player.GetInventory();
            if (inventory == null) return "";

            var tally = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var item in inventory.GetAllItems())
            {
                if (item == null || item.m_shared == null) continue;

                var key = item.m_shared.m_name;
                if (item.m_quality > 1) key += "*" + item.m_quality;

                int running;
                tally.TryGetValue(key, out running);
                tally[key] = running + item.m_stack;
            }

            var keys = new List<string>(tally.Keys);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            foreach (var key in keys)
            {
                if (sb.Length > 0) sb.Append(',');

                // The separator is also the parser's split, so a name carrying one would
                // reappear as two items. Vanilla names are localisation tokens with neither,
                // but a modded item is not this mod's to vouch for.
                sb.Append(key.Replace(",", " ").Replace(":", " "))
                  .Append(':').Append(tally[key]);
            }

            return sb.ToString();
        }
    }
}
