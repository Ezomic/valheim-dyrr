using BepInEx;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;

namespace Threshold
{
    /// <summary>
    /// Threshold. A door policy: characters that have played elsewhere do not come in.
    ///
    /// This exists because it was living inside Boon, where it did not belong. Boon awards
    /// levels for skill gains, so it wanted to know whether a character's skills were earned
    /// here - and answered that by refusing the connection outright. A levelling mod deciding
    /// who may play is the wrong power in the wrong place: the failure mode is that a bug in
    /// an XP system locks people out of a server, and when it fired the player got Valheim's
    /// generic kick screen with the reason only in a log they had no access to.
    ///
    /// Split along what each half is actually for. Boon keeps the question it has standing to
    /// ask - "do I pay for these levels?" - and answers it by withholding XP, never by
    /// disconnecting anyone. The question of who is allowed through the door is a server
    /// policy, has nothing to do with levelling, and is this.
    ///
    /// There is no BepInProcess attribute. A dedicated server runs valheim_server.exe, and
    /// refusing a connection only happens on the server side of RPC_PeerInfo, so this has to
    /// load there to do anything at all.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("ezomic.valheim.core", BepInDependency.DependencyFlags.HardDependency)]
    public class ThresholdPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.threshold";
        public const string PluginName = "Threshold";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ThresholdConfig.Bind(Config);

            // Everyone, not HostOnly, even though only the server decides. The facts being
            // judged live on the client and have to be reported, so a client without this
            // plugin answers nothing - and a door that cannot ask its question is not a door.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config, Requirement.Everyone);

            // The host's rules are the rules. A client deciding locally that it does not
            // refuse itself would be meaningless anyway, but syncing keeps the log on both
            // ends agreeing about what was applied.
            Suite.Sync(
                ThresholdConfig.Enabled,
                ThresholdConfig.Enforce,
                ThresholdConfig.RefuseOtherWorlds,
                ThresholdConfig.RefuseCheats,
                ThresholdConfig.RefuseUnreported);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Doorman));
            _harmony.PatchAll(typeof(MenuGuard));

            // This class carries a patch of its own - the one that forgets the binding when a
            // world is left - so it has to be handed to Harmony like the others.
            _harmony.PatchAll(typeof(ThresholdPlugin));

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        /// <summary>
        /// Remember which world this character belongs to, the first time it is seen in one.
        /// After that the menu refuses to start it anywhere else, which is the only point at
        /// which that can still be prevented.
        ///
        /// Polled rather than hooked because there is no single moment when both ZNet and the
        /// player profile exist and the world UID is known - the same shape the prefab
        /// registration recipes use. It costs two null checks a frame until it takes, and
        /// nothing afterwards.
        /// </summary>
        private void Update()
        {
            if (_bound || !ThresholdConfig.ProtectCharacter.Value) return;
            if (ZNet.instance == null || Game.instance == null) return;

            var uid = ZNet.instance.GetWorldUID();
            if (uid == 0L) return;

            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return;

            _bound = true;
            Home.Bind(Home.IdOf(profile), profile.GetName(), uid);
        }

        /// <summary>Reset when a world is left, so the next one binds its own character.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        private static void OnWorldLeft()
        {
            _bound = false;
        }

        private static bool _bound;

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
