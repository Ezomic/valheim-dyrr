using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Dyrr
{
    /// <summary>
    /// The door works in both directions: a player who has stopped playing is shown out.
    ///
    /// An AFK body on a dedicated server is not neutral. It holds a slot, it keeps its
    /// zones loaded and simulated, it anchors sleep - the night cannot be skipped while
    /// one player lies in a bed and another stands parked in a corner - and on a small
    /// server it reads as presence that is not there. So after a configurable stretch of
    /// genuine stillness the server kicks, with a warning first so a player reading a map
    /// or alt-tabbed for a minute can twitch the mouse and stay.
    ///
    /// **What counts as moving.** Position OR facing, read off the character's ZDO on the
    /// server. Facing matters because a player standing at a chest sorting inventory does
    /// not translate, but nobody plays this game for five minutes without once moving
    /// the camera - and the camera is the character's yaw. The thresholds are small and
    /// the sweep is coarse (every five seconds), so this costs nothing measurable.
    ///
    /// **Why the server and not a client mod.** Only the server can enforce, and the
    /// whole point of Dyrr is that the door does not rely on the guest's cooperation.
    /// The kick itself is vanilla's own - the same path the console kick takes - so the
    /// client sees the ordinary "kicked" screen, and Crier reports the departure to
    /// Discord the same way it reports any other.
    /// </summary>
    internal static class Idle
    {
        private sealed class Watch
        {
            public Vector3 Pos;
            public Quaternion Rot;
            public float LastMotion;
            public bool Warned;
        }

        private static readonly Dictionary<long, Watch> Watches = new Dictionary<long, Watch>();
        private static readonly List<long> Stale = new List<long>();
        private static float _nextSweep;

        /// <summary>
        /// Called every frame from the plugin's Update; does real work every five seconds.
        /// </summary>
        public static void Tick()
        {
            if (!DyrrConfig.KickIdle.Value) return;

            var net = ZNet.instance;

            // Dedicated only. On a locally hosted world the host's own machine is the
            // server, and a host who walks away from their own game is not a policy
            // question this mod should answer.
            if (net == null || !net.IsServer() || !net.IsDedicated()) return;

            if (Time.time < _nextSweep) return;
            _nextSweep = Time.time + 5f;

            var peers = net.GetPeers();

            // Forget the departed, or a rejoining player inherits the old clock.
            if (Watches.Count > 0)
            {
                Stale.Clear();
                foreach (var uid in Watches.Keys)
                {
                    var present = false;
                    foreach (var p in peers)
                        if (p != null && p.m_uid == uid) { present = true; break; }
                    if (!present) Stale.Add(uid);
                }
                foreach (var uid in Stale) Watches.Remove(uid);
            }

            var limit = Mathf.Max(1, DyrrConfig.IdleMinutes.Value) * 60f;
            var warnAt = limit - Mathf.Max(0, DyrrConfig.IdleWarnMinutes.Value) * 60f;

            foreach (var peer in peers)
            {
                if (peer == null || !peer.IsReady()) continue;

                // No spawned character - character select, or a corpse-run loading
                // screen. The clock only runs against a body standing in the world.
                if (peer.m_characterID.IsNone()) continue;

                var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo == null) continue;

                var pos = zdo.GetPosition();
                var rot = zdo.GetRotation();

                Watch watch;
                if (!Watches.TryGetValue(peer.m_uid, out watch))
                {
                    Watches[peer.m_uid] = new Watch
                        { Pos = pos, Rot = rot, LastMotion = Time.time };
                    continue;
                }

                // 5cm or one degree. Walking, fighting, turning the camera, even sorting
                // a chest all clear these easily; only a hands-off body does not.
                var moved = (pos - watch.Pos).sqrMagnitude > 0.0025f
                            || Quaternion.Angle(rot, watch.Rot) > 1f;

                watch.Pos = pos;
                watch.Rot = rot;

                if (moved)
                {
                    watch.LastMotion = Time.time;
                    watch.Warned = false;
                    continue;
                }

                var idle = Time.time - watch.LastMotion;

                if (!watch.Warned && warnAt > 0f && idle >= warnAt && idle < limit)
                {
                    watch.Warned = true;
                    Warn(peer, Mathf.Max(1, Mathf.CeilToInt((limit - idle) / 60f)));
                    continue;
                }

                if (idle < limit) continue;

                // Three audiences, three sentences. The player gets a personal reason on
                // their disconnect screen, through the same Dyrr_Refused channel a join
                // refusal uses - the handler is registered on every connection and Core
                // appends it to the screen vanilla leaves blank. The log gets a line
                // whose exact wording is load-bearing: Crier string-matches it to post
                // the departure to Discord, so reword it only together with KickMatch
                // in Crier's config.
                if (peer.m_rpc != null)
                    peer.m_rpc.Invoke(Doorman.RpcRefused,
                        "You were still for " + DyrrConfig.IdleMinutes.Value
                        + " minutes, so the server freed your seat. Rejoin whenever "
                        + "you like.");

                DyrrPlugin.Log.LogWarning("Kicked while away: " + peer.m_playerName
                    + ", still for " + DyrrConfig.IdleMinutes.Value + " minutes.");
                Watches.Remove(peer.m_uid);
                Kick(peer);
            }
        }

        /// <summary>
        /// The warning, as a HUD message to the one player.
        ///
        /// This used to be a routed "ChatMessage" carrying an invented sender,
        /// <c>new PlatformUserID("Server")</c>, on the reasoning that a shout needs no
        /// client-side support. **It was never shown to anybody**, and it failed silently,
        /// which is why it survived a release: the server sent it, Crier read it off the
        /// router and posted it to the site, and every client threw it away. So the one
        /// place the warning existed was the one place the player was not looking.
        ///
        /// Two gates, either of them fatal, both in vanilla:
        ///
        /// - `PlatformUserID(string)` wants `Platform_userid`, the `Steam_7656...` form.
        ///   "Server" has no platform part, so it fails to parse, `m_userID` is left null
        ///   and `IsValid` is false. `Chat.OnNewChatMessage` hands the sender to
        ///   `RelationsManager.CheckPermissionAsync` unless it is the local player, and
        ///   that method opens with `if (!user.IsValid)` and completes with `Error`.
        ///   `Error.IsGranted()` is false, so neither `AddString` nor the in-world text
        ///   ever runs.
        /// - A valid id would not have saved it either. `Terminal.AddString` looks the
        ///   sender up with `ZNet.TryGetPlayerByPlatformUserID` and draws the *connected
        ///   peer's* name, and a server has no entry in that list and never will. Crier
        ///   spent two versions on that identity before concluding the identity was the
        ///   wrong thing to fix; `crier/src/Announce.cs` carries the write-up.
        ///
        /// So the warning goes the way Crier's own notices go: MessageHud registers the
        /// routed RPC "ShowMessage" taking only (type, text), and its handler calls
        /// ShowMessage straight through - no sender, no permission check, no lookup to
        /// fail. Center rather than Crier's TopLeft, and that is the one difference worth
        /// arguing: TopLeft is the branch that also writes the message log, which suits a
        /// notice you might have missed, while Center is the big crossfade the game saves
        /// for what you must not miss. Two minutes from being disconnected is that.
        ///
        /// One trap inherited from the vanilla channel: ShowMessage runs its text through
        /// Localization.Localize, so a line containing a $token would be translated rather
        /// than printed. This one has none, and should not grow one.
        /// </summary>
        private static void Warn(ZNetPeer peer, int minutesLeft)
        {
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage",
                    (int)MessageHud.MessageType.Center,
                    "You seem to be away - move within " + minutesLeft
                    + " minute" + (minutesLeft == 1 ? "" : "s") + " or you will be kicked.");

                // Logged as well as shown, because the warning used to reach the site as a
                // chat line and now correctly does not: it is a server event, not something
                // anybody said. Crier's log relay carries this to the site instead. The
                // wording deliberately avoids "Kicked while away:", which Crier string
                // matches to post a departure - a warning is not a departure.
                DyrrPlugin.Log.LogInfo("Warned while away: " + peer.m_playerName
                    + ", " + minutesLeft + " minute" + (minutesLeft == 1 ? "" : "s")
                    + " left.");
            }
            catch (System.Exception e)
            {
                // A failed warning must never block the kick that follows it.
                DyrrPlugin.Log.LogWarning("Could not warn " + peer.m_playerName
                    + " about idling: " + e.Message);
            }
        }

        /// <summary>Vanilla's own kick, which is private; the console command's path.</summary>
        private static void Kick(ZNetPeer peer)
        {
            try
            {
                AccessTools.Method(typeof(ZNet), "InternalKick", new[] { typeof(ZNetPeer) })
                    .Invoke(ZNet.instance, new object[] { peer });
            }
            catch (System.Exception e)
            {
                DyrrPlugin.Log.LogError("Could not kick " + peer.m_playerName + ": " + e.Message);
            }
        }
    }
}
